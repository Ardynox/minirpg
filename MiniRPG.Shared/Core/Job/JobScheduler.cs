using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Facility;
using MiniRPG.Core.World;

namespace MiniRPG.Core.Job;

/// <summary>
/// 工作调度器：扫描 JobBoard 上的 WorkTicket，为空闲工人分配最优任务。
/// 纯函数，无状态——所有状态存在 GameState.JobBoardState 和 Actor 上。
/// </summary>
public static class JobScheduler
{
	/// <summary>每隔多少回合重建一次 ticket 列表。</summary>
	private const int RebuildIntervalTurns = 5;

	/// <summary>工人搜索任务的最大曼哈顿距离。</summary>
	private const int MaxJobSearchRadius = 30;

	/// <summary>
	/// 为指定工人寻找最优可执行任务。
	/// 返回 null 表示当前无可用任务。
	/// </summary>
	public static WorkTicket? FindBestTicket(GameState state, Actor worker)
	{
		EnsureTicketsUpToDate(state);
		var board = state.JobBoardState;
		if (board.Tickets.Count == 0)
			return null;

		WorkTicket? best = null;
		var bestScore = int.MinValue;

		foreach (var ticket in board.Tickets.Values)
		{
			if (!IsAvailableForWorker(state, ticket, worker))
				continue;

			var score = ScoreTicket(state, ticket, worker);
			if (score > bestScore)
			{
				bestScore = score;
				best = ticket;
			}
		}

		return best;
	}

	/// <summary>
	/// 工人领取任务：设置 reservation。
	/// </summary>
	public static bool TryReserve(GameState state, WorkTicket ticket, Actor worker)
	{
		if (!string.IsNullOrEmpty(ticket.ReservedByActorId)
			&& !string.Equals(ticket.ReservedByActorId, worker.Id, StringComparison.Ordinal))
		{
			return false;
		}

		ticket.ReservedByActorId = worker.Id;
		return true;
	}

	/// <summary>
	/// 释放工人对任务的预留。
	/// </summary>
	public static void Release(WorkTicket ticket)
	{
		ticket.ReservedByActorId = string.Empty;
	}

	/// <summary>
	/// 释放指定工人的所有预留。
	/// </summary>
	public static void ReleaseAll(GameState state, string actorId)
	{
		foreach (var ticket in state.JobBoardState.Tickets.Values)
		{
			if (string.Equals(ticket.ReservedByActorId, actorId, StringComparison.Ordinal))
				ticket.ReservedByActorId = string.Empty;
		}
	}

	/// <summary>
	/// 获取工人当前已预留的任务（如果有）。
	/// </summary>
	public static WorkTicket? GetReservedTicket(GameState state, Actor worker)
	{
		foreach (var ticket in state.JobBoardState.Tickets.Values)
		{
			if (string.Equals(ticket.ReservedByActorId, worker.Id, StringComparison.Ordinal))
				return ticket;
		}

		return null;
	}

	/// <summary>
	/// 确保 ticket 列表是最新的。按间隔自动重建。
	/// </summary>
	public static void EnsureTicketsUpToDate(GameState state)
	{
		var board = state.JobBoardState;
		if (board.LastRebuildTurn >= 0
			&& state.Turn - board.LastRebuildTurn < RebuildIntervalTurns)
		{
			return;
		}

		RebuildAllTickets(state);
	}

	/// <summary>
	/// 完整重建所有 ticket：建造 + 制作 + 搬运 + 补给燃料。
	/// 保留已有 reservation。
	/// </summary>
	public static void RebuildAllTickets(GameState state)
	{
		var board = state.JobBoardState;
		// 保存现有 reservation 映射
		var reservations = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var ticket in board.Tickets.Values)
		{
			if (!string.IsNullOrEmpty(ticket.ReservedByActorId))
				reservations[$"{ticket.Type}:{ticket.FacilityId}:{ticket.RecipeId}:{ticket.RequiredItemId}"] = ticket.ReservedByActorId;
		}

		board.Tickets.Clear();

