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
	public string? ReservationKey { get; init; }
	public string? BusyByPlayerSessionId { get; init; }
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

	public static ServerActionResult Busy(
		string message,
		string? errorCode = null,
		string? reservationKey = null,
		string? busyByPlayerSessionId = null) =>
		new()
		{
			Status = ServerActionStatus.Busy,
			ErrorCode = errorCode,
			ReservationKey = reservationKey,
			BusyByPlayerSessionId = busyByPlayerSessionId,
			Logs = [message],
		};
}

public static class ServerActionGateway
{
	public static ServerActionResult Execute(GameState state, ClientCommand command, DateTimeOffset? now = null)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(command);
		var timestamp = now ?? DateTimeOffset.UtcNow;

		return command switch
		{
			MoveClientCommand move => ExecuteTimelineAction(state, TimelinePlayerAction.Move(move.Dx, move.Dy)),
			DigClientCommand dig => ExecuteTimelineAction(state, TimelinePlayerAction.Dig(dig.Dx, dig.Dy, dig.SkillId)),
			AttackClientCommand attack => ExecuteCombatAttack(state, attack),
			CastSkillClientCommand cast => ExecuteTimelineAction(
				state,
				TimelinePlayerAction.CastSkill(
					cast.SkillId,
					cast.TargetType,
					cast.TargetActorId,
					cast.TargetLimbId,
					cast.TargetItemId,
					targetX: cast.TargetX,
					targetY: cast.TargetY,
					targetZ: cast.TargetZ)),
			EatInventoryClientCommand eat => ExecuteTimelineAction(state, TimelinePlayerAction.EatInventory(eat.InventoryIndex)),
			RestClientCommand => ExecuteTimelineAction(state, TimelinePlayerAction.Rest()),
			FacilityDeliverClientCommand deliver => ExecuteTimelineAction(
				state,
				TimelinePlayerAction.FacilityDeliver(deliver.FacilityId)),
			FacilityConstructClientCommand construct => ExecuteTimelineAction(
				state,
				TimelinePlayerAction.FacilityConstruct(construct.FacilityId)),
			InteractClientCommand interact => ExecuteInteraction(state, interact, timestamp),
			PickupClientCommand pickup => ExecutePickup(state, pickup),
			InventoryToggleEquipClientCommand toggleEquip => ExecuteInventoryToggleEquip(state, toggleEquip),
			InventoryDropClientCommand drop => ExecuteInventoryDrop(state, drop),
			ChestTakeClientCommand take => ExecuteChestTake(state, take, timestamp),
			ChestTakeAllClientCommand takeAll => ExecuteChestTakeAll(state, takeAll, timestamp),
			ChestPutClientCommand put => ExecuteChestPut(state, put, timestamp),
			TradeBuyClientCommand buy => ExecuteTradeBuy(state, buy, timestamp),
			TradeSellClientCommand sell => ExecuteTradeSell(state, sell, timestamp),
			DialogChooseClientCommand dialogChoose => ExecuteDialogChoose(state, dialogChoose, timestamp),
			OpenModalClientCommand openModal => ExecuteOpenModal(state, openModal, timestamp),
			CloseModalClientCommand closeModal => ExecuteCloseModal(state, closeModal),
			DelegateActorClientCommand delegateActor => ExecuteDelegateActor(state, delegateActor),
			ReclaimPrimaryActorClientCommand reclaim => ExecuteReclaimPrimaryActor(state, reclaim),
			AssignPrimaryActorClientCommand assign => ExecuteAssignPrimaryActor(state, assign),
			KickPlayerClientCommand kick => ExecuteKickPlayer(state, kick),
			StartCombatClientCommand startCombat => ExecuteStartCombat(state, startCombat),
			EndTurnClientCommand endTurn => ExecuteEndTurn(state, endTurn),
			UseSkillClientCommand useSkill => ExecuteUseSkill(state, useSkill),
			EndCombatClientCommand endCombat => ExecuteEndCombat(state, endCombat),
			_ => ServerActionResult.Reject(
				LocalizationService.TOrFallback(
					"log.server_action.unsupported",
					"Unsupported server command: {kind}",
					("kind", command.Kind.ToString())),
				errorCode: ErrorCode.UnsupportedCommand.ToWireCode()),
		};
	}

	public static ServerActionResult HandleActorKilled(GameState state, GameEvent e)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(e);

		var logs = new List<string>();
		var rewardActor = ResolveRewardActor(state, e);
		if (rewardActor != null)
		{
			var goldDrop = e.Damage > 0 ? e.Damage : 5;
			rewardActor.Gold += goldDrop;
			logs.Add(LocalizationService.T("combat.gold_reward", ("gold", goldDrop), ("total", rewardActor.Gold)));
		}

		GenerateLoot(state, e, logs);
		return ServerActionResult.Accept(logs: logs);
	}

	private static ServerActionResult ExecuteTimelineAction(GameState state, TimelinePlayerAction action)
	{
		var timelineResult = TimelineTurnGateway.SubmitPlayerAction(state, action);
		return ServerActionResult.Accept(events: timelineResult.Events);
	}

	private static ServerActionResult ExecuteInteraction(GameState state, InteractClientCommand command, DateTimeOffset now)
	{
		var actor = ResolveActor(state, command.ActorId);
		var target = ActorModule.GetById(state, command.TargetActorId);
		if (actor == null || target == null)
			return ServerActionResult.Reject(LocalizationService.T("ui.interaction.none_nearby"), ErrorCode.InvalidTarget.ToWireCode());

		var interaction = InteractionDefs.All.FirstOrDefault(def => string.Equals(def.Id, command.InteractionDefId, StringComparison.Ordinal));
		if (interaction == null)
			return ServerActionResult.Reject(LocalizationService.T("ui.interaction.none_nearby"), ErrorCode.InvalidInteraction.ToWireCode());

		var reservationResult = TryReserveInteractionTarget(state, command.PlayerSessionId, target.Id, interaction.EffectType, now);
		if (reservationResult != null)
			return reservationResult;

		return ServerActionResult.Accept(events: InteractionModule.Execute(state, actor, target, interaction));
	}

	private static ServerActionResult ExecutePickup(GameState state, PickupClientCommand command)
	{
		var actor = ResolveActor(state, command.ActorId);
		if (actor == null)
		{
			return ServerActionResult.Reject(
				LocalizationService.TOrFallback("log.pickup.invalid_actor", "Pick up failed."),
				ErrorCode.InvalidActor.ToWireCode());
		}

		return ServerActionResult.Accept(events: InteractionModule.PickupItem(state, actor, command.ItemInstanceId));
	}

	private static ServerActionResult ExecuteInventoryToggleEquip(GameState state, InventoryToggleEquipClientCommand command)
	{
		var actor = ResolveActor(state, command.ActorId);
		if (actor == null)
			return ServerActionResult.Reject(LocalizationService.T("inventory.invalid_index"), ErrorCode.InvalidActor.ToWireCode());

		var result = InventoryModule.ToggleEquip(actor, command.InventoryIndex, state);
		return result.Ok
			? ServerActionResult.Accept(logs: [result.Message])
			: ServerActionResult.Reject(result.Message, ErrorCode.InventoryToggleRejected.ToWireCode());
	}

	private static ServerActionResult ExecuteInventoryDrop(GameState state, InventoryDropClientCommand command)
	{
		var actor = ResolveActor(state, command.ActorId);
		if (actor == null)
			return ServerActionResult.Reject(LocalizationService.T("inventory.invalid_index"), ErrorCode.InvalidActor.ToWireCode());
		if (command.InventoryIndex < 0 || command.InventoryIndex >= actor.Inventory.Count)
			return ServerActionResult.Reject(LocalizationService.T("inventory.invalid_index"), ErrorCode.InvalidInventoryIndex.ToWireCode());

		var events = InteractionModule.DropItem(state, actor, command.InventoryIndex);
		return ServerActionResult.Accept(events: events);
	}

	private static ServerActionResult ExecuteChestTake(GameState state, ChestTakeClientCommand command, DateTimeOffset now)
	{
		var reservationResult = TryReserveIfNeeded(state, command.PlayerSessionId, BuildContainerReservationKey(command), now);
		if (reservationResult != null)
			return reservationResult;

		var actor = ResolveActor(state, command.ActorId);
		var chest = ResolveContainer(state, command.ContainerSource, command.ContainerInstanceId, command.ContainerOwnerActorId, command.ContainerX, command.ContainerY, command.ContainerZ);
		if (actor == null || chest?.Contents == null)
			return ServerActionResult.Reject(LocalizationService.T("ui.common.empty_inline"), ErrorCode.InvalidContainer.ToWireCode());

		var index = chest.Contents.FindIndex(item => string.Equals(item.InstanceId, command.ItemInstanceId, StringComparison.Ordinal));
		if (index < 0)
			return ServerActionResult.Reject(LocalizationService.T("ui.common.empty_inline"), ErrorCode.ItemNotFound.ToWireCode());

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

	private static ServerActionResult ExecuteChestTakeAll(GameState state, ChestTakeAllClientCommand command, DateTimeOffset now)
	{
		var reservationResult = TryReserveIfNeeded(state, command.PlayerSessionId, BuildContainerReservationKey(command), now);
		if (reservationResult != null)
			return reservationResult;

		var actor = ResolveActor(state, command.ActorId);
		var chest = ResolveContainer(state, command.ContainerSource, command.ContainerInstanceId, command.ContainerOwnerActorId, command.ContainerX, command.ContainerY, command.ContainerZ);
		if (actor == null || chest?.Contents == null)
			return ServerActionResult.Reject(LocalizationService.T("ui.common.empty_inline"), ErrorCode.InvalidContainer.ToWireCode());

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

	private static ServerActionResult ExecuteChestPut(GameState state, ChestPutClientCommand command, DateTimeOffset now)
	{
		var reservationResult = TryReserveIfNeeded(state, command.PlayerSessionId, BuildContainerReservationKey(command), now);
		if (reservationResult != null)
			return reservationResult;

		var actor = ResolveActor(state, command.ActorId);
		var chest = ResolveContainer(state, command.ContainerSource, command.ContainerInstanceId, command.ContainerOwnerActorId, command.ContainerX, command.ContainerY, command.ContainerZ);
		if (actor == null || chest == null)
			return ServerActionResult.Reject(LocalizationService.T("ui.common.empty_inline"), ErrorCode.InvalidContainer.ToWireCode());
		if (command.InventoryIndex < 0 || command.InventoryIndex >= actor.Inventory.Count)
			return ServerActionResult.Reject(LocalizationService.T("inventory.invalid_index"), ErrorCode.InvalidInventoryIndex.ToWireCode());

		var candidate = actor.Inventory[command.InventoryIndex];
		if (candidate.Equipped)
		{
			return ServerActionResult.Reject(
				LocalizationService.T("log.inventory.unequip_first", ("item", ItemFormatHelper.GetDisplayName(state, candidate))),
				ErrorCode.ItemEquipped.ToWireCode());
		}

		chest.Contents ??= [];
		var removed = InventoryModule.RemoveAt(actor, command.InventoryIndex);
		if (removed == null)
			return ServerActionResult.Reject(LocalizationService.T("inventory.invalid_index"), ErrorCode.InvalidInventoryIndex.ToWireCode());

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

	private static ServerActionResult ExecuteTradeBuy(GameState state, TradeBuyClientCommand command, DateTimeOffset now)
	{
		var reservationResult = TryReserveIfNeeded(state, command.PlayerSessionId, BuildTradeReservationKey(command.TraderActorId), now);
		if (reservationResult != null)
			return reservationResult;

		var buyer = ResolveActor(state, command.ActorId);
		var trader = ActorModule.GetById(state, command.TraderActorId);
		if (buyer == null || trader == null)
			return ServerActionResult.Reject(LocalizationService.T("trade.insufficient_gold", ("required", 0), ("current", 0)), ErrorCode.InvalidTradeActor.ToWireCode());

		var good = TradeModule.ListGoods(trader)
			.FirstOrDefault(entry =>
				entry.Index == command.GoodIndex
				&& entry.From == ToTradeGoodSource(command.GoodSource));
		if (good == null)
			return ServerActionResult.Reject(LocalizationService.T("ui.common.empty_inline"), ErrorCode.TradeGoodMissing.ToWireCode());

		var result = TradeModule.Buy(buyer, trader, good, state);
		return result.Ok
			? ServerActionResult.Accept(logs:
			[
				result.Message,
				LocalizationService.T("trade.gold_remaining", ("gold", buyer.Gold)),
			])
			: ServerActionResult.Reject(result.Message, ErrorCode.TradeBuyRejected.ToWireCode());
	}

	private static ServerActionResult ExecuteTradeSell(GameState state, TradeSellClientCommand command, DateTimeOffset now)
	{
		var reservationResult = TryReserveIfNeeded(state, command.PlayerSessionId, BuildTradeReservationKey(command.TraderActorId), now);
		if (reservationResult != null)
			return reservationResult;

		var seller = ResolveActor(state, command.ActorId);
		var trader = ActorModule.GetById(state, command.TraderActorId);
		if (seller == null || trader == null)
			return ServerActionResult.Reject(LocalizationService.T("inventory.invalid_index"), ErrorCode.InvalidTradeActor.ToWireCode());

		var result = TradeModule.Sell(seller, trader, command.InventoryIndex, state);
		return result.Ok
			? ServerActionResult.Accept(logs:
			[
				result.Message,
				LocalizationService.T("trade.gold_remaining", ("gold", seller.Gold)),
			])
			: ServerActionResult.Reject(result.Message, ErrorCode.TradeSellRejected.ToWireCode());
	}

	private static ServerActionResult ExecuteDialogChoose(GameState state, DialogChooseClientCommand command, DateTimeOffset now)
	{
		if (string.IsNullOrWhiteSpace(command.DialogId) || string.IsNullOrWhiteSpace(command.OptionId))
		{
			return ServerActionResult.Reject(
				LocalizationService.TOrFallback("log.server_action.invalid_dialog", "Dialog choice is invalid."),
				ErrorCode.InvalidDialog.ToWireCode());
		}

		var reservationResult = TryReserveIfNeeded(
			state,
			command.PlayerSessionId,
			BuildPrefixedReservationKey("dialog", command.DialogId),
			now);
		return reservationResult ?? ServerActionResult.Accept();
	}

	private static ServerActionResult ExecuteOpenModal(GameState state, OpenModalClientCommand command, DateTimeOffset now)
	{
		if (string.IsNullOrWhiteSpace(command.ModalId))
		{
			return ServerActionResult.Reject(
				LocalizationService.TOrFallback("log.server_action.invalid_modal", "Modal id is invalid."),
				ErrorCode.InvalidModal.ToWireCode());
		}

		var reservationResult = TryReserveIfNeeded(
			state,
			command.PlayerSessionId,
			BuildPrefixedReservationKey("modal", command.ModalId),
			now);
		return reservationResult ?? ServerActionResult.Accept();
	}

	private static ServerActionResult ExecuteCloseModal(GameState state, CloseModalClientCommand command)
	{
		if (string.IsNullOrWhiteSpace(command.ModalId))
		{
			return ServerActionResult.Reject(
				LocalizationService.TOrFallback("log.server_action.invalid_modal", "Modal id is invalid."),
				ErrorCode.InvalidModal.ToWireCode());
		}

		if (!state.Room.IsActive)
			return ServerActionResult.Accept();
		if (string.IsNullOrWhiteSpace(command.PlayerSessionId))
			return ServerActionResult.Reject("Missing player session id.", ErrorCode.MissingPlayerSession.ToWireCode());

		var reservationKey = BuildPrefixedReservationKey("modal", command.ModalId);
		if (RoomRuntimeModule.ReleaseInteraction(state, reservationKey, command.PlayerSessionId))
			return ServerActionResult.Accept();

		if (state.Room.InteractionReservations.TryGetValue(reservationKey, out var reservation))
		{
			return ServerActionResult.Busy(
				LocalizationService.TOrFallback(
					"log.server_action.reservation_busy",
					"Another player is already using {reservationKey}.",
					("reservationKey", reservationKey)),
				errorCode: ErrorCode.ReservationBusy.ToWireCode(),
				reservationKey: reservationKey,
				busyByPlayerSessionId: reservation.PlayerSessionId);
		}

		return ServerActionResult.Accept();
	}

	private static ServerActionResult ExecuteDelegateActor(GameState state, DelegateActorClientCommand command)
	{
		if (string.IsNullOrWhiteSpace(command.ActorId))
			return ServerActionResult.Reject(LocalizationService.T("ui.common.none"), ErrorCode.InvalidActor.ToWireCode());
		if (!RoomRuntimeModule.IsPrimaryOwner(state, command.PlayerSessionId, command.ActorId))
		{
			return ServerActionResult.Reject(
				LocalizationService.TOrFallback(
					"log.multiplayer.delegate_actor_unauthorized",
					"Only the primary owner can delegate {actor}.",
					("actor", command.ActorId)),
				ErrorCode.UnauthorizedActor.ToWireCode());
		}

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
				ErrorCode.DelegateFailed.ToWireCode());
	}

	private static ServerActionResult ExecuteReclaimPrimaryActor(GameState state, ReclaimPrimaryActorClientCommand command)
	{
		if (string.IsNullOrWhiteSpace(command.ActorId) || string.IsNullOrWhiteSpace(command.PlayerSessionId))
			return ServerActionResult.Reject(LocalizationService.T("ui.common.none"), ErrorCode.InvalidActor.ToWireCode());

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
				ErrorCode.ReclaimFailed.ToWireCode());
	}

	private static ServerActionResult ExecuteAssignPrimaryActor(GameState state, AssignPrimaryActorClientCommand command)
	{
		if (string.IsNullOrWhiteSpace(command.PlayerSessionId)
			|| string.IsNullOrWhiteSpace(command.TargetPlayerSessionId)
			|| string.IsNullOrWhiteSpace(command.TargetActorId))
		{
			return ServerActionResult.Reject("Invalid assignment request.", ErrorCode.InvalidAssignRequest.ToWireCode());
		}

		var ok = RoomRuntimeModule.AssignPrimaryActorByHost(
			state,
			command.PlayerSessionId,
			command.TargetPlayerSessionId,
			command.TargetActorId);
		return ok
			? ServerActionResult.Accept(logs:
			[
				LocalizationService.TOrFallback(
					"log.multiplayer.assign_primary_actor",
					"Assigned {actor} to {player}.",
					("actor", command.TargetActorId),
					("player", command.TargetPlayerSessionId)),
			])
			: ServerActionResult.Reject(
				LocalizationService.TOrFallback(
					"log.multiplayer.assign_primary_actor_failed",
					"Failed to assign {actor}.",
					("actor", command.TargetActorId)),
				ErrorCode.AssignPrimaryActorFailed.ToWireCode());
	}

	private static ServerActionResult ExecuteKickPlayer(GameState state, KickPlayerClientCommand command)
	{
		if (string.IsNullOrWhiteSpace(command.PlayerSessionId)
			|| string.IsNullOrWhiteSpace(command.TargetPlayerSessionId))
		{
			return ServerActionResult.Reject("Invalid kick request.", ErrorCode.InvalidKickRequest.ToWireCode());
		}

		var ok = RoomRuntimeModule.KickPlayerByHost(
			state,
			command.PlayerSessionId,
			command.TargetPlayerSessionId);
		return ok
			? ServerActionResult.Accept(logs:
			[
				LocalizationService.TOrFallback(
					"log.multiplayer.kick_player",
					"Kicked player {player}.",
					("player", command.TargetPlayerSessionId)),
			])
			: ServerActionResult.Reject(
				LocalizationService.TOrFallback(
					"log.multiplayer.kick_player_failed",
					"Failed to kick player {player}.",
					("player", command.TargetPlayerSessionId)),
				ErrorCode.KickPlayerFailed.ToWireCode());
	}

	private static ServerActionResult ExecuteStartCombat(GameState state, StartCombatClientCommand command)
	{
		if (state.Room.SimulationMode == RoomSimulationMode.CombatTurnBased)
			return ServerActionResult.Accept();
		if (string.IsNullOrWhiteSpace(command.TargetActorId))
			return ServerActionResult.Reject("Invalid combat target.", ErrorCode.StartCombatRejected.ToWireCode());

		var actor = ResolveActor(state, command.ActorId);
		var target = ActorModule.GetById(state, command.TargetActorId);
		if (actor == null || target == null)
			return ServerActionResult.Reject("Invalid combat actor.", ErrorCode.StartCombatRejected.ToWireCode());

		if (!string.Equals(target.Faction, Factions.Hostile, StringComparison.Ordinal))
			return ServerActionResult.Reject("Target is not hostile.", ErrorCode.StartCombatRejected.ToWireCode());

		TimelineTurnManager.SyncActors(state);
		if (state.Timeline.Actors.Count == 0)
			TimelineTurnManager.Reset(state);

		return ServerActionResult.Accept(logs: ["combat_mode_entered"]);
	}

	private static ServerActionResult ExecuteEndTurn(GameState state, EndTurnClientCommand command)
	{
		if (state.Room.SimulationMode != RoomSimulationMode.CombatTurnBased)
			return ServerActionResult.Reject("Room is not in combat mode.", ErrorCode.NotInCombat.ToWireCode());
		if (!TimelineTurnManager.IsPlayerTurn(state))
			return ServerActionResult.Reject("Not your turn.", ErrorCode.NotYourTurn.ToWireCode());

		var timelineResult = TimelineTurnGateway.SubmitPlayerAction(state, TimelinePlayerAction.Rest());
		return timelineResult.ActionConsumed
			? ServerActionResult.Accept(events: timelineResult.Events)
			: ServerActionResult.Reject("Failed to end turn.", ErrorCode.EndTurnRejected.ToWireCode());
	}

	private static ServerActionResult ExecuteCombatAttack(GameState state, AttackClientCommand command)
	{
		var attackerActorId = command.ActorId ?? string.Empty;
		if (!RoomCombatRules.TryValidateAttack(state, attackerActorId, command.TargetActorId, out var ruleErrorCode))
		{
			return ServerActionResult.Reject(
				ruleErrorCode == ErrorCode.PvpDisabled ? "PvP is disabled in this room." : "Friendly fire is disabled for your team.",
				ruleErrorCode.ToWireCode());
		}

		var timelineResult = TimelineTurnGateway.SubmitPlayerAction(
			state,
			TimelinePlayerAction.Attack(command.TargetActorId, command.SkillId, command.TargetLimbId));
		return timelineResult.ActionConsumed
			? ServerActionResult.Accept(events: timelineResult.Events)
			: ServerActionResult.Reject("Attack rejected.", ErrorCode.NotYourTurn.ToWireCode());
	}

	private static ServerActionResult ExecuteUseSkill(GameState state, UseSkillClientCommand command)
	{
		if (state.Room.SimulationMode != RoomSimulationMode.CombatTurnBased)
			return ServerActionResult.Reject("Room is not in combat mode.", ErrorCode.NotInCombat.ToWireCode());

		var timelineResult = TimelineTurnGateway.SubmitPlayerAction(
			state,
			TimelinePlayerAction.CastSkill(
				command.SkillId,
				command.TargetType,
				command.TargetActorId,
				command.TargetLimbId,
				command.TargetItemId,
				command.TargetX,
				command.TargetY,
				command.TargetZ));
		return timelineResult.ActionConsumed
			? ServerActionResult.Accept(events: timelineResult.Events)
			: ServerActionResult.Reject("Use skill rejected.", ErrorCode.UseSkillRejected.ToWireCode());
	}

	private static ServerActionResult ExecuteEndCombat(GameState state, EndCombatClientCommand command)
	{
		if (state.Room.SimulationMode != RoomSimulationMode.CombatTurnBased)
			return ServerActionResult.Accept();

		var factions = state.Actors.Values
			.Where(actor => CombatModule.CheckVitalStatus(actor) != "death_instant")
			.Select(actor => actor.Faction)
			.Distinct(StringComparer.Ordinal)
			.Count();
		if (factions > 1)
			return ServerActionResult.Reject("Combat is still active.", ErrorCode.EndCombatRejected.ToWireCode());

		return ServerActionResult.Accept(logs: ["combat_mode_exited"]);
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

	private static Actor? ResolveRewardActor(GameState state, GameEvent e)
	{
		if (!string.IsNullOrWhiteSpace(e.InitiatorId))
		{
			var initiator = ActorModule.GetById(state, e.InitiatorId);
			if (initiator != null)
				return initiator;
		}

		return ActorModule.GetPlayer(state);
	}

	private static Actor? ResolveActor(GameState state, string? actorId)
	{
		if (!string.IsNullOrWhiteSpace(actorId))
			return ActorModule.GetById(state, actorId);

		if (state.Room.IsActive)
			return null;

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

	private static ServerActionResult? TryReserveInteractionTarget(
		GameState state,
		string? playerSessionId,
		string targetActorId,
		string? effectType,
		DateTimeOffset now)
	{
		if (string.Equals(effectType, "trade", StringComparison.OrdinalIgnoreCase))
			return TryReserveIfNeeded(state, playerSessionId, BuildTradeReservationKey(targetActorId), now);
		if (string.Equals(effectType, "talk", StringComparison.OrdinalIgnoreCase))
			return TryReserveIfNeeded(state, playerSessionId, BuildPrefixedReservationKey("dialog", targetActorId), now);

		return null;
	}

	private static ServerActionResult? TryReserveIfNeeded(
		GameState state,
		string? playerSessionId,
		string reservationKey,
		DateTimeOffset now)
	{
		if (!state.Room.IsActive)
			return null;
		if (string.IsNullOrWhiteSpace(playerSessionId))
			return ServerActionResult.Reject("Missing player session id.", ErrorCode.MissingPlayerSession.ToWireCode());
		if (RoomRuntimeModule.TryReserveInteraction(state, reservationKey, playerSessionId, now, out var conflictingReservation))
			return null;

		return ServerActionResult.Busy(
			LocalizationService.TOrFallback(
				"log.server_action.reservation_busy",
				"Another player is already using {reservationKey}.",
				("reservationKey", reservationKey)),
			errorCode: ErrorCode.ReservationBusy.ToWireCode(),
			reservationKey: reservationKey,
			busyByPlayerSessionId: conflictingReservation?.PlayerSessionId);
	}

	private static string BuildTradeReservationKey(string traderActorId) =>
		BuildPrefixedReservationKey("trade", traderActorId);

	private static string BuildContainerReservationKey(ChestTakeClientCommand command) =>
		BuildContainerReservationKey(
			command.ContainerSource,
			command.ContainerInstanceId,
			command.ContainerOwnerActorId,
			command.ContainerX,
			command.ContainerY,
			command.ContainerZ);

	private static string BuildContainerReservationKey(ChestTakeAllClientCommand command) =>
		BuildContainerReservationKey(
			command.ContainerSource,
			command.ContainerInstanceId,
			command.ContainerOwnerActorId,
			command.ContainerX,
			command.ContainerY,
			command.ContainerZ);

	private static string BuildContainerReservationKey(ChestPutClientCommand command) =>
		BuildContainerReservationKey(
			command.ContainerSource,
			command.ContainerInstanceId,
			command.ContainerOwnerActorId,
			command.ContainerX,
			command.ContainerY,
			command.ContainerZ);

	private static string BuildContainerReservationKey(
		ContainerSourceKind source,
		string containerInstanceId,
		string? ownerActorId,
		int x,
		int y,
		int z) => source switch
	{
		ContainerSourceKind.Inventory => $"container:inventory:{ownerActorId ?? string.Empty}:{containerInstanceId}",
		_ => $"container:ground:{x}:{y}:{z}:{containerInstanceId}",
	};

	private static string BuildPrefixedReservationKey(string prefix, string rawId)
	{
		var normalizedId = rawId.Trim();
		if (normalizedId.Contains(':', StringComparison.Ordinal))
			return normalizedId;

		return $"{prefix}:{normalizedId}";
	}
}
