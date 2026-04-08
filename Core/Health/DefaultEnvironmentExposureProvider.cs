using System;
using MiniRPG.Core.Config;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;

namespace MiniRPG.Core.Health;

public sealed class DefaultEnvironmentExposureProvider : IEnvironmentExposureProvider
{
	public static readonly DefaultEnvironmentExposureProvider Instance = new();

	private DefaultEnvironmentExposureProvider()
	{
	}

	public EnvironmentExposureSnapshot Capture(GameState state, Actor actor)
	{
		return Capture(state, actor, actor.X, actor.Y, actor.Z);
	}

	public EnvironmentExposureSnapshot Capture(GameState state, Actor actor, int x, int y, int z)
	{
		if (state.World == null)
			return EnvironmentExposureSnapshot.Neutral;

		var world = state.World;
		var terrain = world.GetTerrain(x, y, z);
		var surface = WeatherSurface.GetSurfaceState(state, x, y, z);
		var room = RoomContextAnalyzer.AnalyzeRoom(state, x, y, z);
		var roomContext = new RoomContextSnapshot(room.IsIndoors, room.CellCount, room.ShelterStrength);
		var isUnderground = z != 0;
		var isExposed = !isUnderground && world.IsWeatherExposed(x, y, z);
		var weatherState = state.Weather;
		var hasWeatherData = weatherState != null;
		var weatherSample = hasWeatherData
			? WeatherFieldSampler.Sample(state, x, y, 0, state.Turn, weatherState!.FrontPhase)
			: default;
		var heatSourceTemperatureBonus = ResolveHeatSourceTemperatureBonus(state, actor, x, y, z);
		var dryingBonus = ResolveDryingBonus(state, actor, x, y, z);

		var cleanliness = ResolveCleanliness(state, x, y, z, terrain.StringId, surface);
		var sleepSurfaceQuality = ResolveSleepSurfaceQuality(actor, terrain.StringId, x, y, z, room.Modifiers.RestQuality);
		var wetnessDelta = ResolveWetnessDelta(actor, surface, weatherSample, roomContext.IsIndoors, isExposed, isUnderground, hasWeatherData, dryingBonus);
		var ambientTemperature = ResolveAmbientTemperature(actor, weatherSample, roomContext, isExposed, isUnderground, hasWeatherData, heatSourceTemperatureBonus);

		return new EnvironmentExposureSnapshot(
			IsIndoors: roomContext.IsIndoors,
			Cleanliness: cleanliness,
			SleepSurfaceQuality: sleepSurfaceQuality,
			WetnessDelta: wetnessDelta,
			AmbientTemperature: ambientTemperature,
			ShelterStrength: roomContext.ShelterStrength,
			HeatSourceTemperatureBonus: heatSourceTemperatureBonus,
			DryingBonus: dryingBonus,
			HasWeatherData: hasWeatherData);
	}

	private static float ResolveCleanliness(GameState state, int x, int y, int z, string terrainId, WeatherSurfaceState surface)
	{
		var cleanliness = terrainId switch
		{
			Terrains.Floor => 60f,
			Terrains.Grass => 35f,
			Terrains.Gravel => 30f,
			Terrains.Sand => 20f,
			Terrains.Water => 5f,
			Terrains.Swamp => 0f,
			Terrains.Marsh => 0f,
			Terrains.Snow => 12f,
			Terrains.Ice => 18f,
			_ => 40f,
		};

		foreach (var hazard in state.World!.GetEntitiesByType(x, y, z, CellEntityType.Hazard))
		{
			if (string.Equals(hazard.EntityId, "blood_filth", StringComparison.Ordinal))
				cleanliness -= 35f;
			else
				cleanliness -= 15f;
		}

		if (surface.Accumulation.Wetness >= GameConfig.Weather.WetGlossThreshold)
			cleanliness -= 10f;
		if (surface.Accumulation.SnowDepth > 0)
			cleanliness -= 8f;
		if (surface.Accumulation.SandDepth > 0)
			cleanliness -= 12f;

		return Math.Clamp(cleanliness, 0f, 100f);
	}

