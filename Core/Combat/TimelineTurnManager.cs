using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.AI;
using MiniRPG.Core.Facility;

namespace MiniRPG.Core.Combat;

public enum TimelinePlayerActionType
{
	Move,
	Dig,
	Attack,
	CastSkill,
	EatInventory,
	Rest,
	FacilityDeliver,
	FacilityConstruct,
}

public sealed class TimelinePlayerAction
{
	public TimelinePlayerActionType Type { get; }
	public int Dx { get; }
	public int Dy { get; }
	public string? SkillId { get; }
	public SkillTargetType TargetType { get; }
	public string? TargetActorId { get; }
	public string? TargetLimbId { get; }
	public string? TargetItemId { get; }
	public int TargetX { get; }
	public int TargetY { get; }
	public int TargetZ { get; }
	public int InventoryIndex { get; }
	public string? FacilityId { get; }

	private TimelinePlayerAction(
		TimelinePlayerActionType type,
		int dx = 0,
		int dy = 0,
		string? skillId = null,
		SkillTargetType targetType = SkillTargetType.Self,
		string? targetActorId = null,
		string? targetLimbId = null,
		string? targetItemId = null,
		int targetX = 0,
		int targetY = 0,
		int targetZ = 0,
		int inventoryIndex = -1,
		string? facilityId = null)
	{
		Type = type;
		Dx = dx;
		Dy = dy;
		SkillId = skillId;
		TargetType = targetType;
		TargetActorId = targetActorId;
		TargetLimbId = targetLimbId;
		TargetItemId = targetItemId;
		TargetX = targetX;
		TargetY = targetY;
		TargetZ = targetZ;
		InventoryIndex = inventoryIndex;
		FacilityId = facilityId;
	}

	public static TimelinePlayerAction Move(int dx, int dy) =>
		new(TimelinePlayerActionType.Move, dx: dx, dy: dy);

	public static TimelinePlayerAction Dig(int dx, int dy, string skillId) =>
		new(TimelinePlayerActionType.Dig, dx: dx, dy: dy, skillId: skillId);

	public static TimelinePlayerAction Attack(string targetActorId, string? skillId, string? targetLimbId) =>
		new(TimelinePlayerActionType.Attack,
			skillId: skillId,
			targetType: SkillTargetType.Actor,
			targetActorId: targetActorId,
			targetLimbId: targetLimbId);

	public static TimelinePlayerAction CastSkill(
		string skillId,
		SkillTargetType targetType,
		string? targetActorId = null,
		string? targetLimbId = null,
		string? targetItemId = null,
		int targetX = 0,
		int targetY = 0,
		int targetZ = 0) =>
		new(TimelinePlayerActionType.CastSkill,
			skillId: skillId,
			targetType: targetType,
			targetActorId: targetActorId,
			targetLimbId: targetLimbId,
			targetItemId: targetItemId,
			targetX: targetX,
			targetY: targetY,
			targetZ: targetZ);

	public static TimelinePlayerAction EatInventory(int inventoryIndex) =>
		new(TimelinePlayerActionType.EatInventory, inventoryIndex: inventoryIndex);

	public static TimelinePlayerAction Rest() =>
		new(TimelinePlayerActionType.Rest);

	public static TimelinePlayerAction FacilityDeliver(string facilityId) =>
		new(TimelinePlayerActionType.FacilityDeliver, facilityId: facilityId);

	public static TimelinePlayerAction FacilityConstruct(string facilityId) =>
		new(TimelinePlayerActionType.FacilityConstruct, facilityId: facilityId);
}

public sealed class TimelineActorState
{
	public string ActorId { get; set; } = "";
	public float Charge { get; set; }
}

public sealed class TimelineState
{
	public string? CurrentActorId { get; set; }
	public string? LastActorId { get; set; }
	public List<TimelineActorState> Actors { get; set; } = [];

	public void Reset()
	{
		CurrentActorId = null;
		LastActorId = null;
		Actors.Clear();
	}
}

public sealed class TimelineStepResult
{
	public List<GameEvent> Events { get; } = [];
	public string? ActingActorId { get; set; }
	public bool ActionConsumed { get; set; }
	public bool PlayerTurnReady { get; set; }
	public bool HasPendingAutoStep { get; set; }
}

public enum TimelineDebugPhase
{
	PlayerTurn,
	AutoAdvance,
	WatchMode,
	NoActiveActor,
	Dead,
}

