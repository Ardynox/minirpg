using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Render.Surface;

/// <summary>
/// 真·3D shader 草叶批次渲染节点（Phase 1，仅供 Preview 使用）。
///
/// 架构：
///   一个 Node2D，内部挂一个 <see cref="MultiMeshInstance2D"/>（+ 一个 <see cref="ShaderMaterial"/> 实例）。
///   上游每帧重建时调用 <see cref="BeginFill"/> → 多次 <see cref="AddBlade"/> → <see cref="EndFill"/>，
///   本节点把 blade instance 数组 pack 进 <see cref="MultiMesh"/>，走 1 次 draw call 绘完整批草。
///
/// 单 blade 数据：
///   屏幕位置 (screen_pos) 打进 Transform2D.origin（MultiMesh per-instance transform）。
///   宽/高像素 打进 instance COLOR（MultiMesh 开 UseColors = true）。
///   风相位 / 倾角 / 色板索引 / 色相扰动 打进 INSTANCE_CUSTOM（SetInstanceCustomData）。
///
/// 运行时：
///   每帧 <see cref="UpdateWindUniforms"/> 喂 wind/sun；TIME 由 Godot 内建。
///
/// Phase 1 不接入 chunk 系统，只做 preview 级单节点管理；稳定后再搬 chunk 级。
/// </summary>
public sealed partial class GrassBladeField : Node2D
{
	/// <summary>单根 blade 的打包数据。</summary>
	public readonly record struct BladeInstance(
		Vector2 ScreenPos,
		float   WidthPx,
		float   HeightPx,
		float   TiltRad,
		float   WindPhase,
		int     PaletteIdx,
		float   HueJitter);

	private MultiMeshInstance2D? _mmi;
	private MultiMesh? _multiMesh;
	private ShaderMaterial? _material;

	// 根部 AO decal：独立 MultiMeshInstance2D，z_index 低于 blade 一层 → 绘制顺序 dirt < AO < blade
	private MultiMeshInstance2D? _aoMmi;
	private MultiMesh? _aoMultiMesh;
	private ShaderMaterial? _aoMaterial;

	private readonly List<BladeInstance> _pending = new(capacity: 4096);

	/// <summary>是否启用根部 AO decal 层（默认启用）。</summary>
	public bool AoEnabled { get; set; } = true;

	/// <summary>默认 8 档 blade 色板，配合 6 个 variant 做可视差异。</summary>
	private static readonly Color[] DefaultPalette =
	[
		new(0.42f, 0.62f, 0.24f, 1f),   // 0 春嫩草·主
		new(0.58f, 0.58f, 0.26f, 1f),   // 1 春嫩草·枯黄
		new(0.30f, 0.58f, 0.22f, 1f),   // 2 仲夏草·主
		new(0.22f, 0.48f, 0.18f, 1f),   // 3 仲夏草·深
		new(0.24f, 0.50f, 0.22f, 1f),   // 4 深绿密草
		new(0.46f, 0.55f, 0.22f, 1f),   // 5 黄绿野草
		new(0.28f, 0.45f, 0.26f, 1f),   // 6 苔绿
		new(0.55f, 0.45f, 0.18f, 1f),   // 7 枯苔/秋色
	];

	public override void _Ready()
	{
		_material = new ShaderMaterial
		{
			Shader = ResourceLoader.Load<Shader>("res://Assets/Shaders/procedural_grass_blade.gdshader"),
		};
		ApplyDefaultPalette(_material);
		UpdateWindUniforms(windSpeed: 1.8f, windAmplitude: 2.2f, windDir: new Vector2(1f, 0.15f));
		UpdateSunUniforms(sunDir: new Vector2(0.35f, -0.94f), sunColor: new Color(1f, 0.96f, 0.86f), ambient: 0.55f);
		// Shadertoy Xsf3zX 风格扩展默认值：双轴风 + 极少数 blade 草尖漂白
		UpdateCrossWindUniforms(crossWindGain: 0.35f, crossWindRatio: 2.1f);
		UpdateTipBleachUniforms(
			threshold: 0.32f,
			strength:  0.75f,
			color:     new Color(0.92f, 0.90f, 0.62f));
		UpdateTaperUniforms(tipWidthRatio: 0.15f, taperCurve: 0.55f);
		UpdateToneUniforms(rootDarken: 0.55f, tipBoost: 1.25f);

		_multiMesh = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
			UseColors = true,
			UseCustomData = true,
			Mesh = BuildBladeQuad(),
			InstanceCount = 0,
		};

