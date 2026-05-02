using System;
using Godot;
using MiniRPG.Core.Debug;
using MiniRPG.Core.World;
using MiniRPG.Core.World.Surface;
using MiniRPG.Module.Render.Surface;

namespace MiniRPG.App.Previews;

/// <summary>
/// 草地 SurfaceCover 重构后的独立预览场景脚本。
/// 直接打开 res://Scene/GrassSurfacePreview.tscn → F6 运行即可看到效果，
/// 不依赖任何 GameState / SaveModule / 主渲染器，只跑：
///
///   GrassCoverSampler  (确定性 PerlinNoise) →
///   渲染模式分叉 (DebugModule.GrassRenderMode)：
///     Shader3D（默认）→ GrassBladeField + procedural_grass_blade.gdshader
///     Legacy           → GrassOverlayPass + TerrainAtlas (6 个 procedural 变体)
///     Off              → 仅裸 dirt
///
/// 键位：
///   1/2/3      切 ForceGrassOverlay = Auto / On / Off（Legacy 模式下才生效）
///   4/5/6      切 GrassRenderMode = Shader3D / Legacy / Off
///   [ / ]      调 GrassDensityThreshold ±32
///   , / .      切 GrassVariantOverride 在 -1 / 0..5 之间（Legacy 用）
///   - / =      Shader3D 模式下 blade/tile 数 ±8
///   ; / '      Shader3D 模式下风速 ±0.4 Hz
///   R          重 roll worldSeed (重新 patchy)
///   Z / X      键盘缩放
///   滚轮        缩放（指哪缩哪）
///   鼠标右键拖动  平移
///   鼠标中键     回中
/// </summary>
public sealed partial class GrassSurfacePreview : Node2D
{
	// ── 网格大小：48×48 = 2304 格，多块拼一起足够看清整体 patchy 形态 ──
	// 单格顶面 128×64，48 格对角线 ≈ 6144×3072 px，远超 1080p 视口；
	// 默认 Camera2D.zoom 1.6 + 滚轮自由缩放配合查看。
	private const int GridSize = 48;

	// 单 tile blade 上限：批量 Buffer 上传后 1024 也能流畅 rebuild。
	// 2304 tile × 1024 = 2.36M blade；GPU pixel 覆盖会成为真正瓶颈（不是 CPU）。
	public const int MaxBladesPerTile = 1024;
	// 键盘 +/- 的步进（上限抬高后旧 ±8 要按 ~100 次才到顶，太慢）
	private const int BladeStepKeyboard = 32;

	// ── 顶面菱形尺寸，和主渲染器对齐（IsoCoordUtil） ──
	private const float TileHalfW = IsoCoordUtil.TileHalfW;
	private const float TileHalfH = IsoCoordUtil.TileHalfH;
	private const float TopFaceW = TileHalfW * 2f; // 128
	private const float TopFaceH = TileHalfH * 2f; // 64

	// ── 缩放范围 ──
	private static readonly Vector2 MinZoom = new(0.25f, 0.25f);
	private static readonly Vector2 MaxZoom = new(4.0f, 4.0f);
	private const float ZoomStep = 1.15f;

	private TerrainAtlas? _atlas;
	private TerrainDef? _dirtDef;

	private int _worldSeed = 1337;

	private Camera2D? _camera;
	private bool _isDragging;
	private Vector2 _dragLastScreenPos;

	private Label? _statusLabel;
	private Label? _hintLabel;
	private Label? _diagLabel;
	private VariantStripCanvas? _variantStrip;
	private GrassDebugPanel? _debugPanel;
	private string _diag = "";

	// 缓存 EmitFace 委托避免每格分配（与主渲染器同款做法）
	private Action<Rect2, Vector2, Color, long>? _emitGrassFace;

	// _Draw 一次性收集所有 face，按 SortKey 稳定排序后画出，
	// 让草地 overlay 永远盖在 dirt 顶面之上。
	private readonly System.Collections.Generic.List<DrawFace> _faces = new();

	private readonly record struct DrawFace(Rect2 Region, Vector2 Pos, Color Tint, long SortKey);

	// ── Shader3D 模式：GrassBladeField 真·3D blade 渲染节点 ──
	private GrassBladeField? _bladeField;
	private bool _bladesDirty = true;

