using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Facility;

public sealed class FacilityPlacementResult
{
	public bool Success { get; init; }
	public string FailureReason { get; init; } = "";
	public string FacilityId { get; init; } = "";
	public FacilityInstance? Facility { get; init; }
	public List<ZoneCell> Blockers { get; init; } = [];
}

public sealed class FacilityWorkResult
{
	public bool Consumed { get; init; }
	public string FailureReason { get; init; } = "";
	public FacilityInstance? Facility { get; init; }
	public List<ItemAmount> MissingMaterials { get; init; } = [];
	public List<ItemAmount> DeliveredMaterials { get; init; } = [];
}

public static class FacilityConstructionModule
{
	/// <summary>
	/// 直接放一个已建成的 facility（<see cref="FacilityStage.Active"/>），跳过蓝图→送料→施工的全流程。
	/// 供地图初始化 / debug / 特殊 incident 使用；正常玩家交互应走 <see cref="TryPlaceBlueprint"/>。
	/// </summary>
	public static FacilityPlacementResult TryPlaceCompleted(
		GameState state,
		string facilityDefId,
		int anchorX,
		int anchorY,
		int z,
		FacilityRotation rotation,
		string? ownerDomainId = null)
	{
		if (state.World == null)
			return new FacilityPlacementResult { FailureReason = "missing_world" };

		var def = FacilityRegistry.Get(facilityDefId);
		if (def == null)
			return new FacilityPlacementResult { FailureReason = "unknown_facility" };

		var effectiveRotation = def.CanRotate ? rotation : FacilityRotation.North;
		var blockers = state.World.GetFacilityBlockers(def, anchorX, anchorY, z, effectiveRotation);
		if (blockers.Count > 0)
			return new FacilityPlacementResult { FailureReason = "blocked", Blockers = blockers };

		state.EnsureDefaultEconomicDomains();
		var facility = new FacilityInstance
		{
			Id = AllocateFacilityId(state, def.Id),
			FacilityDefId = def.Id,
			AnchorX = anchorX,
			AnchorY = anchorY,
			Z = z,
			Rotation = effectiveRotation,
			Stage = FacilityStage.Active,
			OwnerDomainId = string.IsNullOrWhiteSpace(ownerDomainId) ? DomainIds.Player : ownerDomainId,
			AllowPersonalUse = SupportsInteraction(def, FacilityInteractionModes.PersonalUse),
			AllowDomainOrders = SupportsInteraction(def, FacilityInteractionModes.DomainOrder),
			MaxHitPoints = Math.Max(1, def.MaxHitPoints),
			HitPoints = Math.Max(1, def.MaxHitPoints),
		};

		state.Facilities[facility.Id] = facility;
		SyncConstructionRuntime(state);

		return new FacilityPlacementResult
		{
			Success = true,
			FacilityId = facility.Id,
			Facility = facility,
		};
	}

	public static FacilityPlacementResult TryPlaceBlueprint(
		GameState state,
		string facilityDefId,
		int anchorX,
		int anchorY,
		int z,
		FacilityRotation rotation,
		string? ownerDomainId = null)
	{
		if (state.World == null)
		{
			return new FacilityPlacementResult
			{
				FailureReason = "missing_world",
			};
		}

		var def = FacilityRegistry.Get(facilityDefId);
		if (def == null)
		{
			return new FacilityPlacementResult
			{
				FailureReason = "unknown_facility",
			};
		}

		var effectiveRotation = def.CanRotate ? rotation : FacilityRotation.North;
		var blockers = state.World.GetFacilityBlockers(def, anchorX, anchorY, z, effectiveRotation);
		if (blockers.Count > 0)
		{
			return new FacilityPlacementResult
			{
				FailureReason = "blocked",
				Blockers = blockers,
			};
		}

		state.EnsureDefaultEconomicDomains();
		var facility = new FacilityInstance
		{
			Id = AllocateFacilityId(state, def.Id),
			FacilityDefId = def.Id,
			AnchorX = anchorX,
			AnchorY = anchorY,
			Z = z,
			Rotation = effectiveRotation,
			Stage = FacilityStage.Blueprint,
			OwnerDomainId = string.IsNullOrWhiteSpace(ownerDomainId) ? DomainIds.Player : ownerDomainId,
			AllowPersonalUse = SupportsInteraction(def, FacilityInteractionModes.PersonalUse),
			AllowDomainOrders = SupportsInteraction(def, FacilityInteractionModes.DomainOrder),
			MaxHitPoints = Math.Max(1, def.MaxHitPoints),
			HitPoints = Math.Max(1, def.MaxHitPoints),
		};

		state.Facilities[facility.Id] = facility;
		NormalizeConstructionState(facility, def);
		SyncConstructionRuntime(state);

		return new FacilityPlacementResult
		{
			Success = true,
			FacilityId = facility.Id,
			Facility = facility,
		};
	}

