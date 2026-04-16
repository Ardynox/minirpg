using System;
using System.Collections.Generic;
using MiniRPG.Core.World;

namespace MiniRPG.Module.Render;

/// <summary>
/// Resolves a <see cref="TerrainDef"/> to the voxel tile texture path under
/// <c>res://Assets/Art/Generated/voxel_tiles</c>. Extracted from
/// <c>IsometricVoxelRenderer</c> / <c>TerrainAtlas</c> to keep a single source
/// of truth for terrain → tile-file lookup.
/// </summary>
internal static class VoxelTilePathResolver
{
	public const string VoxelTileRoot = "res://Assets/Art/Generated/voxel_tiles";

	private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
	{
		["grass_top"] = "tile_grass_top.png",
		["grass_side"] = "tile_grass_side.png",
		["dirt"] = "tile_dirt.png",
		["dirt_side"] = "tile_dirt.png",
		["soil"] = "tile_dirt.png",
		["soil_side"] = "tile_dirt.png",
		["stone"] = "tile_stone.png",
		["stone_side"] = "tile_stone.png",
		["sand"] = "tile_sand.png",
		["sand_side"] = "tile_sand.png",
		["gravel"] = "tile_gravel.png",
		["gravel_side"] = "tile_gravel.png",
		["mud"] = "tile_mud.png",
		["mud_side"] = "tile_mud.png",
		["clay"] = "tile_clay.png",
		["clay_side"] = "tile_clay.png",
		["snow"] = "tile_snow.png",
		["snow_side"] = "tile_snow.png",
		["ash"] = "tile_ash.png",
		["ash_side"] = "tile_ash.png",
		["plank"] = "tile_plank.png",
		["plank_side"] = "tile_plank.png",
		["log_top"] = "tile_log_top.png",
		["log_side"] = "tile_log_side.png",
		["brick"] = "tile_brick.png",
		["brick_side"] = "tile_brick.png",
		["brick_mossy"] = "tile_brick_mossy.png",
		["brick_mossy_side"] = "tile_brick_mossy.png",
		["brick_cracked"] = "tile_brick_cracked.png",
		["brick_cracked_side"] = "tile_brick_cracked.png",
		["ore_coal"] = "ores/tile_stone_ore_coal.png",
		["ore_iron"] = "ores/tile_stone_ore_iron.png",
		["ore_copper"] = "ores/tile_stone_ore_copper.png",
		["ore_gold"] = "ores/tile_stone_ore_gold.png",
		["ore_crystal"] = "ores/tile_stone_ore_crystal.png",
		["wall_stone_side"] = "tile_stone.png",
		["wall_granite_side"] = "tile_stone.png",
		["wall_obsidian_side"] = "tile_stone.png",
		["wall_iron_side"] = "tile_stone.png",
		["mountain_side"] = "tile_stone.png",
		["rubble"] = "tile_gravel.png",
		["rubble_side"] = "tile_gravel.png",
	};

	public static string ResolveTopPath(TerrainDef terrain)
	{
		var token = string.IsNullOrWhiteSpace(terrain.TopTile) ? terrain.StringId : terrain.TopTile;
		var fileName = ResolveFileName(token, isTop: true);
		return string.IsNullOrWhiteSpace(fileName) ? string.Empty : $"{VoxelTileRoot}/{fileName}";
	}

	public static string ResolveSidePath(TerrainDef terrain)
	{
		var token = string.IsNullOrWhiteSpace(terrain.SideTile) ? terrain.StringId : terrain.SideTile;
		var fileName = ResolveFileName(token, isTop: false);
		return string.IsNullOrWhiteSpace(fileName) ? string.Empty : $"{VoxelTileRoot}/{fileName}";
	}

	internal static string ResolveFileName(string token, bool isTop)
	{
		if (Aliases.TryGetValue(token, out var alias))
			return alias;

		return token switch
		{
			"grass_block" => isTop ? "tile_grass_top.png" : "tile_grass_side.png",
			"grass" => isTop ? "tile_grass_top.png" : "tile_grass_side.png",
			"tree" => "tile_grass_side.png",
			"fungus" => "tile_grass_side.png",
			"dirt" => "tile_dirt.png",
			"swamp" => "tile_dirt.png",
			"marsh" => "tile_dirt.png",
			"sand" => "tile_dirt.png",
			"wall_soil" => "tile_dirt.png",
			"stone" => "tile_stone.png",
			"gravel" => "tile_stone.png",
			"mountain" => "tile_stone.png",
			"rubble" => "tile_stone.png",
			"floor" => "tile_stone.png",
			"wall_stone" => "tile_stone.png",
			"wall_granite" => "tile_stone.png",
			"wall_obsidian" => "tile_stone.png",
			"wall_iron" => "tile_stone.png",
			"crystal_vein" => "tile_stone.png",
			_ => string.Empty,
		};
	}
}