		_mmi = new MultiMeshInstance2D
		{
			Name = "BladeMultiMesh",
			Multimesh = _multiMesh,
			Material = _material,
			ZIndex = 1,
		};
		AddChild(_mmi);

		// ── AO decal 子层：独立 MultiMesh + shader，z_index = 0 压在 blade 下一层 ──
		_aoMaterial = new ShaderMaterial
		{
			Shader = ResourceLoader.Load<Shader>("res://Assets/Shaders/procedural_grass_ao_decal.gdshader"),
		};
		_aoMaterial.SetShaderParameter("softness", 0.55f);

		_aoMultiMesh = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
			UseColors = true,
			UseCustomData = true,
			Mesh = BuildCenteredQuad(),
			InstanceCount = 0,
		};

		_aoMmi = new MultiMeshInstance2D
		{
			Name = "AoMultiMesh",
			Multimesh = _aoMultiMesh,
			Material = _aoMaterial,
			ZIndex = 0,
		};
		AddChild(_aoMmi);
	}

	/// <summary>
	/// 清空上一帧的 blade 列表。
	/// </summary>
	public void BeginFill() => _pending.Clear();

	/// <summary>
	/// 追加一根 blade 到本次 flush 队列。<paramref name="paletteIdx"/> 会被 clamp 到 0..7。
	/// </summary>
	public void AddBlade(in BladeInstance blade)
	{
		if (blade.HeightPx <= 0f || blade.WidthPx <= 0f)
			return;
		_pending.Add(blade);
	}

	/// <summary>
	/// 每个 2D instance 在 MultiMesh.Buffer 里占 16 个 float（Godot 4 规范）：
	///   [0..7]  Transform2D（两行 vec4，middle slot 恒 0）
	///   [8..11] Color：r=blade_width_px, g=blade_height_px, b=1, a=1（shader 里走 COLOR.r / .g）
	///   [12..15] CustomData：x=phase, y=tilt, z=paletteIdx, w=hueJitter（对应 INSTANCE_CUSTOM）
	/// </summary>
	private const int InstanceStride = 16;

	/// <summary>
	/// 把 <see cref="_pending"/> 打包进 MultiMesh；本次 flush 后开始在画面上显示这批 blade。
	/// 若 <see cref="AoEnabled"/> 为 true，同时生成同数量的 AO decal 实例。
	///
	/// 性能说明：
	///   原实现对每根 blade 调用 3× SetInstance* → 每次都是托管/原生边界调用。
	///   50 万 blade = 150 万次 interop，CPU 数百毫秒，完全卡死。
	///   改用 <see cref="MultiMesh.Buffer"/> 一次性上传整条 PackedFloat32Array：1 次 interop + 1 次内存拷贝。
	///   实测 100 万 blade rebuild 从 ~秒级 → ~20ms 级。
	/// </summary>
	public void EndFill()
	{
		if (_multiMesh == null)
			return;

		var count = _pending.Count;
		_multiMesh.InstanceCount = count;

		var aoCount = AoEnabled ? count : 0;
		if (_aoMultiMesh != null)
			_aoMultiMesh.InstanceCount = aoCount;

		if (count == 0)
			return;

		// Buffer 大小必须恰好 = InstanceCount * stride（Godot 源码里有 ERR_FAIL_COND）。
		// rebuild 不是每帧做（仅 dirty 时），所以这里直接分配新数组，不做复用。
		var bladeBuf = new float[count * InstanceStride];
		var aoBuf    = aoCount > 0 ? new float[aoCount * InstanceStride] : null;

		for (var i = 0; i < count; i++)
		{
			var b = _pending[i];
			var off = i * InstanceStride;
			var palClamped = Math.Clamp(b.PaletteIdx, 0, 7);

			// Transform2D 身份 + 平移到 ScreenPos（没有旋转/缩放，几何都在 shader 里做）
			bladeBuf[off + 0] = 1f;
			bladeBuf[off + 1] = 0f;
			bladeBuf[off + 2] = 0f;
			bladeBuf[off + 3] = b.ScreenPos.X;
			bladeBuf[off + 4] = 0f;
			bladeBuf[off + 5] = 1f;
			bladeBuf[off + 6] = 0f;
			bladeBuf[off + 7] = b.ScreenPos.Y;
			// Color（shader 里 COLOR.r = width、COLOR.g = height）
			bladeBuf[off + 8]  = b.WidthPx;
			bladeBuf[off + 9]  = b.HeightPx;
			bladeBuf[off + 10] = 1f;
			bladeBuf[off + 11] = 1f;
			// CustomData（shader 里 INSTANCE_CUSTOM.xyzw）
			bladeBuf[off + 12] = b.WindPhase;
			bladeBuf[off + 13] = b.TiltRad;
			bladeBuf[off + 14] = palClamped;
			bladeBuf[off + 15] = b.HueJitter;

			if (aoBuf == null) continue;

			// ── AO decal：位置同 blade base，椭圆大小随 blade 宽高略放大 ──
			var heightScale = 1f + Math.Clamp((b.HeightPx - 6f) / 10f, 0f, 0.5f);
			var aoHalfW     = Math.Max(1.2f, b.WidthPx * 2.8f * heightScale);
			var aoHalfH     = Math.Max(0.6f, b.WidthPx * 1.0f * heightScale);
			var paletteCol  = DefaultPalette[palClamped];
			var darkness    = 0.35f + Math.Min(0.20f, b.WidthPx * 0.10f);

			aoBuf[off + 0] = 1f;
			aoBuf[off + 1] = 0f;
			aoBuf[off + 2] = 0f;
			aoBuf[off + 3] = b.ScreenPos.X;
			aoBuf[off + 4] = 0f;
			aoBuf[off + 5] = 1f;
			aoBuf[off + 6] = 0f;
			aoBuf[off + 7] = b.ScreenPos.Y;
			// Color：AO 吃草色 × 0.20，和草地 tint 保持一致
			aoBuf[off + 8]  = paletteCol.R * 0.20f;
			aoBuf[off + 9]  = paletteCol.G * 0.20f;
			aoBuf[off + 10] = paletteCol.B * 0.18f;
			aoBuf[off + 11] = 1f;
			// CustomData：aoHalfW / aoHalfH / darkness / reserved
			aoBuf[off + 12] = aoHalfW;
			aoBuf[off + 13] = aoHalfH;
			aoBuf[off + 14] = darkness;
			aoBuf[off + 15] = 0f;
		}

		_multiMesh.Buffer = bladeBuf;
		if (aoBuf != null && _aoMultiMesh != null)
			_aoMultiMesh.Buffer = aoBuf;
	}

	/// <summary>
	/// 当前可见 blade 数（用于 HUD 诊断）。
	/// </summary>
	public int VisibleBladeCount => _multiMesh?.InstanceCount ?? 0;

	public void UpdateWindUniforms(float windSpeed, float windAmplitude, Vector2 windDir)
	{
		if (_material == null)
			return;
		_material.SetShaderParameter("wind_speed", windSpeed);
		_material.SetShaderParameter("wind_amp", windAmplitude);
		_material.SetShaderParameter("wind_dir", windDir);
	}

	public void UpdateSunUniforms(Vector2 sunDir, Color sunColor, float ambient = 0.55f)
	{
		if (_material == null)
			return;
		_material.SetShaderParameter("sun_dir", sunDir);
		_material.SetShaderParameter("sun_color", sunColor);
		_material.SetShaderParameter("ambient", ambient);
	}

	/// <summary>
	/// 整批 blade 统一的 value ramp 调参（全局）。
	/// 调用方：Preview 的调试键位、或后续 DebugModule 热调接线。
	/// </summary>
	public void UpdateToneUniforms(float rootDarken, float tipBoost)
	{
		if (_material == null)
			return;
		_material.SetShaderParameter("root_darken", rootDarken);
		_material.SetShaderParameter("tip_boost", tipBoost);
	}

	/// <summary>
	/// 副风（正交）强度与频率比调参。参考 Shadertoy Xsf3zX 的 sin(…*1.3+p.z) / sin(…*2.6+p.x) 双轴摆动。
	/// crossWindGain=0 → 退化为单轴主风；=1 → 主副风等量。
	/// </summary>
	public void UpdateCrossWindUniforms(float crossWindGain, float crossWindRatio)
	{
		if (_material == null)
			return;
		_material.SetShaderParameter("cross_wind_gain", crossWindGain);
		_material.SetShaderParameter("cross_wind_ratio", crossWindRatio);
	}

	/// <summary>
	/// 草尖漂白（Shadertoy pale-tip）调参。threshold 越低漂白的 blade 越多；strength=0 关闭效果。
	/// </summary>
	public void UpdateTipBleachUniforms(float threshold, float strength, Color color)
	{
		if (_material == null)
			return;
		_material.SetShaderParameter("tip_bleach_threshold", threshold);
		_material.SetShaderParameter("tip_bleach_strength", strength);
		_material.SetShaderParameter("tip_bleach_color", new Vector3(color.R, color.G, color.B));
	}

	/// <summary>
	/// 锥形收口（taper）调参：tipWidthRatio=0 极尖、=1 等长方形；taperCurve &lt;1 根部饱满、&gt;1 根部瘦削。
	/// </summary>
	public void UpdateTaperUniforms(float tipWidthRatio, float taperCurve)
	{
		if (_material == null)
			return;
		_material.SetShaderParameter("tip_width_ratio", tipWidthRatio);
		_material.SetShaderParameter("taper_curve", taperCurve);
	}

	private static void ApplyDefaultPalette(ShaderMaterial material)
	{
		var array = new Godot.Collections.Array();
		foreach (var color in DefaultPalette)
			array.Add(color);
		material.SetShaderParameter("blade_palette", array);
	}

	/// <summary>
	/// 构建单个 unit quad mesh：
	/// 顶点 x ∈ [-0.5, 0.5]，y ∈ [0, 1]，y=0 根 / y=1 尖。
	/// UV: (x+0.5, 1-y) → shader 里 UV.y=1 表根、UV.y=0 表尖。
	/// </summary>
	private static ArrayMesh BuildBladeQuad()
	{
		var vertices = new[]
		{
			new Vector2(-0.5f, 0f),
			new Vector2( 0.5f, 0f),
			new Vector2(-0.5f, 1f),
			new Vector2( 0.5f, 1f),
		};
		var uvs = new[]
		{
			new Vector2(0f, 1f),
			new Vector2(1f, 1f),
			new Vector2(0f, 0f),
			new Vector2(1f, 0f),
		};
		var indices = new[] { 0, 2, 1, 1, 2, 3 };

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.TexUV] = uvs;
		arrays[(int)Mesh.ArrayType.Index] = indices;

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}

	/// <summary>
	/// 构建中心对齐 unit quad mesh：
	/// 顶点 x, y ∈ [-0.5, 0.5]，中心 (0, 0)。用于 AO decal（椭圆从中心径向淡出）。
	/// UV: (x+0.5, y+0.5) → 片段内 UV ∈ [0, 1]，(0.5, 0.5) 为中心。
	/// </summary>
	private static ArrayMesh BuildCenteredQuad()
	{
		var vertices = new[]
		{
			new Vector2(-0.5f, -0.5f),
			new Vector2( 0.5f, -0.5f),
			new Vector2(-0.5f,  0.5f),
			new Vector2( 0.5f,  0.5f),
		};
		var uvs = new[]
		{
			new Vector2(0f, 0f),
			new Vector2(1f, 0f),
			new Vector2(0f, 1f),
			new Vector2(1f, 1f),
		};
		var indices = new[] { 0, 2, 1, 1, 2, 3 };

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.TexUV] = uvs;
		arrays[(int)Mesh.ArrayType.Index] = indices;

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}
}