	public static List<ItemAmount> GetMissingConstructionMaterials(GameState state, string facilityId)
	{
		if (!state.Facilities.TryGetValue(facilityId, out var facility))
			return [];

		return GetMissingConstructionMaterials(facility);
	}

	public static List<ItemAmount> GetMissingConstructionMaterials(FacilityInstance facility)
	{
		var def = FacilityRegistry.Get(facility.FacilityDefId);
		if (def == null)
			return [];

		var requiredByItem = AggregateItemAmounts(def.ConstructionCost);
		if (requiredByItem.Count == 0)
			return [];

		var deliveredByItem = AggregateItemAmounts(facility.DeliveredConstructionMaterials);
		var missing = new List<ItemAmount>();
		foreach (var requirement in def.ConstructionCost)
		{
			if (!requiredByItem.TryGetValue(requirement.ItemId, out var requiredCount))
				continue;

			var deliveredCount = deliveredByItem.GetValueOrDefault(requirement.ItemId);
			var remaining = Math.Max(0, requiredCount - deliveredCount);
			if (remaining <= 0)
				continue;

			missing.Add(new ItemAmount
			{
				ItemId = requirement.ItemId,
				Count = remaining,
			});
			requiredByItem.Remove(requirement.ItemId);
		}

		return missing;
	}

	public static void RebuildConstructionTickets(GameState state)
	{
		state.JobBoardState ??= new JobBoardState();
		var board = state.JobBoardState;
		board.Tickets.Clear();

		foreach (var facility in state.Facilities.Values.OrderBy(static facility => facility.Id, StringComparer.Ordinal))
		{
			var def = FacilityRegistry.Get(facility.FacilityDefId);
			if (def == null)
				continue;

			NormalizeConstructionState(facility, def);
			switch (facility.Stage)
			{
				case FacilityStage.DeliverMaterials:
					foreach (var missing in GetMissingConstructionMaterials(facility))
					{
						var ticketId = board.AllocateTicketId();
						board.Tickets[ticketId] = new WorkTicket
						{
							Id = ticketId,
							Type = WorkTicketType.DeliverConstructionMaterial,
							FacilityId = facility.Id,
							OwnerDomainId = facility.OwnerDomainId,
							RequiredItemId = missing.ItemId,
							RequiredCount = missing.Count,
							WorkRemaining = 1,
						};
					}
					break;

				case FacilityStage.Construct:
					var constructTicketId = board.AllocateTicketId();
					board.Tickets[constructTicketId] = new WorkTicket
					{
						Id = constructTicketId,
						Type = WorkTicketType.ConstructFacility,
						FacilityId = facility.Id,
						OwnerDomainId = facility.OwnerDomainId,
						WorkRemaining = 1,
					};
					break;
			}
		}

		board.LastRebuildTurn = state.Turn;
	}

	public static bool TryFindNearbyFacility(
		GameState state,
		Actor actor,
		Func<FacilityInstance, bool> predicate,
		bool includeCurrentCell,
		out FacilityInstance? facility)
	{
		foreach (var candidate in EnumerateNearbyFacilities(state, actor, includeCurrentCell))
		{
			if (predicate(candidate))
			{
				facility = candidate;
				return true;
			}
		}

		facility = null;
		return false;
	}

	public static List<WorkTicket> GetConstructionTickets(GameState state, string facilityId) =>
		state.JobBoardState?.Tickets.Values
			.Where(ticket => string.Equals(ticket.FacilityId, facilityId, StringComparison.Ordinal))
			.OrderBy(ticket => ticket.Type)
			.ThenBy(ticket => ticket.RequiredItemId, StringComparer.Ordinal)
			.ToList() ?? [];

