using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Multiplayer;

namespace MiniRPG.Module;

public sealed class MultiplayerHubTemplateOption
{
	public string Id { get; init; } = string.Empty;
	public string DisplayName { get; init; } = string.Empty;
	public string Summary { get; init; } = string.Empty;
}

public sealed class MultiplayerHubViewState
{
	public MultiplayerSettings Settings { get; init; } = new();
	public IReadOnlyList<LobbyRoomSummary> Rooms { get; init; } = Array.Empty<LobbyRoomSummary>();
	public IReadOnlyList<MultiplayerHubTemplateOption> Templates { get; init; } = Array.Empty<MultiplayerHubTemplateOption>();
	public MultiplayerReconnectTicket? ReconnectTicket { get; init; }
	public bool Busy { get; init; }
	public bool StatusIsError { get; init; }
	public string StatusMessage { get; init; } = string.Empty;
}

public sealed class MultiplayerHubJoinRequest
{
	public MultiplayerSettings Settings { get; init; } = new();
	public string RoomId { get; init; } = string.Empty;
}

public sealed class MultiplayerHubJoinCodeRequest
{
	public MultiplayerSettings Settings { get; init; } = new();
	public string RoomCode { get; init; } = string.Empty;
}

public sealed class MultiplayerHubCreateRequest
{
	public MultiplayerSettings Settings { get; init; } = new();
	public string RoomDisplayName { get; init; } = string.Empty;
	public string TemplateId { get; init; } = string.Empty;
	public bool IsPublic { get; init; }
}

public sealed class MultiplayerHubModule
{
	private readonly PanelContainer _panel;
	private readonly Label _titleLabel;
	private readonly Label _subtitleLabel;
	private readonly Button _backButton;
	private readonly LineEdit _displayNameInput;
	private readonly LineEdit _lobbyBaseUrlInput;
	private readonly OptionButton _hostModeOption;
	private readonly LineEdit _localServerPathInput;
	private readonly LineEdit _localLobbyPrefixInput;
	private readonly LineEdit _localGameAddressInput;
	private readonly SpinBox _localGamePortInput;
	private readonly Button _saveSettingsButton;
	private readonly Label _reconnectSummaryLabel;
	private readonly Button _reconnectButton;
	private readonly Button _refreshRoomsButton;
	private readonly Button _joinSelectedButton;
	private readonly ItemList _roomList;
	private readonly LineEdit _roomCodeInput;
	private readonly Button _joinByCodeButton;
	private readonly LineEdit _roomDisplayNameInput;
	private readonly OptionButton _templateOption;
	private readonly CheckButton _publicRoomCheck;
	private readonly Button _createRoomButton;
	private readonly Label _statusLabel;

	private readonly List<string> _roomIds = [];
	private readonly List<string> _templateIds = [];

	private MultiplayerHubViewState _state = new();

