using Godot;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Facility;
using MiniRPG.Module.WorldTool;

namespace MiniRPG;

public partial class Main
{
	private void InitializeRuntimeWorldToolUi()
	{
		_runtimeWorldToolSession = new RuntimeWorldToolSession(_state);

		var panel = new PanelContainer
		{
			Name = "RuntimeWorldToolBar",
			Theme = _uiTheme,
			Visible = false,
			ZIndex = 70,
		};
		_overlayLayer.AddChild(panel);
		_runtimeWorldToolBar = new RuntimeWorldToolBarModule(panel);
		RefreshRuntimeWorldToolBar();
	}

	private void ResetRuntimeWorldToolSession()
	{
		if (_runtimeWorldToolSession == null)
			return;

		_runtimeWorldToolSession.ResetForSession();
		_runtimeWorldToolDragActive = false;
		_runtimeWorldToolLastAppliedCell = null;
		RefreshRuntimeWorldHoverPresentation(_runtimeWorldToolSession.HoverWorld);
		RefreshRuntimeWorldToolBar();
	}

	private void RefreshRuntimeWorldToolBar(RuntimeUiModeSnapshot? snapshot = null)
	{
		if (_runtimeWorldToolBar == null || _runtimeWorldToolSession == null)
			return;

		var resolvedSnapshot = snapshot ?? CaptureRuntimeUiMode();
		var visible = resolvedSnapshot.SessionStarted
			&& !resolvedSnapshot.InMenu
			&& !resolvedSnapshot.MapEditorActive
			&& !resolvedSnapshot.LayoutEditActive
			&& !resolvedSnapshot.HasVisibleModalLayer
			&& !resolvedSnapshot.BusyOperationActive;
		_runtimeWorldToolBar.Visible = visible;
		if (!visible)
			return;

		var previewState = ResolveRuntimeWorldToolPreviewState();
		_runtimeWorldToolBar.Render(
			_runtimeWorldToolSession.CurrentToolMode,
			_runtimeWorldToolSession.CurrentCategory,
			_runtimeWorldToolSession.CurrentBrushes,
			_runtimeWorldToolSession.CurrentBrushIndex,
			_runtimeWorldToolSession.BuildSummary(previewState),
			_runtimeWorldToolSession.FacilityRotation,
			showRotationControls: _runtimeWorldToolSession.CurrentCategory == WorldToolCategory.Facility);
	}

	private WorldToolPreviewState? ResolveRuntimeWorldToolPreviewState()
	{
		if (_runtimeWorldToolSession == null)
			return null;

		return _runtimeWorldToolSession.ResolveHoverState(_runtimeWorldToolSession.HoverWorld);
	}

	private void RefreshRuntimeWorldHoverPresentation(Vector3I? hoveredCell, Vector2? pointerGlobalPosition = null)
	{
		var overlayCell = ResolveRuntimeWorldHoverOverlayCell(hoveredCell, ResolveRuntimeWorldToolPreviewState());
		SetWorldHoverCell(overlayCell, flushMap: false);
		if (overlayCell != null && pointerGlobalPosition is { } position)
			PositionWorldHoverOverlay(position);
		RefreshRuntimeWorldToolBar();
	}

	private Vector3I? ResolveRuntimeWorldHoverOverlayCell(
		Vector3I? hoveredCell,
		WorldToolPreviewState? previewState)
	{
		if (_runtimeWorldToolSession == null || _runtimeWorldToolSession.CurrentToolMode != WorldToolMode.Select)
			return null;

		if (previewState is { ShowInfoOverlay: true, ResolvedTargetCell: { } targetCell })
			return targetCell;

		return hoveredCell;
	}

