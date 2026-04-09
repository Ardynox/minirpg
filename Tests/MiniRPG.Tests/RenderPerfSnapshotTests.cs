using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class RenderPerfSnapshotTests
{
	[Fact]
	public void Empty_IsZeroed()
	{
		var snapshot = TileMapRenderModule.RenderPerfSnapshot.Empty;

		Assert.Equal(0, snapshot.ActiveSpriteCount);
		Assert.Equal(0, snapshot.DrawCommandCount);
		Assert.Equal(0d, snapshot.FrameTimeAvgMs);
	}
}
