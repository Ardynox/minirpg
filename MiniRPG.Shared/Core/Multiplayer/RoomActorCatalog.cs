using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;
using MiniRPG.Core.Map;

namespace MiniRPG.Core.Multiplayer;

public static class RoomActorCatalog
{
	public static IReadOnlyList<string> EnumerateAssignableActorIds(SaveFile? snapshot)
	{
		if (snapshot?.Payload == null)
			return Array.Empty<string>();

		var payload = snapshot.Payload;
		var candidates = new List<(string ActorId, int Priority)>();
		foreach (var actor in payload.Actors)
		{
			if (string.IsNullOrWhiteSpace(actor.Id))
				continue;

			var priority = GetPriority(payload, actor);
			if (priority < 0)
				continue;

			candidates.Add((actor.Id, priority));
		}

		return candidates
			.OrderBy(static candidate => candidate.Priority)
			.ThenBy(static candidate => candidate.ActorId, StringComparer.Ordinal)
			.Select(static candidate => candidate.ActorId)
			.Distinct(StringComparer.Ordinal)
			.ToArray();
	}

	private static int GetPriority(SavePayload payload, ActorSnapshot actor)
	{
		if (string.Equals(actor.Id, payload.PlayerId, StringComparison.Ordinal))
			return 0;
		if (string.Equals(actor.Faction, Factions.Player, StringComparison.Ordinal))
			return 1;
		if (string.Equals(actor.Faction, Factions.Friendly, StringComparison.Ordinal))
			return 2;

		return -1;
	}
}
