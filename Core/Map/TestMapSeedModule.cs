using System;
using System.IO;
using MiniRPG.Core.Debug;
using MiniRPG.Core.World;

namespace MiniRPG.Core.Map;

/// <summary>
/// 首次启动时写入一份固定测试存档，之后不再自动重建。
/// </summary>
public static class TestMapSeedModule
{
	public const string TestSaveFileName = "000_test_map.json";
	public const string SeedMarkerFileName = ".test_map_seeded";

	private const int TestSeed = 424242;

	private static readonly string[] Rows =
	[
		"#####################",
		"#....D.........H....#",
		"#...................#",
		"#..#####...#######..#",
		"#..#P..#...#.....#..#",
		"#..#...D...D.....#..#",
		"#..#...#...#.....#..#",
		"#..#####...#######..#",
		"#...................#",
		"#....D..............#",
		"#####################",
	];

	public static void EnsureSeeded(string saveDirectory)
	{
		Directory.CreateDirectory(saveDirectory);

		var markerPath = Path.Combine(saveDirectory, SeedMarkerFileName);
		if (File.Exists(markerPath))
			return;

		var testSavePath = Path.Combine(saveDirectory, TestSaveFileName);
		if (!File.Exists(testSavePath))
		{
			var state = BuildTestSave();
			SaveModule.SaveGame(state, testSavePath);
		}

		File.WriteAllText(markerPath, DateTime.UtcNow.ToString("O"));
	}

	private static GameState BuildTestSave()
	{
		var state = new GameState
		{
			WorldSeed = TestSeed,
			GeneratorId = "room_corridor",
			ViewModeId = "single_layer",
			PlayerX = 0,
			PlayerY = 0,
			PlayerZ = 0,
		};

		MapGenModule.InitializeWorld(state, TestSeed);
		PrepareChunk(state);
		MapModule.LoadFromStrings(state, Rows, 0);
		DecorateTerrain(state);
		MapGenModule.SpawnPlayer(state);
		SeedActors(state);
		SeedLoot(state);
		return state;
	}

	private static void PrepareChunk(GameState state)
	{
		var chunk = state.World!.Chunks.GetOrLoad(new ChunkCoord(0, 0, 0));
		chunk.Fill(TerrainRegistry.GetId(Terrains.WallStone));
		chunk.Entities.Clear();
		chunk.Nests.Clear();
		chunk.ActorIds.Clear();
		chunk.Dirty = true;
	}

	private static void DecorateTerrain(GameState state)
	{
		var world = state.World!;
		var z = state.PlayerZ;

		world.SetTerrain(15, 2, z, Terrains.Water);
		world.SetTerrain(16, 2, z, Terrains.Water);
		world.SetTerrain(15, 3, z, Terrains.Water);
		world.SetTerrain(16, 3, z, Terrains.Water);

		world.SetTerrain(17, 2, z, Terrains.Tree);
		world.SetTerrain(5, 8, z, Terrains.WallSoil);
		world.SetTerrain(6, 8, z, Terrains.CrystalVein);
		world.SetTerrain(12, 8, z, Terrains.Grass);
		world.SetTerrain(13, 8, z, Terrains.Sand);
	}

	private static void SeedActors(GameState state)
	{
		var merchant = ActorTemplates.Spawn("merchant", "preset_test_merchant");
		merchant.X = 3;
		merchant.Y = 2;
		merchant.Z = 0;
		merchant.BrainId = null;
		merchant.FacingX = 1;
		merchant.FacingY = 0;
		ActorModule.Add(state, merchant);

		var goblin = ActorTemplates.Spawn("goblin", "preset_test_goblin");
		goblin.X = 16;
		goblin.Y = 6;
		goblin.Z = 0;
		goblin.FacingX = -1;
		goblin.FacingY = 0;
		ActorModule.Add(state, goblin);
	}

	private static void SeedLoot(GameState state)
	{
		DebugModule.SpawnChest(state, 4, 8);

		var torch = PresetDB.CloneItem("torch");
		state.World!.PlaceItem(13, 2, 0, torch);

		var potion = PresetDB.CloneItem("potion_hp");
		state.World.PlaceItem(14, 8, 0, potion);
	}
}