public enum TimelineInputLockReason
{
	None,
	OtherActorsActing,
	WatchMode,
	NoActiveActor,
	Dead,
}

public sealed class TimelineDebugEntry
{
	public string ActorName { get; set; } = "";
	public bool IsPlayer { get; set; }
	public bool IsCurrent { get; set; }
	public bool IsLast { get; set; }
	public float Charge { get; set; }
	public float Speed { get; set; }
	public float EtaToAct { get; set; }
}

public sealed class TimelineDebugSnapshot
{
	public TimelineDebugPhase Phase { get; set; }
	public string? CurrentActorName { get; set; }
	public string? LastActorName { get; set; }
	public bool IsPlayerTurn { get; set; }
	public bool HasPendingAutoAdvance { get; set; }
	public TimelineInputLockReason InputLockedReason { get; set; }
	public int WorldTurn { get; set; }
	public List<TimelineDebugEntry> Entries { get; set; } = [];
}

public static class TimelineTurnManager
{
	public const float ActionThreshold = 100f;
	private const float MinimumActiveSpeed = 0.05f;

	public static void Reset(GameState state, bool playerStarts = true)
	{
		state.Timeline.Reset();
		SyncActors(state);

		if (!playerStarts)
			return;

		var playerEntry = FindEntry(state, state.PlayerId);
		if (playerEntry == null)
			return;

		playerEntry.Charge = ActionThreshold;
		state.Timeline.CurrentActorId = state.PlayerId;
		state.Timeline.LastActorId = null;
	}

	public static void SyncActors(GameState state)
	{
		var liveActorIds = new HashSet<string>(
			state.Actors.Values
				.Where(CanParticipateInTimeline)
				.Select(static actor => actor.Id),
			StringComparer.Ordinal);

		state.Timeline.Actors.RemoveAll(entry => !liveActorIds.Contains(entry.ActorId));
		foreach (var actorId in liveActorIds)
		{
			if (FindEntry(state, actorId) == null)
				state.Timeline.Actors.Add(new TimelineActorState { ActorId = actorId });
		}

		if (state.Timeline.CurrentActorId != null && !liveActorIds.Contains(state.Timeline.CurrentActorId))
			state.Timeline.CurrentActorId = null;
		if (state.Timeline.LastActorId != null && !liveActorIds.Contains(state.Timeline.LastActorId))
			state.Timeline.LastActorId = null;
	}

	public static bool IsPlayerTurn(GameState state)
	{
		var actor = EnsureCurrentActor(state);
		return actor?.Id == state.PlayerId && IsPlayerControllable(actor);
	}

	public static float CalculateSpeed(Actor actor)
	{
		return CalculateSpeed(state: null, actor);
	}

	public static float CalculateSpeed(GameState? state, Actor actor)
	{
		if (!CanParticipateInTimeline(actor))
			return 0f;
		if (HasPendingHealthDeath(actor))
			return MinimumActiveSpeed;

		var moving = actor.GetCapacity(Caps.Moving);
		var consciousness = actor.GetCapacity(Caps.Consciousness);
		var metabolism = actor.GetCapacity(Caps.Metabolism);
		var raw = moving * consciousness * metabolism;
		if (state?.World != null
			&& state.World.IsWeatherExposed(actor.X, actor.Y, actor.Z))
		{
			var weather = WeatherRules.GetLocalWeather(state, actor.X, actor.Y, actor.Z);
			raw *= WeatherRules.GetExposedSpeedMultiplier(weather);
		}

		return raw > 0f ? MathF.Max(raw, MinimumActiveSpeed) : MinimumActiveSpeed;
	}

	public static TimelineDebugSnapshot CreateDebugSnapshot(GameState state, bool playerDead, bool? watchModeEnabled = null)
	{
		SyncActors(state);
		var resolvedWatchMode = watchModeEnabled ?? false;

		var currentActor = playerDead
			? TryGetActiveActor(state, state.Timeline.CurrentActorId)
			: EnsureCurrentActor(state);
		var lastActor = TryGetActiveActor(state, state.Timeline.LastActorId);
		var isPlayerTurn = !playerDead && currentActor?.Id == state.PlayerId && IsPlayerControllable(currentActor);
		var hasPendingAutoAdvance = !playerDead
			&& currentActor != null
			&& (resolvedWatchMode || !isPlayerTurn);

		return new TimelineDebugSnapshot
		{
			Phase = ResolveDebugPhase(playerDead, resolvedWatchMode, currentActor, isPlayerTurn),
			CurrentActorName = currentActor != null ? GetActorDisplayName(currentActor) : null,
			LastActorName = lastActor != null ? GetActorDisplayName(lastActor) : null,
			IsPlayerTurn = isPlayerTurn,
			HasPendingAutoAdvance = hasPendingAutoAdvance,
			InputLockedReason = ResolveInputLockReason(playerDead, resolvedWatchMode, currentActor, isPlayerTurn),
			WorldTurn = state.Turn,
			Entries = BuildDebugEntries(state, currentActor),
		};
	}

