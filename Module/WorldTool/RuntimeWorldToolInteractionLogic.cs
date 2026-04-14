using Godot;

namespace MiniRPG.Module.WorldTool;

internal static class RuntimeWorldToolInteractionLogic
{
	internal static bool ShouldProcessDragHoverCell(
		Vector3I hoverCell,
		Vector3I? lastProcessedHoverCell) =>
		lastProcessedHoverCell != hoverCell;

	internal static Vector3I? ResolveInfoOverlayCell(WorldToolPreviewState? previewState) =>
		previewState is { ShowInfoOverlay: true, ResolvedTargetCell: { } targetCell }
			? targetCell
			: null;
}
