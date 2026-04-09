using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;

namespace MiniRPG.Module.Render;

public class TileMapRenderModule
{
	private const string FallbackTile = "BLACK TILE";
	private const string MappingResPath = "tile_mapping.json";
	private const string IdMapResPath = "res://Tools/tile_name_to_id.json";
	private const string ItemWorldRenderResPath = "item_world_render.json";
	private const string FallbackFixtureTile = "Misc A4_N";
	private const string PeripheralFixtureTile = "Misc A4_N";
	private const string WeatherAssetRoot = "res://Assets/Art/Placeholders/weather";
	private const string WeatherScreenShaderPath = "res://Assets/Shaders/weather_screen_fx.gdshader";
	private const string WeatherRealtimeShaderPath = "res://Assets/Shaders/weather_realtime_fx.gdshader";
	private const string WeatherScreenFxOverlayName = "WeatherScreenFxOverlay";
	private static readonly string[] WeatherAssetIds =
	[
		"snow_cover",
		"sand_cover",
		"ice_gloss",
		"wet_gloss",
		"rain",
		"fog",
		"snow",
		"dust",
		"lightning",
	];
	private const float DefaultAnimatedTileFps = 10f;
	private const float DefaultCameraZoom = 1.0f;
	private const float DefaultMinCameraZoom = 0.6f;
	private const float DefaultMaxCameraZoom = 2.4f;
	private const float CameraZoomStep = 0.1f;
	private const float WeatherOverlayFootprintTiles = 1.0f;
	private static readonly Color MemoryTint = new(0.22f, 0.22f, 0.28f);
	private static readonly Color PeripheralTint = new(0.50f, 0.50f, 0.56f);
	private static readonly Color EditorHighlightTint = new(1.0f, 0.95f, 0.55f, 0.55f);
	private static readonly Vector2 DefaultTilePixelSize = new(64f, 64f);
	private static readonly Vector2 PlayerSpriteOffset = new(0, 64);
	private static readonly Vector2 EntitySpriteBaseOffset = new(0, 32);
	private static readonly Vector2 DefaultEntitySpriteScale = new(0.25f, 0.25f);
	private static readonly TileVisual InvalidTile = TileVisual.Static(-1, Vector2I.Zero);

	private readonly GameState _state;
	private readonly FogOfWarTracker _fogTracker;
	private readonly int _viewW;
	private readonly int _viewH;

	private TileMapLayer _groundLayer = null!;
	private TileMapLayer _memoryLayer = null!;
	private TileMapLayer _overlayLayer = null!;
	private TileMapLayer _memoryOverlayLayer = null!;
	private TileMapLayer _peripheralLayer = null!;
	private TileMapLayer _peripheralOverlayLayer = null!;
	private Node2D _weatherOverlayRoot = null!;
	private Node2D _peripheralWeatherOverlayRoot = null!;
	private Node2D _memoryWeatherOverlayRoot = null!;
	private Node2D _groundItemRoot = null!;
	private Node2D _peripheralGroundItemRoot = null!;
	private Node2D _weatherFxRoot = null!;
	private Node2D _peripheralWeatherFxRoot = null!;
	private Node2D _entitySpriteRoot = null!;
	private Node2D _peripheralEntitySpriteRoot = null!;
	private Node2D _combatFxWorldRoot = null!;
	private TileMapLayer _peripheralEntityLayer = null!;
	private TileMapLayer _entityLayer = null!;
	private TileMapLayer _fogLayer = null!;
	private TileMapLayer _editorHighlightBaseLayer = null!;
	private TileMapLayer _editorHighlightOverlayLayer = null!;
	private TileSet _tileSet = null!;

	private Camera2D? _camera;
	private IAnimatable? _playerAnim;
	private SubViewportContainer? _viewportContainer;
	private SubViewport? _subViewport;
	private ColorRect? _weatherScreenFxOverlay;
	private ShaderMaterial? _weatherScreenFxMaterial;
	private Shader? _weatherScreenFxShader;
	private WeatherScreenFxParams _weatherScreenFxTarget = WeatherScreenFxParams.Clear;
	private readonly WeatherScreenFxState _weatherScreenFxState = new();
	private WeatherScreenFxTuningSet _weatherScreenFxTuning = new();

	private readonly List<AnimatedTileBinding> _animatedTiles = [];
	private Dictionary<string, TileVisual> _nameToVisual = new(StringComparer.OrdinalIgnoreCase);
	private TileVisual _blackTile;
	private double _tileAnimationClockSeconds;
	private float _zoom = DefaultCameraZoom;
	private float _minCameraZoom = DefaultMinCameraZoom;
	private float _maxCameraZoom = DefaultMaxCameraZoom;

	private Dictionary<string, TerrainTileMapping> _terrainMap = new();
	private Dictionary<string, string> _entityMap = new();
	private Dictionary<string, string> _fixtureMap = new();
	private Dictionary<string, string> _itemMap = new();
	private ItemWorldRenderRegistry _itemWorldRegistry = ItemWorldRenderRegistry.Empty;
	private readonly Dictionary<string, Texture2D> _itemTextureCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Texture2D> _weatherTextureCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<Sprite2D> _weatherOverlaySprites = [];
	private readonly List<Sprite2D> _peripheralWeatherOverlaySprites = [];
	private readonly List<Sprite2D> _memoryWeatherOverlaySprites = [];
	private readonly List<Sprite2D> _groundItemSprites = [];
	private readonly List<Sprite2D> _peripheralGroundItemSprites = [];
	private readonly List<Sprite2D> _weatherFxSprites = [];
	private readonly List<Sprite2D> _peripheralWeatherFxSprites = [];
	private readonly List<Sprite2D> _entitySprites = [];
	private readonly List<Sprite2D> _peripheralEntitySprites = [];
	private Vector2 _tilePixelSize = DefaultTilePixelSize;
	private Texture2D? _weatherFxQuadTexture;
	private Shader? _weatherFxShader;
	private int _weatherOverlaySpriteCount;
	private int _peripheralWeatherOverlaySpriteCount;
	private int _memoryWeatherOverlaySpriteCount;
	private int _groundItemSpriteCount;
	private int _peripheralGroundItemSpriteCount;
	private int _weatherFxSpriteCount;
	private int _peripheralWeatherFxSpriteCount;
	private int _entitySpriteCount;
	private int _peripheralEntitySpriteCount;
	private int _tileDrawCommandCount;
	private RenderPerfSnapshot _lastPerfSnapshot = RenderPerfSnapshot.Empty;
	private double _frameTimeEwmaMs;
	private bool _hasFrameTimeEwma;

	private bool _editorViewActive;
	private int _viewCenterX;
	private int _viewCenterY;
	private int _viewCenterZ;
	private Vector2I? _editorHoverWorld;
	private Vector3I? _lastPlayerWorldPosition;

	// ── 等距体素渲染器 ──
	private IsometricVoxelRenderer? _voxelRenderer;
	private Node2D? _voxelRoot;
	private bool _isometricMode = true;

	public bool FogMapVisible { get; set; }
	public bool MinimapVisible { get; set; }
	public Vector3I? InspectWorldCell { get; set; }
	public float Zoom => _zoom;
	public Node2D CombatFxWorldRoot => _combatFxWorldRoot;
	public bool IsIsometricMode => _isometricMode;
	public Vector2I MapViewportSize => _subViewport?.Size ?? Vector2I.Zero;
	public Vector2 MapViewportContainerSize => _viewportContainer?.Size ?? Vector2.Zero;
	public RenderPerfSnapshot LastPerfSnapshot => _lastPerfSnapshot;

	public TileMapRenderModule(GameState state, FogOfWarTracker fogTracker, int viewW, int viewH)
	{
		_state = state;
		_fogTracker = fogTracker;
		_viewW = viewW;
		_viewH = viewH;
		_blackTile = InvalidTile;
	}

	public static IReadOnlyList<string> EnumerateWeatherAssetPaths() =>
		WeatherAssetIds
			.Select(static assetId => $"{WeatherAssetRoot}/{assetId}.png")
			.ToArray();

