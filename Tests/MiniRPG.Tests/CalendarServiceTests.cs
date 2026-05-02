using MiniRPG.Core.Calendar;
using MiniRPG.Core.Data;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 验证 CalendarService 把 GameState.Turn 翻译成 (Year/Season/Day/Hour) 的纯函数边界，
/// 以及"是不是新一天 / 新一季"的判定语义——这是 Storyteller / Farm
/// 等跨系统消费方的唯一事实源，任何错位都会让"季节加权 incident / 农作物开花"
/// 这一类时间相关玩法整批跑偏。
/// </summary>
public class CalendarServiceTests
{
	[Fact]
	public void View_Turn0_ReturnsYearZeroSpringDayZeroHourZero()
	{
		var view = CalendarService.View(turn: 0);

		Assert.Equal(0, view.Year);
		Assert.Equal(Season.Spring, view.Season);
		Assert.Equal(0, view.DayIndex);
		Assert.Equal(0, view.HourOfDay);
		Assert.Equal(0, view.Turn);
	}

	[Fact]
	public void View_AdvancesDayEveryTurnsPerDay()
	{
		Assert.Equal(0, CalendarService.View(turn: DayNightCycle.TurnsPerDay - 1).DayIndex);
		Assert.Equal(1, CalendarService.View(turn: DayNightCycle.TurnsPerDay).DayIndex);
		Assert.Equal(2, CalendarService.View(turn: DayNightCycle.TurnsPerDay * 2).DayIndex);
		Assert.Equal(10, CalendarService.View(turn: DayNightCycle.TurnsPerDay * 10).DayIndex);
	}

	[Fact]
	public void View_HourOfDay_LinearWithinDay()
	{
		var perHour = DayNightCycle.TurnsPerDay / 24f; // 当前 240/24 = 10 turn/hour（旧历法 120/24 = 5）
		Assert.Equal(0, CalendarService.View(0).HourOfDay);
		Assert.Equal(0, CalendarService.View(1).HourOfDay);
		Assert.Equal(1, CalendarService.View((int)perHour).HourOfDay);
		Assert.Equal(12, CalendarService.View(DayNightCycle.TurnsPerDay / 2).HourOfDay);
		Assert.Equal(23, CalendarService.View(DayNightCycle.TurnsPerDay - 1).HourOfDay);
		// 跨天后 hour 重置到 0。
		Assert.Equal(0, CalendarService.View(DayNightCycle.TurnsPerDay).HourOfDay);
	}

	[Fact]
	public void View_SeasonIndex_CyclesThroughFour()
	{
		Assert.Equal(Season.Spring, CalendarService.View(turn: 0).Season);
		Assert.Equal(Season.Summer, CalendarService.View(turn: DayNightCycle.TurnsPerSeason).Season);
		Assert.Equal(Season.Autumn, CalendarService.View(turn: DayNightCycle.TurnsPerSeason * 2).Season);
		Assert.Equal(Season.Winter, CalendarService.View(turn: DayNightCycle.TurnsPerSeason * 3).Season);
		// 第 4 季过完回到 Spring 但 Year 推进到 1。
		Assert.Equal(Season.Spring, CalendarService.View(turn: DayNightCycle.TurnsPerSeason * 4).Season);
		Assert.Equal(1, CalendarService.View(turn: DayNightCycle.TurnsPerSeason * 4).Year);
	}

	[Fact]
	public void IsStartOfNewDay_OnlyTrueWhenCrossingDayBoundary()
	{
		// 0→1：同 day。
		Assert.False(CalendarService.IsStartOfNewDay(0, 1));
		// 同 day 内任意推进。
		Assert.False(CalendarService.IsStartOfNewDay(1, DayNightCycle.TurnsPerDay - 1));
		// 跨过第 0 天结尾 → 第 1 天起点。
		Assert.True(CalendarService.IsStartOfNewDay(DayNightCycle.TurnsPerDay - 1, DayNightCycle.TurnsPerDay));
		// 跨多个 day。
		Assert.True(CalendarService.IsStartOfNewDay(DayNightCycle.TurnsPerDay - 1, DayNightCycle.TurnsPerDay * 3));
		// 倒退 / 同位置不算 new day。
		Assert.False(CalendarService.IsStartOfNewDay(DayNightCycle.TurnsPerDay, DayNightCycle.TurnsPerDay));
		Assert.False(CalendarService.IsStartOfNewDay(DayNightCycle.TurnsPerDay, DayNightCycle.TurnsPerDay - 1));
	}

	[Fact]
	public void IsStartOfNewSeason_OnlyTrueWhenCrossingSeasonBoundary()
	{
		Assert.False(CalendarService.IsStartOfNewSeason(0, 1));
		Assert.True(CalendarService.IsStartOfNewSeason(DayNightCycle.TurnsPerSeason - 1, DayNightCycle.TurnsPerSeason));
		// 跨年（Winter→Spring）也算 new season。
		Assert.True(CalendarService.IsStartOfNewSeason(
			DayNightCycle.TurnsPerSeason * 4 - 1,
			DayNightCycle.TurnsPerSeason * 4));
		// 同位置 / 倒退不算。
		Assert.False(CalendarService.IsStartOfNewSeason(DayNightCycle.TurnsPerSeason, DayNightCycle.TurnsPerSeason));
		Assert.False(CalendarService.IsStartOfNewSeason(DayNightCycle.TurnsPerSeason + 5, DayNightCycle.TurnsPerSeason));
	}

	[Fact]
	public void CurrentSeason_FromGameState_MatchesViewSeason()
	{
		var state = new GameState { PlayerId = "p", Turn = DayNightCycle.TurnsPerSeason * 2 + 5 };
		Assert.Equal(Season.Autumn, CalendarService.CurrentSeason(state));
	}

	[Fact]
	public void CurrentDayIndex_FromGameState_MatchesViewDayIndex()
	{
		var state = new GameState { PlayerId = "p", Turn = DayNightCycle.TurnsPerDay * 7 + 30 };
		Assert.Equal(7, CalendarService.CurrentDayIndex(state));
	}

	[Fact]
	public void View_LargeTurn_ProducesCorrectYearAndSeason()
	{
		// TurnsPerSeason*40 = 40 季 = 10 年（4 季/年）。
		var view = CalendarService.View(turn: DayNightCycle.TurnsPerSeason * 40);
		Assert.Equal(10, view.Year);
		Assert.Equal(Season.Spring, view.Season);
	}
}
