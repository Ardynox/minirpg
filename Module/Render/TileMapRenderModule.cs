using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MiniRPG.Core.World;

namespace MiniRPG.Module.Render;

/// <summary>
/// TileMapLayer 渲染模块。
///
/// 4 层架构：
///   GroundLayer  — 可见/周边感知的地形 tile（正常亮度）
///   MemoryLayer  — 已探索但不可见的地形 tile（暗色，仅地形无实体）
///   EntityLayer  — 可见/周边感知的实体 tile（玩家、怪物、道具、设施）
///   FogLayer     — 未探索格子用纯黑 tile 遮盖
///
/// Atlas 合图模式：tile_name_to_id.json 存储 (source_id, atlas_x, atlas_y)，
/// 同一 atlas source 内的 tile 共享纹理，大幅减少 draw call。
/// </summary>
public class TileMapRenderModule
{
	private const string FallbackTile = "BLACK TILE";
	private const string MappingResPath = "res://Data/tile_mapping.json";
	private const string IdMapResPath = "res://Tools/tile_name_to_id.json";
	private const string FallbackFixtureTile = "Misc A4_N";

	/// <summary>无效 tile 标识，sourceId = -1 表示跳过渲染。</summary>
	private static readonly TileLoc InvalidTile = new(-1, Vector2I.Zero);

	// ── 字段 ──────────────────────────────────────────────

	private readonly GameState       _state;
	private readonly FogOfWarTracker _fogTracker;
	private readonly int             _viewW;
	private readonly int             _viewH;

	private TileMapLayer _groundLayer = null!;
	private TileMapLayer _memoryLayer = null!;
	private TileMapLayer _entityLayer = null!;
	private TileMapLayer _fogLayer    = null!;

	private Camera2D? _camera;
	private IAnimatable? _playerAnim;

	// tile 名称 → (sourceId, atlasCoord)
	private Dictionary<string, TileLoc> _nameToLoc = new();
	private TileLoc _blackTile;

	// 从 tile_mapping.json 加载的映射表
	private Dictionary<string, string> _terrainMap = new();
	private Dictionary<string, string> _entityMap  = new();
	private Dictionary<string, string> _fixtureMap = new();
	private Dictionary<string, string> _itemMap    = new();

	// ── 公开属性（与 MapRenderModule 接口兼容） ──────────────

	public bool FogMapVisible  { get; set; }
	public bool MinimapVisible { get; set; }

	// ── 构造 ──────────────────────────────────────────────

	public TileMapRenderModule(GameState state, FogOfWarTracker fogTracker, int viewW, int viewH)
	{
		_state      = state;
		_fogTracker = fogTracker;
		_viewW      = viewW;
		_viewH      = viewH;
		_blackTile  = InvalidTile;
	}

	// ── 初始化 ────────────────────────────────────────────

	/// <summary>
	/// 创建并挂载 TileMapLayer 子节点，加载映射数据。
	/// playerSpine 参数保留向后兼容，内部包装为 IAnimatable 并注册到 ResAccess。
	/// </summary>
	public void Init(Node2D mapRoot, TileSet tileSet, Node2D? playerSpine = null, Camera2D? camera = null)
	{
		LoadIdMap(IdMapResPath);
		LoadTileMapping(MappingResPath);

		_groundLayer = MakeLayer(mapRoot, "GroundLayer", tileSet, 0);
		_memoryLayer = MakeLayer(mapRoot, "MemoryLayer", tileSet, 0);
		_entityLayer = MakeLayer(mapRoot, "EntityLayer", tileSet, 1);
		_fogLayer    = MakeLayer(mapRoot, "FogLayer",    tileSet, 2);

		_memoryLayer.Modulate = new Color(0.22f, 0.22f, 0.28f);

		if (playerSpine != null)
		{
			playerSpine.Visible = false;
			_playerAnim = new SpineAnimatable(playerSpine);
			ResAccess.RegisterAnimatable("player", _playerAnim);
			GD.Print("[TileMapRender] Spine 角色已绑定（通过 IAnimatable）");
		}

		_camera = camera;
	}

	// ── 主渲染 ────────────────────────────────────────────

