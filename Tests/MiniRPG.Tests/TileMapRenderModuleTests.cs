using Godot;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class TileMapRenderModuleTests
{
	[Fact]
	public void ResolveAnimatedFrames_PrefersClipCoords_WhenPresent()
	{
		var entry = new TileMapRenderModule.TileSourceEntry
		{
			SourceId = 42,
			IsAnimated = true,
			FrameSourceIds = [101, 102],
			FrameCoords =
			[
				new TileMapRenderModule.TileFrameCoordEntry { AtlasX = 3, AtlasY = 4 },
				new TileMapRenderModule.TileFrameCoordEntry { AtlasX = 4, AtlasY = 4 },
			],
		};

		var frames = TileMapRenderModule.ResolveAnimatedFrames(entry);

		Assert.Collection(
			frames,
			frame =>
			{
				Assert.Equal(42, frame.SourceId);
				Assert.Equal(new Vector2I(3, 4), frame.Coord);
			},
			frame =>
			{
				Assert.Equal(42, frame.SourceId);
				Assert.Equal(new Vector2I(4, 4), frame.Coord);
			});
	}

	[Fact]
	public void ResolveAnimatedFrames_FallsBackToLegacyFrameSourceIds()
	{
		var entry = new TileMapRenderModule.TileSourceEntry
		{
			SourceId = 42,
			IsAnimated = true,
			FrameSourceIds = [7, -1, 9],
		};

		var frames = TileMapRenderModule.ResolveAnimatedFrames(entry);

		Assert.Collection(
			frames,
			frame =>
			{
				Assert.Equal(7, frame.SourceId);
				Assert.Equal(Vector2I.Zero, frame.Coord);
			},
			frame =>
			{
				Assert.Equal(9, frame.SourceId);
				Assert.Equal(Vector2I.Zero, frame.Coord);
			});
	}
}
