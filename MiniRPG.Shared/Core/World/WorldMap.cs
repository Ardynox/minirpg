using System;
using System.Collections.Generic;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.World;

/// <summary>
/// 统一的三维世界访问 API — 薄路由层。
/// 实际逻辑委托给 Terrain / Entities / Actors / Facilities / Query 五个子服务。
/// 旧方法签名保留为转发代理，确保现有调用者编译不中断。
/// </summary>
public class WorldMap
{
	public ChunkManager Chunks { get; }
	public int WorldSeed { get; }

	// ── 子服务 ──
	public TerrainAccess Terrain { get; }
	public EntityAccess Entities { get; }
	public ActorIndex Actors { get; }
	public FacilityIndex Facilities { get; }
	public WorldQuery Query { get; }

	public WorldMap(int worldSeed, IMapGenerator generator)
	{
		WorldSeed = worldSeed;
		Chunks = new ChunkManager(worldSeed, generator);
		Chunks.ApplyRuntimeConfig(GameConfig.WorldRuntime);

		Terrain = new TerrainAccess(Chunks);
		Entities = new EntityAccess(Chunks);
		Actors = new ActorIndex(Chunks);
		Facilities = new FacilityIndex();
		Query = new WorldQuery(Terrain, Entities, Actors, Facilities);
	}

	// ══════════════════════════════════════════════════════
	//  设施状态绑定（转发到 FacilityIndex）
	// ══════════════════════════════════════════════════════

	public void AttachFacilityState(IReadOnlyDictionary<string, FacilityInstance>? facilities) =>
		Facilities.AttachFacilityState(facilities);

	public void RebuildFacilityIndex() =>
		Facilities.RebuildIndex();

	// ══════════════════════════════════════════════════════
	//  地形操作（转发到 TerrainAccess）
	// ══════════════════════════════════════════════════════

	public ushort GetTerrainId(int x, int y, int z) => Terrain.GetTerrainId(x, y, z);
	public TerrainDef GetTerrain(int x, int y, int z) => Terrain.GetTerrain(x, y, z);
	public void SetTerrainId(int x, int y, int z, ushort terrainId) => Terrain.SetTerrainId(x, y, z, terrainId);
	public void SetTerrain(int x, int y, int z, string terrainStringId) => Terrain.SetTerrain(x, y, z, terrainStringId);
	public byte GetHardness(int x, int y, int z) => Terrain.GetHardness(x, y, z);
	public void SetHardness(int x, int y, int z, byte hardness) => Terrain.SetHardness(x, y, z, hardness);

	// ══════════════════════════════════════════════════════
	//  实体栈操作（转发到 EntityAccess）
	// ══════════════════════════════════════════════════════

	public List<CellEntity> GetEntities(int x, int y, int z) => Entities.GetEntities(x, y, z);
	public void PushEntity(int x, int y, int z, CellEntity entity) => Entities.PushEntity(x, y, z, entity);
	public bool RemoveEntity(int x, int y, int z, CellEntityType type, string entityId) => Entities.RemoveEntity(x, y, z, type, entityId);
	public int RemoveEntitiesByType(int x, int y, int z, CellEntityType type) => Entities.RemoveEntitiesByType(x, y, z, type);
	public List<CellEntity> GetEntitiesByType(int x, int y, int z, CellEntityType type) => Entities.GetEntitiesByType(x, y, z, type);
	public CellEntity? GetFirstEntity(int x, int y, int z, CellEntityType type) => Entities.GetFirstEntity(x, y, z, type);
	public CellEntity? GetEntity(int x, int y, int z, CellEntityType type, string entityId) => Entities.GetEntity(x, y, z, type, entityId);
	public bool UpdateEntity(int x, int y, int z, CellEntityType type, string entityId, Action<CellEntity> update) => Entities.UpdateEntity(x, y, z, type, entityId, update);

	// ── Fixture 便捷方法 ──
	public bool HasFixture(int x, int y, int z, string fixtureId) => Entities.HasFixture(x, y, z, fixtureId);
	public bool HasVerticalAnchor(int x, int y, int z, bool goDown) => Entities.HasVerticalAnchor(x, y, z, goDown);
	public string GetFixtureId(int x, int y, int z) => Entities.GetFixtureId(x, y, z);
	public void SetFixture(int x, int y, int z, string glyph, string entityId) => Entities.SetFixture(x, y, z, glyph, entityId);
	public static string ResolveFixtureGlyph(string fixtureId) => EntityAccess.ResolveFixtureGlyph(fixtureId);

