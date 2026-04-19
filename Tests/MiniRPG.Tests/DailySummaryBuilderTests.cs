using System.Linq;
using MiniRPG.Core.Calendar;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Event;
using MiniRPG.Core.Needs;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 验证 DailySummaryBuilder.Build 输出包含的所有字段在不同场景下正确。
/// 这是"每日总结屏"——《产品愿景》第 2 条体验目标的核心载体——的展示数据合约：
/// 任何字段缺失或错位都会让玩家感觉"世界不在活"。
/// </summary>
public class DailySummaryBuilderTests
{
	[Fact]
	public void Build_EmptyParty_ReturnsZeroCounts()
	{
		var state = new GameState { PlayerId = "x", Turn = 0 };
		state.Party.MemberIds.Clear();

		var summary = DailySummaryBuilder.Build(state);

		Assert.Equal(0, summary.LivingMemberCount);
		Assert.Equal(0, summary.DeadMemberCount);
		Assert.Empty(summary.PartyMembers);
		Assert.Empty(summary.RecentIncidents);
		Assert.Equal(0, summary.CurrentTurn);
	}

	[Fact]
	public void Build_PartyWithLivingAndDead_CountsCorrectly()
	{
		var state = NewStateWithParty(("alpha", true), ("beta", true), ("gamma", false));

		var summary = DailySummaryBuilder.Build(state);

		Assert.Equal(2, summary.LivingMemberCount);
		Assert.Equal(1, summary.DeadMemberCount);
		Assert.Equal(3, summary.PartyMembers.Count);
		var dead = summary.PartyMembers.Single(m => m.ActorId == "gamma");
		Assert.True(dead.IsDead);
	}

	[Fact]
	public void Build_FlagsActiveFocusMember()
	{
		var state = NewStateWithParty(("alpha", true), ("beta", true));
		state.Party.ActiveId = "beta";

		var summary = DailySummaryBuilder.Build(state);

		var alpha = summary.PartyMembers.Single(m => m.ActorId == "alpha");
		var beta = summary.PartyMembers.Single(m => m.ActorId == "beta");
		Assert.False(alpha.IsActiveFocus);
		Assert.True(beta.IsActiveFocus);
	}

	[Fact]
	public void Build_PicksTopNegativeAndPositiveThoughtPerMember()
	{
		var state = NewStateWithParty(("alpha", true));
		var alpha = state.Actors["alpha"];
		alpha.Thoughts.Add(new ThoughtState
		{
			Id = "small_pleasure", Source = "test", MoodOffset = 1f, ExpiresOnTurn = -1,
		});
		alpha.Thoughts.Add(new ThoughtState
		{
			Id = "big_pleasure", Source = "test", MoodOffset = 8f, ExpiresOnTurn = -1,
		});
		alpha.Thoughts.Add(new ThoughtState
		{
			Id = "minor_pain", Source = "test", MoodOffset = -2f, ExpiresOnTurn = -1,
		});
		alpha.Thoughts.Add(new ThoughtState
		{
			Id = "big_pain", Source = "test", MoodOffset = -7f, ExpiresOnTurn = -1,
		});

		var summary = DailySummaryBuilder.Build(state);
		var snap = summary.PartyMembers.Single();

		Assert.Equal("big_pain", snap.TopNegativeThoughtId);
		Assert.Equal("big_pleasure", snap.TopPositiveThoughtId);
	}

