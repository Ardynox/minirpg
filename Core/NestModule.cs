using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniRPG.Core;

/// <summary>
/// 巢穴模块：地图上的 "N" 标记是怪物刷新点。
/// 每回合调用 Tick，到达间隔后在巢穴周围空格刷出 "M"。
/// </summary>
public static class NestModule
{
	private static readonly (int Dx, int Dy)[] Dirs = [(0, -1), (0, 1), (-1, 0), (1, 0)];

	/// <summary>注册巢穴：扫描 Objects 层，把所有 "N" 收集到 state.Nests。</summary>
	public static void RegisterNests(GameState state, int spawnInterval = 5, int maxSpawned = 3)
	{
		state.Nests.Clear();
		for (var y = 0; y < state.MapHeight; y++)
		for (var x = 0; x < state.MapWidth; x++)
		{
			if (MapModule.GetFixture(state, x, y) != "N")
				continue;
			state.Nests.Add(new NestData
			{
				X = x,
				Y = y,
				SpawnInterval = spawnInterval,
				MaxSpawned = maxSpawned,
				TurnsSinceSpawn = 0,
			});
		}
	}

	/// <summary>
	/// 每回合调用一次。巢穴计时器 +1，到达间隔后尝试在相邻空格刷怪。
	/// 返回产出的事件列表。
	/// </summary>
	private static int _nestSpawnCounter;

	public static List<GameEvent> Tick(GameState state)
	{
		var events = new List<GameEvent>();
		var rng = new Random(state.RngSeed + state.Turn);
		var monsterTemplates = ActorTemplates.MonsterIds.ToArray();

		foreach (var nest in state.Nests)
		{
			nest.TurnsSinceSpawn++;
			if (nest.TurnsSinceSpawn < nest.SpawnInterval)
				continue;

			var nearby = CountNearbyMonsters(state, nest.X, nest.Y, 3);
			if (nearby >= nest.MaxSpawned)
				continue;

			var slot = FindSpawnSlot(state, nest, rng);
			if (slot is null)
				continue;

			var (sx, sy) = slot.Value;
			var templateId = !string.IsNullOrEmpty(nest.TemplateId)
				? nest.TemplateId
				: monsterTemplates[rng.Next(monsterTemplates.Length)];
			var monster = ActorTemplates.Spawn(templateId, $"nest_{_nestSpawnCounter++}");
			monster.X = sx;
			monster.Y = sy;
			ActorModule.Add(state, monster);
			nest.TurnsSinceSpawn = 0;
			events.Add(new GameEvent("monster_spawned") { TargetX = sx, TargetY = sy });
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
			if (MapModule.IsWalkable(state, nx, ny) && ActorModule.GetAt(state, nx, ny) == null)
				candidates.Add((nx, ny));
		}
		if (candidates.Count == 0)
			return null;
		return candidates[rng.Next(candidates.Count)];
	}

	private static int CountNearbyMonsters(GameState state, int cx, int cy, int radius)
	{
		var count = 0;
		foreach (var actor in state.Actors.Values)
		{
			if (actor.Faction != "hostile") continue;
			var adx = actor.X - cx;
			var ady = actor.Y - cy;
			if (adx >= -radius && adx <= radius && ady >= -radius && ady <= radius)
				count++;
		}
		return count;
	}
}
