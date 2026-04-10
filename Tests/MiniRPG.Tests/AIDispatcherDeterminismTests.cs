using System.Collections.Generic;
using MiniRPG.Core.AI;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

public sealed class AIDispatcherDeterminismTests
{
	[Fact]
	public void Classify_WithEmptyAnchors_ReturnsSummary()
	{
		var state = new GameState();
		var actor = new Actor
		{
			Id = "npc-test",
			X = 0,
			Y = 0,
			Z = 0,
		};

		var detail = AIDispatcher.Classify(state, actor, new List<WorldCoord>(), range: 6);
		Assert.Equal(SimDetail.Summary, detail);
	}

	[Fact]
	public void Classify_UsesClosestAnchorOnSameZ()
	{
		var state = new GameState();
		var actor = new Actor
		{
			Id = "npc-test",
			X = 10,
			Y = 10,
			Z = 1,
		};

		var anchors = new List<WorldCoord>
		{
			new(100, 100, 0),
			new(50, 50, 1),
			new(10, 10, 1),
		};

		var detail = AIDispatcher.Classify(state, actor, anchors, range: 3);
		Assert.Equal(SimDetail.Full, detail);
	}
}
