using Godot;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Facility;
using MiniRPG.Module;
using MiniRPG.Module.Panel;
using MiniRPG.Module.Render;
using MiniRPG.Module.WorldTool;

namespace MiniRPG;

public partial class Main
{
	private void InitializeRuntimeWorldToolUi()
	{
		_runtimeCameraController = new RuntimeCameraController(_state);
		_runtimeWorldToolSession = new RuntimeWorldToolSession(_state);
		_cameraRightDrag = new RuntimeCameraRightDragHandler(_panels);
		_runtimeWorldToolHasLastPointerGlobalPosition = false;

		var toolPanel = new PanelContainer
		{
			Name = "RuntimeWorldToolBar",
			Theme = _uiTheme,
			Visible = false,
			ZIndex = 70,
		};
		_overlayLayer.AddChild(toolPanel);
		_runtimeWorldToolBar = new RuntimeWorldToolBarModule(toolPanel);
		RegisterRuntimeWorldToolPanel(
			"runtime_world_tool",
			_runtimeWorldToolBar.PanelNode,
			_runtimeWorldToolBar.DragHandle);

		var heightPanel = new PanelContainer
		{
			Name = "RuntimeWorldToolHeightPanel",
			Theme = _uiTheme,
			Visible = false,
			ZIndex = 70,
		};
		_overlayLayer.AddChild(heightPanel);
		_runtimeWorldToolHeightPanel = new RuntimeWorldToolHeightPanelModule(heightPanel);
		RegisterRuntimeWorldToolPanel(
			"runtime_world_tool_height",
			_runtimeWorldToolHeightPanel.PanelNode,
			_runtimeWorldToolHeightPanel.DragHandle);

		RefreshRuntimeWorldToolBar();
	}

	private void RegisterRuntimeWorldToolPanel(string panelId, PanelContainer panelNode, Control dragHandle)
	{
		_panelLayouts.RegisterPanel(panelId, panelNode);
		_panelDrag.Register(new DraggablePanelRegistration(
			panelId,
			panelNode,
			PanelDragAvailability.Always,
			[dragHandle],
			DefaultFloating: true));
	}

	private void ResetRuntimeWorldToolSession()
	{
		if (_runtimeWorldToolSession == null || _runtimeCameraController == null)
			return;

		_runtimeCameraController.ResetForSession();
		_cameraRightDrag.Reset(_runtimeCameraController);
		_runtimeWorldToolSession.ResetForSession();
		_runtimeWorldToolDragActive = false;
		_runtimeWorldToolLastDraggedHoverCell = null;
		_runtimeWorldToolHasLastPointerGlobalPosition = false;
		RefreshRuntimeWorldHoverPresentation(_runtimeWorldToolSession.HoverWorld);
		RefreshRuntimeWorldToolBar();
	}

	private void RefreshRuntimeWorldToolBar(RuntimeUiModeSnapshot? snapshot = null)
	{
		if (_runtimeWorldToolBar == null
			|| _runtimeWorldToolHeightPanel == null
			|| _runtimeWorldToolSession == null
			|| _runtimeCameraController == null)
			return;

		var resolvedSnapshot = snapshot ?? CaptureRuntimeUiMode();
		var visible = resolvedSnapshot.SessionStarted
			&& !resolvedSnapshot.InMenu
			&& !resolvedSnapshot.MapEditorActive
			&& !resolvedSnapshot.LayoutEditActive
			&& !resolvedSnapshot.HasVisibleModalLayer
			&& !resolvedSnapshot.BusyOperationActive;
		_runtimeWorldToolBar.Visible = visible;
		_runtimeWorldToolHeightPanel.Visible = visible;
		if (!visible)
			return;

		var previewState = ResolveRuntimeWorldToolPreviewState();
		var runtimeCameraSnapshot = _runtimeCameraController.BuildSnapshot();
		_runtimeWorldToolBar.Render(
			_runtimeWorldToolSession.CurrentToolMode,
			_runtimeWorldToolSession.CurrentCategory,
			_runtimeWorldToolSession.CurrentBrushes,
			_runtimeWorldToolSession.CurrentBrushIndex,
			_runtimeWorldToolSession.BuildSummary(previewState),
			_runtimeWorldToolSession.FacilityRotation,
			showRotationControls: _runtimeWorldToolSession.CurrentCategory == WorldToolCategory.Facility);
		_runtimeWorldToolHeightPanel.Render(
			runtimeCameraSnapshot.Mode,
			runtimeCameraSnapshot.CenterZ,
			canAdjustLayer: runtimeCameraSnapshot.Mode == RuntimeCameraMode.LayerPan);
	}

