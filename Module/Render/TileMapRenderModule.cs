using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MiniRPG.Core.World;

namespace MiniRPG.Module.Render;

public class TileMapRenderModule
{
	private const string FallbackTile = "BLACK TILE";
	private const string MappingResPath = "res://Data/tile_mapping.json";
	private const string IdMapResPath = "res://Tools/tile_name_to_id.json";
	private const string FallbackFixtureTile = "Misc A4_N";
	private const string PeripheralFixtureTile = "Misc A4_N";
	private static readonly Color MemoryTint = new(0.22f, 0.22f, 0.28f);
	private static readonly Color PeripheralTint = new(0.50f, 0.50f, 0.56f);
	private static readonly Color EditorHighlightTint = new(1.0f, 0.95f, 0.55f, 0.55f);
	private static readonly Vector2 PlayerSpriteOffset = new(0, 64);
	private static readonly TileLoc InvalidTile = new(-1, Vector2I.Zero);

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
	private TileMapLayer _peripheralEntityLayer = null!;
	private TileMapLayer _entityLayer = null!;
	private TileMapLayer _fogLayer = null!;
	private TileMapLayer _editorHighlightBaseLayer = null!;
	private TileMapLayer _editorHighlightOverlayLayer = null!;

	private Camera2D? _camera;
	private IAnimatable? _playerAnim;
	private SubViewportContainer? _viewportContainer;
	private SubViewport? _subViewport;

	private Dictionary<string, TileLoc> _nameToLoc = new();
	private TileLoc _blackTile;

	private Dictionary<string, TerrainTileMapping> _terrainMap = new();
	private Dictionary<string, string> _entityMap = new();
	private Dictionary<string, string> _fixtureMap = new();
	private Dictionary<string, string> _itemMap = new();

	private bool _editorViewActive;
	private int _viewCenterX;
	private int _viewCenterY;
	private int _viewCenterZ;
	private Vector2I? _editorHoverWorld;

	public bool FogMapVisible { get; set; }
	public bool MinimapVisible { get; set; }

	public TileMapRenderModule(GameState state, FogOfWarTracker fogTracker, int viewW, int viewH)
	{
		_state = state;
		_fogTracker = fogTracker;
		_viewW = viewW;
		_viewH = viewH;
		_blackTile = InvalidTile;
	}

	public void Init(
		Node2D mapRoot,
		TileSet tileSet,
		SubViewportContainer? viewportContainer = null,
		SubViewport? subViewport = null,
		Node2D? playerSpine = null,
		Camera2D? camera = null)
	{
		LoadIdMap(IdMapResPath);
		LoadTileMapping(MappingResPath);

		_groundLayer = MakeLayer(mapRoot, "GroundLayer", tileSet, 0);
		_memoryLayer = MakeLayer(mapRoot, "MemoryLayer", tileSet, 0);
		_overlayLayer = MakeLayer(mapRoot, "OverlayLayer", tileSet, 1);
		_memoryOverlayLayer = MakeLayer(mapRoot, "MemoryOverlayLayer", tileSet, 1);
		_peripheralLayer = MakeLayer(mapRoot, "PeripheralLayer", tileSet, 0);
		_peripheralOverlayLayer = MakeLayer(mapRoot, "PeripheralOverlayLayer", tileSet, 1);
		_peripheralEntityLayer = MakeLayer(mapRoot, "PeripheralEntityLayer", tileSet, 2);
		_entityLayer = MakeLayer(mapRoot, "EntityLayer", tileSet, 2);
		_fogLayer = MakeLayer(mapRoot, "FogLayer", tileSet, 3);
		_editorHighlightBaseLayer = MakeLayer(mapRoot, "EditorHighlightBaseLayer", tileSet, 4);
		_editorHighlightOverlayLayer = MakeLayer(mapRoot, "EditorHighlightOverlayLayer", tileSet, 5);

		_memoryLayer.Modulate = MemoryTint;
		_memoryOverlayLayer.Modulate = MemoryTint;
		_peripheralLayer.Modulate = PeripheralTint;
		_peripheralOverlayLayer.Modulate = PeripheralTint;
		_peripheralEntityLayer.Modulate = PeripheralTint;
		_editorHighlightBaseLayer.Modulate = EditorHighlightTint;
		_editorHighlightOverlayLayer.Modulate = EditorHighlightTint;

		if (playerSpine != null)
		{
			playerSpine.Visible = false;
			_playerAnim = new SpineAnimatable(playerSpine);
			ResAccess.RegisterAnimatable("player", _playerAnim);
		}

		_viewportContainer = viewportContainer;
		_subViewport = subViewport;
		_camera = camera;
	}

