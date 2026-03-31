using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniRPG.Core;

/// <summary>
/// 巢穴模块：管理地图上 Fixture "nest" 怪物刷新点的注册和每回合刷怪逻辑。
///
/// 流程：
///   1. MapGenModule.Generate → RegisterNests() 扫描全图 "nest" Fixture，写入 state.Nests
///   2. TurnModule.Tick → NestModule.Tick() 每回合推进计时器，满足条件时刷出怪物
///
/// 刷怪约束：
///   - 到达 SpawnInterval 回合才触发
///   - 周围 radius=3 范围内敌对 Actor 数 < MaxSpawned 才刷
///   - 只在巢穴四方向相邻的可行走空格中选择刷出位置
/// </summary>
public static class NestModule
{
	private static readonly (int Dx, int Dy)[] Dirs = [(0, -1), (0, 1), (-1, 0), (1, 0)];

	/// <summary>
	/// 扫描全图，将所有 Fixture "nest" 注册为 NestData 存入 state.Nests。
	/// 会清空已有 Nests 列表。
	/// </summary>
	// REVIEW: RegisterNests 会覆盖 state.Nests，但 PopulateSurface 在 Generate 中
	//         已经手动向 state.Nests 添加了 NPC 用的 NestData。
	//         Generate 里 floor==0 时不调用 RegisterNests（因此地表 NPC Nest 保留），
	//         但如果未来改为统一调用，地表 NPC Nest 会被清空。
	public static void RegisterNests(GameState state, int spawnInterval = 5, int maxSpawned = 3)
	{
		state.Nests.Clear();
		for (var y = 0; y < state.MapHeight; y++)
		for (var x = 0; x < state.MapWidth; x++)
		{
			if (!MapModule.HasFixture(state, x, y, "nest"))
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

	// REVIEW: _nestSpawnCounter 是 static 字段，进程生命周期内递增不归零，
	//         与 MapGenModule._monsterCounter 存在同样的问题。
	private static int _nestSpawnCounter;

	/// <summary>
	/// 每回合调用一次：遍历所有巢穴，推进计时器，满足条件时在相邻空格刷出怪物。
	/// 返回 monster_spawned 事件列表。
	/// </summary>
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

	/// <summary>在巢穴四方向相邻格中随机选一个可行走且无 Actor 的格子。</summary>
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
		if (candidates.Count == 0)
			return null;
		return candidates[rng.Next(candidates.Count)];
	}

	/// <summary>统计巢穴 (cx,cy) 周围 radius 范围内的敌对 Actor 数量（矩形范围）。</summary>
	// REVIEW: 使用矩形距离（Chebyshev）而非曼哈顿距离，
	//         与 AIDispatcher 中使用曼哈顿距离做分级模拟的标准不一致。
	//         遍历全部 Actors 而非仅检查 radius 范围内的格子，O(n) 开销。
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
