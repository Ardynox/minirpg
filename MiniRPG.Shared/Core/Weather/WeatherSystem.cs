using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using MiniRPG.Core.Map;
using MiniRPG.Core.World;

namespace MiniRPG.Core.Weather;

public enum WeatherType
{
	Clear,
	Rain,
	Fog,
	Snow,
	Storm,
	Thunderstorm,
	Sandstorm,
}

public enum WeatherIntensity
{
	Light,
	Normal,
	Heavy,
}

public sealed class WeatherState
{
	public float FrontPhase { get; set; }
	public WeatherDebugOverride? DebugOverride { get; set; }
	public WeatherLocalSnapshot? LastLocalWeather { get; set; }

	public static WeatherState CreateDefault(int worldSeed) => new()
	{
		FrontPhase = WeatherMath.SeedPhase(worldSeed),
	};

	public void ResetForWorld(int worldSeed)
	{
		FrontPhase = WeatherMath.SeedPhase(worldSeed);
		DebugOverride = null;
		LastLocalWeather = null;
	}
}

public sealed class WeatherDebugOverride
{
	public WeatherType Type { get; set; } = WeatherType.Clear;
	public WeatherIntensity Intensity { get; set; } = WeatherIntensity.Normal;
}

public sealed class WeatherLocalSnapshot
{
	public WeatherType Type { get; set; } = WeatherType.Clear;
	public WeatherIntensity Intensity { get; set; } = WeatherIntensity.Normal;
	public int Turn { get; set; }

	public static WeatherLocalSnapshot FromSample(WeatherSample sample, int turn) => new()
	{
		Type = sample.Type,
		Intensity = sample.Intensity,
		Turn = turn,
	};
}

public readonly record struct WeatherSample(
	WeatherType Type,
	WeatherIntensity Intensity,
	float Severity = 0f,
	float TemperatureNormalized = 0.5f,
	float AmbientTemperatureC = 10f)
{
	public bool HasActiveWeather => Type != WeatherType.Clear;
	public bool HasReducedVisibility => Type is not WeatherType.Clear;
	public string TypeId => WeatherIds.ToId(Type);
	public string IntensityId => WeatherIds.ToId(Intensity);
}

public readonly record struct WeatherAccumulation(
	byte SnowDepth,
	byte SandDepth,
	byte Wetness,
	byte IceDepth);

public readonly record struct WeatherSurfaceState(
	TerrainDef BaseTerrain,
	WeatherAccumulation Accumulation,
	bool IsExposed)
{
	public bool HasSnowCover => Accumulation.SnowDepth >= GameConfig.Weather.SnowCoverThreshold;
	public bool HasSandCover => Accumulation.SandDepth >= GameConfig.Weather.SandCoverThreshold;
	public bool HasWetGloss => Accumulation.Wetness >= GameConfig.Weather.WetGlossThreshold;
	public bool HasIceGloss => Accumulation.IceDepth >= GameConfig.Weather.IceGlossThreshold;

	public string EffectiveTerrainId => HasIceGloss
		? Terrains.Ice
		: HasSnowCover
			? Terrains.Snow
			: HasSandCover
				? Terrains.Sand
			: BaseTerrain.StringId;
}

public static class WeatherIds
{
	public static string ToId(WeatherType type) => type switch
	{
		WeatherType.Clear => "clear",
		WeatherType.Rain => "rain",
		WeatherType.Fog => "fog",
		WeatherType.Snow => "snow",
		WeatherType.Storm => "storm",
		WeatherType.Thunderstorm => "thunderstorm",
		WeatherType.Sandstorm => "sandstorm",
		_ => "clear",
	};

	public static string ToId(WeatherIntensity intensity) => intensity switch
	{
		WeatherIntensity.Light => "light",
		WeatherIntensity.Heavy => "heavy",
		_ => "normal",
	};

	public static bool TryParseType(string? id, out WeatherType type)
	{
		type = id?.Trim().ToLowerInvariant() switch
		{
			"clear" => WeatherType.Clear,
			"rain" => WeatherType.Rain,
			"fog" => WeatherType.Fog,
			"snow" => WeatherType.Snow,
			"storm" => WeatherType.Storm,
			"thunderstorm" => WeatherType.Thunderstorm,
			"sandstorm" => WeatherType.Sandstorm,
			_ => WeatherType.Clear,
		};
		return id != null && type != WeatherType.Clear || string.Equals(id, "clear", StringComparison.OrdinalIgnoreCase);
	}