	public static TimelineStepResult SubmitPlayerAction(GameState state, TimelinePlayerAction action)
	{
		var result = new TimelineStepResult();
		var actor = EnsureCurrentActor(state);
		if (actor == null || actor.Id != state.PlayerId || !IsPlayerControllable(actor))
		{
			result.PlayerTurnReady = false;
			result.HasPendingAutoStep = actor != null;
			return result;
		}

		if (TryHandleHealthDeath(state, actor, result.Events))
		{
			result.ActingActorId = actor.Id;
			FinalizeActorRemoval(state, actor.Id, watchModeEnabled: false, result);
			return result;
		}

		result.ActingActorId = actor.Id;
		if (!TryExecutePlayerAction(state, actor, action, result.Events))
		{
			result.PlayerTurnReady = true;
			return result;
		}

		FinalizeConsumedAction(state, actor.Id, watchModeEnabled: false, result);
		return result;
	}

	public static TimelineStepResult AdvanceAuto(GameState state, bool watchModeEnabled)
	{
		var result = new TimelineStepResult();
		var actor = EnsureCurrentActor(state);
		if (actor == null)
		{
			result.PlayerTurnReady = false;
			result.HasPendingAutoStep = false;
			return result;
		}

		if (TryHandleHealthDeath(state, actor, result.Events))
		{
			result.ActingActorId = actor.Id;
			FinalizeActorRemoval(state, actor.Id, watchModeEnabled, result);
			return result;
		}

		if (actor.Id == state.PlayerId && !watchModeEnabled && IsPlayerControllable(actor))
		{
			result.PlayerTurnReady = true;
			return result;
		}

		result.ActingActorId = actor.Id;
		if (actor.Id == state.PlayerId)
		{
			actor.BrainId ??= "simple";
		}

		var execution = AIDispatcher.DecideAndExecuteAnyResult(state, actor, tickBuffs: false);
		result.Events.AddRange(execution.Events);
		if (!execution.Consumed)
		{
			result.PlayerTurnReady = actor.Id == state.PlayerId && !watchModeEnabled;
			result.HasPendingAutoStep = !result.PlayerTurnReady;
			return result;
		}

		FinalizeConsumedAction(state, actor.Id, watchModeEnabled, result);
		return result;
	}

	private static bool TryExecutePlayerAction(
		GameState state,
		Actor player,
		TimelinePlayerAction action,
		List<GameEvent> events)
	{
		switch (action.Type)
		{
			case TimelinePlayerActionType.Move:
				return TryExecuteMove(state, player, action, events);
			case TimelinePlayerActionType.Dig:
				return TryExecuteDig(state, player, action, events);
			case TimelinePlayerActionType.Attack:
				return TryExecuteAttack(state, player, action, events);
			case TimelinePlayerActionType.CastSkill:
				return TryExecuteCastSkill(state, player, action, events);
			case TimelinePlayerActionType.EatInventory:
				return TryExecuteEat(state, player, action, events);
			case TimelinePlayerActionType.Rest:
				return TryExecuteRest(state, player, events);
			case TimelinePlayerActionType.FacilityDeliver:
				return TryExecuteFacilityDeliver(state, player, action, events);
			case TimelinePlayerActionType.FacilityConstruct:
				return TryExecuteFacilityConstruct(state, player, action, events);
			default:
				return false;
		}
	}

