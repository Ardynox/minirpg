using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniRPG.Core.Config;

public static class GameConfig
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
	};

	public static bool IsLoaded { get; private set; }

	public static PlayerVisionConfig PlayerVision { get; private set; } = new();
	public static AIVisionConfig AIVision { get; private set; } = new();
	public static WorldRuntimeConfig WorldRuntime { get; private set; } = new();
	public static WeatherConfig Weather { get; private set; } = new();
	public static FireConfig Fire { get; private set; } = new();
	public static DebugConfig Debug { get; private set; } = new();
	public static GenerationConfigSet Generation { get; private set; } = new();

	public static void Load()
	{
		if (IsLoaded) return;

		PlayerVision = LoadRequired<PlayerVisionConfig>("Config/player_vision.json");
		AIVision = LoadRequired<AIVisionConfig>("Config/ai_vision.json");
		WorldRuntime = LoadRequired<WorldRuntimeConfig>("Config/world_runtime.json");
		Weather = LoadRequired<WeatherConfig>("Config/weather.json");
		Fire = LoadRequired<FireConfig>("Config/fire.json");
		Debug = LoadRequired<DebugConfig>("Config/debug.json");
		Generation = new GenerationConfigSet
		{
			Bsp = LoadRequired<BspGenerationConfig>("Config/generation/bsp.json"),
			Cellular = LoadRequired<CellularGenerationConfig>("Config/generation/cellular.json"),
			DrunkardWalk = LoadRequired<DrunkardWalkGenerationConfig>("Config/generation/drunkard_walk.json"),
			Perlin = LoadRequired<PerlinGenerationConfig>("Config/generation/perlin.json"),
			RoomCorridor = LoadRequired<RoomCorridorGenerationConfig>("Config/generation/room_corridor.json"),
		};

		IsLoaded = true;
	}

	private static T LoadRequired<T>(string relativeDataPath) where T : class
	{
		var json = GameDataLocator.ReadTextOrThrow(relativeDataPath);
		var data = JsonSerializer.Deserialize<T>(json, JsonOptions);
		if (data == null)
			throw new InvalidOperationException($"Config file is empty or invalid: {relativeDataPath}");

		return data;
	}
}

public sealed class PlayerVisionConfig
{
	[JsonPropertyName("base_vision_radius")]
	public int BaseVisionRadius { get; set; } = 12;

	[JsonPropertyName("rear_vision_ratio")]
	public float RearVisionRatio { get; set; } = 0.4f;

	[JsonPropertyName("ambient_light")]
	public float AmbientLight { get; set; } = 1.0f;

	[JsonPropertyName("minimum_vision_radius")]
	public int MinimumVisionRadius { get; set; } = 2;

	[JsonPropertyName("minimum_rear_vision_radius")]
	public int MinimumRearVisionRadius { get; set; } = 2;
}

public sealed class AIVisionConfig
{
	[JsonPropertyName("activation_view_range")]
	public int ActivationViewRange { get; set; } = 10;

	[JsonPropertyName("simplified_activation_range_multiplier")]
	public int SimplifiedActivationRangeMultiplier { get; set; } = 3;

	[JsonPropertyName("simplified_update_interval_turns")]
	public int SimplifiedUpdateIntervalTurns { get; set; } = 3;

	[JsonPropertyName("local_context_radius")]
	public int LocalContextRadius { get; set; } = 1;

	[JsonPropertyName("full_range")]
	public int FullRange { get; set; } = 8;

	[JsonPropertyName("simplified_range")]
	public int SimplifiedRange { get; set; } = 5;

	[JsonPropertyName("full_shortlist_limit")]
	public int FullShortlistLimit { get; set; } = 8;

	[JsonPropertyName("simplified_shortlist_limit")]
	public int SimplifiedShortlistLimit { get; set; } = 4;

	[JsonPropertyName("rear_vision_ratio")]
	public float RearVisionRatio { get; set; } = 0.5f;

	[JsonPropertyName("gpu_observer_threshold")]
	public int GpuObserverThreshold { get; set; } = 200;

