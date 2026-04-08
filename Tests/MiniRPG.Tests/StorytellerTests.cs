using System.Collections.Generic;
using MiniRPG.Core;
using MiniRPG.Core.Data;
using MiniRPG.Core.Event;
using MiniRPG.Core.Event.Workers;
using Xunit;

namespace MiniRPG.Tests;

public class StorytellerTests
{
	private static GameState CreateState(int turn = 0)
	{
		var state = new GameState { PlayerId = "player" };
		state.Turn = turn;
		state.Actors["player"] = new Actor
		{
			Id = "player",
			X = 10,
			Y = 10,
			Faction = Factions.Player,
		};
		state.Actors["player"].Limbs.Add(new Limb { Id = "torso", Durability = 10, MaxDurability = 10 });
		PartyModule.Initialize(state);
		return state;
	}

	[Fact]
	public void Tick_NoDefsRegistered_ReturnsEmpty()
	{
		Storyteller.ClearDefs();
		var state = CreateState(100);
		var events = Storyteller.Tick(state);
		Assert.Empty(events);
	}

	[Fact]
	public void ForceFireIncident_GeneratesEvent()
	{
		Storyteller.ClearDefs();
		Storyteller.ClearWorkers();

		Storyteller.RegisterDef(new IncidentDef
		{
			Id = "test_event",
			Name = "Test Event",
			Category = IncidentCategory.Neutral,
		});

		var state = CreateState();
		var events = Storyteller.ForceFireIncident(state, "test_event");

		Assert.NotEmpty(events);
		Assert.Equal("incident", events[0].Type);
		Assert.Equal("test_event", events[0].InteractionDefId);
	}

	[Fact]
	public void ForceFireIncident_RecordsHistory()
	{
		Storyteller.ClearDefs();
		Storyteller.ClearWorkers();

		Storyteller.RegisterDef(new IncidentDef
		{
			Id = "history_test",
			Name = "History Test",
		});

		var state = CreateState(50);
		Storyteller.ForceFireIncident(state, "history_test");

		Assert.Single(state.StorytellerState.History);
		Assert.Equal("history_test", state.StorytellerState.History[0].DefId);
		Assert.Equal(50, state.StorytellerState.History[0].Turn);
	}

	[Fact]
	public void ForceFireIncident_SetsCooldown()
	{
		Storyteller.ClearDefs();
		Storyteller.ClearWorkers();

		Storyteller.RegisterDef(new IncidentDef
		{
			Id = "cooldown_test",
			Name = "Cooldown Test",
			CooldownTurns = 100,
		});

		var state = CreateState(10);
		Storyteller.ForceFireIncident(state, "cooldown_test");

		Assert.True(state.StorytellerState.LastIncidentTurn.ContainsKey("cooldown_test"));
		Assert.Equal(10, state.StorytellerState.LastIncidentTurn["cooldown_test"]);
	}

	[Fact]
	public void ScheduleIncident_FiresAtCorrectTurn()
	{
		Storyteller.ClearDefs();
		Storyteller.ClearWorkers();

		Storyteller.RegisterDef(new IncidentDef
		{
			Id = "delayed_event",
			Name = "Delayed Event",
		});

		var state = CreateState(10);
		Storyteller.ScheduleIncident(state, "delayed_event", delayTurns: 5);

		Assert.Single(state.StorytellerState.PendingIncidents);
		Assert.Equal(15, state.StorytellerState.PendingIncidents[0].TriggerTurn);

		// 回合 12：还没到
		state.Turn = 12;
		var events = Storyteller.Tick(state);
		Assert.Empty(events);
		Assert.Single(state.StorytellerState.PendingIncidents);

		// 回合 15：触发
		state.Turn = 15;
		events = Storyteller.Tick(state);
		Assert.NotEmpty(events);
		Assert.Empty(state.StorytellerState.PendingIncidents);
	}

	[Fact]
	public void ForceFireIncident_UnknownDef_ReturnsEmpty()
	{
		Storyteller.ClearDefs();
		var state = CreateState();
		var events = Storyteller.ForceFireIncident(state, "nonexistent");
		Assert.Empty(events);
	}

	[Fact]
	public void StorytellerState_Clone_IsIndependent()
	{
		var original = new StorytellerState
		{
			ThreatLevel = 42f,
			LastCheckTurn = 100,
		};
		original.LastIncidentTurn["raid"] = 50;
		original.History.Add(new IncidentRecord { DefId = "raid", Turn = 50 });
		original.PendingIncidents.Add(new PendingIncident { IncidentDefId = "test", TriggerTurn = 200 });

		var clone = original.Clone();

		clone.ThreatLevel = 0f;
		clone.LastIncidentTurn["raid"] = 999;
		clone.History.Clear();
		clone.PendingIncidents.Clear();

		Assert.Equal(42f, original.ThreatLevel);
		Assert.Equal(50, original.LastIncidentTurn["raid"]);
		Assert.Single(original.History);
		Assert.Single(original.PendingIncidents);
	}
}
