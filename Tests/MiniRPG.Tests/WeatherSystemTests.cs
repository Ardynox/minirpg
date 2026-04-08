using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class WeatherSystemTests
{
	public WeatherSystemTests()
	{
		SkillCastingTestHelper.EnsureGameDataLoaded();
		LocalizationService.SetLocale("en", notify: false);
		HealthCatalog.Load();
	}

	[Fact]
	public void Sampling_IsDeterministic_AndUndergroundIsAlwaysClear()
	{
		var state = CreateWeatherState(worldSeed: 424242);

		var sampleA = WeatherFieldSampler.Sample(state, 48, 64, 0, 30, state.Weather.FrontPhase);
		var sampleB = WeatherFieldSampler.Sample(state, 48, 64, 0, 30, state.Weather.FrontPhase);
		var underground = WeatherFieldSampler.Sample(state, 48, 64, 1, 30, state.Weather.FrontPhase);

		Assert.Equal(sampleA, sampleB);
		Assert.InRange(sampleA.TemperatureNormalized, 0f, 1f);
		Assert.Equal(WeatherRules.GetAmbientTemperatureC(sampleA.TemperatureNormalized), sampleA.AmbientTemperatureC, 3);
		Assert.Equal(WeatherType.Clear, underground.Type);
		Assert.Equal(WeatherIntensity.Normal, underground.Intensity);
		Assert.InRange(underground.TemperatureNormalized, 0f, 1f);
		Assert.Equal(WeatherRules.GetAmbientTemperatureC(underground.TemperatureNormalized), underground.AmbientTemperatureC, 3);
	}

	[Fact]
	public void Sampling_ExposesTemperatureFields_ForDebugWeatherToo()
	{
		var state = CreateWeatherState(worldSeed: 7777);
		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Snow,
			Intensity = WeatherIntensity.Heavy,
		};

		var sample = WeatherFieldSampler.Sample(state, 64, 64, 0, 20, state.Weather.FrontPhase);

		Assert.Equal(WeatherType.Snow, sample.Type);
		Assert.Equal(WeatherIntensity.Heavy, sample.Intensity);
		Assert.InRange(sample.TemperatureNormalized, 0f, 1f);
		Assert.Equal(WeatherRules.GetAmbientTemperatureC(sample.TemperatureNormalized), sample.AmbientTemperatureC, 3);
	}

	[Fact]
	public void Accumulation_Grows_Decays_AndDoesNotBackfillUnloadedChunks()
	{
		var state = CreateWeatherState();
		const int floorX = 2;
		const int floorY = 1;
		const int waterX = 3;
		const int waterY = 1;
		state.World!.SetTerrain(waterX, waterY, 0, Terrains.Water);

		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Snow,
			Intensity = WeatherIntensity.Heavy,
		};
		AdvanceTurns(state, 4);

		var afterSnow = WeatherSurface.GetAccumulation(state, floorX, floorY, 0);
		var waterAfterSnow = WeatherSurface.GetAccumulation(state, waterX, waterY, 0);
		Assert.True(afterSnow.SnowDepth > 0);
		Assert.True(waterAfterSnow.IceDepth > 0);

		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Rain,
			Intensity = WeatherIntensity.Heavy,
		};
		AdvanceTurns(state, 2);

		var afterRain = WeatherSurface.GetAccumulation(state, floorX, floorY, 0);
		Assert.True(afterRain.Wetness > afterSnow.Wetness);

		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Sandstorm,
			Intensity = WeatherIntensity.Heavy,
		};
		AdvanceTurns(state, 3);

		var afterSand = WeatherSurface.GetAccumulation(state, floorX, floorY, 0);
		Assert.True(afterSand.SandDepth > 0);

		var farX = ChunkData.Size * 8 + 1;
		var farAccumulation = WeatherSurface.GetAccumulation(state, farX, 1, 0);
		Assert.Equal(default, farAccumulation);

		var farChunk = state.World.Chunks.GetOrLoad(CoordUtil.WorldToChunk(farX, 1, 0));
		Assert.Equal(0, farChunk.LastWeatherSimTurn);
		Assert.Equal(0, farChunk.SnowDepth[0]);
		Assert.Equal(0, farChunk.SandDepth[0]);
		Assert.Equal(0, farChunk.Wetness[0]);
		Assert.Equal(0, farChunk.IceDepth[0]);

		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Clear,
			Intensity = WeatherIntensity.Normal,
		};
		AdvanceTurns(state, 4);

		var afterClear = WeatherSurface.GetAccumulation(state, floorX, floorY, 0);
		var waterAfterClear = WeatherSurface.GetAccumulation(state, waterX, waterY, 0);
		Assert.True(afterClear.SnowDepth < afterSand.SnowDepth);
		Assert.True(afterClear.SandDepth < afterSand.SandDepth);
		Assert.True(afterClear.Wetness < afterSand.Wetness);
		Assert.True(waterAfterClear.IceDepth < waterAfterSnow.IceDepth);
	}

	[Fact]
	public void FogOfWarTracker_ReducesSurfaceVision_ButLeavesUndergroundUntouched()
	{
		var state = CreateWeatherState();
		var player = ActorModule.GetPlayer(state)!;
		var tracker = new FogOfWarTracker { BaseVisionRadius = 12 };

		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Clear,
			Intensity = WeatherIntensity.Normal,
		};
		tracker.Update(state);
		var clearBand = tracker.GetVisionBand(10, 1, 0);
		Assert.NotEqual(PlayerVisionBand.Unknown, clearBand);

		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Fog,
			Intensity = WeatherIntensity.Heavy,
		};
		tracker.Update(state);
		var fogBand = tracker.GetVisionBand(10, 1, 0);
		Assert.NotEqual(PlayerVisionBand.Focused, fogBand);
		Assert.NotEqual(PlayerVisionBand.Peripheral, fogBand);

		player.Z = 1;
		state.PlayerZ = 1;
		tracker.Update(state);
		var undergroundBand = tracker.GetVisionBand(10, 1, 1);
		Assert.NotEqual(PlayerVisionBand.Unknown, undergroundBand);
	}

	[Fact]
	public void Lightning_StrikesOnlyExposedActors_AndChainsKillEvents()
	{
		var state = CreateWeatherState();
		var strikeX = ChunkData.Size * 4 + 1;
		var strikeY = 1;
		state.World!.GetTerrain(strikeX, strikeY, 0);
		state.World.GetTerrain(strikeX + 1, strikeY, 0);

		var exposed = CreateFragileActor("exposed_target", strikeX, strikeY, 0);
		var sheltered = CreateFragileActor("sheltered_target", strikeX + 1, strikeY, 0);
		ActorModule.Add(state, exposed);
		ActorModule.Add(state, sheltered);
		state.World.SetFixture(sheltered.X, sheltered.Y, sheltered.Z, "H", Entities.House);

		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Thunderstorm,
			Intensity = WeatherIntensity.Heavy,
		};
		state.Turn = GameConfig.Weather.LightningIntervalTurns - 1;

		var events = TurnModule.AdvanceWorld(state);

		var strike = Assert.Single(events, evt => evt.Type == "weather_lightning_strike");
		Assert.Equal(exposed.Id, strike.TargetId);
		Assert.DoesNotContain(events, evt => evt.Type == "weather_lightning_strike" && evt.TargetId == sheltered.Id);
		Assert.Contains(events, evt => evt.Type == "actor_killed" && evt.TargetId == exposed.Id);
		Assert.False(state.Actors.ContainsKey(exposed.Id));
		Assert.True(state.Actors.ContainsKey(sheltered.Id));
	}

	[Fact]
	public void Lightning_TargetSelection_PrefersHigherConductivityActors()
	{
		var state = CreateWeatherState();
		var high = CreateFragileActor("high_conductor", 2, 2, 0);
		var low = CreateFragileActor("low_conductor", 3, 2, 0);
		state.World!.GetTerrain(high.X, high.Y, high.Z);
		state.World.GetTerrain(low.X, low.Y, low.Z);
		high.Inventory.Add(new Item
		{
			Id = "steel_plate",
			Name = "Steel Plate",
			MaterialId = "steel",
			Category = ItemCategories.Armor,
			Equipped = true,
			BodyPart = BodyParts.Torso,
			CoveredParts = [BodyParts.Torso],
		});
		low.Inventory.Add(new Item
		{
			Id = "cloth_wrap",
			Name = "Cloth Wrap",
			MaterialId = "cloth",
			Category = ItemCategories.Clothing,
			Equipped = true,
			BodyPart = BodyParts.Torso,
			CoveredParts = [BodyParts.Torso],
		});
		high.Limbs.Add(new Limb
		{
			Id = "high_aux_1",
			Name = "High Aux 1",
			MaxDurability = 10,
			Durability = 10,
			Material = "steel",
			BodyPart = BodyParts.Arm,
		});
		high.Limbs.Add(new Limb
		{
			Id = "high_aux_2",
			Name = "High Aux 2",
			MaxDurability = 10,
			Durability = 10,
			Material = "steel",
			BodyPart = BodyParts.Head,
		});
		low.Limbs.Add(new Limb
		{
			Id = "low_aux_1",
			Name = "Low Aux 1",
			MaxDurability = 10,
			Durability = 10,
			Material = "cloth",
			BodyPart = BodyParts.Arm,
		});

		ActorModule.Add(state, high);
		ActorModule.Add(state, low);
		var coord = CoordUtil.WorldToChunk(high.X, high.Y, high.Z);
		var chunk = state.World!.Chunks.GetOrLoad(coord);
		var selectedIds = Enumerable.Range(0, 16)
			.Select(strikeIndex => WeatherAccumulationSimulator.SelectLightningTarget(state, state.World, coord, chunk, strikeIndex))
			.Where(static target => target.HasValue)
			.Select(static target => target!.Value.Actor.Id)
			.ToList();

		Assert.NotEmpty(selectedIds);
		Assert.True(WeatherAccumulationSimulator.GetActorLightningWeight(high) > WeatherAccumulationSimulator.GetActorLightningWeight(low));
		Assert.True(selectedIds.Count(id => id == high.Id) > selectedIds.Count(id => id == low.Id));
	}

	private static void AdvanceTurns(GameState state, int turns)
	{
		for (var i = 0; i < turns; i++)
			TurnModule.AdvanceWorld(state);
	}

	private static GameState CreateWeatherState(int worldSeed = 12345)
	{
		var state = new GameState
		{
			WorldSeed = worldSeed,
			PlayerId = "player",
			PlayerX = 1,
			PlayerY = 1,
			PlayerZ = 0,
			GeneratorId = "test",
			ViewModeId = "single_layer",
			Actors = new Dictionary<string, Actor>(StringComparer.Ordinal),
			World = new WorldMap(worldSeed, new FlatFloorGenerator()),
			Weather = WeatherState.CreateDefault(worldSeed),
		};

		var player = PresetDB.SpawnActor("player", "player");
		player.X = state.PlayerX;
		player.Y = state.PlayerY;
		player.Z = state.PlayerZ;
		player.FacingX = 1;
		player.FacingY = 0;
		player.BrainId = null;
		ActorModule.Add(state, player);
		return state;
	}

	private static Actor CreateFragileActor(string id, int x, int y, int z)
	{
		var actor = PresetDB.SpawnActor("player", id);
		actor.DisplayName = id;
		actor.X = x;
		actor.Y = y;
		actor.Z = z;
		actor.Faction = Factions.Hostile;
		actor.BrainId = null;
		actor.Limbs.Clear();
		actor.Limbs.Add(new Limb
		{
			Id = $"{id}_core",
			Name = "Core",
			MaxDurability = 10,
			Durability = 10,
			Material = "flesh",
			BodyPart = BodyParts.Torso,
			Capacities = new Dictionary<string, float>
			{
				[Caps.BloodCirculation] = 1f,
				[Caps.Consciousness] = 1f,
				[Caps.Moving] = 1f,
				[Caps.Metabolism] = 1f,
			},
			Tags = new Dictionary<string, int>
			{
				[CombatModule.VitalTag] = 1,
			},
		});
		return actor;
	}

	private sealed class FlatFloorGenerator : IMapGenerator
	{
		public string Id => "flat_floor";
		public string Name => "Flat Floor";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
			chunk.Fill(TerrainRegistry.GetId(Terrains.Floor));
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
