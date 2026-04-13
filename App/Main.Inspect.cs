using System;
using Godot;

namespace MiniRPG;

public partial class Main
{
	private void HandleInspectToggleCommand()
	{
		if (_inspectModeActive)
		{
			if (_skillCastCursorActive)
				_log.Add(LocalizationService.T("ui.skill.targeting.canceled"));
			EndInspectMode();
			return;
		}

		if (IsTimelineInputLocked())
			return;

		if (!_enableKeyboardTargeting)
			return;

		if (GetArmedSkill() != null)
		{
			StartSkillCastCursorMode();
			return;
		}

		StartInspectMode();
	}

	private bool TryOpenActorInspectPanelAtMouse(Vector2 globalPosition)
	{
		if (_mapRender == null || !(_mapRender.TryGetWorldCellFromGlobalPosition(globalPosition, out var worldCell)))
			return false;

		var actor = LookModule.TryGetInspectableActor(_state, _fogTracker, worldCell.X, worldCell.Y, worldCell.Z);
		if (actor == null)
			return false;

		OpenActorInspectPanel(actor);
		return true;
	}

	private bool HandleInspectModeKey(InputEventKey key)
	{
		if (!_inspectModeActive || !key.Pressed)
			return false;

		if (_actorInspectPanel?.Visible == true && _panels.FocusedId == _actorInspectPanel.PanelId)
			return false;

		if (key.Keycode is Key.Enter or Key.KpEnter)
		{
			if (_skillCastCursorActive)
			{
				if (_inspectWorldCell is { } worldCell)
					TryCastArmedSkillAtWorldCell(worldCell);
			}
			else
			{
				OpenInspectActorPanelAtCursor();
			}
			return true;
		}

		if (key.Keycode == Key.Escape)
		{
			if (_skillCastCursorActive)
				_log.Add(LocalizationService.T("ui.skill.targeting.canceled"));
			EndInspectMode();
			return true;
		}

		if (_inputBindings.Resolve(InputBindingContext.Action, key, out var actionId, out _))
		{
			switch (actionId)
			{
				case "move_north":
					MoveInspectCursor(0, -1);
					return true;
				case "move_south":
					MoveInspectCursor(0, 1);
					return true;
				case "move_west":
					MoveInspectCursor(-1, 0);
					return true;
				case "move_east":
					MoveInspectCursor(1, 0);
					return true;
				case "inspect_mode":
					if (_skillCastCursorActive)
						_log.Add(LocalizationService.T("ui.skill.targeting.canceled"));
					EndInspectMode();
					return true;
			}
		}

		if (key.Unicode == '*')
		{
			if (_skillCastCursorActive)
				_log.Add(LocalizationService.T("ui.skill.targeting.canceled"));
			EndInspectMode();
			return true;
		}

		return true;
	}

	private void StartInspectMode()
	{
		if (!_session.GameStarted || _menu.InMenu || _mapRender == null)
			return;

		_inspectModeActive = true;
		_skillCastCursorActive = false;
		_inspectWorldCell = new Vector3I(_state.PlayerX, _state.PlayerY, _state.PlayerZ);
		_inspectPreviousFocusId = _panels.FocusedId;
		_panels.SetFocus("map");
		_log.Add(LocalizationService.T("ui.inspect.entered"));
		LogInspectCellInfo();
		FlushMap();
	}

	private void EndInspectMode(bool restoreFocus = true)
	{
		if (!_inspectModeActive)
			return;

		CloseActorInspectPanel();
		_inspectModeActive = false;
		_skillCastCursorActive = false;
		_inspectWorldCell = null;
		_mapRender!.InspectWorldCell = null;

		var focusId = restoreFocus ? _inspectPreviousFocusId : null;
		_inspectPreviousFocusId = null;
		if (restoreFocus)
		{
			if (string.IsNullOrEmpty(focusId))
				_panels.ClearFocus();
			else
				_panels.SetFocus(focusId);
		}

		FlushMap();
	}

	private void MoveInspectCursor(int dx, int dy)
	{
		if (_inspectWorldCell is not { } current)
			return;

		var (halfW, halfH) = GetCurrentVisibleWorldHalfExtents();
		var nextX = Math.Clamp(current.X + dx, _state.PlayerX - halfW, _state.PlayerX + halfW);
		var nextY = Math.Clamp(current.Y + dy, _state.PlayerY - halfH, _state.PlayerY + halfH);
		if (nextX == current.X && nextY == current.Y)
			return;

		_inspectWorldCell = new Vector3I(nextX, nextY, _state.PlayerZ);
		if (!_skillCastCursorActive)
			LogInspectCellInfo();
		FlushMap();
	}

	private void LogInspectCellInfo()
	{
		if (_inspectWorldCell is not { } cell)
			return;

		var info = LookModule.DescribeCell(_state, _fogTracker, cell.X, cell.Y, cell.Z);
		_log.Add(info.Text);
	}

	private void OpenInspectActorPanelAtCursor()
	{
		if (_inspectWorldCell is not { } cell)
			return;

		var info = LookModule.DescribeCell(_state, _fogTracker, cell.X, cell.Y, cell.Z);
		if (info.InspectableActor == null)
		{
			_log.Add(LocalizationService.T("ui.inspect.no_actor"));
			return;
		}

		OpenActorInspectPanel(info.InspectableActor);
	}

	private void RefreshActorInspectPanel()
	{
		if (_actorInspectPanel == null || !_actorInspectPanel.Visible)
			return;

		if (string.IsNullOrEmpty(_inspectActorId))
		{
			CloseActorInspectPanel();
			return;
		}

		var actor = ActorModule.GetById(_state, _inspectActorId);
		if (actor == null)
		{
			CloseActorInspectPanel();
			return;
		}

		ActorDerivedStateUpdater.SyncInspectActor(_state, actor);
		_actorInspectPanel.Refresh(_state, actor);
	}

	private void OpenActorInspectPanel(Actor actor)
	{
		var panel = EnsureActorInspectPanel();
		_inspectActorId = actor.Id;
		ActorDerivedStateUpdater.SyncInspectActor(_state, actor);
		panel.Open(_state, actor);
		_panels.PushFocus(panel);
	}

	private void CloseActorInspectPanel()
	{
		_inspectActorId = null;
		if (_actorInspectPanel == null || !_actorInspectPanel.Visible)
			return;

		_actorInspectPanel.Close();
		_panels.OnPanelClosed(_actorInspectPanel);
	}
}
