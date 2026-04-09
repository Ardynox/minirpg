using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniRPG.Module;

public enum ServerActionStatus
{
	Accepted,
	Rejected,
	Busy,
}

public sealed class ServerActionResult
{
	public ServerActionStatus Status { get; init; } = ServerActionStatus.Accepted;
	public string? ErrorCode { get; init; }
	public List<GameEvent> Events { get; init; } = [];
	public List<string> Logs { get; init; } = [];

	public bool Ok => Status == ServerActionStatus.Accepted;

	public static ServerActionResult Accept(
		IEnumerable<GameEvent>? events = null,
		IEnumerable<string>? logs = null) =>
		new()
		{
			Events = events != null ? [.. events] : [],
			Logs = logs != null ? [.. logs] : [],
		};

	public static ServerActionResult Reject(string message, string? errorCode = null) =>
		new()
		{
			Status = ServerActionStatus.Rejected,
			ErrorCode = errorCode,
			Logs = [message],
		};

	public static ServerActionResult Busy(string message, string? errorCode = null) =>
		new()
		{
			Status = ServerActionStatus.Busy,
			ErrorCode = errorCode,
			Logs = [message],
		};
}

public static class ServerActionGateway
{
	public static ServerActionResult Execute(GameState state, ClientCommand command, DateTimeOffset? now = null)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(command);

