using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.World;

/// <summary>
/// 统一的三维世界访问 API。
/// 所有地图操作通过此类进行，内部路由到对应的 ChunkData。
/// 替代旧的 GameState.Cells / MapWidth / MapHeight。
/// </summary>
public class WorldMap
{
	private readonly record struct FacilityOccupancy(
		string FacilityId,
		FacilityFootprintCell Cell,
		bool BlocksSight,
		bool Passable);

	private const string ItemSnapshotMetaKey = "itemSnapshot";
	private const string ItemTemplateIdMetaKey = "templateId";
	private const string ItemInstanceIdMetaKey = "instanceId";
	private const string ItemCategoryMetaKey = "category";
	private const string ItemDurabilityMetaKey = "durability";
	private const string ItemMaxDurabilityMetaKey = "maxDurability";
	private const string EntityDurabilityMetaKey = "durability";
	private const string EntityMaxDurabilityMetaKey = "maxDurability";
	private const string EntityMaterialMetaKey = "material";
	private const string EntityFlammableMetaKey = "flammable";
	private const string EntityBlocksSightMetaKey = "blocksSight";
	private const string EntityControlledFireSourceMetaKey = "controlledFireSource";

	public ChunkManager Chunks { get; }
	public int WorldSeed { get; }
	private IReadOnlyDictionary<string, FacilityInstance>? _facilities;
	private readonly Dictionary<ZoneCell, FacilityOccupancy> _facilityIndex = new();

	public WorldMap(int worldSeed, IMapGenerator generator)
	{
		WorldSeed = worldSeed;
		Chunks = new ChunkManager(worldSeed, generator);
		Chunks.ApplyRuntimeConfig(GameConfig.WorldRuntime);
	}

	public void AttachFacilityState(IReadOnlyDictionary<string, FacilityInstance>? facilities)
	{
		_facilities = facilities;
		RebuildFacilityIndex();
	}

	public void RebuildFacilityIndex()
	{
		_facilityIndex.Clear();
		if (_facilities == null || _facilities.Count == 0)
			return;

		foreach (var facility in _facilities.Values.OrderBy(static facility => facility.Id, StringComparer.Ordinal))
		{
			var def = FacilityRegistry.Get(facility.FacilityDefId);
			if (def == null)
				continue;

			foreach (var (worldX, worldY, _, footprintCell) in EnumerateFootprint(def, facility.AnchorX, facility.AnchorY, facility.Z, facility.Rotation))
			{
				_facilityIndex[new ZoneCell(worldX, worldY, facility.Z)] = new FacilityOccupancy(
					facility.Id,
					footprintCell,
					BlocksSight: ShouldFacilityBlockSight(facility, footprintCell),
					Passable: IsFacilityPassable(facility, footprintCell));
			}
		}
	}

	// ══════════════════════════════════════════════════════
	//  坐标 → Chunk 解析（内部工具）
	// ══════════════════════════════════════════════════════

	private (ChunkData Chunk, int Lx, int Ly) Resolve(int x, int y, int z)
	{
		var cc = CoordUtil.WorldToChunk(x, y, z);
		var (lx, ly) = CoordUtil.WorldToLocal(x, y);
		var chunk = Chunks.GetOrLoad(cc);
		return (chunk, lx, ly);
	}

	// ══════════════════════════════════════════════════════
	//  地形操作
	// ══════════════════════════════════════════════════════

	public ushort GetTerrainId(int x, int y, int z)
	{
		var (chunk, lx, ly) = Resolve(x, y, z);
		return chunk.GetTerrainId(lx, ly);
	}

	public TerrainDef GetTerrain(int x, int y, int z) =>
		TerrainRegistry.Get(GetTerrainId(x, y, z));

	public void SetTerrainId(int x, int y, int z, ushort terrainId)
	{
		var (chunk, lx, ly) = Resolve(x, y, z);
		chunk.SetTerrain(lx, ly, terrainId);
	}

	public void SetTerrain(int x, int y, int z, string terrainStringId)
	{
		var id = TerrainRegistry.GetId(terrainStringId);
		SetTerrainId(x, y, z, id);
	}

	public byte GetHardness(int x, int y, int z)
	{
		var (chunk, lx, ly) = Resolve(x, y, z);
		return chunk.GetHardness(lx, ly);
	}

	public void SetHardness(int x, int y, int z, byte hardness)
	{
		var (chunk, lx, ly) = Resolve(x, y, z);
		chunk.SetHardness(lx, ly, hardness);
	}

