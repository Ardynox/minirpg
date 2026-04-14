using Godot;
using Xunit;

namespace MiniRPG.Tests;

public sealed class RuntimeStatusPanelPlacementPolicyTests
{
	[Fact]
	public void IsInDefaultSlot_AllowsSmallPointerSizedDrift()
	{
		var defaultPosition = new Vector2(120f, 80f);

		Assert.True(RuntimeStatusPanelPlacementPolicy.IsInDefaultSlot(new Vector2(120.5f, 79.2f), defaultPosition));
		Assert.False(RuntimeStatusPanelPlacementPolicy.IsInDefaultSlot(new Vector2(122f, 79.2f), defaultPosition));
	}

	[Fact]
	public void ResolveReplaceableKey_ReturnsVisiblePanelStillInDefaultSlot()
	{
		var key = RuntimeStatusPanelPlacementPolicy.ResolveReplaceableKey(
		[
			new RuntimeStatusPanelPlacementCandidate("actor:a", true, new Vector2(40f, 20f), 0),
			new RuntimeStatusPanelPlacementCandidate("actor:b", true, new Vector2(120f, 80f), 2),
			new RuntimeStatusPanelPlacementCandidate("corpse:c", true, new Vector2(200f, 120f), 1),
		],
			exceptKey: null,
			defaultPosition: new Vector2(120f, 80f));

		Assert.Equal("actor:b", key);
	}

	[Fact]
	public void ResolveReplaceableKey_IgnoresPinnedPanelsAwayFromDefaultSlot()
	{
		var key = RuntimeStatusPanelPlacementPolicy.ResolveReplaceableKey(
		[
			new RuntimeStatusPanelPlacementCandidate("actor:a", true, new Vector2(180f, 120f), 0),
			new RuntimeStatusPanelPlacementCandidate("corpse:b", true, new Vector2(220f, 180f), 1),
		],
			exceptKey: null,
			defaultPosition: new Vector2(120f, 80f));

		Assert.Null(key);
	}

	[Fact]
	public void ResolveReplaceableKey_DoesNotReplaceTargetThatIsAlreadyOpen()
	{
		var key = RuntimeStatusPanelPlacementPolicy.ResolveReplaceableKey(
		[
			new RuntimeStatusPanelPlacementCandidate("actor:a", true, new Vector2(120f, 80f), 2),
			new RuntimeStatusPanelPlacementCandidate("corpse:b", true, new Vector2(180f, 120f), 1),
		],
			exceptKey: "actor:a",
			defaultPosition: new Vector2(120f, 80f));

		Assert.Null(key);
	}

	[Fact]
	public void ResolveReplaceableKey_PrefersTopmostPanelWhenDefaultSlotOverlaps()
	{
		var key = RuntimeStatusPanelPlacementPolicy.ResolveReplaceableKey(
		[
			new RuntimeStatusPanelPlacementCandidate("actor:a", true, new Vector2(120f, 80f), 1),
			new RuntimeStatusPanelPlacementCandidate("corpse:b", true, new Vector2(120f, 80f), 3),
		],
			exceptKey: null,
			defaultPosition: new Vector2(120f, 80f));

		Assert.Equal("corpse:b", key);
	}
}
