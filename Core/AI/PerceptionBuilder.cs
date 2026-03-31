using System;
using System.Collections.Generic;

namespace MiniRPG.Core.AI;

/// <summary>
/// 从 GameState 构造不同精度的 Perception。
/// Full = 半径 5，Simplified = 半径 2。
/// 三维感知：同层 + Z 坐标过滤。
/// </summary>
public static class PerceptionBuilder
{
	private const int FullRange = 5;
	private const int SimplifiedRange = 2;

	public static Perception Build(GameState state, Actor self, SimDetail detail)
	{
		var range = detail == SimDetail.Full ? FullRange : SimplifiedRange;

		var nearbyActors = new List<Actor>();
		var nearbyWalkable = new Dictionary<(int, int), bool>();
		var nearbyFixtures = new Dictionary<(int, int), string>();

		for (var dy = -range; dy <= range; dy++)
		for (var dx = -range; dx <= range; dx++)
		{
			if (Math.Abs(dx) + Math.Abs(dy) > range) continue;

			var x = self.X + dx;
			var y = self.Y + dy;

			nearbyWalkable[(x, y)] = MapModule.IsWalkable(state, x, y);

			var fixture = MapModule.GetFixture(state, x, y);
			if (!string.IsNullOrEmpty(fixture))
				nearbyFixtures[(x, y)] = fixture;
		}

		foreach (var actor in state.Actors.Values)
		{
			if (actor.Id == self.Id) continue;
			if (actor.Z != self.Z) continue;
			var adx = Math.Abs(actor.X - self.X);
			var ady = Math.Abs(actor.Y - self.Y);
			if (adx + ady <= range)
				nearbyActors.Add(actor);
		}

		return new Perception
		{
			Self = self,
			NearbyActors = nearbyActors,
			NearbyWalkable = nearbyWalkable,
			NearbyFixtures = nearbyFixtures,
			Turn = state.Turn,
			Floor = self.Z,
		};
	}
}
