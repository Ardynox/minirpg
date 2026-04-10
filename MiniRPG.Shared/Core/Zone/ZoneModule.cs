using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Zone;

/// <summary>
/// 区域类型枚举。
/// </summary>
public enum ZoneType
{
	/// <summary>存储区（已有 StockpileZone 的泛化）。</summary>
	Stockpile,

	/// <summary>种植区。</summary>
	Growing,

	/// <summary>家畜区。</summary>
	Animal,

	/// <summary>禁区（NPC 不进入）。</summary>
	Forbidden,

	/// <summary>家区（NPC 休息/社交的区域）。</summary>
	Home,

	/// <summary>垃圾区（丢弃物品）。</summary>
	Dumping,
}

/// <summary>
/// 通用区域定义。
/// 每个区域由一组格子组成，有类型和所有者。
/// </summary>
public sealed class ZoneDef
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	[JsonPropertyName("type")]
	public ZoneType Type { get; set; }

	[JsonPropertyName("ownerDomainId")]
	public string OwnerDomainId { get; set; } = "";

	[JsonPropertyName("cells")]
	public List<ZoneCell> Cells { get; set; } = [];

	/// <summary>种植区专用：种植的作物 ID。</summary>
	[JsonPropertyName("cropId")]
	public string CropId { get; set; } = "";

	/// <summary>家畜区专用：允许的动物类型 ID 列表。</summary>
	[JsonPropertyName("allowedAnimalIds")]
	public List<string> AllowedAnimalIds { get; set; } = [];

	/// <summary>存储区专用：允许的物品过滤。</summary>
	[JsonPropertyName("allowedItemIds")]
	public List<string> AllowedItemIds { get; set; } = [];

	[JsonPropertyName("allowedCategories")]
	public List<string> AllowedCategories { get; set; } = [];

	[JsonPropertyName("priority")]
	public int Priority { get; set; } = 50;

	[JsonPropertyName("enabled")]
	public bool Enabled { get; set; } = true;
}

/// <summary>
/// 区域管理模块：创建、删除、查询区域。
/// 
/// 设计原则：
/// - ZoneDef 存储在 GameState.Zones 中
/// - 与现有 StockpileZone 兼容（可以迁移）
/// - Zone 数据跟随 GameState 序列化
/// - 工作系统通过 ZoneModule 查询种植区、存储区等
/// </summary>
public static class ZoneModule
{
	private static int _nextId;

	/// <summary>创建新区域。</summary>
	public static ZoneDef CreateZone(GameState state, ZoneType type, string name, string ownerDomainId)
	{
		var zone = new ZoneDef
		{
			Id = $"zone_{_nextId++}_{state.Turn}",
			Name = name,
			Type = type,
			OwnerDomainId = ownerDomainId,
		};
		state.Zones[zone.Id] = zone;
		return zone;
	}

	/// <summary>删除区域。</summary>
	public static bool RemoveZone(GameState state, string zoneId) =>
		state.Zones.Remove(zoneId);

	/// <summary>向区域添加格子。</summary>
	public static void AddCell(ZoneDef zone, int x, int y, int z)
	{
		if (zone.Cells.Any(c => c.X == x && c.Y == y && c.Z == z))
			return;

		zone.Cells.Add(new ZoneCell { X = x, Y = y, Z = z });
	}

	/// <summary>从区域移除格子。</summary>
	public static void RemoveCell(ZoneDef zone, int x, int y, int z)
	{
		zone.Cells.RemoveAll(c => c.X == x && c.Y == y && c.Z == z);
	}

	/// <summary>查询指定位置属于哪些区域。</summary>
	public static List<ZoneDef> GetZonesAt(GameState state, int x, int y, int z) =>
		state.Zones.Values
			.Where(zone => zone.Enabled && zone.Cells.Any(c => c.X == x && c.Y == y && c.Z == z))
			.ToList();

	/// <summary>查询指定类型的所有区域。</summary>
	public static List<ZoneDef> GetZonesByType(GameState state, ZoneType type) =>
		state.Zones.Values
			.Where(zone => zone.Enabled && zone.Type == type)
			.ToList();

	/// <summary>查询指定位置是否在禁区内。</summary>
	public static bool IsForbidden(GameState state, int x, int y, int z) =>
		state.Zones.Values.Any(zone =>
			zone.Enabled
			&& zone.Type == ZoneType.Forbidden
			&& zone.Cells.Any(c => c.X == x && c.Y == y && c.Z == z));

	/// <summary>查询指定位置是否在家区内。</summary>
	public static bool IsHome(GameState state, int x, int y, int z) =>
		state.Zones.Values.Any(zone =>
			zone.Enabled
			&& zone.Type == ZoneType.Home
			&& zone.Cells.Any(c => c.X == x && c.Y == y && c.Z == z));

	/// <summary>获取最近的指定类型区域的格子。</summary>
	public static (int X, int Y, int Z)? FindNearestCell(GameState state, ZoneType type, int fromX, int fromY, int fromZ)
	{
		(int X, int Y, int Z)? best = null;
		var bestDist = int.MaxValue;

		foreach (var zone in state.Zones.Values)
		{
			if (!zone.Enabled || zone.Type != type)
				continue;

			foreach (var cell in zone.Cells)
			{
				if (cell.Z != fromZ)
					continue;

				var dist = Math.Abs(cell.X - fromX) + Math.Abs(cell.Y - fromY);
				if (dist < bestDist)
				{
					bestDist = dist;
					best = (cell.X, cell.Y, cell.Z);
				}
			}
		}

		return best;
	}

	/// <summary>获取种植区中需要种植的空格子。</summary>
	public static List<(int X, int Y, int Z, string CropId)> GetEmptyGrowingCells(GameState state)
	{
		var result = new List<(int X, int Y, int Z, string CropId)>();

		foreach (var zone in state.Zones.Values)
		{
			if (!zone.Enabled || zone.Type != ZoneType.Growing || string.IsNullOrEmpty(zone.CropId))
				continue;

			foreach (var cell in zone.Cells)
			{
				// 检查该格子是否已有作物（通过 CellEntity 检查）
				if (state.World != null)
				{
					var entities = state.World.GetEntitiesByType(cell.X, cell.Y, cell.Z, Data.CellEntityType.Fixture);
					var hasCrop = entities.Any(e =>
						e.Meta != null && e.Meta.ContainsKey("crop"));
					if (hasCrop)
						continue;
				}

				result.Add((cell.X, cell.Y, cell.Z, zone.CropId));
			}
		}

		return result;
	}
}