	public void Init(
		Node2D mapRoot,
		TileSet tileSet,
		SubViewportContainer? viewportContainer = null,
		SubViewport? subViewport = null,
		Node2D? playerVisual = null,
		Camera2D? camera = null)
	{
		LoadIdMap(IdMapResPath);
		LoadTileMapping(MappingResPath);
		LoadItemWorldRenderMapping(ItemWorldRenderResPath);
		_tileSet = tileSet;
		_tilePixelSize = ResolveTilePixelSize();
		_weatherFxQuadTexture = null;
		_weatherFxShader = null;
		_weatherScreenFxShader = ResolveWeatherScreenFxShader();
		_itemTextureCache.Clear();
		_weatherTextureCache.Clear();
		_weatherOverlaySprites.Clear();
		_peripheralWeatherOverlaySprites.Clear();
		_memoryWeatherOverlaySprites.Clear();
		_groundItemSprites.Clear();
		_peripheralGroundItemSprites.Clear();
		_weatherFxSprites.Clear();
		_peripheralWeatherFxSprites.Clear();
		_entitySprites.Clear();
		_peripheralEntitySprites.Clear();

		_groundLayer = MakeLayer(mapRoot, "GroundLayer", tileSet, 0);
		_memoryLayer = MakeLayer(mapRoot, "MemoryLayer", tileSet, 0);
		_overlayLayer = MakeLayer(mapRoot, "OverlayLayer", tileSet, 1);
		_memoryOverlayLayer = MakeLayer(mapRoot, "MemoryOverlayLayer", tileSet, 1);
		_peripheralLayer = MakeLayer(mapRoot, "PeripheralLayer", tileSet, 0);
		_peripheralOverlayLayer = MakeLayer(mapRoot, "PeripheralOverlayLayer", tileSet, 1);
		_weatherOverlayRoot = MakeSpriteRoot(mapRoot, "WeatherOverlayRoot", 1);
		_peripheralWeatherOverlayRoot = MakeSpriteRoot(mapRoot, "PeripheralWeatherOverlayRoot", 1);
		_memoryWeatherOverlayRoot = MakeSpriteRoot(mapRoot, "MemoryWeatherOverlayRoot", 1);
		_groundItemRoot = MakeSpriteRoot(mapRoot, "GroundItemRoot", 1);
		_peripheralGroundItemRoot = MakeSpriteRoot(mapRoot, "PeripheralGroundItemRoot", 1);
		_weatherFxRoot = MakeSpriteRoot(mapRoot, "WeatherFxRoot", 2);
		_peripheralWeatherFxRoot = MakeSpriteRoot(mapRoot, "PeripheralWeatherFxRoot", 2);
		_entitySpriteRoot = MakeSpriteRoot(mapRoot, "EntitySpriteRoot", 2);
		_peripheralEntitySpriteRoot = MakeSpriteRoot(mapRoot, "PeripheralEntitySpriteRoot", 2);
		_combatFxWorldRoot = MakeSpriteRoot(mapRoot, "CombatFxWorldRoot", 6);
		_peripheralEntityLayer = MakeLayer(mapRoot, "PeripheralEntityLayer", tileSet, 2);
		_entityLayer = MakeLayer(mapRoot, "EntityLayer", tileSet, 2);
		_fogLayer = MakeLayer(mapRoot, "FogLayer", tileSet, 3);
		_editorHighlightBaseLayer = MakeLayer(mapRoot, "EditorHighlightBaseLayer", tileSet, 4);
		_editorHighlightOverlayLayer = MakeLayer(mapRoot, "EditorHighlightOverlayLayer", tileSet, 5);

		_memoryLayer.Modulate = MemoryTint;
		_memoryOverlayLayer.Modulate = MemoryTint;
		_peripheralLayer.Modulate = PeripheralTint;
		_peripheralOverlayLayer.Modulate = PeripheralTint;
		_memoryWeatherOverlayRoot.Modulate = MemoryTint;
		_peripheralWeatherOverlayRoot.Modulate = PeripheralTint;
		_peripheralGroundItemRoot.Modulate = PeripheralTint;
		_peripheralWeatherFxRoot.Modulate = PeripheralTint;
		_peripheralEntitySpriteRoot.Modulate = PeripheralTint;
		_peripheralEntityLayer.Modulate = PeripheralTint;
		_editorHighlightBaseLayer.Modulate = EditorHighlightTint;
		_editorHighlightOverlayLayer.Modulate = EditorHighlightTint;

		if (playerVisual != null)
		{
			playerVisual.Visible = false;
			_playerAnim = playerVisual as IAnimatable ?? new TileAnimatable(playerVisual);
			ResAccess.RegisterAnimatable("player", _playerAnim);
			if (!string.IsNullOrWhiteSpace(_state.PlayerId))
				ResAccess.RegisterAnimatable(_state.PlayerId, _playerAnim);
		}

		_viewportContainer = viewportContainer;
		_subViewport = subViewport;
		_camera = camera;
		_weatherScreenFxOverlay = EnsureWeatherScreenFxOverlay(viewportContainer);
		_weatherScreenFxMaterial = EnsureWeatherScreenFxMaterial(_weatherScreenFxOverlay);
		RefreshWeatherScreenFxTarget();
		UpdateWeatherScreenFxOverlay(0d);
		UpdateCamera();

		// 初始化等距体素渲染器
		_voxelRoot = new Node2D { Name = "VoxelRoot", Visible = false };
		mapRoot.AddChild(_voxelRoot);
		_voxelRenderer = new IsometricVoxelRenderer(_state, _fogTracker, _viewW, _viewH);
		_voxelRenderer.Init(_voxelRoot, tileSet, camera, this);

		// 纯等距 2.5D 启动状态：显示体素层，隐藏旧 TileMap 层。
		_isometricMode = true;
		_voxelRoot.Visible = true;
		SetTileMapLayersVisible(false);
	}

	public void SetEditorView(bool active, int centerX, int centerY, int centerZ, Vector2I? hoverWorld = null)
	{
		_editorViewActive = active;
		_viewCenterX = centerX;
		_viewCenterY = centerY;
		_viewCenterZ = centerZ;
		_editorHoverWorld = active ? hoverWorld : null;
	}

	internal void SetWeatherScreenFxTuning(WeatherScreenFxTuningSet? tuning)
	{
		_weatherScreenFxTuning = tuning ?? new WeatherScreenFxTuningSet();
		RefreshWeatherScreenFxTarget();
		UpdateWeatherScreenFxOverlay(0d);
	}

	public bool TryGetWorldCellFromGlobalPosition(Vector2 globalPos, out Vector3I worldCell)
	{
		worldCell = Vector3I.Zero;
		if (_viewportContainer == null || _subViewport == null || _camera == null)
			return false;

		var rect = _viewportContainer.GetGlobalRect();
		if (!rect.HasPoint(globalPos) || rect.Size.X <= 0 || rect.Size.Y <= 0)
			return false;

		var localInContainer = globalPos - rect.Position;
		var viewportPos = new Vector2(
			localInContainer.X * _subViewport.Size.X / rect.Size.X,
			localInContainer.Y * _subViewport.Size.Y / rect.Size.Y);
		var viewportSize = new Vector2(_subViewport.Size.X, _subViewport.Size.Y);
		var screenOffset = viewportPos - viewportSize / 2f;
		var mapLocal = _camera.Position + new Vector2(
			screenOffset.X / _camera.Zoom.X,
			screenOffset.Y / _camera.Zoom.Y);

		if (_isometricMode)
		{
			// 等距模式：逆变换 + Z 射线扫描
			return TryPickIsometricCell(mapLocal, out worldCell);
		}

		var cell = _groundLayer.LocalToMap(mapLocal);
		if (cell.X < 0 || cell.X >= _viewW || cell.Y < 0 || cell.Y >= _viewH)
			return false;

		var centerX = _editorViewActive ? _viewCenterX : _state.PlayerX;
		var centerY = _editorViewActive ? _viewCenterY : _state.PlayerY;
		var centerZ = _editorViewActive ? _viewCenterZ : _state.PlayerZ;
		worldCell = new Vector3I(
			centerX - _viewW / 2 + cell.X,
			centerY - _viewH / 2 + cell.Y,
			centerZ);
		return true;
	}

	/// <summary>
	/// 等距模式下的点击拾取：从最高 Z 向下扫描，找到第一个实心方块。
	/// </summary>
	private bool TryPickIsometricCell(Vector2 screenPos, out Vector3I worldCell)
	{
		worldCell = Vector3I.Zero;
		if (_state.World == null) return false;

		var cz = _editorViewActive ? _viewCenterZ : _state.PlayerZ;
		var zMin = cz - 4;
		var zMax = cz + 2;

		// 从最高层（Z 最小）向下扫描
		for (var z = zMin; z <= zMax; z++)
		{
			var (wx, wy) = IsoCoordUtil.ScreenToWorldCell(screenPos, z);
			var terrain = _state.World.GetTerrain(wx, wy, z);
			if (terrain.StringId != Terrains.Air && terrain.StringId != Terrains.Void)
			{
				worldCell = new Vector3I(wx, wy, z);
				return true;
			}
		}

		return false;
	}

	public bool IsWorldCellVisible(int wx, int wy, int wz)
	{
		if (_state.World == null || wz != _viewCenterZ)
			return false;

		if (!TryGetScreenCell(wx, wy, wz, out _))
			return false;

		var band = _fogTracker.GetVisionBand(wx, wy, wz);
		return band is PlayerVisionBand.Focused or PlayerVisionBand.Peripheral;
	}

	public bool TryGetWorldEffectPosition(int wx, int wy, int wz, out Vector2 position)
	{
		position = Vector2.Zero;
		if (!TryGetScreenCell(wx, wy, wz, out var cell))
			return false;

		position = _groundLayer.MapToLocal(cell);
		return true;
	}

	public bool TryGetWorldOverlayPosition(int wx, int wy, int wz, out Vector2 position)
	{
		position = Vector2.Zero;
		if (!TryGetWorldEffectPosition(wx, wy, wz, out var mapPosition))
			return false;

		return TryMapToOverlayPosition(mapPosition, out position);
	}

	public bool StepZoom(int direction)
	{
		if (_camera == null || direction == 0)
			return false;

		var requestedZoom = Mathf.Clamp(_zoom + direction * CameraZoomStep, _minCameraZoom, _maxCameraZoom);
		if (Mathf.IsEqualApprox(requestedZoom, _zoom))
			return false;

		_zoom = requestedZoom;
		UpdateCamera();
		return true;
	}

	public void SetZoomRange(float minZoom, float maxZoom)
	{
		var normalizedMin = Mathf.Clamp(minZoom, 0.2f, 4.0f);
		var normalizedMax = Mathf.Clamp(maxZoom, 0.2f, 4.0f);
		if (normalizedMin > normalizedMax)
			(normalizedMin, normalizedMax) = (normalizedMax, normalizedMin);

		_minCameraZoom = normalizedMin;
		_maxCameraZoom = normalizedMax;
		_zoom = Mathf.Clamp(_zoom, _minCameraZoom, _maxCameraZoom);
		UpdateCamera();
	}

	public bool SetZoomTo(float zoom)
	{
		if (_camera == null)
			return false;

		var clamped = Mathf.Clamp(zoom, _minCameraZoom, _maxCameraZoom);
		if (Mathf.IsEqualApprox(clamped, _zoom))
			return false;

		_zoom = clamped;
		UpdateCamera();
		return true;
	}