	// ══════════════════════════════════════════════════════
	//  实体栈操作（非地形：Fixture / Item / Hazard / ...）
	// ══════════════════════════════════════════════════════

	public List<CellEntity> GetEntities(int x, int y, int z)
	{
		var (chunk, lx, ly) = Resolve(x, y, z);
		return chunk.GetEntities(lx, ly);
	}

	public void PushEntity(int x, int y, int z, CellEntity entity)
	{
		var (chunk, lx, ly) = Resolve(x, y, z);
		chunk.PushEntity(lx, ly, entity);
	}

	public bool RemoveEntity(int x, int y, int z, CellEntityType type, string entityId)
	{
		var (chunk, lx, ly) = Resolve(x, y, z);
		var removed = chunk.RemoveEntity(lx, ly, type, entityId);
		if (removed && type == CellEntityType.Fixture)
			RemoveNestAt(chunk, lx, ly);
		return removed;
	}

	public int RemoveEntitiesByType(int x, int y, int z, CellEntityType type)
	{
		var (chunk, lx, ly) = Resolve(x, y, z);
		var removed = chunk.RemoveEntitiesByType(lx, ly, type);
		if (removed > 0 && type == CellEntityType.Fixture)
			RemoveNestAt(chunk, lx, ly);
		return removed;
	}

	public List<CellEntity> GetEntitiesByType(int x, int y, int z, CellEntityType type) =>
		GetEntities(x, y, z).Where(e => e.Type == type).ToList();

	public CellEntity? GetFirstEntity(int x, int y, int z, CellEntityType type) =>
		GetEntities(x, y, z).FirstOrDefault(e => e.Type == type);

	public CellEntity? GetEntity(int x, int y, int z, CellEntityType type, string entityId) =>
		GetEntities(x, y, z).FirstOrDefault(entity =>
			entity.Type == type
			&& string.Equals(entity.EntityId, entityId, StringComparison.Ordinal));

	public bool UpdateEntity(int x, int y, int z, CellEntityType type, string entityId, Action<CellEntity> update)
	{
		var (chunk, lx, ly) = Resolve(x, y, z);
		var idx = ly * ChunkData.Size + lx;
		if (!chunk.Entities.TryGetValue(idx, out var list))
			return false;

		for (var i = 0; i < list.Count; i++)
		{
			var entity = list[i];
			if (entity.Type != type || !string.Equals(entity.EntityId, entityId, StringComparison.Ordinal))
				continue;

			update(entity);
			chunk.Dirty = true;
			return true;
		}

		return false;
	}

	// ══════════════════════════════════════════════════════
	//  查询
	// ══════════════════════════════════════════════════════

	public bool IsSolid(int x, int y, int z) =>
		GetTerrain(x, y, z).Solid;

	/// <summary>
	/// 共享视线遮挡真相：地形沿用 Solid，fixture 可覆写是否挡视线。
	/// 当前约定：房屋挡视线；门/楼梯/巢穴默认视为可透视的开口或低矮设施。
	/// </summary>
	public bool BlocksSight(int x, int y, int z)
	{
		if (GetTerrain(x, y, z).Solid)
			return true;

		if (TryGetFacilityOccupancy(x, y, z, out var occupancy) && occupancy.BlocksSight)
			return true;

		foreach (var entity in GetEntities(x, y, z))
		{
			if (entity.Type != CellEntityType.Fixture) continue;
			if (FixtureBlocksSight(entity))
				return true;
		}

		return false;
	}

	public bool IsWalkable(int x, int y, int z) =>
		!IsSolid(x, y, z)
		&& (!TryGetFacilityOccupancy(x, y, z, out var occupancy) || occupancy.Passable);

	public bool IsWeatherExposed(int x, int y, int z) =>
		z == 0 && !BlocksSight(x, y, z);

	/// <summary>
	/// 按坐标查询 Actor。
	/// 已加载 chunk 优先走 chunk.ActorIds 索引；未加载 chunk 回退为全表扫描，
	/// 以保持未加载区域的原有查询语义。
	/// </summary>
	public List<Actor> GetActorsAt(int x, int y, int z, Dictionary<string, Actor> actors)
	{
		if (actors.Count == 0)
			return [];

		var coord = CoordUtil.WorldToChunk(x, y, z);
		if (!Chunks.IsLoaded(coord))
			return ScanActorsAt(x, y, z, actors);

		var chunk = Chunks.GetOrLoad(coord);
		if (chunk.ActorIds.Count == 0)
			return [];

		var result = new List<Actor>(chunk.ActorIds.Count);
		foreach (var actorId in chunk.ActorIds)
		{
			if (!actors.TryGetValue(actorId, out var actor))
				continue;

			if (actor.X == x && actor.Y == y && actor.Z == z)
				result.Add(actor);
		}

		result.Sort(static (left, right) => string.Compare(left.Id, right.Id, System.StringComparison.Ordinal));
		return result;
	}

