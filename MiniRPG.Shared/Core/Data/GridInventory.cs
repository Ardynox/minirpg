using System;
using System.Collections.Generic;

namespace MiniRPG.Core.Data;

/// <summary>
/// 网格容器类型：决定容器在 UI 上分组、装备来源、是否可嵌套。
/// </summary>
public enum GridContainerKind
{
	Pocket,
	Backpack,
	Vest,
	Belt,
	Holster,
	ContainerItem,
	GroundTile,
}

/// <summary>
/// 一件物品在某个网格里的占位记录。
/// Width/Height 已经是 effective size（旋转后已 swap），grid 占位算法直接用，
/// 不需要再回查 <see cref="ItemSizeRegistry"/>。
/// </summary>
public sealed class GridPlacement
{
	public string ItemInstanceId { get; set; } = "";
	public int X { get; set; }
	public int Y { get; set; }
	public int Width { get; set; } = 1;
	public int Height { get; set; } = 1;
	public bool Rotated { get; set; }
}

/// <summary>
/// 一个网格容器：固定 W×H 的二维矩阵 + 占位列表。
/// Inventory 仍然是 <see cref="Actor.Inventory"/>（truth），grid 只是空间索引；
/// 由 <c>InventoryModule</c> 在 Add/Remove/Equip 时同步。
/// </summary>
public sealed class GridInventory
{
	public string Id { get; set; } = "";
	public int Width { get; set; }
	public int Height { get; set; }
	public GridContainerKind Kind { get; set; }
	public List<GridPlacement> Placements { get; set; } = [];

	/// <summary>玩家自带的口袋（始终存在）。</summary>
	public const string PocketsId = "pockets";
	/// <summary>装备槽里某件容器物品提供的子网格 id 前缀。</summary>
	public const string EquippedPrefix = "equipped:";
	/// <summary>放在网格里的容器物品自己的子网格 id 前缀（嵌套，Phase 4）。</summary>
	public const string ContainerPrefix = "container:";

	public static string MakeEquippedId(string itemInstanceId) =>
		EquippedPrefix + (itemInstanceId ?? string.Empty);

	public static string MakeContainerId(string itemInstanceId) =>
		ContainerPrefix + (itemInstanceId ?? string.Empty);

	public GridPlacement? FindPlacement(string itemInstanceId)
	{
		if (string.IsNullOrEmpty(itemInstanceId))
			return null;

		foreach (var p in Placements)
		{
			if (string.Equals(p.ItemInstanceId, itemInstanceId, StringComparison.Ordinal))
				return p;
		}

		return null;
	}

	public bool RemovePlacement(string itemInstanceId)
	{
		if (string.IsNullOrEmpty(itemInstanceId))
			return false;

		for (var i = 0; i < Placements.Count; i++)
		{
			if (string.Equals(Placements[i].ItemInstanceId, itemInstanceId, StringComparison.Ordinal))
			{
				Placements.RemoveAt(i);
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// 矩形 (x,y,w,h) 是否可以放进 grid（不越界、不与已有 placement 重叠）。
	/// excludeInstanceId 用于"移动一件已存在物品"场景：忽略它自己的旧 placement。
	/// </summary>
	public bool CanPlaceAt(int x, int y, int w, int h, string? excludeInstanceId = null)
	{
		if (w <= 0 || h <= 0)
			return false;
		if (x < 0 || y < 0)
			return false;
		if (x + w > Width || y + h > Height)
			return false;

		foreach (var p in Placements)
		{
			if (excludeInstanceId != null
				&& string.Equals(p.ItemInstanceId, excludeInstanceId, StringComparison.Ordinal))
			{
				continue;
			}

			if (Overlap(x, y, w, h, p.X, p.Y, p.Width, p.Height))
				return false;
		}

		return true;
	}

	/// <summary>
	/// 自动为指定物品找一个空位放进去。
	/// 优先 (left→right, top→bottom) 扫第一格能放下的位置；
	/// 若 allowRotation 且原方向放不下，尝试旋转 90°。
	/// 返回写入的 placement 或 null（放不下）。
	/// </summary>
	public GridPlacement? TryAutoPlace(string itemInstanceId, int width, int height, bool allowRotation)
	{
		if (string.IsNullOrEmpty(itemInstanceId))
			return null;
		if (width <= 0 || height <= 0)
			return null;

		var placement = FindFreeSpot(itemInstanceId, width, height, rotated: false);
		if (placement != null)
			return placement;

		if (!allowRotation || width == height)
			return null;

		return FindFreeSpot(itemInstanceId, height, width, rotated: true);
	}

	/// <summary>当前 placement 占用的格子数（含旋转后的 effective area）。</summary>
	public int OccupiedCells
	{
		get
		{
			var total = 0;
			foreach (var p in Placements)
				total += p.Width * p.Height;
			return total;
		}
	}

	public int FreeCells => Math.Max(0, Width * Height - OccupiedCells);

	private GridPlacement? FindFreeSpot(string itemInstanceId, int w, int h, bool rotated)
	{
		for (var y = 0; y <= Height - h; y++)
		{
			for (var x = 0; x <= Width - w; x++)
			{
				if (!CanPlaceAt(x, y, w, h, excludeInstanceId: itemInstanceId))
					continue;

				var placement = new GridPlacement
				{
					ItemInstanceId = itemInstanceId,
					X = x,
					Y = y,
					Width = w,
					Height = h,
					Rotated = rotated,
				};
				Placements.Add(placement);
				return placement;
			}
		}

		return null;
	}

	private static bool Overlap(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh) =>
		ax < bx + bw && ax + aw > bx && ay < by + bh && ay + ah > by;
}