	public void SetEditorView(bool active, int centerX, int centerY, int centerZ, Vector2I? hoverWorld = null)
	{
		_editorViewActive = active;
		_viewCenterX = centerX;
		_viewCenterY = centerY;
		_viewCenterZ = centerZ;
		_editorHoverWorld = active ? hoverWorld : null;
	}

	public bool TryGetWorldCellFromGlobalPosition(Vector2 globalPos, out Vector3I worldCell)
	{
		worldCell = Vector3I.Zero;
		if (!_editorViewActive || _viewportContainer == null || _subViewport == null || _camera == null)
			return false;

		var rect = _viewportContainer.GetGlobalRect();
		if (!rect.HasPoint(globalPos) || rect.Size.X <= 0 || rect.Size.Y <= 0)
			return false;

		var localInContainer = globalPos - rect.Position;
		var viewportPos = new Vector2(
			localInContainer.X * _subViewport.Size.X / rect.Size.X,
			localInContainer.Y * _subViewport.Size.Y / rect.Size.Y);
		var viewportSize = new Vector2(_subViewport.Size.X, _subViewport.Size.Y);
		var mapLocal = viewportPos + _camera.Position - viewportSize / 2f;
		var cell = _groundLayer.LocalToMap(mapLocal);
		if (cell.X < 0 || cell.X >= _viewW || cell.Y < 0 || cell.Y >= _viewH)
			return false;

		worldCell = new Vector3I(
			_viewCenterX - _viewW / 2 + cell.X,
			_viewCenterY - _viewH / 2 + cell.Y,
			_viewCenterZ);
		return true;
	}