	private WorldToolPreviewState? ResolveRuntimeWorldToolPreviewState()
	{
		if (_runtimeWorldToolSession == null)
			return null;

		return _runtimeWorldToolSession.ResolveHoverState(_runtimeWorldToolSession.HoverWorld);
	}

	private void RefreshRuntimeWorldHoverPresentation(Vector3I? hoveredCell, Vector2? pointerGlobalPosition = null)
	{
		if (pointerGlobalPosition is { } pointerPosition)
		{
			_runtimeWorldToolLastPointerGlobalPosition = pointerPosition;
			_runtimeWorldToolHasLastPointerGlobalPosition = true;
		}

		var overlayCell = ResolveRuntimeWorldHoverOverlayCell(hoveredCell, ResolveRuntimeWorldToolPreviewState());
		SetWorldHoverCell(overlayCell, flushMap: false);
		var resolvedPointerPosition = pointerGlobalPosition
			?? (_runtimeWorldToolHasLastPointerGlobalPosition ? _runtimeWorldToolLastPointerGlobalPosition : (Vector2?)null);
		if (overlayCell != null && resolvedPointerPosition is { } position)
			PositionWorldHoverOverlay(position);
		RefreshRuntimeWorldToolBar();
	}

	private Vector3I? ResolveRuntimeWorldHoverOverlayCell(
		Vector3I? hoveredCell,
		WorldToolPreviewState? previewState)
	{
		if (_runtimeWorldToolSession.CurrentToolMode == WorldToolMode.Select)
			return ResolveRuntimeInspectHoverCell() ?? RuntimeWorldToolInteractionLogic.ResolveInfoOverlayCell(previewState) ?? hoveredCell;

		_ = hoveredCell;
		return RuntimeWorldToolInteractionLogic.ResolveInfoOverlayCell(previewState);
	}

	private Vector3I? ResolveRuntimeRenderHoverCell(
		Vector3I? hoveredCell,
		WorldToolPreviewState? previewState)
	{
		if (_runtimeWorldToolSession.CurrentToolMode == WorldToolMode.Select)
			return ResolveRuntimeInspectHoverCell() ?? hoveredCell;

		return hoveredCell;
	}

	private Vector3I? ResolveRuntimeInspectHoverCell()
	{
		if (_mapRender == null || !_runtimeWorldToolHasLastPointerGlobalPosition)
			return null;

		return _mapRender.TryGetInspectWorldCellFromGlobalPosition(_runtimeWorldToolLastPointerGlobalPosition, out var inspectCell)
			? inspectCell
			: null;
	}

	private void RefreshRuntimeWorldToolHoverFromLastPointer()
	{
		if (_runtimeWorldToolSession == null)
			return;

		SyncRuntimeCameraPreview();
		if (_mapRender != null
			&& _runtimeWorldToolHasLastPointerGlobalPosition
			&& _mapRender.TryGetWorldCellFromGlobalPosition(_runtimeWorldToolLastPointerGlobalPosition, out var worldCell))
		{
			_runtimeWorldToolSession.SetHover(worldCell);
			RefreshRuntimeWorldHoverPresentation(worldCell);
			return;
		}

		_runtimeWorldToolSession.SetHover(null);
		RefreshRuntimeWorldHoverPresentation(null);
	}

