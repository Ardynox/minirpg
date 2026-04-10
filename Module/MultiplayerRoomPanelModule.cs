using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace MiniRPG.Module;

public enum MultiplayerRoomPanelMode
{
	LocalSession,
	MultiplayerRoom,
}

public sealed class MultiplayerRoomPlayerOption
{
	public string PlayerSessionId { get; init; } = string.Empty;
	public string DisplayLabel { get; init; } = string.Empty;
}

public sealed class MultiplayerRoomActorOption
{
	public string ActorId { get; init; } = string.Empty;
	public string DisplayLabel { get; init; } = string.Empty;
}

public sealed class MultiplayerRoomHostRequest
{
	public string RoomDisplayName { get; init; } = string.Empty;
	public bool IsPublic { get; init; }
}

public sealed class MultiplayerRoomPanelViewState
{
	public MultiplayerRoomPanelMode Mode { get; init; }
	public string Subtitle { get; init; } = string.Empty;
	public string Summary { get; init; } = string.Empty;
	public string StatusMessage { get; init; } = string.Empty;
	public bool StatusIsError { get; init; }
	public bool Busy { get; init; }
	public bool CanHostCurrentSession { get; init; }
	public string HostRoomDisplayName { get; init; } = string.Empty;
	public bool HostRoomIsPublic { get; init; }
	public IReadOnlyList<MultiplayerRoomPlayerOption> Players { get; init; } = Array.Empty<MultiplayerRoomPlayerOption>();
	public IReadOnlyList<MultiplayerRoomActorOption> Actors { get; init; } = Array.Empty<MultiplayerRoomActorOption>();
	public string CurrentPlayerSessionId { get; init; } = string.Empty;
	public bool IsCurrentPlayerRoomOwner { get; init; }
	public bool CanReclaimPrimaryActor { get; init; }
}

public sealed class MultiplayerRoomPanelModule : IModalInputLayer
{
	private readonly PanelContainer _panel;
	private readonly Label _titleLabel;
	private readonly Label _subtitleLabel;
	private readonly Label _summaryLabel;
	private readonly Label _statusLabel;
	private readonly VBoxContainer _localHostSection;
	private readonly Label _localHostHintLabel;
	private readonly LineEdit _hostRoomNameInput;
	private readonly CheckButton _hostPublicCheck;
	private readonly Button _hostCurrentSessionButton;
	private readonly HBoxContainer _inRoomSection;
	private readonly Label _playerListTitleLabel;
	private readonly ItemList _playerList;
	private readonly Label _actorListTitleLabel;
	private readonly ItemList _actorList;
	private readonly Button _assignPrimaryActorButton;
	private readonly Button _kickPlayerButton;
	private readonly Button _reclaimPrimaryActorButton;
	private readonly Button _closeButton;

	private readonly List<string> _playerIds = [];
	private readonly List<string> _actorIds = [];

	private MultiplayerRoomPanelViewState _state = new();

	public MultiplayerRoomPanelModule(PanelContainer panel)
	{
		_panel = panel;
		_titleLabel = panel.GetNode<Label>("Margin/VBox/Title");
		_subtitleLabel = panel.GetNode<Label>("Margin/VBox/Subtitle");
		_summaryLabel = panel.GetNode<Label>("Margin/VBox/Summary");
		_statusLabel = panel.GetNode<Label>("Margin/VBox/Status");
		_localHostSection = panel.GetNode<VBoxContainer>("Margin/VBox/Content/LocalHostSection");
		_localHostHintLabel = panel.GetNode<Label>("Margin/VBox/Content/LocalHostSection/Hint");
		_hostRoomNameInput = panel.GetNode<LineEdit>("Margin/VBox/Content/LocalHostSection/HostGrid/RoomNameInput");
		_hostPublicCheck = panel.GetNode<CheckButton>("Margin/VBox/Content/LocalHostSection/PublicCheck");
		_hostCurrentSessionButton = panel.GetNode<Button>("Margin/VBox/Content/LocalHostSection/HostCurrentSessionBtn");
		_inRoomSection = panel.GetNode<HBoxContainer>("Margin/VBox/Content/InRoomSection");
		_playerListTitleLabel = panel.GetNode<Label>("Margin/VBox/Content/InRoomSection/PlayerColumn/PlayerListTitle");
		_playerList = panel.GetNode<ItemList>("Margin/VBox/Content/InRoomSection/PlayerColumn/PlayerList");
		_actorListTitleLabel = panel.GetNode<Label>("Margin/VBox/Content/InRoomSection/ActorColumn/ActorListTitle");
		_actorList = panel.GetNode<ItemList>("Margin/VBox/Content/InRoomSection/ActorColumn/ActorList");
		_assignPrimaryActorButton = panel.GetNode<Button>("Margin/VBox/Content/InRoomSection/ActionColumn/AssignPrimaryActorBtn");
		_kickPlayerButton = panel.GetNode<Button>("Margin/VBox/Content/InRoomSection/ActionColumn/KickPlayerBtn");
		_reclaimPrimaryActorButton = panel.GetNode<Button>("Margin/VBox/Content/InRoomSection/ActionColumn/ReclaimPrimaryActorBtn");
		_closeButton = panel.GetNode<Button>("Margin/VBox/Footer/CloseBtn");

		_closeButton.Pressed += () => CloseRequested?.Invoke();
		_hostCurrentSessionButton.Pressed += HandleHostCurrentSessionPressed;
		_assignPrimaryActorButton.Pressed += HandleAssignPrimaryActorPressed;
		_kickPlayerButton.Pressed += HandleKickPlayerPressed;
		_reclaimPrimaryActorButton.Pressed += () => ReclaimPrimaryActorRequested?.Invoke();
		_playerList.ItemSelected += _ => UpdateActionButtons();
		_actorList.ItemSelected += _ => UpdateActionButtons();

		_panel.Visible = false;
		RefreshTexts();
		ApplyState(_state);
	}

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public string HostRoomDisplayName =>
		string.IsNullOrWhiteSpace(_hostRoomNameInput.Text)
			? LocalizationService.TOrFallback("ui.multiplayer.room_panel.host.default_name", "Current Session")
			: _hostRoomNameInput.Text.Trim();

