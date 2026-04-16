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
	public void ShouldBlockNpcMotion_SinglePlayerFarFriendly_DoesNotBlock()
	{
		var state = new GameState();
		var activeActor = new Actor { Id = "player", Faction = Factions.Player };
		var friendlyActor = new Actor { Id = "ally", Faction = Factions.Friendly, X = 7, Y = 0, Z = 0 };
		ActorModule.Add(state, friendlyActor);

		Assert.False(Main.ShouldBlockNpcMotion(
			state,
			actorId: friendlyActor.Id,
			activeActor,
			isMultiplayerSession: false));
	}

	[Fact]
	public void ShouldBlockNpcMotion_SinglePlayerNearbyNpc_Blocks()
	{
		var state = new GameState();
		var activeActor = new Actor { Id = "player", Faction = Factions.Player };
		var nearbyActor = new Actor { Id = "ally", Faction = Factions.Friendly, X = 3, Y = 0, Z = 0 };
		ActorModule.Add(state, nearbyActor);

		Assert.True(Main.ShouldBlockNpcMotion(
			state,
			actorId: nearbyActor.Id,
			activeActor,
			isMultiplayerSession: false));
	}

	[Fact]
	public void ShouldBlockNpcMotion_SinglePlayerNearbyHostile_Blocks()
	{
		var state = new GameState();
		var activeActor = new Actor { Id = "player", Faction = Factions.Player, X = 0, Y = 0, Z = 0 };
		var hostileActor = new Actor { Id = "enemy", Faction = Factions.Hostile, X = 6, Y = 0, Z = 0 };
		ActorModule.Add(state, hostileActor);

		Assert.True(Main.ShouldBlockNpcMotion(
			state,
			actorId: hostileActor.Id,
			activeActor,
			isMultiplayerSession: false));
	}

	[Fact]
	public void ShouldBlockNpcMotion_Multiplayer_PreservesBlocking()
	{
		var state = new GameState();
		var activeActor = new Actor { Id = "player", Faction = Factions.Player };
		var friendlyActor = new Actor { Id = "ally", Faction = Factions.Friendly, X = 7, Y = 0, Z = 0 };
		ActorModule.Add(state, friendlyActor);

		Assert.True(Main.ShouldBlockNpcMotion(
			state,
			actorId: friendlyActor.Id,
			activeActor,
			isMultiplayerSession: true));
	}

	[Fact]
	public void ShouldUseNpcRushMotionDuration_SinglePlayerNonBlockingAutoChain_UsesRushDuration()
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
			blockingMotion: false,
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
			blockingMotion: false,
			hasNearbyThreat: static (_, _, _) => true));
	}

	[Fact]
	public void ShouldUseNpcRushMotionDuration_WhenBlockingMotion_DoesNotUseRushDuration()
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
			blockingMotion: true,
			hasNearbyThreat: static (_, _, _) => false));
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
			blockingMotion: false,
			hasNearbyThreat: static (_, _, _) => false));
	}
}
