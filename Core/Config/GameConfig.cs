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
	public static DebugConfig Debug { get; private set; } = new();
	public static GenerationConfigSet Generation { get; private set; } = new();

	public static void Load()
	{
		if (IsLoaded) return;

		PlayerVision = LoadRequired<PlayerVisionConfig>("res://Data/Config/player_vision.json");
		AIVision = LoadRequired<AIVisionConfig>("res://Data/Config/ai_vision.json");
		WorldRuntime = LoadRequired<WorldRuntimeConfig>("res://Data/Config/world_runtime.json");
		Debug = LoadRequired<DebugConfig>("res://Data/Config/debug.json");
		Generation = new GenerationConfigSet
		{
			Bsp = LoadRequired<BspGenerationConfig>("res://Data/Config/generation/bsp.json"),
			Cellular = LoadRequired<CellularGenerationConfig>("res://Data/Config/generation/cellular.json"),
			DrunkardWalk = LoadRequired<DrunkardWalkGenerationConfig>("res://Data/Config/generation/drunkard_walk.json"),
			Perlin = LoadRequired<PerlinGenerationConfig>("res://Data/Config/generation/perlin.json"),
			RoomCorridor = LoadRequired<RoomCorridorGenerationConfig>("res://Data/Config/generation/room_corridor.json"),
		};

		IsLoaded = true;
	}

	private static T LoadRequired<T>(string resPath) where T : class
	{
		using var file = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Read);
		if (file == null)
		{
			throw new InvalidOperationException(
				$"Failed to open config file: {resPath} (error: {Godot.FileAccess.GetOpenError()})");
		}

		var json = file.GetAsText();
		var data = JsonSerializer.Deserialize<T>(json, JsonOptions);
		if (data == null)
			throw new InvalidOperationException($"Config file is empty or invalid: {resPath}");

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

	[JsonPropertyName("max_cached_chunks")]
	public int MaxCachedChunks { get; set; } = 512;

	[JsonPropertyName("chunk_evict_padding_xy")]
	public int ChunkEvictPaddingXy { get; set; } = 2;

	[JsonPropertyName("chunk_simulation_near_radius_xy")]
	public int ChunkSimulationNearRadiusXy { get; set; } = 1;

	[JsonPropertyName("nest_nearby_count_radius")]
	public int NestNearbyCountRadius { get; set; } = 3;
}

public sealed class DebugConfig
{
	[JsonPropertyName("auto_test_step_delay")]
	public float AutoTestStepDelay { get; set; } = 0.05f;

	[JsonPropertyName("auto_test_stop_on_fail")]
	public bool AutoTestStopOnFail { get; set; }

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