	public override void _Ready()
	{
		var loadOk = false;
		var loadMsg = "";
		try
		{
			TerrainRegistry.Load("terrains.json");
			loadOk = TerrainRegistry.All.Count > 0;
			loadMsg = $"已加载 {TerrainRegistry.All.Count} 个地形";
		}
		catch (Exception ex)
		{
			loadMsg = $"加载失败：{ex.GetType().Name}：{ex.Message}";
			GD.PushWarning($"[GrassPreview] TerrainRegistry.Load failed: {ex.Message}");
		}

		_dirtDef = TerrainRegistry.Get(Terrains.Dirt);
		var dirtSource = "terrains.json";
		if (_dirtDef == null)
		{
			// F6 单跑预览场景时 GameDataLocator 可能没找到 res://Data/terrains.json，
			// 内联兜底注册最小可用 dirt + grass_block，让 sampler/atlas 都能 work。
			RegisterFallbackTerrains();
			_dirtDef = TerrainRegistry.Get(Terrains.Dirt);
			dirtSource = "内联兜底";
		}

		_atlas = new TerrainAtlas();
		_atlas.Build();

		// 初始钩子状态：Auto + 不裁剪 + 走 hash 选变体，默认 Shader3D
		DebugModule.ForceGrassOverlay = GrassOverlayForceMode.Auto;
		DebugModule.GrassDensityThreshold = 0;
		DebugModule.GrassVariantOverride = -1;
		DebugModule.GrassRenderMode = GrassRenderMode.Shader3D;

		// Shader3D 渲染节点：挂到 self 下，MultiMeshInstance2D 自动跟随 Camera2D 变换。
		_bladeField = new GrassBladeField
		{
			Name = "GrassBladeField",
			AoEnabled = DebugModule.GrassBladeAo,
		};
		AddChild(_bladeField);
		_bladesDirty = true;

		// .tscn 里挂的 Camera2D 抓一下，方便 zoom / pan 通过代码改
		_camera = GetNodeOrNull<Camera2D>("Camera2D");

		_diag =
			$"地形数据：{loadMsg}    dirt 来源：{dirtSource}    " +
			$"dirt = {(_dirtDef != null ? $"id={_dirtDef.Id}" : "null")}    " +
			$"AtlasTexture = {(_atlas.AtlasTexture != null ? "已生成" : "null")}    " +
			$"草地变体数 = {_atlas.GrassOverlayVariantCount}";
		GD.Print($"[GrassPreview] {_diag}");

		BuildOverlayUi();
		QueueRedraw();
	}

	private static void RegisterFallbackTerrains()
	{
		// 只补 sampler 真正会判定的两种 base terrain，id 任取（避开 0=void）
		if (TerrainRegistry.Get(Terrains.Dirt) == null)
		{
			TerrainRegistry.Register(new TerrainDef
			{
				Id = 1,
				StringId = Terrains.Dirt,
				Glyph = ".",
				Solid = true,
				Material = "dirt",
			});
		}
		if (TerrainRegistry.Get(Terrains.GrassBlock) == null)
		{
			TerrainRegistry.Register(new TerrainDef
			{
				Id = 2,
				StringId = Terrains.GrassBlock,
				Glyph = ",",
				Solid = true,
				Material = "dirt",
			});
		}
	}