	private static float ResolveSleepSurfaceQuality(Actor actor, string terrainId, int x, int y, int z, float roomRestModifier)
	{
		var baseQuality = actor.Inventory.Exists(item => string.Equals(item.Id, "bedroll", StringComparison.Ordinal))
			? 90f
			: actor.HasHomePosition && x == actor.HomeX && y == actor.HomeY && z == actor.HomeZ
				? 45f
				: terrainId switch
				{
					Terrains.Floor => 30f,
					Terrains.Grass => 18f,
					Terrains.Snow => 5f,
					Terrains.Water => 0f,
					_ => 20f,
				};

		return Math.Clamp(baseQuality * Math.Max(0.1f, roomRestModifier), 0f, 100f);
	}

	private static float ResolveWetnessDelta(
		Actor actor,
		WeatherSurfaceState surface,
		WeatherSample sample,
		bool indoors,
		bool isExposed,
		bool isUnderground,
		bool hasWeatherData,
		float dryingBonus)
	{
		var positive = surface.BaseTerrain.StringId switch
		{
			Terrains.Water => 16f,
			Terrains.Swamp => 10f,
			Terrains.Marsh => 8f,
			Terrains.Snow => 6f,
			Terrains.Ice => 2f,
			_ => 0f,
		};

		var negative = 0f;
		if (hasWeatherData && isExposed)
			positive += WeatherRules.GetAccumulationDelta(sample).Wetness * GameConfig.Weather.WeatherWetnessScale;

		positive += surface.Accumulation.Wetness / 20f;
		positive += surface.Accumulation.SnowDepth / 24f;

		if (!isExposed)
		{
			negative += indoors || isUnderground ? -3f : -1.5f;
		}

		if (positive > 0f)
			positive *= 1f - actor.GetWaterproofing() / 100f;

		return positive + negative - dryingBonus;
	}

	private static float ResolveAmbientTemperature(
		Actor actor,
		WeatherSample sample,
		RoomContextSnapshot roomContext,
		bool isExposed,
		bool isUnderground,
		bool hasWeatherData,
		float heatSourceTemperatureBonus)
	{
		var config = GameConfig.Weather;
		var ambient = !hasWeatherData
			? EnvironmentExposureSnapshot.Neutral.AmbientTemperature
			: isUnderground
				? config.UndergroundNeutralTemperatureC
				: isExposed
					? sample.AmbientTemperatureC
					: Lerp(sample.AmbientTemperatureC, config.ShelterNeutralTemperatureC, config.ShelterTemperatureLerp);

		if (roomContext.IsIndoors)
		{
			var roomLerp = Lerp(config.RoomRetentionMinLerp, config.RoomRetentionMaxLerp, roomContext.ShelterStrength);
			ambient = Lerp(ambient, config.ShelterNeutralTemperatureC, roomLerp);
		}

		ambient += heatSourceTemperatureBonus;

		if (ambient < config.ShelterNeutralTemperatureC)
			ambient += actor.GetColdInsulation() * 0.12f;
		else if (ambient > config.ShelterNeutralTemperatureC)
			ambient -= actor.GetHeatInsulation() * 0.10f;

		return ambient;
	}

	private static float ResolveHeatSourceTemperatureBonus(GameState state, Actor actor, int x, int y, int z)
	{
		var campfireBonus = ResolveCampfireBonus(state, x, y, z, GameConfig.Weather.CampfirePeakTemperatureBonusC);
		var wildfireBonus = ResolveFireHazardBonus(state, x, y, z, GameConfig.Fire.HazardPeakTemperatureBonusC);
		var facilityBonus = ResolveFacilityBonus(state, x, y, z, static def => def.HeatPerFuelledTickC * 100f);
		return campfireBonus + wildfireBonus + facilityBonus + actor.GetPortableWarmthC();
	}

	private static float ResolveDryingBonus(GameState state, Actor actor, int x, int y, int z)
	{
		var campfireBonus = ResolveCampfireBonus(state, x, y, z, GameConfig.Weather.CampfirePeakDryingBonus);
		var wildfireBonus = ResolveFireHazardBonus(state, x, y, z, GameConfig.Fire.HazardPeakDryingBonus);
		var facilityBonus = ResolveFacilityBonus(state, x, y, z, static def => def.DryingBonusPerFuelledTick * 100f);
		return campfireBonus + wildfireBonus + facilityBonus + actor.GetPortableDryingBonus();
	}

