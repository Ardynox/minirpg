using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Farm;

/// <summary>
/// 农业模块：种植、生长、收获。
/// 
/// 生命周期：
///   1. 玩家在种植区指定作物 → ZoneDef.CropId
///   2. 工人执行种植任务 → FarmModule.Plant()
///   3. 每回合 TurnModule 调用 → FarmModule.TickGrowth()
///   4. 成熟后工人执行收获任务 → FarmModule.Harvest()
///   5. 如果 AutoReplant，收获后自动重新种植
/// 
/// 数据存储：
///   - CropInstance 存在 GameState.Crops 字典中
///   - 作物也作为 CellEntity 存在 WorldMap 中（用于渲染）
/// </summary>
public static class FarmModule
{
	/// <summary>
	/// 种植作物。
	/// </summary>
	public static FarmActionResult Plant(GameState state, string cropDefId, int x, int y, int z)
	{
		var def = CropRegistry.Get(cropDefId);
		if (def == null)
			return new FarmActionResult { FailureReason = "unknown_crop" };

		// 检查是否已有作物
		var existingKey = MakeKey(x, y, z);
		if (state.Crops.ContainsKey(existingKey))
			return new FarmActionResult { FailureReason = "cell_occupied" };

		var crop = new CropInstance
		{
			Id = $"crop_{x}_{y}_{z}_{state.Turn}",
			CropDefId = cropDefId,
			X = x,
			Y = y,
			Z = z,
			Growth = 0,
			PlantedTurn = state.Turn,
		};

		state.Crops[existingKey] = crop;

		return new FarmActionResult
		{
			Success = true,
			Events =
			{
				new GameEvent("crop_planted")
				{
					TargetX = x,
					TargetY = y,
					TargetZ = z,
					ItemName = def.Name,
					ItemTypeId = cropDefId,
				},
			},
		};
	}

	/// <summary>
	/// 每回合推进所有作物的生长。
	/// 在 TurnModule.AdvanceWorld() 中调用。
	/// </summary>
	public static List<GameEvent> TickGrowth(GameState state)
	{
		var events = new List<GameEvent>();

		foreach (var crop in state.Crops.Values)
		{
			if (crop.Mature || crop.Withered)
				continue;

			var def = CropRegistry.Get(crop.CropDefId);
			if (def == null)
				continue;

			// 季节检查
			if (def.Seasons.Count > 0 && !IsInSeason(state, def))
			{
				crop.Withered = true;
				events.Add(new GameEvent("crop_withered")
				{
					TargetX = crop.X,
					TargetY = crop.Y,
					TargetZ = crop.Z,
					ItemName = def.Name,
					ItemTypeId = crop.CropDefId,
				});
				continue;
			}

			// 生长
			crop.Growth++;
			if (crop.Growth >= def.GrowthTurns)
			{
				crop.Mature = true;
				events.Add(new GameEvent("crop_mature")
				{
					TargetX = crop.X,
					TargetY = crop.Y,
					TargetZ = crop.Z,
					ItemName = def.Name,
					ItemTypeId = crop.CropDefId,
				});
			}
		}

		return events;
	}

	/// <summary>
	/// 收获成熟作物。
	/// </summary>
	public static FarmActionResult Harvest(GameState state, int x, int y, int z, Actor? harvester = null)
	{
		var key = MakeKey(x, y, z);
		if (!state.Crops.TryGetValue(key, out var crop))
			return new FarmActionResult { FailureReason = "no_crop" };

		if (!crop.Mature)
			return new FarmActionResult { FailureReason = "not_mature" };

		var def = CropRegistry.Get(crop.CropDefId);
		if (def == null)
			return new FarmActionResult { FailureReason = "unknown_crop" };

		var rng = new Random(state.RngSeed + state.Turn + x * 31 + y * 17);
		var yield = rng.Next(def.HarvestMin, def.HarvestMax + 1);

		var result = new FarmActionResult { Success = true };

		// 产出物品。走 PresetDB.CloneItem 而不是裸 new，让 Tags / Weight / Nutrition 等 preset 字段
		// 一次性带过来；同时传 state.Turn，让食物自动登记 SpawnedAtTurn 用于保质期评估。
		for (var i = 0; i < yield; i++)
		{
			if (!PresetDB.Items.ContainsKey(def.HarvestItemId))
				continue;

			var item = PresetDB.CloneItem(def.HarvestItemId, state.Turn);

			if (harvester != null)
			{
				InventoryModule.Add(harvester, item);
			}
			else
			{
				state.World?.PlaceItem(x, y, z, item);
			}
		}

		result.Events.Add(new GameEvent("crop_harvested")
		{
			TargetX = x,
			TargetY = y,
			TargetZ = z,
			ItemName = def.Name,
		});

		// 移除作物
		state.Crops.Remove(key);

		// 自动重新种植
		if (def.AutoReplant)
		{
			var replant = Plant(state, crop.CropDefId, x, y, z);
			if (replant.Success)
				result.Events.AddRange(replant.Events);
		}

		return result;
	}

	/// <summary>
	/// 移除作物（枯萎清理/手动移除）。
	/// </summary>
	public static bool Remove(GameState state, int x, int y, int z)
	{
		var key = MakeKey(x, y, z);
		return state.Crops.Remove(key);
	}

	/// <summary>获取指定位置的作物。</summary>
	public static CropInstance? GetCropAt(GameState state, int x, int y, int z)
	{
		var key = MakeKey(x, y, z);
		return state.Crops.TryGetValue(key, out var crop) ? crop : null;
	}

	/// <summary>获取所有成熟可收获的作物。</summary>
	public static List<CropInstance> GetMatureCrops(GameState state) =>
		state.Crops.Values.Where(static c => c.Mature && !c.Withered).ToList();

	/// <summary>获取所有枯萎的作物。</summary>
	public static List<CropInstance> GetWitheredCrops(GameState state) =>
		state.Crops.Values.Where(static c => c.Withered).ToList();

	/// <summary>获取作物生长百分比（0.0 ~ 1.0）。</summary>
	public static float GetGrowthPercent(CropInstance crop)
	{
		var def = CropRegistry.Get(crop.CropDefId);
		if (def == null || def.GrowthTurns <= 0)
			return 0f;

		return Math.Clamp((float)crop.Growth / def.GrowthTurns, 0f, 1f);
	}

	// ── 内部 ──

	private static string MakeKey(int x, int y, int z) => $"{x},{y},{z}";

	private static bool IsInSeason(GameState state, CropDef def)
	{
		// 简单实现：根据回合数推算季节
		// 每 100 回合一个季节：spring(0-99), summer(100-199), autumn(200-299), winter(300-399)
		var seasonIndex = (state.Turn / 100) % 4;
		var currentSeason = seasonIndex switch
		{
			0 => "spring",
			1 => "summer",
			2 => "autumn",
			_ => "winter",
		};

		return def.Seasons.Contains(currentSeason, StringComparer.OrdinalIgnoreCase);
	}
}

public sealed class FarmActionResult
{
	public bool Success { get; init; }
	public string FailureReason { get; init; } = "";
	public List<GameEvent> Events { get; init; } = [];
}