	public static bool TryParseIntensity(string? id, out WeatherIntensity intensity)
	{
		intensity = id?.Trim().ToLowerInvariant() switch
		{
			"light" => WeatherIntensity.Light,
			"heavy" => WeatherIntensity.Heavy,
			_ => WeatherIntensity.Normal,
		};
		return id != null && intensity != WeatherIntensity.Normal || string.Equals(id, "normal", StringComparison.OrdinalIgnoreCase);
	}
}

public static class WeatherRules
{
	public static WeatherSample GetLocalWeather(GameState state, int x, int y, int z)
	{
		if (state.World == null || !state.World.IsWeatherExposed(x, y, z))
			return new WeatherSample(WeatherType.Clear, WeatherIntensity.Normal);

		return WeatherFieldSampler.Sample(state, x, y, z);
	}

	public static float GetVisionMultiplier(WeatherSample sample) =>
		Math.Clamp(GetProfile(sample.Type).VisionMultiplier, 0.1f, 1.0f);

	public static float GetAiVisionMultiplier(WeatherSample sample)
	{
		var profile = GetProfile(sample.Type);
		var multiplier = profile.AiVisionMultiplier > 0f
			? profile.AiVisionMultiplier
			: profile.VisionMultiplier;
		return Math.Clamp(multiplier, 0.1f, 1.0f);
	}

	public static float GetExposedSpeedMultiplier(WeatherSample sample) =>
		Math.Clamp(GetProfile(sample.Type).ExposedSpeedMultiplier, 0.1f, 1.5f);

	public static int GetRangedPenalty(WeatherSample sample) =>
		Math.Max(0, GetProfile(sample.Type).RangedPenalty);

	public static bool HasLightning(WeatherSample sample) =>
		sample.Type == WeatherType.Thunderstorm;

	public static float GetIntensityMultiplier(WeatherIntensity intensity)
	{
		var key = WeatherIds.ToId(intensity);
		if (GameConfig.Weather.IntensityMultipliers.TryGetValue(key, out var value))
			return Math.Max(0f, value);

		return intensity switch
		{
			WeatherIntensity.Light => 0.65f,
			WeatherIntensity.Heavy => 1.45f,
			_ => 1.0f,
		};
	}

	public static float GetAmbientTemperatureC(float temperatureNormalized) =>
		WeatherMath.Lerp(
			GameConfig.Weather.AmbientTemperatureMinC,
			GameConfig.Weather.AmbientTemperatureMaxC,
			Math.Clamp(temperatureNormalized, 0f, 1f));

	public static WeatherAccumulationDelta GetAccumulationDelta(WeatherSample sample)
	{
		var profile = GetProfile(sample.Type);
		var factor = sample.Type == WeatherType.Clear
			? 1f
			: GetIntensityMultiplier(sample.Intensity);
		return new WeatherAccumulationDelta(
			ScaleDelta(profile.SnowDelta, factor),
			ScaleDelta(profile.SandDelta, factor),
			ScaleDelta(profile.WetnessDelta, factor),
			ScaleDelta(profile.IceDelta, factor));
	}

	private static WeatherProfileConfig GetProfile(WeatherType type)
	{
		var key = WeatherIds.ToId(type);
		if (GameConfig.Weather.Profiles.TryGetValue(key, out var profile))
			return profile;

		return new WeatherProfileConfig();
	}

	private static int ScaleDelta(int delta, float factor) =>
		(int)MathF.Round(delta * factor, MidpointRounding.AwayFromZero);
}

public static class WeatherSurface
{
	public static WeatherAccumulation GetAccumulation(GameState state, int x, int y, int z)
	{
		if (state.World == null)
			return default;

		var coord = CoordUtil.WorldToChunk(x, y, z);
		var (lx, ly) = CoordUtil.WorldToLocal(x, y);
		var index = ly * ChunkData.Size + lx;
		if (state.World.Chunks.IsLoaded(coord))
		{
			var chunk = state.World.Chunks.GetOrLoad(coord);
			return new WeatherAccumulation(
				ReadAt(chunk.SnowDepth, index),
				ReadAt(chunk.SandDepth, index),
				ReadAt(chunk.Wetness, index),
				ReadAt(chunk.IceDepth, index));
		}

		if (SaveModule.DirtyChunkCache.TryGetValue(coord, out var snapshot))
		{
			return new WeatherAccumulation(
				ReadAt(snapshot.SnowDepth, index),
				ReadAt(snapshot.SandDepth, index),
				ReadAt(snapshot.Wetness, index),
				ReadAt(snapshot.IceDepth, index));
		}

		return default;
	}

