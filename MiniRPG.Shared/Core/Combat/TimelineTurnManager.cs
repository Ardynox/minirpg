using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.AI;
using MiniRPG.Core.Data;
using MiniRPG.Core.Facility;
using MiniRPG.Core.Needs;
using MiniRPG.Core.World;

namespace MiniRPG.Core.Combat;

public enum TimelinePlayerActionType
{
	Move,
	Dig,
	Attack,
	CastSkill,
	EatInventory,
	Rest,
	TerrainBuild,
	TerrainDemolish,
	FacilityPlaceBlueprint,
	FacilityDemolish,
	FacilityDeliver,
	FacilityConstruct,
	Climb,
}

public sealed class TimelinePlayerAction
{
	public TimelinePlayerActionType Type { get; }
	public int Dx { get; }
	public int Dy { get; }
	public int Dz { get; }
	public string? SkillId { get; }
	public SkillTargetType TargetType { get; }
	public string? TargetActorId { get; }
	public string? TargetLimbId { get; }
	public string? TargetItemId { get; }
	public int TargetX { get; }
	public int TargetY { get; }
	public int TargetZ { get; }
	public int InventoryIndex { get; }
	public string? TerrainId { get; }
	public string? FacilityId { get; }
	public string? FacilityDefId { get; }
	public FacilityRotation FacilityRotation { get; }