	public void Flush()
	{
		var frameStartUsec = Time.GetTicksUsec();
		_tileDrawCommandCount = 0;

		// 等距模式：委托给体素渲染器
		if (_isometricMode && _voxelRenderer != null)
		{
			_animatedTiles.Clear();
			ClearLayers();
			BeginGroundItemFrame();
			BeginWeatherFrame();
			BeginEntitySpriteFrame();
			HidePlayerVisual();
			_lastPlayerWorldPosition = null;

			if (_state.World == null)
			{
				_weatherScreenFxTarget = WeatherScreenFxParams.Clear;
				UpdateWeatherScreenFxOverlay(0d);
				EndEntitySpriteFrame();
				EndWeatherFrame();
				EndGroundItemFrame();
				CommitPerfFrame((Time.GetTicksUsec() - frameStartUsec) / 1000.0);
				return;
			}

			if (_editorViewActive)
			{
				_weatherScreenFxTarget = WeatherScreenFxParams.Clear;
			}
			else
			{
				_fogTracker.Update(_state);
				RefreshWeatherScreenFxTarget();
				_viewCenterX = _state.PlayerX;
				_viewCenterY = _state.PlayerY;
				_viewCenterZ = _state.PlayerZ;
			}

			_voxelRenderer.Render();
			_tileDrawCommandCount = _voxelRenderer.LastDrawCommandCount;
			UpdateWeatherScreenFxOverlay(0d);
			EndEntitySpriteFrame();
			EndWeatherFrame();
			EndGroundItemFrame();
			CommitPerfFrame((Time.GetTicksUsec() - frameStartUsec) / 1000.0);
			return;
		}

		_animatedTiles.Clear();
		ClearLayers();
		BeginGroundItemFrame();
		BeginWeatherFrame();
		BeginEntitySpriteFrame();

		if (_state.World == null)
		{
			_weatherScreenFxTarget = WeatherScreenFxParams.Clear;
			UpdateWeatherScreenFxOverlay(0d);
			HidePlayerVisual();
			_lastPlayerWorldPosition = null;
			EndEntitySpriteFrame();
			EndWeatherFrame();
			EndGroundItemFrame();
			CommitPerfFrame((Time.GetTicksUsec() - frameStartUsec) / 1000.0);
			return;
		}

		if (_editorViewActive)
		{
			_weatherScreenFxTarget = WeatherScreenFxParams.Clear;
			UpdateWeatherScreenFxOverlay(0d);
			FlushEditor();
			EndEntitySpriteFrame();
			EndWeatherFrame();
			EndGroundItemFrame();
			CommitPerfFrame((Time.GetTicksUsec() - frameStartUsec) / 1000.0);
			return;
		}

		_fogTracker.Update(_state);

		var cx = _state.PlayerX;
		var cy = _state.PlayerY;
		var cz = _state.PlayerZ;
		RefreshWeatherScreenFxTarget();
		_viewCenterX = cx;
		_viewCenterY = cy;
		_viewCenterZ = cz;

		var halfW = _viewW / 2;
		var halfH = _viewH / 2;

		for (var sy = 0; sy < _viewH; sy++)
		{
			for (var sx = 0; sx < _viewW; sx++)
			{
				var wx = cx - halfW + sx;
				var wy = cy - halfH + sy;
				var cell = new Vector2I(sx, sy);

				var visionBand = _fogTracker.GetVisionBand(wx, wy, cz);
				if (visionBand == PlayerVisionBand.Focused)
				{
					RenderTerrainCell(cell, wx, wy, cz, visionBand);

					var isPlayerCell = _playerAnim != null && wx == cx && wy == cy;
					RenderEntityCell(_entityLayer, cell, wx, wy, cz, peripheral: false, skipPlayer: isPlayerCell);
					RenderGroundItems(cell, wx, wy, cz, peripheral: false);
				}
				else if (visionBand == PlayerVisionBand.Peripheral)
				{
					RenderTerrainCell(cell, wx, wy, cz, visionBand);
					RenderEntityCell(_peripheralEntityLayer, cell, wx, wy, cz, peripheral: true, skipPlayer: false);
					RenderGroundItems(cell, wx, wy, cz, peripheral: true);
				}
				else if (visionBand == PlayerVisionBand.Memory)
				{
					RenderTerrainCell(cell, wx, wy, cz, visionBand);
				}
				else
				{
					ApplyTileVisual(_fogLayer, cell, _blackTile);
				}
			}
		}

		DrawInspectHighlight(cx, cy, cz);
		UpdateCamera();
		UpdatePlayerVisual(cx, cy, cz);
		UpdateWeatherScreenFxOverlay(0d);
		EndEntitySpriteFrame();
		EndWeatherFrame();
		EndGroundItemFrame();
		CommitPerfFrame((Time.GetTicksUsec() - frameStartUsec) / 1000.0);
	}

	public void AdvanceAnimations(double delta)
	{
		if (delta <= 0d)
			return;

		_tileAnimationClockSeconds += delta;
		UpdateWeatherScreenFxOverlay(delta);
		if (_animatedTiles.Count == 0)
			return;

		foreach (var binding in _animatedTiles)
		{
			var frameIndex = binding.Visual.GetFrameIndexAtTime(_tileAnimationClockSeconds);
			if (frameIndex == binding.FrameIndex)
				continue;

			binding.FrameIndex = frameIndex;
			var frame = binding.Visual.GetFrame(frameIndex);
			binding.Layer.SetCell(binding.Cell, frame.SourceId, frame.Coord);
		}
	}

	public string? ToggleRenderMode()
	{
		// 项目已切换为纯等距 2.5D：不再允许在运行时切换回 2D。
		if (_voxelRoot != null)
			_voxelRoot.Visible = true;
		SetTileMapLayersVisible(false);
		_isometricMode = true;
		return LocalizationService.T("render.view_mode.iso_only");
	}

	public string ToggleMinimap()
	{
		MinimapVisible = !MinimapVisible;
		return MinimapVisible
			? LocalizationService.T("render.minimap.unimplemented")
			: LocalizationService.T("render.minimap.closed");
	}

	public string ToggleFogMap()
	{
		FogMapVisible = !FogMapVisible;
		return FogMapVisible
			? LocalizationService.T("render.fog_map.unimplemented")
			: LocalizationService.T("render.fog_map.closed");
	}

	public void CenterFogMap() { }

	public void ScrollFogMap(int dx, int dy) { }

	public void ResetOverlays()
	{
		FogMapVisible = false;
		MinimapVisible = false;
	}

	public void PlaySpineAnim(string animName, bool loop, int track = 0)
		=> _playerAnim?.Play(animName, loop);

	public void QueueSpineAnim(string animName, bool loop, float delay = 0f, int track = 0)
	{
	}

	public void PlayOneShotThenIdle(string animName)
		=> _playerAnim?.PlayOneShot(animName, "Idle");

	public IAnimatable? PlayerAnimatable => _playerAnim;

	private void FlushEditor()
	{
		var cx = _viewCenterX;
		var cy = _viewCenterY;
		var cz = _viewCenterZ;
		var halfW = _viewW / 2;
		var halfH = _viewH / 2;

		for (var sy = 0; sy < _viewH; sy++)
		{
			for (var sx = 0; sx < _viewW; sx++)
			{
				var wx = cx - halfW + sx;
				var wy = cy - halfH + sy;
				var cell = new Vector2I(sx, sy);
				var terrain = _state.World!.GetTerrain(wx, wy, cz);
				SetTerrain(cell, terrain, _groundLayer, _overlayLayer);

				var isPlayerCell = _playerAnim != null
					&& wx == _state.PlayerX
					&& wy == _state.PlayerY
					&& cz == _state.PlayerZ;
				RenderEntityCell(_entityLayer, cell, wx, wy, cz, peripheral: false, skipPlayer: isPlayerCell);
				RenderGroundItems(cell, wx, wy, cz, peripheral: false);
			}
		}

		if (_editorHoverWorld is { } hover)
		{
			var sx = hover.X - (cx - halfW);
			var sy = hover.Y - (cy - halfH);
			if (sx >= 0 && sx < _viewW && sy >= 0 && sy < _viewH)
			{
				var cell = new Vector2I(sx, sy);
				var terrain = _state.World!.GetTerrain(hover.X, hover.Y, cz);
				SetTerrain(cell, terrain, _editorHighlightBaseLayer, _editorHighlightOverlayLayer);
			}
		}

		UpdateCamera();
		UpdatePlayerVisual(cx, cy, cz);
	}

	private void ClearLayers()
	{
		_groundLayer.Clear();
		_memoryLayer.Clear();
		_overlayLayer.Clear();
		_memoryOverlayLayer.Clear();
		_peripheralLayer.Clear();
		_peripheralOverlayLayer.Clear();
		_peripheralEntityLayer.Clear();
		_entityLayer.Clear();
		_fogLayer.Clear();
		_editorHighlightBaseLayer.Clear();
		_editorHighlightOverlayLayer.Clear();
	}

	private void SetTileMapLayersVisible(bool visible)
	{
		_groundLayer.Visible = visible;
		_memoryLayer.Visible = visible;
		_overlayLayer.Visible = visible;
		_memoryOverlayLayer.Visible = visible;
		_peripheralLayer.Visible = visible;
		_peripheralOverlayLayer.Visible = visible;
		_peripheralEntityLayer.Visible = visible;
		_entityLayer.Visible = visible;
		_fogLayer.Visible = visible;
		_editorHighlightBaseLayer.Visible = visible;
		_editorHighlightOverlayLayer.Visible = visible;
		_weatherOverlayRoot.Visible = visible;
		_peripheralWeatherOverlayRoot.Visible = visible;
		_memoryWeatherOverlayRoot.Visible = visible;
		_groundItemRoot.Visible = visible;
		_peripheralGroundItemRoot.Visible = visible;
		_weatherFxRoot.Visible = visible;
		_peripheralWeatherFxRoot.Visible = visible;
		_entitySpriteRoot.Visible = visible;
		_peripheralEntitySpriteRoot.Visible = visible;
	}