	public void Flush()
	{
		ClearLayers();

		if (_state.World == null)
		{
			HidePlayerSpine();
			return;
		}

		if (_editorViewActive)
		{
			FlushEditor();
			return;
		}

		_fogTracker.Update(_state);

		var cx = _state.PlayerX;
		var cy = _state.PlayerY;
		var cz = _state.PlayerZ;
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
					var terrain = _state.World.GetTerrain(wx, wy, cz);
					SetTerrain(cell, terrain, _groundLayer, _overlayLayer);

					var isPlayerCell = _playerAnim != null && wx == cx && wy == cy;
					SetTile(_entityLayer, cell, isPlayerCell ? EntityLocSkipPlayer(wx, wy, cz) : EntityLoc(wx, wy, cz));
				}
				else if (visionBand == PlayerVisionBand.Peripheral)
				{
					var terrain = _state.World.GetTerrain(wx, wy, cz);
					SetTerrain(cell, terrain, _peripheralLayer, _peripheralOverlayLayer);
					SetTile(_peripheralEntityLayer, cell, PeripheralEntityLoc(wx, wy, cz));
				}
				else if (visionBand == PlayerVisionBand.Memory)
				{
					var terrain = _state.World.GetTerrain(wx, wy, cz);
					SetTerrain(cell, terrain, _memoryLayer, _memoryOverlayLayer);
				}
				else if (_blackTile.SourceId >= 0)
				{
					_fogLayer.SetCell(cell, _blackTile.SourceId, _blackTile.Coord);
				}
			}
		}

		UpdateCamera();
		UpdatePlayerSpine(cx, cy, cz);
	}

	public string? ToggleRenderMode() => null;

	public string ToggleMinimap()
	{
		MinimapVisible = !MinimapVisible;
		return MinimapVisible ? "小地图: TileMap 模式暂未实现" : "小地图: 关闭";
	}

	public string ToggleFogMap()
	{
		FogMapVisible = !FogMapVisible;
		return FogMapVisible ? "大地图: TileMap 模式暂未实现" : "大地图: 关闭";
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
				SetTile(_entityLayer, cell, isPlayerCell ? EntityLocSkipPlayer(wx, wy, cz) : EntityLoc(wx, wy, cz));
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
		UpdatePlayerSpine(cx, cy, cz);
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

	private void UpdateCamera()
	{
		if (_camera == null)
			return;

		_camera.Position = _groundLayer.MapToLocal(new Vector2I(_viewW / 2, _viewH / 2));
	}

	private void UpdatePlayerSpine(int centerWorldX, int centerWorldY, int centerWorldZ)
	{
		if (_playerAnim == null)
			return;

		var player = ActorModule.GetPlayer(_state);
		if (player == null || player.Z != centerWorldZ)
		{
			HidePlayerSpine();
			return;
		}

		var halfW = _viewW / 2;
		var halfH = _viewH / 2;
		var sx = player.X - (centerWorldX - halfW);
		var sy = player.Y - (centerWorldY - halfH);
		if (sx < 0 || sx >= _viewW || sy < 0 || sy >= _viewH)
		{
			HidePlayerSpine();
			return;
		}

		var localPos = _groundLayer.MapToLocal(new Vector2I(sx, sy));
		var node = _playerAnim.Node;
		if (!node.Visible)
		{
			node.Visible = true;
			_playerAnim.Play("Idle");
		}

		node.Position = localPos + PlayerSpriteOffset;
	}

	private void HidePlayerSpine()
	{
		if (_playerAnim?.Node != null)
			_playerAnim.Node.Visible = false;
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

	private static void SetTile(TileMapLayer layer, Vector2I cell, TileLoc loc)
	{
		if (loc.SourceId >= 0)
			layer.SetCell(cell, loc.SourceId, loc.Coord);
	}

	private void SetTerrain(Vector2I cell, TerrainDef terrain, TileMapLayer baseLayer, TileMapLayer overlayLayer)
	{
		var terrainLoc = TerrainLoc(terrain);
		SetTile(baseLayer, cell, terrainLoc.Base);
		SetTile(overlayLayer, cell, terrainLoc.Overlay);
	}

	private TerrainTileLoc TerrainLoc(TerrainDef terrain)
	{
		if (_terrainMap.TryGetValue(terrain.StringId, out var tileNames))
			return new TerrainTileLoc(Resolve(tileNames.Base), ResolveOptional(tileNames.Overlay));

		return new TerrainTileLoc(_blackTile, InvalidTile);
	}

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

	private TileLoc PeripheralEntityLoc(int wx, int wy, int cz)
	{
		foreach (var actor in _state.Actors.Values)
		{
			if (actor.X != wx || actor.Y != wy || actor.Z != cz) continue;
			if (actor.Id == _state.PlayerId) continue;

			var key = actor.Faction == Factions.Hostile ? "hostile" : "friendly";
			return Resolve(_entityMap.GetValueOrDefault(key, FallbackTile));
		}

		var fixture = _state.World!.GetFirstEntity(wx, wy, cz, CellEntityType.Fixture);
		if (fixture != null)
			return Resolve(PeripheralFixtureTile);

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

	private TileLoc ResolveOptional(string? name)
	{
		if (string.IsNullOrEmpty(name))
			return InvalidTile;

		return _nameToLoc.TryGetValue(name, out var loc) ? loc : InvalidTile;
	}

	private void LoadIdMap(string resPath)
	{
		_nameToLoc.Clear();

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
			_nameToLoc[name] = new TileLoc(entry.SourceId, new Vector2I(entry.AtlasX, entry.AtlasY));

		_blackTile = _nameToLoc.GetValueOrDefault(FallbackTile, InvalidTile);
	}

	private void LoadTileMapping(string resPath)
	{
		if (!Godot.FileAccess.FileExists(resPath))
		{
			GD.PrintErr($"[TileMapRender] Missing tile mapping config: {resPath}");
			return;
		}

		var json = Godot.FileAccess.GetFileAsString(resPath);
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

	private readonly record struct TileLoc(int SourceId, Vector2I Coord);

	private readonly record struct TerrainTileLoc(TileLoc Base, TileLoc Overlay);

	[JsonConverter(typeof(TerrainTileMappingConverter))]
	private readonly record struct TerrainTileMapping(string Base, string? Overlay);

	private sealed class TileSourceEntry
	{
		[JsonPropertyName("source_id")] public int SourceId { get; set; }
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
