using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace MiniRPG;

internal readonly record struct RuntimeStatusPanelPlacementCandidate(
	string Key,
	bool Visible,
	Vector2 Position,
	int ZOrder
);

internal static class RuntimeStatusPanelPlacementPolicy
{
	private const float PositionEpsilon = 1f;

	public static bool IsInDefaultSlot(Vector2 position, Vector2 defaultPosition)
	{
		return Math.Abs(position.X - defaultPosition.X) <= PositionEpsilon
			&& Math.Abs(position.Y - defaultPosition.Y) <= PositionEpsilon;
	}

	public static string? ResolveReplaceableKey(
		IEnumerable<RuntimeStatusPanelPlacementCandidate> candidates,
		string? exceptKey,
		Vector2 defaultPosition)
	{
		return candidates
			.Where(candidate =>
				candidate.Visible
				&& !string.Equals(candidate.Key, exceptKey, StringComparison.Ordinal)
				&& IsInDefaultSlot(candidate.Position, defaultPosition))
			.OrderByDescending(candidate => candidate.ZOrder)
			.Select(candidate => candidate.Key)
			.FirstOrDefault();
	}
}