	private void DrawInspectHighlight(int centerWorldX, int centerWorldY, int centerWorldZ)
	{
		if (InspectWorldCell is not { } inspectCell
			|| inspectCell.Z != centerWorldZ
			|| _state.World == null)
		{
			return;
		}

		var halfW = _viewW / 2;
		var halfH = _viewH / 2;
		var sx = inspectCell.X - (centerWorldX - halfW);
		var sy = inspectCell.Y - (centerWorldY - halfH);
		if (sx < 0 || sx >= _viewW || sy < 0 || sy >= _viewH)
			return;

		var cell = new Vector2I(sx, sy);
		var band = _fogTracker.GetVisionBand(inspectCell.X, inspectCell.Y, inspectCell.Z);
		if (band == PlayerVisionBand.Unknown)
		{
			ApplyTileVisual(_editorHighlightBaseLayer, cell, _blackTile);
			return;
		}

		var terrain = _state.World.GetTerrain(inspectCell.X, inspectCell.Y, inspectCell.Z);
		SetTerrain(cell, terrain, _editorHighlightBaseLayer, _editorHighlightOverlayLayer);
	}

	private void UpdateCamera()
	{
		if (_camera == null)
			return;

		_camera.Position = _groundLayer.MapToLocal(new Vector2I(_viewW / 2, _viewH / 2));
		_camera.Zoom = Vector2.One * _zoom;
	}

	private void UpdatePlayerVisual(int centerWorldX, int centerWorldY, int centerWorldZ)
	{
		if (_playerAnim == null)
			return;

		var player = ActorModule.GetPlayer(_state);
		if (player == null || player.Z != centerWorldZ)
		{
			HidePlayerVisual();
			_lastPlayerWorldPosition = null;
			return;
		}

		var halfW = _viewW / 2;
		var halfH = _viewH / 2;
		var sx = player.X - (centerWorldX - halfW);
		var sy = player.Y - (centerWorldY - halfH);
		if (sx < 0 || sx >= _viewW || sy < 0 || sy >= _viewH)
		{
			HidePlayerVisual();
			return;
		}

		if (_playerAnim.Node is FantasyCharacterAnimatable sheetAnim
			&& _lastPlayerWorldPosition is { } lastPlayerWorldPosition
			&& lastPlayerWorldPosition.Z == player.Z)
		{
			var dx = player.X - lastPlayerWorldPosition.X;
			var dy = player.Y - lastPlayerWorldPosition.Y;
			if (dx != 0 || dy != 0)
				sheetAnim.SetMovementDirection(dx, dy);
		}

		var localPos = _groundLayer.MapToLocal(new Vector2I(sx, sy));
		var node = _playerAnim.Node;
		if (!node.Visible)
		{
			node.Visible = true;
			_playerAnim.Play("Idle");
		}

		node.Position = localPos + PlayerSpriteOffset;
		_lastPlayerWorldPosition = new Vector3I(player.X, player.Y, player.Z);
	}

	private void HidePlayerVisual()
	{
		if (_playerAnim?.Node != null)
			_playerAnim.Node.Visible = false;
	}

	private bool TryGetScreenCell(int wx, int wy, int wz, out Vector2I cell)
	{
		cell = Vector2I.Zero;
		if (_state.World == null || wz != _viewCenterZ)
			return false;

		var halfW = _viewW / 2;
		var halfH = _viewH / 2;
		var sx = wx - (_viewCenterX - halfW);
		var sy = wy - (_viewCenterY - halfH);
		if (sx < 0 || sx >= _viewW || sy < 0 || sy >= _viewH)
			return false;

		cell = new Vector2I(sx, sy);
		return true;
	}

	private bool TryMapToOverlayPosition(Vector2 mapPosition, out Vector2 overlayPosition)
	{
		overlayPosition = Vector2.Zero;
		if (_camera == null || _subViewport == null || _viewportContainer == null)
			return false;

		var viewportSize = new Vector2(_subViewport.Size.X, _subViewport.Size.Y);
		if (viewportSize.X <= 0f || viewportSize.Y <= 0f)
			return false;

		var zoom = _camera.Zoom;
		var viewportPosition = viewportSize / 2f + new Vector2(
			(mapPosition.X - _camera.Position.X) * zoom.X,
			(mapPosition.Y - _camera.Position.Y) * zoom.Y);
		var containerSize = _viewportContainer.Size;
		if (containerSize.X <= 0f || containerSize.Y <= 0f)
			return false;

		overlayPosition = new Vector2(
			viewportPosition.X * containerSize.X / viewportSize.X,
			viewportPosition.Y * containerSize.Y / viewportSize.Y);
		return true;
	}

	private void BeginGroundItemFrame()
	{
		_groundItemSpriteCount = 0;
		_peripheralGroundItemSpriteCount = 0;
	}

	private void EndGroundItemFrame()
	{
		HideUnusedGroundItemSprites(_groundItemSprites, _groundItemSpriteCount);
		HideUnusedGroundItemSprites(_peripheralGroundItemSprites, _peripheralGroundItemSpriteCount);
	}

	private void BeginWeatherFrame()
	{
		_weatherOverlaySpriteCount = 0;
		_peripheralWeatherOverlaySpriteCount = 0;
		_memoryWeatherOverlaySpriteCount = 0;
		_weatherFxSpriteCount = 0;
		_peripheralWeatherFxSpriteCount = 0;
	}

	private void EndWeatherFrame()
	{
		HideUnusedGroundItemSprites(_weatherOverlaySprites, _weatherOverlaySpriteCount);
		HideUnusedGroundItemSprites(_peripheralWeatherOverlaySprites, _peripheralWeatherOverlaySpriteCount);
		HideUnusedGroundItemSprites(_memoryWeatherOverlaySprites, _memoryWeatherOverlaySpriteCount);
		HideUnusedGroundItemSprites(_weatherFxSprites, _weatherFxSpriteCount);
		HideUnusedGroundItemSprites(_peripheralWeatherFxSprites, _peripheralWeatherFxSpriteCount);
	}

	private void BeginEntitySpriteFrame()
	{
		_entitySpriteCount = 0;
		_peripheralEntitySpriteCount = 0;
	}

	private void EndEntitySpriteFrame()
	{
		HideUnusedGroundItemSprites(_entitySprites, _entitySpriteCount);
		HideUnusedGroundItemSprites(_peripheralEntitySprites, _peripheralEntitySpriteCount);
	}

	private void RenderEntityCell(
		TileMapLayer layer,
		Vector2I cell,
		int wx,
		int wy,
		int cz,
		bool peripheral,
		bool skipPlayer)
	{
		ApplyTileVisual(layer, cell, ResolveEntityTileVisual(cell, wx, wy, cz, peripheral, skipPlayer));
	}

	private TileVisual ResolveEntityTileVisual(
		Vector2I cell,
		int wx,
		int wy,
		int cz,
		bool peripheral,
		bool skipPlayer)
	{
		if (!skipPlayer)
		{
			var player = ActorModule.GetPlayer(_state);
			if (player is { X: var px, Y: var py, Z: var pz } && px == wx && py == wy && pz == cz)
				return Resolve(_entityMap.GetValueOrDefault("player", FallbackTile));
		}

		var actor = FindVisibleNonPlayerActor(wx, wy, cz);
		if (actor != null)
		{
			if (TryRenderActorSprite(cell, actor, peripheral))
				return InvalidTile;

			var key = actor.Faction == Factions.Hostile ? "hostile" : "friendly";
			return Resolve(_entityMap.GetValueOrDefault(key, FallbackTile));
		}

		var hazard = _state.World!.GetFirstEntity(wx, wy, cz, CellEntityType.Hazard);
		if (hazard != null)
			return Resolve(_entityMap.GetValueOrDefault(hazard.EntityId, FallbackTile));

		var fixture = _state.World!.GetFirstEntity(wx, wy, cz, CellEntityType.Fixture);
		if (fixture != null)
			return Resolve(peripheral ? PeripheralFixtureTile : _fixtureMap.GetValueOrDefault(fixture.EntityId, FallbackFixtureTile));

		var container = _state.World.GetFirstEntity(wx, wy, cz, CellEntityType.Container);
		if (container != null)
			return Resolve(_itemMap.GetValueOrDefault("container", FallbackTile));

		return InvalidTile;
	}

	private Actor? FindVisibleNonPlayerActor(int wx, int wy, int cz)
	{
		foreach (var actor in _state.World!.GetActorsAt(wx, wy, cz, _state.Actors))
		{
			if (actor.Id != _state.PlayerId)
				return actor;
		}

		return null;
	}

	private bool TryRenderActorSprite(Vector2I cell, Actor actor, bool peripheral)
	{
		var entry = ResolveActorTextureEntry(actor);
		if (entry?.TexturePath is not { Length: > 0 } texturePath)
			return false;

		var texture = ResAccess.Get<Texture2D>(texturePath);
		if (texture == null)
			return false;

		var sprite = AcquireEntitySprite(peripheral);
		var region = ResolveEntitySpriteRegion(entry, actor, texture);
		ConfigureEntitySprite(
			sprite,
			texture,
			region,
			ResolveEntitySpriteScale(entry.Scale),
			_groundLayer.MapToLocal(cell) + EntitySpriteBaseOffset + ResolveEntitySpriteOffset(entry.Offset));
		return true;
	}

	private static Vector2 ResolveEntitySpriteScale(float[]? scale)
	{
		if (scale is [var x, var y] && x > 0f && y > 0f)
			return new Vector2(x, y);

		return DefaultEntitySpriteScale;
	}

	private static Vector2 ResolveEntitySpriteOffset(float[]? offset)
	{
		if (offset is [var x, var y])
			return new Vector2(x, y);

		return Vector2.Zero;
	}