	public override void _Draw()
	{
		// dirt 顶面 region：从 atlas 拿；若拿不到则用纯色菱形兜底，让画面始终有内容。
		var atlas = _atlas?.AtlasTexture;
		TerrainAtlas.FaceRegions dirtRegions = default;
		var hasAtlasDirt = _atlas != null && _atlas.TryGetRegions(Terrains.Dirt, out dirtRegions);
		var dirtTopRegion = dirtRegions.Top;

		// 没 dirt def 时拿 0 给 sampler，反正 sampler 会判定 StringId != "dirt" 直接返回 0。
		ushort dirtId = _dirtDef?.Id ?? 0;
		var dirtTint = new Color(0.55f, 0.35f, 0.15f); // TerrainAtlas 里 fallback 同色

		_faces.Clear();
		_emitGrassFace ??= EmitGrassFace;

		var renderMode = DebugModule.GrassRenderMode;
		var useLegacyOverlay = renderMode == GrassRenderMode.Legacy;

		// ── 第一遍：dirt 顶面 + （Legacy 模式下）草地 overlay 入队 ──
		// 用 (-GridSize/2, -GridSize/2) 起点让网格围绕世界原点居中，
		// 配合场景里的 Camera2D 即可一眼看到中心区域。
		var half = GridSize / 2;
		for (var wy = -half; wy < half; wy++)
		for (var wx = -half; wx < half; wx++)
		{
			var screen = IsoCoordUtil.WorldToScreen(wx, wy, 0);
			var sortKey = IsoCoordUtil.SortKey(wx, wy, 0);

			// dirt 顶面（不应用 lighting，预览要看清原色）
			if (hasAtlasDirt)
				_faces.Add(new DrawFace(dirtTopRegion, screen, Colors.White, sortKey));
			else
				DrawDirtFallbackDiamond(screen, dirtTint);

			if (!useLegacyOverlay || _atlas == null) continue;

			// 走 sampler 拿密度 → 走 overlay pass 入队（Legacy 路径）
			var cover = GrassCoverSampler.Sample(
				worldSeed: _worldSeed,
				wx: wx,
				wy: wy,
				baseTerrainId: dirtId,
				exposedToSky: true,
				underwater: false);

			var ctx = new GrassOverlayDrawContext(
				_atlas,
				screen,
				Colors.White,
				sortKey,
				_emitGrassFace);

			GrassOverlayPass.DrawTopFace(in ctx, cover, wx, wy);
		}

		// ── 第二遍：按 SortKey 升序绘制（painter，让草地稳定盖住 dirt） ──
		// 同 sortKey 的 dirt 先入队，overlay 后入队 → 稳定排序自动保留 painter 顺序。
		if (atlas != null && _faces.Count > 0)
		{
			_faces.Sort(static (a, b) => a.SortKey.CompareTo(b.SortKey));
			foreach (var f in _faces)
			{
				var size = f.Region.Size;
				if (size.X <= 0 || size.Y <= 0) continue;
				var dest = new Rect2(f.Pos - size / 2f, size);
				DrawTextureRectRegion(atlas, dest, f.Region, f.Tint);
			}
		}

		// Shader3D 路径：按需重建 MultiMesh blade 批次
		if (renderMode == GrassRenderMode.Shader3D)
		{
			if (_bladesDirty)
			{
				RebuildBladeField(dirtId);
				_bladesDirty = false;
			}
		}
		else
		{
			_bladeField?.BeginFill();
			_bladeField?.EndFill();
		}

		// 网格指示框 + 中心十字（轻量）
		DrawDebugFrame();
	}

	public override void _Process(double delta)
	{
		if (_bladeField == null) return;
		// 风速 / 振幅每帧从 DebugModule 拉，允许用户用热键实时调
		_bladeField.UpdateWindUniforms(
			windSpeed: DebugModule.GrassWindSpeed,
			windAmplitude: DebugModule.GrassWindAmplitude,
			windDir: new Vector2(1f, 0.15f));
	}

	/// <summary>
	/// Shader3D 模式的 blade 实例重建：遍历 48×48 网格，每格按 cover 密度 hash 出 N 根 blade 放进 MultiMesh。
	/// 与主渲染器共用 <see cref="GrassBladeEmitter.EmitTile"/>，保证位置/色板哈希一致。
	/// </summary>
	private void RebuildBladeField(ushort dirtId)
	{
		if (_bladeField == null) return;
		_bladeField.BeginFill();

		// 上限 MaxBladesPerTile：批量 Buffer 上传后，rebuild 成本接近线性，上到 1024 不卡
		var bladesPerTile = Math.Max(0, Math.Min(MaxBladesPerTile, DebugModule.GrassBladesPerTile));
		if (bladesPerTile == 0)
		{
			_bladeField.EndFill();
			return;
		}

		var force = DebugModule.ForceGrassOverlay;
		var variantOverride = DebugModule.GrassVariantOverride;
		var densityThreshold = DebugModule.GrassDensityThreshold;

		var half = GridSize / 2;
		for (var wy = -half; wy < half; wy++)
		for (var wx = -half; wx < half; wx++)
		{
			byte cover;
			if (force == GrassOverlayForceMode.On)
				cover = 255;
			else if (force == GrassOverlayForceMode.Off)
				continue;
			else
				cover = GrassCoverSampler.Sample(
					worldSeed: _worldSeed,
					wx: wx,
					wy: wy,
					baseTerrainId: dirtId,
					exposedToSky: true,
					underwater: false);

			if (cover == 0 || cover < densityThreshold) continue;

			var screen = IsoCoordUtil.WorldToScreen(wx, wy, 0);
			GrassBladeEmitter.EmitTile(
				_bladeField, screen, wx, wy,
				worldSeed: _worldSeed,
				cover: cover,
				bladesPerTile: bladesPerTile,
				variantOverride: variantOverride);
		}

		_bladeField.EndFill();
	}