	public Actor? GetActorAt(int x, int y, int z, Dictionary<string, Actor> actors) =>
		GetActorsAt(x, y, z, actors).FirstOrDefault();

	public Actor? GetHostileActorAt(int x, int y, int z, Dictionary<string, Actor> actors) =>
		GetActorsAt(x, y, z, actors).FirstOrDefault(static actor => actor.Faction == Factions.Hostile);

	/// <summary>
	/// 渲染用：获取格子的显示字符。
	/// 优先显示 Actor（player > hostile > 其他），否则显示栈顶实体 Glyph，否则显示地形 Glyph。
	/// </summary>
	public string GetDisplayCell(int x, int y, int z, Dictionary<string, Actor> actors)
	{
		var actor = GetDisplayActor(x, y, z, actors);
		if (actor != null) return actor.Glyph;

		var entities = GetEntities(x, y, z);
		if (entities.Count > 0)
		{
			var top = entities[^1];
			if (TryGetFacilityOccupancy(x, y, z, out var facility)
				&& top.Type is CellEntityType.Item or CellEntityType.Container)
			{
				return facility.Cell.Glyph;
			}

			return top.Glyph;
		}

		if (TryGetFacilityOccupancy(x, y, z, out var occupancyForDisplay))
			return occupancyForDisplay.Cell.Glyph;

		return GetTerrain(x, y, z).Glyph;
	}

	/// <summary>该格是否有 Actor 或非地形实体（不依赖 Glyph 比较）。</summary>
	public bool HasActorOrEntity(int x, int y, int z, Dictionary<string, Actor> actors)
	{
		return GetActorsAt(x, y, z, actors).Count > 0
			|| GetEntities(x, y, z).Count > 0
			|| _facilityIndex.ContainsKey(new ZoneCell(x, y, z));
	}

	public bool TryGetFacilityAt(int x, int y, int z, out FacilityInstance? facility)
	{
		if (TryGetFacilityOccupancy(x, y, z, out var occupancy)
			&& _facilities != null
			&& _facilities.TryGetValue(occupancy.FacilityId, out var resolved))
		{
			facility = resolved;
			return true;
		}

		facility = null;
		return false;
	}

	public bool TryGetFacilityAt(int x, int y, int z, out FacilityInstance? facility, out FacilityFootprintCell? footprintCell)
	{
		if (TryGetFacilityOccupancy(x, y, z, out var occupancy)
			&& _facilities != null
			&& _facilities.TryGetValue(occupancy.FacilityId, out var resolved))
		{
			facility = resolved;
			footprintCell = occupancy.Cell.Clone();
			return true;
		}

		facility = null;
		footprintCell = null;
		return false;
	}

	public List<ZoneCell> GetFootprintCells(FacilityInstance facility)
	{
		var def = FacilityRegistry.Get(facility.FacilityDefId);
		return def == null
			? []
			: GetFootprintCells(def, facility.AnchorX, facility.AnchorY, facility.Z, facility.Rotation);
	}

	public List<ZoneCell> GetFootprintCells(FacilityDef def, int anchorX, int anchorY, int z, FacilityRotation rotation) =>
		EnumerateFootprint(def, anchorX, anchorY, z, rotation)
			.Select(static cell => new ZoneCell(cell.WorldX, cell.WorldY, cell.WorldZ))
			.ToList();

	public List<ZoneCell> GetFacilityBlockers(FacilityDef def, int anchorX, int anchorY, int z, FacilityRotation rotation)
	{
		var blockers = new List<ZoneCell>();
		foreach (var (worldX, worldY, worldZ, _) in EnumerateFootprint(def, anchorX, anchorY, z, rotation))
		{
			if (GetTerrain(worldX, worldY, worldZ).Solid)
			{
				blockers.Add(new ZoneCell(worldX, worldY, worldZ));
				continue;
			}

			if (TryGetFacilityAt(worldX, worldY, worldZ, out _))
			{
				blockers.Add(new ZoneCell(worldX, worldY, worldZ));
				continue;
			}

			if (GetEntities(worldX, worldY, worldZ).Any(static entity =>
				entity.Type is CellEntityType.Fixture or CellEntityType.Container or CellEntityType.Hazard or CellEntityType.Corpse))
			{
				blockers.Add(new ZoneCell(worldX, worldY, worldZ));
			}
		}

		return blockers;
	}