	private static Rect2? ResolveEntitySpriteRegion(ResAccess.RenderEntry entry, Actor actor, Texture2D texture)
	{
		if (!entry.UseFacing)
			return null;

		var textureWidth = (int)texture.GetWidth();
		var textureHeight = (int)texture.GetHeight();
		if (textureWidth <= 0 || textureHeight <= 0)
			return null;

		var frameWidth = entry.FrameWidth > 0 ? Math.Min(entry.FrameWidth, textureWidth) : textureWidth;
		var inferredFrameHeight = textureHeight % 8 == 0 ? textureHeight / 8 : textureHeight;
		var frameHeight = entry.FrameHeight > 0 ? Math.Min(entry.FrameHeight, textureHeight) : inferredFrameHeight;
		if (frameWidth <= 0 || frameHeight <= 0)
			return null;

		var rowCount = Math.Max(1, textureHeight / frameHeight);
		var row = Math.Clamp(
			DirectionalSpriteHelper.ResolveDirectionRow(actor.FacingX, actor.FacingY),
			0,
			rowCount - 1);
		return new Rect2(0, row * frameHeight, frameWidth, frameHeight);
	}

	private ResAccess.RenderEntry? ResolveActorTextureEntry(Actor actor)
	{
		var direct = ResAccess.GetEntry(actor.Id);
		if (IsTextureEntry(direct))
			return direct;

		if (actor.Faction == Factions.Hostile && actor.Race?.Id is { Length: > 0 } raceId)
		{
			var byRace = ResAccess.GetEntry(raceId);
			if (IsTextureEntry(byRace))
				return byRace;
		}

		return null;
	}

	private static bool IsTextureEntry(ResAccess.RenderEntry? entry) =>
		entry != null
		&& string.Equals(entry.Type, "texture", StringComparison.OrdinalIgnoreCase)
		&& !string.IsNullOrWhiteSpace(entry.TexturePath);

	private Sprite2D AcquireEntitySprite(bool peripheral)
	{
		var pool = peripheral ? _peripheralEntitySprites : _entitySprites;
		var root = peripheral ? _peripheralEntitySpriteRoot : _entitySpriteRoot;
		var index = peripheral ? _peripheralEntitySpriteCount++ : _entitySpriteCount++;
		if (index >= pool.Count)
		{
			var sprite = new Sprite2D
			{
				Name = peripheral
					? $"PeripheralEntitySprite{pool.Count}"
					: $"EntitySprite{pool.Count}",
				Centered = false,
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			};
			root.AddChild(sprite);
			pool.Add(sprite);
		}

		return pool[index];
	}

	private static void ConfigureEntitySprite(
		Sprite2D sprite,
		Texture2D texture,
		Rect2? region,
		Vector2 scale,
		Vector2 basePosition)
	{
		sprite.Texture = texture;
		if (region is { } resolvedRegion)
		{
			sprite.RegionEnabled = true;
			sprite.RegionRect = resolvedRegion;
		}
		else
		{
			sprite.RegionEnabled = false;
		}

		var size = region?.Size ?? texture.GetSize();
		sprite.Scale = scale;
		sprite.Position = basePosition + new Vector2(
			-(size.X * scale.X) / 2f,
			-(size.Y * scale.Y));
		sprite.Visible = true;
	}

	private void RenderGroundItems(Vector2I cell, int wx, int wy, int cz, bool peripheral)
	{
		var items = _state.World!.GetEntitiesByType(wx, wy, cz, CellEntityType.Item);
		if (items.Count == 0)
			return;

		var visibleCount = GroundItemStackLayout.GetVisibleCount(items.Count);
		if (visibleCount == 0)
			return;

		var startIndex = items.Count - visibleCount;
		var basePosition = _groundLayer.MapToLocal(cell) + GroundItemStackLayout.BaseOffset;
		for (var visibleIndex = 0; visibleIndex < visibleCount; visibleIndex++)
		{
			var entity = items[startIndex + visibleIndex];
			var visual = ResolveGroundItemVisual(ResolveGroundItemSpec(entity));
			if (visual.Texture == null)
				continue;

			var sprite = AcquireGroundItemSprite(peripheral);
			ConfigureGroundItemSprite(
				sprite,
				visual.Texture,
				visual.Scale,
				basePosition + GroundItemStackLayout.GetOffset(visibleIndex));
		}
	}

	private ItemWorldVisualSpec ResolveGroundItemSpec(CellEntity entity)
	{
		var templateId = WorldMap.ResolveGroundItemTemplateId(entity);
		string? category = null;
		if (entity.Meta != null && entity.Meta.TryGetValue("category", out var metaCategory))
			category = metaCategory;
		else if (PresetDB.Items.TryGetValue(templateId, out var preset))
			category = preset.Category;

		if (_itemWorldRegistry.TryResolve(templateId, category, out var resolved))
			return resolved;

		return new ItemWorldVisualSpec(
			ItemWorldRenderKind.Tile,
			_itemMap.GetValueOrDefault("drop", FallbackTile),
			GroundItemStackLayout.DefaultScale);
	}

	private GroundItemSpriteVisual ResolveGroundItemVisual(ItemWorldVisualSpec spec) =>
		new(ResolveGroundItemTexture(spec), spec.Scale);

	private Texture2D? ResolveGroundItemTexture(ItemWorldVisualSpec spec)
	{
		var cacheKey = $"{spec.Kind}:{spec.Value}";
		if (_itemTextureCache.TryGetValue(cacheKey, out var cached))
			return cached;

		Texture2D? texture = spec.Kind switch
		{
			ItemWorldRenderKind.Tile => BuildTileTexture(spec.Value),
			ItemWorldRenderKind.Texture => GD.Load<Texture2D>(spec.Value),
			_ => null,
		};
		if (texture != null)
			_itemTextureCache[cacheKey] = texture;

		return texture;
	}

	private Texture2D? BuildTileTexture(string tileName)
	{
		if (!_nameToVisual.TryGetValue(tileName, out var visual) || visual.SourceId < 0)
			return null;

		var frame = visual.GetFrame(0);
		if (_tileSet.GetSource(frame.SourceId) is not TileSetAtlasSource atlasSource || atlasSource.Texture == null)
			return null;

		return new AtlasTexture
		{
			Atlas = atlasSource.Texture,
			Region = new Rect2(
				frame.Coord.X * atlasSource.TextureRegionSize.X,
				frame.Coord.Y * atlasSource.TextureRegionSize.Y,
				atlasSource.TextureRegionSize.X,
				atlasSource.TextureRegionSize.Y),
		};
	}

	/// <summary>
	/// 根据地形 stringId 获取对应的 TileSet 贴图。
	/// 供 IsometricVoxelRenderer 使用。
	/// </summary>
	public Texture2D? BuildTerrainTexture(string terrainStringId)
	{
		if (!_terrainMap.TryGetValue(terrainStringId, out var mapping))
			return null;
		return BuildTileTexture(mapping.Base);
	}

	private Sprite2D AcquireGroundItemSprite(bool peripheral)
	{
		var pool = peripheral ? _peripheralGroundItemSprites : _groundItemSprites;
		var root = peripheral ? _peripheralGroundItemRoot : _groundItemRoot;
		var index = peripheral ? _peripheralGroundItemSpriteCount++ : _groundItemSpriteCount++;
		if (index >= pool.Count)
		{
			var sprite = new Sprite2D
			{
				Name = peripheral
					? $"PeripheralGroundItemSprite{pool.Count}"
					: $"GroundItemSprite{pool.Count}",
				Centered = false,
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			};
			root.AddChild(sprite);
			pool.Add(sprite);
		}

		return pool[index];
	}

	private static void ConfigureGroundItemSprite(
		Sprite2D sprite,
		Texture2D texture,
		Vector2 scale,
		Vector2 basePosition)
	{
		var size = texture.GetSize();
		sprite.Texture = texture;
		sprite.Scale = scale;
		sprite.Position = basePosition + new Vector2(
			-(size.X * scale.X) / 2f,
			-(size.Y * scale.Y));
		sprite.Visible = true;
	}

	private static void HideUnusedGroundItemSprites(IReadOnlyList<Sprite2D> sprites, int activeCount)
	{
		for (var i = activeCount; i < sprites.Count; i++)
			sprites[i].Visible = false;
	}

	private void RenderTerrainCell(Vector2I cell, int wx, int wy, int wz, PlayerVisionBand band)
	{
		var surface = WeatherSurface.GetSurfaceState(_state, wx, wy, wz);
		switch (band)
		{
			case PlayerVisionBand.Focused:
				SetTerrain(cell, surface.BaseTerrain, _groundLayer, _overlayLayer);
				break;
			case PlayerVisionBand.Peripheral:
				SetTerrain(cell, surface.BaseTerrain, _peripheralLayer, _peripheralOverlayLayer);
				break;
			case PlayerVisionBand.Memory:
				SetTerrain(cell, surface.BaseTerrain, _memoryLayer, _memoryOverlayLayer);
				break;
			default:
				return;
		}

	}

	private void RenderWeatherFx(Vector2I cell, int wx, int wy, int wz, WeatherSurfaceState surface, PlayerVisionBand band)
	{
		if (wz != 0 || !surface.IsExposed || band is PlayerVisionBand.Memory or PlayerVisionBand.Unknown)
			return;

		var sample = WeatherRules.GetLocalWeather(_state, wx, wy, wz);
		if (!sample.HasActiveWeather)
			return;

		var hash = ComputeWeatherVisualHash(wx, wy, wz, _state.Turn, (int)sample.Type, (int)sample.Intensity);
		if (WeatherFxVisualResolver.ResolveWeatherFxVisual(sample, band, hash) is { } weatherFx
			&& !TryRenderWeatherShaderFx(cell, band, weatherFx))
		{
			var assetId = ResolveLegacyWeatherFxAssetId(sample.Type);
			if (!string.IsNullOrEmpty(assetId))
			{
				RenderWeatherTextureSprite(
					cell,
					assetId,
					band,
					overlay: false,
					weatherFx.FootprintTiles,
					new Color(weatherFx.Tint.R, weatherFx.Tint.G, weatherFx.Tint.B, weatherFx.Alpha));
			}
		}

		if (WeatherFxVisualResolver.ResolveLightningFxVisual(sample, band, hash) is { } lightningFx
			&& !TryRenderWeatherShaderFx(cell, band, lightningFx))
		{
			RenderWeatherTextureSprite(
				cell,
				"lightning",
				band,
				overlay: false,
				lightningFx.FootprintTiles,
				new Color(lightningFx.Tint.R, lightningFx.Tint.G, lightningFx.Tint.B, lightningFx.Alpha));
		}
	}

