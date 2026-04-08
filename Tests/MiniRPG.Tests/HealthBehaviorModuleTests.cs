using MiniRPG.Core.AI;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using Xunit;

namespace MiniRPG.Tests;

public sealed class HealthBehaviorModuleTests
{
	public HealthBehaviorModuleTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	[Fact]
	public void TryExecute_HealthyIdleActor_ReturnsNotConsumed()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState();
		player.AwarenessState = AwarenessState.Idle;
		var perception = new Perception { Self = player };
		var result = HealthBehaviorModule.TryExecute(state, player, perception, tickBuffs: false);
		Assert.False(result.Consumed);
	}

	[Fact]
	public void TryExecute_AlertedActor_ReturnsNotConsumed()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState();
		player.AwarenessState = AwarenessState.Alerted;
		var perception = new Perception { Self = player };
		var result = HealthBehaviorModule.TryExecute(state, player, perception, tickBuffs: false);
		Assert.False(result.Consumed);
	}

	[Fact]
	public void TryExecute_WithBehaviorContext_DoesNotThrow()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState();
		player.AwarenessState = AwarenessState.Idle;
		var perception = new Perception { Self = player };
		var ctx = new AIBehaviorContext(state);
		var result = HealthBehaviorModule.TryExecute(state, player, perception, tickBuffs: false, behaviorContext: ctx);
		Assert.NotNull(result);
	}
}
