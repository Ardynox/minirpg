using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class RuntimeCameraControllerTests
{
	[Fact]
	public void BuildSnapshot_FollowMode_TracksActiveActorPosition()
	{
		var state = CreateState(playerX: 3, playerY: 4, playerZ: 5);
		var controller = new RuntimeCameraController(state);
		var actor = Assert.IsType<Actor>(ActorModule.GetPlayer(state));

		actor.X = 9;
		actor.Y = 8;
		actor.Z = 7;

		var snapshot = controller.BuildSnapshot();

		Assert.Equal(RuntimeCameraMode.FollowActor, snapshot.Mode);
		Assert.Equal(9, snapshot.CenterX);
		Assert.Equal(8, snapshot.CenterY);
		Assert.Equal(7, snapshot.CenterZ);
	}

	[Fact]
	public void BeginPanDragFromCurrentView_FollowMode_SeedsFromActiveActor()
	{
		var controller = new RuntimeCameraController(CreateState(playerX: 6, playerY: 7, playerZ: 2));

		Assert.True(controller.BeginPanDragFromCurrentView());
		var snapshot = controller.BuildSnapshot();

		Assert.Equal(RuntimeCameraMode.LayerPan, snapshot.Mode);
		Assert.Equal(6, snapshot.CenterX);
		Assert.Equal(7, snapshot.CenterY);
		Assert.Equal(2, snapshot.CenterZ);
	}

	[Fact]
	public void PanDrag_MovesPanCameraByScreenSpaceDelta()
	{
		var controller = new RuntimeCameraController(CreateState(playerX: 10, playerY: 20, playerZ: 5));
		Assert.True(controller.BeginPanDragFromCurrentView());

		var changed = controller.PanByScreenDelta(new Vector2(64f, 32f));
		controller.EndPanDrag();
		var snapshot = controller.BuildSnapshot();

		Assert.True(changed);
		Assert.Equal(RuntimeCameraMode.LayerPan, snapshot.Mode);
		Assert.Equal(9, snapshot.CenterX);
		Assert.Equal(20, snapshot.CenterY);
		Assert.Equal(5, snapshot.CenterZ);
	}

	[Fact]
	public void BeginPanDragFromCurrentView_LayerPan_PreservesExistingPanPosition()
	{
		var state = CreateState(playerX: 2, playerY: 3, playerZ: 1);
		var controller = new RuntimeCameraController(state);

		controller.BeginPanDragFromCurrentView();
		controller.PanByScreenDelta(new Vector2(-64f, -32f));
		controller.EndPanDrag();
		var panSnapshot = controller.BuildSnapshot();

		var actor = Assert.IsType<Actor>(ActorModule.GetPlayer(state));
		actor.X = 30;
		actor.Y = 40;
		actor.Z = 6;

		controller.BeginPanDragFromCurrentView();
		var restoredPanSnapshot = controller.BuildSnapshot();

		Assert.Equal(RuntimeCameraMode.LayerPan, restoredPanSnapshot.Mode);
		Assert.Equal(panSnapshot.CenterX, restoredPanSnapshot.CenterX);
		Assert.Equal(panSnapshot.CenterY, restoredPanSnapshot.CenterY);
		Assert.Equal(panSnapshot.CenterZ, restoredPanSnapshot.CenterZ);
	}

	[Fact]
	public void ReturnToFollowActor_EndsDragAndReseedsNextPanFromActiveActor()
	{
		var state = CreateState(playerX: 4, playerY: 5, playerZ: 1);
		var controller = new RuntimeCameraController(state);
		var actor = Assert.IsType<Actor>(ActorModule.GetPlayer(state));

		controller.BeginPanDragFromCurrentView();
		controller.PanByScreenDelta(new Vector2(-64f, -32f));

		actor.X = 12;
		actor.Y = 14;
		actor.Z = 3;

		Assert.True(controller.ReturnToFollowActor());
		var followSnapshot = controller.BuildSnapshot();

		Assert.Equal(RuntimeCameraMode.FollowActor, followSnapshot.Mode);
		Assert.Equal(12, followSnapshot.CenterX);
		Assert.Equal(14, followSnapshot.CenterY);
		Assert.Equal(3, followSnapshot.CenterZ);
		Assert.False(controller.IsPanDragActive);

		controller.BeginPanDragFromCurrentView();
		var resumedPanSnapshot = controller.BuildSnapshot();

		Assert.Equal(RuntimeCameraMode.LayerPan, resumedPanSnapshot.Mode);
		Assert.Equal(12, resumedPanSnapshot.CenterX);
		Assert.Equal(14, resumedPanSnapshot.CenterY);
		Assert.Equal(3, resumedPanSnapshot.CenterZ);
	}

	[Fact]
	public void AdjustPanZ_ClampsWithinRuntimeLayerRange_AndNoOpsInFollowMode()
	{
		var controller = new RuntimeCameraController(CreateState(playerZ: 0));

		Assert.False(controller.AdjustPanZ(1));

		controller.BeginPanDragFromCurrentView();
		Assert.True(controller.AdjustPanZ(99));
		Assert.Equal(20, controller.BuildSnapshot().CenterZ);
		Assert.True(controller.AdjustPanZ(-99));
		Assert.Equal(-20, controller.BuildSnapshot().CenterZ);
	}

	private static GameState CreateState(int playerX = 0, int playerY = 0, int playerZ = 0)
	{
		var state = new GameState
		{
			PlayerId = "player",
			PlayerX = playerX,
			PlayerY = playerY,
			PlayerZ = playerZ,
			Actors = new Dictionary<string, Actor>(System.StringComparer.Ordinal),
		};
		var player = new Actor
		{
			Id = state.PlayerId,
			X = playerX,
			Y = playerY,
			Z = playerZ,
			Faction = Factions.Player,
			PrimaryDomainId = DomainIds.Player,
		};
		ActorModule.Add(state, player);
		return state;
	}
}
