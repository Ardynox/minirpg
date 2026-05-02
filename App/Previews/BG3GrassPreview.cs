using System;
using Godot;

namespace MiniRPG.App.Previews;

/// <summary>
/// BG3 风独立草地预览场景脚本（类比 <see cref="GrassSurfacePreview"/> 的做法：
/// 打开 <c>res://Scene/BG3GrassPreview.tscn</c> → F6 直接跑，不依赖 GameState / 主渲染器 / 存档）。
///
/// 完全程序化：
///   地形：<see cref="FastNoiseLite"/> 多倍频 → ArrayMesh subdivided plane + 顶点位移 + 中心差分法线
///   草叶：QuadMesh + MultiMesh 批量实例 + 自定义 spatial shader（风摆 + SSS 透光 + 顶端收窄）
///   光照：DirectionalLight3D 暖色 + WorldEnvironment ACES/SSAO/Bloom/SDFGI（在 .tscn 里挂）
///
/// 键位：
///   鼠标左键拖动     绕 focus 点 orbit（调整 yaw / pitch）
///   鼠标右键拖动     相机平移（沿 focus 视平面）
///   鼠标滚轮         zoom（调整 orbit 距离）
///   WASD             平移 focus（世界 XZ）
///   Q / E            focus 下/上
///   1 / 2 / 3        草密度 低（15K）/ 中（40K）/ 高（120K）
///   [ / ]            风速 ±0.2
///   , / .            风向 ±15°
///   - / =            草叶高度上限 ±0.1
///   R                重 roll 地形 seed（重建 terrain + grass）
///   F / G            太阳俯角 -/+
///   T                切日/黄昏配色
///   F1               HUD 显隐
///   Home / 0         重置相机
/// </summary>
public sealed partial class BG3GrassPreview : Node3D
{
	// ── 地形网格 ──
	// 200×200 格 = 201² = 40,401 顶点，一次性构建 < 200ms 可接受
	private const int GridSize = 200;
	private const float CellSize = 1.0f;
	private const float TerrainExtent = GridSize * CellSize;
	private const float TerrainHalf = TerrainExtent * 0.5f;

	// 地形高度随机振幅（米），BG3 那种缓丘范围
	private const float TerrainMacroAmp = 3.5f;
	private const float TerrainMicroAmp = 0.35f;

	// ── 草密度预设 ──
	private const int DensityLow = 15_000;
	private const int DensityMid = 40_000;
	private const int DensityHigh = 120_000;

	// ── 草叶几何 ──
	// 高度范围按 BG3 近景"15-40cm 剧跳"的审美要求拉大（旧值 0.85-1.35 线性太平）：
	// 0.45 是半枯矮草，1.8 是"冒尖"的疯长叶，差 4× 能拉出明显高低起伏
	private const float BladeWidth = 0.09f;
	private const float BladeBaseHeight = 0.45f;
	private float _bladeHeightScaleMax = 1.80f;

	// ── orbit 相机默认值 ──
	// BG3 cinematic "草丛一瞥" 低机位（Gemini 子代理给的参数）：
	// y≈0.91m 由 focus.y + distance*sin(pitch) 得出；草丛挡住镜头下沿的 framing 感
	private static readonly float DefaultYaw = Mathf.DegToRad(35f);
	private static readonly float DefaultPitch = Mathf.DegToRad(6.5f);
	private const float DefaultDistance = 4.5f;
	private static readonly Vector3 DefaultFocus = new(0f, 0.4f, 0f);
	private const float MinDistance = 1.2f;
	private const float MaxDistance = 120f;

	// 控件灵敏度
	private const float MouseOrbitSensitivity = 0.0065f;
	private const float MousePanSensitivity = 0.015f;
	private const float ZoomStep = 1.12f;
	private const float MoveSpeed = 14f;
	private const float VerticalSpeed = 8f;

	// ── 状态 ──
	private int _seed = 20260420;
	private int _bladeCount = DensityMid;
	private float[,] _heights = null!;

	private MeshInstance3D _ground = null!;
	private ShaderMaterial _groundMat = null!;