	private static bool TryExecuteMove(
		GameState state,
		Actor player,
		TimelinePlayerAction action,
		List<GameEvent> events)
	{
		var targetX = player.X + action.Dx;
		var targetY = player.Y + action.Dy;
		var hostile = ActorModule.GetAllAt(state, targetX, targetY, player.Z)
			.FirstOrDefault(other =>
				other.Id != player.Id &&
				FactionRelation.IsHostile(player.Faction, other.Faction));

		player.TickBuffs();
		if (hostile != null)
		{
			var attackEvents = ActionModule.TryAttack(state, player, hostile);
			if (attackEvents.Count == 0)
				return false;

			events.AddRange(attackEvents);
			return true;
		}

		events.AddRange(ActionModule.TryMove(state, player, action.Dx, action.Dy));
		return true;
	}

	private static bool TryExecuteDig(
		GameState state,
		Actor player,
		TimelinePlayerAction action,
		List<GameEvent> events)
	{
		var castAction = TimelinePlayerAction.CastSkill(
			action.SkillId ?? string.Empty,
			SkillTargetType.Cell,
			targetX: player.X + action.Dx,
			targetY: player.Y + action.Dy,
			targetZ: player.Z);
		return TryExecuteCastSkill(state, player, castAction, events);
	}

	private static bool TryExecuteAttack(
		GameState state,
		Actor player,
		TimelinePlayerAction action,
		List<GameEvent> events)
	{
		var castAction = TimelinePlayerAction.CastSkill(
			action.SkillId ?? string.Empty,
			SkillTargetType.Actor,
			targetActorId: action.TargetActorId,
			targetLimbId: action.TargetLimbId);
		return TryExecuteCastSkill(state, player, castAction, events);
	}

	private static bool TryExecuteCastSkill(
		GameState state,
		Actor player,
		TimelinePlayerAction action,
		List<GameEvent> events)
	{
		if (string.IsNullOrEmpty(action.SkillId))
			return false;

		var result = ActionModule.TryCastSkill(
			state,
			player,
			action.SkillId,
			action.TargetType,
			targetActorId: action.TargetActorId,
			targetLimbId: action.TargetLimbId,
			targetItemId: action.TargetItemId,
			targetX: action.TargetX,
			targetY: action.TargetY,
			targetZ: action.TargetZ);
		events.AddRange(result.Events);
		return result.Consumed;
	}

	private static bool TryExecuteEat(
		GameState state,
		Actor player,
		TimelinePlayerAction action,
		List<GameEvent> events)
	{
		var result = NeedActionModule.TryConsumeFood(state, player, action.InventoryIndex);
		events.AddRange(result.Events);
		return result.Consumed;
	}

	private static bool TryExecuteRest(
		GameState state,
		Actor player,
		List<GameEvent> events)
	{
		var result = NeedActionModule.TryRest(
			state,
			player,
			RestContext.ForPlayerBedroll(NeedActionModule.GetBedrollQuality(player)));
		events.AddRange(result.Events);
		return result.Consumed;
	}

	private static bool TryExecuteFacilityDeliver(
		GameState state,
		Actor player,
		TimelinePlayerAction action,
		List<GameEvent> events)
	{
		if (string.IsNullOrWhiteSpace(action.FacilityId))
			return false;

		var result = FacilityConstructionModule.TryDeliverMaterials(state, player, action.FacilityId);
		return result.Consumed;
	}

	private static bool TryExecuteFacilityConstruct(
		GameState state,
		Actor player,
		TimelinePlayerAction action,
		List<GameEvent> events)
	{
		if (string.IsNullOrWhiteSpace(action.FacilityId))
			return false;

		var result = FacilityConstructionModule.TryConstructFacility(state, player, action.FacilityId);
		return result.Consumed;
	}

	private static void FinalizeConsumedAction(GameState state, string actorId, bool watchModeEnabled, TimelineStepResult result)
	{
		result.ActionConsumed = true;
		ActorModule.GetById(state, actorId)?.TickSkillCooldowns();
		result.Events.AddRange(TurnModule.AdvanceWorld(state));
		ConsumeTurn(state, actorId);
		result.PlayerTurnReady = IsPlayerTurn(state);
		result.HasPendingAutoStep = watchModeEnabled || !result.PlayerTurnReady;
	}

	private static void ConsumeTurn(GameState state, string actorId)
	{
		var entry = FindEntry(state, actorId);
		if (entry != null)
			entry.Charge = Math.Max(0f, entry.Charge - ActionThreshold);

		state.Timeline.CurrentActorId = null;
		state.Timeline.LastActorId = actorId;
		SyncActors(state);
	}