	private void MarkBladesDirty() => _bladesDirty = true;

	/// <summary>
	/// atlas 没生成 dirt region 时直接用代码画一个菱形兜底，保证画面有内容。
	/// </summary>
	private void DrawDirtFallbackDiamond(Vector2 center, Color color)
	{
		var top = center + new Vector2(0, -TileHalfH);
		var right = center + new Vector2(TileHalfW, 0);
		var bottom = center + new Vector2(0, TileHalfH);
		var left = center + new Vector2(-TileHalfW, 0);
		var pts = new[] { top, right, bottom, left };
		DrawColoredPolygon(pts, color);

		var edge = new Color(color.R * 0.6f, color.G * 0.6f, color.B * 0.6f);
		DrawLine(top, right, edge, 1f);
		DrawLine(right, bottom, edge, 1f);
		DrawLine(bottom, left, edge, 1f);
		DrawLine(left, top, edge, 1f);
	}

	private void EmitGrassFace(Rect2 region, Vector2 position, Color tint, long sortKey)
	{
		// overlay 紧跟同格 dirt 之后入队 → 排序后仍位于 dirt 之上一位 → painter 顺序正确
		_faces.Add(new DrawFace(region, position, tint, sortKey + 1));
	}

	private void DrawDebugFrame()
	{
		// 用网格四角的菱形中心连出菱形外框
		var half = GridSize / 2;
		var n = -half;
		var s = half - 1;
		var w = -half;
		var e = half - 1;

		var nwTop = IsoCoordUtil.WorldToScreen(w, n, 0);
		var neTop = IsoCoordUtil.WorldToScreen(e, n, 0);
		var swTop = IsoCoordUtil.WorldToScreen(w, s, 0);
		var seTop = IsoCoordUtil.WorldToScreen(e, s, 0);

		var frameColor = new Color(1f, 1f, 1f, 0.18f);
		DrawLine(nwTop, neTop, frameColor, 1.5f);
		DrawLine(neTop, seTop, frameColor, 1.5f);
		DrawLine(seTop, swTop, frameColor, 1.5f);
		DrawLine(swTop, nwTop, frameColor, 1.5f);

		var crossColor = new Color(1f, 0.85f, 0.4f, 0.55f);
		DrawLine(new Vector2(-12, 0), new Vector2(12, 0), crossColor, 1.5f);
		DrawLine(new Vector2(0, -8), new Vector2(0, 8), crossColor, 1.5f);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (HandleKey(@event)) return;
		if (HandleMouseButton(@event)) return;
		HandleMouseMotion(@event);
	}

