using System;
using System.Collections.Generic;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Facility;
using MiniRPG.Core.World;

namespace MiniRPG.Core.Job;

/// <summary>
/// 工作执行器：根据 WorkTicket 类型执行具体的工作动作。
/// 纯函数，返回 ActionExecutionResult。
/// </summary>
public static class JobExecutor
{
	/// <summary>
	/// 执行一步工作。工人可能需要多步才能完成一个 ticket：
	/// 1. 移动到物品位置拾取
	/// 2. 移动到设施位置
	/// 3. 执行工作动作
	/// </summary>
	public static ActionExecutionResult Execute(GameState state, Actor worker, WorkTicket ticket)
	{
		return ticket.Type switch
		{
			WorkTicketType.DeliverConstructionMaterial => ExecuteDeliver(state, worker, ticket),
			WorkTicketType.ConstructFacility => ExecuteConstruct(state, worker, ticket),
			WorkTicketType.RefuelFacility => ExecuteRefuel(state, worker, ticket),
			WorkTicketType.SupplyFacilityInput => ExecuteSupplyInput(state, worker, ticket),
			WorkTicketType.ProduceRecipe => ExecuteProduce(state, worker, ticket),
			WorkTicketType.HaulFacilityOutput => ExecuteHaulOutput(state, worker, ticket),
			WorkTicketType.RepairFacility => ExecuteRepair(state, worker, ticket),
			_ => new ActionExecutionResult(),
		};
	}

	// ── 搬运建材 ──

	private static ActionExecutionResult ExecuteDeliver(GameState state, Actor worker, WorkTicket ticket)
	{
		if (!state.Facilities.TryGetValue(ticket.FacilityId, out var facility))
			return Fail(state, ticket);

		// 如果背包没有所需物品，先去拾取
		var hasItem = InventoryModule.CountMatching(worker, item =>
			!item.Equipped && string.Equals(item.Id, ticket.RequiredItemId, StringComparison.Ordinal)) > 0;

		if (!hasItem)
			return TryPickupItem(state, worker, ticket.RequiredItemId);

		// 有物品了，移动到设施旁边
		if (!IsAdjacentTo(worker, facility))
			return TryMoveToward(state, worker, facility.AnchorX, facility.AnchorY);

		// 到达设施旁，执行交付
		var result = FacilityConstructionModule.TryDeliverMaterials(state, worker, ticket.FacilityId);
		if (result.Consumed)
			JobScheduler.CompleteTicket(state, ticket.Id);

		return new ActionExecutionResult
		{
			Consumed = result.Consumed,
		};
	}

	// ── 建造设施 ──

	private static ActionExecutionResult ExecuteConstruct(GameState state, Actor worker, WorkTicket ticket)
	{
		if (!state.Facilities.TryGetValue(ticket.FacilityId, out var facility))
			return Fail(state, ticket);

		if (!IsAdjacentTo(worker, facility))
			return TryMoveToward(state, worker, facility.AnchorX, facility.AnchorY);

		var result = FacilityConstructionModule.TryConstructFacility(state, worker, ticket.FacilityId);
		if (result.Consumed)
			JobScheduler.CompleteTicket(state, ticket.Id);

		return new ActionExecutionResult
		{
			Consumed = result.Consumed,
		};
	}

	// ── 补充燃料 ──

	private static ActionExecutionResult ExecuteRefuel(GameState state, Actor worker, WorkTicket ticket)
	{
		if (!state.Facilities.TryGetValue(ticket.FacilityId, out var facility))
			return Fail(state, ticket);

		var hasItem = InventoryModule.CountMatching(worker, item =>
			!item.Equipped && string.Equals(item.Id, ticket.RequiredItemId, StringComparison.Ordinal)) > 0;

		if (!hasItem)
			return TryPickupItem(state, worker, ticket.RequiredItemId);

		if (!IsAdjacentTo(worker, facility))
			return TryMoveToward(state, worker, facility.AnchorX, facility.AnchorY);

		// 将燃料从背包转移到设施
		var consumed = TransferItemToFacilityBuffer(worker, facility.FuelBuffer, ticket.RequiredItemId, ticket.RequiredCount);
		if (consumed > 0)
		{
			JobScheduler.CompleteTicket(state, ticket.Id);
			return new ActionExecutionResult { Consumed = true };
		}

		return new ActionExecutionResult();
	}

