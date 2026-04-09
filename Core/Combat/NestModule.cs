using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.World;

namespace MiniRPG.Core.Combat;

/// <summary>
/// 巢穴模块：管理怪物刷新点的每回合刷怪逻辑。
/// 巢穴现在存储在 ChunkData.Nests 中，每回合遍历玩家附近已加载 chunk 的巢穴。
/// </summary>
public static class NestModule
{
	private static int _nestSpawnCounter;

	internal static void ResetSpawnCounter()
	{
		_nestSpawnCounter = 0;
	}

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
		var nearbyRadius = Math.Max(0, GameConfig.WorldRuntime.NestNearbyCountRadius);
		var r = state.World.Chunks.LoadRadiusXY;
		var coords = new HashSet<ChunkCoord>();
		foreach (var anchor in RoomRuntimeModule.GetWorldAnchors(state))
		{
			var playerChunk = CoordUtil.WorldToChunk(anchor.Position);
			for (var cy = playerChunk.Cy - r; cy <= playerChunk.Cy + r; cy++)
			for (var cx = playerChunk.Cx - r; cx <= playerChunk.Cx + r; cx++)
				coords.Add(new ChunkCoord(cx, cy, playerChunk.Cz));
		}

		foreach (var coord in coords)
		{
			if (!state.World.Chunks.IsLoaded(coord))
				continue;
			var chunk = state.World.Chunks.GetOrLoad(coord);

			foreach (var nest in chunk.Nests)
			{
				nest.TurnsSinceSpawn++;
				if (nest.TurnsSinceSpawn < nest.SpawnInterval) continue;

				var nearby = CountNearbyMonsters(state, nest.X, nest.Y, coord.Cz, nearbyRadius);
				if (nearby >= nest.MaxSpawned) continue;

				var slot = FindSpawnSlot(state, nest, coord.Cz, rng);
				if (slot is null) continue;

				var (sx, sy) = slot.Value;
				var templateId = !string.IsNullOrEmpty(nest.TemplateId)
					? nest.TemplateId
					: monsterTemplates[rng.Next(monsterTemplates.Length)];
				var monster = ActorTemplates.Spawn(templateId, $"nest_{_nestSpawnCounter++}");
				monster.X = sx;
				monster.Y = sy;
				monster.Z = coord.Cz;
				ActorModule.Add(state, monster);
				nest.TurnsSinceSpawn = 0;
				events.Add(new GameEvent("monster_spawned") { TargetX = sx, TargetY = sy, TargetZ = coord.Cz });
			}
		}

		return events;
	}

	private static (int, int)? FindSpawnSlot(GameState state, NestData nest, int z, Random rng)
	{
		var candidates = new List<(int, int)>();
		foreach (var (dx, dy) in GridDirections.Cardinal)
		{
			var nx = nest.X + dx;
			var ny = nest.Y + dy;
			if (MapModule.IsWalkable(state, nx, ny, z) && ActorModule.GetAllAt(state, nx, ny, z).Count == 0)
				candidates.Add((nx, ny));
		}
		return candidates.Count == 0 ? null : candidates[rng.Next(candidates.Count)];
	}

	private static int CountNearbyMonsters(GameState state, int cx, int cy, int z, int radius)
	{
		var count = 0;
		foreach (var actor in state.Actors.Values)
		{
			if (actor.Faction != Factions.Hostile) continue;
			if (actor.Z != z) continue;
			var adx = actor.X - cx;
			var ady = actor.Y - cy;
			if (adx >= -radius && adx <= radius && ady >= -radius && ady <= radius)
				count++;
		}
		return count;
	}
}
