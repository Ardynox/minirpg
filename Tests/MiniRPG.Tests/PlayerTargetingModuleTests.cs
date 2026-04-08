using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Module;
using Xunit;

namespace MiniRPG.Tests;

public sealed class PlayerTargetingModuleTests
{
	[Fact]
	public void Resolve_PreservesLockedTarget_WhenItRemainsValid()
	{
		var (state, _, enemy) = SkillCastingTestHelper.CreateCombatState(enemyX: 2, enemyY: 1);
		enemy.AwarenessState = AwarenessState.Alerted;
		var context = new PlayerTargetingContext(enemy.Id, PlayerTargetSource.Explicit);

		var resolution = PlayerTargetingModule.Resolve(state, context);

		Assert.Equal(enemy.Id, resolution.Target?.Id);
		Assert.Equal(enemy.Id, resolution.Context.CurrentTargetActorId);
		Assert.Equal(PlayerTargetSource.Explicit, resolution.Context.CurrentTargetSource);
	}

	[Fact]
	public void Resolve_FallsBackToAdjacentTarget_WhenLockedTargetBecomesInvalid()
	{
		var (state, _, lockedEnemy) = SkillCastingTestHelper.CreateCombatState(enemyX: 5, enemyY: 1);
		var fallbackEnemy = CreateEnemy("fallback_enemy", 2, 1, 0);
		ActorModule.Add(state, fallbackEnemy);
		var context = new PlayerTargetingContext(lockedEnemy.Id, PlayerTargetSource.Inspect);

		ActorModule.Remove(state, lockedEnemy.Id);

		var resolution = PlayerTargetingModule.Resolve(state, context);

		Assert.Equal(fallbackEnemy.Id, resolution.Target?.Id);
		Assert.Equal(fallbackEnemy.Id, resolution.Context.CurrentTargetActorId);
		Assert.Equal(PlayerTargetSource.Auto, resolution.Context.CurrentTargetSource);
	}

	[Fact]
	public void Resolve_ClearsTarget_WhenNoValidFallbackExists()
	{
		var (state, _, enemy) = SkillCastingTestHelper.CreateCombatState(enemyX: 3, enemyY: 1);
		var context = new PlayerTargetingContext(enemy.Id, PlayerTargetSource.Explicit);

		enemy.Faction = Factions.Friendly;

		var resolution = PlayerTargetingModule.Resolve(state, context);

		Assert.Null(resolution.Target);
		Assert.Null(resolution.Context.CurrentTargetActorId);
		Assert.Equal(PlayerTargetSource.Auto, resolution.Context.CurrentTargetSource);
	}

	[Fact]
	public void ResolveSummaryTarget_FallsBack_WhenLockedTargetIsOutsideHudContext()
	{
		var (state, _, lockedEnemy) = SkillCastingTestHelper.CreateCombatState(enemyX: 6, enemyY: 1);
		lockedEnemy.AwarenessState = AwarenessState.Idle;
		var fallbackEnemy = CreateEnemy("aware_enemy", 3, 1, 0);
		fallbackEnemy.AwarenessState = AwarenessState.Alerted;
		ActorModule.Add(state, fallbackEnemy);
		var context = new PlayerTargetingContext(lockedEnemy.Id, PlayerTargetSource.Inspect);

		var selection = PlayerTargetingModule.ResolveSummaryTarget(state, context, _ => false);

		Assert.Equal(fallbackEnemy.Id, selection.Target?.Id);
		Assert.False(selection.MarkCurrentTarget);
	}

	[Fact]
	public void TryResolveDirectSkillCast_ReturnsAction_WhenCurrentTargetIsCastable()
	{
		var (state, player, enemy) = SkillCastingTestHelper.CreateCombatState(enemyX: 4, enemyY: 1);
		var skill = InteractionDefs.Get("bow_shoot")!;
		var context = new PlayerTargetingContext(enemy.Id, PlayerTargetSource.Explicit);

		var success = PlayerTargetingModule.TryResolveDirectSkillCast(
			state,
			player,
			context,
			skill,
			out var action,
			out var normalizedContext);

		Assert.True(success);
		Assert.NotNull(action);
		Assert.Equal(TimelinePlayerActionType.CastSkill, action!.Type);
		Assert.Equal(enemy.Id, action.TargetActorId);
		Assert.Equal(enemy.Id, normalizedContext.CurrentTargetActorId);
	}

	[Fact]
	public void TryResolveDirectSkillCast_ReturnsFalse_WhenCurrentTargetCannotBeCast()
	{
		var (state, player, enemy) = SkillCastingTestHelper.CreateCombatState(enemyX: 7, enemyY: 1);
		var skill = InteractionDefs.Get("bow_shoot")!;
		var context = new PlayerTargetingContext(enemy.Id, PlayerTargetSource.Explicit);

		var success = PlayerTargetingModule.TryResolveDirectSkillCast(
			state,
			player,
			context,
			skill,
			out var action,
			out _);

		Assert.False(success);
		Assert.Null(action);
	}

	private static Actor CreateEnemy(string id, int x, int y, int z)
	{
		var enemy = PresetDB.SpawnActor("player", "enemy");
		enemy.Id = id;
		enemy.DisplayName = id;
		enemy.X = x;
		enemy.Y = y;
		enemy.Z = z;
		enemy.Faction = Factions.Hostile;
		enemy.BrainId = "simple";
		if (enemy.Limbs.Count > 0)
			enemy.Limbs[0].Tags[CombatModule.VitalTag] = 1;
		return enemy;
	}
}