	// ── 供应原材料 ──

	private static ActionExecutionResult ExecuteSupplyInput(GameState state, Actor worker, WorkTicket ticket)
	{
		if (!state.Facilities.TryGetValue(ticket.FacilityId, out var facility))
			return Fail(state, ticket);

		var hasItem = InventoryModule.CountMatching(worker, item =>
			!item.Equipped && string.Equals(item.Id, ticket.RequiredItemId, StringComparison.Ordinal)) > 0;

		if (!hasItem)
			return TryPickupItem(state, worker, ticket.RequiredItemId);

		if (!IsAdjacentTo(worker, facility))
			return TryMoveToward(state, worker, facility.AnchorX, facility.AnchorY);

		var consumed = TransferItemToFacilityBuffer(worker, facility.InputBuffer, ticket.RequiredItemId, ticket.RequiredCount);
		if (consumed > 0)
		{
			JobScheduler.CompleteTicket(state, ticket.Id);
			return new ActionExecutionResult { Consumed = true };
		}

		return new ActionExecutionResult();
	}

	// ── 制作配方 ──

	private static ActionExecutionResult ExecuteProduce(GameState state, Actor worker, WorkTicket ticket)
	{
		if (!state.Facilities.TryGetValue(ticket.FacilityId, out var facility))
			return Fail(state, ticket);

		if (!IsAdjacentTo(worker, facility))
			return TryMoveToward(state, worker, facility.AnchorX, facility.AnchorY);

		var recipe = RecipeRegistry.Get(ticket.RecipeId);
		if (recipe == null)
			return Fail(state, ticket);

		// 减少剩余工作量
		ticket.WorkRemaining = Math.Max(0, ticket.WorkRemaining - 1);
		if (ticket.WorkRemaining > 0)
			return new ActionExecutionResult { Consumed = true };

		// 工作完成：消耗输入，产出输出
		if (!ConsumeRecipeInputs(facility, recipe))
		{
			// 原材料不足，任务失败
			JobScheduler.Release(ticket);
			return new ActionExecutionResult();
		}

		ProduceRecipeOutputs(facility, recipe);
		JobScheduler.CompleteTicket(state, ticket.Id);
		return new ActionExecutionResult { Consumed = true };
	}

	// ── 搬运产出 ──

	private static ActionExecutionResult ExecuteHaulOutput(GameState state, Actor worker, WorkTicket ticket)
	{
		if (!state.Facilities.TryGetValue(ticket.FacilityId, out var facility))
			return Fail(state, ticket);

		if (facility.OutputBuffer == null || facility.OutputBuffer.Count == 0)
		{
			JobScheduler.CompleteTicket(state, ticket.Id);
			return new ActionExecutionResult();
		}

		// 如果工人背包里有从设施拿的产出物品，先搬到 stockpile
		if (worker.Inventory.Count > 0)
		{
			var stockpileCell = FindNearestStockpileCell(state, worker, facility.OwnerDomainId);
			if (stockpileCell != null)
			{
				if (worker.X == stockpileCell.Value.X && worker.Y == stockpileCell.Value.Y)
				{
					// 到达 stockpile，放下物品
					DropInventoryToGround(state, worker);
					JobScheduler.CompleteTicket(state, ticket.Id);
					return new ActionExecutionResult { Consumed = true };
				}

				return TryMoveToward(state, worker, stockpileCell.Value.X, stockpileCell.Value.Y);
			}
		}

		// 先到设施旁拿产出
		if (!IsAdjacentTo(worker, facility))
			return TryMoveToward(state, worker, facility.AnchorX, facility.AnchorY);

		// 从设施产出缓冲区拿物品到背包
		if (facility.OutputBuffer.Count > 0)
		{
			var item = facility.OutputBuffer[0];
			facility.OutputBuffer.RemoveAt(0);
			worker.Inventory.Add(item);
			return new ActionExecutionResult { Consumed = true };
		}

		return new ActionExecutionResult();
	}