	public bool CanPlaceFacility(FacilityDef def, int anchorX, int anchorY, int z, FacilityRotation rotation) =>
		GetFacilityBlockers(def, anchorX, anchorY, z, rotation).Count == 0;

	private Actor? GetDisplayActor(int x, int y, int z, Dictionary<string, Actor> actors)
	{
		var occupants = GetActorsAt(x, y, z, actors);
		Actor? best = null;
		foreach (var a in occupants)
		{
			if (best == null) { best = a; continue; }
			if (a.Faction == Factions.Player) { best = a; break; }
			if (a.Faction == Factions.Hostile && best.Faction != Factions.Hostile) best = a;
		}
		return best;
	}

	// ══════════════════════════════════════════════════════
	//  Fixture 便捷方法（兼容旧 MapModule API）
	// ══════════════════════════════════════════════════════

	public bool HasFixture(int x, int y, int z, string fixtureId) =>
		GetEntities(x, y, z).Any(e => e.Type == CellEntityType.Fixture && e.EntityId == fixtureId);

	private static bool FixtureBlocksSight(CellEntity fixture)
	{
		if (fixture.Meta != null
			&& fixture.Meta.TryGetValue(EntityBlocksSightMetaKey, out var rawBlocksSight)
			&& bool.TryParse(rawBlocksSight, out var blocksSight))
		{
			return blocksSight;
		}

		return FixtureRegistry.Get(fixture.EntityId)?.BlocksSight ?? fixture.EntityId == Entities.House;
	}

	public string GetFixtureId(int x, int y, int z)
	{
		var f = GetFirstEntity(x, y, z, CellEntityType.Fixture);
		return f?.EntityId ?? "";
	}

	public void SetFixture(int x, int y, int z, string glyph, string entityId)
	{
		var (chunk, lx, ly) = Resolve(x, y, z);
		chunk.RemoveEntitiesByType(lx, ly, CellEntityType.Fixture);
		RemoveNestAt(chunk, lx, ly);
		if (string.IsNullOrEmpty(glyph))
			return;

		chunk.PushEntity(lx, ly, new CellEntity
		{
			Type = CellEntityType.Fixture,
			Glyph = glyph,
			EntityId = entityId,
			Meta = BuildFixtureMeta(entityId),
		});
		if (entityId == Entities.Nest)
		{
			var coord = CoordUtil.LocalToWorld(chunk.Coord, lx, ly);
			chunk.Nests.Add(new NestData { X = coord.X, Y = coord.Y });
			chunk.Dirty = true;
		}
	}

	private static void RemoveNestAt(ChunkData chunk, int lx, int ly)
	{
		if (chunk.Nests.Count == 0)
			return;

		var coord = CoordUtil.LocalToWorld(chunk.Coord, lx, ly);
		if (chunk.Nests.RemoveAll(n => n.X == coord.X && n.Y == coord.Y) > 0)
			chunk.Dirty = true;
	}

	// ══════════════════════════════════════════════════════
	//  Item 便捷方法
	// ══════════════════════════════════════════════════════

	public void PlaceItem(int x, int y, int z, Item item)
	{
		item.EnsureRuntimeState();
		if (TryMergeGroundItem(x, y, z, item))
			return;

		PushEntity(x, y, z, new CellEntity
		{
			Type = CellEntityType.Item,
			Glyph = item.IsContainer ? "C" : "!",
			EntityId = item.InstanceId,
			Meta = BuildItemMeta(item),
		});
	}

	public Item? PickupItem(int x, int y, int z, string entityId)
	{
		var (chunk, lx, ly) = Resolve(x, y, z);
		var idx = ly * ChunkData.Size + lx;
		if (!chunk.Entities.TryGetValue(idx, out var list)) return null;

		for (var i = 0; i < list.Count; i++)
		{
			if (list[i].Type != CellEntityType.Item || list[i].EntityId != entityId) continue;
			var entity = list[i];
			list.RemoveAt(i);
			if (list.Count == 0) chunk.Entities.Remove(idx);
			chunk.Dirty = true;
			return RestoreItemFromEntity(entity);
		}
		return null;
	}

