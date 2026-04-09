using Godot;
using MiniRPG.Module;
using Xunit;

namespace MiniRPG.Tests;

public sealed class LoadRecoveryDialogModuleTests
{
	[Fact]
	public void HandleKeyInput_ConfirmReturnsCurrentlySelectedCandidate()
	{
		var module = new LoadRecoveryDialogModule(CreatePanel());
		string? confirmedActorId = null;
		module.RecoveryConfirmed += actorId => confirmedActorId = actorId;

		module.Open(new PreparedLoadRecovery
		{
			MissingPlayerId = "missing-player",
			Candidates =
			[
				new PreparedLoadCandidate { ActorId = "player_a", DisplayName = "Rook", X = 1, Y = 2, Z = 0 },
				new PreparedLoadCandidate { ActorId = "player_b", DisplayName = "Rook Twin", X = 3, Y = 4, Z = 0 },
			],
		});

		Assert.True(module.HandleKeyInput(new InputEventKey { Pressed = true, Keycode = Key.Down }));
		Assert.True(module.HandleKeyInput(new InputEventKey { Pressed = true, Keycode = Key.Enter }));
		Assert.Equal("player_b", confirmedActorId);
	}

	[Fact]
	public void HandleKeyInput_EscapeRaisesCancel()
	{
		var module = new LoadRecoveryDialogModule(CreatePanel());
		var cancelCount = 0;
		module.CancelRequested += () => cancelCount++;

		module.Open(new PreparedLoadRecovery
		{
			MissingPlayerId = "missing-player",
			Candidates =
			[
				new PreparedLoadCandidate { ActorId = "player_a", DisplayName = "Rook", X = 1, Y = 2, Z = 0 },
			],
		});

		Assert.True(module.HandleKeyInput(new InputEventKey { Pressed = true, Keycode = Key.Escape }));
		Assert.Equal(1, cancelCount);
	}

	[Fact]
	public void HandleKeyInput_EnterDoesNotBypassFocusedButtons()
	{
		var viewport = new SubViewport();
		var panel = CreatePanel();
		viewport.AddChild(panel);
		var module = new LoadRecoveryDialogModule(panel);
		string? confirmedActorId = null;
		module.RecoveryConfirmed += actorId => confirmedActorId = actorId;

		module.Open(new PreparedLoadRecovery
		{
			MissingPlayerId = "missing-player",
			Candidates =
			[
				new PreparedLoadCandidate { ActorId = "player_a", DisplayName = "Rook", X = 1, Y = 2, Z = 0 },
			],
		});

		panel.GetNode<Button>("Margin/VBox/Footer/CancelBtn").GrabFocus();

		Assert.False(module.HandleKeyInput(new InputEventKey { Pressed = true, Keycode = Key.Enter }));
		Assert.Null(confirmedActorId);
	}

	private static PanelContainer CreatePanel()
	{
		var panel = new PanelContainer();
		var margin = new MarginContainer { Name = "Margin" };
		var vbox = new VBoxContainer { Name = "VBox" };
		var title = new Label { Name = "Title" };
		var message = new Label { Name = "Message" };
		var candidateList = new ItemList { Name = "CandidateList" };
		var footer = new HBoxContainer { Name = "Footer" };
		var confirm = new Button { Name = "ConfirmBtn" };
		var cancel = new Button { Name = "CancelBtn" };

		panel.AddChild(margin);
		margin.AddChild(vbox);
		vbox.AddChild(title);
		vbox.AddChild(message);
		vbox.AddChild(candidateList);
		vbox.AddChild(footer);
		footer.AddChild(confirm);
		footer.AddChild(cancel);
		return panel;
	}
}
