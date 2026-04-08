using MiniRPG.Core;
using MiniRPG.Core.Data;
using MiniRPG.Core.Needs;
using Xunit;

namespace MiniRPG.Tests;

public class MentalBreakModuleTests
{
	private static GameState CreateState(int turn = 100)
	{
		var state = new GameState { PlayerId = "player" };
		state.Turn = turn;
		state.RngSeed = 42;
		var player = new Actor
		{
			Id = "player",
			X = 5,
			Y = 5,
			MoodValue = 50f,
		};
		player.Limbs.Add(new Limb { Id = "torso", Durability = 10, MaxDurability = 10 });
		player.Inventory.Add(new Item { Id = "bread", Name = "面包", Category = "food" });
		state.Actors["player"] = player;
		return state;
	}

	[Fact]
	public void Check_HighMood_NoBreak()
	{
		var state = CreateState();
		var actor = state.Actors["player"];
		actor.MoodValue = 60f;

		var events = MentalBreakModule.Check(state, actor);
		Assert.Empty(events);
		Assert.Null(actor.MentalBreak);
	}

	[Fact]
	public void Check_LowMood_CanTriggerBreak()
	{
		var state = CreateState();
		var actor = state.Actors["player"];
		actor.MoodValue = 3f; // 极低 mood

		// 多次尝试（概率性），至少有一次应该触发
		var triggered = false;
		for (var seed = 0; seed < 100; seed++)
		{
			state.RngSeed = seed;
			actor.MentalBreak = null;
			actor.LastMentalBreakTurn = 0;

			var events = MentalBreakModule.Check(state, actor);
			if (events.Count > 0)
			{
				triggered = true;
				Assert.Equal("mental_break", events[0].Type);
				Assert.NotNull(actor.MentalBreak);
				break;
			}
		}

		Assert.True(triggered, "极低 mood 应该能触发精神崩溃");
	}

	[Fact]
	public void Check_AlreadyInBreak_DoesNotTriggerNew()
	{
		var state = CreateState();
		var actor = state.Actors["player"];
		actor.MoodValue = 3f;
		actor.MentalBreak = new MentalBreakState
		{
			Type = MentalBreakType.Catatonic,
			StartTurn = state.Turn,
			Duration = 20,
		};

		var events = MentalBreakModule.Check(state, actor);
		// 不应该触发新的崩溃，只是继续当前崩溃
		Assert.Empty(events);
		Assert.NotNull(actor.MentalBreak);
	}

	[Fact]
	public void Check_BreakExpires_ClearsState()
	{
		var state = CreateState(100);
		var actor = state.Actors["player"];
		actor.MoodValue = 10f;
		actor.MentalBreak = new MentalBreakState
		{
			Type = MentalBreakType.Catatonic,
			StartTurn = 80,
			Duration = 15,
		};

		// Turn 100, started at 80, duration 15 → 已过期
		var events = MentalBreakModule.Check(state, actor);
		Assert.Contains(events, e => e.Type == "mental_break_end");
		Assert.Null(actor.MentalBreak);
		// mood 应该回升
		Assert.True(actor.MoodValue > 10f);
	}

	[Fact]
	public void Check_CooldownPreventsRepeat()
	{
		var state = CreateState(100);
		var actor = state.Actors["player"];
		actor.MoodValue = 3f;
		actor.LastMentalBreakTurn = 90; // 10 回合前刚崩溃过

		var events = MentalBreakModule.Check(state, actor);
		Assert.Empty(events);
	}

	[Fact]
	public void TryExecuteBreakBehavior_NoBreak_ReturnsFalse()
	{
		var state = CreateState();
		var actor = state.Actors["player"];

		var result = MentalBreakModule.TryExecuteBreakBehavior(state, actor, out var events);
		Assert.False(result);
		Assert.Empty(events);
	}

	[Fact]
	public void TryExecuteBreakBehavior_Catatonic_DoesNothing()
	{
		var state = CreateState();
		var actor = state.Actors["player"];
		actor.MentalBreak = new MentalBreakState
		{
			Type = MentalBreakType.Catatonic,
			StartTurn = state.Turn,
			Duration = 10,
		};

		var origX = actor.X;
		var origY = actor.Y;
		var result = MentalBreakModule.TryExecuteBreakBehavior(state, actor, out var events);
		Assert.True(result);
		Assert.Equal(origX, actor.X);
		Assert.Equal(origY, actor.Y);
	}

	[Fact]
	public void TryExecuteBreakBehavior_BingeEating_ConsumesFood()
	{
		var state = CreateState();
		var actor = state.Actors["player"];
		actor.MentalBreak = new MentalBreakState
		{
			Type = MentalBreakType.BingeEating,
			StartTurn = state.Turn,
			Duration = 10,
		};
		actor.Needs = new() { ["hunger"] = new NeedState { Current = 80f } };

		var foodCount = actor.Inventory.Count;
		var result = MentalBreakModule.TryExecuteBreakBehavior(state, actor, out var events);
		Assert.True(result);
		Assert.True(actor.Inventory.Count < foodCount);
		Assert.Contains(events, e => e.Type == "mental_break_binge");
	}

	[Fact]
	public void TryExecuteBreakBehavior_Berserk_AttacksNearby()
	{
		var state = CreateState();
		var actor = state.Actors["player"];
		actor.MentalBreak = new MentalBreakState
		{
			Type = MentalBreakType.Berserk,
			StartTurn = state.Turn,
			Duration = 10,
		};

		var npc = new Actor { Id = "npc", X = 5, Y = 6 };
		npc.Limbs.Add(new Limb { Id = "torso", Durability = 10, MaxDurability = 10 });
		state.Actors["npc"] = npc;

		var result = MentalBreakModule.TryExecuteBreakBehavior(state, actor, out var events);
		Assert.True(result);
		Assert.Contains(events, e => e.Type == "mental_break_attack");
		Assert.True(npc.Limbs[0].Durability < 10);
	}

	[Fact]
	public void IsInBreak_ReturnsCorrectly()
	{
		var actor = new Actor { Id = "test" };
		Assert.False(MentalBreakModule.IsInBreak(actor, 100));

		actor.MentalBreak = new MentalBreakState
		{
			Type = MentalBreakType.Flee,
			StartTurn = 90,
			Duration = 15,
		};
		Assert.True(MentalBreakModule.IsInBreak(actor, 100));
		Assert.False(MentalBreakModule.IsInBreak(actor, 110));
	}
}
