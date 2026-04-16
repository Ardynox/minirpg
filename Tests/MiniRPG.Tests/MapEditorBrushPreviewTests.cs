using System;
using System.Collections.Generic;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using MiniRPG.Module.Editor;
using Xunit;

namespace MiniRPG.Tests;

public sealed class MapEditorBrushPreviewTests
{
	public MapEditorBrushPreviewTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	[Fact]
	public void MapEditorSession_FixtureBrushes_TextureFixturesExposePreview()
	{
		var session = new MapEditorSession(CreateEditorState());

		var brush = Assert.Single(
			session.FixtureBrushes,
			entry => string.Equals(entry.Id, Entities.Door, StringComparison.Ordinal));
		var preview = Assert.IsType<BrushPreview>(brush.Preview);

		Assert.Equal("res://Assets/Art/Generated/fixtures/door.png", preview.TexturePath);
		Assert.Null(preview.Region);
	}

	[Fact]
	public void MapEditorSession_FixtureBrushes_TileFixturesExposeSpritePreview()
	{
		var session = new MapEditorSession(CreateEditorState());

		var brush = Assert.Single(
			session.FixtureBrushes,
			entry => string.Equals(entry.Id, Entities.House, StringComparison.Ordinal));
		var preview = Assert.IsType<BrushPreview>(brush.Preview);

		Assert.Equal(
			"res://Assets/Art/Tilesets/FantasyKingdom/FantasyKingdomTileset_Godot/Environment/Sprites/Roof A1_N.png",
			preview.TexturePath);
		Assert.Null(preview.Region);
	}

	[Fact]
	public void MapEditorSession_FixtureBrushes_MissingPreviewFallsBackToGlyph()
	{
		var session = new MapEditorSession(CreateEditorState());

		var brush = Assert.Single(
			session.FixtureBrushes,
			entry => string.Equals(entry.Id, Entities.Ladder, StringComparison.Ordinal));

		Assert.Null(brush.Preview);
		Assert.Equal("|", brush.Glyph);
	}

	private static GameState CreateEditorState() =>
		new()
		{
			WorldSeed = 5678,
			PlayerId = "player",
			PlayerX = 0,
			PlayerY = 0,
			PlayerZ = 0,
			World = new WorldMap(5678, new AirGenerator()),
			Actors = new Dictionary<string, Actor>(StringComparer.Ordinal),
		};

	private sealed class AirGenerator : IMapGenerator
	{
		public string Id => "air";
		public string Name => "Air";

		public void GenerateChunk(ChunkData chunk, int worldSeed) =>
			chunk.Fill(TerrainRegistry.GetId(Terrains.Air));

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
