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

	public LoadRecoveryDialogModule(PanelContainer panel)
	{
		_panel = panel;
		_titleLabel = panel.GetNode<Label>("Margin/VBox/Title");
		_messageLabel = panel.GetNode<Label>("Margin/VBox/Message");
		_candidateList = panel.GetNode<ItemList>("Margin/VBox/CandidateList");
		_confirmButton = panel.GetNode<Button>("Margin/VBox/Footer/ConfirmBtn");
		_cancelButton = panel.GetNode<Button>("Margin/VBox/Footer/CancelBtn");

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
		_selectedIndex = recovery.Candidates.Count > 0 ? 0 : -1;
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

		switch (key.Keycode)
		{
			case Key.Escape:
				CancelRequested?.Invoke();
				return true;
			case Key.Up:
				return MoveSelection(-1);
			case Key.Down:
				return MoveSelection(1);
			case Key.Enter:
			case Key.KpEnter:
				return ConfirmCurrentSelection();
			default:
				return false;
		}
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

		if (_recovery.Candidates.Count == 0)
		{
			_selectedIndex = -1;
			_confirmButton.Disabled = true;
			return;
		}

		_selectedIndex = Math.Clamp(_selectedIndex, 0, _recovery.Candidates.Count - 1);
		_candidateList.Select(_selectedIndex);
		_confirmButton.Disabled = false;
	}

	private bool MoveSelection(int delta)
	{
		if (_recovery == null || _recovery.Candidates.Count == 0)
			return false;

		var current = _selectedIndex < 0 ? 0 : _selectedIndex;
		_selectedIndex = Math.Clamp(current + delta, 0, _recovery.Candidates.Count - 1);
		_candidateList.Select(_selectedIndex);
		_confirmButton.Disabled = false;
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

	private static string BuildCandidateText(PreparedLoadCandidate candidate) =>
		LocalizationService.T(
			"ui.load_recovery.candidate",
			("name", candidate.DisplayName),
			("actorId", candidate.ActorId),
			("x", candidate.X),
			("y", candidate.Y),
			("z", candidate.Z));
}