	private bool HandleKey(InputEvent @event)
	{
		if (@event is not InputEventKey { Pressed: true, Echo: false } key)
			return false;

		var changed = false;

		switch (key.Keycode)
		{
			case Key.Key1:
				DebugModule.ForceGrassOverlay = GrassOverlayForceMode.Auto;
				MarkBladesDirty();
				changed = true;
				break;
			case Key.Key2:
				DebugModule.ForceGrassOverlay = GrassOverlayForceMode.On;
				MarkBladesDirty();
				changed = true;
				break;
			case Key.Key3:
				DebugModule.ForceGrassOverlay = GrassOverlayForceMode.Off;
				MarkBladesDirty();
				changed = true;
				break;

			case Key.Key4:
				DebugModule.GrassRenderMode = GrassRenderMode.Shader3D;
				MarkBladesDirty();
				changed = true;
				break;
			case Key.Key5:
				DebugModule.GrassRenderMode = GrassRenderMode.Legacy;
				changed = true;
				break;
			case Key.Key6:
				DebugModule.GrassRenderMode = GrassRenderMode.Off;
				MarkBladesDirty();
				changed = true;
				break;

			case Key.Bracketleft:
				DebugModule.GrassDensityThreshold = (byte)Math.Max(0, DebugModule.GrassDensityThreshold - 32);
				MarkBladesDirty();
				changed = true;
				break;
			case Key.Bracketright:
				DebugModule.GrassDensityThreshold = (byte)Math.Min(255, DebugModule.GrassDensityThreshold + 32);
				MarkBladesDirty();
				changed = true;
				break;

			case Key.Comma:
				DebugModule.GrassVariantOverride = WrapVariant(DebugModule.GrassVariantOverride - 1);
				MarkBladesDirty();
				changed = true;
				break;
			case Key.Period:
				DebugModule.GrassVariantOverride = WrapVariant(DebugModule.GrassVariantOverride + 1);
				MarkBladesDirty();
				changed = true;
				break;

			case Key.Minus:
				DebugModule.GrassBladesPerTile = Math.Max(0, DebugModule.GrassBladesPerTile - BladeStepKeyboard);
				MarkBladesDirty();
				changed = true;
				break;
			case Key.Equal:
				DebugModule.GrassBladesPerTile = Math.Min(MaxBladesPerTile, DebugModule.GrassBladesPerTile + BladeStepKeyboard);
				MarkBladesDirty();
				changed = true;
				break;

			case Key.Semicolon:
				DebugModule.GrassWindSpeed = Math.Max(0f, DebugModule.GrassWindSpeed - 0.4f);
				changed = true;
				break;
			case Key.Apostrophe:
				DebugModule.GrassWindSpeed = Math.Min(20f, DebugModule.GrassWindSpeed + 0.4f);
				changed = true;
				break;

			case Key.A:
				DebugModule.GrassBladeAo = !DebugModule.GrassBladeAo;
				if (_bladeField != null)
					_bladeField.AoEnabled = DebugModule.GrassBladeAo;
				MarkBladesDirty();
				changed = true;
				break;

			case Key.R:
				_worldSeed = unchecked(_worldSeed * 1664525 + 1013904223);
				MarkBladesDirty();
				changed = true;
				break;

			case Key.Z:
				ApplyZoom(ZoomStep, screenPivot: null);
				changed = true;
				break;
			case Key.X:
				ApplyZoom(1f / ZoomStep, screenPivot: null);
				changed = true;
				break;
			case Key.Home:
			case Key.Key0:
				ResetCamera();
				changed = true;
				break;

			case Key.F1:
				if (_debugPanel != null)
					_debugPanel.Visible = !_debugPanel.Visible;
				changed = true;
				break;
		}

		if (!changed) return false;

		QueueRedraw();
		_variantStrip?.QueueRedraw();
		RefreshStatusLabel();
		GetViewport().SetInputAsHandled();
		return true;
	}

	private bool HandleMouseButton(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mb) return false;

		switch (mb.ButtonIndex)
		{
			case MouseButton.WheelUp when mb.Pressed:
				ApplyZoom(ZoomStep, mb.Position);
				QueueRedraw();
				RefreshStatusLabel();
				GetViewport().SetInputAsHandled();
				return true;

			case MouseButton.WheelDown when mb.Pressed:
				ApplyZoom(1f / ZoomStep, mb.Position);
				QueueRedraw();
				RefreshStatusLabel();
				GetViewport().SetInputAsHandled();
				return true;

			case MouseButton.Right:
				_isDragging = mb.Pressed;
				_dragLastScreenPos = mb.Position;
				GetViewport().SetInputAsHandled();
				return true;

			case MouseButton.Middle when mb.Pressed:
				ResetCamera();
				QueueRedraw();
				RefreshStatusLabel();
				GetViewport().SetInputAsHandled();
				return true;
		}

