using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.World;

/// <summary>
/// 实体栈操作服务：Fixture / Item / Hazard / Container 等非地形实体的增删查改。
/// </summary>
public class EntityAccess
{
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

	private readonly ChunkManager _chunks;

	public EntityAccess(ChunkManager chunks)
	{
		_chunks = chunks;
	}

	private (ChunkData Chunk, int Lx, int Ly) Resolve(int x, int y, int z)
	{
		var cc = CoordUtil.WorldToChunk(x, y, z);
		var (lx, ly) = CoordUtil.WorldToLocal(x, y);
		var chunk = _chunks.GetOrLoad(cc);
		return (chunk, lx, ly);
	}

	// ── 基础实体栈操作 ──

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

	// ── Fixture 便捷方法 ──

	public bool HasFixture(int x, int y, int z, string fixtureId) =>
		GetEntities(x, y, z).Any(e => e.Type == CellEntityType.Fixture && e.EntityId == fixtureId);

	public bool HasVerticalAnchor(int x, int y, int z, bool goDown)
	{
		if (HasFixture(x, y, z, Entities.Ladder))
			return true;

		return goDown
			? HasFixture(x, y, z, Entities.StairDown)
			: HasFixture(x, y, z, Entities.StairUp);
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

	/// <summary>Fixture 是否遮挡视线。</summary>
	public static bool FixtureBlocksSight(CellEntity fixture)
	{
		if (fixture.Meta != null
			&& fixture.Meta.TryGetValue(EntityBlocksSightMetaKey, out var rawBlocksSight)
			&& bool.TryParse(rawBlocksSight, out var blocksSight))
		{
			return blocksSight;
		}

		return FixtureRegistry.Get(fixture.EntityId)?.BlocksSight ?? fixture.EntityId == Entities.House;
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
		Entities.Ladder => "|",
		_ => string.IsNullOrWhiteSpace(fixtureId) ? string.Empty : fixtureId[..1],
	};

	// ── Item 便捷方法 ──

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
			if (list[i].Type != CellEntityType.Item || !string.Equals(list[i].EntityId, item.InstanceId, StringComparison.Ordinal))
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

	/// <summary>地图渲染：从 Meta 取物品模板 id 与 <see cref="Item.Category"/>（用于 <c>item_world_render.json</c> 解析）。</summary>
	public static bool TryGetGroundItemRenderKeys(CellEntity entity, out string templateId, out string category)
	{
		templateId = "";
		category = ItemCategories.Misc;
		if (entity.Type != CellEntityType.Item || entity.Meta == null)
			return false;
		if (!entity.Meta.TryGetValue(ItemTemplateIdMetaKey, out var tid) || string.IsNullOrWhiteSpace(tid))
			return false;
		templateId = tid;
		if (entity.Meta.TryGetValue(ItemCategoryMetaKey, out var cat) && !string.IsNullOrWhiteSpace(cat))
			category = cat;
		return true;
	}

	// ── 内部工具 ──

	internal static Item RestoreItemFromEntity(CellEntity entity)
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
		return new Dictionary<string, string>(StringComparer.Ordinal)
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

	private static void RemoveNestAt(ChunkData chunk, int lx, int ly)
	{
		if (chunk.Nests.Count == 0)
			return;

		var coord = CoordUtil.LocalToWorld(chunk.Coord, lx, ly);
		if (chunk.Nests.RemoveAll(n => n.X == coord.X && n.Y == coord.Y) > 0)
			chunk.Dirty = true;
	}
}
