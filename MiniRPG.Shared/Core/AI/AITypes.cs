using System;
using System.Collections.Generic;

namespace MiniRPG.Core.AI;

public enum SimDetail
{
	Full,
	Simplified,
	Summary,
}

public class Perception
{
	public required Actor Self { get; init; }
	public List<Actor> NearbyActors { get; init; } = [];
	public Dictionary<(int X, int Y), bool> NearbyWalkable { get; init; } = [];
	public Dictionary<(int X, int Y), string> NearbyFixtures { get; init; } = [];
	public int Turn { get; init; }
	public int Floor { get; init; }
	public GameState? State { get; init; }
}

public static class AIUtil
{
	public static int Distance3D(Actor a, Actor b) =>
		Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);

	public static int Distance3D(Actor a, int x, int y, int z) =>
		Math.Abs(a.X - x) + Math.Abs(a.Y - y) + Math.Abs(a.Z - z);

	public static bool IsAdjacent3D(Actor a, Actor b) =>
		Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z) == 1;
}

public static class FactionRelation
{
	public static bool IsHostile(string a, string b)
	{
		if (a == b) return false;
		return (a, b) switch
		{
			(Factions.Hostile, Factions.Player) or (Factions.Player, Factions.Hostile) => true,
			(Factions.Hostile, Factions.Friendly) or (Factions.Friendly, Factions.Hostile) => true,
			_ => false,
		};
	}
}
