using Godot;

namespace MiniRPG.Module.WorldTool;

internal static class RuntimeWorldToolInteractionLogic
{
	internal static bool IsPrimaryDragPressed(
		MouseButtonMask buttonMask,
		bool isPrimaryButtonPressed) =>
		isPrimaryButtonPressed || (buttonMask & MouseButtonMask.Left) != 0;

	internal static bool ShouldKeepDragStrokeActive(
		bool dragActive,
		bool isPrimaryDragPressed) =>
		dragActive && isPrimaryDragPressed;

	internal static bool ShouldProcessDragHoverCell(
		Vector3I hoverCell,
		Vector3I? lastProcessedHoverCell) =>
		lastProcessedHoverCell != hoverCell;

	internal static Vector3I? ResolveInfoOverlayCell(WorldToolPreviewState? previewState) =>
		previewState is { ShowInfoOverlay: true, ResolvedTargetCell: { } targetCell }
			? targetCell
			: null;
}
