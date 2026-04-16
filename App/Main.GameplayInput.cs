using Godot;

namespace MiniRPG;

public partial class Main
{
	private bool _zoomHintShown;
	private ulong _lastZoomLimitLogAtMsec;

	private bool HandleLayoutEditKeyInput(InputEventKey key)
	{
		if (key.Pressed && key.Keycode == Key.Escape)
			CancelLayoutEditMode();

		return false;
	}

	private static bool HandleLayoutEditInput(InputEvent @event) =>
		@event is InputEventMouseButton editMouse
		&& editMouse.Pressed
		&& editMouse.ButtonIndex == MouseButton.Right;

	private bool HandleGameplayMouseInput(InputEvent @event, RuntimeUiModeSnapshot snapshot)
	{
		if (HandleWorldHoverInput(@event, snapshot))
			return true;

		if (@event is InputEventMouseButton releasedButton && !releasedButton.Pressed)
		{
			if (releasedButton.ButtonIndex == MouseButton.Left)
			{
				_runtimeWorldToolDragActive = false;
				_runtimeWorldToolLastDraggedHoverCell = null;
				return false;
			}

			if (releasedButton.ButtonIndex == MouseButton.Right)
				return HandleGameplayRightMouseRelease();
		}

		if (@event is not InputEventMouseButton mb || !mb.Pressed)
			return false;
		if (!snapshot.AllowGameplayInput)
			return false;

		if (HandleGameplayMouseWheelInput(mb, snapshot))
			return true;

		if (mb.ButtonIndex == MouseButton.Middle)
			return ReturnRuntimeCameraToPlayer();

		if ((_runtimeWorldToolBar.Visible && _runtimeWorldToolBar.IsPointerOver(mb.GlobalPosition))
			|| (_runtimeWorldToolHeightPanel.Visible && _runtimeWorldToolHeightPanel.IsPointerOver(mb.GlobalPosition)))
			return false;

		if (snapshot.AllowPanelChrome && _panelChrome.IsPointerOverInteractiveChrome(mb.GlobalPosition))
			return false;

		var hit = _panels.HitTest(mb.GlobalPosition);

		if (mb.ButtonIndex == MouseButton.Right)
		{
			if (_cameraRightDrag.TryBegin(mb.GlobalPosition, hit, _mapRender))
				return true;

			return ExecuteGameplayRightClick(mb.GlobalPosition);
		}

		if (mb.ButtonIndex != MouseButton.Left)
			return false;

		if (hit != null && hit.PanelId != "map")
		{
			if (hit == _panels.Focused)
				return false;

			if (hit.PanelId == "inventory" && !_inventoryPanel.Visible)
			{
				_inventoryPanel.Visible = true;
				FlushMap();
			}

			_panels.FocusFromPointer(hit, false);
			return false;
		}

		if (_mapRender != null && _mapRender.TryGetWorldCellFromGlobalPosition(mb.GlobalPosition, out var worldCell))
		{
			_runtimeWorldToolSession.SetReverseStack(mb.CtrlPressed);
			_runtimeWorldToolSession.SetHover(worldCell);
			_runtimeWorldToolDragActive = SupportsRuntimeWorldToolDrag();
			_runtimeWorldToolLastDraggedHoverCell = null;
			RefreshRuntimeWorldHoverPresentation(worldCell, mb.GlobalPosition);
			_panels.SetFocus("map");
			TryApplyRuntimeWorldToolAtCell(worldCell);
			if (_runtimeWorldToolDragActive)
				_runtimeWorldToolLastDraggedHoverCell = worldCell;
			return true;
		}

		if (hit == null)
			return false;

		var clearFocusStack = hit.PanelId == "map";
		if (hit == _panels.Focused && !clearFocusStack)
			return false;

		if (hit.PanelId == "inventory" && !_inventoryPanel.Visible)
		{
			_inventoryPanel.Visible = true;
			FlushMap();
		}

		_panels.FocusFromPointer(hit, clearFocusStack);
		return false;
	}

	private bool ExecuteGameplayRightClick(Vector2 globalPosition)
	{
		if (IsAutoNavigationPreviewActive)
		{
			CancelAutoNavigationPreview();
			return true;
		}

		if (GetArmedSkill() != null)
		{
			if (TryCastArmedSkillAtMouse(globalPosition))
				return true;
			return false;
		}

		if (TryOpenActorInspectPanelAtMouse(globalPosition))
			return true;

		if (_panels.CloseFocused())
		{
			FlushMap();
			return true;
		}

		return false;
	}

	private bool HandleGameplayRightMouseRelease()
	{
		if (!_cameraRightDrag.IsPending && !_cameraRightDrag.IsPromotedToPan)
			return false;

		var clickPosition = _cameraRightDrag.PressGlobalPosition;
		var promotedToPan = _cameraRightDrag.IsPromotedToPan;
		_cameraRightDrag.Reset(_runtimeCameraController, endPanDrag: promotedToPan);
		if (promotedToPan)
			return true;

		if (!_session.GameStarted || _menu.InMenu || MapEditorActive || LayoutEditActive || _busyOperationActive)
			return false;

		return ExecuteGameplayRightClick(clickPosition);
	}

	private bool HandleGameplayMouseWheelInput(InputEventMouseButton mb, RuntimeUiModeSnapshot snapshot)
	{
		if (!snapshot.AllowGameplayInput
			|| mb.ButtonIndex is not MouseButton.WheelUp and not MouseButton.WheelDown)
		{
			return false;
		}

		if (MapPanelFocused)
		{
			if (_inputModule.HandleMouseButtonInput(mb))
				return true;

			if (mb.ShiftPressed && !mb.CtrlPressed && !mb.AltPressed && _runtimeWorldToolSession != null)
			{
				_runtimeWorldToolSession.StepBrush(mb.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
				RefreshRuntimeWorldHoverPresentation(_runtimeWorldToolSession.HoverWorld);
				FlushMap();
				return true;
			}

			if (_mapRender != null)
			{
				if (!_zoomHintShown)
				{
					_log.Add(LocalizationService.T("ui.zoom.hint"));
					_zoomHintShown = true;
				}

				var zoomed = _mapRender.StepZoom(mb.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
				if (zoomed)
				{
					FlushMap();
				}
				else
				{
					var now = Time.GetTicksMsec();
					if (now - _lastZoomLimitLogAtMsec > 1000)
					{
						_log.Add(LocalizationService.T("ui.zoom.limit_reached"));
						_lastZoomLimitLogAtMsec = now;
					}
				}
				return true;
			}
		}

		return _inputModule.HandleMouseButtonInput(mb);
	}
}