	private bool TryApplyRuntimeWorldToolAtCell(Vector3I worldCell)
	{
		if (_runtimeWorldToolSession == null)
			return false;

		if (_runtimeWorldToolSession.CurrentToolMode == WorldToolMode.Select)
		{
			var selectionPreviewState = _runtimeWorldToolSession.ResolveHoverState(worldCell);
			StartOrRetargetAutoNavigation(selectionPreviewState?.ResolvedTargetCell ?? worldCell);
			return true;
		}

		var previewState = _runtimeWorldToolSession.ResolveHoverState(worldCell);
		if (previewState is not { CanApply: true, ResolvedTargetCell: { } targetCell } preview)
			return false;

		if (!TryBuildRuntimeWorldToolAction(preview, targetCell, out var action))
			return false;

		InterruptAutoNavigationForManualInput();
		SubmitPlayerAction(action);
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

	private void AdjustRuntimeWorldToolHeight(int delta)
	{
		if (_runtimeCameraController == null || !_runtimeCameraController.AdjustPanZ(delta))
			return;

		_runtimeWorldToolDragActive = false;
		_runtimeWorldToolLastDraggedHoverCell = null;
		RefreshRuntimeWorldToolHoverFromLastPointer();
		FlushMap();
	}

	private void CenterRuntimeWorldToolCameraOnPlayer()
	{
		if (_runtimeCameraController == null)
			return;

		_runtimeCameraController.CenterOnActiveActor();
		_cameraRightDrag.Reset(_runtimeCameraController);
		_runtimeWorldToolDragActive = false;
		_runtimeWorldToolLastDraggedHoverCell = null;
		RefreshRuntimeWorldToolHoverFromLastPointer();
		FlushMap();
	}

	private RuntimeCameraSnapshot ResolveRuntimeCameraSnapshot() =>
		_runtimeCameraController.BuildSnapshot();

	private void SyncRuntimeCameraPreview()
	{
		if (_mapRender == null || _runtimeCameraController == null)
			return;

		_mapRender.SetRuntimeView(!MapEditorActive, _runtimeCameraController.BuildSnapshot());
	}

	private bool ReturnRuntimeCameraToPlayer()
	{
		if (_runtimeCameraController == null
			|| !_session.GameStarted
			|| _menu.InMenu
			|| MapEditorActive
			|| LayoutEditActive
			|| _busyOperationActive)
		{
			return false;
		}

		_runtimeCameraController.ReturnToFollowActor();
		_cameraRightDrag.Reset(_runtimeCameraController);
		_runtimeWorldToolDragActive = false;
		_runtimeWorldToolLastDraggedHoverCell = null;
		RefreshRuntimeWorldToolHoverFromLastPointer();
		FlushMap();
		return true;
	}

	private void HandleRuntimeWorldToolSelection(Vector3I worldCell)
	{
		var target = ResolveInspectTargetFromRuntimePointerOrCell(worldCell);
		if (target.Kind is LookInspectTargetKind.Actor or LookInspectTargetKind.CorpseItem)
		{
			OpenStatusPanelForInspectTarget(target);
			return;
		}

		_log.Add(target.Text);
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

		if (!key.Pressed || key.Echo)
			return false;

		if (key.ShiftPressed && !key.AltPressed && !key.CtrlPressed)
		{
			switch (key.Keycode)
			{
				case Key.S:
					_runtimeWorldToolSession.SetToolMode(WorldToolMode.Select);
					RefreshRuntimeWorldHoverPresentation(_runtimeWorldToolSession.HoverWorld);
					FlushMap();
					return true;
				case Key.B:
					_runtimeWorldToolSession.SetToolMode(WorldToolMode.Build);
					RefreshRuntimeWorldHoverPresentation(_runtimeWorldToolSession.HoverWorld);
					FlushMap();
					return true;
				case Key.D:
					_runtimeWorldToolSession.SetToolMode(WorldToolMode.Demolish);
					RefreshRuntimeWorldHoverPresentation(_runtimeWorldToolSession.HoverWorld);
					FlushMap();
					return true;
			}
		}

		if (key.AltPressed || key.CtrlPressed || key.ShiftPressed)
			return false;

		if (key.Keycode == Key.Tab)
		{
			var nextCategory = _runtimeWorldToolSession.CurrentCategory == WorldToolCategory.Terrain
				? WorldToolCategory.Facility
				: WorldToolCategory.Terrain;
			_runtimeWorldToolSession.SetCategory(nextCategory);
			RefreshRuntimeWorldHoverPresentation(_runtimeWorldToolSession.HoverWorld);
			FlushMap();
			return true;
		}

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
