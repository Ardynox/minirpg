using System;
using MiniRPG.Core.Map;

namespace MiniRPG.Core.Multiplayer;

public sealed class HostedLobbyService : ILobbyService
{
	private readonly ILobbyService _inner;
	private readonly DedicatedGameServerHost _gameHost;

	public HostedLobbyService(ILobbyService inner, DedicatedGameServerHost gameHost)
	{
		_inner = inner ?? throw new ArgumentNullException(nameof(inner));
		_gameHost = gameHost ?? throw new ArgumentNullException(nameof(gameHost));
	}

	public LobbyJoinTicket CreateRoom(LobbyCreateRoomRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		var ticket = _inner.CreateRoom(request);
		if (_gameHost.TryGetRoom(ticket.RoomId, out _))
			return ticket;

		var initialState = CreateInitialState(request.InitialSnapshot);
		_gameHost.RegisterRoom(initialState, ticket.Room);
		return ticket;
	}

	public IReadOnlyList<LobbyRoomSummary> ListRooms() => _inner.ListRooms();

	public LobbyRoomResolution ResolveRoomCode(string roomCode) => _inner.ResolveRoomCode(roomCode);

	public LobbyJoinTicket JoinRoom(LobbyJoinRoomRequest request) => _inner.JoinRoom(request);

	public LobbyJoinTicket ReconnectClaim(LobbyReconnectClaimRequest request) => _inner.ReconnectClaim(request);

	public RoomRuntimeState GetRoomState(string roomId) => _inner.GetRoomState(roomId);

	public void UpdateRoomState(RoomRuntimeState room) => _inner.UpdateRoomState(room);

	private static GameState CreateInitialState(SaveFile? initialSnapshot)
		=> DedicatedGameServerHost.CreateStateFromSnapshot(initialSnapshot);
}