	public static WeatherSurfaceState GetSurfaceState(GameState state, int x, int y, int z)
	{
		var baseTerrain = state.World?.GetTerrain(x, y, z)
			?? TerrainRegistry.Get((ushort)0);
		var accumulation = GetAccumulation(state, x, y, z);
		var isExposed = state.World?.IsWeatherExposed(x, y, z) ?? false;
		return new WeatherSurfaceState(baseTerrain, accumulation, isExposed);
	}

	private static byte ReadAt(byte[]? values, int index) =>
		values != null && index >= 0 && index < values.Length
			? values[index]
			: (byte)0;
}

public static class WeatherFieldSampler
{
	public static WeatherSample Sample(GameState state, int x, int y, int z) =>
		Sample(state, x, y, z, state.Turn, state.Weather.FrontPhase);

	public static WeatherSample Sample(GameState state, ChunkCoord coord, int turn, float frontPhase)
	{
		var center = CoordUtil.LocalToWorld(coord, ChunkData.Size / 2, ChunkData.Size / 2);
		return Sample(state, center.X, center.Y, center.Z, turn, frontPhase);
	}

	public static WeatherSample Sample(GameState state, int x, int y, int z, int turn, float frontPhase)
	{
		var config = GameConfig.Weather;
		var regionSize = Math.Max(1, config.RegionSizeChunks);
		var chunk = CoordUtil.WorldToChunk(x, y, z);
		var sampleX = (chunk.Cx + 0.5f) / regionSize;
		var sampleY = (chunk.Cy + 0.5f) / regionSize;

		var regionX = WeatherMath.FastFloor(sampleX);
		var regionY = WeatherMath.FastFloor(sampleY);
		var tx = WeatherMath.SmoothStep(sampleX - regionX);
		var ty = WeatherMath.SmoothStep(sampleY - regionY);
		var phase = frontPhase + turn * config.FrontTurnScale;

		var basis00 = SampleBasis(state.WorldSeed, regionX, regionY, phase);
		var basis10 = SampleBasis(state.WorldSeed, regionX + 1, regionY, phase);
		var basis01 = SampleBasis(state.WorldSeed, regionX, regionY + 1, phase);
		var basis11 = SampleBasis(state.WorldSeed, regionX + 1, regionY + 1, phase);
		var basis = WeatherBasis.Lerp(
			WeatherBasis.Lerp(basis00, basis10, tx),
			WeatherBasis.Lerp(basis01, basis11, tx),
			ty);

		if (z != 0)
			return BuildSample(WeatherType.Clear, 0f, basis.Temperature, WeatherRules.GetAmbientTemperatureC(basis.Temperature), WeatherIntensity.Normal);

		if (state.Weather.DebugOverride is { } debugOverride)
			return BuildSample(debugOverride.Type, 1f, basis.Temperature, WeatherRules.GetAmbientTemperatureC(basis.Temperature), debugOverride.Intensity);

		return ResolveSample(basis);
	}

	private static WeatherBasis SampleBasis(int worldSeed, int regionX, int regionY, float phase) => new(
		Moisture: WeatherMath.Oscillate(worldSeed, regionX, regionY, 101, phase, 0.85f, 0.37f, -0.19f),
		Temperature: WeatherMath.Oscillate(worldSeed, regionX, regionY, 211, phase, 0.49f, -0.16f, 0.24f),
		Instability: WeatherMath.Oscillate(worldSeed, regionX, regionY, 307, phase, 1.18f, 0.28f, 0.13f),
		Dust: WeatherMath.Oscillate(worldSeed, regionX, regionY, 401, phase, 0.93f, -0.31f, 0.29f));