	private bool TryApplyRuntimeWorldToolAtCell(Vector3I worldCell)
	{
		if (_runtimeWorldToolSession == null)
			return false;

		if (_runtimeWorldToolSession.CurrentToolMode == WorldToolMode.Select)
		{
			HandleRuntimeWorldToolSelection(worldCell);
			return true;
		}

		var previewState = _runtimeWorldToolSession.ResolveHoverState(worldCell);
		if (previewState is not { CanApply: true, ResolvedTargetCell: { } targetCell } preview)
			return false;

		if (!TryBuildRuntimeWorldToolAction(preview, targetCell, out var action))
			return false;

		SubmitPlayerAction(action);
		_runtimeWorldToolLastAppliedCell = targetCell;
		FlushMap();
		return true;
	}

	private bool TryBuildRuntimeWorldToolAction(
		WorldToolPreviewState previewState,
		Vector3I targetCell,
		out TimelinePlayerAction action)
	{
		action = null!;
		if (_runtimeWorldToolSession == null)
			return false;

		switch (previewState.Category)
		{
			case WorldToolCategory.Terrain:
				if (previewState.ToolMode == WorldToolMode.Build)
				{
					action = TimelinePlayerAction.TerrainBuild(previewState.BrushId, targetCell.X, targetCell.Y, targetCell.Z);
					return true;
				}

				if (previewState.ToolMode == WorldToolMode.Demolish)
				{
					action = TimelinePlayerAction.TerrainDemolish(targetCell.X, targetCell.Y, targetCell.Z);
					return true;
				}
				break;
			case WorldToolCategory.Facility:
				if (previewState.ToolMode == WorldToolMode.Build)
				{
					action = TimelinePlayerAction.FacilityPlaceBlueprint(
						previewState.BrushId,
						targetCell.X,
						targetCell.Y,
						targetCell.Z,
						_runtimeWorldToolSession.FacilityRotation);
					return true;
				}

				if (previewState.ToolMode == WorldToolMode.Demolish
					&& !string.IsNullOrWhiteSpace(previewState.ResolvedEntityId))
				{
					action = TimelinePlayerAction.FacilityDemolish(previewState.ResolvedEntityId);
					return true;
				}
				break;
		}

		return false;
	}

	private void HandleRuntimeWorldToolSelection(Vector3I worldCell)
	{
		var actor = LookModule.TryGetInspectableActor(_state, _fogTracker, worldCell.X, worldCell.Y, worldCell.Z);
		if (actor != null)
		{
			OpenActorInspectPanel(actor);
			return;
		}

		var info = LookModule.DescribeCell(_state, _fogTracker, worldCell.X, worldCell.Y, worldCell.Z);
		_log.Add(info.Text);
		_panels.SetFocus("map");
	}

	private bool HandleRuntimeWorldToolKey(InputEventKey key)
	{
		if (HandleSkillTargetCursorKey(key))
			return true;

		if (!_session.GameStarted || _menu.InMenu || MapEditorActive || LayoutEditActive)
			return false;

		if (key.Keycode == Key.Ctrl)
		{
			var changed = _runtimeWorldToolSession.SetReverseStack(key.CtrlPressed);
			if (changed)
			{
				RefreshRuntimeWorldHoverPresentation(_runtimeWorldToolSession.HoverWorld);
				FlushMap();
			}
			return changed;
		}

		if (!key.Pressed || key.Echo || key.AltPressed || key.CtrlPressed || key.ShiftPressed)
			return false;

		if (key.Keycode == Key.R
			&& _runtimeWorldToolSession.CurrentCategory == WorldToolCategory.Facility)
		{
			_runtimeWorldToolSession.RotateFacility(1);
			RefreshRuntimeWorldToolBar();
			FlushMap();
			return true;
		}

		return false;
	}

	private bool SupportsRuntimeWorldToolDrag() =>
		_runtimeWorldToolSession.CurrentCategory == WorldToolCategory.Terrain
		&& _runtimeWorldToolSession.CurrentToolMode is WorldToolMode.Build or WorldToolMode.Demolish;

	private string LocalizeFacilityRotation(FacilityRotation rotation) =>
		LocalizationService.TOrFallback(
			$"ui.runtime_tool.rotation.{rotation.ToString().ToLowerInvariant()}",
			rotation.ToString());
}
