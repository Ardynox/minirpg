using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using Xunit;

namespace MiniRPG.Tests;

public sealed class HealthActionModuleTests
{
	public HealthActionModuleTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	[Fact]
	public void TryTendSelf_HealthyActor_ReturnsNotConsumed()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState();
		var result = HealthActionModule.TryTendSelf(state, player);
		Assert.False(result.Consumed);
	}

	[Fact]
	public void TryTendOther_HostileFaction_ReturnsNotConsumed()
	{
		var (state, player, enemy) = SkillCastingTestHelper.CreateCombatState();
		var result = HealthActionModule.TryTendOther(state, player, enemy);
		Assert.False(result.Consumed);
	}

	[Fact]
	public void TryTendOther_NotAdjacent_ReturnsNotConsumed()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState();
		var ally = PresetDB.SpawnActor("player", "ally");
		ally.Faction = player.Faction;
		ally.X = player.X + 5;
		ally.Y = player.Y;
		ally.Z = player.Z;
		ActorModule.Add(state, ally);

		var result = HealthActionModule.TryTendOther(state, player, ally);
		Assert.False(result.Consumed);
	}

	[Fact]
	public void TryTendOther_AdjacentAlly_Healthy_ReturnsNotConsumed()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState();
		var ally = PresetDB.SpawnActor("player", "ally");
		ally.Faction = player.Faction;
		ally.X = player.X + 1;
		ally.Y = player.Y;
		ally.Z = player.Z;
		ActorModule.Add(state, ally);

		var result = HealthActionModule.TryTendOther(state, player, ally);
		Assert.False(result.Consumed);
	}

	[Fact]
	public void TryTendOther_DifferentZ_ReturnsNotConsumed()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState();
		var ally = PresetDB.SpawnActor("player", "ally");
		ally.Faction = player.Faction;
		ally.X = player.X + 1;
		ally.Y = player.Y;
		ally.Z = player.Z + 1;
		ActorModule.Add(state, ally);

		var result = HealthActionModule.TryTendOther(state, player, ally);
		Assert.False(result.Consumed);
	}
}
