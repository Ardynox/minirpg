using System;
using Godot;

namespace MiniRPG.Module;

public sealed class LoadRecoveryDialogModule : IModalInputLayer
{
	private readonly PanelContainer _panel;
	private readonly Label _titleLabel;
	private readonly Label _messageLabel;
	private readonly ItemList _candidateList;
	private readonly Button _confirmButton;
	private readonly Button _cancelButton;

	private PreparedLoadRecovery? _recovery;
	private int _selectedIndex = -1;

	public event Action<string>? RecoveryConfirmed;
	public event Action? CancelRequested;
	internal Func<Control?> FocusOwnerResolver { get; set; }

	public LoadRecoveryDialogModule(PanelContainer panel)
	{
		_panel = panel;
		_titleLabel = panel.GetNode<Label>("Margin/VBox/Title");
		_messageLabel = panel.GetNode<Label>("Margin/VBox/Message");
		_candidateList = panel.GetNode<ItemList>("Margin/VBox/CandidateList");
		_confirmButton = panel.GetNode<Button>("Margin/VBox/Footer/ConfirmBtn");
		_cancelButton = panel.GetNode<Button>("Margin/VBox/Footer/CancelBtn");
		FocusOwnerResolver = ResolveFocusOwnerFromViewport;

		_candidateList.SelectMode = ItemList.SelectModeEnum.Single;
		_candidateList.ItemSelected += index =>
		{
			_selectedIndex = (int)index;
			_confirmButton.Disabled = _selectedIndex < 0;
		};
		_candidateList.ItemActivated += index =>
		{
			_selectedIndex = (int)index;
			ConfirmSelection();
		};
		_confirmButton.Pressed += ConfirmSelection;
		_cancelButton.Pressed += () => CancelRequested?.Invoke();
	}

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public void Open(PreparedLoadRecovery recovery)
	{
		ArgumentNullException.ThrowIfNull(recovery);
		_recovery = recovery;
		_selectedIndex = LoadRecoveryDialogLogic.GetInitialSelectedIndex(recovery);
		Visible = true;
		RefreshTexts();
		if (_candidateList.IsInsideTree())
			_candidateList.GrabFocus();
	}

	public void RefreshTexts()
	{
		_titleLabel.Text = LocalizationService.T("ui.load_recovery.title");
		_messageLabel.Text = LocalizationService.T(
			"ui.load_recovery.message",
			("playerId", _recovery?.MissingPlayerId ?? string.Empty));
		_confirmButton.Text = LocalizationService.T("ui.load_recovery.confirm");
		_cancelButton.Text = LocalizationService.T("ui.load_recovery.cancel");
		RefreshCandidateList();
	}

	public void Close()
	{
		Visible = false;
		_recovery = null;
		_selectedIndex = -1;
		_candidateList.Clear();
		_confirmButton.Disabled = true;
	}

	public bool HandleKeyInput(InputEventKey key)
	{
		if (!Visible || !key.Pressed || key.Echo || key.AltPressed || key.CtrlPressed || key.MetaPressed)
			return false;

		var result = LoadRecoveryDialogLogic.HandleKey(
			_recovery,
			_selectedIndex,
			key.Keycode,
			ShouldBlockConfirm(FocusOwnerResolver()));
		if (!result.Handled)
			return false;

		_selectedIndex = result.SelectedIndex;
		ApplySelectionState();
		if (result.CancelRequested)
			CancelRequested?.Invoke();
		if (!string.IsNullOrWhiteSpace(result.ConfirmedActorId))
			RecoveryConfirmed?.Invoke(result.ConfirmedActorId);
		return true;
	}

	internal static bool ShouldBlockConfirm(Control? focusOwner) => focusOwner is Button;

	private Control? ResolveFocusOwnerFromViewport()
	{
		var viewport = _panel.GetViewport();
		return viewport?.GuiGetFocusOwner();
	}

	private void RefreshCandidateList()
	{
		_candidateList.Clear();
		if (_recovery == null)
		{
			_confirmButton.Disabled = true;
			return;
		}

		foreach (var candidate in _recovery.Candidates)
			_candidateList.AddItem(BuildCandidateText(candidate));

		_selectedIndex = LoadRecoveryDialogLogic.NormalizeSelection(_recovery, _selectedIndex);
		ApplySelectionState();
	}

