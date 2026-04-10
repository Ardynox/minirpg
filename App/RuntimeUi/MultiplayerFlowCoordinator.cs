using System;
using MiniRPG.Core.Config;

namespace MiniRPG;

internal enum MultiplayerFlowState
{
	MainMenu,
	MultiplayerHub,
	StartingLocalServer,
	ConnectingLobby,
	JoiningRoom,
	LoadingRemoteSnapshot,
	InMultiplayerGame,
	DisconnectedRecoverable,
}

internal sealed class MultiplayerFlowCoordinator
{
	private readonly Func<MultiplayerSettings> _loadSettings;
	private readonly Action<MultiplayerSettings> _saveSettings;
	private readonly Action _showMainMenu;

	private MultiplayerSettings _settings;

	public MultiplayerFlowState State { get; private set; } = MultiplayerFlowState.MainMenu;
	public bool HubVisible => State is MultiplayerFlowState.MultiplayerHub or MultiplayerFlowState.DisconnectedRecoverable;

	public MultiplayerFlowCoordinator(
		Func<MultiplayerSettings> loadSettings,
		Action<MultiplayerSettings> saveSettings,
		Action showMainMenu)
	{
		_loadSettings = loadSettings;
		_saveSettings = saveSettings;
		_showMainMenu = showMainMenu;
		_settings = _loadSettings();
	}

	public MultiplayerSettings CurrentSettings => _settings.Clone();

	public void OpenHub()
	{
		_settings = _loadSettings();
		if (_settings.LastReconnectTicket != null && _settings.LastReconnectTicket.IsExpired(DateTimeOffset.UtcNow))
		{
			_settings = _settings.Clone();
			_settings = new MultiplayerSettings
			{
				DisplayName = _settings.DisplayName,
				LobbyBaseUrl = _settings.LobbyBaseUrl,
				PreferredHostMode = _settings.PreferredHostMode,
				LocalServerExecutablePath = _settings.LocalServerExecutablePath,
				LocalLobbyPrefix = _settings.LocalLobbyPrefix,
				LocalGameAddress = _settings.LocalGameAddress,
				LocalGamePort = _settings.LocalGamePort,
				LastReconnectTicket = null,
			};
			_saveSettings(_settings);
		}
		State = MultiplayerFlowState.MultiplayerHub;
	}

	public void BackToMainMenu()
	{
		State = MultiplayerFlowState.MainMenu;
		_showMainMenu();
	}

	public bool HasReconnectTicket()
	{
		var ticket = _settings.LastReconnectTicket;
		return ticket != null && !ticket.IsExpired(DateTimeOffset.UtcNow);
	}

	public void SaveSettings(MultiplayerSettings settings)
	{
		_settings = settings.Clone();
		_saveSettings(_settings);
	}

	public void SaveReconnectTicket(MultiplayerReconnectTicket? ticket)
	{
		var next = _settings.Clone();
		next = new MultiplayerSettings
		{
			DisplayName = next.DisplayName,
			LobbyBaseUrl = next.LobbyBaseUrl,
			PreferredHostMode = next.PreferredHostMode,
			LocalServerExecutablePath = next.LocalServerExecutablePath,
			LocalLobbyPrefix = next.LocalLobbyPrefix,
			LocalGameAddress = next.LocalGameAddress,
			LocalGamePort = next.LocalGamePort,
			LastReconnectTicket = ticket?.Clone(),
		};
		SaveSettings(next);
	}

	public void MarkDisconnectedRecoverable()
	{
		State = MultiplayerFlowState.DisconnectedRecoverable;
	}

	public void EnterMultiplayerGame()
	{
		State = MultiplayerFlowState.InMultiplayerGame;
	}
}
