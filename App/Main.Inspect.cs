using System;
using Godot;
using MiniRPG.Core.Data;

namespace MiniRPG;

public partial class Main
{
	private bool TryOpenActorInspectPanelAtMouse(Vector2 globalPosition)
	{
		if (_mapRender == null || !_mapRender.TryGetWorldCellFromGlobalPosition(globalPosition, out var worldCell))
			return false;

		var actor = LookModule.TryGetInspectableActor(_state, _fogTracker, worldCell.X, worldCell.Y, worldCell.Z);
		if (actor == null)
			return false;

		OpenActorInspectPanel(actor);
		return true;
	}

	private bool HandleSkillTargetCursorKey(InputEventKey key)
	{
		if (!_skillTargetCursorActive || !key.Pressed)
			return false;

		if (_actorInspectPanel?.Visible == true && _panels.FocusedId == _actorInspectPanel.PanelId)
			return false;

		if (key.Keycode is Key.Enter or Key.KpEnter)
		{
			if (_skillTargetWorldCell is { } worldCell)
				TryCastArmedSkillAtWorldCell(worldCell);
			return true;
		}

		if (key.Keycode == Key.Escape)
		{
			_log.Add(LocalizationService.T("ui.skill.targeting.canceled"));
			EndSkillTargetCursorMode();
			return true;
		}

		if (_inputBindings.Resolve(InputBindingContext.Action, key, out var actionId, out _))
		{
			switch (actionId)
			{
				case "move_north":
					MoveSkillTargetCursor(0, -1);
					return true;
				case "move_south":
					MoveSkillTargetCursor(0, 1);
					return true;
				case "move_west":
					MoveSkillTargetCursor(-1, 0);
					return true;
				case "move_east":
					MoveSkillTargetCursor(1, 0);
					return true;
			}
		}

		return true;
	}

	private void StartSkillTargetCursorMode()
	{
		var skill = GetArmedSkill();
		if (skill == null || !_session.GameStarted || _menu.InMenu || _mapRender == null)
			return;

		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return;

		_skillTargetCursorActive = true;
		_skillTargetWorldCell = ResolveSkillCursorOriginCell(player);
		_skillTargetPreviousFocusId = _panels.FocusedId;
		CloseActorInspectPanel();
		_panels.SetFocus("map");
		_log.Add(LocalizationService.T("ui.skill.targeting.entered", ("skill", skill.Name)));
		FlushMap();
	}

	private void EndSkillTargetCursorMode(bool restoreFocus = true)
	{
		if (!_skillTargetCursorActive)
			return;

		_skillTargetCursorActive = false;
		_skillTargetWorldCell = null;

		var focusId = restoreFocus ? _skillTargetPreviousFocusId : null;
		_skillTargetPreviousFocusId = null;
		if (restoreFocus)
		{
			if (string.IsNullOrEmpty(focusId))
				_panels.ClearFocus();
			else
				_panels.SetFocus(focusId);
		}

		FlushMap();
	}

	private void MoveSkillTargetCursor(int dx, int dy)
	{
		if (_skillTargetWorldCell is not { } current)
			return;

		var (halfW, halfH) = GetCurrentVisibleWorldHalfExtents();
		var nextX = Math.Clamp(current.X + dx, _state.PlayerX - halfW, _state.PlayerX + halfW);
		var nextY = Math.Clamp(current.Y + dy, _state.PlayerY - halfH, _state.PlayerY + halfH);
		if (nextX == current.X && nextY == current.Y)
			return;

		_skillTargetWorldCell = new Vector3I(nextX, nextY, _state.PlayerZ);
		FlushMap();
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