	[Fact]
	public void Build_RecentIncidents_FiltersByLookbackWindow()
	{
		var state = new GameState { PlayerId = "p", Turn = 1000 };
		state.Party.MemberIds.Add("p");
		state.Actors["p"] = MakeLivingActor("p");

		state.StorytellerState.History.Add(new IncidentRecord
		{
			DefId = "ancient_event", Turn = 100, Category = IncidentCategory.Threat,
		});
		state.StorytellerState.History.Add(new IncidentRecord
		{
			DefId = "recent_event", Turn = 850, Category = IncidentCategory.Positive,
		});
		state.StorytellerState.History.Add(new IncidentRecord
		{
			DefId = "very_recent", Turn = 999, Category = IncidentCategory.Neutral,
		});

		// 默认 lookback=200：cutoff=Turn-200=800；ancient_event(100) 排除，recent_event(850) + very_recent(999) 进入。
		var summary = DailySummaryBuilder.Build(state);

		Assert.Equal(2, summary.RecentIncidents.Count);
		Assert.Equal("very_recent", summary.RecentIncidents[0].DefId);
		Assert.Equal("recent_event", summary.RecentIncidents[1].DefId);
		Assert.DoesNotContain(summary.RecentIncidents, r => r.DefId == "ancient_event");

		// 自定义 lookback=10：只 very_recent 进入。
		var tighter = DailySummaryBuilder.Build(state, incidentLookbackTurns: 10);
		Assert.Single(tighter.RecentIncidents);
		Assert.Equal("very_recent", tighter.RecentIncidents[0].DefId);
	}

	[Fact]
	public void Build_PopulatesCalendarFieldsFromCurrentTurn()
	{
		var state = new GameState { PlayerId = "p", Turn = DayNightCycle.TurnsPerSeason * 5 + 7 };
		state.Party.MemberIds.Add("p");
		state.Actors["p"] = MakeLivingActor("p");

		var summary = DailySummaryBuilder.Build(state);
		var view = CalendarService.View(state);

		Assert.Equal(view.DayIndex, summary.DayIndex);
		Assert.Equal(view.Season, summary.Season);
		Assert.Equal(view.Year, summary.Year);
		Assert.Equal(state.Turn, summary.CurrentTurn);
	}

	[Fact]
	public void Build_HasSignificantContent_TriggersByThreatLevel()
	{
		var state = new GameState { PlayerId = "p", Turn = 0 };
		state.Party.MemberIds.Add("p");
		state.Actors["p"] = MakeLivingActor("p");

		Assert.False(DailySummaryBuilder.Build(state).HasSignificantContent);

		state.StorytellerState.ThreatLevel = 25f;
		Assert.True(DailySummaryBuilder.Build(state).HasSignificantContent,
			"ThreatLevel >20 应该让 HasSignificantContent=true。");
	}

	[Fact]
	public void Build_HasSignificantContent_TriggersByDeadMember()
	{
		var state = NewStateWithParty(("alpha", true), ("beta", false));

		Assert.True(DailySummaryBuilder.Build(state).HasSignificantContent,
			"队伍里有死者就应该展示总结屏。");
	}

	[Fact]
	public void Build_DayIndexOverride_TakesPrecedenceOverCalendar()
	{
		var state = new GameState { PlayerId = "p", Turn = 0 };
		state.Party.MemberIds.Add("p");
		state.Actors["p"] = MakeLivingActor("p");

		var summary = DailySummaryBuilder.Build(state, dayIndex: 42);

		Assert.Equal(42, summary.DayIndex);
	}

	private static GameState NewStateWithParty(params (string Id, bool Alive)[] members)
	{
		var state = new GameState { PlayerId = members[0].Id };
		foreach (var (id, alive) in members)
		{
			var actor = MakeLivingActor(id);
			if (!alive)
				actor.Limbs.Clear();
			actor.InvalidateCapacityCache();
			state.Actors[id] = actor;
			state.Party.MemberIds.Add(id);
		}
		state.Party.ActiveId = members[0].Id;
		return state;
	}

	private static Actor MakeLivingActor(string id) => new()
	{
		Id = id,
		DisplayName = id,
		Faction = Factions.Player,
		Limbs =
		{
			new Limb
			{
				Id = $"{id}_torso",
				Name = "torso",
				BodyPart = BodyParts.Torso,
				Capacities =
				{
					["blood_circulation"] = 1.0f,
					["consciousness"] = 1.0f,
				},
				MaxDurability = 10,
				Durability = 10,
				Tags = { [CombatModule.VitalTag] = 1 },
			},
		},
	};
}
