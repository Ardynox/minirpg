using System;
using System.Collections.Generic;
using MiniRPG.Core.Calendar;
using MiniRPG.Core.Data;
using MiniRPG.Core.Events;
using MiniRPG.Core.Health;

namespace MiniRPG.Core.Combat;

/// <summary>
/// 一个 <see cref="IGameEventConsequenceHandler"/>：滚动保存最近 N 回合内每个 actor 身上发生的关键事件，
/// 在 <c>actor_killed</c> 触发时构建一份 <see cref="DeathReport"/>，让玩家死后能回看"为什么死的"。
/// </summary>
/// <remarks>
/// <para>设计意图（见 <c>Docs/产品愿景.md</c> "结果可追溯"）：
/// 把已有的 <c>combat_attack</c> / <c>injury_applied</c> / <c>food_consumed</c> 事件流"沉淀"出一份
/// 死亡复盘——不需要新事件、不需要改 Actor 字段、不动 Save/Load。</para>
///
/// <para>实现要点：
/// <list type="bullet">
/// <item>actor 死亡后会被 <c>ActorModule.Remove</c> 从 <c>GameState.Actors</c> 删除，
///   所以 BuildReport **不能**从 state 拿 final actor 状态——只能用本 recorder 已经记录的事件流。</item>
/// <item>每个 actor 的事件用 <see cref="System.Collections.Generic.LinkedList{T}"/> FIFO 截断到 <see cref="MaxEventsPerActor"/>（默认 64）；
///   按 <see cref="MaxRetentionTurns"/>（默认 30）窗口在 actor_killed 时再做一次时间过滤。</item>
/// <item>不维护 actor 间的关联关系；同一事件如果同时影响多个 actor（例如同 Z 8 格内的 actor_killed casualty 旁观），
///   只为 ev.TargetId 落账——casualty rumor / memory 由 <c>RumorBus</c> / <c>ActorMemoryModule</c> 各自处理。</item>
/// </list></para>
/// </remarks>
public sealed class DeathReportRecorder : IGameEventConsequenceHandler
{
	public const int MaxEventsPerActor = 64;
	public const int MaxRetentionTurns = 30;

	private readonly Dictionary<string, LinkedList<RecentEventEntry>> _byActor =
		new(StringComparer.Ordinal);

	private readonly Dictionary<string, DeathReport> _completed =
		new(StringComparer.Ordinal);

	/// <summary>清空所有缓存的事件流和已构建的 report；session 切换时调一次。</summary>
	public void Reset()
	{
		_byActor.Clear();
		_completed.Clear();
	}

	/// <summary>当前缓存了多少 actor 的事件流（debug / 测试用）。</summary>
	public int TrackedActorCount => _byActor.Count;

	public void OnEvent(GameState state, GameEvent ev)
	{
		if (ev == null) return;

		switch (ev.Type)
		{
			case "combat_attack":
			case "injury_applied":
			case "limb_destroyed":
			case "food_consumed":
			case "food_rejected":
			case "actor_incapacitated":
				if (!string.IsNullOrWhiteSpace(ev.TargetId))
					Append(ev.TargetId, state.Turn, ev);
				break;

			case "actor_killed":
				if (!string.IsNullOrWhiteSpace(ev.TargetId))
				{
					Append(ev.TargetId, state.Turn, ev);
					_completed[ev.TargetId] = BuildReport(state, ev.TargetId, state.Turn);
				}
				break;

			case "death_blood_loss":
			case "death_infection":
				if (!string.IsNullOrWhiteSpace(ev.TargetId))
					Append(ev.TargetId, state.Turn, ev);
				break;
		}
	}

	/// <summary>
	/// 显式为某 actor 构建死亡报告。如果 OnEvent 已经在 actor_killed 时缓存了一份，直接返回那一份。
	/// </summary>
	public DeathReport? GetOrBuildReport(GameState state, string actorId, int currentTurn)
	{
		if (string.IsNullOrWhiteSpace(actorId))
			return null;
		if (_completed.TryGetValue(actorId, out var cached))
			return cached;
		if (!_byActor.ContainsKey(actorId))
			return null;
		return BuildReport(state, actorId, currentTurn);
	}

