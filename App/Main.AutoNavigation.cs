using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Config;

namespace MiniRPG;

public partial class Main
{
	private AutoNavigationCoordinator _autoNav = null!;

	private bool IsAutoNavigationActive => _autoNav.IsActive;
	private bool IsAutoNavigationPreviewActive => _autoNav.IsPreviewActive;

	private bool StartOrRetargetAutoNavigation(Vector3I targetCell)
		=> _autoNav.StartOrRetarget(targetCell);

	private void CancelAutoNavigationPreview()
		=> _autoNav.CancelPreview();

	private void CancelAutoNavigation(AutoNavigationStopReason reason, bool emitLog)
		=> _autoNav.Cancel(reason, emitLog);

	private void ProcessAutoNavigation(RuntimeUiModeSnapshot snapshot)
		=> _autoNav.Process(snapshot);

	private void InterruptAutoNavigationForManualInput()
		=> _autoNav.InterruptForManualInput();

	private Vector3I? ResolvePrimaryTargetCursorWorldCell()
		=> _autoNav.ResolvePrimaryTargetCursorWorldCell();

	private IReadOnlyList<Vector3I>? ResolveAutoNavigationPathHighlightCells()
		=> _autoNav.ResolvePathHighlightCells();

	private void CycleAutoNavigationInterruptPolicy()
		=> _autoNav.CycleInterruptPolicy();

	private static bool ShouldInterruptAutoNavigationForCommand(string cmd)
		=> AutoNavigationCoordinator.ShouldInterruptForCommand(cmd);
}
