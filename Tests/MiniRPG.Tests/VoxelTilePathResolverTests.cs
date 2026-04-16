using MiniRPG.Core.World;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class VoxelTilePathResolverTests
{
	private const string Root = VoxelTilePathResolver.VoxelTileRoot;

	[Fact]
	public void ResolveTopPath_UsesExplicitTopTile_WhenSet()
	{
		var terrain = new TerrainDef { StringId = "anything", TopTile = "grass_top" };
		Assert.Equal($"{Root}/tile_grass_top.png", VoxelTilePathResolver.ResolveTopPath(terrain));
	}

	[Fact]
	public void ResolveTopPath_FallsBackToStringId_WhenTopTileEmpty()
	{
		var terrain = new TerrainDef { StringId = "grass_block" };
		Assert.Equal($"{Root}/tile_grass_top.png", VoxelTilePathResolver.ResolveTopPath(terrain));
	}

	[Fact]
	public void ResolveSidePath_FallsBackToStringId_WhenSideTileEmpty()
	{
		var terrain = new TerrainDef { StringId = "grass_block" };
		Assert.Equal($"{Root}/tile_grass_side.png", VoxelTilePathResolver.ResolveSidePath(terrain));
	}

	[Theory]
	[InlineData("ore_iron", "ores/tile_stone_ore_iron.png")]
	[InlineData("stone", "tile_stone.png")]
	[InlineData("dirt", "tile_dirt.png")]
	[InlineData("snow", "tile_snow.png")]
	[InlineData("wall_stone_side", "tile_stone.png")]
	public void ResolveFileName_MatchesAliasTable(string token, string expectedFile)
	{
		Assert.Equal(expectedFile, VoxelTilePathResolver.ResolveFileName(token, isTop: true));
	}

	[Theory]
	[InlineData("wall_stone", "tile_stone.png")]
	[InlineData("tree", "tile_grass_side.png")]
	[InlineData("wall_soil", "tile_dirt.png")]
	public void ResolveFileName_MatchesSwitchFallback(string token, string expectedFile)
	{
		Assert.Equal(expectedFile, VoxelTilePathResolver.ResolveFileName(token, isTop: false));
	}

	[Fact]
	public void ResolveFileName_UnknownToken_ReturnsEmpty()
	{
		Assert.Equal(string.Empty, VoxelTilePathResolver.ResolveFileName("there_is_no_such_token", isTop: true));
	}

	[Fact]
	public void ResolveTopPath_UnknownToken_ReturnsEmpty()
	{
		var terrain = new TerrainDef { StringId = "definitely_missing" };
		Assert.Equal(string.Empty, VoxelTilePathResolver.ResolveTopPath(terrain));
	}
}