	public bool HostRoomIsPublic => _hostPublicCheck.ButtonPressed;

	public event Action? CloseRequested;
	public event Action<MultiplayerRoomHostRequest>? HostCurrentSessionRequested;
	public event Action<string, string>? AssignPrimaryActorRequested;
	public event Action<string>? KickPlayerRequested;
	public event Action? ReclaimPrimaryActorRequested;

	public void Open(MultiplayerRoomPanelViewState state)
	{
		ApplyState(state);
		Visible = true;
	}

	public void Close() => Visible = false;

	public void ApplyState(MultiplayerRoomPanelViewState state)
	{
		_state = state;
		_subtitleLabel.Text = state.Subtitle;
		_summaryLabel.Text = state.Summary;
		_summaryLabel.Visible = !string.IsNullOrWhiteSpace(state.Summary);
		_statusLabel.Visible = !string.IsNullOrWhiteSpace(state.StatusMessage);
		_statusLabel.Text = state.StatusMessage;
		_statusLabel.Modulate = state.StatusIsError
			? new Color(1f, 0.58f, 0.58f, 1f)
			: new Color(0.72f, 0.92f, 0.76f, 1f);

		var localMode = state.Mode == MultiplayerRoomPanelMode.LocalSession;
		_localHostSection.Visible = localMode;
		_inRoomSection.Visible = !localMode;
		_hostRoomNameInput.Text = state.HostRoomDisplayName;
		_hostPublicCheck.ButtonPressed = state.HostRoomIsPublic;
		ApplyPlayers(state.Players);
		ApplyActors(state.Actors);
		ApplyBusyState(state.Busy);
		UpdateActionButtons();
	}

	public void RefreshTexts()
	{
		_titleLabel.Text = LocalizationService.TOrFallback("ui.multiplayer.room_panel.title", "Multiplayer Room");
		_localHostHintLabel.Text = LocalizationService.TOrFallback(
			"ui.multiplayer.room_panel.host.hint",
			"Host the current session with your saved multiplayer profile and server settings.");
		_hostRoomNameInput.PlaceholderText = LocalizationService.TOrFallback(
			"ui.multiplayer.room_panel.host.room_name.placeholder",
			"Room display name");
		_hostPublicCheck.Text = LocalizationService.TOrFallback(
			"ui.multiplayer.room_panel.host.public",
			"List this room publicly");
		_hostCurrentSessionButton.Text = LocalizationService.TOrFallback(
			"ui.multiplayer.room_panel.host.action",
			"Host Current Session");
		_playerListTitleLabel.Text = LocalizationService.TOrFallback("ui.multiplayer.room_panel.players", "Players");
		_actorListTitleLabel.Text = LocalizationService.TOrFallback("ui.multiplayer.room_panel.actors", "Assignable Actors");
		_assignPrimaryActorButton.Text = LocalizationService.TOrFallback(
			"ui.multiplayer.room_panel.assign_primary_actor",
			"Assign Selected Actor");
		_kickPlayerButton.Text = LocalizationService.TOrFallback(
			"ui.multiplayer.room_panel.kick_player",
			"Kick Selected Player");
		_reclaimPrimaryActorButton.Text = LocalizationService.TOrFallback(
			"ui.multiplayer.room_panel.reclaim_primary_actor",
			"Reclaim My Actor");
		_closeButton.Text = LocalizationService.TOrFallback("ui.multiplayer.room_panel.close", "Close");
	}

