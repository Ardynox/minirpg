using System.Linq;
using MiniRPG.Core.Map;
using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

public sealed class ChunkGrassCoverTests
{
	[Fact]
	public void NewChunkData_HasGrassCoverArrayOfChunkArea()
	{
		var chunk = new ChunkData();

		Assert.NotNull(chunk.GrassCover);
		Assert.Equal(ChunkData.Area, chunk.GrassCover.Length);
	}

	[Fact]
	public void NewChunkData_GrassCoverDefaultsToAllZero()
	{
		var chunk = new ChunkData();

		Assert.All(chunk.GrassCover, value => Assert.Equal(0, value));
	}

	[Fact]
	public void SaveChunkToCache_RoundTripsGrassCoverValues()
	{
		ResetCache();
		var coord = new ChunkCoord(7, 9, 1);
		var chunk = new ChunkData { Coord = coord, Dirty = true };
		chunk.GrassCover[0] = 1;
		chunk.GrassCover[5] = 128;
		chunk.GrassCover[ChunkData.Area - 1] = 255;

		SaveModule.SaveChunkToCache(coord, chunk);
		var restored = SaveModule.LoadChunkFromCache(coord);

		Assert.NotNull(restored);
		Assert.Equal(ChunkData.Area, restored!.GrassCover.Length);
		Assert.Equal(1, restored.GrassCover[0]);
		Assert.Equal(128, restored.GrassCover[5]);
		Assert.Equal(255, restored.GrassCover[ChunkData.Area - 1]);
		Assert.Equal(chunk.GrassCover, restored.GrassCover);
	}

	[Fact]
	public void LoadChunkFromCache_LegacySnapshotWithoutGrassCover_DefaultsToAllZeroArray()
	{
		ResetCache();
		var coord = new ChunkCoord(2, 3, 0);
		var legacy = new ChunkSnapshot
		{
			Cx = coord.Cx,
			Cy = coord.Cy,
			Cz = coord.Cz,
			TerrainIds = new ushort[ChunkData.Area],
			Hardness = new byte[ChunkData.Area],
			Stacks = [],
			Nests = [],
			GrassCover = null,
		};
		SaveModule.DirtyChunkCache[coord] = legacy;

		var restored = SaveModule.LoadChunkFromCache(coord);

		Assert.NotNull(restored);
		Assert.NotNull(restored!.GrassCover);
		Assert.Equal(ChunkData.Area, restored.GrassCover.Length);
		Assert.True(restored.GrassCover.All(value => value == 0));
	}

	private static void ResetCache() => SaveModule.DirtyChunkCache.Clear();
}