	[JsonPropertyName("gpu_turn_budget_ms")]
	public double GpuTurnBudgetMs { get; set; } = 4.0;

	[JsonPropertyName("gpu_usage_ratio_threshold")]
	public double GpuUsageRatioThreshold { get; set; } = 0.25;
}

public sealed class WorldRuntimeConfig
{
	[JsonPropertyName("chunk_load_radius_xy")]
	public int ChunkLoadRadiusXy { get; set; } = 2;

	[JsonPropertyName("chunk_load_radius_z")]
	public int ChunkLoadRadiusZ { get; set; } = 1;

	[JsonPropertyName("critical_chunk_load_radius_xy")]
	public int CriticalChunkLoadRadiusXy { get; set; } = 1;

	[JsonPropertyName("chunk_background_load_budget_per_frame")]
	public int ChunkBackgroundLoadBudgetPerFrame { get; set; } = 2;

	[JsonPropertyName("max_cached_chunks")]
	public int MaxCachedChunks { get; set; } = 512;

	[JsonPropertyName("chunk_evict_padding_xy")]
	public int ChunkEvictPaddingXy { get; set; } = 2;

	[JsonPropertyName("chunk_simulation_near_radius_xy")]
	public int ChunkSimulationNearRadiusXy { get; set; } = 1;

	[JsonPropertyName("nest_nearby_count_radius")]
	public int NestNearbyCountRadius { get; set; } = 3;

	[JsonPropertyName("fall_damage_free_layers")]
	public int FallDamageFreeLayers { get; set; } = 1;

	[JsonPropertyName("fall_damage_per_layer")]
	public int FallDamagePerLayer { get; set; } = 4;

	[JsonPropertyName("max_fall_layers_per_step")]
	public int MaxFallLayersPerStep { get; set; } = 6;
}

public sealed class WeatherConfig
{
	[JsonPropertyName("region_size_chunks")]
	public int RegionSizeChunks { get; set; } = 4;

	[JsonPropertyName("front_phase_step")]
	public float FrontPhaseStep { get; set; } = 0.0625f;

	[JsonPropertyName("front_turn_scale")]
	public float FrontTurnScale { get; set; } = 0.03125f;

	[JsonPropertyName("lightning_interval_turns")]
	public int LightningIntervalTurns { get; set; } = 6;

	[JsonPropertyName("lightning_damage")]
	public int LightningDamage { get; set; } = 12;

	[JsonPropertyName("lightning_strikes_per_pulse")]
	public int LightningStrikesPerPulse { get; set; } = 1;

	[JsonPropertyName("snow_cover_threshold")]
	public byte SnowCoverThreshold { get; set; } = 28;

	[JsonPropertyName("sand_cover_threshold")]
	public byte SandCoverThreshold { get; set; } = 28;

	[JsonPropertyName("wet_gloss_threshold")]
	public byte WetGlossThreshold { get; set; } = 18;

	[JsonPropertyName("ice_gloss_threshold")]
	public byte IceGlossThreshold { get; set; } = 18;

	[JsonPropertyName("high_wetness_ice_threshold")]
	public byte HighWetnessIceThreshold { get; set; } = 80;

	[JsonPropertyName("ambient_temperature_min_c")]
	public float AmbientTemperatureMinC { get; set; } = -20f;

	[JsonPropertyName("ambient_temperature_max_c")]
	public float AmbientTemperatureMaxC { get; set; } = 40f;

	[JsonPropertyName("shelter_neutral_temperature_c")]
	public float ShelterNeutralTemperatureC { get; set; } = 18f;

	[JsonPropertyName("shelter_temperature_lerp")]
	public float ShelterTemperatureLerp { get; set; } = 0.65f;

	[JsonPropertyName("underground_neutral_temperature_c")]
	public float UndergroundNeutralTemperatureC { get; set; } = 16f;

	[JsonPropertyName("weather_wetness_scale")]
	public float WeatherWetnessScale { get; set; } = 0.35f;