	private static Actor? EnsureCurrentActor(GameState state)
	{
		SyncActors(state);
		if (state.Timeline.CurrentActorId != null)
		{
			var current = ActorModule.GetById(state, state.Timeline.CurrentActorId);
			if (current != null && CanParticipateInTimeline(current))
			{
				HealthSystem.Sync(current, state.Turn, DefaultEnvironmentExposureProvider.Instance.Capture(state, current));
				if (CanParticipateInTimeline(current))
					return current;
			}

			state.Timeline.CurrentActorId = null;
		}

		var ready = PickReadyActor(state);
		if (ready != null)
		{
			HealthSystem.Sync(ready, state.Turn, DefaultEnvironmentExposureProvider.Instance.Capture(state, ready));
			if (IsActorActive(ready) || HealthSystem.GetFatalCause(ready) != null)
			{
				state.Timeline.CurrentActorId = ready.Id;
				return ready;
			}
		}

		AdvanceCharges(state);
		ready = PickReadyActor(state);
		if (ready != null)
		{
			HealthSystem.Sync(ready, state.Turn, DefaultEnvironmentExposureProvider.Instance.Capture(state, ready));
			if (!(IsActorActive(ready) || HealthSystem.GetFatalCause(ready) != null))
				ready = null;
		}
		state.Timeline.CurrentActorId = ready?.Id;
		return ready;
	}

	private static Actor? PickReadyActor(GameState state)
	{
		return state.Timeline.Actors
			.Select(entry => new
			{
				Entry = entry,
				Actor = ActorModule.GetById(state, entry.ActorId),
			})
			.Where(item => item.Actor != null && CanParticipateInTimeline(item.Actor) && item.Entry.Charge >= ActionThreshold)
			.OrderByDescending(item => item.Entry.Charge)
			.ThenByDescending(item => CalculateSpeed(state, item.Actor!))
			.ThenBy(item => string.Equals(item.Entry.ActorId, state.Timeline.LastActorId, StringComparison.Ordinal) ? 1 : 0)
			.ThenBy(item => item.Entry.ActorId, StringComparer.Ordinal)
			.Select(item => item.Actor)
			.FirstOrDefault();
	}

	private static void AdvanceCharges(GameState state)
	{
		var candidates = state.Timeline.Actors
			.Select(entry => new
			{
				Entry = entry,
				Actor = ActorModule.GetById(state, entry.ActorId),
			})
			.Where(item => item.Actor != null && CanParticipateInTimeline(item.Actor))
			.Select(item => new
			{
				item.Entry,
				Speed = CalculateSpeed(state, item.Actor!),
			})
			.Where(item => item.Speed > 0f)
			.ToList();

		if (candidates.Count == 0)
			return;

		float minDelta = float.MaxValue;
		foreach (var candidate in candidates)
		{
			var remaining = Math.Max(0f, ActionThreshold - candidate.Entry.Charge);
			var delta = remaining / candidate.Speed;
			if (delta < minDelta)
				minDelta = delta;
		}

		if (!float.IsFinite(minDelta) || minDelta <= 0f)
			return;

		foreach (var candidate in candidates)
			candidate.Entry.Charge += candidate.Speed * minDelta;
	}

	private static TimelineActorState? FindEntry(GameState state, string actorId) =>
		state.Timeline.Actors.FirstOrDefault(entry => string.Equals(entry.ActorId, actorId, StringComparison.Ordinal));

	private static List<TimelineDebugEntry> BuildDebugEntries(GameState state, Actor? currentActor)
	{
		return state.Timeline.Actors
			.Select(entry => new
			{
				Entry = entry,
				Actor = ActorModule.GetById(state, entry.ActorId),
			})
			.Where(item => item.Actor != null && CanParticipateInTimeline(item.Actor))
			.Select(item =>
			{
				var actor = item.Actor!;
				var isCurrent = currentActor != null && string.Equals(actor.Id, currentActor.Id, StringComparison.Ordinal);
				var speed = CalculateSpeed(state, actor);
				var eta = isCurrent ? 0f : CalculateEtaToAct(item.Entry.Charge, speed);
				return new
				{
					Actor = actor,
					IsCurrent = isCurrent,
					IsLast = string.Equals(actor.Id, state.Timeline.LastActorId, StringComparison.Ordinal),
					Charge = item.Entry.Charge,
					Speed = speed,
					Eta = eta,
				};
			})
			.OrderByDescending(item => item.IsCurrent)
			.ThenBy(item => item.Eta)
			.ThenByDescending(item => item.Speed)
			.ThenBy(item => item.Actor.Id, StringComparer.Ordinal)
			.Take(4)
			.Select(item => new TimelineDebugEntry
			{
				ActorName = GetActorDisplayName(item.Actor),
				IsPlayer = string.Equals(item.Actor.Id, state.PlayerId, StringComparison.Ordinal),
				IsCurrent = item.IsCurrent,
				IsLast = item.IsLast,
				Charge = item.Charge,
				Speed = item.Speed,
				EtaToAct = item.Eta,
			})
			.ToList();
	}