	private void RenderWeatherTextureSprite(
		Vector2I cell,
		string assetId,
		PlayerVisionBand band,
		bool overlay,
		float footprintTiles,
		Color modulate)
	{
		var texture = ResolveWeatherTexture(assetId);
		if (texture == null)
			return;

		var sprite = AcquireWeatherSprite(band, overlay);
		ConfigureWeatherTextureSprite(sprite, texture, cell, footprintTiles, modulate);
	}

	private bool TryRenderWeatherShaderFx(Vector2I cell, PlayerVisionBand band, WeatherFxVisualParams visual)
	{
		if (_weatherFxShader == null || _weatherFxQuadTexture == null)
			return false;

		var sprite = AcquireWeatherSprite(band, overlay: false);
		ConfigureWeatherFxSprite(sprite, cell, visual);
		return true;
	}

	private static string ResolveLegacyWeatherFxAssetId(WeatherType type) => type switch
	{
		WeatherType.Rain => "rain",
		WeatherType.Fog => "fog",
		WeatherType.Snow => "snow",
		WeatherType.Storm => "rain",
		WeatherType.Thunderstorm => "rain",
		WeatherType.Sandstorm => "dust",
		_ => string.Empty,
	};

	private void ConfigureWeatherTextureSprite(
		Sprite2D sprite,
		Texture2D texture,
		Vector2I cell,
		float footprintTiles,
		Color modulate)
	{
		sprite.Material = null;
		sprite.Texture = texture;
		sprite.RegionEnabled = false;
		sprite.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
		sprite.Scale = ResolveWeatherTextureScale(texture, footprintTiles);
		sprite.Position = _groundLayer.MapToLocal(cell);
		sprite.Modulate = modulate;
		sprite.Visible = true;
	}

	private void ConfigureWeatherFxSprite(Sprite2D sprite, Vector2I cell, WeatherFxVisualParams visual)
	{
		if (_weatherFxQuadTexture == null || EnsureWeatherFxMaterial(sprite) is not { } material)
			return;

		sprite.Texture = _weatherFxQuadTexture;
		sprite.RegionEnabled = false;
		sprite.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
		sprite.Scale = _tilePixelSize * visual.FootprintTiles;
		sprite.Position = _groundLayer.MapToLocal(cell);
		sprite.Modulate = Colors.White;
		sprite.Visible = true;

		material.SetShaderParameter("mode", (int)visual.Mode);
		material.SetShaderParameter("intensity", visual.Intensity);
		material.SetShaderParameter("band_strength", visual.BandStrength);
		material.SetShaderParameter("time", (float)_tileAnimationClockSeconds);
		material.SetShaderParameter("seed", visual.Seed);
		material.SetShaderParameter("wind_dir", visual.WindDirection);
		material.SetShaderParameter("coverage_alpha", visual.CoverageAlpha * visual.Alpha);
		material.SetShaderParameter("tint", visual.Tint);
		material.SetShaderParameter("density", visual.Density);
		material.SetShaderParameter("speed", visual.Speed);
	}

	private Texture2D? ResolveWeatherTexture(string assetId)
	{
		if (_weatherTextureCache.TryGetValue(assetId, out var cached))
			return cached;

		var texture = ResAccess.Get<Texture2D>($"{WeatherAssetRoot}/{assetId}.png");
		if (texture != null)
			_weatherTextureCache[assetId] = texture;
		return texture;
	}

	private Vector2 ResolveWeatherTextureScale(Texture2D texture, float footprintTiles)
	{
		var size = texture.GetSize();
		if (size.X <= 0f || size.Y <= 0f)
			return Vector2.One;

		return new Vector2(
			(_tilePixelSize.X * footprintTiles) / size.X,
			(_tilePixelSize.Y * footprintTiles) / size.Y);
	}

	private ShaderMaterial? EnsureWeatherFxMaterial(Sprite2D sprite)
	{
		if (_weatherFxShader == null)
			return null;

		if (sprite.Material is ShaderMaterial material)
		{
			if (material.Shader != _weatherFxShader)
				material.Shader = _weatherFxShader;
			return material;
		}

		var created = new ShaderMaterial
		{
			Shader = _weatherFxShader,
		};
		sprite.Material = created;
		return created;
	}

	private Shader? ResolveWeatherFxShader() =>
		ResAccess.Get<Shader>(WeatherRealtimeShaderPath);

	private Shader? ResolveWeatherScreenFxShader() =>
		ResAccess.Get<Shader>(WeatherScreenShaderPath);

	private static Texture2D ResolveWeatherQuadTexture()
	{
		var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
		image.SetPixel(0, 0, Colors.White);
		return ImageTexture.CreateFromImage(image);
	}

	private ColorRect? EnsureWeatherScreenFxOverlay(SubViewportContainer? viewportContainer)
	{
		if (viewportContainer == null)
			return null;

		var existingNode = viewportContainer.GetNodeOrNull<Node>(WeatherScreenFxOverlayName);
		if (existingNode is ColorRect existingOverlay)
		{
			ConfigureWeatherScreenFxOverlay(existingOverlay);
			return existingOverlay;
		}

		existingNode?.QueueFree();
		var overlay = new ColorRect
		{
			Name = WeatherScreenFxOverlayName,
			Color = Colors.White,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false,
			ZIndex = 32,
		};
		ConfigureWeatherScreenFxOverlay(overlay);
		viewportContainer.AddChild(overlay);
		return overlay;
	}

	private static void ConfigureWeatherScreenFxOverlay(ColorRect overlay)
	{
		overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		overlay.OffsetLeft = 0f;
		overlay.OffsetTop = 0f;
		overlay.OffsetRight = 0f;
		overlay.OffsetBottom = 0f;
		overlay.MouseFilter = Control.MouseFilterEnum.Ignore;
	}

	private ShaderMaterial? EnsureWeatherScreenFxMaterial(ColorRect? overlay)
	{
		if (overlay == null || _weatherScreenFxShader == null)
			return null;

		if (overlay.Material is ShaderMaterial material)
		{
			if (material.Shader != _weatherScreenFxShader)
				material.Shader = _weatherScreenFxShader;
			return material;
		}

		var created = new ShaderMaterial
		{
			Shader = _weatherScreenFxShader,
		};
		overlay.Material = created;
		return created;
	}

	private void RefreshWeatherScreenFxTarget()
	{
		if (_state.World == null || _editorViewActive)
		{
			_weatherScreenFxTarget = WeatherScreenFxParams.Clear;
			return;
		}

		var surface = WeatherSurface.GetSurfaceState(_state, _state.PlayerX, _state.PlayerY, _state.PlayerZ);
		var canShowScreenFx = _state.PlayerZ == 0 && surface.IsExposed;
		if (!canShowScreenFx)
		{
			_weatherScreenFxTarget = WeatherScreenFxParams.Clear;
			return;
		}

		var sample = WeatherRules.GetLocalWeather(_state, _state.PlayerX, _state.PlayerY, _state.PlayerZ);
		var hash = ComputeWeatherVisualHash(
			_state.WorldSeed,
			_state.PlayerX,
			_state.PlayerY,
			_state.PlayerZ,
			_state.Turn,
			(int)sample.Type,
			(int)sample.Intensity);
		_weatherScreenFxTarget = WeatherScreenFxResolver.ApplyTuning(
			WeatherScreenFxResolver.Resolve(sample, isActive: true, hash),
			_weatherScreenFxTuning);
	}

	private void UpdateWeatherScreenFxOverlay(double deltaSeconds)
	{
		if (_weatherScreenFxOverlay == null || _weatherScreenFxMaterial == null)
			return;

		_weatherScreenFxState.AdvanceTo(_weatherScreenFxTarget, deltaSeconds);
		var primary = _weatherScreenFxState.Primary;
		var secondary = _weatherScreenFxState.Secondary;
		_weatherScreenFxOverlay.Visible = _weatherScreenFxState.HasVisibleFx;
		if (!_weatherScreenFxOverlay.Visible)
			return;

		var viewportSize = _viewportContainer?.Size ?? Vector2.Zero;
		_weatherScreenFxMaterial.SetShaderParameter("time", (float)_tileAnimationClockSeconds);
		_weatherScreenFxMaterial.SetShaderParameter("viewport_size", viewportSize);
		ApplyWeatherScreenFxParams("a", primary);
		ApplyWeatherScreenFxParams("b", secondary);
		ApplyWeatherScreenFxTuningProfile(_weatherScreenFxTuning.ResolveBlendedProfile(
			primary.Mode,
			secondary.Mode,
			_weatherScreenFxState.Blend));
		_weatherScreenFxMaterial.SetShaderParameter("blend_factor", _weatherScreenFxState.Blend);
	}