	private TimelinePlayerAction(
		TimelinePlayerActionType type,
		int dx = 0,
		int dy = 0,
		int dz = 0,
		string? skillId = null,
		SkillTargetType targetType = SkillTargetType.Self,
		string? targetActorId = null,
		string? targetLimbId = null,
		string? targetItemId = null,
		int targetX = 0,
		int targetY = 0,
		int targetZ = 0,
		int inventoryIndex = -1,
		string? terrainId = null,
		string? facilityId = null,
		string? facilityDefId = null,
		FacilityRotation facilityRotation = FacilityRotation.North)
	{
		Type = type;
		Dx = dx;
		Dy = dy;
		Dz = dz;
		SkillId = skillId;
		TargetType = targetType;
		TargetActorId = targetActorId;
		TargetLimbId = targetLimbId;
		TargetItemId = targetItemId;
		TargetX = targetX;
		TargetY = targetY;
		TargetZ = targetZ;
		InventoryIndex = inventoryIndex;
		TerrainId = terrainId;
		FacilityId = facilityId;
		FacilityDefId = facilityDefId;
		FacilityRotation = facilityRotation;
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

	public static TimelinePlayerAction TerrainBuild(string terrainId, int targetX, int targetY, int targetZ) =>
		new(
			TimelinePlayerActionType.TerrainBuild,
			terrainId: terrainId,
			targetX: targetX,
			targetY: targetY,
			targetZ: targetZ);

	public static TimelinePlayerAction TerrainDemolish(int targetX, int targetY, int targetZ) =>
		new(
			TimelinePlayerActionType.TerrainDemolish,
			targetX: targetX,
			targetY: targetY,
			targetZ: targetZ);

	public static TimelinePlayerAction FacilityPlaceBlueprint(
		string facilityDefId,
		int targetX,
		int targetY,
		int targetZ,
		FacilityRotation rotation) =>
		new(
			TimelinePlayerActionType.FacilityPlaceBlueprint,
			targetX: targetX,
			targetY: targetY,
			targetZ: targetZ,
			facilityDefId: facilityDefId,
			facilityRotation: rotation);

	public static TimelinePlayerAction FacilityDemolish(string facilityId) =>
		new(TimelinePlayerActionType.FacilityDemolish, facilityId: facilityId);

	public static TimelinePlayerAction FacilityDeliver(string facilityId) =>
		new(TimelinePlayerActionType.FacilityDeliver, facilityId: facilityId);

	public static TimelinePlayerAction FacilityConstruct(string facilityId) =>
		new(TimelinePlayerActionType.FacilityConstruct, facilityId: facilityId);

	public static TimelinePlayerAction Climb(int dz) =>
		new(TimelinePlayerActionType.Climb, dz: dz);
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
	public string ActorId { get; set; } = "";
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

	private enum PlayerActionOutcome
	{
		Failed,
		AppliedWithoutTurnCost,
		ConsumedTurn,
	}

	/// <summary>
	/// 单步性能剖析计数器。由 <see cref="EnableStepProfiling"/> 启用，
	/// <see cref="AdvanceAutoSingleStep"/> 中各阶段累加 tick 数。
	/// 仅用于性能测试，生产路径零开销（通过字段判零跳过）。
	/// </summary>
	public struct StepProfilingStats
	{
		public int Steps;
		public long EnsureCurrentTicks;
		public long HealthDeathTicks;
		public long AiDispatchTicks;
		public long FinalizeTicks;
		public long FinalizeCooldownTicks;
		public long FinalizeGravityTicks;
		public long FinalizeConsumeTurnTicks;
		public long FinalizeProbeTicks;
	}

	private static bool _stepProfilingEnabled;
	private static StepProfilingStats _stepProfilingStats;

	public static void EnableStepProfiling()
	{
		_stepProfilingEnabled = true;
		_stepProfilingStats = default;
	}

	public static StepProfilingStats DisableStepProfilingAndRead()
	{
		_stepProfilingEnabled = false;
		var snapshot = _stepProfilingStats;
		_stepProfilingStats = default;
		return snapshot;
	}

	public static double TicksToMs(long ticks) =>
		ticks * 1000d / System.Diagnostics.Stopwatch.Frequency;

	private sealed class TimelineEvalContext
	{
		public Dictionary<string, Actor?> ActorsById { get; } = new(StringComparer.Ordinal);
		public Dictionary<string, float> SpeedByActorId { get; } = new(StringComparer.Ordinal);
		public bool ActorsSynced { get; set; }
		/// <summary>
		/// 当 AdvanceCharges 推进时间时收集的世界事件。为 null 表示不执行世界系统（查询场景）。
		/// </summary>
		public List<GameEvent>? PendingWorldEvents { get; set; }
	}

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

	[ThreadStatic] private static HashSet<string>? _syncLiveIds;
	[ThreadStatic] private static HashSet<string>? _syncExistingIds;

	public static void SyncActors(GameState state)
	{
		var liveActorIds = _syncLiveIds ??= new HashSet<string>(StringComparer.Ordinal);
		liveActorIds.Clear();
		foreach (var actor in state.Actors.Values)
		{
			if (CanParticipateInTimeline(actor))
				liveActorIds.Add(actor.Id);
		}

		state.Timeline.Actors.RemoveAll(entry => !liveActorIds.Contains(entry.ActorId));

		var existingActorIds = _syncExistingIds ??= new HashSet<string>(StringComparer.Ordinal);
		existingActorIds.Clear();
		foreach (var entry in state.Timeline.Actors)
			existingActorIds.Add(entry.ActorId);

		foreach (var actorId in liveActorIds)
		{
			if (!existingActorIds.Contains(actorId))
				state.Timeline.Actors.Add(new TimelineActorState { ActorId = actorId });
		}

		if (state.Timeline.CurrentActorId != null && !liveActorIds.Contains(state.Timeline.CurrentActorId))
			state.Timeline.CurrentActorId = null;
		if (state.Timeline.LastActorId != null && !liveActorIds.Contains(state.Timeline.LastActorId))
			state.Timeline.LastActorId = null;
	}

	public static bool IsPlayerTurn(GameState state)
	{
		var context = new TimelineEvalContext();
		return IsPlayerTurn(state, context);
	}

	private static bool IsPlayerTurn(GameState state, TimelineEvalContext context)
	{
		var actor = EnsureCurrentActor(state, context);
		if (actor == null || !IsPlayerControllable(actor))
			return false;

		// 激活角色的回合 = 玩家回合
		return string.Equals(actor.Id, PartyModule.GetActiveId(state), StringComparison.Ordinal);
	}

	/// <summary>
	/// 轻量级玩家回合探测：仅查 Timeline 数据判断下一个 ready actor 是否为玩家，
	/// 不执行 HealthSync，避免 FinalizeConsumedAction 中的重复开销。
	/// 当无 actor 达到阈值时，调用 AdvanceCharges 推进时间。
	/// 同时设置 CurrentActorId，使下次 EnsureCurrentActor 直接命中。
	/// </summary>
	private static bool ProbeIsPlayerTurnNext(GameState state, TimelineEvalContext context)
	{
		// 如果 CurrentActorId 已设置（由 EnsureCurrentActor 设定），直接查
		if (state.Timeline.CurrentActorId != null)
		{
			var current = ResolveActor(state, state.Timeline.CurrentActorId, context);
			if (current != null && CanParticipateInTimeline(current) && IsPlayerControllable(current))
				return string.Equals(current.Id, PartyModule.GetActiveId(state), StringComparison.Ordinal);
			return false;
		}

		// CurrentActorId 被清除（ConsumeTurn 后）：用 PickReadyActor 轻量查找
		var ready = PickReadyActor(state, context);
		if (ready == null)
		{
			// 无 actor 达到阈值 — 推进 charge 直到有人 ready。
			// 世界事件会写入 context.PendingWorldEvents，由 FinalizeConsumedAction 排出。
			AdvanceCharges(state, context);
			ready = PickReadyActor(state, context);
		}
		if (ready == null)
			return false;

		// 预设 CurrentActorId，省去下一次 EnsureCurrentActor 的重复 PickReady + AdvanceCharges。
		state.Timeline.CurrentActorId = ready.Id;

		if (!IsPlayerControllable(ready))
			return false;

		return string.Equals(ready.Id, PartyModule.GetActiveId(state), StringComparison.Ordinal);
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

	public static TimelineDebugSnapshot CreateDebugSnapshot(
		GameState state,
		bool playerDead,
		bool? watchModeEnabled = null,
		bool includeEntries = true)
	{
		var context = new TimelineEvalContext();
		SyncActors(state);
		context.ActorsSynced = true;
		var resolvedWatchMode = watchModeEnabled ?? false;

		var currentActor = playerDead
			? TryGetActiveActor(state, state.Timeline.CurrentActorId, context)
			: EnsureCurrentActor(state, context);
		var lastActor = TryGetActiveActor(state, state.Timeline.LastActorId, context);
		var isPlayerTurn = !playerDead
			&& currentActor != null
			&& string.Equals(currentActor.Id, PartyModule.GetActiveId(state), StringComparison.Ordinal)
			&& IsPlayerControllable(currentActor);
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
			Entries = includeEntries
				? BuildDebugEntries(state, currentActor, context)
				: [],
		};
	}

	public static TimelineStepResult SubmitPlayerAction(GameState state, TimelinePlayerAction action)
	{
		var context = new TimelineEvalContext();
		context.PendingWorldEvents = new List<GameEvent>();
		var result = new TimelineStepResult();
		var actor = EnsureCurrentActor(state, context);

		// 排出 AdvanceCharges 产生的世界事件
		if (context.PendingWorldEvents is { Count: > 0 })
		{
			result.Events.AddRange(context.PendingWorldEvents);
			context.PendingWorldEvents.Clear();
		}

		if (actor == null
			|| !string.Equals(actor.Id, PartyModule.GetActiveId(state), StringComparison.Ordinal)
			|| !IsPlayerControllable(actor))
		{
			result.PlayerTurnReady = false;
			result.HasPendingAutoStep = actor != null;
			return result;
		}

		if (TryHandleHealthDeath(state, actor, result.Events))
		{
			result.ActingActorId = actor.Id;
			FinalizeActorRemoval(state, actor.Id, watchModeEnabled: false, result, context);
			return result;
		}

		result.ActingActorId = actor.Id;
		var outcome = TryExecutePlayerAction(state, actor, action, result.Events);
		if (outcome == PlayerActionOutcome.Failed)
		{
			result.PlayerTurnReady = true;
			return result;
		}

		if (outcome == PlayerActionOutcome.AppliedWithoutTurnCost)
		{
			result.PlayerTurnReady = true;
			result.HasPendingAutoStep = false;
			return result;
		}

		FinalizeConsumedAction(state, actor.Id, watchModeEnabled: false, result, context);
		return result;
	}

	public static TimelineStepResult AdvanceAuto(GameState state, bool watchModeEnabled, bool fastTurnModeEnabled)
	{
		var context = new TimelineEvalContext();
		context.PendingWorldEvents = new List<GameEvent>();
		var aggregate = new TimelineStepResult();
		var remainingSteps = fastTurnModeEnabled && !watchModeEnabled ? 64 : 1;

		// 快照缓存跨 AdvanceAuto 调用复用，避免每步重建 O(N) 快照。
		// IsDirty 标记由 ActorModule.Add/Remove 设置，RefreshFromActors 负责位置/死亡更新。
		state.SnapshotCache ??= new TimelineSnapshotCache();

		// 感知缓存：首次 AI 步骤批量构建所有敌人感知，后续步骤直接复用。
		// 每步之间只有一个 actor 移动，感知最多陈旧 1 步，对游戏 AI 可接受。
		state.PerceptionCache ??= new Dictionary<string, Perception>(StringComparer.Ordinal);
		state.AwarenessContextCache ??= AwarenessModule.CreateTurnContext(state);
		state.BehaviorContextCache ??= new AIBehaviorContext(state);

		while (remainingSteps-- > 0)
		{
			var step = AdvanceAutoSingleStep(state, watchModeEnabled, context);
			aggregate.Events.AddRange(step.Events);
			aggregate.ActingActorId = step.ActingActorId;
			aggregate.ActionConsumed |= step.ActionConsumed;
			aggregate.PlayerTurnReady = step.PlayerTurnReady;
			aggregate.HasPendingAutoStep = step.HasPendingAutoStep;
			if (watchModeEnabled || step.PlayerTurnReady || !step.HasPendingAutoStep)
				break;
		}

		return aggregate;
	}

	private static TimelineStepResult AdvanceAutoSingleStep(GameState state, bool watchModeEnabled, TimelineEvalContext context)
	{
		var profile = _stepProfilingEnabled;
		var stageStart = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;

		var result = new TimelineStepResult();
		var actor = EnsureCurrentActor(state, context);

		if (profile)
		{
			var now = System.Diagnostics.Stopwatch.GetTimestamp();
			_stepProfilingStats.EnsureCurrentTicks += now - stageStart;
			stageStart = now;
		}

		// 排出 AdvanceCharges 产生的世界事件（天气、刷怪等）
		if (context.PendingWorldEvents is { Count: > 0 })
		{
			result.Events.AddRange(context.PendingWorldEvents);
			context.PendingWorldEvents.Clear();
		}

		if (actor == null)
		{
			result.PlayerTurnReady = false;
			result.HasPendingAutoStep = false;
			return result;
		}

		if (TryHandleHealthDeath(state, actor, result.Events))
		{
			result.ActingActorId = actor.Id;
			FinalizeActorRemoval(state, actor.Id, watchModeEnabled, result, context);
			if (profile)
				_stepProfilingStats.HealthDeathTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stageStart;
			return result;
		}

		var isActivePartyMember = string.Equals(actor.Id, PartyModule.GetActiveId(state), StringComparison.Ordinal);
		if (isActivePartyMember && !watchModeEnabled && IsPlayerControllable(actor))
		{
			result.PlayerTurnReady = true;
			return result;
		}

		result.ActingActorId = actor.Id;
		if (isActivePartyMember)
		{
			actor.BrainId ??= "simple";
		}

		if (profile)
		{
			var now = System.Diagnostics.Stopwatch.GetTimestamp();
			_stepProfilingStats.HealthDeathTicks += now - stageStart;
			stageStart = now;
		}

		var execution = AIDispatcher.DecideAndExecuteAnyResult(state, actor, tickBuffs: false);
		result.Events.AddRange(execution.Events);

		if (profile)
		{
			var now = System.Diagnostics.Stopwatch.GetTimestamp();
			_stepProfilingStats.AiDispatchTicks += now - stageStart;
			stageStart = now;
		}

		if (!execution.Consumed)
		{
			result.PlayerTurnReady = isActivePartyMember && !watchModeEnabled;
			result.HasPendingAutoStep = !result.PlayerTurnReady;
			return result;
		}

		FinalizeConsumedAction(state, actor.Id, watchModeEnabled, result, context);

		if (profile)
		{
			_stepProfilingStats.FinalizeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stageStart;
			_stepProfilingStats.Steps++;
		}

		return result;
	}

	private static PlayerActionOutcome TryExecutePlayerAction(
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
			case TimelinePlayerActionType.TerrainBuild:
				return TryExecuteTerrainBuild(state, player, action);
			case TimelinePlayerActionType.TerrainDemolish:
				return TryExecuteTerrainDemolish(state, player, action);
			case TimelinePlayerActionType.FacilityPlaceBlueprint:
				return TryExecuteFacilityPlaceBlueprint(state, player, action);
			case TimelinePlayerActionType.FacilityDemolish:
				return TryExecuteFacilityDemolish(state, player, action);
			case TimelinePlayerActionType.FacilityDeliver:
				return TryExecuteFacilityDeliver(state, player, action, events);
			case TimelinePlayerActionType.FacilityConstruct:
				return TryExecuteFacilityConstruct(state, player, action, events);
			case TimelinePlayerActionType.Climb:
				return TryExecuteClimb(state, player, action, events);
			default:
				return PlayerActionOutcome.Failed;
		}
	}