	public bool HandleKeyInput(InputEventKey key)
	{
		if (!key.Pressed || key.Echo || key.AltPressed || key.CtrlPressed || key.MetaPressed)
			return false;

		if (key.Keycode == Key.Escape)
		{
			CloseRequested?.Invoke();
			return true;
		}

		return false;
	}

	private void ApplyPlayers(IReadOnlyList<MultiplayerRoomPlayerOption> players)
	{
		var previousPlayerId = SelectedPlayerSessionId;
		_playerIds.Clear();
		_playerList.Clear();
		foreach (var player in players)
		{
			_playerIds.Add(player.PlayerSessionId);
			_playerList.AddItem(player.DisplayLabel);
		}

		ReselectItem(_playerList, _playerIds, previousPlayerId);
	}

	private void ApplyActors(IReadOnlyList<MultiplayerRoomActorOption> actors)
	{
		var previousActorId = SelectedActorId;
		_actorIds.Clear();
		_actorList.Clear();
		foreach (var actor in actors)
		{
			_actorIds.Add(actor.ActorId);
			_actorList.AddItem(actor.DisplayLabel);
		}

		ReselectItem(_actorList, _actorIds, previousActorId);
	}

	private void ApplyBusyState(bool busy)
	{
		_hostRoomNameInput.Editable = !busy;
		_hostPublicCheck.Disabled = busy;
		_hostCurrentSessionButton.Disabled = busy || !_state.CanHostCurrentSession;
		_playerList.MouseFilter = busy ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop;
		_actorList.MouseFilter = busy ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop;
		_playerList.FocusMode = busy ? Control.FocusModeEnum.None : Control.FocusModeEnum.All;
		_actorList.FocusMode = busy ? Control.FocusModeEnum.None : Control.FocusModeEnum.All;
		_closeButton.Disabled = busy;
	}

	private void UpdateActionButtons()
	{
		var selectedPlayerId = SelectedPlayerSessionId;
		var selectedActorId = SelectedActorId;
		var busy = _state.Busy;
		_assignPrimaryActorButton.Disabled = busy
			|| _state.Mode != MultiplayerRoomPanelMode.MultiplayerRoom
			|| !_state.IsCurrentPlayerRoomOwner
			|| string.IsNullOrWhiteSpace(selectedPlayerId)
			|| string.IsNullOrWhiteSpace(selectedActorId);
		_kickPlayerButton.Disabled = busy
			|| _state.Mode != MultiplayerRoomPanelMode.MultiplayerRoom
			|| !_state.IsCurrentPlayerRoomOwner
			|| string.IsNullOrWhiteSpace(selectedPlayerId)
			|| string.Equals(selectedPlayerId, _state.CurrentPlayerSessionId, StringComparison.Ordinal);
		_reclaimPrimaryActorButton.Disabled = busy
			|| _state.Mode != MultiplayerRoomPanelMode.MultiplayerRoom
			|| !_state.CanReclaimPrimaryActor;
	}

	private void HandleHostCurrentSessionPressed()
	{
		HostCurrentSessionRequested?.Invoke(new MultiplayerRoomHostRequest
		{
			RoomDisplayName = HostRoomDisplayName,
			IsPublic = HostRoomIsPublic,
		});
	}

	private void HandleAssignPrimaryActorPressed()
	{
		var playerSessionId = SelectedPlayerSessionId;
		var actorId = SelectedActorId;
		if (string.IsNullOrWhiteSpace(playerSessionId) || string.IsNullOrWhiteSpace(actorId))
			return;

		AssignPrimaryActorRequested?.Invoke(playerSessionId, actorId);
	}

	private void HandleKickPlayerPressed()
	{
		var playerSessionId = SelectedPlayerSessionId;
		if (string.IsNullOrWhiteSpace(playerSessionId))
			return;

		KickPlayerRequested?.Invoke(playerSessionId);
	}

	private string? SelectedPlayerSessionId
	{
		get
		{
			var selectedItems = _playerList.GetSelectedItems();
			if (selectedItems.Length == 0)
				return null;

			var index = selectedItems[0];
			return index >= 0 && index < _playerIds.Count ? _playerIds[index] : null;
		}
	}

	private string? SelectedActorId
	{
		get
		{
			var selectedItems = _actorList.GetSelectedItems();
			if (selectedItems.Length == 0)
				return null;

			var index = selectedItems[0];
			return index >= 0 && index < _actorIds.Count ? _actorIds[index] : null;
		}
	}

	private static void ReselectItem(ItemList list, IReadOnlyList<string> ids, string? previousId)
	{
		if (string.IsNullOrWhiteSpace(previousId))
		{
			if (ids.Count > 0)
				list.Select(0);
			return;
		}

		for (var i = 0; i < ids.Count; i++)
		{
			if (!string.Equals(ids[i], previousId, StringComparison.Ordinal))
				continue;

			list.Select(i);
			return;
		}

		if (ids.Count > 0)
			list.Select(0);
	}
}
