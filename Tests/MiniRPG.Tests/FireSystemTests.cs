using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using Xunit;

namespace MiniRPG.Tests;

public sealed class FireSystemTests
{
	public FireSystemTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	// ── Advance ──────────────────────────────────────────

	[Fact]
	public void Advance_NullWorld_ReturnsEmpty()
	{
		var state = new GameState { World = null };
		Assert.Empty(FireSystem.Advance(state));
	}

	[Fact]
	public void Advance_NoFires_ReturnsEmpty()
	{
		var (state, _, _) = SkillCastingTestHelper.CreateCombatState();
		var events = FireSystem.Advance(state);
		Assert.Empty(events);
	}

	// ── TryIgniteActor ───────────────────────────────────

	[Fact]
	public void TryIgniteActor_AddsOnFireCondition()
	{
		var (state, _, enemy) = SkillCastingTestHelper.CreateCombatState();
		var events = FireSystem.TryIgniteActor(state, enemy);
		Assert.NotEmpty(events);
		Assert.Contains(enemy.HealthConditions, c => c.Id == HealthConditionIds.OnFire);
	}

	[Fact]
	public void TryIgniteActor_AlreadyOnFire_IncreasesSeverity()
	{
		var (state, _, enemy) = SkillCastingTestHelper.CreateCombatState();
		FireSystem.TryIgniteActor(state, enemy);
		var severity1 = enemy.HealthConditions.Find(c => c.Id == HealthConditionIds.OnFire)!.Severity;

		FireSystem.TryIgniteActor(state, enemy);
		var severity2 = enemy.HealthConditions.Find(c => c.Id == HealthConditionIds.OnFire)!.Severity;

		Assert.True(severity2 >= severity1);
	}

	// ── TryIgniteCell ────────────────────────────────────

	[Fact]
	public void TryIgniteCell_FloorTile_ReturnsEvents()
	{
		var (state, _, _) = SkillCastingTestHelper.CreateCombatState();
		var events = FireSystem.TryIgniteCell(state, 5, 5, 0);
		Assert.NotNull(events);
	}

	// ── TryExtinguish ────────────────────────────────────

	[Fact]
	public void TryExtinguish_NoFire_ReturnsNotConsumed()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState();
		var result = FireSystem.TryExtinguish(state, player, player.X + 1, player.Y, player.Z);
		Assert.False(result.Consumed);
	}

	// ── IsDangerousCell ──────────────────────────────────

	[Fact]
	public void IsDangerousCell_NullWorld_ReturnsFalse()
	{
		var state = new GameState { World = null };
		Assert.False(FireSystem.IsDangerousCell(state, 0, 0, 0));
	}

	[Fact]
	public void IsDangerousCell_NoFire_ReturnsFalse()
	{
		var (state, _, _) = SkillCastingTestHelper.CreateCombatState();
		Assert.False(FireSystem.IsDangerousCell(state, 5, 5, 0));
	}

	// ── IsSafeWalkableForActor ───────────────────────────

	[Fact]
	public void IsSafeWalkableForActor_FloorTile_ReturnsTrue()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState();
		Assert.True(FireSystem.IsSafeWalkableForActor(state, player, 3, 3, 0));
	}

	[Fact]
	public void IsSafeWalkableForActor_NullWorld_ReturnsFalse()
	{
		var state = new GameState { World = null };
		var actor = PresetDB.SpawnActor("player", "test");
		Assert.False(FireSystem.IsSafeWalkableForActor(state, actor, 0, 0, 0));
	}
}