	// ── 修理设施 ──

	private static ActionExecutionResult ExecuteRepair(GameState state, Actor worker, WorkTicket ticket)
	{
		if (!state.Facilities.TryGetValue(ticket.FacilityId, out var facility))
			return Fail(state, ticket);

		if (!IsAdjacentTo(worker, facility))
			return TryMoveToward(state, worker, facility.AnchorX, facility.AnchorY);

		// 简单修理：恢复 HP，切换到 Active
		facility.HitPoints = facility.MaxHitPoints;
		if (facility.Stage == FacilityStage.Broken)
			facility.Stage = FacilityStage.Active;

		JobScheduler.CompleteTicket(state, ticket.Id);
		return new ActionExecutionResult { Consumed = true };
	}

	// ── 辅助方法 ──

	private static ActionExecutionResult Fail(GameState state, WorkTicket ticket)
	{
		JobScheduler.CompleteTicket(state, ticket.Id);
		return new ActionExecutionResult();
	}

	private static ActionExecutionResult TryMoveToward(GameState state, Actor actor, int targetX, int targetY)
	{
		var step = Pathfinding.NextStep(
			actor.X, actor.Y, targetX, targetY,
			(x, y) => Health.FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z));

		if (step == null)
			return new ActionExecutionResult();

		var events = ActionModule.TryMove(state, actor, step.Value.X - actor.X, step.Value.Y - actor.Y);
		return new ActionExecutionResult
		{
			Consumed = events.Count > 0,
			Events = { },
		};
	}

	private static ActionExecutionResult TryPickupItem(GameState state, Actor worker, string itemId)
	{
		if (state.World == null)
			return new ActionExecutionResult();

		// 搜索附近地面上的目标物品
		var bestCell = FindNearestGroundItem(state, worker, itemId);
		if (bestCell == null)
			return new ActionExecutionResult();

		// 如果在同一格，拾取
		if (worker.X == bestCell.Value.X && worker.Y == bestCell.Value.Y)
		{
			state.World.TryPickupItem(worker.X, worker.Y, worker.Z, bestCell.Value.EntityId, out var pickedItem);
			if (pickedItem != null)
			{
				worker.Inventory.Add(pickedItem);
				return new ActionExecutionResult { Consumed = true };
			}

			return new ActionExecutionResult();
		}

		// 移动过去
		return TryMoveToward(state, worker, bestCell.Value.X, bestCell.Value.Y);
	}

	private static (int X, int Y, string EntityId)? FindNearestGroundItem(GameState state, Actor actor, string itemId)
	{
		if (state.World == null)
			return null;

		(int X, int Y, string EntityId)? best = null;
		var bestDist = int.MaxValue;
		const int searchRadius = 20;

		for (var y = actor.Y - searchRadius; y <= actor.Y + searchRadius; y++)
		{
			for (var x = actor.X - searchRadius; x <= actor.X + searchRadius; x++)
			{
				var dist = Math.Abs(x - actor.X) + Math.Abs(y - actor.Y);
				if (dist > searchRadius || dist >= bestDist)
					continue;

				foreach (var entity in state.World.GetEntitiesByType(x, y, actor.Z, CellEntityType.Item))
				{
					var templateId = WorldMap.ResolveGroundItemTemplateId(entity);
					if (string.Equals(templateId, itemId, StringComparison.Ordinal))
					{
						bestDist = dist;
						best = (x, y, entity.EntityId);
					}
				}
			}
		}

		return best;
	}

	private static (int X, int Y)? FindNearestStockpileCell(GameState state, Actor worker, string domainId)
	{
		(int X, int Y)? best = null;
		var bestDist = int.MaxValue;

		foreach (var zone in state.StockpileZones)
		{
			if (!zone.AllowInput)
				continue;

			// 优先同 domain 的 stockpile
			var sameDomain = string.Equals(zone.OwnerDomainId, domainId, StringComparison.Ordinal);

			foreach (var cell in zone.Cells)
			{
				if (cell.Z != worker.Z)
					continue;

				var dist = Math.Abs(cell.X - worker.X) + Math.Abs(cell.Y - worker.Y);
				if (!sameDomain)
					dist += 100; // 惩罚跨 domain

				if (dist < bestDist)
				{
					bestDist = dist;
					best = (cell.X, cell.Y);
				}
			}
		}

		return best;
	}

	private static bool IsAdjacentTo(Actor actor, FacilityInstance facility)
	{
		return actor.Z == facility.Z
			&& Math.Abs(actor.X - facility.AnchorX) + Math.Abs(actor.Y - facility.AnchorY) <= 1;
	}

	private static int TransferItemToFacilityBuffer(Actor worker, List<Item> buffer, string itemId, int maxCount)
	{
		var transferred = 0;
		for (var i = worker.Inventory.Count - 1; i >= 0 && transferred < maxCount; i--)
		{
			var item = worker.Inventory[i];
			if (item.Equipped || !string.Equals(item.Id, itemId, StringComparison.Ordinal))
				continue;

			worker.Inventory.RemoveAt(i);
			buffer.Add(item);
			transferred += item.StackCount > 0 ? item.StackCount : 1;
		}

		return transferred;
	}

	private static bool ConsumeRecipeInputs(FacilityInstance facility, RecipeDef recipe)
	{
		// 验证所有输入是否充足
		foreach (var input in recipe.Inputs)
		{
			var available = CountInBuffer(facility.InputBuffer, input.ItemId);
			if (available < input.Count)
				return false;
		}

		// 消耗
		foreach (var input in recipe.Inputs)
			RemoveFromBuffer(facility.InputBuffer, input.ItemId, input.Count);

		return true;
	}

	private static void ProduceRecipeOutputs(FacilityInstance facility, RecipeDef recipe)
	{
		foreach (var output in recipe.Outputs)
		{
			for (var i = 0; i < output.Count; i++)
			{
				if (!PresetDB.Items.TryGetValue(output.ItemId, out var preset))
					continue;

				var item = ItemSnapshotMapper.CreateItem(ItemSnapshotMapper.BuildSnapshot(
					new Item { Id = preset.Id, DisplayName = preset.Name }));
				facility.OutputBuffer.Add(item);
			}
		}
	}

	private static int CountInBuffer(List<Item> buffer, string itemId)
	{
		var count = 0;
		foreach (var item in buffer)
		{
			if (string.Equals(item.Id, itemId, StringComparison.Ordinal))
				count += item.StackCount > 0 ? item.StackCount : 1;
		}

		return count;
	}

	private static void RemoveFromBuffer(List<Item> buffer, string itemId, int count)
	{
		var remaining = count;
		for (var i = buffer.Count - 1; i >= 0 && remaining > 0; i--)
		{
			if (!string.Equals(buffer[i].Id, itemId, StringComparison.Ordinal))
				continue;

			var stackSize = buffer[i].StackCount > 0 ? buffer[i].StackCount : 1;
			if (stackSize <= remaining)
			{
				remaining -= stackSize;
				buffer.RemoveAt(i);
			}
			else
			{
				buffer[i].StackCount -= remaining;
				remaining = 0;
			}
		}
	}

	private static void DropInventoryToGround(GameState state, Actor worker)
	{
		if (state.World == null)
			return;

		for (var i = worker.Inventory.Count - 1; i >= 0; i--)
		{
			var item = worker.Inventory[i];
			if (item.Equipped)
				continue;

			worker.Inventory.RemoveAt(i);
			state.World.DropItem(worker.X, worker.Y, worker.Z, item);
		}
	}
}