	public MultiplayerHubModule(PanelContainer panel)
	{
		_panel = panel;
		_titleLabel = panel.GetNode<Label>("Margin/VBox/Header/HeaderText/Title");
		_subtitleLabel = panel.GetNode<Label>("Margin/VBox/Header/HeaderText/Subtitle");
		_backButton = panel.GetNode<Button>("Margin/VBox/Header/BackBtn");
		_displayNameInput = panel.GetNode<LineEdit>("Margin/VBox/ContentScroll/Sections/ConfigSection/Margin/VBox/Grid/DisplayNameInput");
		_lobbyBaseUrlInput = panel.GetNode<LineEdit>("Margin/VBox/ContentScroll/Sections/ConfigSection/Margin/VBox/Grid/LobbyBaseUrlInput");
		_hostModeOption = panel.GetNode<OptionButton>("Margin/VBox/ContentScroll/Sections/ConfigSection/Margin/VBox/Grid/PreferredHostModeOption");
		_localServerPathInput = panel.GetNode<LineEdit>("Margin/VBox/ContentScroll/Sections/ConfigSection/Margin/VBox/Grid/LocalServerPathInput");
		_localLobbyPrefixInput = panel.GetNode<LineEdit>("Margin/VBox/ContentScroll/Sections/ConfigSection/Margin/VBox/Grid/LocalLobbyPrefixInput");
		_localGameAddressInput = panel.GetNode<LineEdit>("Margin/VBox/ContentScroll/Sections/ConfigSection/Margin/VBox/Grid/LocalGameAddressInput");
		_localGamePortInput = panel.GetNode<SpinBox>("Margin/VBox/ContentScroll/Sections/ConfigSection/Margin/VBox/Grid/LocalGamePortInput");
		_saveSettingsButton = panel.GetNode<Button>("Margin/VBox/ContentScroll/Sections/ConfigSection/Margin/VBox/SaveSettingsBtn");
		_reconnectSummaryLabel = panel.GetNode<Label>("Margin/VBox/ContentScroll/Sections/ReconnectSection/Margin/VBox/ReconnectSummary");
		_reconnectButton = panel.GetNode<Button>("Margin/VBox/ContentScroll/Sections/ReconnectSection/Margin/VBox/ReconnectBtn");
		_refreshRoomsButton = panel.GetNode<Button>("Margin/VBox/ContentScroll/Sections/RoomsSection/Margin/VBox/ActionRow/RefreshRoomsBtn");
		_joinSelectedButton = panel.GetNode<Button>("Margin/VBox/ContentScroll/Sections/RoomsSection/Margin/VBox/ActionRow/JoinSelectedBtn");
		_roomList = panel.GetNode<ItemList>("Margin/VBox/ContentScroll/Sections/RoomsSection/Margin/VBox/RoomList");
		_roomCodeInput = panel.GetNode<LineEdit>("Margin/VBox/ContentScroll/Sections/RoomsSection/Margin/VBox/JoinCodeRow/RoomCodeInput");
		_joinByCodeButton = panel.GetNode<Button>("Margin/VBox/ContentScroll/Sections/RoomsSection/Margin/VBox/JoinCodeRow/JoinByCodeBtn");
		_roomDisplayNameInput = panel.GetNode<LineEdit>("Margin/VBox/ContentScroll/Sections/CreateSection/Margin/VBox/Grid/RoomDisplayNameInput");
		_templateOption = panel.GetNode<OptionButton>("Margin/VBox/ContentScroll/Sections/CreateSection/Margin/VBox/Grid/TemplateOption");
		_publicRoomCheck = panel.GetNode<CheckButton>("Margin/VBox/ContentScroll/Sections/CreateSection/Margin/VBox/PublicCheck");
		_createRoomButton = panel.GetNode<Button>("Margin/VBox/ContentScroll/Sections/CreateSection/Margin/VBox/CreateRoomBtn");
		_statusLabel = panel.GetNode<Label>("Margin/VBox/StatusLabel");

		PopulateHostModeOptions();
		_backButton.Pressed += () => BackRequested?.Invoke();
		_saveSettingsButton.Pressed += () => SaveSettingsRequested?.Invoke(CaptureSettings());
		_refreshRoomsButton.Pressed += () => RefreshRoomsRequested?.Invoke(CaptureSettings());
		_joinSelectedButton.Pressed += HandleJoinSelectedPressed;
		_joinByCodeButton.Pressed += HandleJoinByCodePressed;
		_reconnectButton.Pressed += () => ReconnectRequested?.Invoke(CaptureSettings());
		_createRoomButton.Pressed += HandleCreatePressed;
		_roomList.ItemSelected += _ => UpdateJoinSelectedButtonState();

		_panel.Visible = false;
		RefreshTexts();
		ApplyState(_state);
	}

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public event Action? BackRequested;
	public event Action<MultiplayerSettings>? SaveSettingsRequested;
	public event Action<MultiplayerSettings>? RefreshRoomsRequested;
	public event Action<MultiplayerHubJoinRequest>? JoinRoomRequested;
	public event Action<MultiplayerHubJoinCodeRequest>? JoinByCodeRequested;
	public event Action<MultiplayerHubCreateRequest>? CreateRoomRequested;
	public event Action<MultiplayerSettings>? ReconnectRequested;

	public void Open(MultiplayerHubViewState state)
	{
		ApplyState(state);
		Visible = true;
	}

	public void Close() => Visible = false;

	public void ApplyState(MultiplayerHubViewState state)
	{
		_state = state;
		ApplySettings(state.Settings);
		ApplyTemplates(state.Templates);
		ApplyRoomList(state.Rooms);
		ApplyReconnectState(state.ReconnectTicket);
		ApplyBusyState(state.Busy);
		_statusLabel.Visible = !string.IsNullOrWhiteSpace(state.StatusMessage);
		_statusLabel.Text = state.StatusMessage;
		_statusLabel.Modulate = state.StatusIsError
			? new Color(1f, 0.58f, 0.58f, 1f)
			: new Color(0.72f, 0.92f, 0.76f, 1f);
	}

