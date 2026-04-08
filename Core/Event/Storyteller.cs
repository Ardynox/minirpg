using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Event;

/// <summary>
/// 故事讲述者：RimWorld 风格的事件调度器。
/// 
/// 核心机制：
/// 1. 每隔 CheckInterval 回合检查一次是否应该触发事件
/// 2. 根据当前殖民地状态（财富、人口、威胁历史）计算威胁等级
/// 3. 按权重随机选择一个合适的事件
/// 4. 事件可以立即触发或延迟触发
/// 
/// 调用方式：在 TurnModule.AdvanceWorld() 中调用 Storyteller.Tick()
/// </summary>
public static class Storyteller
{
	/// <summary>检查间隔（回合数）。</summary>
	private const int CheckInterval = 30;

	/// <summary>历史记录最大条数。</summary>
	private const int MaxHistorySize = 50;

	/// <summary>事件定义注册表。</summary>
	private static readonly Dictionary<string, IncidentDef> _defs = new(StringComparer.Ordinal);

	/// <summary>事件执行器注册表。</summary>
	private static readonly Dictionary<string, IIncidentWorker> _workers = new(StringComparer.Ordinal);

	// ── 注册 ──

	public static void RegisterDef(IncidentDef def) => _defs[def.Id] = def;

	public static void RegisterWorker(string id, IIncidentWorker worker) => _workers[id] = worker;

	public static void ClearDefs() => _defs.Clear();

	public static void ClearWorkers() => _workers.Clear();

	public static IncidentDef? GetDef(string id) =>
		_defs.TryGetValue(id, out var def) ? def : null;

	public static IReadOnlyDictionary<string, IncidentDef> AllDefs => _defs;

	// ── 核心调度 ──

	/// <summary>
	/// 每回合调用：处理延迟事件 + 定期检查新事件。
	/// </summary>
	public static List<GameEvent> Tick(GameState state)
	{
		var events = new List<GameEvent>();
		var story = state.StorytellerState;

		// 1. 处理到期的延迟事件
		events.AddRange(ProcessPendingIncidents(state));

		// 2. 定期检查是否触发新事件
		if (state.Turn - story.LastCheckTurn >= CheckInterval)
		{
			story.LastCheckTurn = state.Turn;
			UpdateThreatLevel(state);

			var incident = SelectIncident(state);
			if (incident != null)
				events.AddRange(FireIncident(state, incident, []));
		}

		return events;
	}

	/// <summary>
	/// 强制触发指定事件（调试/脚本用）。
	/// </summary>
	public static List<GameEvent> ForceFireIncident(GameState state, string defId, Dictionary<string, string>? runtimeParams = null)
	{
		if (!_defs.TryGetValue(defId, out var def))
			return [];

		return FireIncident(state, def, runtimeParams ?? []);
	}

	/// <summary>
	/// 安排延迟事件。
	/// </summary>
	public static void ScheduleIncident(GameState state, string defId, int delayTurns, Dictionary<string, string>? runtimeParams = null)
	{
		state.StorytellerState.PendingIncidents.Add(new PendingIncident
		{
			IncidentDefId = defId,
			TriggerTurn = state.Turn + delayTurns,
			Params = runtimeParams ?? [],
		});
	}

	// ── 内部逻辑 ──

	private static List<GameEvent> ProcessPendingIncidents(GameState state)
	{
		var events = new List<GameEvent>();
		var story = state.StorytellerState;

		for (var i = story.PendingIncidents.Count - 1; i >= 0; i--)
		{
			var pending = story.PendingIncidents[i];
			if (state.Turn < pending.TriggerTurn)
				continue;

			story.PendingIncidents.RemoveAt(i);

			if (!_defs.TryGetValue(pending.IncidentDefId, out var def))
				continue;

			events.AddRange(FireIncident(state, def, pending.Params));
		}

		return events;
	}

