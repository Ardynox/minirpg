using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MiniRPG.Core.Config;
using MiniRPG.Core.Map;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Core.Session;
using MiniRPG.Module;
using MiniRPG.Module.Network;

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

internal sealed class MultiplayerConnectResult
{
	public bool Success { get; init; }
	public string? FailureReason { get; init; }
	public MultiplayerSessionBackend? Backend { get; init; }
	public LobbyJoinTicket? JoinTicket { get; init; }
	public RoomSnapshotMessage? InitialSnapshot { get; init; }

	public static MultiplayerConnectResult Ok(
		MultiplayerSessionBackend backend,
		LobbyJoinTicket joinTicket,
		RoomSnapshotMessage initialSnapshot) => new()
		{
			Success = true,
			Backend = backend,
			JoinTicket = joinTicket,
			InitialSnapshot = initialSnapshot,
		};

	public static MultiplayerConnectResult Fail(string reason) => new()
	{
		Success = false,
		FailureReason = reason,
	};
}

internal sealed class MultiplayerSnapshotRoomCreateRequest
{
	public MultiplayerSettings Settings { get; init; } = new();
	public string RoomDisplayName { get; init; } = string.Empty;
	public bool IsPublic { get; init; }
	public SaveFile Snapshot { get; init; } = null!;
	public string PrimaryActorId { get; init; } = string.Empty;
}

internal sealed class MultiplayerFlowCoordinator : IAsyncDisposable
{
	private readonly Func<MultiplayerSettings> _loadSettings;
	private readonly Action<MultiplayerSettings> _saveSettings;
	private readonly Action _showMainMenu;
	private readonly ILocalServerLauncher _localServerLauncher;
	private readonly Func<string, ILobbyClient> _lobbyClientFactory;
	private readonly Func<MultiplayerSessionBackend> _sessionBackendFactory;

	private MultiplayerSettings _settings;

	public MultiplayerFlowState State { get; private set; } = MultiplayerFlowState.MainMenu;
	public bool HubVisible => State is MultiplayerFlowState.MultiplayerHub or MultiplayerFlowState.DisconnectedRecoverable;

	public MultiplayerFlowCoordinator(
		Func<MultiplayerSettings> loadSettings,
		Action<MultiplayerSettings> saveSettings,
		Action showMainMenu,
		ILocalServerLauncher localServerLauncher,
		Func<string, ILobbyClient> lobbyClientFactory,
		Func<MultiplayerSessionBackend> sessionBackendFactory)
	{
		_loadSettings = loadSettings;
		_saveSettings = saveSettings;
		_showMainMenu = showMainMenu;
		_localServerLauncher = localServerLauncher;
		_lobbyClientFactory = lobbyClientFactory;
		_sessionBackendFactory = sessionBackendFactory;
		_settings = _loadSettings();
	}

	public MultiplayerSettings CurrentSettings => _settings.Clone();

