using System;

namespace MiniRPG.Module;

internal static class ReservationService
{
	internal static ServerActionResult? TryReserveInteractionTarget(
		GameState state,
		string? playerSessionId,
		string targetActorId,
		string? effectType,
		DateTimeOffset now)
	{
		if (string.Equals(effectType, "trade", StringComparison.OrdinalIgnoreCase))
			return TryReserveIfNeeded(state, playerSessionId, BuildTradeReservationKey(targetActorId), now);
		if (string.Equals(effectType, "talk", StringComparison.OrdinalIgnoreCase))
			return TryReserveIfNeeded(state, playerSessionId, BuildPrefixedReservationKey("dialog", targetActorId), now);

		return null;
	}

	internal static ServerActionResult? TryReserveIfNeeded(
		GameState state,
		string? playerSessionId,
		string reservationKey,
		DateTimeOffset now)
	{
		if (!state.Room.IsActive)
			return null;
		if (string.IsNullOrWhiteSpace(playerSessionId))
			return ServerActionResult.Reject("Missing player session id.", ErrorCode.MissingPlayerSession.ToWireCode());
		if (RoomRuntimeModule.TryReserveInteraction(state, reservationKey, playerSessionId, now, out var conflictingReservation))
			return null;

		return ServerActionResult.Busy(
			LocalizationService.TOrFallback(
				"log.server_action.reservation_busy",
				"Another player is already using {reservationKey}.",
				("reservationKey", reservationKey)),
			errorCode: ErrorCode.ReservationBusy.ToWireCode(),
			reservationKey: reservationKey,
			busyByPlayerSessionId: conflictingReservation?.PlayerSessionId);
	}

	internal static string BuildTradeReservationKey(string traderActorId) =>
		BuildPrefixedReservationKey("trade", traderActorId);

	internal static string BuildContainerReservationKey(ChestTakeClientCommand command) =>
		BuildContainerReservationKey(
			command.ContainerSource,
			command.ContainerInstanceId,
			command.ContainerOwnerActorId,
			command.ContainerX,
			command.ContainerY,
			command.ContainerZ);

	internal static string BuildContainerReservationKey(ChestTakeAllClientCommand command) =>
		BuildContainerReservationKey(
			command.ContainerSource,
			command.ContainerInstanceId,
			command.ContainerOwnerActorId,
			command.ContainerX,
			command.ContainerY,
			command.ContainerZ);

	internal static string BuildContainerReservationKey(ChestPutClientCommand command) =>
		BuildContainerReservationKey(
			command.ContainerSource,
			command.ContainerInstanceId,
			command.ContainerOwnerActorId,
			command.ContainerX,
			command.ContainerY,
			command.ContainerZ);

	internal static string BuildContainerReservationKey(
		ContainerSourceKind source,
		string containerInstanceId,
		string? ownerActorId,
		int x,
		int y,
		int z) => source switch
	{
		ContainerSourceKind.Inventory => $"container:inventory:{ownerActorId ?? string.Empty}:{containerInstanceId}",
		_ => $"container:ground:{x}:{y}:{z}:{containerInstanceId}",
	};

	internal static string BuildPrefixedReservationKey(string prefix, string rawId)
	{
		var normalizedId = rawId.Trim();
		if (normalizedId.Contains(':', StringComparison.Ordinal))
			return normalizedId;

		return $"{prefix}:{normalizedId}";
	}
}