	public static FacilityWorkResult TryDeliverMaterials(GameState state, Actor actor, string facilityId)
	{
		if (!state.Facilities.TryGetValue(facilityId, out var facility))
		{
			return new FacilityWorkResult
			{
				FailureReason = "missing_facility",
			};
		}

		var def = FacilityRegistry.Get(facility.FacilityDefId);
		if (def == null)
		{
			return new FacilityWorkResult
			{
				FailureReason = "missing_definition",
				Facility = facility,
			};
		}

		NormalizeConstructionState(facility, def);
		if (!IsAdjacentToFacility(state, actor, facility))
		{
			return new FacilityWorkResult
			{
				FailureReason = "not_adjacent",
				Facility = facility,
			};
		}

		if (facility.Stage != FacilityStage.DeliverMaterials)
		{
			return new FacilityWorkResult
			{
				FailureReason = "wrong_stage",
				Facility = facility,
				MissingMaterials = GetMissingConstructionMaterials(facility),
			};
		}

		var missing = GetMissingConstructionMaterials(facility);
		if (missing.Count == 0)
		{
			NormalizeConstructionState(facility, def);
			SyncConstructionRuntime(state);
			return new FacilityWorkResult
			{
				FailureReason = "ready_to_construct",
				Facility = facility,
			};
		}

		var delivered = new List<ItemAmount>();
		foreach (var requirement in missing)
		{
			var count = InventoryModule.CountMatching(actor, item =>
				!item.Equipped
				&& string.Equals(item.Id, requirement.ItemId, StringComparison.Ordinal));
			if (count <= 0)
				continue;

			var amount = Math.Min(count, requirement.Count);
			if (amount <= 0)
				continue;

			var consumed = InventoryModule.ConsumeMatching(actor, item =>
				!item.Equipped
				&& string.Equals(item.Id, requirement.ItemId, StringComparison.Ordinal), amount);
			if (consumed <= 0)
				continue;

			AddDeliveredMaterials(facility, requirement.ItemId, consumed);
			delivered.Add(new ItemAmount
			{
				ItemId = requirement.ItemId,
				Count = consumed,
			});
		}

		NormalizeConstructionState(facility, def);
		SyncConstructionRuntime(state);

		return new FacilityWorkResult
		{
			Consumed = delivered.Count > 0,
			FailureReason = delivered.Count > 0 ? string.Empty : "missing_inventory_materials",
			Facility = facility,
			DeliveredMaterials = delivered,
			MissingMaterials = GetMissingConstructionMaterials(facility),
		};
	}

	public static FacilityWorkResult TryConstructFacility(GameState state, Actor actor, string facilityId)
	{
		if (!state.Facilities.TryGetValue(facilityId, out var facility))
		{
			return new FacilityWorkResult
			{
				FailureReason = "missing_facility",
			};
		}

		var def = FacilityRegistry.Get(facility.FacilityDefId);
		if (def == null)
		{
			return new FacilityWorkResult
			{
				FailureReason = "missing_definition",
				Facility = facility,
			};
		}

		NormalizeConstructionState(facility, def);
		if (!IsAdjacentToFacility(state, actor, facility))
		{
			return new FacilityWorkResult
			{
				FailureReason = "not_adjacent",
				Facility = facility,
			};
		}

		if (facility.Stage != FacilityStage.Construct)
		{
			return new FacilityWorkResult
			{
				FailureReason = "wrong_stage",
				Facility = facility,
				MissingMaterials = GetMissingConstructionMaterials(facility),
			};
		}

		facility.MaxHitPoints = Math.Max(1, def.MaxHitPoints);
		facility.HitPoints = facility.MaxHitPoints;
		facility.Stage = FacilityStage.Active;
		SyncConstructionRuntime(state);

		return new FacilityWorkResult
		{
			Consumed = true,
			Facility = facility,
		};
	}

	private static void SyncConstructionRuntime(GameState state)
	{
		RebuildConstructionTickets(state);
		state.World?.AttachFacilityState(state.Facilities);
	}

	private static Dictionary<string, int> AggregateItemAmounts(IEnumerable<ItemAmount>? items)
	{
		var totals = new Dictionary<string, int>(StringComparer.Ordinal);
		if (items == null)
			return totals;

		foreach (var item in items)
		{
			if (item == null
				|| string.IsNullOrWhiteSpace(item.ItemId)
				|| item.Count <= 0)
			{
				continue;
			}

			totals[item.ItemId] = totals.GetValueOrDefault(item.ItemId) + item.Count;
		}

		return totals;
	}

	private static void AddDeliveredMaterials(FacilityInstance facility, string itemId, int count)
	{
		if (count <= 0 || string.IsNullOrWhiteSpace(itemId))
			return;

		facility.DeliveredConstructionMaterials ??= [];
		var existing = facility.DeliveredConstructionMaterials
			.FirstOrDefault(entry => string.Equals(entry.ItemId, itemId, StringComparison.Ordinal));
		if (existing != null)
		{
			existing.Count += count;
			return;
		}

		facility.DeliveredConstructionMaterials.Add(new ItemAmount
		{
			ItemId = itemId,
			Count = count,
		});
	}