	private void ApplyWeatherScreenFxParams(string suffix, WeatherScreenFxParams value)
	{
		if (_weatherScreenFxMaterial == null)
			return;

		_weatherScreenFxMaterial.SetShaderParameter($"mode_{suffix}", (int)value.Mode);
		_weatherScreenFxMaterial.SetShaderParameter($"overlay_alpha_{suffix}", value.OverlayAlpha);
		_weatherScreenFxMaterial.SetShaderParameter($"fog_alpha_{suffix}", value.FogAlpha);
		_weatherScreenFxMaterial.SetShaderParameter($"edge_tint_alpha_{suffix}", value.EdgeTintAlpha);
		_weatherScreenFxMaterial.SetShaderParameter($"edge_shadow_{suffix}", value.EdgeShadow);
		_weatherScreenFxMaterial.SetShaderParameter($"density_{suffix}", value.Density);
		_weatherScreenFxMaterial.SetShaderParameter($"speed_{suffix}", value.Speed);
		_weatherScreenFxMaterial.SetShaderParameter($"lightning_flash_{suffix}", value.LightningFlash);
		_weatherScreenFxMaterial.SetShaderParameter($"temperature_bias_{suffix}", value.TemperatureBias);
		_weatherScreenFxMaterial.SetShaderParameter($"seed_{suffix}", value.Seed);
		_weatherScreenFxMaterial.SetShaderParameter($"wind_dir_{suffix}", value.WindDirection);
		_weatherScreenFxMaterial.SetShaderParameter($"tint_{suffix}", value.Tint);
	}

	private void ApplyWeatherScreenFxTuningProfile(WeatherScreenFxTuningProfile value)
	{
		if (_weatherScreenFxMaterial == null)
			return;

		_weatherScreenFxMaterial.SetShaderParameter("particle_size_scale", value.ParticleSizeScale);
		_weatherScreenFxMaterial.SetShaderParameter("particle_frequency_scale", value.ParticleFrequencyScale);
		_weatherScreenFxMaterial.SetShaderParameter("particle_blend_scale", value.ParticleBlendScale);
		_weatherScreenFxMaterial.SetShaderParameter("tint_strength_scale", value.TintStrengthScale);
	}

	private Vector2 ResolveTilePixelSize()
	{
		if (_blackTile.SourceId >= 0
			&& _tileSet.GetSource(_blackTile.SourceId) is TileSetAtlasSource atlasSource
			&& atlasSource.TextureRegionSize.X > 0
			&& atlasSource.TextureRegionSize.Y > 0)
		{
			return new Vector2(atlasSource.TextureRegionSize.X, atlasSource.TextureRegionSize.Y);
		}

		return DefaultTilePixelSize;
	}

	private Sprite2D AcquireWeatherSprite(PlayerVisionBand band, bool overlay)
	{
		return (band, overlay) switch
		{
			(PlayerVisionBand.Focused, true) => AcquireSprite(_weatherOverlaySprites, _weatherOverlayRoot, "WeatherOverlaySprite", ref _weatherOverlaySpriteCount),
			(PlayerVisionBand.Peripheral, true) => AcquireSprite(_peripheralWeatherOverlaySprites, _peripheralWeatherOverlayRoot, "PeripheralWeatherOverlaySprite", ref _peripheralWeatherOverlaySpriteCount),
			(PlayerVisionBand.Memory, true) => AcquireSprite(_memoryWeatherOverlaySprites, _memoryWeatherOverlayRoot, "MemoryWeatherOverlaySprite", ref _memoryWeatherOverlaySpriteCount),
			(PlayerVisionBand.Peripheral, false) => AcquireSprite(_peripheralWeatherFxSprites, _peripheralWeatherFxRoot, "PeripheralWeatherFxSprite", ref _peripheralWeatherFxSpriteCount),
			_ => AcquireSprite(_weatherFxSprites, _weatherFxRoot, "WeatherFxSprite", ref _weatherFxSpriteCount),
		};
	}

	private static Sprite2D AcquireSprite(List<Sprite2D> pool, Node2D root, string namePrefix, ref int count)
	{
		var index = count++;
		if (index >= pool.Count)
		{
			var sprite = new Sprite2D
			{
				Name = $"{namePrefix}{pool.Count}",
				Centered = true,
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			};
			root.AddChild(sprite);
			pool.Add(sprite);
		}

		return pool[index];
	}

	private static int ComputeWeatherVisualHash(params int[] values)
	{
		unchecked
		{
			var hash = 19;
			foreach (var value in values)
				hash = (hash * 397) ^ value;
			return hash & int.MaxValue;
		}
	}

	private static TileMapLayer MakeLayer(Node2D parent, string name, TileSet tileSet, int zIndex)
	{
		var layer = new TileMapLayer
		{
			Name = name,
			TileSet = tileSet,
			ZIndex = zIndex,
			SortByTextureId = true,
		};
		parent.AddChild(layer);
		return layer;
	}

	private static Node2D MakeSpriteRoot(Node2D parent, string name, int zIndex)
	{
		parent.GetNodeOrNull<Node2D>(name)?.QueueFree();
		var root = new Node2D
		{
			Name = name,
			ZIndex = zIndex,
		};
		parent.AddChild(root);
		return root;
	}

	private void ApplyTileVisual(TileMapLayer layer, Vector2I cell, TileVisual visual)
	{
		if (visual.SourceId < 0)
			return;

		_tileDrawCommandCount++;

		var frameIndex = visual.GetFrameIndexAtTime(_tileAnimationClockSeconds);
		var frame = visual.GetFrame(frameIndex);
		layer.SetCell(cell, frame.SourceId, frame.Coord);
		if (visual.IsAnimated)
		{
			_animatedTiles.Add(new AnimatedTileBinding
			{
				Layer = layer,
				Cell = cell,
				Visual = visual,
				FrameIndex = frameIndex,
			});
		}
	}

	private void SetTerrain(Vector2I cell, TerrainDef terrain, TileMapLayer baseLayer, TileMapLayer overlayLayer)
	{
		var terrainLoc = TerrainLoc(terrain);
		ApplyTileVisual(baseLayer, cell, terrainLoc.Base);
		ApplyTileVisual(overlayLayer, cell, terrainLoc.Overlay);
	}

	private TerrainTileVisual TerrainLoc(TerrainDef terrain)
	{
		if (_terrainMap.TryGetValue(terrain.StringId, out var tileNames))
			return new TerrainTileVisual(Resolve(tileNames.Base), ResolveOptional(tileNames.Overlay));

		return new TerrainTileVisual(_blackTile, InvalidTile);
	}

	private TileVisual EntityLocSkipPlayer(int wx, int wy, int cz)
	{
		var hazard = _state.World!.GetFirstEntity(wx, wy, cz, CellEntityType.Hazard);
		if (hazard != null)
			return Resolve(_entityMap.GetValueOrDefault(hazard.EntityId, FallbackTile));

		var fixture = _state.World!.GetFirstEntity(wx, wy, cz, CellEntityType.Fixture);
		if (fixture != null)
			return Resolve(_fixtureMap.GetValueOrDefault(fixture.EntityId, FallbackFixtureTile));

		var container = _state.World.GetFirstEntity(wx, wy, cz, CellEntityType.Container);
		if (container != null)
			return Resolve(_itemMap.GetValueOrDefault("container", FallbackTile));

		return InvalidTile;
	}

	private TileVisual EntityLoc(int wx, int wy, int cz)
	{
		var player = ActorModule.GetPlayer(_state);
		if (player is { X: var px, Y: var py, Z: var pz } && px == wx && py == wy && pz == cz)
			return Resolve(_entityMap.GetValueOrDefault("player", FallbackTile));

		foreach (var actor in _state.World!.GetActorsAt(wx, wy, cz, _state.Actors))
		{
			if (actor.Id == _state.PlayerId) continue;

			var key = actor.Faction == Factions.Hostile ? "hostile" : "friendly";
			return Resolve(_entityMap.GetValueOrDefault(key, FallbackTile));
		}

		var hazard = _state.World!.GetFirstEntity(wx, wy, cz, CellEntityType.Hazard);
		if (hazard != null)
			return Resolve(_entityMap.GetValueOrDefault(hazard.EntityId, FallbackTile));

		var fixture = _state.World!.GetFirstEntity(wx, wy, cz, CellEntityType.Fixture);
		if (fixture != null)
			return Resolve(_fixtureMap.GetValueOrDefault(fixture.EntityId, FallbackFixtureTile));

		var container = _state.World.GetFirstEntity(wx, wy, cz, CellEntityType.Container);
		if (container != null)
			return Resolve(_itemMap.GetValueOrDefault("container", FallbackTile));

		return InvalidTile;
	}

	private TileVisual PeripheralEntityLoc(int wx, int wy, int cz)
	{
		foreach (var actor in _state.World!.GetActorsAt(wx, wy, cz, _state.Actors))
		{
			if (actor.Id == _state.PlayerId) continue;

			var key = actor.Faction == Factions.Hostile ? "hostile" : "friendly";
			return Resolve(_entityMap.GetValueOrDefault(key, FallbackTile));
		}

		var hazard = _state.World!.GetFirstEntity(wx, wy, cz, CellEntityType.Hazard);
		if (hazard != null)
			return Resolve(_entityMap.GetValueOrDefault(hazard.EntityId, FallbackTile));

		var fixture = _state.World!.GetFirstEntity(wx, wy, cz, CellEntityType.Fixture);
		if (fixture != null)
			return Resolve(PeripheralFixtureTile);

		var container = _state.World.GetFirstEntity(wx, wy, cz, CellEntityType.Container);
		if (container != null)
			return Resolve(_itemMap.GetValueOrDefault("container", FallbackTile));

		return InvalidTile;
	}

	private TileVisual Resolve(string name) =>
		_nameToVisual.TryGetValue(name, out var visual) ? visual : _blackTile;

	private TileVisual ResolveOptional(string? name)
	{
		if (string.IsNullOrEmpty(name))
			return InvalidTile;

		return _nameToVisual.TryGetValue(name, out var visual) ? visual : InvalidTile;
	}

