using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.World;

namespace MiniRPG.Core.Multiplayer;

public static class RoomRuntimeModule
{
	public const string SinglePlayerSessionId = "local";

	public static RoomPlayerState GetOrCreatePlayer(GameState state, string playerSessionId, string? displayName = null)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentException.ThrowIfNullOrWhiteSpace(playerSessionId);

		if (state.Room.Players.TryGetValue(playerSessionId, out var existing))
		{
			if (!string.IsNullOrWhiteSpace(displayName))
				existing.DisplayName = displayName!;
			return existing;
		}

		var created = new RoomPlayerState
		{
			PlayerSessionId = playerSessionId,
			DisplayName = displayName ?? playerSessionId,
			Connected = true,
		};
		state.Room.Players[playerSessionId] = created;
		return created;
	}

	public static ActorControlBinding GetOrCreateBinding(GameState state, string actorId)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

		if (state.Room.ActorControlBindings.TryGetValue(actorId, out var existing))
			return existing;

		var owner = state.Room.Players.Values
			.FirstOrDefault(player => string.Equals(player.PrimaryActorId, actorId, StringComparison.Ordinal))
			?.PlayerSessionId
			?? string.Empty;
		var created = new ActorControlBinding
		{
			PrimaryOwnerPlayerId = owner,
		};
		state.Room.ActorControlBindings[actorId] = created;
		return created;
	}

	public static void AssignPrimaryActor(
		GameState state,
		string playerSessionId,
		string actorId,
		bool canBeDelegated = true)
	{
		var player = GetOrCreatePlayer(state, playerSessionId);
		player.PrimaryActorId = actorId;

		var binding = GetOrCreateBinding(state, actorId);
		binding.PrimaryOwnerPlayerId = playerSessionId;
		binding.TemporaryControllerPlayerId = null;
		binding.CanBeDelegated = canBeDelegated;
		RefreshControlledActorIds(state);
	}

	public static bool DelegateActor(GameState state, string actorId, string controllerPlayerId)
	{
		var binding = GetOrCreateBinding(state, actorId);
		if (!binding.CanBeDelegated || string.IsNullOrWhiteSpace(binding.PrimaryOwnerPlayerId))
			return false;
		if (!state.Room.Players.ContainsKey(controllerPlayerId))
			return false;

		binding.TemporaryControllerPlayerId = controllerPlayerId;
		RefreshControlledActorIds(state);
		return true;
	}

	public static bool ReclaimPrimaryActor(GameState state, string actorId, string playerSessionId)
	{
		if (!state.Room.ActorControlBindings.TryGetValue(actorId, out var binding))
			return false;
		if (!string.Equals(binding.PrimaryOwnerPlayerId, playerSessionId, StringComparison.Ordinal))
			return false;

		binding.TemporaryControllerPlayerId = null;
		RefreshControlledActorIds(state);
		return true;
	}

	public static bool AssignPrimaryActorByHost(
		GameState state,
		string hostPlayerSessionId,
		string targetPlayerSessionId,
		string actorId)
	{
		if (!state.Room.IsActive)
			return false;
		if (!state.Room.Players.TryGetValue(hostPlayerSessionId, out var host) || !host.IsRoomOwner)
			return false;
		if (!state.Room.Players.ContainsKey(targetPlayerSessionId))
			return false;
		if (!state.Actors.ContainsKey(actorId))
			return false;

		foreach (var player in state.Room.Players.Values)
		{
			if (string.Equals(player.PrimaryActorId, actorId, StringComparison.Ordinal))
				player.PrimaryActorId = string.Empty;
		}

		AssignPrimaryActor(state, targetPlayerSessionId, actorId);
		return true;
	}

	public static bool KickPlayerByHost(GameState state, string hostPlayerSessionId, string targetPlayerSessionId)
	{
		if (!state.Room.IsActive)
			return false;
		if (string.Equals(hostPlayerSessionId, targetPlayerSessionId, StringComparison.Ordinal))
			return false;
		if (!state.Room.Players.TryGetValue(hostPlayerSessionId, out var host) || !host.IsRoomOwner)
			return false;
		if (!state.Room.Players.Remove(targetPlayerSessionId))
			return false;

		foreach (var binding in state.Room.ActorControlBindings.Values)
		{
			if (string.Equals(binding.PrimaryOwnerPlayerId, targetPlayerSessionId, StringComparison.Ordinal))
				binding.PrimaryOwnerPlayerId = string.Empty;
			if (string.Equals(binding.TemporaryControllerPlayerId, targetPlayerSessionId, StringComparison.Ordinal))
				binding.TemporaryControllerPlayerId = null;
		}

		RefreshControlledActorIds(state);
		return true;
	}

	public static void SetPlayerConnection(
		GameState state,
		string playerSessionId,
		bool connected,
		DateTimeOffset now,
		TimeSpan? reconnectGracePeriod = null)
	{
		if (!state.Room.Players.TryGetValue(playerSessionId, out var player))
			return;

		player.Connected = connected;
		player.ReconnectDeadlineUtc = connected
			? null
			: now + (reconnectGracePeriod ?? MultiplayerDefaults.ReconnectGracePeriod);

		if (!connected)
		{
			var fallbackController = FindFallbackControllerPlayerId(state, playerSessionId);
			if (!string.IsNullOrWhiteSpace(fallbackController))
			{
				foreach (var binding in state.Room.ActorControlBindings.Values)
				{
					if (!string.Equals(binding.PrimaryOwnerPlayerId, playerSessionId, StringComparison.Ordinal)
						|| !binding.CanBeDelegated
						|| !string.IsNullOrWhiteSpace(binding.TemporaryControllerPlayerId))
					{
						continue;
					}

					binding.TemporaryControllerPlayerId = fallbackController;
				}
			}
		}

		RefreshControlledActorIds(state);
	}

	public static bool TryReserveInteraction(
		GameState state,
		string reservationKey,
		string playerSessionId,
		DateTimeOffset now,
		out InteractionReservation? conflictingReservation,
		TimeSpan? timeout = null)
	{
		conflictingReservation = null;
		if (string.IsNullOrWhiteSpace(reservationKey))
			return false;

		CleanupExpiredReservations(state, now);
		if (state.Room.InteractionReservations.TryGetValue(reservationKey, out var existing))
		{
			if (!string.Equals(existing.PlayerSessionId, playerSessionId, StringComparison.Ordinal))
			{
				conflictingReservation = existing.Clone();
				return false;
			}

			existing.LastHeartbeatUtc = now;
			existing.ExpiresAtUtc = now + (timeout ?? MultiplayerDefaults.InteractionReservationTimeout);
			return true;
		}

		state.Room.InteractionReservations[reservationKey] = new InteractionReservation
		{
			ReservationKey = reservationKey,
			PlayerSessionId = playerSessionId,
			LastHeartbeatUtc = now,
			ExpiresAtUtc = now + (timeout ?? MultiplayerDefaults.InteractionReservationTimeout),
		};
		return true;
	}

	public static bool ReleaseInteraction(GameState state, string reservationKey, string? playerSessionId = null)
	{
		if (!state.Room.InteractionReservations.TryGetValue(reservationKey, out var existing))
			return false;
		if (!string.IsNullOrWhiteSpace(playerSessionId)
			&& !string.Equals(existing.PlayerSessionId, playerSessionId, StringComparison.Ordinal))
		{
			return false;
		}

		return state.Room.InteractionReservations.Remove(reservationKey);
	}

	public static void CleanupExpiredReservations(GameState state, DateTimeOffset now)
	{
		if (state.Room.InteractionReservations.Count == 0)
			return;

		var expiredKeys = new List<string>();
		foreach (var (key, reservation) in state.Room.InteractionReservations)
		{
			if (reservation.IsExpired(now))
				expiredKeys.Add(key);
		}

		foreach (var key in expiredKeys)
			state.Room.InteractionReservations.Remove(key);
	}

	public static IReadOnlyList<PlayerAnchor> GetWorldAnchors(GameState state, bool connectedOnly = true)
	{
		if (!state.Room.IsActive || state.Room.Players.Count == 0)
		{
			if (state.Actors.TryGetValue(state.PlayerId, out var playerActor))
			{
				return
				[
					new PlayerAnchor(
						SinglePlayerSessionId,
						playerActor.Id,
						new WorldCoord(playerActor.X, playerActor.Y, playerActor.Z),
						true),
				];
			}

			return
			[
				new PlayerAnchor(
					SinglePlayerSessionId,
					state.PlayerId,
					new WorldCoord(state.PlayerX, state.PlayerY, state.PlayerZ),
					true),
			];
		}

		var anchors = new List<PlayerAnchor>();
		foreach (var player in state.Room.Players.Values.OrderBy(static item => item.PlayerSessionId, StringComparer.Ordinal))
		{
			if (connectedOnly && !player.Connected)
				continue;

			foreach (var actorId in EnumerateControlledActorIds(state, player))
			{
				if (!state.Actors.TryGetValue(actorId, out var actor))
					continue;

				anchors.Add(new PlayerAnchor(
					player.PlayerSessionId,
					actor.Id,
					new WorldCoord(actor.X, actor.Y, actor.Z),
					player.Connected));
			}
		}

		if (anchors.Count > 0)
			return anchors;

		if (state.Actors.TryGetValue(state.PlayerId, out var fallbackActor))
		{
			return
			[
				new PlayerAnchor(
					SinglePlayerSessionId,
					fallbackActor.Id,
					new WorldCoord(fallbackActor.X, fallbackActor.Y, fallbackActor.Z),
					true),
			];
		}

		return
		[
			new PlayerAnchor(
				SinglePlayerSessionId,
				state.PlayerId,
				new WorldCoord(state.PlayerX, state.PlayerY, state.PlayerZ),
				true),
		];
	}

	public static IReadOnlyList<Actor> GetVisionActors(GameState state, bool connectedOnly = false)
	{
		var actors = new List<Actor>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var anchor in GetWorldAnchors(state, connectedOnly))
		{
			if (!state.Actors.TryGetValue(anchor.ActorId, out var actor))
				continue;
			if (seen.Add(actor.Id))
				actors.Add(actor);
		}

		return actors;
	}

	public static bool IsAuthorizedToControl(GameState state, string? playerSessionId, string? actorId)
	{
		if (string.IsNullOrWhiteSpace(playerSessionId) || string.IsNullOrWhiteSpace(actorId))
			return false;
		if (!state.Room.IsActive)
			return true;
		if (!state.Room.Players.TryGetValue(playerSessionId, out var player))
			return false;

		return player.CurrentControllerActorIds.Contains(actorId, StringComparer.Ordinal);
	}

	public static string? GetCurrentControllerPlayerId(GameState state, string actorId)
	{
		if (state.Room.ActorControlBindings.TryGetValue(actorId, out var binding))
			return binding.ResolveControllerPlayerId();

		return state.Room.Players.Values
			.FirstOrDefault(player => string.Equals(player.PrimaryActorId, actorId, StringComparison.Ordinal))
			?.PlayerSessionId;
	}

	public static void RefreshControlledActorIds(GameState state)
	{
		foreach (var player in state.Room.Players.Values)
			player.CurrentControllerActorIds.Clear();

		foreach (var (actorId, binding) in state.Room.ActorControlBindings)
		{
			var controllerPlayerId = binding.ResolveControllerPlayerId();
			if (string.IsNullOrWhiteSpace(controllerPlayerId))
				continue;
			if (!state.Room.Players.TryGetValue(controllerPlayerId, out var controller))
				continue;

			AddDistinct(controller.CurrentControllerActorIds, actorId);
		}

		foreach (var player in state.Room.Players.Values)
		{
			if (!string.IsNullOrWhiteSpace(player.PrimaryActorId)
				&& !state.Room.ActorControlBindings.ContainsKey(player.PrimaryActorId))
			{
				AddDistinct(player.CurrentControllerActorIds, player.PrimaryActorId);
			}

			foreach (var actorId in player.DelegatedActorIds)
				AddDistinct(player.CurrentControllerActorIds, actorId);
		}
	}

	public static void SyncLegacyPlayerAlias(GameState state)
	{
		if (!state.Actors.TryGetValue(state.PlayerId, out var player))
			return;

		state.PlayerX = player.X;
		state.PlayerY = player.Y;
		state.PlayerZ = player.Z;
	}

	private static IEnumerable<string> EnumerateControlledActorIds(GameState state, RoomPlayerState player)
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);

		if (!string.IsNullOrWhiteSpace(player.PrimaryActorId) && seen.Add(player.PrimaryActorId))
			yield return player.PrimaryActorId;

		foreach (var actorId in player.CurrentControllerActorIds)
		{
			if (!string.IsNullOrWhiteSpace(actorId) && seen.Add(actorId))
				yield return actorId;
		}

		foreach (var actorId in player.DelegatedActorIds)
		{
			if (!string.IsNullOrWhiteSpace(actorId) && seen.Add(actorId))
				yield return actorId;
		}

		foreach (var (actorId, binding) in state.Room.ActorControlBindings)
		{
			if (string.Equals(binding.ResolveControllerPlayerId(), player.PlayerSessionId, StringComparison.Ordinal)
				&& seen.Add(actorId))
			{
				yield return actorId;
			}
		}
	}

	private static string? FindFallbackControllerPlayerId(GameState state, string excludedPlayerId)
	{
		return state.Room.Players.Values
			.Where(player => player.Connected && !string.Equals(player.PlayerSessionId, excludedPlayerId, StringComparison.Ordinal))
			.OrderByDescending(static player => player.IsRoomOwner)
			.ThenBy(static player => player.PlayerSessionId, StringComparer.Ordinal)
			.Select(static player => player.PlayerSessionId)
			.FirstOrDefault();
	}

	private static void AddDistinct(List<string> values, string value)
	{
		if (!values.Contains(value, StringComparer.Ordinal))
			values.Add(value);
	}
}
