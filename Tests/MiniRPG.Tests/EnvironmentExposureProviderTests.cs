using System;
using System.Collections.Generic;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

public sealed class EnvironmentExposureProviderTests
{
	public EnvironmentExposureProviderTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	[Fact]
	public void Capture_UsesDifferentTemperatureSemantics_ForExposedShelteredAndUnderground()
	{
		var state = CreateState(worldSeed: 424242);
		var (x, y, sample) = FindSurfaceCell(state, static weather => Math.Abs(weather.AmbientTemperatureC - GameConfig.Weather.ShelterNeutralTemperatureC) >= 6f);

		var exposed = CreateActor("exposed", x, y, z: 0);
		var sheltered = CreateActor("sheltered", x + 1, y, z: 0);
		var underground = CreateActor("underground", x, y, z: 1);
		state.World!.SetFixture(sheltered.X, sheltered.Y, sheltered.Z, "H", Entities.House);

		var exposedExposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, exposed);
		var shelteredExposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, sheltered);
		var undergroundExposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, underground);

		Assert.True(exposedExposure.HasWeatherData);
		Assert.Equal(sample.AmbientTemperatureC, exposedExposure.AmbientTemperature, 3);
		Assert.Equal(
			Lerp(sample.AmbientTemperatureC, GameConfig.Weather.ShelterNeutralTemperatureC, GameConfig.Weather.ShelterTemperatureLerp),
			shelteredExposure.AmbientTemperature,
			3);
		Assert.Equal(GameConfig.Weather.UndergroundNeutralTemperatureC, undergroundExposure.AmbientTemperature, 3);
		Assert.NotEqual(exposedExposure.AmbientTemperature, shelteredExposure.AmbientTemperature);
	}

	[Fact]
	public void Capture_RainExposure_IncreasesWetness_WhileShelterDries()
	{
		var state = CreateState(worldSeed: 2026);
		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Rain,
			Intensity = WeatherIntensity.Heavy,
		};

		var exposed = CreateActor("exposed", 2, 2, z: 0);
		var sheltered = CreateActor("sheltered", 3, 2, z: 0);
		state.World!.SetFixture(sheltered.X, sheltered.Y, sheltered.Z, "H", Entities.House);

		var exposedExposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, exposed);
		var shelteredExposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, sheltered);

		Assert.True(exposedExposure.WetnessDelta > 0f);
		Assert.True(shelteredExposure.WetnessDelta < exposedExposure.WetnessDelta);
		Assert.True(shelteredExposure.WetnessDelta < 0f);
	}

	[Fact]
	public void Capture_Accumulation_PenalizesCleanliness()
	{
		var state = CreateState(worldSeed: 99);
		SetSurfaceAccumulation(state, 4, 4, 0, wetness: 20, snow: 6, sand: 8, ice: 0);
		var actor = CreateActor("test", 4, 4, z: 0);

		var exposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, actor);

		Assert.Equal(30f, exposure.Cleanliness, 3);
	}

	[Fact]
	public void Capture_ClosedRoomsGainShelterStrength_AndSmallRoomsRetainMoreHeat()
	{
		var state = CreateState(worldSeed: 314159);
		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Snow,
			Intensity = WeatherIntensity.Heavy,
		};

		BuildRoofedRoom(state, left: 18, top: 18, width: 3, height: 3);
		BuildRoofedRoom(state, left: 28, top: 18, width: 6, height: 6);
		var smallRoomActor = CreateActor("small_room", 19, 19, z: 0);
		var largeRoomActor = CreateActor("large_room", 30, 20, z: 0);

		var smallExposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, smallRoomActor);
		var largeExposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, largeRoomActor);
		var neutral = GameConfig.Weather.ShelterNeutralTemperatureC;

		Assert.True(smallExposure.IsIndoors);
		Assert.True(largeExposure.IsIndoors);
		Assert.True(smallExposure.ShelterStrength > largeExposure.ShelterStrength);
		Assert.True(Math.Abs(neutral - smallExposure.AmbientTemperature) < Math.Abs(neutral - largeExposure.AmbientTemperature));
	}

	[Fact]
	public void Capture_FueledFacilityHeat_AddsTemperature_AndDrying()
	{
		var state = CreateState(worldSeed: 4444);
		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Rain,
			Intensity = WeatherIntensity.Heavy,
		};

		var actor = CreateActor("facility_heat", 8, 8, z: 0);
		var baseline = DefaultEnvironmentExposureProvider.Instance.Capture(state, actor);

		AddFacility(state, new FacilityInstance
		{
			Id = "brazier_1",
			FacilityDefId = FacilityIds.FireBrazier,
			AnchorX = 9,
			AnchorY = 8,
			Z = 0,
			Rotation = FacilityRotation.North,
			Stage = FacilityStage.Active,
			OwnerDomainId = DomainIds.Player,
			HitPoints = 40,
			MaxHitPoints = 40,
			FuelTicksRemaining = 80,
		});

		var heated = DefaultEnvironmentExposureProvider.Instance.Capture(state, actor);
		state.Facilities["brazier_1"].FuelTicksRemaining = 0;
		var cold = DefaultEnvironmentExposureProvider.Instance.Capture(state, actor);

		Assert.True(heated.HeatSourceTemperatureBonus > baseline.HeatSourceTemperatureBonus);
		Assert.True(heated.DryingBonus > baseline.DryingBonus);
		Assert.True(heated.AmbientTemperature > baseline.AmbientTemperature);
		Assert.Equal(baseline.HeatSourceTemperatureBonus, cold.HeatSourceTemperatureBonus, 3);
		Assert.Equal(baseline.DryingBonus, cold.DryingBonus, 3);
	}

	[Fact]
	public void Capture_RoomRoleModifiers_ImproveSleepSurfaceQuality()
	{
		var state = CreateState(worldSeed: 5150);
		BuildRoofedRoom(state, left: 18, top: 18, width: 4, height: 4);
		BuildRoofedRoom(state, left: 28, top: 18, width: 4, height: 4);

		AddFacility(state, new FacilityInstance
		{
			Id = "dorm_bed_1",
			FacilityDefId = FacilityIds.DormitoryBed,
			AnchorX = 19,
			AnchorY = 19,
			Z = 0,
			Rotation = FacilityRotation.North,
			Stage = FacilityStage.Active,
			OwnerDomainId = DomainIds.Player,
			HitPoints = 28,
			MaxHitPoints = 28,
		});
		AddFacility(state, new FacilityInstance
		{
			Id = "dorm_bed_2",
			FacilityDefId = FacilityIds.DormitoryBed,
			AnchorX = 20,
			AnchorY = 19,
			Z = 0,
			Rotation = FacilityRotation.North,
			Stage = FacilityStage.Active,
			OwnerDomainId = DomainIds.Player,
			HitPoints = 28,
			MaxHitPoints = 28,
		});

		var dormRoom = RoomContextAnalyzer.AnalyzeRoom(state, 19, 20, 0);
		var plainRoom = RoomContextAnalyzer.AnalyzeRoom(state, 29, 20, 0);
		var dormActor = CreateActor("dorm_actor", 19, 20, z: 0);
		var plainActor = CreateActor("plain_actor", 29, 20, z: 0);
		var dormExposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, dormActor);
		var plainExposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, plainActor);

		Assert.Equal(RoomRoleIds.Dormitory, dormRoom.PrimaryRoleId);
		Assert.Equal(string.Empty, plainRoom.PrimaryRoleId);
		Assert.True(dormRoom.Modifiers.RestQuality > plainRoom.Modifiers.RestQuality);
		Assert.True(dormExposure.SleepSurfaceQuality > plainExposure.SleepSurfaceQuality);
	}

	[Fact]
	public void Capture_CampfireAndPortableHeat_AddTemperature_AndDrying()
	{
		var state = CreateState(worldSeed: 8080);
		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Rain,
			Intensity = WeatherIntensity.Heavy,
		};

		var plain = CreateActor("plain", 5, 5, z: 0);
		var warmed = CreateActor("warmed", 8, 5, z: 0);
		Equip(warmed, "torch");
		state.World!.SetFixture(9, 5, 0, "*", Entities.Campfire);

		var plainExposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, plain);
		var warmedExposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, warmed);

		Assert.True(warmedExposure.HeatSourceTemperatureBonus > plainExposure.HeatSourceTemperatureBonus);
		Assert.True(warmedExposure.DryingBonus > 0f);
		Assert.True(warmedExposure.AmbientTemperature > plainExposure.AmbientTemperature);
		Assert.True(warmedExposure.WetnessDelta < plainExposure.WetnessDelta);
	}

	[Fact]
	public void Capture_Wildfire_AddsTemperature_AndDrying()
	{
		var state = CreateState(worldSeed: 9001);
		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Rain,
			Intensity = WeatherIntensity.Light,
		};

		var plain = CreateActor("plain_fire", 4, 4, z: 0);
		var nearFire = CreateActor("near_fire", 7, 4, z: 0);
		FireSystem.TryIgniteCell(state, 8, 4, 0, intensity: GameConfig.Fire.MaxIntensity, fuel: 30);

		var plainExposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, plain);
		var fireExposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, nearFire);

		Assert.True(fireExposure.HeatSourceTemperatureBonus > plainExposure.HeatSourceTemperatureBonus);
		Assert.True(fireExposure.DryingBonus > plainExposure.DryingBonus);
		Assert.True(fireExposure.AmbientTemperature > plainExposure.AmbientTemperature);
	}

	[Fact]
	public void ActorProtection_AggregatesCoveredParts_AndReducesExposure()
	{
		var state = CreateState(worldSeed: 777);
		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Storm,
			Intensity = WeatherIntensity.Heavy,
		};

		var unclothed = CreateActor("unclothed", 5, 5, z: 0);
		var clothed = CreateActor("clothed", 6, 5, z: 0);
		Equip(clothed, "parka");
		Equip(clothed, "tuque");

		Assert.InRange(clothed.GetColdInsulation(), 35.5f, 35.9f);
		Assert.InRange(clothed.GetHeatInsulation(), 1.5f, 1.6f);
		Assert.InRange(clothed.GetWaterproofing(), 14.8f, 14.9f);

		var unclothedExposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, unclothed);
		var clothedExposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, clothed);

		Assert.True(clothedExposure.WetnessDelta < unclothedExposure.WetnessDelta);
	}

	private static GameState CreateState(int worldSeed)
	{
		var state = new GameState
		{
			WorldSeed = worldSeed,
			PlayerId = "player",
			PlayerX = 1,
			PlayerY = 1,
			PlayerZ = 0,
			World = new WorldMap(worldSeed, new FloorGenerator()),
			Weather = WeatherState.CreateDefault(worldSeed),
		};

		for (var z = 0; z <= 1; z++)
		{
			for (var x = 0; x < 12; x++)
			{
				for (var y = 0; y < 12; y++)
					state.World.SetTerrain(x, y, z, Terrains.Floor);
			}
		}

		return state;
	}

	private static Actor CreateActor(string id, int x, int y, int z) => new()
	{
		Id = id,
		DisplayName = id,
		X = x,
		Y = y,
		Z = z,
		Race = new Race
		{
			Id = "human",
			Name = "Human",
			HealthProfileId = HealthCatalog.ResolveProfileId("human", null),
		},
		Limbs =
		[
			CreateLimb($"{id}_head", BodyParts.Head),
			CreateLimb($"{id}_torso", BodyParts.Torso),
			CreateLimb($"{id}_arm", BodyParts.Arm),
			CreateLimb($"{id}_leg", BodyParts.Leg),
		],
	};

	private static Limb CreateLimb(string id, string bodyPart) => new()
	{
		Id = id,
		Name = bodyPart,
		MaxDurability = 10,
		Durability = 10,
		Material = "flesh",
		BodyPart = bodyPart,
		Capacities = new Dictionary<string, float>(),
		Tags = new Dictionary<string, int>(),
	};

	private static void Equip(Actor actor, string itemId)
	{
		var item = PresetDB.CloneItem(itemId);
		item.Equipped = true;
		actor.Inventory.Add(item);
	}

	private static (int X, int Y, WeatherSample Sample) FindSurfaceCell(GameState state, Func<WeatherSample, bool> predicate)
	{
		for (var cx = 0; cx < 256; cx++)
		{
			for (var cy = 0; cy < 256; cy++)
			{
				var x = cx * CoordUtil.ChunkSize + 1;
				var y = cy * CoordUtil.ChunkSize + 1;
				var sample = WeatherFieldSampler.Sample(state, x, y, 0, state.Turn, state.Weather.FrontPhase);
				if (predicate(sample))
					return (x, y, sample);
			}
		}

		throw new InvalidOperationException("Unable to find a matching weather sample.");
	}

	private static void SetSurfaceAccumulation(GameState state, int x, int y, int z, byte wetness, byte snow, byte sand, byte ice)
	{
		var coord = CoordUtil.WorldToChunk(x, y, z);
		var chunk = state.World!.Chunks.GetOrLoad(coord);
		var (lx, ly) = CoordUtil.WorldToLocal(x, y);
		var index = CoordUtil.LocalIndex(lx, ly);
		chunk.Wetness[index] = wetness;
		chunk.SnowDepth[index] = snow;
		chunk.SandDepth[index] = sand;
		chunk.IceDepth[index] = ice;
	}

	private static void BuildRoofedRoom(GameState state, int left, int top, int width, int height)
	{
		for (var x = left; x < left + width; x++)
		{
			for (var y = top; y < top + height; y++)
			{
				var isBorder = x == left || x == left + width - 1 || y == top || y == top + height - 1;
				state.World!.SetTerrain(x, y, 0, isBorder ? Terrains.WallStone : Terrains.Floor);
				if (!isBorder)
					state.World.SetFixture(x, y, 0, "H", Entities.House);
			}
		}
	}

	private static void AddFacility(GameState state, FacilityInstance facility)
	{
		state.Facilities[facility.Id] = facility;
		state.World!.AttachFacilityState(state.Facilities);
	}

	private static float Lerp(float from, float to, float t) =>
		from + (to - from) * Math.Clamp(t, 0f, 1f);

	private sealed class FloorGenerator : IMapGenerator
	{
		public string Id => "floor";
		public string Name => "Floor";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
			chunk.Fill(TerrainRegistry.GetId(Terrains.Floor));
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