	private MultiMeshInstance3D _grassInstance = null!;
	private MultiMesh _multiMesh = null!;
	private ShaderMaterial _grassMat = null!;
	private QuadMesh _bladeMesh = null!;

	private Camera3D _camera = null!;
	private DirectionalLight3D _sun = null!;
	private WorldEnvironment? _worldEnvironment;

	private Vector3 _focus = DefaultFocus;
	private float _yaw = DefaultYaw;
	private float _pitch = DefaultPitch;
	private float _distance = DefaultDistance;

	private bool _rotating, _panning;
	private Vector2 _lastMousePos;

	private float _windSpeed = 1.2f;
	private float _windAmp = 0.22f;
	private float _windAngleDeg = 8f;       // 0° = +X 向
	private float _sunPitchDeg = 42f;
	private float _sunYawDeg = -35f;
	private bool _duskMode;

	private Label _statusLabel = null!;
	private Label _hintLabel = null!;
	private CanvasLayer _hud = null!;
	private bool _hudVisible = true;

	public override void _Ready()
	{
		_camera = GetNode<Camera3D>("Camera3D");
		_sun = GetNode<DirectionalLight3D>("Sun");
		_worldEnvironment = GetNodeOrNull<WorldEnvironment>("WorldEnvironment");

		_ground = GetNode<MeshInstance3D>("Ground");
		_groundMat = new ShaderMaterial
		{
			Shader = GD.Load<Shader>("res://App/Previews/bg3_ground.gdshader"),
		};
		_ground.MaterialOverride = _groundMat;

		BuildTerrain();
		BuildGrassInstance();
		RebuildBlades();

		UpdateCameraTransform();
		ApplySunTransform();
		ApplyPaletteToGround();

		BuildHud();
		RefreshStatusLabel();
	}

	public override void _Process(double delta)
	{
		var dt = (float)delta;

		// WASD / QE 平移 focus（相对相机朝向 xz 投影，方便操控）
		if (!Input.IsMouseButtonPressed(MouseButton.Right))
		{
			var fwd = new Vector3(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw));
			var right = new Vector3(fwd.Z, 0, -fwd.X);
			var move = Vector3.Zero;
			if (Input.IsKeyPressed(Key.W)) move += fwd;
			if (Input.IsKeyPressed(Key.S)) move -= fwd;
			if (Input.IsKeyPressed(Key.A)) move -= right;
			if (Input.IsKeyPressed(Key.D)) move += right;
			if (move.LengthSquared() > 0)
			{
				_focus += move.Normalized() * MoveSpeed * dt;
				UpdateCameraTransform();
			}

			if (Input.IsKeyPressed(Key.Q)) { _focus.Y -= VerticalSpeed * dt; UpdateCameraTransform(); }
			if (Input.IsKeyPressed(Key.E)) { _focus.Y += VerticalSpeed * dt; UpdateCameraTransform(); }
		}