	private static PlayerActionOutcome TryExecuteMove(
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
				return PlayerActionOutcome.Failed;

			events.AddRange(attackEvents);
			return PlayerActionOutcome.ConsumedTurn;
		}

		events.AddRange(ActionModule.TryMove(state, player, action.Dx, action.Dy));
		return PlayerActionOutcome.ConsumedTurn;
	}

	private static PlayerActionOutcome TryExecuteDig(
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

	private static PlayerActionOutcome TryExecuteAttack(
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

	private static PlayerActionOutcome TryExecuteCastSkill(
		GameState state,
		Actor player,
		TimelinePlayerAction action,
		List<GameEvent> events)
	{
		if (string.IsNullOrEmpty(action.SkillId))
			return PlayerActionOutcome.Failed;

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
		return result.Consumed ? PlayerActionOutcome.ConsumedTurn : PlayerActionOutcome.Failed;
	}

	private static PlayerActionOutcome TryExecuteEat(
		GameState state,
		Actor player,
		TimelinePlayerAction action,
		List<GameEvent> events)
	{
		var result = NeedActionModule.TryConsumeFood(state, player, action.InventoryIndex);
		events.AddRange(result.Events);
		if (result.Consumed)
			return PlayerActionOutcome.ConsumedTurn;

		var drinkResult = NeedActionModule.TryConsumeDrink(state, player, action.InventoryIndex);
		events.AddRange(drinkResult.Events);
		return drinkResult.Consumed ? PlayerActionOutcome.ConsumedTurn : PlayerActionOutcome.Failed;
	}

	private static PlayerActionOutcome TryExecuteRest(
		GameState state,
		Actor player,
		List<GameEvent> events)
	{
		if (AI.ThreatDetection.HasNearbyThreat(state, player))
		{
			NeedSystem.ApplyThought(player, "sleep_interrupted", state.Turn, NeedThoughtSources.Sleep, events, state);
			return PlayerActionOutcome.ConsumedTurn;
		}

		var result = NeedActionModule.TryRest(
			state,
			player,
			RestContext.ForPlayerBedroll(NeedActionModule.GetBedrollQuality(player)));
		events.AddRange(result.Events);
		return result.Consumed ? PlayerActionOutcome.ConsumedTurn : PlayerActionOutcome.Failed;
	}

	private static PlayerActionOutcome TryExecuteTerrainBuild(
		GameState state,
		Actor player,
		TimelinePlayerAction action)
	{
		if (string.IsNullOrWhiteSpace(action.TerrainId))
			return PlayerActionOutcome.Failed;

		return RuntimeBuildActionModule.TryExecuteTerrainBuild(
			state,
			player,
			action.TerrainId,
			action.TargetX,
			action.TargetY,
			action.TargetZ,
			state.RuntimeFreeBuild)
			? PlayerActionOutcome.AppliedWithoutTurnCost
			: PlayerActionOutcome.Failed;
	}

	private static PlayerActionOutcome TryExecuteTerrainDemolish(
		GameState state,
		Actor player,
		TimelinePlayerAction action) =>
		RuntimeBuildActionModule.TryExecuteTerrainDemolish(
			state,
			player,
			action.TargetX,
			action.TargetY,
			action.TargetZ,
			state.RuntimeFreeBuild)
			? PlayerActionOutcome.AppliedWithoutTurnCost
			: PlayerActionOutcome.Failed;

	private static PlayerActionOutcome TryExecuteFacilityPlaceBlueprint(
		GameState state,
		Actor player,
		TimelinePlayerAction action)
	{
		if (string.IsNullOrWhiteSpace(action.FacilityDefId))
			return PlayerActionOutcome.Failed;

		return RuntimeBuildActionModule.TryExecuteFacilityPlaceBlueprint(
			state,
			player,
			action.FacilityDefId,
			action.TargetX,
			action.TargetY,
			action.TargetZ,
			action.FacilityRotation,
			state.RuntimeFreeBuild)
			? PlayerActionOutcome.AppliedWithoutTurnCost
			: PlayerActionOutcome.Failed;
	}

	private static PlayerActionOutcome TryExecuteFacilityDemolish(
		GameState state,
		Actor player,
		TimelinePlayerAction action)
	{
		if (string.IsNullOrWhiteSpace(action.FacilityId))
			return PlayerActionOutcome.Failed;

		return RuntimeBuildActionModule.TryExecuteFacilityDemolish(
			state,
			player,
			action.FacilityId,
			state.RuntimeFreeBuild)
			? PlayerActionOutcome.AppliedWithoutTurnCost
			: PlayerActionOutcome.Failed;
	}

	private static PlayerActionOutcome TryExecuteFacilityDeliver(
		GameState state,
		Actor player,
		TimelinePlayerAction action,
		List<GameEvent> events)
	{
		if (string.IsNullOrWhiteSpace(action.FacilityId))
			return PlayerActionOutcome.Failed;

		var result = FacilityConstructionModule.TryDeliverMaterials(state, player, action.FacilityId);
		return result.Consumed ? PlayerActionOutcome.ConsumedTurn : PlayerActionOutcome.Failed;
	}

	private static PlayerActionOutcome TryExecuteFacilityConstruct(
		GameState state,
		Actor player,
		TimelinePlayerAction action,
		List<GameEvent> events)
	{
		if (string.IsNullOrWhiteSpace(action.FacilityId))
			return PlayerActionOutcome.Failed;

		var result = FacilityConstructionModule.TryConstructFacility(state, player, action.FacilityId);
		return result.Consumed ? PlayerActionOutcome.ConsumedTurn : PlayerActionOutcome.Failed;
	}

	private static PlayerActionOutcome TryExecuteClimb(
		GameState state,
		Actor player,
		TimelinePlayerAction action,
		List<GameEvent> events)
	{
		if (state.World == null || action.Dz == 0)
			return PlayerActionOutcome.Failed;

		var world = state.World;
		var x = player.X;
		var y = player.Y;
		var z = player.Z;
		var targetZ = z + action.Dz;

		if (ClimbingService.CanAutoClimb(world, x, y, z, action.Dz))
		{
			player.TickBuffs();
			ClimbingService.MoveActorVertical(state, player, action.Dz);
			events.Add(MovementEventFactory.CreateActorClimbed(player, x, y, z, targetZ, action.Dz));
			return PlayerActionOutcome.ConsumedTurn;
		}

		if (ClimbingService.CanAttemptClimb(world, x, y, z, action.Dz))
		{
			player.TickBuffs();
			var difficulty = ClimbingService.GetClimbDifficulty(world, x, y, z);
			if (ClimbingService.RollClimbCheck(player, difficulty))
			{
				ClimbingService.MoveActorVertical(state, player, action.Dz);
				events.Add(MovementEventFactory.CreateActorClimbed(player, x, y, z, targetZ, action.Dz));
			}
			else
			{
				events.Add(new GameEvent("climb_failed")
				{
					InitiatorId = player.Id,
				});
			}
			return PlayerActionOutcome.ConsumedTurn;
		}

		return PlayerActionOutcome.Failed;
	}

	private static void FinalizeConsumedAction(
		GameState state,
		string actorId,
		bool watchModeEnabled,
		TimelineStepResult result,
		TimelineEvalContext context)
	{
		var profile = _stepProfilingEnabled;
		var t = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;

		result.ActionConsumed = true;
		ResolveActor(state, actorId, context)?.TickSkillCooldowns();
		ClimbingService.ApplyPlayerGravityAndDamage(state, result.Events);

		if (profile)
		{
			var now = System.Diagnostics.Stopwatch.GetTimestamp();
			_stepProfilingStats.FinalizeCooldownTicks += now - t;
			t = now;
		}

		state.Turn++;
		ConsumeTurn(state, actorId, context);
		context.ActorsById.Clear();
		context.SpeedByActorId.Clear();

		if (profile)
		{
			var now = System.Diagnostics.Stopwatch.GetTimestamp();
			_stepProfilingStats.FinalizeConsumeTurnTicks += now - t;
			t = now;
		}

		// 用轻量探测代替完整 EnsureCurrentActor，避免重复 HealthSync + PickReady
		result.PlayerTurnReady = ProbeIsPlayerTurnNext(state, context);
		// 排出探测中 AdvanceCharges 产生的世界事件
		if (context.PendingWorldEvents is { Count: > 0 })
		{
			result.Events.AddRange(context.PendingWorldEvents);
			context.PendingWorldEvents.Clear();
		}
		result.HasPendingAutoStep = watchModeEnabled || !result.PlayerTurnReady;

		if (profile)
			_stepProfilingStats.FinalizeProbeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t;
	}

	private static void ConsumeTurn(GameState state, string actorId, TimelineEvalContext context)
	{
		var entry = FindEntry(state, actorId);
		if (entry != null)
			entry.Charge = Math.Max(0f, entry.Charge - ActionThreshold);

		state.Timeline.CurrentActorId = null;
		state.Timeline.LastActorId = actorId;
		SyncActors(state);
		context.ActorsSynced = true;
	}

	private static Actor? EnsureCurrentActor(GameState state, TimelineEvalContext context)
	{
		if (!context.ActorsSynced)
			SyncActors(state);
		context.ActorsSynced = false;

		if (state.Timeline.CurrentActorId != null)
		{
			var current = ResolveActor(state, state.Timeline.CurrentActorId, context);
			if (current != null && CanParticipateInTimeline(current))
			{
				HealthSystem.Sync(current, state.Turn, DefaultEnvironmentExposureProvider.Instance.Capture(state, current));
				context.SpeedByActorId.Remove(current.Id);
				if (CanParticipateInTimeline(current))
					return current;
			}

			state.Timeline.CurrentActorId = null;
		}

		var ready = PickReadyActor(state, context);
		string? firstReadyId = null;
		if (ready != null)
		{
			firstReadyId = ready.Id;
			HealthSystem.Sync(ready, state.Turn, DefaultEnvironmentExposureProvider.Instance.Capture(state, ready));
			context.SpeedByActorId.Remove(ready.Id);
			if (IsActorActive(ready) || HealthSystem.GetFatalCause(ready) != null)
			{
				state.Timeline.CurrentActorId = ready.Id;
				return ready;
			}
		}

		AdvanceCharges(state, context);
		ready = PickReadyActor(state, context);
		if (ready != null)
		{
			if (!string.Equals(ready.Id, firstReadyId, StringComparison.Ordinal))
			{
				HealthSystem.Sync(ready, state.Turn, DefaultEnvironmentExposureProvider.Instance.Capture(state, ready));
				context.SpeedByActorId.Remove(ready.Id);
			}
			if (!(IsActorActive(ready) || HealthSystem.GetFatalCause(ready) != null))
				ready = null;
		}
		state.Timeline.CurrentActorId = ready?.Id;
		return ready;
	}

	private static Actor? PickReadyActor(GameState state, TimelineEvalContext context)
	{
		Actor? bestActor = null;
		float bestCharge = float.MinValue;
		float bestSpeed = float.MinValue;
		var bestWasLast = 1;
		string? bestId = null;

		foreach (var entry in state.Timeline.Actors)
		{
			if (entry.Charge < ActionThreshold)
				continue;

			var actor = ResolveActor(state, entry.ActorId, context);
			if (actor == null || !CanParticipateInTimeline(actor))
				continue;

			var speed = ResolveSpeed(state, actor, context);
			var wasLast = string.Equals(entry.ActorId, state.Timeline.LastActorId, StringComparison.Ordinal) ? 1 : 0;

			if (bestActor == null
				|| entry.Charge > bestCharge
				|| (entry.Charge == bestCharge && speed > bestSpeed)
				|| (entry.Charge == bestCharge && speed == bestSpeed && wasLast < bestWasLast)
				|| (entry.Charge == bestCharge && speed == bestSpeed && wasLast == bestWasLast && string.CompareOrdinal(entry.ActorId, bestId) < 0))
			{
				bestActor = actor;
				bestCharge = entry.Charge;
				bestSpeed = speed;
				bestWasLast = wasLast;
				bestId = entry.ActorId;
			}
		}

		return bestActor;
	}

	[ThreadStatic] private static List<(TimelineActorState Entry, float Speed)>? _chargeCandidates;

	private static void AdvanceCharges(GameState state, TimelineEvalContext context)
	{
		var candidates = _chargeCandidates ??= new List<(TimelineActorState Entry, float Speed)>();
		candidates.Clear();
		float minDelta = float.MaxValue;

		foreach (var entry in state.Timeline.Actors)
		{
			var actor = ResolveActor(state, entry.ActorId, context);
			if (actor == null || !CanParticipateInTimeline(actor))
				continue;

			var speed = ResolveSpeed(state, actor, context);
			if (speed <= 0f)
				continue;

			candidates.Add((entry, speed));
			var remaining = Math.Max(0f, ActionThreshold - entry.Charge);
			var delta = remaining / speed;
			if (delta < minDelta)
				minDelta = delta;
		}

		if (candidates.Count == 0)
			return;
		if (!float.IsFinite(minDelta) || minDelta <= 0f)
			return;

		foreach (var candidate in candidates)
			candidate.Entry.Charge += candidate.Speed * minDelta;

		// 时间实际流逝：推进世界系统（巢穴刷怪、天气、火灾、故事、农业）。
		// 仅在执行上下文（PendingWorldEvents != null）中触发，查询场景跳过。
		if (context.PendingWorldEvents != null)
			context.PendingWorldEvents.AddRange(TurnModule.AdvanceWorldSystems(state));
	}

	private static TimelineActorState? FindEntry(GameState state, string actorId) =>
		state.Timeline.Actors.FirstOrDefault(entry => string.Equals(entry.ActorId, actorId, StringComparison.Ordinal));

	private static List<TimelineDebugEntry> BuildDebugEntries(GameState state, Actor? currentActor, TimelineEvalContext context)
	{
		var entries = new List<TimelineDebugEntry>(capacity: Math.Min(4, state.Timeline.Actors.Count));
		foreach (var entry in state.Timeline.Actors)
		{
			var actor = ResolveActor(state, entry.ActorId, context);
			if (actor == null || !CanParticipateInTimeline(actor))
				continue;

			var isCurrent = currentActor != null && string.Equals(actor.Id, currentActor.Id, StringComparison.Ordinal);
			var speed = ResolveSpeed(state, actor, context);
			var eta = isCurrent ? 0f : CalculateEtaToAct(entry.Charge, speed);
			entries.Add(new TimelineDebugEntry
			{
				ActorId = actor.Id,
				ActorName = GetActorDisplayName(actor),
				IsPlayer = PartyModule.IsPartyMember(state, actor.Id),
				IsCurrent = isCurrent,
				IsLast = string.Equals(actor.Id, state.Timeline.LastActorId, StringComparison.Ordinal),
				Charge = entry.Charge,
				Speed = speed,
				EtaToAct = eta,
			});
		}

		entries.Sort(static (left, right) => CompareDebugEntries(left, right));
		if (entries.Count > 4)
			entries.RemoveRange(4, entries.Count - 4);
		return entries;
	}

	private static int CompareDebugEntries(TimelineDebugEntry left, TimelineDebugEntry right)
	{
		var currentCompare = right.IsCurrent.CompareTo(left.IsCurrent);
		if (currentCompare != 0)
			return currentCompare;

		var etaCompare = left.EtaToAct.CompareTo(right.EtaToAct);
		if (etaCompare != 0)
			return etaCompare;

		var speedCompare = right.Speed.CompareTo(left.Speed);
		if (speedCompare != 0)
			return speedCompare;

		return string.CompareOrdinal(left.ActorId, right.ActorId);
	}

	private static Actor? ResolveActor(GameState state, string actorId, TimelineEvalContext context)
	{
		if (context.ActorsById.TryGetValue(actorId, out var cachedActor))
			return cachedActor;

		var actor = ActorModule.GetById(state, actorId);
		context.ActorsById[actorId] = actor;
		return actor;
	}

	private static float ResolveSpeed(GameState state, Actor actor, TimelineEvalContext context)
	{
		if (context.SpeedByActorId.TryGetValue(actor.Id, out var cachedSpeed))
			return cachedSpeed;

		var speed = CalculateSpeed(state, actor);
		context.SpeedByActorId[actor.Id] = speed;
		return speed;
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

	private static Actor? TryGetActiveActor(GameState state, string? actorId, TimelineEvalContext context)
	{
		if (string.IsNullOrEmpty(actorId))
			return null;

		var actor = ResolveActor(state, actorId, context);
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

	private static void FinalizeActorRemoval(
		GameState state,
		string actorId,
		bool watchModeEnabled,
		TimelineStepResult result,
		TimelineEvalContext context)
	{
		result.ActionConsumed = true;
		state.Timeline.CurrentActorId = null;
		state.Timeline.LastActorId = actorId;
		SyncActors(state);
		context.ActorsSynced = true;
		context.ActorsById.Clear();
		context.SpeedByActorId.Clear();
		// Actor 死亡移除后，缓存的感知可能引用已移除的 actor，需要清空重建。
		state.PerceptionCache?.Clear();
		result.PlayerTurnReady = ProbeIsPlayerTurnNext(state, context);
		result.HasPendingAutoStep = watchModeEnabled || !result.PlayerTurnReady;
	}

	private static InteractionDef? FindInteraction(string? actionDefId)
	{
		if (string.IsNullOrEmpty(actionDefId))
			return null;

		return InteractionDefs.All.FirstOrDefault(def => string.Equals(def.Id, actionDefId, StringComparison.Ordinal));
	}
}