	public void RefreshTexts()
	{
		_titleLabel.Text = LocalizationService.T("ui.multiplayer.hub.title");
		_subtitleLabel.Text = LocalizationService.T("ui.multiplayer.hub.subtitle");
		_backButton.Text = LocalizationService.T("ui.multiplayer.back");
		_displayNameInput.PlaceholderText = LocalizationService.T("ui.multiplayer.config.display_name.placeholder");
		_lobbyBaseUrlInput.PlaceholderText = LocalizationService.T("ui.multiplayer.config.lobby_url.placeholder");
		_localServerPathInput.PlaceholderText = LocalizationService.T("ui.multiplayer.config.local_server_path.placeholder");
		_localLobbyPrefixInput.PlaceholderText = LocalizationService.T("ui.multiplayer.config.local_lobby_prefix.placeholder");
		_localGameAddressInput.PlaceholderText = LocalizationService.T("ui.multiplayer.config.local_game_address.placeholder");
		_roomCodeInput.PlaceholderText = LocalizationService.T("ui.multiplayer.rooms.join_code.placeholder");
		_roomDisplayNameInput.PlaceholderText = LocalizationService.T("ui.multiplayer.create.room_name.placeholder");
		_saveSettingsButton.Text = LocalizationService.T("ui.multiplayer.config.save");
		_reconnectButton.Text = LocalizationService.T("ui.multiplayer.reconnect.action");
		_refreshRoomsButton.Text = LocalizationService.T("ui.multiplayer.rooms.refresh");
		_joinSelectedButton.Text = LocalizationService.T("ui.multiplayer.rooms.join_selected");
		_joinByCodeButton.Text = LocalizationService.T("ui.multiplayer.rooms.join_code.action");
		_publicRoomCheck.Text = LocalizationService.T("ui.multiplayer.create.public");
		_createRoomButton.Text = LocalizationService.T("ui.multiplayer.create.action");
		PopulateHostModeOptions();
	}

	private void PopulateHostModeOptions()
	{
		var selected = _hostModeOption.Selected;
		_hostModeOption.Clear();
		_hostModeOption.AddItem(LocalizationService.T("ui.multiplayer.config.host_mode.local"));
		_hostModeOption.AddItem(LocalizationService.T("ui.multiplayer.config.host_mode.remote"));
		if (selected >= 0 && selected < _hostModeOption.ItemCount)
			_hostModeOption.Select(selected);
	}

	private void ApplySettings(MultiplayerSettings settings)
	{
		_displayNameInput.Text = settings.DisplayName;
		_lobbyBaseUrlInput.Text = settings.LobbyBaseUrl;
		_hostModeOption.Select(settings.PreferredHostMode == MultiplayerHostMode.Remote ? 1 : 0);
		_localServerPathInput.Text = settings.LocalServerExecutablePath;
		_localLobbyPrefixInput.Text = settings.LocalLobbyPrefix;
		_localGameAddressInput.Text = settings.LocalGameAddress;
		_localGamePortInput.Value = settings.LocalGamePort;
	}

	private void ApplyTemplates(IReadOnlyList<MultiplayerHubTemplateOption> templates)
	{
		var previousTemplateId = SelectedTemplateId;
		_templateIds.Clear();
		_templateOption.Clear();
		foreach (var template in templates)
		{
			_templateIds.Add(template.Id);
			_templateOption.AddItem(template.DisplayName);
		}

		var selectedIndex = 0;
		for (var i = 0; i < _templateIds.Count; i++)
		{
			if (!string.Equals(_templateIds[i], previousTemplateId, StringComparison.Ordinal))
				continue;

			selectedIndex = i;
			break;
		}

		if (_templateOption.ItemCount > 0)
			_templateOption.Select(selectedIndex);
	}

	private void ApplyRoomList(IReadOnlyList<LobbyRoomSummary> rooms)
	{
		var previousRoomId = SelectedRoomId;
		_roomIds.Clear();
		_roomList.Clear();
		for (var i = 0; i < rooms.Count; i++)
		{
			var room = rooms[i];
			_roomIds.Add(room.RoomId);
			_roomList.AddItem($"{room.RoomDisplayName} [{room.RoomCode}]  {room.PlayerCount}p");
		}

		if (!string.IsNullOrWhiteSpace(previousRoomId))
		{
			for (var i = 0; i < _roomIds.Count; i++)
			{
				if (!string.Equals(_roomIds[i], previousRoomId, StringComparison.Ordinal))
					continue;

				_roomList.Select(i);
				break;
			}
		}

		UpdateJoinSelectedButtonState();
	}

