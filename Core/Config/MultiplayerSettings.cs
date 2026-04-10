using System;

namespace MiniRPG.Core.Config;

public enum MultiplayerHostMode
{
	Remote,
	Local,
}

public sealed class MultiplayerSettings
{
	public string DisplayName { get; init; } = "Player";
	public string LobbyBaseUrl { get; init; } = "http://127.0.0.1:5076/";
	public MultiplayerHostMode PreferredHostMode { get; init; } = MultiplayerHostMode.Local;
	public string LocalServerExecutablePath { get; init; } = string.Empty;
	public string LocalLobbyPrefix { get; init; } = "http://127.0.0.1:5076/";
	public string LocalGameAddress { get; init; } = "127.0.0.1";
	public int LocalGamePort { get; init; } = 2455;
	public MultiplayerReconnectTicket? LastReconnectTicket { get; init; }

	public MultiplayerSettings Clone() => new()
	{
		DisplayName = DisplayName,
		LobbyBaseUrl = LobbyBaseUrl,
		PreferredHostMode = PreferredHostMode,
		LocalServerExecutablePath = LocalServerExecutablePath,
		LocalLobbyPrefix = LocalLobbyPrefix,
		LocalGameAddress = LocalGameAddress,
		LocalGamePort = LocalGamePort,
		LastReconnectTicket = LastReconnectTicket?.Clone(),
	};
}

public sealed class MultiplayerReconnectTicket
{
	public string RoomId { get; init; } = string.Empty;
	public string RoomCode { get; init; } = string.Empty;
	public string RoomDisplayName { get; init; } = string.Empty;
	public string ServerEndpoint { get; init; } = string.Empty;
	public string PlayerSessionId { get; init; } = string.Empty;
	public string PrimaryActorId { get; init; } = string.Empty;
	public string ReconnectToken { get; init; } = string.Empty;
	public string DisplayName { get; init; } = string.Empty;
	public DateTimeOffset? ReconnectDeadlineUtc { get; init; }

	public bool IsExpired(DateTimeOffset now) =>
		ReconnectDeadlineUtc is { } deadline && now > deadline;

	public MultiplayerReconnectTicket Clone() => new()
	{
		RoomId = RoomId,
		RoomCode = RoomCode,
		RoomDisplayName = RoomDisplayName,
		ServerEndpoint = ServerEndpoint,
		PlayerSessionId = PlayerSessionId,
		PrimaryActorId = PrimaryActorId,
		ReconnectToken = ReconnectToken,
		DisplayName = DisplayName,
		ReconnectDeadlineUtc = ReconnectDeadlineUtc,
	};
}