	[JsonPropertyName("campfire_heat_radius")]
	public int CampfireHeatRadius { get; set; } = 4;

	[JsonPropertyName("campfire_peak_temperature_bonus_c")]
	public float CampfirePeakTemperatureBonusC { get; set; } = 16f;

	[JsonPropertyName("campfire_peak_drying_bonus")]
	public float CampfirePeakDryingBonus { get; set; } = 5f;

	[JsonPropertyName("room_retention_min_lerp")]
	public float RoomRetentionMinLerp { get; set; } = 0.20f;

	[JsonPropertyName("room_retention_max_lerp")]
	public float RoomRetentionMaxLerp { get; set; } = 0.55f;

	[JsonPropertyName("intensity_multipliers")]
	public Dictionary<string, float> IntensityMultipliers { get; set; } = new(StringComparer.OrdinalIgnoreCase)
	{
		["light"] = 0.65f,
		["normal"] = 1.0f,
		["heavy"] = 1.45f,
	};

	[JsonPropertyName("profiles")]
	public Dictionary<string, WeatherProfileConfig> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class WeatherProfileConfig
{
	[JsonPropertyName("vision_multiplier")]
	public float VisionMultiplier { get; set; } = 1.0f;

	[JsonPropertyName("ai_vision_multiplier")]
	public float AiVisionMultiplier { get; set; } = 1.0f;

	[JsonPropertyName("exposed_speed_multiplier")]
	public float ExposedSpeedMultiplier { get; set; } = 1.0f;

	[JsonPropertyName("ranged_penalty")]
	public int RangedPenalty { get; set; }

	[JsonPropertyName("snow_delta")]
	public int SnowDelta { get; set; }

	[JsonPropertyName("sand_delta")]
	public int SandDelta { get; set; }

	[JsonPropertyName("wetness_delta")]
	public int WetnessDelta { get; set; }

	[JsonPropertyName("ice_delta")]
	public int IceDelta { get; set; }
}

public sealed class FireConfig
{
	[JsonPropertyName("initial_intensity")]
	public int InitialIntensity { get; set; } = 5;

	[JsonPropertyName("initial_fuel")]
	public int InitialFuel { get; set; } = 20;

	[JsonPropertyName("max_intensity")]
	public int MaxIntensity { get; set; } = 10;

	[JsonPropertyName("fuel_decay_per_turn")]
	public int FuelDecayPerTurn { get; set; } = 2;

	[JsonPropertyName("rain_decay_bonus")]
	public int RainDecayBonus { get; set; } = 2;

	[JsonPropertyName("wetness_decay_divisor")]
	public int WetnessDecayDivisor { get; set; } = 30;

	[JsonPropertyName("spread_base_chance")]
	public float SpreadBaseChance { get; set; } = 0.12f;

	[JsonPropertyName("spread_intensity_factor")]
	public float SpreadIntensityFactor { get; set; } = 0.05f;

	[JsonPropertyName("ignite_threshold")]
	public float IgniteThreshold { get; set; } = 0.15f;

	[JsonPropertyName("item_damage_per_intensity")]
	public float ItemDamagePerIntensity { get; set; } = 0.6f;

	[JsonPropertyName("terrain_damage_per_intensity")]
	public float TerrainDamagePerIntensity { get; set; } = 0.8f;

	[JsonPropertyName("fixture_damage_per_intensity")]
	public float FixtureDamagePerIntensity { get; set; } = 0.9f;

	[JsonPropertyName("actor_damage_per_intensity")]
	public float ActorDamagePerIntensity { get; set; } = 0.45f;

	[JsonPropertyName("on_fire_severity_gain_per_intensity")]
	public float OnFireSeverityGainPerIntensity { get; set; } = 6f;

	[JsonPropertyName("self_extinguish_power")]
	public int SelfExtinguishPower { get; set; } = 12;

	[JsonPropertyName("extinguish_power")]
	public int ExtinguishPower { get; set; } = 7;