	private static float ResolveFacilityBonus(GameState state, int x, int y, int z, Func<FacilityDef, float> peakSelector)
	{
		if (state.World == null || state.Facilities.Count == 0)
			return 0f;

		var radius = Math.Max(0, GameConfig.Weather.CampfireHeatRadius);
		var best = 0f;
		foreach (var facility in state.Facilities.Values)
		{
			if (facility.Z != z || !TryGetActiveHeatFacility(facility, out var def))
				continue;

			var peakBonus = Math.Max(0f, peakSelector(def));
			if (peakBonus <= 0f)
				continue;

			var distance = GetFacilityDistance(state.World, facility, x, y, z, radius);
			if (distance > radius)
				continue;

			var bonus = Math.Max(0f, peakBonus * (1f - distance / (radius + 1f)));
			if (bonus > best)
				best = bonus;
		}

		return best;
	}

	private static int GetFacilityDistance(WorldMap world, FacilityInstance facility, int x, int y, int z, int maxDistance)
	{
		var best = int.MaxValue;
		foreach (var cell in world.GetFootprintCells(facility))
		{
			if (cell.Z != z)
				continue;

			var distance = Math.Abs(cell.X - x) + Math.Abs(cell.Y - y);
			if (distance < best)
				best = distance;
			if (best == 0)
				return 0;
		}

		return best == int.MaxValue ? maxDistance + 1 : best;
	}

	private static bool TryGetActiveHeatFacility(FacilityInstance facility, out FacilityDef def)
	{
		def = null!;
		if (!facility.IsOperational)
			return false;

		var resolved = FacilityRegistry.Get(facility.FacilityDefId);
		if (resolved == null)
			return false;

		if (resolved.RequiresFuel && facility.FuelTicksRemaining <= 0)
			return false;

		if (resolved.HeatPerFuelledTickC <= 0f && resolved.DryingBonusPerFuelledTick <= 0f)
			return false;

		def = resolved;
		return true;
	}

	private static float ResolveCampfireBonus(GameState state, int x, int y, int z, float peakBonus)
	{
		if (state.World == null || peakBonus <= 0f)
			return 0f;

		var radius = Math.Max(0, GameConfig.Weather.CampfireHeatRadius);
		var best = 0f;
		for (var dy = -radius; dy <= radius; dy++)
		{
			for (var dx = -radius; dx <= radius; dx++)
			{
				var distance = Math.Abs(dx) + Math.Abs(dy);
				if (distance > radius)
					continue;
				if (!state.World.HasFixture(x + dx, y + dy, z, Entities.Campfire))
					continue;

				var bonus = Math.Max(0f, peakBonus * (1f - distance / (radius + 1f)));
				if (bonus > best)
					best = bonus;
			}
		}

		return best;
	}

	private static float ResolveFireHazardBonus(GameState state, int x, int y, int z, float peakBonus)
	{
		if (state.World == null || peakBonus <= 0f)
			return 0f;

		var radius = Math.Max(0, GameConfig.Fire.HazardHeatRadius);
		var best = 0f;
		for (var dy = -radius; dy <= radius; dy++)
		{
			for (var dx = -radius; dx <= radius; dx++)
			{
				var distance = Math.Abs(dx) + Math.Abs(dy);
				if (distance > radius)
					continue;

				var fireIntensity = FireSystem.GetFireIntensityAt(state, x + dx, y + dy, z);
				if (fireIntensity <= 0)
					continue;

				var distanceFactor = 1f - distance / (radius + 1f);
				var intensityFactor = fireIntensity / (float)Math.Max(1, GameConfig.Fire.MaxIntensity);
				var bonus = Math.Max(0f, peakBonus * distanceFactor * intensityFactor);
				if (bonus > best)
					best = bonus;
			}
		}

		return best;
	}

	private static float Lerp(float from, float to, float t) =>
		from + (to - from) * Math.Clamp(t, 0f, 1f);
}