	private static WeatherSample ResolveSample(WeatherBasis basis)
	{
		var moisture = basis.Moisture;
		var coldness = 1f - basis.Temperature;
		var instability = basis.Instability;
		var dust = basis.Dust * (1f - moisture * 0.45f);
		var ambientTemperature = WeatherRules.GetAmbientTemperatureC(basis.Temperature);

		if (dust > 0.74f && moisture < 0.34f)
			return BuildSample(WeatherType.Sandstorm, (dust + (1f - moisture)) * 0.5f, basis.Temperature, ambientTemperature);

		if (moisture > 0.72f && instability > 0.78f)
			return BuildSample(WeatherType.Thunderstorm, (moisture + instability) * 0.5f, basis.Temperature, ambientTemperature);

		if (moisture > 0.60f && instability > 0.64f)
			return BuildSample(WeatherType.Storm, (moisture + instability) * 0.5f, basis.Temperature, ambientTemperature);

		if (moisture > 0.54f && coldness > 0.58f)
			return BuildSample(WeatherType.Snow, (moisture + coldness) * 0.5f, basis.Temperature, ambientTemperature);

		if (moisture > 0.50f && instability < 0.46f)
			return BuildSample(WeatherType.Fog, (moisture + (1f - instability)) * 0.5f, basis.Temperature, ambientTemperature);

		if (moisture > 0.56f)
			return BuildSample(WeatherType.Rain, moisture, basis.Temperature, ambientTemperature);

		return BuildSample(WeatherType.Clear, 0f, basis.Temperature, ambientTemperature, WeatherIntensity.Normal);
	}

	private static WeatherSample BuildSample(
		WeatherType type,
		float severity,
		float temperatureNormalized,
		float ambientTemperatureC,
		WeatherIntensity? intensityOverride = null)
	{
		var clampedSeverity = Math.Clamp(severity, 0f, 1f);
		var intensity = intensityOverride ?? clampedSeverity switch
		{
			>= 0.82f => WeatherIntensity.Heavy,
			>= 0.60f => WeatherIntensity.Normal,
			_ => WeatherIntensity.Light,
		};
		return new WeatherSample(
			type,
			intensity,
			clampedSeverity,
			Math.Clamp(temperatureNormalized, 0f, 1f),
			ambientTemperatureC);
	}

	private readonly record struct WeatherBasis(
		float Moisture,
		float Temperature,
		float Instability,
		float Dust)
	{
		public static WeatherBasis Lerp(WeatherBasis a, WeatherBasis b, float t) => new(
			WeatherMath.Lerp(a.Moisture, b.Moisture, t),
			WeatherMath.Lerp(a.Temperature, b.Temperature, t),
			WeatherMath.Lerp(a.Instability, b.Instability, t),
			WeatherMath.Lerp(a.Dust, b.Dust, t));
	}
}

public static class WeatherAccumulationSimulator
{
	private const float LightningBaseWeight = 1.0f;
	private const float LightningConductivityInfluence = 10.0f;

	public static List<GameEvent> Advance(GameState state)
	{
		state.Weather ??= WeatherState.CreateDefault(state.WorldSeed);
		state.Weather.FrontPhase = WeatherMath.WrapPhase(state.Weather.FrontPhase + GameConfig.Weather.FrontPhaseStep);

		var events = new List<GameEvent>();
		var world = state.World;
		if (world == null)
			return events;

		var loadedChunks = world.Chunks.LoadedChunks
			.OrderBy(static entry => entry.Key.Cz)
			.ThenBy(static entry => entry.Key.Cy)
			.ThenBy(static entry => entry.Key.Cx);
		foreach (var (coord, chunk) in loadedChunks)
			SimulateChunk(state, coord, chunk);

		EmitLightningPulse(state, events);
		UpdatePlayerWeatherSnapshot(state, events);
		return events;
	}

	public static void ClearAccumulation(GameState state)
	{
		if (state.World != null)
		{
			foreach (var chunk in state.World.Chunks.LoadedChunks.Values)
			{
				Array.Fill(chunk.SnowDepth, (byte)0);
				Array.Fill(chunk.SandDepth, (byte)0);
				Array.Fill(chunk.Wetness, (byte)0);
				Array.Fill(chunk.IceDepth, (byte)0);
				chunk.LastWeatherSimTurn = state.Turn;
				chunk.Dirty = true;
			}
		}

		foreach (var snapshot in SaveModule.DirtyChunkCache.Values)
		{
			snapshot.SnowDepth ??= [];
			snapshot.SandDepth ??= [];
			snapshot.Wetness ??= [];
			snapshot.IceDepth ??= [];
			Array.Fill(snapshot.SnowDepth, (byte)0);
			Array.Fill(snapshot.SandDepth, (byte)0);
			Array.Fill(snapshot.Wetness, (byte)0);
			Array.Fill(snapshot.IceDepth, (byte)0);
			snapshot.LastWeatherSimTurn = state.Turn;
		}
	}