	private bool MoveSelection(int delta)
	{
		if (!LoadRecoveryDialogLogic.TryMoveSelection(_recovery, _selectedIndex, delta, out var nextIndex))
			return false;

		_selectedIndex = nextIndex;
		ApplySelectionState();
		return true;
	}

	private bool ConfirmCurrentSelection()
	{
		if (_recovery == null || _selectedIndex < 0 || _selectedIndex >= _recovery.Candidates.Count)
			return false;

		RecoveryConfirmed?.Invoke(_recovery.Candidates[_selectedIndex].ActorId);
		return true;
	}

	private void ConfirmSelection()
	{
		_ = ConfirmCurrentSelection();
	}

	private void ApplySelectionState()
	{
		if (_recovery != null && _selectedIndex >= 0 && _selectedIndex < _recovery.Candidates.Count)
			_candidateList.Select(_selectedIndex);

		_confirmButton.Disabled = !LoadRecoveryDialogLogic.CanConfirm(_recovery, _selectedIndex);
	}

	private static string BuildCandidateText(PreparedLoadCandidate candidate) =>
		LocalizationService.T(
			"ui.load_recovery.candidate",
			("name", candidate.DisplayName),
			("actorId", candidate.ActorId),
			("x", candidate.X),
			("y", candidate.Y),
			("z", candidate.Z));
}

internal static class LoadRecoveryDialogLogic
{
	public static int GetInitialSelectedIndex(PreparedLoadRecovery? recovery) =>
		recovery is { Candidates.Count: > 0 } ? 0 : -1;

	public static int NormalizeSelection(PreparedLoadRecovery? recovery, int selectedIndex)
	{
		if (recovery == null || recovery.Candidates.Count == 0)
			return -1;

		return Math.Clamp(selectedIndex, 0, recovery.Candidates.Count - 1);
	}

	public static bool CanConfirm(PreparedLoadRecovery? recovery, int selectedIndex) =>
		recovery != null
		&& selectedIndex >= 0
		&& selectedIndex < recovery.Candidates.Count;

	public static bool TryMoveSelection(PreparedLoadRecovery? recovery, int selectedIndex, int delta, out int nextIndex)
	{
		nextIndex = selectedIndex;
		if (recovery == null || recovery.Candidates.Count == 0)
			return false;

		var current = selectedIndex < 0 ? 0 : selectedIndex;
		nextIndex = Math.Clamp(current + delta, 0, recovery.Candidates.Count - 1);
		return true;
	}

	public static bool TryConfirm(PreparedLoadRecovery? recovery, int selectedIndex, out string actorId)
	{
		actorId = string.Empty;
		if (!CanConfirm(recovery, selectedIndex))
			return false;

		actorId = recovery!.Candidates[selectedIndex].ActorId;
		return true;
	}

	public static LoadRecoveryDialogKeyResult HandleKey(
		PreparedLoadRecovery? recovery,
		int selectedIndex,
		Key keycode,
		bool confirmBlockedByFocus)
	{
		switch (keycode)
		{
			case Key.Escape:
				return new LoadRecoveryDialogKeyResult(true, selectedIndex, CancelRequested: true);

			case Key.Up:
				return TryMoveSelection(recovery, selectedIndex, -1, out var previousIndex)
					? new LoadRecoveryDialogKeyResult(true, previousIndex)
					: LoadRecoveryDialogKeyResult.Unhandled(selectedIndex);

			case Key.Down:
				return TryMoveSelection(recovery, selectedIndex, 1, out var nextIndex)
					? new LoadRecoveryDialogKeyResult(true, nextIndex)
					: LoadRecoveryDialogKeyResult.Unhandled(selectedIndex);

			case Key.Enter:
			case Key.KpEnter:
				if (confirmBlockedByFocus)
					return LoadRecoveryDialogKeyResult.Unhandled(selectedIndex);

				return TryConfirm(recovery, selectedIndex, out var actorId)
					? new LoadRecoveryDialogKeyResult(true, selectedIndex, ConfirmedActorId: actorId)
					: LoadRecoveryDialogKeyResult.Unhandled(selectedIndex);

			default:
				return LoadRecoveryDialogKeyResult.Unhandled(selectedIndex);
		}
	}
}

internal readonly record struct LoadRecoveryDialogKeyResult(
	bool Handled,
	int SelectedIndex,
	bool CancelRequested = false,
	string? ConfirmedActorId = null)
{
	public static LoadRecoveryDialogKeyResult Unhandled(int selectedIndex) => new(false, selectedIndex);
}
