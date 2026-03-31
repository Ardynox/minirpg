using System.Collections.Generic;
using System.Linq;

namespace MiniRPG.Core.World;

/// <summary>
/// 统一的三维世界访问 API。
/// 所有地图操作通过此类进行，内部路由到对应的 ChunkData。
/// 替代旧的 GameState.Cells / MapWidth / MapHeight。
/// </summary>
public class WorldMap
{
	public ChunkManager Chunks { get; }
	public int WorldSeed { get; }

	public WorldMap(int worldSeed, IMapGenerator generator)
	{
		WorldSeed = worldSeed;
		Chunks = new ChunkManager(worldSeed, generator);
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
		return chunk.RemoveEntity(lx, ly, type, entityId);
	}

	public int RemoveEntitiesByType(int x, int y, int z, CellEntityType type)
	{
		var (chunk, lx, ly) = Resolve(x, y, z);
		return chunk.RemoveEntitiesByType(lx, ly, type);
	}

	public List<CellEntity> GetEntitiesByType(int x, int y, int z, CellEntityType type) =>
		GetEntities(x, y, z).Where(e => e.Type == type).ToList();

	public CellEntity? GetFirstEntity(int x, int y, int z, CellEntityType type) =>
		GetEntities(x, y, z).FirstOrDefault(e => e.Type == type);

	// ══════════════════════════════════════════════════════
	//  查询
	// ══════════════════════════════════════════════════════

	public bool IsSolid(int x, int y, int z) =>
		GetTerrain(x, y, z).Solid;

	public bool IsWalkable(int x, int y, int z) =>
		!IsSolid(x, y, z);

	/// <summary>
	/// 渲染用：获取格子的显示字符。
	/// 优先显示 Actor（player > hostile > 其他），否则显示栈顶实体 Glyph，否则显示地形 Glyph。
	/// </summary>
	public string GetDisplayCell(int x, int y, int z, Dictionary<string, Actor> actors)
	{
		var actor = GetDisplayActor(x, y, z, actors);
		if (actor != null) return actor.Glyph;

		var entities = GetEntities(x, y, z);
		if (entities.Count > 0) return entities[^1].Glyph;

		return GetTerrain(x, y, z).Glyph;
	}

	private static Actor? GetDisplayActor(int x, int y, int z, Dictionary<string, Actor> actors)
	{
		Actor? best = null;
		foreach (var a in actors.Values)
		{
			if (a.X != x || a.Y != y || a.Z != z) continue;
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

	public string GetFixtureId(int x, int y, int z)
	{
		var f = GetFirstEntity(x, y, z, CellEntityType.Fixture);
		return f?.EntityId ?? "";
	}

	public void SetFixture(int x, int y, int z, string glyph, string entityId)
	{
		RemoveEntitiesByType(x, y, z, CellEntityType.Fixture);
		if (!string.IsNullOrEmpty(glyph))
			PushEntity(x, y, z, new CellEntity { Type = CellEntityType.Fixture, Glyph = glyph, EntityId = entityId });
	}

	// ══════════════════════════════════════════════════════
	//  Item 便捷方法
	// ══════════════════════════════════════════════════════

	public void PlaceItem(int x, int y, int z, Item item)
	{
		PushEntity(x, y, z, new CellEntity
		{
			Type = CellEntityType.Item,
			Glyph = "!",
			EntityId = item.Id,
			Meta = new Dictionary<string, string>
			{
				["name"] = item.Name,
				["price"] = item.Price.ToString(),
				["tags"] = SerializeItemTags(item.Tags),
			},
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

	private static Item RestoreItemFromEntity(CellEntity entity)
	{
		var meta = entity.Meta ?? new Dictionary<string, string>();
		return new Item
		{
			Id = entity.EntityId,
			Name = meta.GetValueOrDefault("name", entity.EntityId),
			Price = int.TryParse(meta.GetValueOrDefault("price", "0"), out var p) ? p : 0,
			Tags = DeserializeItemTags(meta.GetValueOrDefault("tags", "")),
		};
	}

	private static string SerializeItemTags(Dictionary<string, int> tags)
	{
		var parts = new List<string>();
		foreach (var (k, v) in tags) parts.Add($"{k}={v}");
		return string.Join(";", parts);
	}

	private static Dictionary<string, int> DeserializeItemTags(string raw)
	{
		var tags = new Dictionary<string, int>();
		if (string.IsNullOrEmpty(raw)) return tags;
		foreach (var pair in raw.Split(';'))
		{
			var kv = pair.Split('=', 2);
			if (kv.Length == 2 && int.TryParse(kv[1], out var val)) tags[kv[0]] = val;
		}
		return tags;
	}

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
}
