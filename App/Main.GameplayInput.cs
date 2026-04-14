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

		if (@event is InputEventMouseButton releasedButton
			&& releasedButton.ButtonIndex == MouseButton.Left
			&& !releasedButton.Pressed)
		{
			_runtimeWorldToolDragActive = false;
			_runtimeWorldToolLastDraggedHoverCell = null;
			return false;
		}

		if (@event is InputEventMouseButton releasedMiddleButton
			&& releasedMiddleButton.ButtonIndex == MouseButton.Middle
			&& !releasedMiddleButton.Pressed)
		{
			return _runtimeCameraController.EndPanDrag();
		}

		if (@event is not InputEventMouseButton mb || !mb.Pressed)
			return false;
		if (!snapshot.AllowGameplayInput)
			return false;

		if (HandleGameplayMouseWheelInput(mb, snapshot))
			return true;

		if ((_runtimeWorldToolBar.Visible && _runtimeWorldToolBar.IsPointerOver(mb.GlobalPosition))
			|| (_runtimeWorldToolHeightPanel.Visible && _runtimeWorldToolHeightPanel.IsPointerOver(mb.GlobalPosition)))
			return false;

		if (snapshot.AllowPanelChrome && _panelChrome.IsPointerOverInteractiveChrome(mb.GlobalPosition))
			return false;

		var hit = _panels.HitTest(mb.GlobalPosition);

		if (mb.ButtonIndex == MouseButton.Middle)
		{
			if (_runtimeCameraController == null)
				return false;
			if (hit != null && hit.PanelId != "map")
				return false;
			if (_mapRender == null || !_mapRender.TryGetWorldCellFromGlobalPosition(mb.GlobalPosition, out _))
				return false;
			if (!_runtimeCameraController.BeginPanDrag())
				return false;

			_panels.SetFocus("map");
			RefreshRuntimeWorldHoverPresentation(_runtimeWorldToolSession.HoverWorld, mb.GlobalPosition);
			return true;
		}

		if (mb.ButtonIndex == MouseButton.Right)
		{
			if (GetArmedSkill() != null)
			{
				if (TryCastArmedSkillAtMouse(mb.GlobalPosition))
					return true;
				return false;
			}

			if (TryOpenActorInspectPanelAtMouse(mb.GlobalPosition))
				return true;

			if (_panels.CloseFocused())
			{
				FlushMap();
				return true;
			}

			return false;
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

	private bool HandleGameplayMouseWheelInput(InputEventMouseButton mb, RuntimeUiModeSnapshot snapshot)
	{
		if (!snapshot.AllowGameplayInput
			|| mb.ButtonIndex is not MouseButton.WheelUp and not MouseButton.WheelDown)
		{
			return false;
		}

		if (MapPanelFocused)
		{
			if (mb.AltPressed && !mb.CtrlPressed && !mb.ShiftPressed)
			{
				OnCommand(mb.ButtonIndex == MouseButton.WheelUp ? ":skill_prev" : ":skill_next");
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
