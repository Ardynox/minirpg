using System;
using System.Collections.Generic;
using MiniRPG.Core.World;
using MiniRPG.Core.World.Generators;

namespace MiniRPG.Core.Map;

/// <summary>
/// 程序化地图生成模块（适配器）。
/// 初始化 WorldMap + 注册生成器 + 触发玩家起始区域的 chunk 加载。
/// 实际生成逻辑委托给 IMapGenerator 实现。
/// </summary>
public static class MapGenModule
{
	private static readonly Dictionary<string, IMapGenerator> Generators = new();

	static MapGenModule()
	{
		RegisterGenerator(new RoomCorridorGenerator());
		RegisterGenerator(new PerlinGenerator());
		RegisterGenerator(new CellularAutomataGenerator());
		RegisterGenerator(new DrunkardWalkGenerator());
		RegisterGenerator(new BSPGenerator());
		RegisterGenerator(new BlankFloorGenerator());
	}

	public static void RegisterGenerator(IMapGenerator gen) => Generators[gen.Id] = gen;

	public static IReadOnlyDictionary<string, IMapGenerator> AllGenerators => Generators;

	public static IMapGenerator GetGenerator(string id) =>
		Generators.GetValueOrDefault(id) ?? Generators["room_corridor"];

	/// <summary>
	/// 初始化世界：创建 WorldMap，设置 chunk 回调，加载玩家周围的 chunk。
	/// 替代旧的 Generate(state, width, height, floor) 方法。
	/// </summary>
	public static void InitializeWorld(GameState state, int? seed = null)
	{
		var actualSeed = seed ?? state.WorldSeed;
		state.WorldSeed = actualSeed;

		var generator = GetGenerator(state.GeneratorId);
		state.World = new WorldMap(actualSeed, generator);

		state.World.Chunks.OnChunkLoad = SaveModule.LoadChunkFromCache;
		state.World.Chunks.OnChunkUnload = SaveModule.SaveChunkToCache;

		var center = new WorldCoord(state.PlayerX, state.PlayerY, state.PlayerZ);
		state.World.Chunks.UpdateLoadedChunks(center, state.Turn);
	}

	/// <summary>在玩家位置放置一个新生成的玩家 Actor。</summary>
	public static void SpawnPlayer(GameState state)
	{
		ActorModule.ClearAll(state);

		var player = ActorTemplates.Spawn(Factions.Player, Factions.Player);
		player.X = state.PlayerX;
		player.Y = state.PlayerY;
		player.Z = state.PlayerZ;
		ActorModule.Add(state, player);
	}

	/// <summary>寻找玩家附近的第一个可行走格子作为出生点。</summary>
	public static void FindSpawnPoint(GameState state)
	{
		if (state.World == null) return;

		var searchRadius = 20;
		for (var r = 0; r < searchRadius; r++)
		for (var dy = -r; dy <= r; dy++)
		for (var dx = -r; dx <= r; dx++)
		{
			if (Math.Abs(dx) + Math.Abs(dy) != r) continue;
			var x = state.PlayerX + dx;
			var y = state.PlayerY + dy;
			if (state.World.IsWalkable(x, y, state.PlayerZ))
			{
				state.PlayerX = x;
				state.PlayerY = y;
				return;
			}
		}
	}
}
