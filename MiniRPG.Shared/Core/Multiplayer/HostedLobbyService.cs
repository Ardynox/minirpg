using System;
using System.Linq;
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

		var resolvedRequest = ResolveCreateRoomRequest(request);
		var ticket = _inner.CreateRoom(resolvedRequest);
		if (_gameHost.TryGetRoom(ticket.RoomId, out _))
			return ticket;

		var initialState = CreateInitialState(resolvedRequest.InitialSnapshot);
		_gameHost.RegisterRoom(initialState, ticket.Room);
		return ticket;
	}

	public IReadOnlyList<LobbyRoomSummary> ListRooms() => _inner.ListRooms();

	public LobbyRoomResolution ResolveRoomCode(string roomCode) => _inner.ResolveRoomCode(roomCode);

	public LobbyJoinTicket JoinRoom(LobbyJoinRoomRequest request)
	{
		var ticket = _inner.JoinRoom(request);
		if (_gameHost.TryGetRoom(ticket.RoomId, out var roomHost))
			roomHost.AppendLifecycleAudit("join", ticket.PlayerSessionId, ticket.PrimaryActorId, result: "ok", code: null);
		return ticket;
	}

	public LobbyJoinTicket ReconnectClaim(LobbyReconnectClaimRequest request)
	{
		var ticket = _inner.ReconnectClaim(request);
		if (_gameHost.TryGetRoom(ticket.RoomId, out var roomHost))
			roomHost.AppendLifecycleAudit("reconnect", ticket.PlayerSessionId, ticket.PrimaryActorId, result: "ok", code: null);
		return ticket;
	}

	public LobbyLeaveRoomResult LeaveRoom(LobbyLeaveRoomRequest request)
	{
		var result = _inner.LeaveRoom(request);
		if (result.Ok && _gameHost.TryGetRoom(result.RoomId, out var roomHost))
			roomHost.AppendLifecycleAudit("leave", result.PlayerSessionId, result: "ok", code: null);
		return result;
	}

	public RoomRuntimeState GetRoomState(string roomId) => _inner.GetRoomState(roomId);

	public void UpdateRoomState(RoomRuntimeState room) => _inner.UpdateRoomState(room);

	private static LobbyCreateRoomRequest ResolveCreateRoomRequest(LobbyCreateRoomRequest request)
	{
		var resolvedSnapshot = request.InitialSnapshot;
		if (!string.IsNullOrWhiteSpace(request.TemplateId))
		{
			if (!PresetScenarioCatalog.TryGet(request.TemplateId, out var scenario))
				throw new InvalidOperationException($"Unknown preset scenario: {request.TemplateId}");

			resolvedSnapshot = SaveModule.DeserializeSaveFile(PresetScenarioCatalog.ReadText(scenario.TemplatePath))
				?? throw new InvalidOperationException($"Failed to deserialize preset scenario '{request.TemplateId}'.");
		}

		var resolvedPrimaryActorId = request.PrimaryActorId;
		if (string.IsNullOrWhiteSpace(resolvedPrimaryActorId) && resolvedSnapshot != null)
		{
			resolvedPrimaryActorId = RoomActorCatalog.EnumerateAssignableActorIds(resolvedSnapshot).FirstOrDefault()
				?? resolvedSnapshot.Payload.PlayerId;
		}

		return new LobbyCreateRoomRequest
		{
			RoomDisplayName = request.RoomDisplayName,
			OwnerDisplayName = request.OwnerDisplayName,
			ServerEndpoint = request.ServerEndpoint,
			PrimaryActorId = resolvedPrimaryActorId ?? string.Empty,
			RequestedRoomCode = request.RequestedRoomCode,
			IsPublic = request.IsPublic,
			TemplateId = request.TemplateId,
			InitialSnapshot = resolvedSnapshot,
			PvpEnabled = request.PvpEnabled,
			TeamMode = request.TeamMode,
			FriendlyFire = request.FriendlyFire,
		};
	}

	private static GameState CreateInitialState(SaveFile? initialSnapshot)
		=> DedicatedGameServerHost.CreateStateFromSnapshot(initialSnapshot);
}