	public List<Item> PeekGroundItems(int x, int y, int z)
	{
		var items = new List<Item>();
		foreach (var e in GetEntitiesByType(x, y, z, CellEntityType.Item))
			items.Add(RestoreItemFromEntity(e));
		return items;
	}

	public bool UpdateGroundItem(int x, int y, int z, Item item)
	{
		item.EnsureRuntimeState();
		var (chunk, lx, ly) = Resolve(x, y, z);
		var idx = ly * ChunkData.Size + lx;
		if (!chunk.Entities.TryGetValue(idx, out var list))
			return false;

		for (var i = 0; i < list.Count; i++)
		{
			if (list[i].Type != CellEntityType.Item || !string.Equals(list[i].EntityId, item.InstanceId, System.StringComparison.Ordinal))
				continue;

			list[i].Glyph = item.IsContainer ? "C" : "!";
			list[i].Meta = BuildItemMeta(item);
			chunk.Dirty = true;
			return true;
		}

		return false;
	}

	public static string ResolveGroundItemTemplateId(CellEntity entity)
	{
		if (entity.Meta != null
			&& entity.Meta.TryGetValue(ItemTemplateIdMetaKey, out var templateId)
			&& !string.IsNullOrWhiteSpace(templateId))
		{
			return templateId;
		}

		return entity.EntityId;
	}

	private static Item RestoreItemFromEntity(CellEntity entity)
	{
		if (entity.Meta != null
			&& entity.Meta.TryGetValue(ItemSnapshotMetaKey, out var snapshotJson)
			&& ItemSnapshotMapper.Deserialize(snapshotJson) is { } snapshotItem)
		{
			if (!string.IsNullOrWhiteSpace(entity.EntityId))
				snapshotItem.InstanceId = entity.EntityId;
			snapshotItem.EnsureRuntimeState();
			return snapshotItem;
		}

		return ItemSnapshotMapper.CreateLegacyItem(entity.EntityId, entity.Meta);
	}

	private static Dictionary<string, string> BuildItemMeta(Item item)
	{
		item.EnsureRuntimeState();
		return new Dictionary<string, string>(System.StringComparer.Ordinal)
		{
			[ItemSnapshotMetaKey] = ItemSnapshotMapper.Serialize(item),
			[ItemTemplateIdMetaKey] = item.Id,
			[ItemInstanceIdMetaKey] = item.InstanceId,
			[ItemCategoryMetaKey] = item.Category,
			[ItemDurabilityMetaKey] = item.Durability.ToString(),
			[ItemMaxDurabilityMetaKey] = item.MaxDurability.ToString(),
		};
	}

	private bool TryMergeGroundItem(int x, int y, int z, Item item)
	{
		if (!item.IsStackable)
			return false;

		var (chunk, lx, ly) = Resolve(x, y, z);
		var idx = ly * ChunkData.Size + lx;
		if (!chunk.Entities.TryGetValue(idx, out var list))
			return false;

		for (var i = 0; i < list.Count; i++)
		{
			if (list[i].Type != CellEntityType.Item)
				continue;

			var existing = RestoreItemFromEntity(list[i]);
			if (!existing.CanStackWith(item))
				continue;

			existing.MergeFrom(item);
			list[i].Meta = BuildItemMeta(existing);
			chunk.Dirty = true;
			if (item.SafeStackCount <= 0)
				return true;
		}

		return item.SafeStackCount <= 0;
	}

	private static Dictionary<string, string>? BuildFixtureMeta(string entityId)
	{
		var def = FixtureRegistry.Get(entityId);
		if (def == null)
			return null;

		return new Dictionary<string, string>(StringComparer.Ordinal)
		{
			[EntityDurabilityMetaKey] = Math.Max(0, def.MaxDurability).ToString(),
			[EntityMaxDurabilityMetaKey] = Math.Max(0, def.MaxDurability).ToString(),
			[EntityMaterialMetaKey] = def.Material ?? string.Empty,
			[EntityFlammableMetaKey] = def.Flammable.ToString(),
			[EntityBlocksSightMetaKey] = def.BlocksSight.ToString(),
			[EntityControlledFireSourceMetaKey] = def.ControlledFireSource.ToString(),
		};
	}

	public static string ResolveFixtureGlyph(string fixtureId) => fixtureId switch
	{
		Entities.StairDown => ">",
		Entities.StairUp => "<",
		Entities.Nest => "N",
		Entities.Door => "D",
		Entities.House => "H",
		Entities.Campfire => "*",
		Entities.Fire => "*",
		_ => string.IsNullOrWhiteSpace(fixtureId) ? string.Empty : fixtureId[..1],
	};