		return false;
	}

	private void HandleMouseMotion(InputEvent @event)
	{
		if (!_isDragging) return;
		if (@event is not InputEventMouseMotion motion) return;
		if (_camera == null) return;

		// 屏幕位移转世界位移：除以 zoom（zoom 越大画面越放大，等量像素对应世界距离越短）
		var delta = motion.Position - _dragLastScreenPos;
		_dragLastScreenPos = motion.Position;
		_camera.Position -= delta / _camera.Zoom;
		RefreshStatusLabel();
	}

	private void ApplyZoom(float factor, Vector2? screenPivot)
	{
		if (_camera == null) return;

		var oldZoom = _camera.Zoom;
		var newZoom = (oldZoom * factor).Clamp(MinZoom, MaxZoom);
		if (newZoom == oldZoom) return;

		// 以鼠标所在位置为缩放锚点：保持光标下的世界点不动
		if (screenPivot is { } pivot)
		{
			var viewportSize = GetViewport().GetVisibleRect().Size;
			// 屏幕坐标 → 相对视口中心的偏移（相机看到的局部坐标）
			var pivotOffset = pivot - viewportSize * 0.5f;
			// 锚点在世界中的位置（旧 zoom）
			var worldPivot = _camera.Position + pivotOffset / oldZoom;
			// 在新 zoom 下让该世界点仍落在同样的屏幕位置
			_camera.Position = worldPivot - pivotOffset / newZoom;
		}

		_camera.Zoom = newZoom;
	}

	private void ResetCamera()
	{
		if (_camera == null) return;
		_camera.Position = Vector2.Zero;
		_camera.Zoom = new Vector2(1.6f, 1.6f);
	}

	private static int WrapVariant(int next)
	{
		const int variantCount = 6;
		if (next < -1) return variantCount - 1;
		if (next >= variantCount) return -1;
		return next;
	}

	// ──────────────────────────────────────────────────────────
	//  UI overlay
	// ──────────────────────────────────────────────────────────

	private void BuildOverlayUi()
	{
		var layer = new CanvasLayer { Name = "Overlay" };
		AddChild(layer);

		var title = new Label
		{
			Text = "草地表面层 预览",
			Position = new Vector2(28, 20),
		};
		title.AddThemeFontSizeOverride("font_size", 26);
		title.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.95f));
		layer.AddChild(title);

		var subtitle = new Label
		{
			Text = "真实跑通 GrassCoverSampler → GrassOverlayPass → TerrainAtlas 全链路（不依赖游戏世界 / 存档）",
			Position = new Vector2(28, 56),
		};
		subtitle.AddThemeFontSizeOverride("font_size", 14);
		subtitle.AddThemeColorOverride("font_color", new Color(0.75f, 0.78f, 0.82f));
		layer.AddChild(subtitle);

		_hintLabel = new Label
		{
			Position = new Vector2(28, 90),
			Text = "F1 调试面板    1/2/3 强制 Auto|On|Off    4/5/6 渲染 Shader3D|Legacy|Off    A 根部AO切换    [ / ] 阈值 ±32    , / . 变体    -/= blade/tile ±32    ;/' 风速 ±0.4    R 种子    Z/X/滚轮 缩放    右键拖动    中键/0 重置",
		};
		_hintLabel.AddThemeFontSizeOverride("font_size", 13);
		_hintLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.85f, 1f));
		layer.AddChild(_hintLabel);

		_statusLabel = new Label
		{
			Position = new Vector2(28, 118),
		};
		_statusLabel.AddThemeFontSizeOverride("font_size", 14);
		_statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.55f));
		layer.AddChild(_statusLabel);

		_diagLabel = new Label
		{
			Position = new Vector2(28, 138),
			Text = _diag,
			AutowrapMode = TextServer.AutowrapMode.Off,
		};
		_diagLabel.AddThemeFontSizeOverride("font_size", 12);
		_diagLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.75f, 0.65f));
		layer.AddChild(_diagLabel);

		// 6 个变体的 swatch 条（直接采样 atlas 子图，所见即所得）
		_variantStrip = new VariantStripCanvas(this)
		{
			Name = "VariantStrip",
			Position = new Vector2(28, 180),
		};
		layer.AddChild(_variantStrip);

		var stripCaption = new Label
		{
			Text = "6 个程序化变体（TerrainAtlas 第 3 行）— 用 , / . 切换；切到 -1 = 按坐标哈希自动分布",
			Position = new Vector2(28, 180 + 80),
		};
		stripCaption.AddThemeFontSizeOverride("font_size", 12);
		stripCaption.AddThemeColorOverride("font_color", new Color(0.6f, 0.7f, 0.78f));
		layer.AddChild(stripCaption);

		// 右上角调试面板：绑定到 bladeField，所有变更走 MarkBladesDirty / RefreshStatusLabel。
		if (_bladeField != null)
		{
			_debugPanel = new GrassDebugPanel(
				bladeField:       _bladeField,
				onRebuildBlades:  () => { MarkBladesDirty(); QueueRedraw(); },
				onSeedReroll:     () =>
				{
					_worldSeed = unchecked(_worldSeed * 1664525 + 1013904223);
					MarkBladesDirty();
					QueueRedraw();
					RefreshStatusLabel();
				},
				onStatusRefresh:  RefreshStatusLabel);
			layer.AddChild(_debugPanel);
		}

		RefreshStatusLabel();
	}

	private void RefreshStatusLabel()
	{
		if (_statusLabel == null) return;

		var forceText = DebugModule.ForceGrassOverlay switch
		{
			GrassOverlayForceMode.Auto => "自动",
			GrassOverlayForceMode.On => "全开",
			GrassOverlayForceMode.Off => "全关",
			_ => DebugModule.ForceGrassOverlay.ToString(),
		};

		var renderText = DebugModule.GrassRenderMode switch
		{
			GrassRenderMode.Shader3D => "Shader3D",
			GrassRenderMode.Legacy => "Legacy",
			GrassRenderMode.Off => "Off",
			_ => DebugModule.GrassRenderMode.ToString(),
		};

		var variant = DebugModule.GrassVariantOverride;
		var variantText = variant < 0 ? "哈希" : $"V{variant}";
		var zoomText = _camera != null ? $"{_camera.Zoom.X:0.00}x" : "-";
		var bladeCount = _bladeField?.VisibleBladeCount ?? 0;

		var aoText = DebugModule.GrassBladeAo ? "开" : "关";

		_statusLabel.Text =
			$"渲染 = {renderText}    强制 = {forceText}    AO = {aoText}    阈值 = {DebugModule.GrassDensityThreshold}    变体 = {variantText}    " +
			$"blade/tile = {DebugModule.GrassBladesPerTile}    blade 总数 = {bladeCount}    风速 = {DebugModule.GrassWindSpeed:0.0}Hz    " +
			$"种子 = {_worldSeed}    网格 = {GridSize}×{GridSize}    缩放 = {zoomText}";
	}

	// ──────────────────────────────────────────────────────────
	//  右上角变体条：直接 blit atlas 6 个 region，让用户看到原图
	// ──────────────────────────────────────────────────────────

	private sealed partial class VariantStripCanvas : Node2D
	{
		private readonly GrassSurfacePreview _owner;
		private const float Cell = 70f;
		private const float Gap = 8f;

		public VariantStripCanvas(GrassSurfacePreview owner)
		{
			_owner = owner;
		}

		public override void _Draw()
		{
			var atlas = _owner._atlas;
			if (atlas?.AtlasTexture == null) return;

			var selected = DebugModule.GrassVariantOverride;
			var variantCount = atlas.GrassOverlayVariantCount;

			for (var i = 0; i < variantCount; i++)
			{
				if (!atlas.TryGetGrassOverlayVariant(i, out var region))
					continue;

				var origin = new Vector2(i * (Cell + Gap), 0);
				var bgColor = new Color(0.12f, 0.16f, 0.18f, 0.85f);
				DrawRect(new Rect2(origin, new Vector2(Cell, Cell)), bgColor, filled: true);

				// 居中绘制 region 缩略图（保持原图比例 128x64）
				var thumbSize = new Vector2(Cell - 8f, (Cell - 8f) * 0.5f);
				var thumbPos = origin + new Vector2(4f, (Cell - thumbSize.Y) * 0.5f);
				DrawTextureRectRegion(atlas.AtlasTexture, new Rect2(thumbPos, thumbSize), region);

				// 选中框（VariantOverride 命中时高亮）
				var borderColor = (selected == i)
					? new Color(1f, 0.85f, 0.3f, 1f)
					: new Color(0.4f, 0.45f, 0.5f, 0.6f);
				var borderWidth = (selected == i) ? 2.5f : 1f;
				DrawRect(new Rect2(origin, new Vector2(Cell, Cell)), borderColor, filled: false, width: borderWidth);

				DrawString(
					ThemeDB.FallbackFont,
					origin + new Vector2(6f, Cell - 6f),
					$"V{i}",
					HorizontalAlignment.Left,
					-1f,
					12,
					new Color(0.9f, 0.9f, 0.9f));
			}
		}
	}
}