		return command switch
		{
			MoveClientCommand move => ExecuteTimelineAction(state, TimelinePlayerAction.Move(move.Dx, move.Dy)),
			CastSkillClientCommand cast => ExecuteTimelineAction(
				state,
				TimelinePlayerAction.CastSkill(
					cast.SkillId,
					cast.TargetType,
					cast.TargetActorId,
					cast.TargetLimbId,
					targetX: cast.TargetX,
					targetY: cast.TargetY,
					targetZ: cast.TargetZ)),
			InteractClientCommand interact => ExecuteInteraction(state, interact),
			PickupClientCommand pickup => ExecutePickup(state, pickup),
			InventoryToggleEquipClientCommand toggleEquip => ExecuteInventoryToggleEquip(state, toggleEquip),
			InventoryDropClientCommand drop => ExecuteInventoryDrop(state, drop),
			ChestTakeClientCommand take => ExecuteChestTake(state, take),
			ChestTakeAllClientCommand takeAll => ExecuteChestTakeAll(state, takeAll),
			ChestPutClientCommand put => ExecuteChestPut(state, put),
			TradeBuyClientCommand buy => ExecuteTradeBuy(state, buy),
			TradeSellClientCommand sell => ExecuteTradeSell(state, sell),
			DelegateActorClientCommand delegateActor => ExecuteDelegateActor(state, delegateActor),
			ReclaimPrimaryActorClientCommand reclaim => ExecuteReclaimPrimaryActor(state, reclaim),
			_ => ServerActionResult.Reject(
				LocalizationService.TOrFallback(
					"log.server_action.unsupported",
					"Unsupported server command: {kind}",
					("kind", command.Kind.ToString())),
				errorCode: "unsupported_command"),
		};
	}

	public static ServerActionResult HandleActorKilled(GameState state, GameEvent e)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(e);

		var logs = new List<string>();
		var player = ActorModule.GetPlayer(state);
		if (player != null)
		{
			var goldDrop = e.Damage > 0 ? e.Damage : 5;
			player.Gold += goldDrop;
			logs.Add(LocalizationService.T("combat.gold_reward", ("gold", goldDrop), ("total", player.Gold)));
		}

		GenerateLoot(state, e, logs);
		return ServerActionResult.Accept(logs: logs);
	}

	private static ServerActionResult ExecuteTimelineAction(GameState state, TimelinePlayerAction action)
	{
		var timelineResult = TimelineTurnGateway.SubmitPlayerAction(state, action);
		return ServerActionResult.Accept(events: timelineResult.Events);
	}

	private static ServerActionResult ExecuteInteraction(GameState state, InteractClientCommand command)
	{
		var actor = ResolveActor(state, command.ActorId);
		var target = ActorModule.GetById(state, command.TargetActorId);
		if (actor == null || target == null)
			return ServerActionResult.Reject(LocalizationService.T("ui.interaction.none_nearby"), "invalid_target");

		var interaction = InteractionDefs.All.FirstOrDefault(def => string.Equals(def.Id, command.InteractionDefId, StringComparison.Ordinal));
		if (interaction == null)
			return ServerActionResult.Reject(LocalizationService.T("ui.interaction.none_nearby"), "invalid_interaction");

		return ServerActionResult.Accept(events: InteractionModule.Execute(state, actor, target, interaction));
	}

	private static ServerActionResult ExecutePickup(GameState state, PickupClientCommand command)
	{
		var actor = ResolveActor(state, command.ActorId);
		if (actor == null)
		{
			return ServerActionResult.Reject(
				LocalizationService.TOrFallback("log.pickup.invalid_actor", "Pick up failed."),
				"invalid_actor");
		}

		return ServerActionResult.Accept(events: InteractionModule.PickupItem(state, actor, command.ItemInstanceId));
	}

	private static ServerActionResult ExecuteInventoryToggleEquip(GameState state, InventoryToggleEquipClientCommand command)
	{
		var actor = ResolveActor(state, command.ActorId);
		if (actor == null)
			return ServerActionResult.Reject(LocalizationService.T("inventory.invalid_index"), "invalid_actor");

		var result = InventoryModule.ToggleEquip(actor, command.InventoryIndex, state);
		return result.Ok
			? ServerActionResult.Accept(logs: [result.Message])
			: ServerActionResult.Reject(result.Message, "inventory_toggle_rejected");
	}

	private static ServerActionResult ExecuteInventoryDrop(GameState state, InventoryDropClientCommand command)
	{
		var actor = ResolveActor(state, command.ActorId);
		if (actor == null)
			return ServerActionResult.Reject(LocalizationService.T("inventory.invalid_index"), "invalid_actor");

		var events = InteractionModule.DropItem(state, actor, command.InventoryIndex);
		return ServerActionResult.Accept(events: events);
	}

	private static ServerActionResult ExecuteChestTake(GameState state, ChestTakeClientCommand command)
	{
		var actor = ResolveActor(state, command.ActorId);
		var chest = ResolveContainer(state, command.ContainerSource, command.ContainerInstanceId, command.ContainerOwnerActorId, command.ContainerX, command.ContainerY, command.ContainerZ);
		if (actor == null || chest?.Contents == null)
			return ServerActionResult.Reject(LocalizationService.T("ui.common.empty_inline"), "invalid_container");

		var index = chest.Contents.FindIndex(item => string.Equals(item.InstanceId, command.ItemInstanceId, StringComparison.Ordinal));
		if (index < 0)
			return ServerActionResult.Reject(LocalizationService.T("ui.common.empty_inline"), "item_not_found");

		var item = chest.Contents[index];
		chest.Contents.RemoveAt(index);
		InventoryModule.Add(actor, item);
		PersistContainer(state, command.ContainerSource, chest, command.ContainerOwnerActorId, command.ContainerX, command.ContainerY, command.ContainerZ);
		return ServerActionResult.Accept(logs:
		[
			LocalizationService.T(
				"log.chest.take_item",
				("chest", ItemFormatHelper.GetDisplayName(state, chest)),
				("item", ItemFormatHelper.GetDisplayName(state, item))),
		]);
	}

	private static ServerActionResult ExecuteChestTakeAll(GameState state, ChestTakeAllClientCommand command)
	{
		var actor = ResolveActor(state, command.ActorId);
		var chest = ResolveContainer(state, command.ContainerSource, command.ContainerInstanceId, command.ContainerOwnerActorId, command.ContainerX, command.ContainerY, command.ContainerZ);
		if (actor == null || chest?.Contents == null)
			return ServerActionResult.Reject(LocalizationService.T("ui.common.empty_inline"), "invalid_container");

		var count = chest.Contents.Count;
		foreach (var item in chest.Contents)
			InventoryModule.Add(actor, item);
		chest.Contents.Clear();
		PersistContainer(state, command.ContainerSource, chest, command.ContainerOwnerActorId, command.ContainerX, command.ContainerY, command.ContainerZ);
		return ServerActionResult.Accept(logs:
		[
			LocalizationService.T(
				"log.chest.take_all",
				("chest", ItemFormatHelper.GetDisplayName(state, chest)),
				("count", count)),
		]);
	}

	private static ServerActionResult ExecuteChestPut(GameState state, ChestPutClientCommand command)
	{
		var actor = ResolveActor(state, command.ActorId);
		var chest = ResolveContainer(state, command.ContainerSource, command.ContainerInstanceId, command.ContainerOwnerActorId, command.ContainerX, command.ContainerY, command.ContainerZ);
		if (actor == null || chest == null)
			return ServerActionResult.Reject(LocalizationService.T("ui.common.empty_inline"), "invalid_container");
		if (command.InventoryIndex < 0 || command.InventoryIndex >= actor.Inventory.Count)
			return ServerActionResult.Reject(LocalizationService.T("inventory.invalid_index"), "invalid_inventory_index");

		var candidate = actor.Inventory[command.InventoryIndex];
		if (candidate.Equipped)
		{
			return ServerActionResult.Reject(
				LocalizationService.T("log.inventory.unequip_first", ("item", ItemFormatHelper.GetDisplayName(state, candidate))),
				"item_equipped");
		}

		chest.Contents ??= [];
		var removed = InventoryModule.RemoveAt(actor, command.InventoryIndex);
		if (removed == null)
			return ServerActionResult.Reject(LocalizationService.T("inventory.invalid_index"), "invalid_inventory_index");

		chest.Contents.Add(removed);
		PersistContainer(state, command.ContainerSource, chest, command.ContainerOwnerActorId, command.ContainerX, command.ContainerY, command.ContainerZ);
		return ServerActionResult.Accept(logs:
		[
			LocalizationService.T(
				"log.chest.put_item",
				("item", ItemFormatHelper.GetDisplayName(state, removed)),
				("chest", ItemFormatHelper.GetDisplayName(state, chest))),
		]);
	}

	private static ServerActionResult ExecuteTradeBuy(GameState state, TradeBuyClientCommand command)
	{
		var buyer = ResolveActor(state, command.ActorId);
		var trader = ActorModule.GetById(state, command.TraderActorId);
		if (buyer == null || trader == null)
			return ServerActionResult.Reject(LocalizationService.T("trade.insufficient_gold", ("required", 0), ("current", 0)), "invalid_trade_actor");

		var good = TradeModule.ListGoods(trader)
			.FirstOrDefault(entry =>
				entry.Index == command.GoodIndex
				&& entry.From == ToTradeGoodSource(command.GoodSource));
		if (good == null)
			return ServerActionResult.Reject(LocalizationService.T("ui.common.empty_inline"), "trade_good_missing");

		var result = TradeModule.Buy(buyer, trader, good, state);
		return result.Ok
			? ServerActionResult.Accept(logs:
			[
				result.Message,
				LocalizationService.T("trade.gold_remaining", ("gold", buyer.Gold)),
			])
			: ServerActionResult.Reject(result.Message, "trade_buy_rejected");
	}

	private static ServerActionResult ExecuteTradeSell(GameState state, TradeSellClientCommand command)
	{
		var seller = ResolveActor(state, command.ActorId);
		var trader = ActorModule.GetById(state, command.TraderActorId);
		if (seller == null || trader == null)
			return ServerActionResult.Reject(LocalizationService.T("inventory.invalid_index"), "invalid_trade_actor");

		var result = TradeModule.Sell(seller, trader, command.InventoryIndex, state);
		return result.Ok
			? ServerActionResult.Accept(logs:
			[
				result.Message,
				LocalizationService.T("trade.gold_remaining", ("gold", seller.Gold)),
			])
			: ServerActionResult.Reject(result.Message, "trade_sell_rejected");
	}

	private static ServerActionResult ExecuteDelegateActor(GameState state, DelegateActorClientCommand command)
	{
		if (string.IsNullOrWhiteSpace(command.ActorId))
			return ServerActionResult.Reject(LocalizationService.T("ui.common.none"), "invalid_actor");

		var ok = RoomRuntimeModule.DelegateActor(state, command.ActorId, command.TargetPlayerSessionId);
		return ok
			? ServerActionResult.Accept(logs:
			[
				LocalizationService.TOrFallback(
					"log.multiplayer.delegate_actor",
					"Delegated {actor} to {player}.",
					("actor", command.ActorId),
					("player", command.TargetPlayerSessionId)),
			])
			: ServerActionResult.Reject(
				LocalizationService.TOrFallback(
					"log.multiplayer.delegate_actor_failed",
					"Failed to delegate {actor}.",
					("actor", command.ActorId)),
				"delegate_failed");
	}

	private static ServerActionResult ExecuteReclaimPrimaryActor(GameState state, ReclaimPrimaryActorClientCommand command)
	{
		if (string.IsNullOrWhiteSpace(command.ActorId) || string.IsNullOrWhiteSpace(command.PlayerSessionId))
			return ServerActionResult.Reject(LocalizationService.T("ui.common.none"), "invalid_actor");

		var ok = RoomRuntimeModule.ReclaimPrimaryActor(state, command.ActorId, command.PlayerSessionId);
		return ok
			? ServerActionResult.Accept(logs:
			[
				LocalizationService.TOrFallback(
					"log.multiplayer.reclaim_actor",
					"Reclaimed {actor}.",
					("actor", command.ActorId)),
			])
			: ServerActionResult.Reject(
				LocalizationService.TOrFallback(
					"log.multiplayer.reclaim_actor_failed",
					"Failed to reclaim {actor}.",
					("actor", command.ActorId)),
				"reclaim_failed");
	}

	private static void GenerateLoot(GameState state, GameEvent e, List<string> logs)
	{
		var rng = new Random(state.RngSeed + state.Turn + (e.TargetId ?? string.Empty).GetHashCode());
		if (rng.Next(100) >= 40)
			return;

		var pool = new List<string>(PresetDB.Items.Keys);
		if (pool.Count == 0)
			return;

		var itemId = pool[rng.Next(pool.Count)];
		var item = PresetDB.CloneItem(itemId);
		MapModule.PlaceItem(state, e.TargetX, e.TargetY, e.TargetZ, item);
		logs.Add(LocalizationService.T(
			"combat.loot_drop",
			("target", e.TargetActorName),
			("item", IdentificationModule.GetItemDisplayName(state, item))));
	}

	private static Actor? ResolveActor(GameState state, string? actorId)
	{
		if (!string.IsNullOrWhiteSpace(actorId))
			return ActorModule.GetById(state, actorId);

		return ActorModule.GetPlayer(state);
	}

	private static Item? ResolveContainer(
		GameState state,
		ContainerSourceKind source,
		string containerInstanceId,
		string? ownerActorId,
		int x,
		int y,
		int z)
	{
		return source switch
		{
			ContainerSourceKind.Inventory => ResolveInventoryContainer(state, containerInstanceId, ownerActorId),
			_ => MapModule.FindGroundItem(state, x, y, z, containerInstanceId),
		};
	}

	private static Item? ResolveInventoryContainer(GameState state, string containerInstanceId, string? ownerActorId)
	{
		var actor = ResolveActor(state, ownerActorId);
		return actor?.Inventory.FirstOrDefault(item =>
			string.Equals(item.InstanceId, containerInstanceId, StringComparison.Ordinal));
	}

	private static void PersistContainer(
		GameState state,
		ContainerSourceKind source,
		Item chest,
		string? ownerActorId,
		int x,
		int y,
		int z)
	{
		if (source == ContainerSourceKind.Ground)
			MapModule.UpdateGroundItem(state, x, y, z, chest);
	}

	private static TradeGood.Source ToTradeGoodSource(TradeGoodSourceKind source) => source switch
	{
		TradeGoodSourceKind.Inventory => TradeGood.Source.Inventory,
		_ => TradeGood.Source.Shop,
	};
}
