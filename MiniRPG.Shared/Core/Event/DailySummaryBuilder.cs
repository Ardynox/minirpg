using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Calendar;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Event;

/// <summary>
/// 从当前 <see cref="GameState"/> 构建一份"每日总结"数据，供 UI / 日志层展示。
/// </summary>
/// <remarks>
/// 设计意图：
/// "每日总结屏"是让玩家感到"世界在活"的最直接载体——见 <c>Docs/产品愿景.md</c> 第 2 条体验目标。
/// 当前版本只给截面数据（最近发生的 incident + 活着成员 + 威胁等级 + 当前回合），
/// 不做"过去 N 回合内 mood 变化了多少 / 谁受伤了"的精细 diff——那需要把 <see cref="GameEvent"/>
/// 持久化到一个事件流，属于更大的 Memory / GameEventLog 批次。
///
/// 触发器（"什么时候展示这份总结"）暂不实现，等 <c>Core/Calendar</c>（B1）引入"新一天开始"信号后再接。
/// </remarks>
public static class DailySummaryBuilder
{
	public sealed class DailySummary
	{
		public int CurrentTurn { get; init; }
		public int? DayIndex { get; init; }
		public Season? Season { get; init; }
		public int? Year { get; init; }
		public float ThreatLevel { get; init; }
		public List<IncidentRecord> RecentIncidents { get; init; } = [];
		public List<PartyMemberSnapshot> PartyMembers { get; init; } = [];
		public int DeadMemberCount { get; init; }
		public int LivingMemberCount { get; init; }

		public bool HasSignificantContent =>
			RecentIncidents.Count > 0 || DeadMemberCount > 0 || ThreatLevel > 20f;
	}

	public readonly record struct PartyMemberSnapshot(
		string ActorId,
		string DisplayName,
		bool IsActiveFocus,
		bool IsDead,
		float MoodValue,
		string? TopNegativeThoughtId,
		string? TopPositiveThoughtId);

	/// <summary>
	/// 构建当前"每日总结"。若提供 <paramref name="incidentLookbackTurns"/>，只返回该窗口内的事件；
	/// 默认 200 回合作为"最近"的保守窗口。
	/// </summary>
	public static DailySummary Build(
		GameState state,
		int? incidentLookbackTurns = null,
		int? dayIndex = null)
	{
		var lookback = incidentLookbackTurns ?? 200;
		var cutoffTurn = state.Turn - lookback;
		var recent = state.StorytellerState.History
			.Where(record => record.Turn >= cutoffTurn)
			.OrderByDescending(record => record.Turn)
			.ToList();

		var snapshots = new List<PartyMemberSnapshot>();
		var activeId = PartyModule.GetActiveId(state);
		var dead = 0;
		var alive = 0;
		foreach (var memberId in state.Party.MemberIds)
		{
			var actor = ActorModule.GetById(state, memberId);
			if (actor == null)
				continue;

			var isDead = Combat.CombatModule.IsDead(actor);
			if (isDead)
				dead++;
			else
				alive++;

			var thoughts = actor.Thoughts ?? [];
			var topNegative = thoughts
				.Where(t => t.MoodOffset < 0f)
				.OrderBy(t => t.MoodOffset)
				.FirstOrDefault();
			var topPositive = thoughts
				.Where(t => t.MoodOffset > 0f)
				.OrderByDescending(t => t.MoodOffset)
				.FirstOrDefault();

			snapshots.Add(new PartyMemberSnapshot(
				ActorId: actor.Id,
				DisplayName: string.IsNullOrWhiteSpace(actor.DisplayName) ? actor.Id : actor.DisplayName,
				IsActiveFocus: string.Equals(actor.Id, activeId, System.StringComparison.Ordinal),
				IsDead: isDead,
				MoodValue: actor.MoodValue,
				TopNegativeThoughtId: topNegative?.Id,
				TopPositiveThoughtId: topPositive?.Id));
		}

		var calendarView = CalendarService.View(state);

		return new DailySummary
		{
			CurrentTurn = state.Turn,
			DayIndex = dayIndex ?? calendarView.DayIndex,
			Season = calendarView.Season,
			Year = calendarView.Year,
			ThreatLevel = state.StorytellerState.ThreatLevel,
			RecentIncidents = recent,
			PartyMembers = snapshots,
			DeadMemberCount = dead,
			LivingMemberCount = alive,
		};
	}
}