	// ══════════════════════════════════════════════════════
	//  Chunk Actor 注册
	// ══════════════════════════════════════════════════════

	/// <summary>将 Actor 注册到其所在 chunk。</summary>
	public void RegisterActor(Actor actor)
	{
		var cc = CoordUtil.WorldToChunk(actor.X, actor.Y, actor.Z);
		if (Chunks.IsLoaded(cc))
			Chunks.GetOrLoad(cc).ActorIds.Add(actor.Id);
	}

	/// <summary>Actor 移动后更新 chunk 注册。</summary>
	public void UpdateActorChunk(Actor actor, int oldX, int oldY, int oldZ)
	{
		var oldCc = CoordUtil.WorldToChunk(oldX, oldY, oldZ);
		var newCc = CoordUtil.WorldToChunk(actor.X, actor.Y, actor.Z);
		if (oldCc == newCc) return;

		if (Chunks.IsLoaded(oldCc))
			Chunks.GetOrLoad(oldCc).ActorIds.Remove(actor.Id);
		if (Chunks.IsLoaded(newCc))
			Chunks.GetOrLoad(newCc).ActorIds.Add(actor.Id);
	}

	/// <summary>从 chunk 中注销 Actor。</summary>
	public void UnregisterActor(Actor actor)
	{
		var cc = CoordUtil.WorldToChunk(actor.X, actor.Y, actor.Z);
		if (Chunks.IsLoaded(cc))
			Chunks.GetOrLoad(cc).ActorIds.Remove(actor.Id);
	}

	/// <summary>
	/// 重新构建当前已加载 chunk 的 actor 索引。
	/// 主要用于读档后恢复了 state.Actors，但 WorldMap 刚重建的场景。
	/// </summary>
	public void RebuildLoadedActorIndex(Dictionary<string, Actor> actors)
	{
		foreach (var chunk in Chunks.LoadedChunks.Values)
			chunk.ActorIds.Clear();

		foreach (var actor in actors.Values)
			RegisterActor(actor);
	}

	private static List<Actor> ScanActorsAt(int x, int y, int z, Dictionary<string, Actor> actors) =>
		actors.Values
			.Where(actor => actor.X == x && actor.Y == y && actor.Z == z)
			.OrderBy(static actor => actor.Id, StringComparer.Ordinal)
			.ToList();

	private bool TryGetFacilityOccupancy(int x, int y, int z, out FacilityOccupancy occupancy) =>
		_facilityIndex.TryGetValue(new ZoneCell(x, y, z), out occupancy);

	private static bool ShouldFacilityBlockSight(FacilityInstance facility, FacilityFootprintCell cell) =>
		facility.Stage is FacilityStage.Construct or FacilityStage.Active or FacilityStage.Broken
		&& cell.BlocksSight;

	private static bool IsFacilityPassable(FacilityInstance facility, FacilityFootprintCell cell) =>
		facility.Stage is FacilityStage.Blueprint or FacilityStage.DeliverMaterials
			? true
			: cell.Passable;

	private static IEnumerable<(int WorldX, int WorldY, int WorldZ, FacilityFootprintCell FootprintCell)> EnumerateFootprint(
		FacilityDef def,
		int anchorX,
		int anchorY,
		int z,
		FacilityRotation rotation)
	{
		var effectiveRotation = def.CanRotate ? rotation : FacilityRotation.North;
		var anchorCell = def.GetAnchorCell();
		foreach (var cell in def.Footprint)
		{
			var (rotatedX, rotatedY) = RotateOffset(cell.X - anchorCell.X, cell.Y - anchorCell.Y, effectiveRotation);
			yield return (
				anchorX + rotatedX,
				anchorY + rotatedY,
				z,
				new FacilityFootprintCell
				{
					X = rotatedX,
					Y = rotatedY,
					Passable = cell.Passable,
					BlocksSight = cell.BlocksSight,
					Glyph = string.IsNullOrWhiteSpace(cell.Glyph) ? def.Glyph : cell.Glyph,
				});
		}
	}

	private static (int X, int Y) RotateOffset(int x, int y, FacilityRotation rotation) => rotation switch
	{
		FacilityRotation.East => (-y, x),
		FacilityRotation.South => (-x, -y),
		FacilityRotation.West => (y, -x),
		_ => (x, y),
	};
}
