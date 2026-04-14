using System;
using System.Collections.Generic;

namespace MiniRPG.Core.World;

public enum AutoNavigationStepKind
{
	Move,
	Climb,
}

public enum AutoNavigationFailureReason
{
	None,
	InvalidTarget,
	Unreachable,
	SearchLimitExceeded,
}

public readonly record struct AutoNavigationStep(
	AutoNavigationStepKind Kind,
	int Dx,
	int Dy,
	int Dz,
	int TargetX,
	int TargetY,
	int TargetZ);

public readonly record struct AutoNavigationPlanningResult(
	bool Success,
	bool ReachedTarget,
	AutoNavigationStep? Step,
	AutoNavigationFailureReason FailureReason)
{
	public static AutoNavigationPlanningResult Reached() =>
		new(
			Success: true,
			ReachedTarget: true,
			Step: null,
			FailureReason: AutoNavigationFailureReason.None);

	public static AutoNavigationPlanningResult Planned(AutoNavigationStep step) =>
		new(
			Success: true,
			ReachedTarget: false,
			Step: step,
			FailureReason: AutoNavigationFailureReason.None);

	public static AutoNavigationPlanningResult Failed(AutoNavigationFailureReason reason) =>
		new(
			Success: false,
			ReachedTarget: false,
			Step: null,
			FailureReason: reason);
}

public static class AutoNavigationPlanner
{
	private const int MaxSearchNodes = 8192;

	private static readonly (int Dx, int Dy)[] CardinalDirections =
	[
		(0, -1),
		(0, 1),
		(-1, 0),
		(1, 0),
	];

	public static AutoNavigationPlanningResult PlanNextStep(
		WorldMap world,
		int startX,
		int startY,
		int startZ,
		int targetX,
		int targetY,
		int targetZ)
	{
		ArgumentNullException.ThrowIfNull(world);

		var start = new AutoNavigationNode(startX, startY, startZ);
		var goal = new AutoNavigationNode(targetX, targetY, targetZ);
		if (start == goal)
			return AutoNavigationPlanningResult.Reached();

		if (!world.IsWalkable(goal.X, goal.Y, goal.Z))
			return AutoNavigationPlanningResult.Failed(AutoNavigationFailureReason.InvalidTarget);

		var open = new PriorityQueue<AutoNavigationNode, int>();
		var cameFrom = new Dictionary<AutoNavigationNode, AutoNavigationNode>();
		var gScore = new Dictionary<AutoNavigationNode, int>
		{
			[start] = 0,
		};
		var closed = new HashSet<AutoNavigationNode>();
		open.Enqueue(start, Heuristic(start, goal));

		var expandedNodes = 0;
		while (open.Count > 0)
		{
			var current = open.Dequeue();
			if (!closed.Add(current))
				continue;

			if (current == goal)
				return BuildPlanningResult(cameFrom, start, goal);

			if (++expandedNodes > MaxSearchNodes)
				return AutoNavigationPlanningResult.Failed(AutoNavigationFailureReason.SearchLimitExceeded);

			var currentG = gScore[current];
			foreach (var neighbor in EnumerateNeighbors(world, current))
			{
				if (closed.Contains(neighbor))
					continue;

				var tentativeG = currentG + 1;
				if (gScore.TryGetValue(neighbor, out var existingG) && tentativeG >= existingG)
					continue;

				cameFrom[neighbor] = current;
				gScore[neighbor] = tentativeG;
				open.Enqueue(neighbor, tentativeG + Heuristic(neighbor, goal));
			}
		}

		return AutoNavigationPlanningResult.Failed(AutoNavigationFailureReason.Unreachable);
	}

	private static IEnumerable<AutoNavigationNode> EnumerateNeighbors(WorldMap world, AutoNavigationNode current)
	{
		foreach (var (dx, dy) in CardinalDirections)
		{
			var nextX = current.X + dx;
			var nextY = current.Y + dy;
			if (world.IsWalkable(nextX, nextY, current.Z))
				yield return new AutoNavigationNode(nextX, nextY, current.Z);
		}

		if (ClimbingService.CanAutoClimb(world, current.X, current.Y, current.Z, -1))
			yield return new AutoNavigationNode(current.X, current.Y, current.Z - 1);
		if (ClimbingService.CanAutoClimb(world, current.X, current.Y, current.Z, +1))
			yield return new AutoNavigationNode(current.X, current.Y, current.Z + 1);
	}

	private static AutoNavigationPlanningResult BuildPlanningResult(
		Dictionary<AutoNavigationNode, AutoNavigationNode> cameFrom,
		AutoNavigationNode start,
		AutoNavigationNode goal)
	{
		var step = goal;
		while (cameFrom.TryGetValue(step, out var previous) && previous != start)
			step = previous;

		var dx = step.X - start.X;
		var dy = step.Y - start.Y;
		var dz = step.Z - start.Z;
		if (dz != 0 && dx == 0 && dy == 0)
		{
			return AutoNavigationPlanningResult.Planned(new AutoNavigationStep(
				AutoNavigationStepKind.Climb,
				Dx: 0,
				Dy: 0,
				Dz: dz,
				TargetX: step.X,
				TargetY: step.Y,
				TargetZ: step.Z));
		}

		if (step.Z == start.Z && Math.Abs(dx) + Math.Abs(dy) == 1)
		{
			return AutoNavigationPlanningResult.Planned(new AutoNavigationStep(
				AutoNavigationStepKind.Move,
				Dx: dx,
				Dy: dy,
				Dz: 0,
				TargetX: step.X,
				TargetY: step.Y,
				TargetZ: step.Z));
		}

		return AutoNavigationPlanningResult.Failed(AutoNavigationFailureReason.Unreachable);
	}

	private static int Heuristic(AutoNavigationNode from, AutoNavigationNode to) =>
		Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y) + Math.Abs(from.Z - to.Z);

	private readonly record struct AutoNavigationNode(int X, int Y, int Z);
}