	private static float CalculateEtaToAct(float charge, float speed)
	{
		if (speed <= 0f)
			return float.PositiveInfinity;

		return Math.Max(0f, ActionThreshold - charge) / speed;
	}

	private static TimelineDebugPhase ResolveDebugPhase(
		bool playerDead,
		bool watchMode,
		Actor? currentActor,
		bool isPlayerTurn)
	{
		if (playerDead)
			return TimelineDebugPhase.Dead;
		if (currentActor == null)
			return TimelineDebugPhase.NoActiveActor;
		if (watchMode)
			return TimelineDebugPhase.WatchMode;
		return isPlayerTurn
			? TimelineDebugPhase.PlayerTurn
			: TimelineDebugPhase.AutoAdvance;
	}

	private static TimelineInputLockReason ResolveInputLockReason(
		bool playerDead,
		bool watchMode,
		Actor? currentActor,
		bool isPlayerTurn)
	{
		if (playerDead)
			return TimelineInputLockReason.Dead;
		if (currentActor == null)
			return TimelineInputLockReason.NoActiveActor;
		if (watchMode)
			return TimelineInputLockReason.WatchMode;
		return isPlayerTurn
			? TimelineInputLockReason.None
			: TimelineInputLockReason.OtherActorsActing;
	}

	private static Actor? TryGetActiveActor(GameState state, string? actorId)
	{
		if (string.IsNullOrEmpty(actorId))
			return null;

		var actor = ActorModule.GetById(state, actorId);
		return actor != null && CanParticipateInTimeline(actor) ? actor : null;
	}

	private static string GetActorDisplayName(Actor actor) =>
		string.IsNullOrWhiteSpace(actor.DisplayName) ? "???" : actor.DisplayName;

	private static bool IsActorActive(Actor actor)
	{
		if (actor.Limbs.Count == 0)
			return false;

		var status = CombatModule.CheckVitalStatus(actor);
		return status != "death_instant";
	}

	private static bool HasPendingHealthDeath(Actor actor) =>
		!string.IsNullOrWhiteSpace(HealthSystem.GetFatalCause(actor));

	private static bool CanParticipateInTimeline(Actor actor) =>
		IsActorActive(actor) || HasPendingHealthDeath(actor);

	private static bool IsPlayerControllable(Actor actor) =>
		CombatModule.CheckVitalStatus(actor) != "incapacitate";

	private static bool TryHandleHealthDeath(GameState state, Actor actor, List<GameEvent> events)
	{
		var fatalCause = HealthSystem.GetFatalCause(actor);
		if (string.IsNullOrWhiteSpace(fatalCause))
			return false;

		var deathEvent = new GameEvent(fatalCause)
		{
			TargetX = actor.X,
			TargetY = actor.Y,
			TargetZ = actor.Z,
		};
		IdentificationModule.PopulateTargetIdentity(deathEvent, state, actor);
		events.Add(deathEvent);
		SurgeryModule.TrySpawnCorpseOnDeath(state, actor, events, fatalCause);
		ActorModule.Remove(state, actor.Id);
		return true;
	}

	private static void FinalizeActorRemoval(GameState state, string actorId, bool watchModeEnabled, TimelineStepResult result)
	{
		result.ActionConsumed = true;
		state.Timeline.CurrentActorId = null;
		state.Timeline.LastActorId = actorId;
		SyncActors(state);
		result.PlayerTurnReady = IsPlayerTurn(state);
		result.HasPendingAutoStep = watchModeEnabled || !result.PlayerTurnReady;
	}

	private static InteractionDef? FindInteraction(string? actionDefId)
	{
		if (string.IsNullOrEmpty(actionDefId))
			return null;

		return InteractionDefs.All.FirstOrDefault(def => string.Equals(def.Id, actionDefId, StringComparison.Ordinal));
	}
}
