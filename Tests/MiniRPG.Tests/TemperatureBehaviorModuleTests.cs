using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.AI;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using MiniRPG.Core.Needs;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

public sealed class TemperatureBehaviorModuleTests
{
	public TemperatureBehaviorModuleTests()
	{
		GameConfig.Load();
		PresetDB.Load();
		TerrainRegistry.Load("terrains.json");
		HealthCatalog.Load();
	}

	[Fact]
	public void TemperatureBehavior_EquipsPortableHeatBeforeLightingFire()
	{
		var state = CreateState();
		var (x, y) = FindSurfaceCell(state, static sample => sample.AmbientTemperatureC <= -8f);
		var actor = CreateTemplateActor("heater", x, y, Factions.Friendly);
		actor.Inventory.Add(PresetDB.CloneItem("torch"));
		actor.Inventory.Add(PresetDB.CloneItem("mat_wood"));
		ActorModule.Add(state, actor);

		var result = TemperatureBehaviorModule.TryExecute(state, actor, PerceptionBuilder.Build(state, actor, SimDetail.Full), tickBuffs: false);

		Assert.True(result.Consumed);
		Assert.Contains(actor.Inventory, item => item.Id == "torch" && item.Equipped);
		Assert.False(HasAdjacentCampfire(state, actor));
	}

	[Fact]
	public void TemperatureBehavior_LightsFire_WhenColdAndNoPortableHeat()
	{
		var state = CreateState();
		var (x, y) = FindSurfaceCell(state, static sample => sample.AmbientTemperatureC <= -8f);
		var actor = CreateTemplateActor("camper", x, y, Factions.Friendly);
		actor.Inventory.Add(PresetDB.CloneItem("mat_wood"));
		ActorModule.Add(state, actor);

		var result = TemperatureBehaviorModule.TryExecute(state, actor, PerceptionBuilder.Build(state, actor, SimDetail.Full), tickBuffs: false);

		Assert.True(result.Consumed);
		Assert.Contains(result.Events, evt => evt.Type == "campfire_lit");
		Assert.True(HasAdjacentCampfire(state, actor));
	}

	[Fact]
	public void TemperatureBehavior_MovesTowardCampfire_WhenCold()
	{
		var state = CreateState();
		var (x, y) = FindSurfaceCell(state, static sample => sample.AmbientTemperatureC <= -8f);
		EnsureFloor(state, x, y);
		EnsureFloor(state, x + 1, y);
		EnsureFloor(state, x + 2, y);
		var actor = CreateTemplateActor("traveler", x, y, Factions.Friendly);
		ActorModule.Add(state, actor);
		state.World!.SetFixture(x + 2, y, 0, "*", Entities.Campfire);

		var result = TemperatureBehaviorModule.TryExecute(state, actor, PerceptionBuilder.Build(state, actor, SimDetail.Full), tickBuffs: false);

		Assert.True(result.Consumed);
		Assert.Contains(result.Events, evt => evt.Type == "actor_moved");
		Assert.Equal(x + 1, actor.X);
		Assert.Equal(y, actor.Y);
	}

	[Fact]
	public void TemperatureBehavior_MovesTowardCoolerShelter_WhenHot()
	{
		var state = CreateState();
		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Clear,
			Intensity = WeatherIntensity.Normal,
		};
		var (x, y) = FindSurfaceCell(state, static sample => sample.AmbientTemperatureC >= 18f);
		EnsureFloor(state, x, y);
		EnsureFloor(state, x + 1, y);
		var actor = CreateTemplateActor("cooler", x, y, Factions.Friendly);
		ActorModule.Add(state, actor);
		state.World!.SetFixture(x, y, 0, "*", Entities.Campfire);
		state.World!.SetFixture(x + 1, y, 0, "H", Entities.House);
		var beforeExposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, actor);

		var result = TemperatureBehaviorModule.TryExecute(state, actor, PerceptionBuilder.Build(state, actor, SimDetail.Full), tickBuffs: false);
		var afterExposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, actor);

		Assert.True(result.Consumed);
		Assert.Contains(result.Events, evt => evt.Type == "actor_moved");
		Assert.True(actor.X != x || actor.Y != y);
		Assert.True(afterExposure.AmbientTemperature < beforeExposure.AmbientTemperature);
	}

	[Fact]
	public void TemperatureBehavior_HostileAlertedActor_IgnoresHeatBehavior()
	{
		var state = CreateState();
		var (x, y) = FindSurfaceCell(state, static sample => sample.AmbientTemperatureC <= -8f);
		var actor = CreateTemplateActor("hostile", x, y, Factions.Hostile);
		actor.AwarenessState = AwarenessState.Alerted;
		actor.Inventory.Add(PresetDB.CloneItem("mat_wood"));
		ActorModule.Add(state, actor);

		var result = TemperatureBehaviorModule.TryExecute(state, actor, PerceptionBuilder.Build(state, actor, SimDetail.Full), tickBuffs: false);

		Assert.False(result.Consumed);
		Assert.False(HasAdjacentCampfire(state, actor));
	}

	private static GameState CreateState()
	{
		var state = new GameState
		{
			PlayerId = "player",
			PlayerX = 1,
			PlayerY = 1,
			PlayerZ = 0,
			WorldSeed = 4040,
			Actors = new Dictionary<string, Actor>(StringComparer.Ordinal),
			World = new WorldMap(4040, new FloorGenerator()),
			Weather = WeatherState.CreateDefault(4040),
		};
		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Snow,
			Intensity = WeatherIntensity.Heavy,
		};
		return state;
	}

	private static Actor CreateTemplateActor(string id, int x, int y, string faction)
	{
		var actor = PresetDB.SpawnActor("player", id);
		actor.DisplayName = id;
		actor.Faction = faction;
		actor.BrainId = "simple";
		actor.X = x;
		actor.Y = y;
		actor.Z = 0;
		actor.FacingX = 1;
		actor.FacingY = 0;
		NeedSystem.EnsureInitialized(actor, 0);
		HealthSystem.EnsureInitialized(actor, 0);
		return actor;
	}

	private static (int X, int Y) FindSurfaceCell(GameState state, Func<WeatherSample, bool> predicate)
	{
		for (var cx = 0; cx < 256; cx++)
		{
			for (var cy = 0; cy < 256; cy++)
			{
				var x = cx * CoordUtil.ChunkSize + 1;
				var y = cy * CoordUtil.ChunkSize + 1;
				var sample = WeatherFieldSampler.Sample(state, x, y, 0, state.Turn, state.Weather.FrontPhase);
				if (predicate(sample))
					return (x, y);
			}
		}

		throw new InvalidOperationException("Unable to find a matching weather sample.");
	}

	private static bool HasAdjacentCampfire(GameState state, Actor actor)
	{
		var cells = new[] { (actor.X, actor.Y), (actor.X, actor.Y - 1), (actor.X, actor.Y + 1), (actor.X - 1, actor.Y), (actor.X + 1, actor.Y) };
		return cells.Any(cell => state.World!.HasFixture(cell.Item1, cell.Item2, actor.Z, Entities.Campfire));
	}

	private static void EnsureFloor(GameState state, int x, int y) =>
		state.World!.SetTerrain(x, y, 0, Terrains.Floor);

	private sealed class FloorGenerator : IMapGenerator
	{
		public string Id => "temp_floor";
		public string Name => "Temp Floor";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
			chunk.Fill(TerrainRegistry.GetId(Terrains.Floor));
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