	private static IncidentDef? SelectIncident(GameState state)
	{
		var rng = new Random(state.RngSeed + state.Turn);
		var story = state.StorytellerState;

		// 根据威胁等级决定事件类别倾向
		var categoryWeights = CalculateCategoryWeights(story.ThreatLevel);

		// 收集所有可触发的事件
		var candidates = new List<(IncidentDef Def, float Weight)>();
		foreach (var def in _defs.Values)
		{
			if (!IsEligible(state, def))
				continue;

			var categoryMultiplier = categoryWeights.TryGetValue(def.Category, out var w) ? w : 1f;
			candidates.Add((def, def.Weight * categoryMultiplier));
		}

		if (candidates.Count == 0)
			return null;

		// 加权随机选择
		var totalWeight = candidates.Sum(static c => c.Weight);
		var roll = (float)(rng.NextDouble() * totalWeight);
		var cumulative = 0f;
		foreach (var (def, weight) in candidates)
		{
			cumulative += weight;
			if (roll <= cumulative)
				return def;
		}

		return candidates[^1].Def;
	}

	private static bool IsEligible(GameState state, IncidentDef def)
	{
		var story = state.StorytellerState;

		// 回合数限制
		if (state.Turn < def.MinTurn)
			return false;

		// 冷却检查
		if (story.LastIncidentTurn.TryGetValue(def.Id, out var lastTurn)
			&& state.Turn - lastTurn < def.CooldownTurns)
		{
			return false;
		}

		// 队伍人数限制
		if (def.MinPartySize > 0 && PartyModule.Count(state) < def.MinPartySize)
			return false;

		// Worker 可行性检查
		if (!string.IsNullOrEmpty(def.WorkerId)
			&& _workers.TryGetValue(def.WorkerId, out var worker)
			&& !worker.CanFire(state, def))
		{
			return false;
		}

		return true;
	}

	private static List<GameEvent> FireIncident(GameState state, IncidentDef def, Dictionary<string, string> runtimeParams)
	{
		var story = state.StorytellerState;

		// 记录触发时间
		story.LastIncidentTurn[def.Id] = state.Turn;

		// 记录历史
		story.History.Add(new IncidentRecord
		{
			DefId = def.Id,
			Turn = state.Turn,
			Category = def.Category,
		});
		if (story.History.Count > MaxHistorySize)
			story.History.RemoveAt(0);

		// 合并参数
		var mergedParams = new Dictionary<string, string>(def.Params, StringComparer.Ordinal);
		foreach (var (key, value) in runtimeParams)
			mergedParams[key] = value;

		// 执行
		if (!string.IsNullOrEmpty(def.WorkerId)
			&& _workers.TryGetValue(def.WorkerId, out var worker))
		{
			return worker.Execute(state, def, mergedParams);
		}

		// 无 Worker 时生成通用事件
		return
		[
			new GameEvent("incident")
			{
				InteractionDefId = def.Id,
				InteractionName = def.Name,
			},
		];
	}

	// ── 威胁等级计算 ──

	private static void UpdateThreatLevel(GameState state)
	{
		var story = state.StorytellerState;

		// 基础威胁 = 回合数 / 100（随时间缓慢增长）
		var baseThreat = Math.Min(50f, state.Turn / 100f);

		// 队伍规模加成
		var partyBonus = PartyModule.Count(state) * 5f;

		// 最近威胁事件的衰减
		var recentThreats = story.History
			.Count(r => r.Category == IncidentCategory.Threat && state.Turn - r.Turn < 100);
		var recentThreatPenalty = recentThreats * 10f; // 最近威胁多 → 降低威胁等级

		story.ThreatLevel = Math.Clamp(baseThreat + partyBonus - recentThreatPenalty, 0f, 100f);
	}

	private static Dictionary<IncidentCategory, float> CalculateCategoryWeights(float threatLevel)
	{
		// 威胁等级高 → 更多威胁事件
		// 威胁等级低 → 更多正面/中性事件
		var threatWeight = 0.2f + threatLevel / 100f * 0.6f; // 0.2 ~ 0.8
		var positiveWeight = 0.8f - threatLevel / 100f * 0.4f; // 0.8 ~ 0.4
		var neutralWeight = 1f; // 始终稳定

		return new Dictionary<IncidentCategory, float>
		{
			[IncidentCategory.Threat] = threatWeight,
			[IncidentCategory.Neutral] = neutralWeight,
			[IncidentCategory.Positive] = positiveWeight,
		};
	}
}