	// ── Item 便捷方法 ──
	public void PlaceItem(int x, int y, int z, Item item) => Entities.PlaceItem(x, y, z, item);
	public Item? PickupItem(int x, int y, int z, string entityId) => Entities.PickupItem(x, y, z, entityId);
	public List<Item> PeekGroundItems(int x, int y, int z) => Entities.PeekGroundItems(x, y, z);
	public bool UpdateGroundItem(int x, int y, int z, Item item) => Entities.UpdateGroundItem(x, y, z, item);
	public static string ResolveGroundItemTemplateId(CellEntity entity) => EntityAccess.ResolveGroundItemTemplateId(entity);

	// ══════════════════════════════════════════════════════
	//  查询（转发到 WorldQuery）
	// ══════════════════════════════════════════════════════

	public bool IsSolid(int x, int y, int z) => Query.IsSolid(x, y, z);
	public bool BlocksSight(int x, int y, int z) => Query.BlocksSight(x, y, z);
	public bool IsWalkable(int x, int y, int z) => Query.IsWalkable(x, y, z);
	public bool IsWeatherExposed(int x, int y, int z) => Query.IsWeatherExposed(x, y, z);
	public bool CanTraverseVertical(int x, int y, int z, bool goDown) => Query.CanTraverseVertical(x, y, z, goDown);
	public string GetDisplayCell(int x, int y, int z, Dictionary<string, Actor> actors) => Query.GetDisplayCell(x, y, z, actors);
	public bool HasActorOrEntity(int x, int y, int z, Dictionary<string, Actor> actors) => Query.HasActorOrEntity(x, y, z, actors);

	// ══════════════════════════════════════════════════════
	//  Actor 查询 & 注册（转发到 ActorIndex）
	// ══════════════════════════════════════════════════════

	public List<Actor> GetActorsAt(int x, int y, int z, Dictionary<string, Actor> actors) => Actors.GetActorsAt(x, y, z, actors);
	public Actor? GetActorAt(int x, int y, int z, Dictionary<string, Actor> actors) => Actors.GetActorAt(x, y, z, actors);
	public Actor? GetHostileActorAt(int x, int y, int z, Dictionary<string, Actor> actors) => Actors.GetHostileActorAt(x, y, z, actors);
	public void RegisterActor(Actor actor) => Actors.RegisterActor(actor);
	public void UpdateActorChunk(Actor actor, int oldX, int oldY, int oldZ) => Actors.UpdateActorChunk(actor, oldX, oldY, oldZ);
	public void UnregisterActor(Actor actor) => Actors.UnregisterActor(actor);
	public void RebuildLoadedActorIndex(Dictionary<string, Actor> actors) => Actors.RebuildLoadedActorIndex(actors);

	// ══════════════════════════════════════════════════════
	//  设施查询（转发到 FacilityIndex）
	// ══════════════════════════════════════════════════════

	public bool TryGetFacilityAt(int x, int y, int z, out FacilityInstance? facility) => Facilities.TryGetFacilityAt(x, y, z, out facility);

	public bool TryGetFacilityAt(int x, int y, int z, out FacilityInstance? facility, out FacilityFootprintCell? footprintCell) =>
		Facilities.TryGetFacilityAt(x, y, z, out facility, out footprintCell);

	public List<ZoneCell> GetFootprintCells(FacilityInstance facility) => Facilities.GetFootprintCells(facility);

	public List<ZoneCell> GetFootprintCells(FacilityDef def, int anchorX, int anchorY, int z, FacilityRotation rotation) =>
		Facilities.GetFootprintCells(def, anchorX, anchorY, z, rotation);

	public List<ZoneCell> GetFacilityBlockers(FacilityDef def, int anchorX, int anchorY, int z, FacilityRotation rotation) =>
		Facilities.GetBlockers(def, anchorX, anchorY, z, rotation, Terrain, Entities);

	public bool CanPlaceFacility(FacilityDef def, int anchorX, int anchorY, int z, FacilityRotation rotation) =>
		Facilities.CanPlace(def, anchorX, anchorY, z, rotation, Terrain, Entities);
}
