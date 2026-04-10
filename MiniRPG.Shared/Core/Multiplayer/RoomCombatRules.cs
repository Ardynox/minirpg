using System;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Multiplayer;

public static class RoomCombatRules
{
	public static bool TryValidateAttack(
		GameState state,
		string attackerActorId,
		string targetActorId,
		out ErrorCode errorCode)
	{
		errorCode = ErrorCode.None;
		ArgumentNullException.ThrowIfNull(state);

		if (!state.Room.IsActive)
			return true;
		if (string.IsNullOrWhiteSpace(attackerActorId) || string.IsNullOrWhiteSpace(targetActorId))
			return true;

		var attackerOwner = RoomRuntimeModule.GetCurrentControllerPlayerId(state, attackerActorId);
		var targetOwner = RoomRuntimeModule.GetCurrentControllerPlayerId(state, targetActorId);
		if (string.IsNullOrWhiteSpace(attackerOwner) || string.IsNullOrWhiteSpace(targetOwner))
			return true;
		if (string.Equals(attackerOwner, targetOwner, StringComparison.Ordinal))
			return true;

		if (!state.Room.Rules.PvpEnabled)
		{
			errorCode = ErrorCode.PvpDisabled;
			return false;
		}

		if (!state.Room.Rules.FriendlyFire && IsSameTeam(state.Room, attackerOwner, targetOwner))
		{
			errorCode = ErrorCode.FriendlyFireDisabled;
			return false;
		}

		return true;
	}

	private static bool IsSameTeam(RoomRuntimeState room, string leftPlayerSessionId, string rightPlayerSessionId)
	{
		if (room.Rules.TeamMode == TeamMode.Shared)
			return true;
		if (room.Rules.TeamMode == TeamMode.Solo)
			return false;

		var leftTeam = ResolveTeamId(room, leftPlayerSessionId);
		var rightTeam = ResolveTeamId(room, rightPlayerSessionId);
		return string.Equals(leftTeam, rightTeam, StringComparison.Ordinal);
	}

	private static string ResolveTeamId(RoomRuntimeState room, string playerSessionId)
	{
		if (!room.Players.TryGetValue(playerSessionId, out var player))
			return playerSessionId;
		if (string.IsNullOrWhiteSpace(player.TeamId))
			return playerSessionId;

		return player.TeamId.Trim();
	}
}
