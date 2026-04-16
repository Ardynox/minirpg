using System.Collections.Generic;
using MiniRPG.Core.AI.Utility;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;

namespace MiniRPG.Tests;

internal static class SkillCastingTestHelper
{
	private static bool _initialized;

	public static void EnsureGameDataLoaded()
	{
		LocalizationService.Initialize();
		if (!_initialized)
		{
			GameConfig.Load();
			PresetDB.Load();
			_initialized = true;
		}

		if (!HasExpectedTerrain(Terrains.Floor) || !HasExpectedTerrain(Terrains.Water))
			TerrainRegistry.Load("terrains.json");
		UtilityActionRegistry.EnsureLoaded();
		ExecutorRegistry.EnsureInitialized();
		PersonalityModule.EnsureLoaded();
	}

	private static bool HasExpectedTerrain(string terrainId) =>
		TerrainRegistry.Get(terrainId)?.StringId == terrainId;

	public static (GameState State, Actor Player, Actor Enemy) CreateCombatState(
		int playerX = 1,
		int playerY = 1,
		int enemyX = 3,
		int enemyY = 1,
		int z = 0)
	{
		EnsureGameDataLoaded();

		var state = new GameState
		{
			WorldSeed = 12345,
			PlayerId = "player",
			PlayerX = playerX,
			PlayerY = playerY,
			PlayerZ = z,
			GeneratorId = "test",
			ViewModeId = "single_layer",
			Actors = new Dictionary<string, Actor>(),
			World = new WorldMap(12345, new FlatFloorGenerator()),
		};
		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Clear,
			Intensity = WeatherIntensity.Normal,
		};

		var player = PresetDB.SpawnActor("player", "player");
		player.X = playerX;
		player.Y = playerY;
		player.Z = z;
		player.FacingX = 1;
		player.FacingY = 0;
		player.BrainId = null;
		player.Gold = 0;
		EnsureVitalLimb(player);

		var enemy = PresetDB.SpawnActor("player", "enemy");
		enemy.X = enemyX;
		enemy.Y = enemyY;
		enemy.Z = z;
		enemy.Faction = Factions.Hostile;
		enemy.DisplayName = "Enemy";
		enemy.FacingX = -1;
		enemy.FacingY = 0;
		enemy.BrainId = "simple";
		enemy.Gold = 0;
		EnsureVitalLimb(enemy);

		ActorModule.Add(state, player);
		ActorModule.Add(state, enemy);
		return (state, player, enemy);
	}

	private static void EnsureVitalLimb(Actor actor)
	{
		if (actor.Limbs.Count == 0)
			return;

		actor.Limbs[0].Tags[CombatModule.VitalTag] = 1;
	}

	private sealed class FlatFloorGenerator : IMapGenerator
	{
		public string Id => "flat_floor";
		public string Name => "Flat Floor";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
			var id = chunk.Coord.Cz == 0
				? TerrainRegistry.GetId(Terrains.Floor)
				: chunk.Coord.Cz > 0
					? TerrainRegistry.GetId(Terrains.WallStone)
					: TerrainRegistry.GetId(Terrains.Air);
			chunk.Fill(id);
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
