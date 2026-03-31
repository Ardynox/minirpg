using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.World;

namespace MiniRPG.Core;

/// <summary>
/// 巢穴模块：管理怪物刷新点的每回合刷怪逻辑。
/// 巢穴现在存储在 ChunkData.Nests 中，每回合遍历玩家附近已加载 chunk 的巢穴。
/// </summary>
public static class NestModule
{
	private static readonly (int Dx, int Dy)[] Dirs = [(0, -1), (0, 1), (-1, 0), (1, 0)];
	private static int _nestSpawnCounter;

	/// <summary>
	/// 每回合调用：遍历玩家附近已加载 chunk 中的巢穴，推进计时器，刷出怪物。
	/// </summary>
	public static List<GameEvent> Tick(GameState state)
	{
		var events = new List<GameEvent>();
		if (state.World == null) return events;

		var rng = new Random(state.WorldSeed + state.Turn);
		var monsterTemplates = ActorTemplates.MonsterIds.ToArray();
		if (monsterTemplates.Length == 0) return events;

		var playerChunk = CoordUtil.WorldToChunk(state.PlayerX, state.PlayerY, state.PlayerZ);
		var r = state.World.Chunks.LoadRadiusXY;

		for (var cy = playerChunk.Cy - r; cy <= playerChunk.Cy + r; cy++)
		for (var cx = playerChunk.Cx - r; cx <= playerChunk.Cx + r; cx++)
		{
			var coord = new ChunkCoord(cx, cy, state.PlayerZ);
			if (!state.World.Chunks.IsLoaded(coord)) continue;
			var chunk = state.World.Chunks.GetOrLoad(coord);

			foreach (var nest in chunk.Nests)
			{
				nest.TurnsSinceSpawn++;
				if (nest.TurnsSinceSpawn < nest.SpawnInterval) continue;

				var nearby = CountNearbyMonsters(state, nest.X, nest.Y, 3);
				if (nearby >= nest.MaxSpawned) continue;

				var slot = FindSpawnSlot(state, nest, rng);
				if (slot is null) continue;

				var (sx, sy) = slot.Value;
				var templateId = !string.IsNullOrEmpty(nest.TemplateId)
					? nest.TemplateId
					: monsterTemplates[rng.Next(monsterTemplates.Length)];
				var monster = ActorTemplates.Spawn(templateId, $"nest_{_nestSpawnCounter++}");
				monster.X = sx;
				monster.Y = sy;
				monster.Z = state.PlayerZ;
				ActorModule.Add(state, monster);
				nest.TurnsSinceSpawn = 0;
				events.Add(new GameEvent("monster_spawned") { TargetX = sx, TargetY = sy });
			}
		}

		return events;
	}

	private static (int, int)? FindSpawnSlot(GameState state, NestData nest, Random rng)
	{
		var candidates = new List<(int, int)>();
		foreach (var (dx, dy) in Dirs)
		{
			var nx = nest.X + dx;
			var ny = nest.Y + dy;
			if (MapModule.IsWalkable(state, nx, ny) && ActorModule.GetAllAt(state, nx, ny).Count == 0)
				candidates.Add((nx, ny));
		}
		return candidates.Count == 0 ? null : candidates[rng.Next(candidates.Count)];
	}

	private static int CountNearbyMonsters(GameState state, int cx, int cy, int radius)
	{
		var count = 0;
		foreach (var actor in state.Actors.Values)
		{
			if (actor.Faction != Factions.Hostile) continue;
			if (actor.Z != state.PlayerZ) continue;
			var adx = actor.X - cx;
			var ady = actor.Y - cy;
			if (adx >= -radius && adx <= radius && ady >= -radius && ady <= radius)
				count++;
		}
		return count;
	}
}