		foreach (var facility in state.Facilities.Values.OrderBy(static f => f.Id, StringComparer.Ordinal))
		{
			var def = FacilityRegistry.Get(facility.FacilityDefId);
			if (def == null)
				continue;

			// 建造相关 ticket
			EmitConstructionTickets(board, facility, def);

			// 运营中设施的 ticket
			if (facility.IsOperational)
			{
				EmitFuelTickets(board, facility, def);
				EmitProductionTickets(board, facility, def);
				EmitHaulOutputTickets(board, facility);
			}
		}

		// 恢复 reservation
		foreach (var ticket in board.Tickets.Values)
		{
			var key = $"{ticket.Type}:{ticket.FacilityId}:{ticket.RecipeId}:{ticket.RequiredItemId}";
			if (reservations.TryGetValue(key, out var actorId))
				ticket.ReservedByActorId = actorId;
		}

		board.LastRebuildTurn = state.Turn;
	}

	/// <summary>
	/// 完成一个 ticket：从 board 中移除。
	/// </summary>
	public static void CompleteTicket(GameState state, string ticketId)
	{
		state.JobBoardState.Tickets.Remove(ticketId);
	}

	// ── 内部：Ticket 生成 ──

	private static void EmitConstructionTickets(JobBoardState board, FacilityInstance facility, FacilityDef def)
	{
		switch (facility.Stage)
		{
			case FacilityStage.DeliverMaterials:
				foreach (var missing in FacilityConstructionModule.GetMissingConstructionMaterials(facility))
				{
					var id = board.AllocateTicketId();
					board.Tickets[id] = new WorkTicket
					{
						Id = id,
						Type = WorkTicketType.DeliverConstructionMaterial,
						Priority = 50,
						FacilityId = facility.Id,
						OwnerDomainId = facility.OwnerDomainId,
						RequiredItemId = missing.ItemId,
						RequiredCount = missing.Count,
						WorkRemaining = 1,
					};
				}
				break;

			case FacilityStage.Construct:
				var constructId = board.AllocateTicketId();
				board.Tickets[constructId] = new WorkTicket
				{
					Id = constructId,
					Type = WorkTicketType.ConstructFacility,
					Priority = 60,
					FacilityId = facility.Id,
					OwnerDomainId = facility.OwnerDomainId,
					WorkRemaining = 1,
				};
				break;

			case FacilityStage.Broken:
				var repairId = board.AllocateTicketId();
				board.Tickets[repairId] = new WorkTicket
				{
					Id = repairId,
					Type = WorkTicketType.RepairFacility,
					Priority = 40,
					FacilityId = facility.Id,
					OwnerDomainId = facility.OwnerDomainId,
					WorkRemaining = 1,
				};
				break;
		}
	}

	private static void EmitFuelTickets(JobBoardState board, FacilityInstance facility, FacilityDef def)
	{
		if (!def.RequiresFuel)
			return;

		// 燃料不足时生成补给 ticket
		var fuelItems = facility.FuelBuffer?.Sum(static item => item.StackCount > 0 ? item.StackCount : 1) ?? 0;
		if (fuelItems >= def.FuelCapacity)
			return;

		var fuelId = board.AllocateTicketId();
		board.Tickets[fuelId] = new WorkTicket
		{
			Id = fuelId,
			Type = WorkTicketType.RefuelFacility,
			Priority = 30,
			FacilityId = facility.Id,
			OwnerDomainId = facility.OwnerDomainId,
			RequiredItemId = def.FuelItemIds.FirstOrDefault() ?? "",
			RequiredCount = def.FuelCapacity - fuelItems,
			WorkRemaining = 1,
		};
	}

	private static void EmitProductionTickets(JobBoardState board, FacilityInstance facility, FacilityDef def)
	{
		foreach (var bill in facility.Bills)
		{
			if (!bill.Enabled)
				continue;

			var recipe = RecipeRegistry.Get(bill.RecipeId);
			if (recipe == null)
				continue;

			// 检查是否已达到目标产量
			var outputCount = facility.OutputBuffer?
				.Where(item => recipe.Outputs.Any(o =>
					string.Equals(o.ItemId, item.Id, StringComparison.Ordinal)))
				.Sum(static item => item.StackCount > 0 ? item.StackCount : 1) ?? 0;
			if (outputCount >= bill.TargetCount)
				continue;

			var ticketId = board.AllocateTicketId();
			board.Tickets[ticketId] = new WorkTicket
			{
				Id = ticketId,
				Type = WorkTicketType.ProduceRecipe,
				Priority = 20,
				FacilityId = facility.Id,
				OwnerDomainId = facility.OwnerDomainId,
				RecipeId = bill.RecipeId,
				BillId = bill.Id,
				WorkRemaining = recipe.WorkTurns,
			};
		}
	}

	private static void EmitHaulOutputTickets(JobBoardState board, FacilityInstance facility)
	{
		if (facility.OutputBuffer == null || facility.OutputBuffer.Count == 0)
			return;

		var haulId = board.AllocateTicketId();
		board.Tickets[haulId] = new WorkTicket
		{
			Id = haulId,
			Type = WorkTicketType.HaulFacilityOutput,
			Priority = 10,
			FacilityId = facility.Id,
			OwnerDomainId = facility.OwnerDomainId,
			WorkRemaining = 1,
		};
	}

	// ── 内部：评分与过滤 ──

	private static bool IsAvailableForWorker(GameState state, WorkTicket ticket, Actor worker)
	{
		// 已被其他人预留
		if (!string.IsNullOrEmpty(ticket.ReservedByActorId)
			&& !string.Equals(ticket.ReservedByActorId, worker.Id, StringComparison.Ordinal))
		{
			return false;
		}

		// 设施必须存在
		if (!state.Facilities.TryGetValue(ticket.FacilityId, out var facility))
			return false;

		// 距离检查
		var dist = Math.Abs(facility.AnchorX - worker.X) + Math.Abs(facility.AnchorY - worker.Y);
		if (dist > MaxJobSearchRadius || facility.Z != worker.Z)
			return false;

		// 制作任务需要检查能力
		if (ticket.Type == WorkTicketType.ProduceRecipe)
		{
			var recipe = RecipeRegistry.Get(ticket.RecipeId);
			if (recipe != null && !MeetsRecipeRequirements(worker, recipe))
				return false;
		}

		// 搬运材料任务需要检查工人是否持有所需物品
		if (ticket.Type == WorkTicketType.DeliverConstructionMaterial
			|| ticket.Type == WorkTicketType.RefuelFacility
			|| ticket.Type == WorkTicketType.SupplyFacilityInput)
		{
			if (!string.IsNullOrEmpty(ticket.RequiredItemId)
				&& !HasOrCanFindItem(state, worker, ticket.RequiredItemId))
			{
				return false;
			}
		}

		return true;
	}

	private static int ScoreTicket(GameState state, WorkTicket ticket, Actor worker)
	{
		if (!state.Facilities.TryGetValue(ticket.FacilityId, out var facility))
			return int.MinValue;

		var dist = Math.Abs(facility.AnchorX - worker.X) + Math.Abs(facility.AnchorY - worker.Y);

		// 基础分 = 优先级 * 100 - 距离
		return ticket.Priority * 100 - dist;
	}

	private static bool MeetsRecipeRequirements(Actor worker, RecipeDef recipe)
	{
		foreach (var (capId, minValue) in recipe.RequiredCapacities)
		{
			if (worker.GetCapacity(capId) < minValue)
				return false;
		}

		return true;
	}

	private static bool HasOrCanFindItem(GameState state, Actor worker, string itemId)
	{
		// 先检查背包
		if (InventoryModule.CountMatching(worker, item =>
			!item.Equipped && string.Equals(item.Id, itemId, StringComparison.Ordinal)) > 0)
		{
			return true;
		}

		// 检查附近 stockpile 是否有
		foreach (var zone in state.StockpileZones)
		{
			if (!zone.AllowOutput)
				continue;

			foreach (var cell in zone.Cells)
			{
				if (cell.Z != worker.Z)
					continue;

				var dist = Math.Abs(cell.X - worker.X) + Math.Abs(cell.Y - worker.Y);
				if (dist > MaxJobSearchRadius)
					continue;

				if (state.World == null)
					continue;

				foreach (var entity in state.World.GetEntitiesByType(cell.X, cell.Y, cell.Z, CellEntityType.Item))
				{
					var templateId = WorldMap.ResolveGroundItemTemplateId(entity);
					if (string.Equals(templateId, itemId, StringComparison.Ordinal))
						return true;
				}
			}
		}

		return false;
	}
}