	private static bool SupportsInteraction(FacilityDef def, string interactionId) =>
		def.Interactions.Any(mode => string.Equals(mode, interactionId, StringComparison.Ordinal));

	private static string AllocateFacilityId(GameState state, string facilityDefId)
	{
		var next = 1;
		while (state.Facilities.ContainsKey($"{facilityDefId}_{next}"))
			next++;
		return $"{facilityDefId}_{next}";
	}

	private static IEnumerable<FacilityInstance> EnumerateNearbyFacilities(GameState state, Actor actor, bool includeCurrentCell)
	{
		if (state.World == null)
			yield break;

		var seenFacilityIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (var cell in EnumerateNearbyCells(actor, includeCurrentCell))
		{
			if (!state.World.TryGetFacilityAt(cell.X, cell.Y, actor.Z, out var facility) || facility == null)
				continue;

			if (seenFacilityIds.Add(facility.Id))
				yield return facility;
		}
	}

	private static IEnumerable<(int X, int Y)> EnumerateNearbyCells(Actor actor, bool includeCurrentCell)
	{
		var seen = new HashSet<(int X, int Y)>();
		foreach (var (dx, dy) in EnumerateOrderedDirections(actor))
		{
			if (seen.Add((dx, dy)))
				yield return (actor.X + dx, actor.Y + dy);
		}

		if (includeCurrentCell)
			yield return (actor.X, actor.Y);
	}

	private static IEnumerable<(int Dx, int Dy)> EnumerateOrderedDirections(Actor actor)
	{
		if (IsCardinalDirection(actor.FacingX, actor.FacingY))
			yield return (actor.FacingX, actor.FacingY);

		yield return (0, -1);
		yield return (1, 0);
		yield return (0, 1);
		yield return (-1, 0);
	}

	private static bool IsAdjacentToFacility(GameState state, Actor actor, FacilityInstance facility)
	{
		if (actor.Z != facility.Z)
			return false;

		var def = FacilityRegistry.Get(facility.FacilityDefId);
		if (def == null)
			return false;

		var footprint = state.World?.GetFootprintCells(facility)
			?? GetFootprintCells(def, facility.AnchorX, facility.AnchorY, facility.Z, facility.Rotation);
		foreach (var cell in footprint)
		{
			var distance = Math.Abs(cell.X - actor.X) + Math.Abs(cell.Y - actor.Y);
			if (distance == 1)
				return true;
		}

		return false;
	}

	private static List<ZoneCell> GetFootprintCells(FacilityDef def, int anchorX, int anchorY, int z, FacilityRotation rotation)
	{
		var cells = new List<ZoneCell>(def.Footprint.Count);
		var effectiveRotation = def.CanRotate ? rotation : FacilityRotation.North;
		var anchorCell = def.GetAnchorCell();
		foreach (var cell in def.Footprint)
		{
			var (rotatedX, rotatedY) = RotateOffset(cell.X - anchorCell.X, cell.Y - anchorCell.Y, effectiveRotation);
			cells.Add(new ZoneCell(anchorX + rotatedX, anchorY + rotatedY, z));
		}

		return cells;
	}

	private static (int X, int Y) RotateOffset(int x, int y, FacilityRotation rotation) => rotation switch
	{
		FacilityRotation.East => (-y, x),
		FacilityRotation.South => (-x, -y),
		FacilityRotation.West => (y, -x),
		_ => (x, y),
	};

	private static bool IsCardinalDirection(int dx, int dy) =>
		(dx == 0 && Math.Abs(dy) == 1) || (dy == 0 && Math.Abs(dx) == 1);

	private static void NormalizeConstructionState(FacilityInstance facility, FacilityDef def)
	{
		if (facility.Stage is FacilityStage.Active or FacilityStage.Broken)
			return;

		facility.Stage = GetMissingConstructionMaterials(facility).Count > 0
			? FacilityStage.DeliverMaterials
			: FacilityStage.Construct;

		if (facility.MaxHitPoints <= 0)
			facility.MaxHitPoints = Math.Max(1, def.MaxHitPoints);
		if (facility.HitPoints <= 0)
			facility.HitPoints = facility.MaxHitPoints;
	}
}
