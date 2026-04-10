using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;

namespace MiniRPG.Core.Health;

public static class RoomContextAnalyzer
{
	private static readonly (int X, int Y)[] Dirs =
	[
		(0, -1),
		(0, 1),
		(-1, 0),
		(1, 0),
	];

	public static bool IsIndoors(GameState state, int x, int y, int z, int maxCells = 48, int maxRadius = 6) =>
		Analyze(state, x, y, z, maxCells, maxRadius).IsIndoors;

	public static RoomSnapshot AnalyzeRoom(GameState state, int x, int y, int z, int maxCells = 48, int maxRadius = 6)
	{
		if (state.World == null || state.World.IsSolid(x, y, z))
			return CreateExposedRoom(z, x, y);

		var visited = new HashSet<(int X, int Y)>();
		var queue = new Queue<(int X, int Y, int Distance)>();
		queue.Enqueue((x, y, 0));
		visited.Add((x, y));

		while (queue.Count > 0)
		{
			var current = queue.Dequeue();
			if (current.Distance > maxRadius || visited.Count > maxCells)
				return CreateExposedRoom(z, x, y);

			foreach (var dir in Dirs)
			{
				var nx = current.X + dir.X;
				var ny = current.Y + dir.Y;
				if (!visited.Add((nx, ny)))
					continue;
				if (state.World.IsSolid(nx, ny, z))
					continue;

				queue.Enqueue((nx, ny, current.Distance + 1));
			}
		}

		var cells = visited
			.Select(static cell => new ZoneCell(cell.X, cell.Y, 0))
			.ToList();
		for (var i = 0; i < cells.Count; i++)
			cells[i] = cells[i] with { Z = z };
		cells.Sort(static (left, right) =>
		{
			var zCompare = left.Z.CompareTo(right.Z);
			if (zCompare != 0)
				return zCompare;
			var yCompare = left.Y.CompareTo(right.Y);
			if (yCompare != 0)
				return yCompare;
			return left.X.CompareTo(right.X);
		});

		var cellCount = cells.Count;
		var shelterStrength = Math.Clamp(1f - (cellCount - 1f) / Math.Max(1f, maxCells - 1f), 0.35f, 1.0f);
		var facilities = ResolveFacilities(state, cells);
		var primaryRole = ResolvePrimaryRole(facilities);

		return new RoomSnapshot
		{
			Id = $"room_{z}_{x}_{y}",
			Z = z,
			IsIndoors = true,
			CellCount = cellCount,
			ShelterStrength = shelterStrength,
			PrimaryRoleId = primaryRole?.Id ?? string.Empty,
			Modifiers = primaryRole?.Modifiers ?? RoomModifierSet.Neutral,
			Cells = cells,
		};
	}

	public static RoomContextSnapshot Analyze(GameState state, int x, int y, int z, int maxCells = 48, int maxRadius = 6)
	{
		var room = AnalyzeRoom(state, x, y, z, maxCells, maxRadius);
		return new RoomContextSnapshot(
			IsIndoors: room.IsIndoors,
			CellCount: room.CellCount,
			ShelterStrength: room.ShelterStrength);
	}

	private static RoomSnapshot CreateExposedRoom(int z, int x, int y) => new()
	{
		Id = $"room_{z}_{x}_{y}",
		Z = z,
		IsIndoors = false,
		CellCount = 0,
		ShelterStrength = 0f,
		PrimaryRoleId = string.Empty,
		Modifiers = RoomModifierSet.Neutral,
		Cells = [],
	};

	private static List<FacilityInstance> ResolveFacilities(GameState state, IEnumerable<ZoneCell> cells)
	{
		if (state.World == null || state.Facilities.Count == 0)
			return [];

		var result = new List<FacilityInstance>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var cell in cells)
		{
			if (!state.World.TryGetFacilityAt(cell.X, cell.Y, cell.Z, out var facility) || facility == null)
				continue;
			if (seen.Add(facility.Id))
				result.Add(facility);
		}

		return result;
	}

	private static RoomRoleDef? ResolvePrimaryRole(IEnumerable<FacilityInstance> facilities)
	{
		var scored = RoomRoleRegistry.All.Values
			.Select(role => new
			{
				Role = role,
				Score = ScoreRole(role, facilities),
			})
			.Where(entry => entry.Score >= entry.Role.MinimumScore)
			.OrderByDescending(static entry => entry.Score)
			.ThenByDescending(static entry => entry.Role.MinimumScore)
			.ThenBy(static entry => entry.Role.Id, StringComparer.Ordinal)
			.FirstOrDefault();

		return scored?.Role;
	}

	private static int ScoreRole(RoomRoleDef role, IEnumerable<FacilityInstance> facilities)
	{
		var score = 0;
		foreach (var facility in facilities)
		{
			var def = FacilityRegistry.Get(facility.FacilityDefId);
			if (def == null)
				continue;

			if (role.FacilityIds.Contains(def.Id, StringComparer.Ordinal))
				score++;

			if (def.RoomTags.Contains(role.Id, StringComparer.Ordinal))
				score++;

			score += role.FacilityTags.Count(tag =>
				def.Tags.Contains(tag, StringComparer.Ordinal)
				|| def.RoomTags.Contains(tag, StringComparer.Ordinal));
		}

		return score;
	}
}