	public void Flush()
	{
		_fogTracker.Update(_state);

		_groundLayer.Clear();
		_memoryLayer.Clear();
		_entityLayer.Clear();
		_fogLayer.Clear();

		if (_state.World == null)
		{
			if (_playerAnim?.Node != null) _playerAnim.Node.Visible = false;
			return;
		}

		var cx = _state.PlayerX;
		var cy = _state.PlayerY;
		var cz = _state.PlayerZ;

		var halfW = _viewW / 2;
		var halfH = _viewH / 2;

		var playerCell = new Vector2I(halfW, halfH);

		for (int sy = 0; sy < _viewH; sy++)
		{
			for (int sx = 0; sx < _viewW; sx++)
			{
				int wx = cx - halfW + sx;
				int wy = cy - halfH + sy;

				var cell = new Vector2I(sx, sy);

				bool visible    = _fogTracker.IsVisible(wx, wy, cz);
				bool peripheral = _fogTracker.IsPeripheral(wx, wy, cz);
				bool hasSeen    = _fogTracker.HasSeen(wx, wy, cz);

				if (visible || peripheral)
				{
					var terrain = _state.World.GetTerrain(wx, wy, cz);
					SetTile(_groundLayer, cell, TerrainLoc(terrain));

					bool isPlayerCell = _playerAnim != null && wx == cx && wy == cy;
					if (isPlayerCell)
						SetTile(_entityLayer, cell, EntityLocSkipPlayer(wx, wy, cz));
					else
						SetTile(_entityLayer, cell, EntityLoc(wx, wy, cz));
				}
				else if (hasSeen)
				{
					var terrain = _state.World.GetTerrain(wx, wy, cz);
					SetTile(_memoryLayer, cell, TerrainLoc(terrain));
				}
				else
				{
					if (_blackTile.SourceId >= 0)
						_fogLayer.SetCell(cell, _blackTile.SourceId, _blackTile.Coord);
				}
			}
		}

		UpdatePlayerSpine(playerCell);
	}

	// ── 模式切换（与 MapRenderModule 接口兼容） ──────────────

	public string? ToggleRenderMode() => null;

	public string ToggleMinimap()
	{
		MinimapVisible = !MinimapVisible;
		return MinimapVisible ? "小地图: 暂不支持（TileMap 模式）" : "小地图: 关闭";
	}

	public string ToggleFogMap()
	{
		FogMapVisible = !FogMapVisible;
		return FogMapVisible ? "大地图: 暂不支持（TileMap 模式）" : "大地图: 关闭";
	}

	public void CenterFogMap() { }

	public void ScrollFogMap(int dx, int dy) { }

	public void ResetOverlays()
	{
		FogMapVisible  = false;
		MinimapVisible = false;
	}

	// ── 内部工具 ──────────────────────────────────────────

	private static TileMapLayer MakeLayer(Node2D parent, string name, TileSet ts, int zIndex)
	{
		var layer      = new TileMapLayer();
		layer.Name     = name;
		layer.TileSet  = ts;
		layer.ZIndex   = zIndex;
		parent.AddChild(layer);
		return layer;
	}

	private static void SetTile(TileMapLayer layer, Vector2I cell, TileLoc loc)
	{
		if (loc.SourceId >= 0)
			layer.SetCell(cell, loc.SourceId, loc.Coord);
	}

	private TileLoc TerrainLoc(TerrainDef terrain)
	{
		if (_terrainMap.TryGetValue(terrain.StringId, out var tileName))
			return Resolve(tileName);
		return _blackTile;
	}

	/// <summary>
	/// 玩家 cell 专用：跳过玩家 tile（由 Spine 替代），但仍渲染 fixture/item。
	/// </summary>
	private TileLoc EntityLocSkipPlayer(int wx, int wy, int cz)
	{
		var fixture = _state.World!.GetFirstEntity(wx, wy, cz, CellEntityType.Fixture);
		if (fixture != null)
			return Resolve(_fixtureMap.GetValueOrDefault(fixture.EntityId, FallbackFixtureTile));

		var container = _state.World.GetFirstEntity(wx, wy, cz, CellEntityType.Container);
		if (container != null)
			return Resolve(_itemMap.GetValueOrDefault("container", FallbackTile));

		var item = _state.World.GetFirstEntity(wx, wy, cz, CellEntityType.Item);
		if (item != null)
			return Resolve(_itemMap.GetValueOrDefault("drop", FallbackTile));

		return InvalidTile;
	}

	private TileLoc EntityLoc(int wx, int wy, int cz)
	{
		var player = ActorModule.GetPlayer(_state);
		if (player is { X: var px, Y: var py, Z: var pz } && px == wx && py == wy && pz == cz)
			return Resolve(_entityMap.GetValueOrDefault("player", FallbackTile));

		foreach (var actor in _state.Actors.Values)
		{
			if (actor.X != wx || actor.Y != wy || actor.Z != cz) continue;
			if (actor.Id == _state.PlayerId) continue;

			var key = actor.Faction == Factions.Hostile ? "hostile" : "friendly";
			return Resolve(_entityMap.GetValueOrDefault(key, FallbackTile));
		}

		var fixture = _state.World!.GetFirstEntity(wx, wy, cz, CellEntityType.Fixture);
		if (fixture != null)
			return Resolve(_fixtureMap.GetValueOrDefault(fixture.EntityId, FallbackFixtureTile));

		var container = _state.World.GetFirstEntity(wx, wy, cz, CellEntityType.Container);
		if (container != null)
			return Resolve(_itemMap.GetValueOrDefault("container", FallbackTile));

		var item = _state.World.GetFirstEntity(wx, wy, cz, CellEntityType.Item);
		if (item != null)
			return Resolve(_itemMap.GetValueOrDefault("drop", FallbackTile));

		return InvalidTile;
	}