		// 风向 uniform 实时推进 shader
		var rad = Mathf.DegToRad(_windAngleDeg);
		var wd = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
		_grassMat.SetShaderParameter("wind_dir", wd);
		_grassMat.SetShaderParameter("wind_speed", _windSpeed);
		_grassMat.SetShaderParameter("wind_amp", _windAmp);
	}

	// ─────────────────────────────────────────────
	//  地形构建
	// ─────────────────────────────────────────────

	private void BuildTerrain()
	{
		var noise = new FastNoiseLite
		{
			Seed = _seed,
			NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
			Frequency = 0.018f,
			FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
			FractalOctaves = 4,
			FractalLacunarity = 2.0f,
			FractalGain = 0.5f,
		};
		var micro = new FastNoiseLite
		{
			Seed = _seed ^ 0x5A5A,
			NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
			Frequency = 0.22f,
			FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
			FractalOctaves = 2,
		};

		var vertsPerSide = GridSize + 1;
		_heights = new float[vertsPerSide, vertsPerSide];

		var verts = new Vector3[vertsPerSide * vertsPerSide];
		var uvs = new Vector2[vertsPerSide * vertsPerSide];

		for (var z = 0; z < vertsPerSide; z++)
		for (var x = 0; x < vertsPerSide; x++)
		{
			var wx = x * CellSize - TerrainHalf;
			var wz = z * CellSize - TerrainHalf;

			// 主起伏 + 微细节 + 中心下凹（让镜头焦点在一块大致平坦的低洼）
			var macro = noise.GetNoise2D(wx, wz);                        // -1..1
			var m = micro.GetNoise2D(wx, wz);                            // -1..1
			var radial = Mathf.Clamp(1f - new Vector2(wx, wz).Length() / TerrainHalf, 0f, 1f);
			var dip = -0.6f * radial * radial;

			var h = macro * TerrainMacroAmp + m * TerrainMicroAmp + dip;
			_heights[x, z] = h;

			var i = z * vertsPerSide + x;
			verts[i] = new Vector3(wx, h, wz);
			uvs[i] = new Vector2((float)x / GridSize, (float)z / GridSize);
		}

		// 中心差分算法线（比 SurfaceTool.GenerateNormals 更快，也更准确）
		var normals = new Vector3[vertsPerSide * vertsPerSide];
		for (var z = 0; z < vertsPerSide; z++)
		for (var x = 0; x < vertsPerSide; x++)
		{
			var hl = _heights[Math.Max(0, x - 1), z];
			var hr = _heights[Math.Min(vertsPerSide - 1, x + 1), z];
			var hd = _heights[x, Math.Max(0, z - 1)];
			var hu = _heights[x, Math.Min(vertsPerSide - 1, z + 1)];
			var n = new Vector3(hl - hr, 2f * CellSize, hd - hu).Normalized();
			normals[z * vertsPerSide + x] = n;
		}

		var indices = new int[GridSize * GridSize * 6];
		var idx = 0;
		for (var z = 0; z < GridSize; z++)
		for (var x = 0; x < GridSize; x++)
		{
			var a = z * vertsPerSide + x;
			var b = a + 1;
			var c = a + vertsPerSide;
			var d = c + 1;

			indices[idx++] = a;
			indices[idx++] = c;
			indices[idx++] = b;

			indices[idx++] = b;
			indices[idx++] = c;
			indices[idx++] = d;
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = verts;
		arrays[(int)Mesh.ArrayType.Normal] = normals;
		arrays[(int)Mesh.ArrayType.TexUV] = uvs;
		arrays[(int)Mesh.ArrayType.Index] = indices;

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		_ground.Mesh = mesh;
	}

	/// <summary>双线性采样地形高度，世界坐标系 xz → y。</summary>
	private float SampleHeight(float worldX, float worldZ)
	{
		var gx = (worldX + TerrainHalf) / CellSize;
		var gz = (worldZ + TerrainHalf) / CellSize;
		var x0 = Mathf.Clamp((int)Mathf.Floor(gx), 0, GridSize);
		var z0 = Mathf.Clamp((int)Mathf.Floor(gz), 0, GridSize);
		var x1 = Mathf.Min(x0 + 1, GridSize);
		var z1 = Mathf.Min(z0 + 1, GridSize);
		var tx = Mathf.Clamp(gx - x0, 0f, 1f);
		var tz = Mathf.Clamp(gz - z0, 0f, 1f);

		var h00 = _heights[x0, z0];
		var h10 = _heights[x1, z0];
		var h01 = _heights[x0, z1];
		var h11 = _heights[x1, z1];
		var hx0 = Mathf.Lerp(h00, h10, tx);
		var hx1 = Mathf.Lerp(h01, h11, tx);
		return Mathf.Lerp(hx0, hx1, tz);
	}

	// ─────────────────────────────────────────────
	//  草叶（MultiMesh）构建
	// ─────────────────────────────────────────────

	private void BuildGrassInstance()
	{
		// 单根草叶 mesh：QuadMesh 面向 +Z，底部 pivot，面大小 1m
		_bladeMesh = new QuadMesh
		{
			Size = new Vector2(1f, 1f),
			CenterOffset = new Vector3(0f, 0.5f, 0f),
			Orientation = PlaneMesh.OrientationEnum.Z,
		};

		_grassMat = new ShaderMaterial
		{
			Shader = GD.Load<Shader>("res://App/Previews/bg3_grass_blade.gdshader"),
		};
		_grassMat.SetShaderParameter("blade_width", BladeWidth);

		_multiMesh = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			UseCustomData = true,
			UseColors = false,
			Mesh = _bladeMesh,
			// 手动给个大包围盒：shader 会把顶点沿风向位移 + 地形有起伏，
			// 让 Godot 自动算 AABB 容易把风摆动幅度外的 blade 剔除掉。
			CustomAabb = new Aabb(
				new Vector3(-TerrainHalf, -TerrainMacroAmp - 5f, -TerrainHalf),
				new Vector3(TerrainExtent, TerrainMacroAmp * 2f + 15f, TerrainExtent)),
		};

		_grassInstance = new MultiMeshInstance3D
		{
			Multimesh = _multiMesh,
			MaterialOverride = _grassMat,
			// cast_shadow 打开会让远处草场掉帧严重，关掉保性能（BG3 远处草也关）
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled,
		};
		AddChild(_grassInstance);
	}

	private void RebuildBlades()
	{
		_multiMesh.InstanceCount = _bladeCount;

		var rng = new Random(_seed ^ 0x1337);

		// 草地只铺在距中心 TerrainHalf - 边缘缓冲 的范围内，避免边界悬空
		var margin = 6f;
		var halfSpan = TerrainHalf - margin;

		// ── Clump 分布（Gemini 给的 BG3 近景参数）──
		// 近景 BG3 "乱中有序"：5-8 根成簇形成小堆、簇间距 ≥1.8m、同簇高度/倾斜方向有相关性。
		// 取 M=25 叶/clump（40K 总量 → 1600 clump），σ=0.15m 覆盖 ≈95% 叶在 0.45m 范围内。
		const int BladesPerClump = 25;
		const float ClumpRadiusSigma = 0.15f;
		const float ClumpRadiusMax = 0.45f;
		const float ClumpMinDist = 1.8f;
		const float HeightClumpBias = 0.7f;     // 同簇 70% 共享基准高度，30% 独立
		const float TiltYawClumpBias = 0.6f;    // 同簇 60% 倾斜方向趋同
		const float TiltAngleSigmaDeg = 7.5f;   // 倾斜幅度 σ，box-muller 高斯
		const float TiltAngleMaxDeg = 22f;

		var clumpCount = Math.Max(1, _bladeCount / BladesPerClump);

		// jittered grid 近似 blue-noise：场地切成 clumpsPerSide² 个 cell，每 cell 内随机放 1 个 center。
		// 例 40K blade → 40×40 cell → cellSpan 9.7m，远大于 ClumpMinDist 1.8m，够安全。
		var clumpsPerSide = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(clumpCount)));
		var cellSpan = halfSpan * 2f / clumpsPerSide;
		var jitterHalf = Math.Max(0f, (cellSpan - ClumpMinDist) * 0.5f);

		var i = 0;
		for (var cy = 0; cy < clumpsPerSide && i < _bladeCount; cy++)
		for (var cx = 0; cx < clumpsPerSide && i < _bladeCount; cx++)
		{
			var cellCenterX = -halfSpan + (cx + 0.5f) * cellSpan;
			var cellCenterZ = -halfSpan + (cy + 0.5f) * cellSpan;
			var clumpCx = cellCenterX + (float)(rng.NextDouble() * 2 - 1) * jitterHalf;
			var clumpCz = cellCenterZ + (float)(rng.NextDouble() * 2 - 1) * jitterHalf;

			var clumpBaseScale = BladeBaseHeight + (float)rng.NextDouble() * (_bladeHeightScaleMax - BladeBaseHeight);
			var clumpTiltYaw = (float)(rng.NextDouble() * Mathf.Tau);

			for (var b = 0; b < BladesPerClump && i < _bladeCount; b++)
			{
				// 簇内散点：box-muller 高斯半径 + 均匀角度
				var u1 = 1.0 - rng.NextDouble();
				var u2 = rng.NextDouble();
				var gaussR = ClumpRadiusSigma * (float)Math.Sqrt(-2.0 * Math.Log(u1));
				gaussR = Math.Min(gaussR, ClumpRadiusMax);
				var gaussAng = (float)(u2 * Mathf.Tau);
				var wx = clumpCx + gaussR * Mathf.Cos(gaussAng);
				var wz = clumpCz + gaussR * Mathf.Sin(gaussAng);
				var wy = SampleHeight(wx, wz) - 0.02f; // 根部下沉 2cm 吃地形线性/双线性差

				// 高度：同簇基准与独立随机加权混合
				var indepScale = BladeBaseHeight + (float)rng.NextDouble() * (_bladeHeightScaleMax - BladeBaseHeight);
				var heightScale = Mathf.Lerp(indepScale, clumpBaseScale, HeightClumpBias);

				// 倾斜：幅度高斯 σ=7.5°（95% 落在 0-15°）、clamp 22°；方向与簇基准 60% 趋同
				var u3 = 1.0 - rng.NextDouble();
				var u4 = rng.NextDouble();
				var tiltMag = Mathf.DegToRad(TiltAngleSigmaDeg) * (float)Math.Sqrt(-2.0 * Math.Log(u3));
				tiltMag = Math.Min(tiltMag, Mathf.DegToRad(TiltAngleMaxDeg));
				var tiltIndepYaw = (float)(u4 * Mathf.Tau);
				var tiltYaw = Mathf.LerpAngle(tiltIndepYaw, clumpTiltYaw, TiltYawClumpBias);
				// tilt 轴 = 水平面内 yaw 方向旋转 90° 的单位向量；绕该轴 tiltMag 弧度
				var tiltAxis = new Vector3(-Mathf.Sin(tiltYaw), 0f, Mathf.Cos(tiltYaw));
				var tiltBasis = new Basis(tiltAxis, tiltMag);

				var phase = (float)(rng.NextDouble() * Mathf.Tau);
				var hue = (float)(rng.NextDouble() * 2 - 1);
				var yaw = (float)(rng.NextDouble() * Mathf.Tau);

				var xform = new Transform3D(tiltBasis, new Vector3(wx, wy, wz));
				_multiMesh.SetInstanceTransform(i, xform);
				_multiMesh.SetInstanceCustomData(i, new Color(phase, hue, heightScale, yaw));
				i++;
			}
		}

		// 剩余 slot（当 _bladeCount 不能被 BladesPerClump × clumpCount 整除时）归零避免残留
		for (; i < _bladeCount; i++)
		{
			_multiMesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, Vector3.Zero));
			_multiMesh.SetInstanceCustomData(i, new Color(0, 0, 0, 0));
		}
	}

	// ─────────────────────────────────────────────
	//  相机 / 光照 / 调色
	// ─────────────────────────────────────────────

	private void UpdateCameraTransform()
	{
		// orbit：pos = focus + offset，基于 yaw / pitch / distance
		_pitch = Mathf.Clamp(_pitch, Mathf.DegToRad(4f), Mathf.DegToRad(85f));
		_distance = Mathf.Clamp(_distance, MinDistance, MaxDistance);

		var cp = Mathf.Cos(_pitch);
		var sp = Mathf.Sin(_pitch);
		var offset = new Vector3(
			Mathf.Sin(_yaw) * cp,
			sp,
			Mathf.Cos(_yaw) * cp
		) * _distance;

		var pos = _focus + offset;
		_camera.GlobalTransform = new Transform3D(Basis.LookingAt(_focus - pos, Vector3.Up), pos);
	}

	private void ApplySunTransform()
	{
		var yaw = Mathf.DegToRad(_sunYawDeg);
		var pitch = Mathf.DegToRad(_sunPitchDeg);
		// DirectionalLight3D 的 -Z 为照射方向，所以让光源朝下倾斜再绕 Y 旋转
		var basis = Basis.FromEuler(new Vector3(-pitch, yaw, 0));
		_sun.GlobalTransform = new Transform3D(basis, new Vector3(0, 30, 0));
	}

	private void ApplyPaletteToGround()
	{
		if (_duskMode)
		{
			_sun.LightColor = new Color(1.0f, 0.72f, 0.48f);
			_sun.LightEnergy = 1.35f;
			_groundMat.SetShaderParameter("fog_color", new Color(0.95f, 0.72f, 0.58f));
			_groundMat.SetShaderParameter("grass_high", new Color(0.55f, 0.48f, 0.22f));
			_groundMat.SetShaderParameter("dry_color", new Color(0.62f, 0.40f, 0.22f));
			_grassMat.SetShaderParameter("sss_color", new Color(1.0f, 0.72f, 0.32f));
			_grassMat.SetShaderParameter("base_color_high", new Color(0.66f, 0.62f, 0.24f));
		}
		else
		{
			// 日间（Gemini 审美版）：橄榄绿 + 蓝雾 + 去饱和
			_sun.LightColor = new Color(1.0f, 0.95f, 0.85f);
			_sun.LightEnergy = 1.55f;
			_groundMat.SetShaderParameter("fog_color", new Color(0.72f, 0.82f, 0.95f));
			_groundMat.SetShaderParameter("grass_high", new Color(0.588f, 0.639f, 0.396f));  // #96A365
			_groundMat.SetShaderParameter("dry_color", new Color(0.710f, 0.651f, 0.427f));   // #B5A66D
			_grassMat.SetShaderParameter("sss_color", new Color(0.784f, 0.761f, 0.490f));    // #C8C27D
			_grassMat.SetShaderParameter("base_color_high", new Color(0.588f, 0.639f, 0.396f)); // #96A365
		}
	}

	// ─────────────────────────────────────────────
	//  输入
	// ─────────────────────────────────────────────

	public override void _UnhandledInput(InputEvent @event)
	{
		if (HandleMouseButton(@event)) return;
		if (HandleMouseMotion(@event)) return;
		HandleKey(@event);
	}

	private bool HandleMouseButton(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mb) return false;

		switch (mb.ButtonIndex)
		{
			case MouseButton.Left:
				_rotating = mb.Pressed;
				_lastMousePos = mb.Position;
				return true;
			case MouseButton.Right:
				_panning = mb.Pressed;
				_lastMousePos = mb.Position;
				return true;
			case MouseButton.WheelUp when mb.Pressed:
				_distance /= ZoomStep;
				UpdateCameraTransform();
				RefreshStatusLabel();
				return true;
			case MouseButton.WheelDown when mb.Pressed:
				_distance *= ZoomStep;
				UpdateCameraTransform();
				RefreshStatusLabel();
				return true;
			case MouseButton.Middle when mb.Pressed:
				ResetCamera();
				return true;
		}
		return false;
	}

	private bool HandleMouseMotion(InputEvent @event)
	{
		if (@event is not InputEventMouseMotion mm) return false;
		var delta = mm.Position - _lastMousePos;
		_lastMousePos = mm.Position;

		if (_rotating)
		{
			_yaw -= delta.X * MouseOrbitSensitivity;
			_pitch -= delta.Y * MouseOrbitSensitivity;
			UpdateCameraTransform();
			RefreshStatusLabel();
			return true;
		}
		if (_panning)
		{
			var right = _camera.GlobalTransform.Basis.X;
			var up = _camera.GlobalTransform.Basis.Y;
			var scale = _distance * MousePanSensitivity;
			_focus -= right * delta.X * scale;
			_focus += up * delta.Y * scale;
			UpdateCameraTransform();
			RefreshStatusLabel();
			return true;
		}
		return false;
	}

	private void HandleKey(InputEvent @event)
	{
		if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

		var changed = true;
		switch (key.Keycode)
		{
			case Key.Key1:
				_bladeCount = DensityLow;
				RebuildBlades();
				break;
			case Key.Key2:
				_bladeCount = DensityMid;
				RebuildBlades();
				break;
			case Key.Key3:
				_bladeCount = DensityHigh;
				RebuildBlades();
				break;

			case Key.Bracketleft:
				_windSpeed = Mathf.Max(0f, _windSpeed - 0.2f);
				break;
			case Key.Bracketright:
				_windSpeed = Mathf.Min(8f, _windSpeed + 0.2f);
				break;

			case Key.Comma:
				_windAngleDeg = (_windAngleDeg - 15f + 360f) % 360f;
				break;
			case Key.Period:
				_windAngleDeg = (_windAngleDeg + 15f) % 360f;
				break;

			case Key.Minus:
				_bladeHeightScaleMax = Mathf.Max(BladeBaseHeight + 0.05f, _bladeHeightScaleMax - 0.1f);
				RebuildBlades();
				break;
			case Key.Equal:
				_bladeHeightScaleMax = Mathf.Min(2.5f, _bladeHeightScaleMax + 0.1f);
				RebuildBlades();
				break;

			case Key.R:
				_seed = unchecked(_seed * 1664525 + 1013904223);
				BuildTerrain();
				RebuildBlades();
				break;

			case Key.F:
				_sunPitchDeg = Mathf.Max(5f, _sunPitchDeg - 5f);
				ApplySunTransform();
				break;
			case Key.G:
				_sunPitchDeg = Mathf.Min(85f, _sunPitchDeg + 5f);
				ApplySunTransform();
				break;

			case Key.T:
				_duskMode = !_duskMode;
				ApplyPaletteToGround();
				break;

			case Key.F1:
				_hudVisible = !_hudVisible;
				_hud.Visible = _hudVisible;
				break;

			case Key.Home:
			case Key.Key0:
				ResetCamera();
				break;

			default:
				changed = false;
				break;
		}

		if (changed)
		{
			GetViewport().SetInputAsHandled();
			RefreshStatusLabel();
		}
	}

	private void ResetCamera()
	{
		_focus = DefaultFocus;
		_yaw = DefaultYaw;
		_pitch = DefaultPitch;
		_distance = DefaultDistance;
		UpdateCameraTransform();
		RefreshStatusLabel();
	}

	// ─────────────────────────────────────────────
	//  HUD
	// ─────────────────────────────────────────────

	private void BuildHud()
	{
		_hud = new CanvasLayer { Name = "HUD" };
		AddChild(_hud);

		var title = new Label
		{
			Text = "BG3 风草地 预览",
			Position = new Vector2(28, 20),
		};
		title.AddThemeFontSizeOverride("font_size", 26);
		title.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.95f));
		_hud.AddChild(title);

		var subtitle = new Label
		{
			Text = "程序化地形 + MultiMesh 草叶 + 风摆 / SSS shader — 独立场景，不依赖 GameState",
			Position = new Vector2(28, 56),
		};
		subtitle.AddThemeFontSizeOverride("font_size", 13);
		subtitle.AddThemeColorOverride("font_color", new Color(0.75f, 0.78f, 0.82f));
		_hud.AddChild(subtitle);

		_hintLabel = new Label
		{
			Position = new Vector2(28, 84),
			Text = "左键 orbit    右键 pan    滚轮 zoom    WASD/QE 平移    " +
				"1/2/3 密度 15K|40K|120K    [/] 风速 ±0.2    ,/. 风向 ±15°    " +
				"-/= 草高 ±0.1    F/G 日高 -/+    T 日/黄昏    R 重生成    Home/0 复位    F1 HUD",
		};
		_hintLabel.AddThemeFontSizeOverride("font_size", 12);
		_hintLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.85f, 1f));
		_hud.AddChild(_hintLabel);

		_statusLabel = new Label
		{
			Position = new Vector2(28, 118),
		};
		_statusLabel.AddThemeFontSizeOverride("font_size", 14);
		_statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.55f));
		_hud.AddChild(_statusLabel);
	}

	private void RefreshStatusLabel()
	{
		if (_statusLabel == null) return;
		_statusLabel.Text =
			$"草数 = {_bladeCount:N0}    风 = {_windSpeed:0.0}Hz @ {_windAngleDeg:0}°    " +
			$"草高 max = {_bladeHeightScaleMax:0.00}m    太阳高度 = {_sunPitchDeg:0}°    " +
			$"配色 = {(_duskMode ? "黄昏" : "日间")}    种子 = {_seed}    " +
			$"相机 dist={_distance:0.0}m  yaw={Mathf.RadToDeg(_yaw):0}°  pitch={Mathf.RadToDeg(_pitch):0}°  " +
			$"focus=({_focus.X:0.0}, {_focus.Y:0.0}, {_focus.Z:0.0})";
	}
}
