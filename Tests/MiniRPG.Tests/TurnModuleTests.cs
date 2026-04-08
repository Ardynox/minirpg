using MiniRPG.Core.Combat;
using Xunit;

namespace MiniRPG.Tests;

public sealed class TurnModuleTests
{
	public TurnModuleTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	[Fact]
	public void AdvanceWorld_IncrementsTurn()
	{
		var (state, _, _) = SkillCastingTestHelper.CreateCombatState();
		var turnBefore = state.Turn;
		TurnModule.AdvanceWorld(state);
		Assert.Equal(turnBefore + 1, state.Turn);
	}

	[Fact]
	public void AdvanceWorld_ReturnsEvents()
	{
		var (state, _, _) = SkillCastingTestHelper.CreateCombatState();
		var events = TurnModule.AdvanceWorld(state);
		Assert.NotNull(events);
	}

	[Fact]
	public void Tick_IncrementsTurn()
	{
		var (state, _, _) = SkillCastingTestHelper.CreateCombatState();
		var turnBefore = state.Turn;
		TurnModule.Tick(state);
		Assert.Equal(turnBefore + 1, state.Turn);
	}

	[Fact]
	public void Tick_ReturnsEvents()
	{
		var (state, _, _) = SkillCastingTestHelper.CreateCombatState();
		var events = TurnModule.Tick(state);
		Assert.NotNull(events);
	}

	[Fact]
	public void TickProfiled_ReturnsMetrics()
	{
		var (state, _, _) = SkillCastingTestHelper.CreateCombatState();
		var result = TurnModule.TickProfiled(state);
		Assert.NotNull(result.Events);
		Assert.NotNull(result.Metrics);
		Assert.True(result.Metrics.TickMs >= 0);
	}

	[Fact]
	public void AdvanceWorld_MultipleTicks_TurnIncrementsCorrectly()
	{
		var (state, _, _) = SkillCastingTestHelper.CreateCombatState();
		var startTurn = state.Turn;
		for (int i = 0; i < 5; i++)
			TurnModule.AdvanceWorld(state);
		Assert.Equal(startTurn + 5, state.Turn);
	}
}
