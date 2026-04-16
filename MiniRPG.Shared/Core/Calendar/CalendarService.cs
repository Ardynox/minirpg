using MiniRPG.Module.Render;

namespace MiniRPG.Core.Calendar;

/// <summary>
/// 把 <see cref="GameState.Turn"/> 翻译成玩家能看懂的"年 / 季 / 日 / 时"。
/// </summary>
/// <remarks>
/// 目的是：
/// <list type="bullet">
/// <item>给 UI / Storyteller / 每日总结 / Dialog / Farm 这些跨系统调用点一个**统一入口**，避免 `state.Turn / TurnsPerDay` 这种计算散落各处。</item>
/// <item>让"是不是新一天"这种边界判定有唯一事实源（见 <see cref="IsStartOfNewDay"/>）。</item>
/// </list>
///
/// 关于数值：
/// <list type="bullet">
/// <item>"一天多少回合"沿用 <see cref="DayNightCycle.TurnsPerDay"/>（当前 120）。</item>
/// <item>"一季多少回合"沿用 <see cref="DayNightCycle.TurnsPerSeason"/>（当前 100）。注意这个比一天还短——设计意图是"季节在同一天内也可能有过渡"；本服务尊重这一设置而不强行对齐。</item>
/// <item>"一年多少季"固定 4（Spring / Summer / Autumn / Winter）。</item>
/// </list>
/// </remarks>
public static class CalendarService
{
	public const int SeasonsPerYear = 4;

	/// <summary>基于 <see cref="GameState.Turn"/> 产出当前日历视图。</summary>
	public static CalendarView View(GameState state) => View(state.Turn);

	public static CalendarView View(int turn)
	{
		var dayIndex = turn / DayNightCycle.TurnsPerDay;
		var turnInDay = turn % DayNightCycle.TurnsPerDay;
		var hourOfDay = (int)(turnInDay * 24f / DayNightCycle.TurnsPerDay);
		var seasonIndex = (turn / DayNightCycle.TurnsPerSeason) % SeasonsPerYear;
		var year = turn / (DayNightCycle.TurnsPerSeason * SeasonsPerYear);
		return new CalendarView(
			Turn: turn,
			Year: year,
			Season: (Season)seasonIndex,
			DayIndex: dayIndex,
			HourOfDay: hourOfDay);
	}

	/// <summary>
	/// 判断在 <paramref name="previousTurn"/> → <paramref name="currentTurn"/> 这一段推进中是否跨过"新的一天"的起点。
	/// 调用约定：在 TurnModule.AdvanceWorld 结束后，拿前后两个 turn 调一次本方法。
	/// </summary>
	public static bool IsStartOfNewDay(int previousTurn, int currentTurn)
	{
		if (currentTurn <= previousTurn)
			return false;

		var prevDay = previousTurn / DayNightCycle.TurnsPerDay;
		var currentDay = currentTurn / DayNightCycle.TurnsPerDay;
		return currentDay > prevDay;
	}

	/// <summary>季节切换（前后两个 turn 跨过季节边界）。</summary>
	public static bool IsStartOfNewSeason(int previousTurn, int currentTurn)
	{
		if (currentTurn <= previousTurn)
			return false;

		var prevSeason = previousTurn / DayNightCycle.TurnsPerSeason;
		var currentSeason = currentTurn / DayNightCycle.TurnsPerSeason;
		return currentSeason > prevSeason;
	}

	/// <summary>当前季节（已归一到 0..3）。</summary>
	public static Season CurrentSeason(GameState state) => View(state).Season;

	/// <summary>当前天索引（从第 0 天起算）。</summary>
	public static int CurrentDayIndex(GameState state) => View(state).DayIndex;
}

public enum Season
{
	Spring,
	Summer,
	Autumn,
	Winter,
}

/// <summary>当前日历的只读快照。</summary>
public readonly record struct CalendarView(
	int Turn,
	int Year,
	Season Season,
	int DayIndex,
	int HourOfDay);
