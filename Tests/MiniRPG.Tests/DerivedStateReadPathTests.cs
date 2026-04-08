using System.Collections.Generic;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using MiniRPG.Core.Needs;
using MiniRPG.Module.Panel;
using Xunit;

namespace MiniRPG.Tests;

public sealed class DerivedStateReadPathTests
{
	public DerivedStateReadPathTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	[Fact]
	public void DialogContextBuild_DoesNotMutateNeedOrHealthTimestamps()
	{
		var (state, player, npc) = SkillCastingTestHelper.CreateCombatState(enemyX: 2, enemyY: 1);
		state.Turn = 40;

		NeedSystem.EnsureInitialized(player, currentTurn: 1);
		NeedSystem.EnsureInitialized(npc, currentTurn: 2);
		HealthSystem.EnsureInitialized(player, currentTurn: 1);
		HealthSystem.EnsureInitialized(npc, currentTurn: 2);
		player.NeedsLastUpdatedTurn = 1;
		player.HealthLastUpdatedTurn = 1;
		npc.NeedsLastUpdatedTurn = 2;
		npc.HealthLastUpdatedTurn = 2;

		var context = DialogContext.Build(state, player, npc);

		Assert.NotNull(context);
		Assert.Equal(1, player.NeedsLastUpdatedTurn);
		Assert.Equal(1, player.HealthLastUpdatedTurn);
		Assert.Equal(2, npc.NeedsLastUpdatedTurn);
		Assert.Equal(2, npc.HealthLastUpdatedTurn);
	}

	[Fact]
	public void NeedsTabBuildLines_DoesNotSyncActorState()
	{
		var (_, actor, _) = SkillCastingTestHelper.CreateCombatState();
		NeedSystem.EnsureInitialized(actor, currentTurn: 3);
		actor.NeedsLastUpdatedTurn = 3;
		actor.HealthLastUpdatedTurn = 7;

		var lines = new List<string>();
		ActorStatusTextBuilder.BuildLines(lines, StatusTab.Needs, actor);

		Assert.NotEmpty(lines);
		Assert.Equal(3, actor.NeedsLastUpdatedTurn);
		Assert.Equal(7, actor.HealthLastUpdatedTurn);
	}
}
