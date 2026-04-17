using System;
using System.Collections.Generic;
using MiniRPG.Core.Map;

namespace MiniRPG.Core.World.Generators;

/// <summary>
/// Stores normalized world-generation settings so generators can respect
/// per-world creation sliders without widening the generator interface.
/// </summary>
public static class WorldGenerationSettingsRegistry
{
	private static readonly object SyncRoot = new();
	private static readonly Dictionary<WorldGenerationKey, WorldSettings> SettingsByWorld = new();

	public static void Register(int worldSeed, string generatorId, WorldSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		lock (SyncRoot)
		{
			SettingsByWorld[new WorldGenerationKey(worldSeed, NormalizeGeneratorId(generatorId))] = settings.Clone();
		}
	}

	public static WorldSettings Resolve(int worldSeed, string generatorId)
	{
		lock (SyncRoot)
		{
			if (SettingsByWorld.TryGetValue(new WorldGenerationKey(worldSeed, NormalizeGeneratorId(generatorId)), out var settings))
				return settings.Clone();
		}

		return WorldSettings.CreateDefault();
	}

	public static int ScaleNestChancePercent(int worldSeed, string generatorId, int baseChancePercent)
	{
		var settings = Resolve(worldSeed, generatorId);
		if (settings.MonsterDensityPercent <= 0 || settings.NestIntensityPercent <= 0)
			return 0;

		return Math.Clamp(
			(int)Math.Round(baseChancePercent * settings.NestIntensityPercent / 100.0, MidpointRounding.AwayFromZero),
			0,
			100);
	}

	public static int ScaleNestSpawnInterval(int worldSeed, string generatorId, int baseInterval)
	{
		var densityPercent = ResolveNestSpawnRatePercent(Resolve(worldSeed, generatorId));
		if (densityPercent <= 0)
			return int.MaxValue;

		return Math.Max(
			1,
			(int)Math.Round(baseInterval * 100.0 / densityPercent, MidpointRounding.AwayFromZero));
	}

	public static int ScaleNestMaxSpawned(int worldSeed, string generatorId, int baseMaxSpawned)
	{
		var densityPercent = ResolveNestSpawnRatePercent(Resolve(worldSeed, generatorId));
		if (densityPercent <= 0 || baseMaxSpawned <= 0)
			return 0;

		return Math.Max(
			1,
			(int)Math.Round(baseMaxSpawned * densityPercent / 100.0, MidpointRounding.AwayFromZero));
	}

	private static int ResolveNestSpawnRatePercent(WorldSettings settings)
	{
		var densityPercent = Math.Clamp(settings.MonsterDensityPercent, 0, 500);
		if (densityPercent <= 0)
			return 0;

		// Nest intensity below 100 already reduces nest placement chance.
		// Values above 100 should keep increasing actual creature generation,
		// so the overflow also feeds spawn cadence and per-nest cap.
		var overflowNestPercent = Math.Max(100, Math.Clamp(settings.NestIntensityPercent, 0, 500));
		return Math.Max(
			0,
			(int)Math.Round(densityPercent * overflowNestPercent / 100.0, MidpointRounding.AwayFromZero));
	}

	private static string NormalizeGeneratorId(string? generatorId) =>
		string.IsNullOrWhiteSpace(generatorId) ? string.Empty : generatorId.Trim();

	private readonly record struct WorldGenerationKey(int WorldSeed, string GeneratorId);
}
