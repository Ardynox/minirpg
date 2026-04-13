using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;
using MiniRPG.Core.Facility;

namespace MiniRPG.Core.World;

public static class RuntimeBuildActionModule
{
	public static List<ItemAmount> GetTerrainCosts(string? terrainId) =>
		CloneItemAmounts(TerrainBuildRuleRegistry.Get(terrainId)?.Costs);

	public static List<ItemAmount> GetFacilityCosts(string? facilityDefId) =>
		CloneItemAmounts(FacilityRegistry.Get(facilityDefId ?? string.Empty)?.ConstructionCost);

	public static List<ItemAmount> GetFacilityDemolishRefund(FacilityInstance? facility)
	{
		if (facility == null)
			return [];

		if (facility.Stage is FacilityStage.Active or FacilityStage.Broken)
			return GetFacilityCosts(facility.FacilityDefId);

		return CloneItemAmounts(facility.DeliveredConstructionMaterials);
	}

	public static List<ItemAmount> ResolveMissingMaterials(Actor? actor, IEnumerable<ItemAmount>? costs)
	{
		if (actor == null)
			return CloneItemAmounts(costs);

		var missing = new List<ItemAmount>();
		foreach (var cost in costs ?? [])
		{
			if (cost == null || string.IsNullOrWhiteSpace(cost.ItemId) || cost.Count <= 0)
				continue;

			var available = InventoryModule.CountMatching(actor, item =>
				!item.Equipped &&
				string.Equals(item.Id, cost.ItemId, StringComparison.Ordinal));
			if (available >= cost.Count)
				continue;

			missing.Add(new ItemAmount
			{
				ItemId = cost.ItemId,
				Count = cost.Count - available,
			});
		}

		return missing;
	}

	public static bool CanAffordMaterials(Actor? actor, IEnumerable<ItemAmount>? costs) =>
		ResolveMissingMaterials(actor, costs).Count == 0;

	public static bool ConsumeMaterials(Actor actor, IEnumerable<ItemAmount>? costs)
	{
		var normalized = CloneItemAmounts(costs);
		if (normalized.Count == 0)
			return true;
		if (!CanAffordMaterials(actor, normalized))
			return false;

		foreach (var cost in normalized)
		{
			var consumed = InventoryModule.ConsumeMatching(actor, item =>
				!item.Equipped &&
				string.Equals(item.Id, cost.ItemId, StringComparison.Ordinal), cost.Count);
			if (consumed != cost.Count)
				return false;
		}

		return true;
	}

	public static void RefundMaterials(Actor actor, IEnumerable<ItemAmount>? materials)
	{
		foreach (var amount in CloneItemAmounts(materials))
			AddMaterials(actor, amount.ItemId, amount.Count);
	}

	public static bool TryExecuteTerrainBuild(
		GameState state,
		Actor actor,
		string? terrainId,
		int x,
		int y,
		int z,
		bool freeBuild)
	{
		if (state.World == null || string.IsNullOrWhiteSpace(terrainId))
			return false;

		var costs = GetTerrainCosts(terrainId);
		if (!freeBuild && costs.Count == 0)
			return false;

		var current = state.World.GetTerrain(x, y, z).StringId;
		if (current is not (Terrains.Air or Terrains.Void))
			return false;
		if (current == terrainId)
			return false;
		if (terrainId is not (Terrains.Air or Terrains.Void)
			&& !BlockPlacementRules.HasFaceConnectedTerrain(state.World, x, y, z))
		{
			return false;
		}
		if (!freeBuild && !ConsumeMaterials(actor, costs))
			return false;

		state.World.SetTerrain(x, y, z, terrainId);
		return true;
	}

	public static bool TryExecuteTerrainDemolish(
		GameState state,
		Actor actor,
		int x,
		int y,
		int z,
		bool freeBuild)
	{
		if (state.World == null)
			return false;

		var terrainId = state.World.GetTerrain(x, y, z).StringId;
		if (terrainId is Terrains.Air or Terrains.Void)
			return false;

		var refund = GetTerrainCosts(terrainId);
		if (!freeBuild && refund.Count == 0)
			return false;

		state.World.SetTerrain(x, y, z, Terrains.Air);
		if (!freeBuild)
			RefundMaterials(actor, refund);
		return true;
	}

	public static bool TryExecuteFacilityPlaceBlueprint(
		GameState state,
		Actor actor,
		string? facilityDefId,
		int anchorX,
		int anchorY,
		int z,
		FacilityRotation rotation,
		bool freeBuild)
	{
		if (string.IsNullOrWhiteSpace(facilityDefId))
			return false;

		var costs = GetFacilityCosts(facilityDefId);
		if (!freeBuild && costs.Count > 0 && !CanAffordMaterials(actor, costs))
			return false;

		var result = FacilityConstructionModule.TryPlaceBlueprint(
			state,
			facilityDefId,
			anchorX,
			anchorY,
			z,
			rotation,
			actor.PrimaryDomainId);
		return result.Success;
	}

	public static bool TryExecuteFacilityDemolish(
		GameState state,
		Actor actor,
		string? facilityId,
		bool freeBuild)
	{
		if (string.IsNullOrWhiteSpace(facilityId))
			return false;
		if (!state.Facilities.TryGetValue(facilityId, out var facility))
			return false;

		var refund = GetFacilityDemolishRefund(facility);
		if (!freeBuild)
			RefundMaterials(actor, refund);

		state.Facilities.Remove(facilityId);
		FacilityConstructionModule.RebuildConstructionTickets(state);
		state.World?.AttachFacilityState(state.Facilities);
		return true;
	}

	private static void AddMaterials(Actor actor, string itemId, int count)
	{
		if (string.IsNullOrWhiteSpace(itemId) || count <= 0)
			return;

		var remaining = count;
		while (remaining > 0)
		{
			var item = PresetDB.CloneItem(itemId);
			item.StackCount = Math.Min(item.MaxStack, remaining);
			item.EnsureRuntimeState();
			InventoryModule.Add(actor, item);
			remaining -= item.StackCount;
		}
	}

	private static List<ItemAmount> CloneItemAmounts(IEnumerable<ItemAmount>? source) =>
		source?
			.Where(static item => item != null && !string.IsNullOrWhiteSpace(item.ItemId) && item.Count > 0)
			.GroupBy(static item => item.ItemId, StringComparer.Ordinal)
			.Select(static group => new ItemAmount
			{
				ItemId = group.Key,
				Count = group.Sum(static item => item.Count),
			})
			.ToList() ?? [];
}