	[JsonPropertyName("ai_search_radius")]
	public int AiSearchRadius { get; set; } = 6;

	[JsonPropertyName("ai_home_search_radius")]
	public int AiHomeSearchRadius { get; set; } = 12;

	[JsonPropertyName("adjacent_actor_ignite_threshold")]
	public int AdjacentActorIgniteThreshold { get; set; } = 7;

	[JsonPropertyName("hazard_heat_radius")]
	public int HazardHeatRadius { get; set; } = 3;

	[JsonPropertyName("hazard_peak_temperature_bonus_c")]
	public float HazardPeakTemperatureBonusC { get; set; } = 20f;

	[JsonPropertyName("hazard_peak_drying_bonus")]
	public float HazardPeakDryingBonus { get; set; } = 8f;
}

public sealed class DebugConfig
{
	[JsonPropertyName("auto_test_step_delay")]
	public float AutoTestStepDelay { get; set; } = 0.05f;

	[JsonPropertyName("auto_test_continue_on_failure")]
	public bool AutoTestContinueOnFailure { get; set; } = true;

	[JsonPropertyName("auto_test_stop_on_fail")]
	public bool AutoTestStopOnFail { get; set; }

	[JsonPropertyName("auto_test_scenarios")]
	public List<string> AutoTestScenarios { get; set; } =
		["resource_smoke", "new_game", "qa_smoke_core", "qa_interaction_hub", "qa_combat_arena"];

	[JsonPropertyName("auto_test_enable_resource_smoke")]
	public bool AutoTestEnableResourceSmoke { get; set; } = true;

	[JsonPropertyName("auto_test_write_structured_log")]
	public bool AutoTestWriteStructuredLog { get; set; } = true;

	[JsonPropertyName("auto_test_render_arena_radius")]
	public int AutoTestRenderArenaRadius { get; set; } = 4;

	[JsonPropertyName("auto_test_ai_arena_radius")]
	public int AutoTestAiArenaRadius { get; set; } = 10;

	[JsonPropertyName("auto_test_benchmark_arena_radius")]
	public int AutoTestBenchmarkArenaRadius { get; set; } = 12;

	[JsonPropertyName("auto_test_benchmark_counts")]
	public List<int> AutoTestBenchmarkCounts { get; set; } = [10, 50, 200];

	[JsonPropertyName("auto_test_ai_vision_warn_ms")]
	public double AutoTestAiVisionWarnMs { get; set; } = 4.0;

	[JsonPropertyName("auto_test_tick_warn_ms")]
	public double AutoTestTickWarnMs { get; set; } = 16.0;
}

public sealed class GenerationConfigSet
{
	public BspGenerationConfig Bsp { get; set; } = new();
	public CellularGenerationConfig Cellular { get; set; } = new();
	public DrunkardWalkGenerationConfig DrunkardWalk { get; set; } = new();
	public PerlinGenerationConfig Perlin { get; set; } = new();
	public RoomCorridorGenerationConfig RoomCorridor { get; set; } = new();
}

public sealed class BspGenerationConfig
{
	[JsonPropertyName("min_leaf_size")]
	public int MinLeafSize { get; set; } = 6;

	[JsonPropertyName("min_room_size")]
	public int MinRoomSize { get; set; } = 3;

	[JsonPropertyName("max_depth")]
	public int MaxDepth { get; set; } = 5;

	[JsonPropertyName("stair_down_chance_percent")]
	public int StairDownChancePercent { get; set; } = 2;

	[JsonPropertyName("stair_up_chance_percent")]
	public int StairUpChancePercent { get; set; } = 2;

	[JsonPropertyName("nest_chance_percent")]
	public int NestChancePercent { get; set; } = 1;

	[JsonPropertyName("nest_spawn_interval")]
	public int NestSpawnInterval { get; set; } = 6;

	[JsonPropertyName("nest_max_spawned")]
	public int NestMaxSpawned { get; set; } = 3;
}

public sealed class CellularGenerationConfig
{
	[JsonPropertyName("iterations")]
	public int Iterations { get; set; } = 4;