	public void OpenHub()
	{
		_settings = SanitizeReconnectTicket(_loadSettings());
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

	public MultiplayerReconnectTicket? GetReconnectTicket()
	{
		var ticket = _settings.LastReconnectTicket;
		return ticket != null && !ticket.IsExpired(DateTimeOffset.UtcNow)
			? ticket.Clone()
			: null;
	}

	public void SaveSettings(MultiplayerSettings settings)
	{
		_settings = settings.Clone();
		_saveSettings(_settings);
	}

	public MultiplayerHubViewState BuildHubViewState(
		IReadOnlyList<MultiplayerHubTemplateOption> templates,
		IReadOnlyList<LobbyRoomSummary>? rooms = null,
		bool busy = false,
		string? statusMessage = null,
		bool statusIsError = false)
	{
		return new MultiplayerHubViewState
		{
			Settings = _settings.Clone(),
			Rooms = rooms ?? Array.Empty<LobbyRoomSummary>(),
			Templates = templates,
			ReconnectTicket = GetReconnectTicket(),
			Busy = busy,
			StatusMessage = statusMessage ?? string.Empty,
			StatusIsError = statusIsError,
		};
	}

	public async Task<IReadOnlyList<LobbyRoomSummary>> RefreshRoomsAsync(
		MultiplayerSettings settings,
		CancellationToken cancellationToken = default)
	{
		_settings = settings.Clone();
		SaveSettings(_settings);
		State = MultiplayerFlowState.ConnectingLobby;
		try
		{
			using var lobby = _lobbyClientFactory(_settings.LobbyBaseUrl);
			var rooms = await lobby.ListRoomsAsync(cancellationToken).ConfigureAwait(false);
			State = MultiplayerFlowState.MultiplayerHub;
			return rooms;
		}
		catch
		{
			State = MultiplayerFlowState.MultiplayerHub;
			throw;
		}
	}

	public async Task<MultiplayerConnectResult> CreateTemplateRoomAsync(
		MultiplayerHubCreateRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		var settings = request.Settings.Clone();
		SaveSettings(settings);

		try
		{
			var lobbyBaseUrl = await EnsureLobbyReadyAsync(settings, cancellationToken).ConfigureAwait(false);
			State = MultiplayerFlowState.ConnectingLobby;
			using var lobby = _lobbyClientFactory(lobbyBaseUrl);
			var joinTicket = await lobby.CreateRoomAsync(new LobbyCreateRoomRequest
			{
				RoomDisplayName = request.RoomDisplayName,
				OwnerDisplayName = settings.DisplayName,
				ServerEndpoint = BuildCreateServerEndpoint(settings, lobbyBaseUrl),
				PrimaryActorId = string.Empty,
				IsPublic = request.IsPublic,
				TemplateId = request.TemplateId,
			}, cancellationToken).ConfigureAwait(false);
			return await ConnectToRoomAsync(joinTicket, lobbyBaseUrl, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			State = MultiplayerFlowState.MultiplayerHub;
			return MultiplayerConnectResult.Fail(ex.Message);
		}
	}

	public async Task<MultiplayerConnectResult> CreateSnapshotRoomAsync(
		MultiplayerSnapshotRoomCreateRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(request.Snapshot);
		var settings = request.Settings.Clone();
		SaveSettings(settings);

		try
		{
			var lobbyBaseUrl = await EnsureLobbyReadyAsync(settings, cancellationToken).ConfigureAwait(false);
			State = MultiplayerFlowState.ConnectingLobby;
			using var lobby = _lobbyClientFactory(lobbyBaseUrl);
			var joinTicket = await lobby.CreateRoomAsync(new LobbyCreateRoomRequest
			{
				RoomDisplayName = request.RoomDisplayName,
				OwnerDisplayName = settings.DisplayName,
				ServerEndpoint = BuildCreateServerEndpoint(settings, lobbyBaseUrl),
				PrimaryActorId = string.IsNullOrWhiteSpace(request.PrimaryActorId)
					? request.Snapshot.Payload.PlayerId
					: request.PrimaryActorId,
				IsPublic = request.IsPublic,
				InitialSnapshot = request.Snapshot,
			}, cancellationToken).ConfigureAwait(false);
			return await ConnectToRoomAsync(joinTicket, lobbyBaseUrl, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			State = MultiplayerFlowState.MultiplayerHub;
			return MultiplayerConnectResult.Fail(ex.Message);
		}
	}

	public async Task<MultiplayerConnectResult> JoinRoomAsync(
		MultiplayerHubJoinRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		var settings = request.Settings.Clone();
		SaveSettings(settings);

		try
		{
			State = MultiplayerFlowState.ConnectingLobby;
			using var lobby = _lobbyClientFactory(settings.LobbyBaseUrl);
			var joinTicket = await lobby.JoinRoomAsync(new LobbyJoinRoomRequest
			{
				RoomId = request.RoomId,
				DisplayName = settings.DisplayName,
				PrimaryActorId = string.Empty,
			}, cancellationToken).ConfigureAwait(false);
			return await ConnectToRoomAsync(joinTicket, settings.LobbyBaseUrl, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			State = MultiplayerFlowState.MultiplayerHub;
			return MultiplayerConnectResult.Fail(ex.Message);
		}
	}

	public async Task<MultiplayerConnectResult> JoinByCodeAsync(
		MultiplayerHubJoinCodeRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		var settings = request.Settings.Clone();
		SaveSettings(settings);

		try
		{
			State = MultiplayerFlowState.ConnectingLobby;
			using var lobby = _lobbyClientFactory(settings.LobbyBaseUrl);
			var resolution = await lobby.ResolveRoomCodeAsync(request.RoomCode, cancellationToken).ConfigureAwait(false);
			var joinTicket = await lobby.JoinRoomAsync(new LobbyJoinRoomRequest
			{
				RoomId = resolution.RoomId,
				DisplayName = settings.DisplayName,
				PrimaryActorId = string.Empty,
			}, cancellationToken).ConfigureAwait(false);
			return await ConnectToRoomAsync(joinTicket, settings.LobbyBaseUrl, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			State = MultiplayerFlowState.MultiplayerHub;
			return MultiplayerConnectResult.Fail(ex.Message);
		}
	}

	public async Task<MultiplayerConnectResult> ReconnectAsync(CancellationToken cancellationToken = default)
	{
		var reconnectTicket = GetReconnectTicket();
		if (reconnectTicket == null)
			return MultiplayerConnectResult.Fail("No reconnect ticket is available.");

		try
		{
			State = MultiplayerFlowState.ConnectingLobby;
			using var lobby = _lobbyClientFactory(reconnectTicket.LobbyBaseUrl);
			var joinTicket = await lobby.ReconnectClaimAsync(new LobbyReconnectClaimRequest
			{
				RoomId = reconnectTicket.RoomId,
				ReconnectToken = reconnectTicket.ReconnectToken,
			}, cancellationToken).ConfigureAwait(false);
			return await ConnectToRoomAsync(joinTicket, reconnectTicket.LobbyBaseUrl, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			ClearReconnectTicket();
			State = MultiplayerFlowState.MultiplayerHub;
			return MultiplayerConnectResult.Fail(ex.Message);
		}
	}

	public void EnterMultiplayerGame()
	{
		State = MultiplayerFlowState.InMultiplayerGame;
	}

	public void SaveReconnectTicket(
		LobbyJoinTicket joinTicket,
		string lobbyBaseUrl,
		DateTimeOffset? reconnectDeadlineUtc = null)
	{
		ArgumentNullException.ThrowIfNull(joinTicket);

		var ticket = new MultiplayerReconnectTicket
		{
			LobbyBaseUrl = lobbyBaseUrl,
			RoomId = joinTicket.RoomId,
			RoomCode = joinTicket.RoomCode,
			RoomDisplayName = joinTicket.RoomDisplayName,
			ServerEndpoint = joinTicket.ServerEndpoint,
			PlayerSessionId = joinTicket.PlayerSessionId,
			PrimaryActorId = joinTicket.PrimaryActorId,
			ReconnectToken = joinTicket.ReconnectToken,
			DisplayName = joinTicket.DisplayName,
			ReconnectDeadlineUtc = reconnectDeadlineUtc,
		};

		var next = _settings.Clone();
		next = next.WithReconnect(ticket);
		SaveSettings(next);
	}

	public void MarkDisconnectedRecoverable(string reason)
	{
		var current = GetReconnectTicket();
		if (current != null)
		{
			var next = _settings.Clone();
			next = next.WithReconnect(new MultiplayerReconnectTicket
			{
				LobbyBaseUrl = current.LobbyBaseUrl,
				RoomId = current.RoomId,
				RoomCode = current.RoomCode,
				RoomDisplayName = current.RoomDisplayName,
				ServerEndpoint = current.ServerEndpoint,
				PlayerSessionId = current.PlayerSessionId,
				PrimaryActorId = current.PrimaryActorId,
				ReconnectToken = current.ReconnectToken,
				DisplayName = current.DisplayName,
				ReconnectDeadlineUtc = DateTimeOffset.UtcNow + MultiplayerDefaults.ReconnectGracePeriod,
			});
			SaveSettings(next);
		}

		State = MultiplayerFlowState.DisconnectedRecoverable;
	}

	public void ClearReconnectTicket()
	{
		var next = _settings.Clone();
		next = next.WithReconnect(null);
		SaveSettings(next);
	}

	public async ValueTask DisposeAsync() => await _localServerLauncher.DisposeAsync();

	private async Task<string> EnsureLobbyReadyAsync(MultiplayerSettings settings, CancellationToken cancellationToken)
	{
		if (settings.PreferredHostMode != MultiplayerHostMode.Local)
			return settings.LobbyBaseUrl;

		State = MultiplayerFlowState.StartingLocalServer;
		var launchResult = await _localServerLauncher.EnsureRunningAsync(new LocalServerLaunchOptions
		{
			ExecutablePath = settings.LocalServerExecutablePath,
			LobbyBaseUrl = settings.LocalLobbyPrefix,
			GameAddress = settings.LocalGameAddress,
			GamePort = settings.LocalGamePort,
		}, cancellationToken).ConfigureAwait(false);
		if (!launchResult.Success)
			throw new InvalidOperationException(launchResult.FailureReason ?? "Failed to start local server.");

		return launchResult.LobbyBaseUrl;
	}

	private async Task<MultiplayerConnectResult> ConnectToRoomAsync(
		LobbyJoinTicket joinTicket,
		string lobbyBaseUrl,
		CancellationToken cancellationToken)
	{
		State = MultiplayerFlowState.JoiningRoom;
		var backend = _sessionBackendFactory();
		var connectResult = await backend.ConnectAsync(new MultiplayerSessionConnectRequest
		{
			ServerEndpoint = joinTicket.ServerEndpoint,
			RoomId = joinTicket.RoomId,
			Token = joinTicket.JoinToken,
		}, cancellationToken).ConfigureAwait(false);
		if (!connectResult.Success || connectResult.InitialSnapshot == null)
		{
			await backend.DisposeAsync().ConfigureAwait(false);
			State = MultiplayerFlowState.MultiplayerHub;
			return MultiplayerConnectResult.Fail(connectResult.FailureReason ?? "Failed to connect to room.");
		}

		State = MultiplayerFlowState.LoadingRemoteSnapshot;
		SaveReconnectTicket(joinTicket, lobbyBaseUrl);
		return MultiplayerConnectResult.Ok(backend, joinTicket, connectResult.InitialSnapshot);
	}

	private MultiplayerSettings SanitizeReconnectTicket(MultiplayerSettings settings)
	{
		var ticket = settings.LastReconnectTicket;
		if (ticket == null || !ticket.IsExpired(DateTimeOffset.UtcNow))
			return settings;

		var next = settings.Clone();
		next = next.WithReconnect(null);
		_saveSettings(next);
		return next;
	}

	private static string BuildCreateServerEndpoint(MultiplayerSettings settings, string lobbyBaseUrl)
	{
		if (settings.PreferredHostMode == MultiplayerHostMode.Local)
			return $"enet://{settings.LocalGameAddress}:{settings.LocalGamePort}";

		if (Uri.TryCreate(lobbyBaseUrl, UriKind.Absolute, out var uri))
			return $"enet://{uri.Host}:{settings.LocalGamePort}";

		return $"enet://{settings.LocalGameAddress}:{settings.LocalGamePort}";
	}
}

file static class MultiplayerSettingsExtensions
{
	public static MultiplayerSettings WithReconnect(this MultiplayerSettings settings, MultiplayerReconnectTicket? ticket) => new()
	{
		DisplayName = settings.DisplayName,
		LobbyBaseUrl = settings.LobbyBaseUrl,
		PreferredHostMode = settings.PreferredHostMode,
		LocalServerExecutablePath = settings.LocalServerExecutablePath,
		LocalLobbyPrefix = settings.LocalLobbyPrefix,
		LocalGameAddress = settings.LocalGameAddress,
		LocalGamePort = settings.LocalGamePort,
		LastReconnectTicket = ticket?.Clone(),
	};
}
