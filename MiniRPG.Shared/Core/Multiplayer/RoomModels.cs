using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.World;

namespace MiniRPG.Core.Multiplayer;

public static class MultiplayerDefaults
{
	public static readonly TimeSpan ReconnectGracePeriod = TimeSpan.FromMinutes(5);
	public static readonly TimeSpan InteractionReservationTimeout = TimeSpan.FromSeconds(15);
}

public enum TeamMode
{
	Solo,
	Shared,
	Manual,
}

public sealed class RoomRules
{
	public bool PvpEnabled { get; set; }
	public TeamMode TeamMode { get; set; } = TeamMode.Solo;
	public bool FriendlyFire { get; set; }

	public RoomRules Clone() => new()
	{
		PvpEnabled = PvpEnabled,
		TeamMode = TeamMode,
		FriendlyFire = FriendlyFire,
	};
}

public sealed class RoomPlayerState
{
	public string PlayerSessionId { get; set; } = string.Empty;
	public string DisplayName { get; set; } = string.Empty;
	public string PrimaryActorId { get; set; } = string.Empty;
	public string TeamId { get; set; } = string.Empty;
	public List<string> DelegatedActorIds { get; set; } = [];
	public List<string> CurrentControllerActorIds { get; set; } = [];
	public string JoinToken { get; set; } = string.Empty;
	public string ReconnectToken { get; set; } = string.Empty;
	public DateTimeOffset? ReconnectDeadlineUtc { get; set; }
	public bool Connected { get; set; }
	public bool IsRoomOwner { get; set; }

	public RoomPlayerState Clone() => new()
	{
		PlayerSessionId = PlayerSessionId,
		DisplayName = DisplayName,
		PrimaryActorId = PrimaryActorId,
		TeamId = TeamId,
		DelegatedActorIds = [.. DelegatedActorIds],
		CurrentControllerActorIds = [.. CurrentControllerActorIds],
		JoinToken = JoinToken,
		ReconnectToken = ReconnectToken,
		ReconnectDeadlineUtc = ReconnectDeadlineUtc,
		Connected = Connected,
		IsRoomOwner = IsRoomOwner,
	};
}

public sealed class ActorControlBinding
{
	public string PrimaryOwnerPlayerId { get; set; } = string.Empty;
	public string? TemporaryControllerPlayerId { get; set; }
	public bool CanBeDelegated { get; set; } = true;

	public string? ResolveControllerPlayerId() =>
		!string.IsNullOrWhiteSpace(TemporaryControllerPlayerId)
			? TemporaryControllerPlayerId
			: (!string.IsNullOrWhiteSpace(PrimaryOwnerPlayerId) ? PrimaryOwnerPlayerId : null);

	public ActorControlBinding Clone() => new()
	{
		PrimaryOwnerPlayerId = PrimaryOwnerPlayerId,
		TemporaryControllerPlayerId = TemporaryControllerPlayerId,
		CanBeDelegated = CanBeDelegated,
	};
}

public sealed class InteractionReservation
{
	public string ReservationKey { get; set; } = string.Empty;
	public string PlayerSessionId { get; set; } = string.Empty;
	public DateTimeOffset LastHeartbeatUtc { get; set; }
	public DateTimeOffset ExpiresAtUtc { get; set; }

	public bool IsExpired(DateTimeOffset now) => now >= ExpiresAtUtc;

	public InteractionReservation Clone() => new()
	{
		ReservationKey = ReservationKey,
		PlayerSessionId = PlayerSessionId,
		LastHeartbeatUtc = LastHeartbeatUtc,
		ExpiresAtUtc = ExpiresAtUtc,
	};
}

public enum RoomSimulationMode
{
	ExploreRealtime,
	CombatTurnBased,
}

public sealed class RoomModeTransitionRecord
{
	public long Sequence { get; set; }
	public string RequestId { get; set; } = string.Empty;
	public DateTimeOffset TransitionUtc { get; set; } = DateTimeOffset.UtcNow;
	public RoomSimulationMode FromMode { get; set; }
	public RoomSimulationMode ToMode { get; set; }
	public string Trigger { get; set; } = string.Empty;
	public string? TriggerActorId { get; set; }

	public RoomModeTransitionRecord Clone() => new()
	{
		Sequence = Sequence,
		RequestId = RequestId,
		TransitionUtc = TransitionUtc,
		FromMode = FromMode,
		ToMode = ToMode,
		Trigger = Trigger,
		TriggerActorId = TriggerActorId,
	};
}

public sealed class RoomRuntimeState
{
	public string RoomId { get; set; } = string.Empty;
	public string RoomCode { get; set; } = string.Empty;
	public RoomRules Rules { get; set; } = new();
	public Dictionary<string, RoomPlayerState> Players { get; set; } = new(StringComparer.Ordinal);
	public Dictionary<string, InteractionReservation> InteractionReservations { get; set; } = new(StringComparer.Ordinal);
	public Dictionary<string, ActorControlBinding> ActorControlBindings { get; set; } = new(StringComparer.Ordinal);
	public RoomSimulationMode SimulationMode { get; set; } = RoomSimulationMode.ExploreRealtime;
	public long LastSnapshotSequence { get; set; }
	public long LastModeTransitionSequence { get; set; }
	public List<RoomModeTransitionRecord> ModeTransitions { get; set; } = [];

	public bool IsActive =>
		!string.IsNullOrWhiteSpace(RoomId)
		|| !string.IsNullOrWhiteSpace(RoomCode)
		|| Players.Count > 0
		|| InteractionReservations.Count > 0
		|| ActorControlBindings.Count > 0;

	public RoomRuntimeState Clone() => new()
	{
		RoomId = RoomId,
		RoomCode = RoomCode,
		Rules = Rules.Clone(),
		SimulationMode = SimulationMode,
		LastSnapshotSequence = LastSnapshotSequence,
		LastModeTransitionSequence = LastModeTransitionSequence,
		ModeTransitions = [.. ModeTransitions.Select(static transition => transition.Clone())],
		Players = Players.ToDictionary(
			static entry => entry.Key,
			static entry => entry.Value.Clone(),
			StringComparer.Ordinal),
		InteractionReservations = InteractionReservations.ToDictionary(
			static entry => entry.Key,
			static entry => entry.Value.Clone(),
			StringComparer.Ordinal),
		ActorControlBindings = ActorControlBindings.ToDictionary(
			static entry => entry.Key,
			static entry => entry.Value.Clone(),
			StringComparer.Ordinal),
	};
}

public readonly record struct PlayerAnchor(
	string PlayerSessionId,
	string ActorId,
	WorldCoord Position,
	bool Connected);
