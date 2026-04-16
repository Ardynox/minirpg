using MiniRPG.Core.Data;
using Xunit;

namespace MiniRPG.Tests;

public sealed class MainTimelineMotionTests
{
	[Fact]
	public void ShouldPauseTimelineAutoAdvanceForBlockingMotion_NormalMode_WaitsForMotion()
	{
		Assert.True(Main.ShouldPauseTimelineAutoAdvanceForBlockingMotion(
			watchModeEnabled: false,
			fastTurnModeEnabled: false,
			hasBlockingActorMotion: true));
	}

	[Fact]
	public void ShouldPauseTimelineAutoAdvanceForBlockingMotion_FastTurnMode_WaitsForMotion()
	{
		Assert.True(Main.ShouldPauseTimelineAutoAdvanceForBlockingMotion(
			watchModeEnabled: false,
			fastTurnModeEnabled: true,
			hasBlockingActorMotion: true));
	}

	[Fact]
	public void ShouldPauseTimelineAutoAdvanceForBlockingMotion_NoMotion_DoesNotPause()
	{
		Assert.False(Main.ShouldPauseTimelineAutoAdvanceForBlockingMotion(
			watchModeEnabled: false,
			fastTurnModeEnabled: true,
			hasBlockingActorMotion: false));
	}

	[Fact]
	public void ShouldUseNpcRushMotionDuration_SinglePlayerSafeAutoChain_UsesRushDuration()
	{
		var state = new GameState();
		var activeActor = new Actor { Id = "player", Faction = Factions.Player };

		Assert.True(Main.ShouldUseNpcRushMotionDuration(
			state,
			actorId: "npc",
			activeActor,
			watchModeEnabled: false,
			isMultiplayerSession: false,
			hasAdditionalAutoAdvance: true,
			hasNearbyThreat: static (_, _, _) => false));
	}

	[Fact]
	public void ShouldUseNpcRushMotionDuration_WhenThreatNearby_DoesNotUseRushDuration()
	{
		var state = new GameState();
		var activeActor = new Actor { Id = "player", Faction = Factions.Player };

		Assert.False(Main.ShouldUseNpcRushMotionDuration(
			state,
			actorId: "npc",
			activeActor,
			watchModeEnabled: false,
			isMultiplayerSession: false,
			hasAdditionalAutoAdvance: true,
			hasNearbyThreat: static (_, _, _) => true));
	}

	[Fact]
	public void ShouldUseNpcRushMotionDuration_WhenNoFurtherAutoChain_DoesNotUseRushDuration()
	{
		var state = new GameState();
		var activeActor = new Actor { Id = "player", Faction = Factions.Player };

		Assert.False(Main.ShouldUseNpcRushMotionDuration(
			state,
			actorId: "npc",
			activeActor,
			watchModeEnabled: false,
			isMultiplayerSession: false,
			hasAdditionalAutoAdvance: false,
			hasNearbyThreat: static (_, _, _) => false));
	}
}
