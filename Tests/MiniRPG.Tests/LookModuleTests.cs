using System;
using System.Collections.Generic;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;
using MiniRPG.Module;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class LookModuleTests
{
	[Fact]
	public void DescribeCell_ReturnsFocusedActorInfo_ForVisibleCreatureTile()
	{
		EnsureTestRegistriesLoaded();

		var player = CreateActor("player", "Hero", Factions.Player, 1, 1, 0);
		player.FacingX = 1;
		player.FacingY = 0;
		var goblin = CreateActor("goblin", "Goblin", Factions.Hostile, 2, 1, 0);
		goblin.TemplateId = "goblin";
		var state = new GameState
		{
			WorldSeed = 123,
			PlayerId = player.Id,
			PlayerX = player.X,
			PlayerY = player.Y,
			PlayerZ = player.Z,
			Actors = new Dictionary<string, Actor>
			{
				[player.Id] = player,
				[goblin.Id] = goblin,
			},
			World = new WorldMap(123, new StubGenerator()),
		};
		state.IdentifiedActorTypes.Add("goblin");
		state.World.SetTerrain(player.X, player.Y, player.Z, Terrains.Floor);
		state.World.SetTerrain(goblin.X, goblin.Y, goblin.Z, Terrains.Floor);
		state.World.RebuildLoadedActorIndex(state.Actors);

		var fog = new FogOfWarTracker();
		fog.Update(state);

		var info = LookModule.DescribeCell(state, fog, goblin.X, goblin.Y, goblin.Z);

		Assert.Equal(PlayerVisionBand.Focused, info.VisionBand);
		Assert.NotNull(info.InspectableActor);
		Assert.Equal(goblin.Id, info.InspectableActor!.Id);
		Assert.Contains("Goblin", info.Text, StringComparison.Ordinal);
		Assert.Contains("Actors:", info.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void DescribeCell_HidesUnknownCreatureName_UntilIdentified()
	{
		EnsureTestRegistriesLoaded();

		var player = CreateActor("player", "Hero", Factions.Player, 1, 1, 0);
		player.FacingX = 1;
		player.FacingY = 0;
		var goblin = CreateActor("goblin", "Goblin", Factions.Hostile, 2, 1, 0);
		goblin.TemplateId = "goblin";
		var state = new GameState
		{
			WorldSeed = 123,
			PlayerId = player.Id,
			PlayerX = player.X,
			PlayerY = player.Y,
			PlayerZ = player.Z,
			Actors = new Dictionary<string, Actor>
			{
				[player.Id] = player,
				[goblin.Id] = goblin,
			},
			World = new WorldMap(123, new StubGenerator()),
		};
		state.World.SetTerrain(player.X, player.Y, player.Z, Terrains.Floor);
		state.World.SetTerrain(goblin.X, goblin.Y, goblin.Z, Terrains.Floor);
		state.World.RebuildLoadedActorIndex(state.Actors);

		var fog = new FogOfWarTracker();
		fog.Update(state);

		var info = LookModule.DescribeCell(state, fog, goblin.X, goblin.Y, goblin.Z);

		Assert.DoesNotContain("Goblin", info.Text, StringComparison.Ordinal);
		Assert.Contains("Unknown hostile", info.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void BuildLookText_AndDescribeCell_IncludeWeatherAndAccumulation()
	{
		EnsureTestRegistriesLoaded();
		SkillCastingTestHelper.EnsureGameDataLoaded();

		var player = PresetDB.SpawnActor("player", "player");
		player.X = 1;
		player.Y = 1;
		player.Z = 0;
		player.FacingX = 1;
		player.FacingY = 0;
		player.BrainId = null;

		var state = new GameState
		{
			WorldSeed = 321,
			PlayerId = player.Id,
			PlayerX = player.X,
			PlayerY = player.Y,
			PlayerZ = player.Z,
			Actors = new Dictionary<string, Actor>
			{
				[player.Id] = player,
			},
			World = new WorldMap(321, new StubGenerator()),
			Weather = new WeatherState
			{
				FrontPhase = 0f,
				DebugOverride = new WeatherDebugOverride
				{
					Type = WeatherType.Snow,
					Intensity = WeatherIntensity.Heavy,
				},
			},
		};
		state.World.SetTerrain(player.X, player.Y, player.Z, Terrains.Floor);
		state.World.RebuildLoadedActorIndex(state.Actors);

		TurnModule.AdvanceWorld(state);

		var fog = new FogOfWarTracker();
		fog.Update(state);

		var lookText = LookModule.BuildLookText(state, fog);
		var info = LookModule.DescribeCell(state, fog, player.X, player.Y, player.Z);

		Assert.Contains("Weather: Snow (Heavy)", lookText, StringComparison.Ordinal);
		Assert.Contains("Underfoot:", lookText, StringComparison.Ordinal);
		Assert.Contains("snow-covered", lookText, StringComparison.Ordinal);
		Assert.Contains("Weather: Snow (Heavy)", info.Text, StringComparison.Ordinal);
		Assert.Contains("Surface:", info.Text, StringComparison.Ordinal);
		Assert.Contains("snow-covered", info.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void DescribeCell_IncludesWildfireAndItemDurability()
	{
		SkillCastingTestHelper.EnsureGameDataLoaded();
		LocalizationService.SetLocale("en", notify: false);

		var player = PresetDB.SpawnActor("player", "player");
		player.X = 1;
		player.Y = 1;
		player.Z = 0;
		player.FacingX = 1;
		player.FacingY = 0;
		player.BrainId = null;

		var state = new GameState
		{
			WorldSeed = 987,
			PlayerId = player.Id,
			PlayerX = player.X,
			PlayerY = player.Y,
			PlayerZ = player.Z,
			Actors = new Dictionary<string, Actor> { [player.Id] = player },
			World = new WorldMap(987, new StubGenerator()),
		};
		state.World.SetTerrain(player.X, player.Y, player.Z, Terrains.Floor);
		state.World.SetTerrain(2, 1, 0, Terrains.Floor);
		state.World.RebuildLoadedActorIndex(state.Actors);

		var torch = PresetDB.CloneItem("torch");
		torch.ApplyDurabilityDamage(torch.MaxDurability / 2);
		state.World.PlaceItem(2, 1, 0, torch);
		FireSystem.TryIgniteCell(state, 2, 1, 0, intensity: 4, fuel: 12);

		var fog = new FogOfWarTracker();
		fog.Update(state);

		var info = LookModule.DescribeCell(state, fog, 2, 1, 0);

		Assert.Contains("Hazard: Fire", info.Text, StringComparison.Ordinal);
		Assert.Contains("Torch", info.Text, StringComparison.Ordinal);
		Assert.Contains("Dur", info.Text, StringComparison.Ordinal);
	}

	private static Actor CreateActor(string id, string name, string faction, int x, int y, int z) =>
		new()
		{
			Id = id,
			DisplayName = name,
			Faction = faction,
			X = x,
			Y = y,
			Z = z,
			Limbs =
			[
				new Limb
				{
					Id = $"{id}_core",
					Name = "Core",
					MaxDurability = 10,
					Durability = 10,
					Material = "flesh",
					BodyPart = "torso",
					Capacities = new Dictionary<string, float>
					{
						[Caps.BloodCirculation] = 1f,
						[Caps.Moving] = 1f,
						[Caps.Consciousness] = 1f,
						[Caps.Metabolism] = 1f,
						[Caps.Sight] = 1f,
					},
				},
			],
		};

	private static void EnsureTestRegistriesLoaded()
	{
		LocalizationService.Initialize();
		LocalizationService.SetLocale("en", notify: false);

		var floor = TerrainRegistry.Get(Terrains.Floor);
		if (floor != null && floor.StringId == Terrains.Floor)
			return;

		TerrainRegistry.LoadFromJson("""
			[
			  {
			    "id": 1,
			    "stringId": "floor",
			    "glyph": ".",
			    "defaultHardness": 0,
			    "solid": false,
			    "material": "soil",
			    "breaksInto": "rubble"
			  },
			  {
			    "id": 2,
			    "stringId": "wall",
			    "glyph": "#",
			    "defaultHardness": 10,
			    "solid": true,
			    "material": "stone",
			    "breaksInto": "rubble"
			  }
			]
			""");
	}

	private sealed class StubGenerator : IMapGenerator
	{
		public string Id => "stub";
		public string Name => "Stub";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
