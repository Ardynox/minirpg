using System.Collections.Generic;

namespace MiniRPG.Core.World;

/// <summary>
/// 单个 chunk 的运行时数据。
/// 地形层用紧凑数组（ushort 地形ID + byte 硬度），非地形实体用稀疏字典。
/// 水平 32x32 格，Z 方向每层一个 chunk。
/// </summary>
public class ChunkData
{
	public const int Size = CoordUtil.ChunkSize;
	public const int Area = Size * Size;

	public ChunkCoord Coord { get; set; }

	/// <summary>chunk 被修改过（挖掘/建造等），需要存盘而非重新生成。</summary>
	public bool Dirty { get; set; }

	/// <summary>最近一次被访问的回合数，用于 LRU 卸载。</summary>
	public int LastAccessTurn { get; set; }

	/// <summary>地形 ID 层：terrainIds[ly * Size + lx]。</summary>
	public ushort[] TerrainIds { get; set; } = new ushort[Area];

	/// <summary>硬度层：hardness[ly * Size + lx]。0 = 不可挖掘/已是空地，>0 = 剩余硬度。</summary>
	public byte[] Hardness { get; set; } = new byte[Area];

	/// <summary>
	/// 稀疏实体层：只有包含非地形实体的格子才有条目。
	/// key = ly * Size + lx，value = 该格的实体栈（Fixture/Item/Hazard/Corpse/Effect）。
	/// </summary>
	public Dictionary<int, List<CellEntity>> Entities { get; set; } = new();

	/// <summary>此 chunk 范围内的 Actor ID 集合，便于快速遍历。</summary>
	public HashSet<string> ActorIds { get; set; } = [];

	/// <summary>此 chunk 内的巢穴列表。</summary>
	public List<NestData> Nests { get; set; } = [];
	public byte[] SnowDepth { get; set; } = new byte[Area];
	public byte[] SandDepth { get; set; } = new byte[Area];
	public byte[] Wetness { get; set; } = new byte[Area];
	public byte[] IceDepth { get; set; } = new byte[Area];
	public int LastWeatherSimTurn { get; set; }

	// ── 便捷访问 ──

	public ushort GetTerrainId(int lx, int ly) =>
		TerrainIds[ly * Size + lx];

	public void SetTerrainId(int lx, int ly, ushort id)
	{
		TerrainIds[ly * Size + lx] = id;
		Dirty = true;
	}

	public byte GetHardness(int lx, int ly) =>
		Hardness[ly * Size + lx];

	public void SetHardness(int lx, int ly, byte h)
	{
		Hardness[ly * Size + lx] = h;
		Dirty = true;
	}

	public List<CellEntity> GetEntities(int lx, int ly)
	{
		var idx = ly * Size + lx;
		return Entities.TryGetValue(idx, out var list) ? list : [];
	}

	public void PushEntity(int lx, int ly, CellEntity entity)
	{
		var idx = ly * Size + lx;
		if (!Entities.TryGetValue(idx, out var list))
		{
			list = [];
			Entities[idx] = list;
		}
		var insertAt = list.Count;
		for (var i = 0; i < list.Count; i++)
		{
			if (list[i].Type > entity.Type) { insertAt = i; break; }
		}
		list.Insert(insertAt, entity);
		Dirty = true;
	}

	public bool RemoveEntity(int lx, int ly, CellEntityType type, string entityId)
	{
		var idx = ly * Size + lx;
		if (!Entities.TryGetValue(idx, out var list)) return false;
		for (var i = 0; i < list.Count; i++)
		{
			if (list[i].Type == type && list[i].EntityId == entityId)
			{
				list.RemoveAt(i);
				if (list.Count == 0) Entities.Remove(idx);
				Dirty = true;
				return true;
			}
		}
		return false;
	}

	public int RemoveEntitiesByType(int lx, int ly, CellEntityType type)
	{
		var idx = ly * Size + lx;
		if (!Entities.TryGetValue(idx, out var list)) return 0;
		var removed = list.RemoveAll(e => e.Type == type);
		if (list.Count == 0) Entities.Remove(idx);
		if (removed > 0) Dirty = true;
		return removed;
	}

	/// <summary>用默认硬度初始化整个 chunk 为指定地形。</summary>
	public void Fill(ushort terrainId)
	{
		var def = TerrainRegistry.Get(terrainId);
		for (var i = 0; i < Area; i++)
		{
			TerrainIds[i] = terrainId;
			Hardness[i] = def.DefaultHardness;
		}
	}

	/// <summary>设置地形并自动同步默认硬度。</summary>
	public void SetTerrain(int lx, int ly, ushort terrainId)
	{
		var idx = ly * Size + lx;
		TerrainIds[idx] = terrainId;
		Hardness[idx] = TerrainRegistry.Get(terrainId).DefaultHardness;
		Dirty = true;
	}
}