	private TileLoc Resolve(string name) =>
		_nameToLoc.TryGetValue(name, out var loc) ? loc : _blackTile;

	/// <summary>
	/// 将 Camera 和玩家动画节点移动到玩家所在 TileMap cell 的像素坐标。
	/// </summary>
	private void UpdatePlayerSpine(Vector2I playerCell)
	{
		var localPos = _groundLayer.MapToLocal(playerCell);

		if (_camera != null)
			_camera.Position = localPos;

		if (_playerAnim == null) return;

		var node = _playerAnim.Node;
		if (!node.Visible)
		{
			node.Visible = true;
			_playerAnim.Play("Idle");
		}
		node.Position = localPos;
	}

	// ── 动画控制（委托给 IAnimatable）─────────────────────

	/// <summary>播放玩家 Spine 动画（向后兼容接口）。</summary>
	public void PlaySpineAnim(string animName, bool loop, int track = 0)
		=> _playerAnim?.Play(animName, loop);

	/// <summary>在当前动画结束后追加播放（向后兼容接口）。</summary>
	public void QueueSpineAnim(string animName, bool loop, float delay = 0f, int track = 0)
	{
	}

	/// <summary>播放一次性动画，结束后自动回到 Idle。</summary>
	public void PlayOneShotThenIdle(string animName)
		=> _playerAnim?.PlayOneShot(animName, "Idle");

	/// <summary>获取玩家的 IAnimatable 实例。供外部（如 Main.Dispatch）使用。</summary>
	public IAnimatable? PlayerAnimatable => _playerAnim;

	// ── 数据加载 ─────────────────────────────────────────

	private void LoadIdMap(string resPath)
	{
		_nameToLoc.Clear();

		if (!Godot.FileAccess.FileExists(resPath))
		{
			GD.PrintErr($"[TileMapRender] 找不到 ID 映射文件：{resPath}");
			GD.PrintErr("[TileMapRender] 请先运行 Tools/TilesetImporter.tscn 生成映射");
			return;
		}

		var json    = Godot.FileAccess.GetFileAsString(resPath);
		var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
		var map     = JsonSerializer.Deserialize<Dictionary<string, TileSourceEntry>>(json, options);

		if (map == null)
		{
			GD.PrintErr("[TileMapRender] ID 映射 JSON 解析失败");
			return;
		}

		foreach (var (name, entry) in map)
			_nameToLoc[name] = new TileLoc(entry.SourceId, new Vector2I(entry.AtlasX, entry.AtlasY));

		_blackTile = _nameToLoc.GetValueOrDefault(FallbackTile, InvalidTile);

		GD.Print($"[TileMapRender] 已加载 {_nameToLoc.Count} 个 tile 映射");
	}

	private void LoadTileMapping(string resPath)
	{
		if (!Godot.FileAccess.FileExists(resPath))
		{
			GD.PrintErr($"[TileMapRender] 找不到 tile 映射配置：{resPath}");
			return;
		}

		var json = Godot.FileAccess.GetFileAsString(resPath);
		var root = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);

		if (root == null)
		{
			GD.PrintErr("[TileMapRender] tile 映射配置解析失败");
			return;
		}

		_terrainMap = root.GetValueOrDefault("terrain") ?? new();
		_entityMap  = root.GetValueOrDefault("entity")  ?? new();
		_fixtureMap = root.GetValueOrDefault("fixture") ?? new();
		_itemMap    = root.GetValueOrDefault("item")     ?? new();

		GD.Print($"[TileMapRender] tile 映射已加载：" +
			$"terrain={_terrainMap.Count} entity={_entityMap.Count} " +
			$"fixture={_fixtureMap.Count} item={_itemMap.Count}");
	}

	// ── 内部数据类型 ─────────────────────────────────────

	/// <summary>atlas 内一个 tile 的定位：sourceId + 网格坐标。</summary>
	private readonly record struct TileLoc(int SourceId, Vector2I Coord);

	private class TileSourceEntry
	{
		[JsonPropertyName("source_id")] public int SourceId { get; set; }
		[JsonPropertyName("atlas_x")]   public int AtlasX   { get; set; }
		[JsonPropertyName("atlas_y")]   public int AtlasY   { get; set; }
	}
}