	private static void SimulateChunk(GameState state, ChunkCoord coord, ChunkData chunk)
	{
		if (coord.Cz != 0)
		{
			chunk.LastWeatherSimTurn = Math.Max(chunk.LastWeatherSimTurn, state.Turn);
			return;
		}

		var firstTurn = chunk.LastWeatherSimTurn > 0
			? chunk.LastWeatherSimTurn + 1
			: state.Turn;
		if (firstTurn > state.Turn)
			return;

		for (var turn = firstTurn; turn <= state.Turn; turn++)
		{
			var historicalPhase = WeatherMath.WrapPhase(
				state.Weather.FrontPhase - (state.Turn - turn) * GameConfig.Weather.FrontPhaseStep);
			var sample = WeatherFieldSampler.Sample(state, coord, turn, historicalPhase);
			ApplySampleToChunk(state.World!, coord, chunk, sample);
		}

		chunk.LastWeatherSimTurn = state.Turn;
	}

	private static void ApplySampleToChunk(WorldMap world, ChunkCoord coord, ChunkData chunk, WeatherSample sample)
	{
		var delta = WeatherRules.GetAccumulationDelta(sample);
		var config = GameConfig.Weather;
		var changed = false;

		for (var index = 0; index < ChunkData.Area; index++)
		{
			var (lx, ly) = CoordUtil.IndexToLocal(index);
			var worldPos = CoordUtil.LocalToWorld(coord, lx, ly);
			if (!world.IsWeatherExposed(worldPos.X, worldPos.Y, worldPos.Z))
				continue;

			var terrain = TerrainRegistry.Get(chunk.TerrainIds[index]);
			var snow = chunk.SnowDepth[index];
			var sand = chunk.SandDepth[index];
			var wetness = chunk.Wetness[index];
			var ice = chunk.IceDepth[index];

			var nextSnow = WeatherMath.ApplyDelta(snow, delta.Snow);
			var nextSand = WeatherMath.ApplyDelta(sand, delta.Sand);
			var nextWetness = WeatherMath.ApplyDelta(wetness, delta.Wetness);
			var nextIce = WeatherMath.ApplyDelta(ice, delta.Ice);

			if (sample.Type == WeatherType.Snow
				&& (string.Equals(terrain.StringId, Terrains.Water, StringComparison.Ordinal)
					|| wetness >= config.HighWetnessIceThreshold))
			{
				nextIce = WeatherMath.ApplyDelta(nextIce, Math.Max(1, delta.Ice == 0 ? 1 : delta.Ice));
			}

			if (sample.Type == WeatherType.Clear
				&& string.Equals(terrain.StringId, Terrains.Water, StringComparison.Ordinal))
			{
				nextIce = WeatherMath.ApplyDelta(nextIce, Math.Min(-1, delta.Ice - 1));
			}

			if (nextSnow == snow
				&& nextSand == sand
				&& nextWetness == wetness
				&& nextIce == ice)
			{
				continue;
			}

			chunk.SnowDepth[index] = nextSnow;
			chunk.SandDepth[index] = nextSand;
			chunk.Wetness[index] = nextWetness;
			chunk.IceDepth[index] = nextIce;
			changed = true;
		}

		if (changed)
			chunk.Dirty = true;
	}