	private void Append(string actorId, int currentTurn, GameEvent ev)
	{
		if (!_byActor.TryGetValue(actorId, out var list))
		{
			list = new LinkedList<RecentEventEntry>();
			_byActor[actorId] = list;
		}

		list.AddLast(new RecentEventEntry
		{
			Turn = currentTurn,
			EventType = ev.Type,
			InitiatorActorName = ev.InitiatorActorName,
			LimbName = ev.LimbName,
			ActionName = ev.ActionName,
			Damage = ev.Damage,
			EffectType = ev.EffectType,
		});

		while (list.Count > MaxEventsPerActor)
			list.RemoveFirst();
	}

	private DeathReport BuildReport(GameState state, string actorId, int deathTurn)
	{
		var entries = ExtractRecentEvents(actorId, deathTurn);
		var (cause, killer) = InferCause(entries);
		var actorName = ResolveActorName(state, actorId, entries, killer);

		return new DeathReport
		{
			ActorId = actorId,
			ActorName = actorName,
			DeathTurn = deathTurn,
			DeathCalendarView = CalendarService.View(deathTurn),
			DeathCause = cause,
			KillerName = killer,
			RecentEvents = entries,
		};
	}

	private List<RecentEventEntry> ExtractRecentEvents(string actorId, int deathTurn)
	{
		if (!_byActor.TryGetValue(actorId, out var list))
			return [];

		var min = deathTurn - MaxRetentionTurns;
		var filtered = new List<RecentEventEntry>(list.Count);
		foreach (var entry in list)
		{
			if (entry.Turn >= min)
				filtered.Add(entry);
		}
		return filtered;
	}

	private static (string cause, string? killer) InferCause(IReadOnlyList<RecentEventEntry> entries)
	{
		// 倒序找最有解释力的 final 事件。优先级：
		//   actor_killed (有 InitiatorActorName) > death_blood_loss > death_infection
		//   > 最近一次 food_rejected/food_consumed=spoiled+rotten + food_poisoning condition
		//   > 最近一次 combat_attack 致死 > actor_incapacitated > unknown
		string? killer = null;
		string cause = "unknown";

		for (var i = entries.Count - 1; i >= 0; i--)
		{
			var e = entries[i];
			switch (e.EventType)
			{
				case "actor_killed":
					if (!string.IsNullOrWhiteSpace(e.InitiatorActorName))
					{
						killer = e.InitiatorActorName;
						cause = "killed_by";
					}
					else if (cause == "unknown")
					{
						cause = "killed";
					}
					break;
				case "death_blood_loss":
					if (cause == "unknown") cause = "blood_loss";
					break;
				case "death_infection":
					if (cause == "unknown") cause = "infection";
					break;
				case "actor_incapacitated":
					if (cause == "unknown") cause = "incapacitated";
					break;
			}
		}

		// food_poisoning 推断：在 30 回合内有 food_consumed=spoiled/rotten 或 food_rejected=rotten + injury_applied(food_poisoning)
		if (cause == "unknown" || cause == "killed")
		{
			var sawPoisonFood = false;
			var sawPoisonInjury = false;
			foreach (var e in entries)
			{
				if (e.EventType is "food_consumed" or "food_rejected"
					&& (e.EffectType is "spoiled" or "rotten"))
					sawPoisonFood = true;
				if (e.EventType == "injury_applied"
					&& string.Equals(e.ActionName, HealthConditionIds.FoodPoisoning, StringComparison.Ordinal))
					sawPoisonInjury = true;
			}
			if (sawPoisonFood && sawPoisonInjury)
				cause = "food_poisoning";
		}

		return (cause, killer);
	}

	private static string ResolveActorName(GameState state, string actorId, IReadOnlyList<RecentEventEntry> entries, string? killerHint)
	{
		// actor 通常已经被 ActorModule.Remove；fallback 顺序：
		// 1) state 还能查到（incapacitated 路径）→ DisplayName
		// 2) 最近一条事件里携带的 ActorId 解析失败时返回 actorId
		var actor = ActorModule.GetById(state, actorId);
		if (actor != null && !string.IsNullOrWhiteSpace(actor.DisplayName))
			return actor.DisplayName;
		// killerHint 不能用作受害者名字，跳过
		_ = killerHint;
		return actorId;
	}
}
