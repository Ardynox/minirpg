using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;
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
		RegisterGenerator(new VoxelBlockIsometricGenerator());
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
		if (state.WorldSeed != actualSeed)
		{
			state.WorldSeed = actualSeed;
		}
		else
		{
			state.Weather ??= WeatherState.CreateDefault(actualSeed);
		}

		var generator = GetGenerator(state.GeneratorId);
		state.World = new WorldMap(actualSeed, generator);

		state.World.Chunks.OnChunkLoad = SaveModule.LoadChunkFromCache;
		state.World.Chunks.OnChunkUnload = SaveModule.SaveChunkToCache;

		var anchors = RoomRuntimeModule.GetWorldAnchors(state).Select(static anchor => anchor.Position).ToArray();
		state.World.Chunks.UpdateLoadedChunks(anchors, state.Turn);
	}

	/// <summary>在玩家位置放置一个新生成的玩家 Actor。</summary>
	public static void SpawnPlayer(GameState state, PlayerCreationOptions? options = null)
	{
		ActorModule.ClearAll(state);

		var player = ActorTemplates.Spawn(Factions.Player, state.PlayerId);
		player.Faction = Factions.Player;
		player.PrimaryDomainId = DomainIds.Player;
		ApplyPlayerCreationOptions(state, player, options);
		GiveStarterKit(player);
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
		var zSearchAbove = 6;
		var zSearchBelow = 10;
		for (var r = 0; r < searchRadius; r++)
		for (var dy = -r; dy <= r; dy++)
		for (var dx = -r; dx <= r; dx++)
		{
			if (Math.Abs(dx) + Math.Abs(dy) != r) continue;
			var x = state.PlayerX + dx;
			var y = state.PlayerY + dy;

			if (TryFindSpawnZ(state.World, x, y, state.PlayerZ, zSearchAbove, zSearchBelow, out var z))
			{
				state.PlayerX = x;
				state.PlayerY = y;
				state.PlayerZ = z;
				return;
			}
		}
	}

	private static bool TryFindSpawnZ(WorldMap world, int x, int y, int centerZ, int searchAbove, int searchBelow, out int z)
	{
		z = centerZ;

		if (world.IsWalkable(x, y, centerZ))
			return true;

		for (var step = 1; step <= Math.Max(searchAbove, searchBelow); step++)
		{
			if (step <= searchAbove)
			{
				var candidateAbove = centerZ - step;
				if (world.IsWalkable(x, y, candidateAbove))
				{
					z = candidateAbove;
					return true;
				}
			}

			if (step <= searchBelow)
			{
				var candidateBelow = centerZ + step;
				if (world.IsWalkable(x, y, candidateBelow))
				{
					z = candidateBelow;
					return true;
				}
			}
		}

		return false;
	}

	private static void ApplyPlayerCreationOptions(GameState state, Actor player, PlayerCreationOptions? options)
	{
		var resolvedOptions = options ?? PlayerCreationOptions.CreateDefault();
		var defaultOptions = PlayerCreationOptions.CreateDefault();
		var raceId = resolvedOptions.ResolveRaceId(defaultOptions.RaceId);
		var professionId = resolvedOptions.ResolveProfessionId(defaultOptions.ProfessionId);

		player.DisplayName = resolvedOptions.ResolveDisplayName(player.DisplayName);
		player.Race = null;
		player.Profession = null;
		player.Limbs.Clear();

		if (PresetDB.Races.TryGetValue(raceId, out var racePreset))
		{
			player.Race = new Race
			{
				Id = racePreset.Id,
				Name = racePreset.Name,
				NeedProfileId = racePreset.NeedProfileId,
				Tags = new(racePreset.Tags),
			};
			foreach (var limbId in racePreset.DefaultLimbs)
				player.Limbs.Add(PresetDB.CloneLimb(limbId));
		}

		if (!string.IsNullOrWhiteSpace(professionId)
			&& PresetDB.Professions.TryGetValue(professionId, out var professionPreset))
		{
			player.Profession = new Profession
			{
				Id = professionPreset.Id,
				Name = professionPreset.Name,
				Tags = new(professionPreset.Tags),
			};
		}

		state.PlayerAppearanceId = resolvedOptions.ResolveAppearanceId();
		NeedSystem.EnsureInitialized(player, state.Turn);
	}

	private static void GiveStarterKit(Actor player)
	{
		player.Inventory.Clear();
		player.Inventory.Add(PresetDB.CloneItem("meal_simple"));
		player.Inventory.Add(PresetDB.CloneItem("bedroll"));
	}
}