	private static void EmitLightningPulse(GameState state, List<GameEvent> events)
	{
		var world = state.World;
		if (world == null)
			return;

		var interval = Math.Max(1, GameConfig.Weather.LightningIntervalTurns);
		if (state.Turn <= 0 || state.Turn % interval != 0)
			return;

		var strikesPerPulse = Math.Max(1, GameConfig.Weather.LightningStrikesPerPulse);
		foreach (var (coord, chunk) in world.Chunks.LoadedChunks
			.OrderBy(static entry => entry.Key.Cz)
			.ThenBy(static entry => entry.Key.Cy)
			.ThenBy(static entry => entry.Key.Cx))
		{
			if (coord.Cz != 0)
				continue;

			var sample = WeatherFieldSampler.Sample(state, coord, state.Turn, state.Weather.FrontPhase);
			if (!WeatherRules.HasLightning(sample))
				continue;

			for (var strikeIndex = 0; strikeIndex < strikesPerPulse; strikeIndex++)
			{
				var target = SelectLightningTarget(state, world, coord, chunk, strikeIndex);
				if (target is not { Actor: { } targetActor, Limb: { } targetLimb })
					continue;

				events.Add(new GameEvent("weather_lightning_strike")
				{
					TargetId = targetActor.Id,
					TargetActorName = IdentificationModule.GetActorDisplayName(state, targetActor),
					TargetX = targetActor.X,
					TargetY = targetActor.Y,
					TargetZ = targetActor.Z,
					SourceX = targetActor.X,
					SourceY = targetActor.Y,
					Damage = Math.Max(1, GameConfig.Weather.LightningDamage),
					DamageType = DamageTypes.Lightning,
					WeatherTypeId = sample.TypeId,
					WeatherIntensityId = sample.IntensityId,
					LimbName = targetLimb.Name,
				});
				events.AddRange(CombatModule.ApplyEnvironmentalDamage(
					state,
					targetActor,
					targetLimb,
					Math.Max(1, GameConfig.Weather.LightningDamage),
					DamageTypes.Lightning));
				events.AddRange(FireSystem.TryIgniteActor(
					state,
					targetActor,
					intensity: Math.Max(1, GameConfig.Fire.InitialIntensity),
					source: DamageTypes.Lightning));
			}
		}
	}

	internal static (Actor Actor, Limb Limb)? SelectLightningTarget(
		GameState state,
		WorldMap world,
		ChunkCoord coord,
		ChunkData chunk,
		int strikeIndex)
	{
		var actors = chunk.ActorIds
			.Select(actorId => state.Actors.TryGetValue(actorId, out var actor) ? actor : null)
			.Where(static actor => actor != null)
			.Select(static actor => actor!)
			.Where(actor =>
				actor.Z == 0
				&& actor.Limbs.Count > 0
				&& !CombatModule.IsDead(actor)
				&& world.IsWeatherExposed(actor.X, actor.Y, actor.Z))
			.OrderBy(static actor => actor.Id, StringComparer.Ordinal)
			.ToList();
		if (actors.Count == 0)
			return null;

		var weightedActors = actors
			.Select(actor => (Actor: actor, Weight: GetActorLightningWeight(actor)))
			.Where(static entry => entry.Weight > 0f)
			.ToList();
		if (weightedActors.Count == 0)
			return null;

		var actorRoll = WeatherMath.HashNormalized(
			state.WorldSeed,
			state.Turn,
			coord.Cx,
			coord.Cy,
			coord.Cz,
			strikeIndex,
			173);
		var actor = PickWeighted(weightedActors, actorRoll);
		if (actor == null)
			return null;

		var weightedLimbs = actor.Limbs
			.OrderBy(static limb => limb.Id, StringComparer.Ordinal)
			.Select(limb => (Limb: limb, Weight: GetLimbLightningWeight(actor, limb)))
			.Where(static entry => entry.Weight > 0f)
			.ToList();
		if (weightedLimbs.Count == 0)
			return null;

		var limbRoll = WeatherMath.HashNormalized(
			state.WorldSeed,
			state.Turn,
			coord.Cx,
			coord.Cy,
			coord.Cz,
			strikeIndex,
			WeatherMath.StableHash(actor.Id),
			587);
		var limb = PickWeighted(weightedLimbs, limbRoll);
		return limb == null ? null : (actor, limb);
	}

	internal static float GetActorLightningWeight(Actor actor)
	{
		var conductivitySum = actor.Limbs.Sum(limb => GetLimbEffectiveConductivity(actor, limb));
		return LightningBaseWeight + conductivitySum * LightningConductivityInfluence;
	}

	internal static float GetLimbLightningWeight(Actor actor, Limb limb) =>
		LightningBaseWeight + GetLimbEffectiveConductivity(actor, limb) * LightningConductivityInfluence;

	internal static float GetLimbEffectiveConductivity(Actor actor, Limb limb)
	{
		var limbConductivity = GetMaterialConductivity(limb.Material);
		var equipmentConductivity = string.IsNullOrWhiteSpace(limb.BodyPart)
			? 0f
			: actor.GetEquippedItemsCovering(limb.BodyPart)
				.Select(item => GetMaterialConductivity(item.MaterialId))
				.DefaultIfEmpty(0f)
				.Max();
		return Math.Max(limbConductivity, equipmentConductivity);
	}

	private static float GetMaterialConductivity(string? materialId) =>
		string.IsNullOrWhiteSpace(materialId)
			? 0f
			: Math.Clamp(MaterialRegistry.Get(materialId).Conductivity, 0f, 1f);

