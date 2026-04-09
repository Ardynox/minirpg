using MiniRPG.Module;
using Xunit;

namespace MiniRPG.Tests;

public sealed class LoadRecoveryDialogModuleTests
{
	[Fact]
	public void HandleKey_ConfirmReturnsCurrentlySelectedCandidate()
	{
		var recovery = new PreparedLoadRecovery
		{
			MissingPlayerId = "missing-player",
			Candidates =
			[
				new PreparedLoadCandidate { ActorId = "player_a", DisplayName = "Rook", X = 1, Y = 2, Z = 0 },
				new PreparedLoadCandidate { ActorId = "player_b", DisplayName = "Rook Twin", X = 3, Y = 4, Z = 0 },
			],
		};

		var moved = LoadRecoveryDialogLogic.HandleKey(recovery, selectedIndex: 0, Godot.Key.Down, confirmBlockedByFocus: false);
		var confirmed = LoadRecoveryDialogLogic.HandleKey(recovery, moved.SelectedIndex, Godot.Key.Enter, confirmBlockedByFocus: false);

		Assert.True(moved.Handled);
		Assert.Equal(1, moved.SelectedIndex);
		Assert.True(confirmed.Handled);
		Assert.Equal("player_b", confirmed.ConfirmedActorId);
	}

	[Fact]
	public void HandleKey_EscapeRequestsCancel()
	{
		var recovery = new PreparedLoadRecovery
		{
			MissingPlayerId = "missing-player",
			Candidates =
			[
				new PreparedLoadCandidate { ActorId = "player_a", DisplayName = "Rook", X = 1, Y = 2, Z = 0 },
			],
		};

		var result = LoadRecoveryDialogLogic.HandleKey(recovery, selectedIndex: 0, Godot.Key.Escape, confirmBlockedByFocus: false);

		Assert.True(result.Handled);
		Assert.True(result.CancelRequested);
	}

	[Fact]
	public void HandleKey_EnterDoesNotBypassFocusedButtons()
	{
		var recovery = new PreparedLoadRecovery
		{
			MissingPlayerId = "missing-player",
			Candidates =
			[
				new PreparedLoadCandidate { ActorId = "player_a", DisplayName = "Rook", X = 1, Y = 2, Z = 0 },
			],
		};

		var result = LoadRecoveryDialogLogic.HandleKey(recovery, selectedIndex: 0, Godot.Key.Enter, confirmBlockedByFocus: true);

		Assert.False(result.Handled);
		Assert.Null(result.ConfirmedActorId);
	}

	[Fact]
	public void Open_WithNoCandidates_DisablesConfirm()
	{
		var recovery = new PreparedLoadRecovery
		{
			MissingPlayerId = "missing-player",
			Candidates = [],
		};

		var selectedIndex = LoadRecoveryDialogLogic.GetInitialSelectedIndex(recovery);

		Assert.Equal(-1, selectedIndex);
		Assert.False(LoadRecoveryDialogLogic.CanConfirm(recovery, selectedIndex));
		Assert.False(LoadRecoveryDialogLogic.HandleKey(recovery, selectedIndex, Godot.Key.Enter, confirmBlockedByFocus: false).Handled);
	}
}
