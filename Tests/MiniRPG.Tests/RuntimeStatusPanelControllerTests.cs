using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Module.Panel;
using Xunit;

namespace MiniRPG.Tests;

public sealed class RuntimeStatusPanelControllerTests
{
	[Fact]
	public void ToggleActiveActorPanel_NoActiveActor_DoesNothing()
	{
		var controller = CreateController(CreateStateWithMissingActiveActor());
		var changedCalls = 0;
		controller.PanelsChanged += () => changedCalls++;

		controller.ToggleActiveActorPanel();

		Assert.False(controller.IsActiveActorPanelVisible);
		Assert.False(controller.HasFocusedPanel);
		Assert.Equal(0, changedCalls);
	}

	[Fact]
	public void CloseFocusedPanel_WithNoFocus_DoesNothing()
	{
		var controller = CreateController(CreateStateWithMissingActiveActor());
		var changedCalls = 0;
		controller.PanelsChanged += () => changedCalls++;

		controller.CloseFocusedPanel();

		Assert.False(controller.HasFocusedPanel);
		Assert.Equal(0, changedCalls);
	}

	[Fact]
	public void OpenCorpse_BlankInstanceId_IsIgnored()
	{
		var controller = CreateController(CreateStateWithMissingActiveActor());
		var changedCalls = 0;
		controller.PanelsChanged += () => changedCalls++;
		var corpse = new Item
		{
			Id = "corpse",
			Name = "Corpse",
			InstanceId = " ",
			Corpse = new ItemCorpseMetadata(),
		};

		controller.OpenCorpse(corpse, new Vector3I(1, 2, 0));

		Assert.False(controller.HasFocusedPanel);
		Assert.Equal(0, changedCalls);
	}

	private static RuntimeStatusPanelController CreateController(GameState state) =>
		new(
			state,
			(PanelContainer)RuntimeHelpers.GetUninitializedObject(typeof(PanelContainer)),
			theme: null,
			(HBoxContainer)RuntimeHelpers.GetUninitializedObject(typeof(HBoxContainer)),
			new PanelManager(),
			(PanelLayoutService)RuntimeHelpers.GetUninitializedObject(typeof(PanelLayoutService)),
			(PanelDragService)RuntimeHelpers.GetUninitializedObject(typeof(PanelDragService)),
			(PanelHoverChromeService)RuntimeHelpers.GetUninitializedObject(typeof(PanelHoverChromeService)));

	private static GameState CreateStateWithMissingActiveActor() =>
		new()
		{
			PlayerId = "player",
			Actors = new Dictionary<string, Actor>(),
			Party = new PartyState
			{
				MemberIds = ["missing_actor"],
				ActiveId = "missing_actor",
			},
		};
}