	[JsonPropertyName("initial_fill_chance")]
	public double InitialFillChance { get; set; } = 0.48;

	[JsonPropertyName("noise_scale")]
	public double NoiseScale { get; set; } = 0.12;

	[JsonPropertyName("birth_threshold")]
	public int BirthThreshold { get; set; } = 5;

	[JsonPropertyName("survive_threshold")]
	public int SurviveThreshold { get; set; } = 4;

	[JsonPropertyName("stair_down_chance_percent")]
	public int StairDownChancePercent { get; set; } = 1;

	[JsonPropertyName("stair_up_chance_percent")]
	public int StairUpChancePercent { get; set; } = 1;

	[JsonPropertyName("nest_chance_percent")]
	public int NestChancePercent { get; set; } = 1;

	[JsonPropertyName("nest_spawn_interval")]
	public int NestSpawnInterval { get; set; } = 6;

	[JsonPropertyName("nest_max_spawned")]
	public int NestMaxSpawned { get; set; } = 3;
}

public sealed class DrunkardWalkGenerationConfig
{
	[JsonPropertyName("walkers_per_chunk")]
	public int WalkersPerChunk { get; set; } = 6;

	[JsonPropertyName("steps_per_walker")]
	public int StepsPerWalker { get; set; } = 200;

	[JsonPropertyName("target_open_ratio")]
	public double TargetOpenRatio { get; set; } = 0.40;

	[JsonPropertyName("stair_down_chance_percent")]
	public int StairDownChancePercent { get; set; } = 2;

	[JsonPropertyName("stair_up_chance_percent")]
	public int StairUpChancePercent { get; set; } = 2;

	[JsonPropertyName("nest_chance_percent")]
	public int NestChancePercent { get; set; } = 1;

	[JsonPropertyName("nest_spawn_interval")]
	public int NestSpawnInterval { get; set; } = 7;

	[JsonPropertyName("nest_max_spawned")]
	public int NestMaxSpawned { get; set; } = 2;
}

public sealed class PerlinGenerationConfig
{
	[JsonPropertyName("cave_scale")]
	public double CaveScale { get; set; } = 0.08;

	[JsonPropertyName("cave_density_threshold")]
	public double CaveDensityThreshold { get; set; } = 0.1;

	[JsonPropertyName("stair_down_chance_percent")]
	public int StairDownChancePercent { get; set; } = 1;

	[JsonPropertyName("stair_up_chance_percent")]
	public int StairUpChancePercent { get; set; } = 1;

	[JsonPropertyName("nest_chance_percent")]
	public int NestChancePercent { get; set; } = 1;

	[JsonPropertyName("nest_spawn_interval")]
	public int NestSpawnInterval { get; set; } = 8;

	[JsonPropertyName("nest_max_spawned")]
	public int NestMaxSpawned { get; set; } = 2;
}

public sealed class RoomCorridorGenerationConfig
{
	[JsonPropertyName("min_room_size")]
	public int MinRoomSize { get; set; } = 4;

	[JsonPropertyName("max_room_size")]
	public int MaxRoomSize { get; set; } = 8;

	[JsonPropertyName("max_attempts")]
	public int MaxAttempts { get; set; } = 30;

	[JsonPropertyName("surface_house_chance_percent")]
	public int SurfaceHouseChancePercent { get; set; } = 3;

	[JsonPropertyName("dungeon_nest_chance_percent")]
	public int DungeonNestChancePercent { get; set; } = 2;

	[JsonPropertyName("dungeon_nest_spawn_interval")]
	public int DungeonNestSpawnInterval { get; set; } = 5;

	[JsonPropertyName("dungeon_nest_max_spawned")]
	public int DungeonNestMaxSpawned { get; set; } = 3;

	[JsonPropertyName("stair_down_chance_percent")]
	public int StairDownChancePercent { get; set; } = 3;

	[JsonPropertyName("stair_up_chance_percent")]
	public int StairUpChancePercent { get; set; } = 3;
}
