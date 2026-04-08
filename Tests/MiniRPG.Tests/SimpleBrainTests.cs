using System;
using MiniRPG.Core.AI;
using MiniRPG.Core.Data;
using Xunit;

namespace MiniRPG.Tests;

public sealed class SimpleBrainTests
{
	[Fact]
	public void Decide_PrefersRangedAttack_WhenTargetIsShootable()
	{
		var (state, player, enemy) = SkillCastingTestHelper.CreateCombatState(enemyX: 4, enemyY: 1);
		player.BrainId = "simple";

		var perception = PerceptionBuilder.Build(state, player, SimDetail.Full);
		var decision = new SimpleBrain().Decide(perception, new Random(7));
		var skill = InteractionDefs.Get(decision.ActionDefId!);

		Assert.Equal(DecisionType.Attack, decision.Type);
		Assert.Equal(enemy.Id, decision.TargetActorId);
		Assert.NotNull(skill);
		Assert.Equal("ranged_attack", skill.EffectType);
	}

	[Fact]
	public void Decide_MovesCloser_WhenAllCombatSkillsAreOutOfRange()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState(enemyX: 6, enemyY: 1);
		player.BrainId = "simple";
		player.StartSkillCooldown("bow_shoot_heavy", 99);

		var perception = PerceptionBuilder.Build(state, player, SimDetail.Full);
		var decision = new SimpleBrain().Decide(perception, new Random(11));

		Assert.Equal(DecisionType.MoveTo, decision.Type);
		Assert.True(decision.TargetPos.HasValue);
		Assert.Equal((2, 1), decision.TargetPos.Value);
		Assert.Null(decision.ActionDefId);
	}
}
