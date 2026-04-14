using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Module.WorldTool;
using Xunit;

namespace MiniRPG.Tests;

public sealed class RuntimeWorldToolInteractionLogicTests
{
	[Fact]
	public void IsPrimaryDragPressed_UsesEventOrLiveButtonState()
	{
		const MouseButtonMask noButtons = 0;

		Assert.True(RuntimeWorldToolInteractionLogic.IsPrimaryDragPressed(MouseButtonMask.Left, false));
		Assert.True(RuntimeWorldToolInteractionLogic.IsPrimaryDragPressed(noButtons, true));
		Assert.False(RuntimeWorldToolInteractionLogic.IsPrimaryDragPressed(noButtons, false));
	}

	[Fact]
	public void ShouldKeepDragStrokeActive_OnlyWhileStrokeExistsAndPrimaryButtonIsPressed()
	{
		Assert.True(RuntimeWorldToolInteractionLogic.ShouldKeepDragStrokeActive(true, true));
		Assert.False(RuntimeWorldToolInteractionLogic.ShouldKeepDragStrokeActive(true, false));
		Assert.False(RuntimeWorldToolInteractionLogic.ShouldKeepDragStrokeActive(false, true));
	}

	[Fact]
	public void ShouldProcessDragHoverCell_OnlyTriggersWhenRawHoverCellChanges()
	{
		var origin = new Vector3I(3, 4, 5);
		var neighbor = new Vector3I(4, 4, 5);

		Assert.False(RuntimeWorldToolInteractionLogic.ShouldProcessDragHoverCell(origin, origin));
		Assert.True(RuntimeWorldToolInteractionLogic.ShouldProcessDragHoverCell(neighbor, origin));
		Assert.True(RuntimeWorldToolInteractionLogic.ShouldProcessDragHoverCell(origin, neighbor));
	}

	[Fact]
	public void ResolveInfoOverlayCell_ReturnsResolvedTargetOnlyWhenPreviewRequestsOverlay()
	{
		var targetCell = new Vector3I(7, 8, 2);
		var overlayPreview = new WorldToolPreviewState(
			WorldToolMode.Demolish,
			WorldToolCategory.Terrain,
			WorldToolPreviewKind.Terrain,
			RawHoverCell: targetCell,
			ResolvedTargetCell: targetCell,
			BrushId: Terrains.Stone,
			BrushGlyph: null,
			CanApply: true,
			ShowGhost: true,
			ShowInfoOverlay: true,
			GhostRenderId: Terrains.Stone,
			GhostGlyph: null,
			ResolvedEntityId: null,
			HideResolvedTargetInWorld: false,
			GhostFacility: null);
		var hiddenPreview = overlayPreview with { ShowInfoOverlay = false };
		var missingTargetPreview = overlayPreview with { ResolvedTargetCell = null };

		Assert.Equal(targetCell, RuntimeWorldToolInteractionLogic.ResolveInfoOverlayCell(overlayPreview));
		Assert.Null(RuntimeWorldToolInteractionLogic.ResolveInfoOverlayCell(hiddenPreview));
		Assert.Null(RuntimeWorldToolInteractionLogic.ResolveInfoOverlayCell(missingTargetPreview));
		Assert.Null(RuntimeWorldToolInteractionLogic.ResolveInfoOverlayCell(null));
	}
}