	private void ApplyReconnectState(MultiplayerReconnectTicket? ticket)
	{
		if (ticket == null)
		{
			_reconnectSummaryLabel.Text = LocalizationService.T("ui.multiplayer.reconnect.unavailable");
			_reconnectButton.Disabled = true;
			return;
		}

		var deadlineText = ticket.ReconnectDeadlineUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
			?? LocalizationService.T("ui.common.none");
		_reconnectSummaryLabel.Text = LocalizationService.T(
			"ui.multiplayer.reconnect.available",
			("room", string.IsNullOrWhiteSpace(ticket.RoomDisplayName) ? ticket.RoomCode : ticket.RoomDisplayName),
			("code", ticket.RoomCode),
			("deadline", deadlineText));
		_reconnectButton.Disabled = false;
	}

	private void ApplyBusyState(bool busy)
	{
		_displayNameInput.Editable = !busy;
		_lobbyBaseUrlInput.Editable = !busy;
		_hostModeOption.Disabled = busy;
		_localServerPathInput.Editable = !busy;
		_localLobbyPrefixInput.Editable = !busy;
		_localGameAddressInput.Editable = !busy;
		_localGamePortInput.Editable = !busy;
		_saveSettingsButton.Disabled = busy;
		_refreshRoomsButton.Disabled = busy;
		_roomList.Disabled = busy;
		_roomCodeInput.Editable = !busy;
		_joinByCodeButton.Disabled = busy;
		_roomDisplayNameInput.Editable = !busy;
		_templateOption.Disabled = busy;
		_publicRoomCheck.Disabled = busy;
		_createRoomButton.Disabled = busy;
		_reconnectButton.Disabled = busy || _state.ReconnectTicket == null;
		_backButton.Disabled = busy;
		UpdateJoinSelectedButtonState();
	}

	private MultiplayerSettings CaptureSettings() => new()
	{
		DisplayName = _displayNameInput.Text,
		LobbyBaseUrl = _lobbyBaseUrlInput.Text,
		PreferredHostMode = _hostModeOption.Selected == 1 ? MultiplayerHostMode.Remote : MultiplayerHostMode.Local,
		LocalServerExecutablePath = _localServerPathInput.Text,
		LocalLobbyPrefix = _localLobbyPrefixInput.Text,
		LocalGameAddress = _localGameAddressInput.Text,
		LocalGamePort = (int)Math.Round(_localGamePortInput.Value),
		LastReconnectTicket = _state.ReconnectTicket?.Clone(),
	};

	private void HandleJoinSelectedPressed()
	{
		var roomId = SelectedRoomId;
		if (string.IsNullOrWhiteSpace(roomId))
			return;

		JoinRoomRequested?.Invoke(new MultiplayerHubJoinRequest
		{
			Settings = CaptureSettings(),
			RoomId = roomId,
		});
	}

	private void HandleJoinByCodePressed()
	{
		var roomCode = _roomCodeInput.Text?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(roomCode))
			return;

		JoinByCodeRequested?.Invoke(new MultiplayerHubJoinCodeRequest
		{
			Settings = CaptureSettings(),
			RoomCode = roomCode,
		});
	}

	private void HandleCreatePressed()
	{
		var templateId = SelectedTemplateId;
		if (string.IsNullOrWhiteSpace(templateId))
			return;

		var roomDisplayName = _roomDisplayNameInput.Text?.Trim();
		if (string.IsNullOrWhiteSpace(roomDisplayName))
			roomDisplayName = LocalizationService.T("ui.multiplayer.create.default_room_name");

		CreateRoomRequested?.Invoke(new MultiplayerHubCreateRequest
		{
			Settings = CaptureSettings(),
			RoomDisplayName = roomDisplayName,
			TemplateId = templateId,
			IsPublic = _publicRoomCheck.ButtonPressed,
		});
	}

	private void UpdateJoinSelectedButtonState()
	{
		_joinSelectedButton.Disabled = _state.Busy || string.IsNullOrWhiteSpace(SelectedRoomId);
	}

	private string? SelectedRoomId
	{
		get
		{
			var selected = _roomList.GetSelectedItems();
			if (selected.Length == 0)
				return null;

			var index = selected[0];
			return index >= 0 && index < _roomIds.Count
				? _roomIds[index]
				: null;
		}
	}

	private string? SelectedTemplateId
	{
		get
		{
			var index = _templateOption.Selected;
			return index >= 0 && index < _templateIds.Count
				? _templateIds[index]
				: null;
		}
	}
}