	private static T? PickWeighted<T>(IReadOnlyList<(T Item, float Weight)> entries, float roll01) where T : class
	{
		if (entries.Count == 0)
			return null;

		var totalWeight = entries.Sum(static entry => Math.Max(0f, entry.Weight));
		if (totalWeight <= 0f)
			return null;

		var target = Math.Clamp(roll01, 0f, 0.999999f) * totalWeight;
		var cumulative = 0f;
		foreach (var (item, weight) in entries)
		{
			cumulative += Math.Max(0f, weight);
			if (target < cumulative)
				return item;
		}

		return entries[^1].Item;
	}

	private static void UpdatePlayerWeatherSnapshot(GameState state, List<GameEvent> events)
	{
		if (state.PlayerZ != 0 || state.World == null)
		{
			state.Weather.LastLocalWeather = null;
			return;
		}

		var sample = WeatherRules.GetLocalWeather(state, state.PlayerX, state.PlayerY, state.PlayerZ);
		var last = state.Weather.LastLocalWeather;
		if (last == null
			|| last.Type != sample.Type
			|| last.Intensity != sample.Intensity)
		{
			events.Add(new GameEvent("weather_changed")
			{
				WeatherTypeId = sample.TypeId,
				WeatherIntensityId = sample.IntensityId,
				TargetX = state.PlayerX,
				TargetY = state.PlayerY,
				TargetZ = state.PlayerZ,
			});
		}

		state.Weather.LastLocalWeather = WeatherLocalSnapshot.FromSample(sample, state.Turn);
	}
}

public readonly record struct WeatherAccumulationDelta(
	int Snow,
	int Sand,
	int Wetness,
	int Ice);

internal static class WeatherMath
{
	public static float SeedPhase(int worldSeed) =>
		Hash01(worldSeed, 0, 0, 991) * MathF.Tau;

	public static float WrapPhase(float phase)
	{
		var wrapped = phase % MathF.Tau;
		return wrapped < 0f ? wrapped + MathF.Tau : wrapped;
	}

	public static int FastFloor(float value) =>
		(int)MathF.Floor(value);

	public static float SmoothStep(float t)
	{
		var clamped = Math.Clamp(t, 0f, 1f);
		return clamped * clamped * (3f - 2f * clamped);
	}

	public static float Lerp(float a, float b, float t) =>
		a + (b - a) * t;

	public static float Oscillate(
		int worldSeed,
		int regionX,
		int regionY,
		int salt,
		float phase,
		float phaseScale,
		float xScale,
		float yScale)
	{
		var primary = Hash01(worldSeed, regionX, regionY, salt) * MathF.Tau;
		var secondary = Hash01(worldSeed, regionX, regionY, salt + 7919) * MathF.Tau;
		var waveA = MathF.Sin(primary + phase * phaseScale + regionX * xScale + regionY * yScale);
		var waveB = MathF.Sin(secondary + phase * (phaseScale * 1.7f) + (regionX - regionY) * 0.19f);
		return Math.Clamp(0.5f + waveA * 0.28f + waveB * 0.16f, 0f, 1f);
	}

	public static byte ApplyDelta(byte value, int delta)
	{
		var next = value + delta;
		if (next < byte.MinValue)
			return byte.MinValue;
		if (next > byte.MaxValue)
			return byte.MaxValue;
		return (byte)next;
	}

	public static int HashInt(params int[] values)
	{
		unchecked
		{
			var hash = 17;
			foreach (var value in values)
				hash = (hash * 397) ^ value;
			return hash & int.MaxValue;
		}
	}

	public static float HashNormalized(params int[] values) =>
		HashInt(values) / (float)int.MaxValue;

	public static int StableHash(string? value)
	{
		if (string.IsNullOrEmpty(value))
			return 0;

		unchecked
		{
			var hash = 23;
			foreach (var ch in value)
				hash = (hash * 397) ^ ch;
			return hash & int.MaxValue;
		}
	}

	private static float Hash01(int seed, int x, int y, int salt)
	{
		unchecked
		{
			var hash = seed;
			hash = (hash * 397) ^ x;
			hash = (hash * 397) ^ y;
			hash = (hash * 397) ^ salt;
			hash ^= hash >> 16;
			hash *= 0x45d9f3b;
			hash ^= hash >> 16;
			var normalized = (uint)hash / (float)uint.MaxValue;
			return normalized;
		}
	}
}