	private void LoadIdMap(string resPath)
	{
		_nameToVisual.Clear();

		if (!Godot.FileAccess.FileExists(resPath))
		{
			GD.PrintErr($"[TileMapRender] Missing tile id map: {resPath}");
			return;
		}

		var json = Godot.FileAccess.GetFileAsString(resPath);
		var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
		var map = JsonSerializer.Deserialize<Dictionary<string, TileSourceEntry>>(json, options);
		if (map == null)
		{
			GD.PrintErr("[TileMapRender] Failed to parse tile id map.");
			return;
		}

		foreach (var (name, entry) in map)
		{
			var frameLocs = ResolveAnimatedFrames(entry);
			_nameToVisual[name] = entry.IsAnimated && frameLocs.Length > 1
				? TileVisual.Animated(frameLocs, entry.Fps)
				: TileVisual.Static(entry.SourceId, new Vector2I(entry.AtlasX, entry.AtlasY));
		}

		_blackTile = _nameToVisual.GetValueOrDefault(FallbackTile, InvalidTile);
	}

	internal static TileLoc[] ResolveAnimatedFrames(TileSourceEntry entry)
	{
		if (entry.FrameCoords is { Count: > 0 })
		{
			return entry.FrameCoords
				.Select(coord => new TileLoc(entry.SourceId, new Vector2I(coord.AtlasX, coord.AtlasY)))
				.ToArray();
		}

		return entry.FrameSourceIds?
			.Where(static sourceId => sourceId >= 0)
			.Select(static sourceId => new TileLoc(sourceId, Vector2I.Zero))
			.ToArray()
			?? [];
	}

	private void LoadTileMapping(string resPath)
	{
		if (!GameDataLocator.TryReadText(resPath, out var json, out var sourceLabel))
		{
			GD.PrintErr($"[TileMapRender] Missing tile mapping config: {sourceLabel}");
			return;
		}

		var root = JsonSerializer.Deserialize<TileMappingConfig>(json);
		if (root == null)
		{
			GD.PrintErr("[TileMapRender] Failed to parse tile mapping config.");
			return;
		}

		_terrainMap = root.Terrain ?? new();
		_entityMap = root.Entity ?? new();
		_fixtureMap = root.Fixture ?? new();
		_itemMap = root.Item ?? new();
	}

	private void LoadItemWorldRenderMapping(string resPath)
	{
		_itemWorldRegistry = ItemWorldRenderRegistry.Empty;
		if (!GameDataLocator.TryReadText(resPath, out var json, out var sourceLabel))
		{
			GD.PushWarning($"[TileMapRender] Missing item world render config: {sourceLabel}");
			return;
		}

		try
		{
			_itemWorldRegistry = ItemWorldRenderRegistry.FromJson(json);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[TileMapRender] Failed to parse item world render config: {ex.Message}");
		}
	}

	internal readonly record struct TileLoc(int SourceId, Vector2I Coord);

	private readonly record struct TerrainTileVisual(TileVisual Base, TileVisual Overlay);
	private readonly record struct GroundItemSpriteVisual(Texture2D? Texture, Vector2 Scale);

	private void CommitPerfFrame(double frameTimeMs)
	{
		if (!_hasFrameTimeEwma)
		{
			_frameTimeEwmaMs = frameTimeMs;
			_hasFrameTimeEwma = true;
		}
		else
		{
			const double alpha = 0.10;
			_frameTimeEwmaMs = (_frameTimeEwmaMs * (1.0 - alpha)) + (frameTimeMs * alpha);
		}

		var activeSpriteCount = _weatherOverlaySpriteCount
			+ _peripheralWeatherOverlaySpriteCount
			+ _memoryWeatherOverlaySpriteCount
			+ _groundItemSpriteCount
			+ _peripheralGroundItemSpriteCount
			+ _weatherFxSpriteCount
			+ _peripheralWeatherFxSpriteCount
			+ _entitySpriteCount
			+ _peripheralEntitySpriteCount
			+ (_isometricMode && _voxelRenderer != null ? _voxelRenderer.LastSpriteCount : 0);

		_lastPerfSnapshot = new RenderPerfSnapshot(
			activeSpriteCount,
			_tileDrawCommandCount,
			_frameTimeEwmaMs);
	}

	public readonly record struct RenderPerfSnapshot(
		int ActiveSpriteCount,
		int DrawCommandCount,
		double FrameTimeAvgMs)
	{
		public static RenderPerfSnapshot Empty => new(0, 0, 0d);
	}

	private sealed class TileVisual
	{
		public int SourceId { get; init; } = -1;
		public Vector2I Coord { get; init; } = Vector2I.Zero;
		public float Fps { get; init; }
		public TileLoc[] Frames { get; init; } = [];

		public bool IsAnimated => Frames.Length > 1;

		public static TileVisual Static(int sourceId, Vector2I coord) => new()
		{
			SourceId = sourceId,
			Coord = coord,
		};

		public static TileVisual Animated(IEnumerable<TileLoc> frameLocs, float fps)
		{
			var frames = frameLocs
				.Where(static loc => loc.SourceId >= 0)
				.ToArray();
			return frames.Length switch
			{
				0 => Static(-1, Vector2I.Zero),
				1 => Static(frames[0].SourceId, frames[0].Coord),
				_ => new TileVisual
				{
					SourceId = frames[0].SourceId,
					Coord = frames[0].Coord,
					Fps = fps > 0f ? fps : DefaultAnimatedTileFps,
					Frames = frames,
				},
			};
		}

		public int GetFrameIndexAtTime(double seconds)
		{
			if (!IsAnimated)
				return 0;

			var frame = (int)Math.Floor(seconds * (Fps > 0f ? Fps : DefaultAnimatedTileFps));
			if (frame < 0)
				frame = 0;
			return frame % Frames.Length;
		}

		public TileLoc GetFrame(int frameIndex)
		{
			if (!IsAnimated)
				return new TileLoc(SourceId, Coord);

			var clampedIndex = Math.Clamp(frameIndex, 0, Frames.Length - 1);
			return Frames[clampedIndex];
		}
	}

	private sealed class AnimatedTileBinding
	{
		public required TileMapLayer Layer { get; init; }
		public required Vector2I Cell { get; init; }
		public required TileVisual Visual { get; init; }
		public int FrameIndex { get; set; }
	}

	[JsonConverter(typeof(TerrainTileMappingConverter))]
	private readonly record struct TerrainTileMapping(string Base, string? Overlay);

	internal sealed class TileSourceEntry
	{
		[JsonPropertyName("source_id")] public int SourceId { get; set; }
		[JsonPropertyName("atlas_x")] public int AtlasX { get; set; }
		[JsonPropertyName("atlas_y")] public int AtlasY { get; set; }
		[JsonPropertyName("is_animated")] public bool IsAnimated { get; set; }
		[JsonPropertyName("fps")] public float Fps { get; set; }
		[JsonPropertyName("frame_source_ids")] public List<int>? FrameSourceIds { get; set; }
		[JsonPropertyName("frame_coords")] public List<TileFrameCoordEntry>? FrameCoords { get; set; }
	}

	internal sealed class TileFrameCoordEntry
	{
		[JsonPropertyName("atlas_x")] public int AtlasX { get; set; }
		[JsonPropertyName("atlas_y")] public int AtlasY { get; set; }
	}

	private sealed class TerrainTileMappingConverter : JsonConverter<TerrainTileMapping>
	{
		public override TerrainTileMapping Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType == JsonTokenType.String)
			{
				var baseTile = reader.GetString();
				if (string.IsNullOrEmpty(baseTile))
					throw new JsonException("Terrain mapping string cannot be empty.");
				return new TerrainTileMapping(baseTile, null);
			}

			if (reader.TokenType != JsonTokenType.StartObject)
				throw new JsonException("Terrain mapping must be a string or object.");

			string? baseTileName = null;
			string? overlayTileName = null;

			while (reader.Read())
			{
				if (reader.TokenType == JsonTokenType.EndObject)
					break;

				if (reader.TokenType != JsonTokenType.PropertyName)
					throw new JsonException("Invalid terrain mapping property.");

				var propertyName = reader.GetString();
				if (!reader.Read())
					throw new JsonException("Unexpected end of terrain mapping.");

				switch (propertyName)
				{
					case "base":
						if (reader.TokenType != JsonTokenType.String)
							throw new JsonException("Terrain mapping 'base' must be a string.");
						baseTileName = reader.GetString();
						break;
					case "overlay":
						if (reader.TokenType == JsonTokenType.Null)
						{
							overlayTileName = null;
							break;
						}
						if (reader.TokenType != JsonTokenType.String)
							throw new JsonException("Terrain mapping 'overlay' must be a string.");
						overlayTileName = reader.GetString();
						break;
					default:
						using (JsonDocument.ParseValue(ref reader)) { }
						break;
				}
			}

			if (string.IsNullOrEmpty(baseTileName))
				throw new JsonException("Terrain mapping object requires a 'base' tile.");

			return new TerrainTileMapping(baseTileName, string.IsNullOrEmpty(overlayTileName) ? null : overlayTileName);
		}

		public override void Write(Utf8JsonWriter writer, TerrainTileMapping value, JsonSerializerOptions options)
		{
			if (string.IsNullOrEmpty(value.Overlay))
			{
				writer.WriteStringValue(value.Base);
				return;
			}

			writer.WriteStartObject();
			writer.WriteString("base", value.Base);
			writer.WriteString("overlay", value.Overlay);
			writer.WriteEndObject();
		}
	}

	private sealed class TileMappingConfig
	{
		[JsonPropertyName("terrain")] public Dictionary<string, TerrainTileMapping>? Terrain { get; set; }
		[JsonPropertyName("entity")] public Dictionary<string, string>? Entity { get; set; }
		[JsonPropertyName("fixture")] public Dictionary<string, string>? Fixture { get; set; }
		[JsonPropertyName("item")] public Dictionary<string, string>? Item { get; set; }
	}
}
