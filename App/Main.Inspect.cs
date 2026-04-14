using System;
using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Module;

namespace MiniRPG;

public partial class Main
{
	private bool TryOpenActorInspectPanelAtMouse(Vector2 globalPosition)
	{
		if (!TryResolveInspectTargetAtMouse(globalPosition, out var target))
			return false;

		if (target.Kind is not (LookInspectTargetKind.Actor or LookInspectTargetKind.CorpseItem))
			return false;

		OpenStatusPanelForInspectTarget(target);
		return true;
	}

	private bool HandleSkillTargetCursorKey(InputEventKey key)
	{
		if (!_skillTargetCursorActive || !key.Pressed)
			return false;

		if (_statusPanelController.HasFocusedPanel)
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
		_statusPanelController.CloseFocusedPanel();
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

	private void RefreshStatusPanels() => _statusPanelController.RefreshVisiblePanels();

	private void OpenActorInspectPanel(Actor actor) => _statusPanelController.OpenActor(actor);

	private void OpenStatusPanelForCorpse(Item corpse, Vector3I cell) =>
		_statusPanelController.OpenCorpse(corpse, cell);

	private void OpenStatusPanelForInspectTarget(LookInspectTarget target) =>
		_statusPanelController.OpenInspectTarget(target);

	private void CloseActorInspectPanel() => _statusPanelController.CloseFocusedPanel();

	private bool TryResolveInspectTargetAtMouse(Vector2 globalPosition, out LookInspectTarget target)
	{
		target = default;
		if (_mapRender == null)
			return false;

		if (_mapRender.TryGetInspectWorldCellFromGlobalPosition(globalPosition, out var inspectCell))
		{
			target = LookModule.ResolveInspectTarget(_state, _fogTracker, inspectCell.X, inspectCell.Y, inspectCell.Z);
			return true;
		}

		if (_mapRender.TryGetWorldCellFromGlobalPosition(globalPosition, out var fallbackCell))
		{
			target = LookModule.ResolveInspectTarget(_state, _fogTracker, fallbackCell.X, fallbackCell.Y, fallbackCell.Z);
			return true;
		}

		return false;
	}

	private LookInspectTarget ResolveInspectTargetFromRuntimePointerOrCell(Vector3I fallbackCell)
	{
		if (_mapRender != null
			&& _runtimeWorldToolHasLastPointerGlobalPosition
			&& _mapRender.TryGetInspectWorldCellFromGlobalPosition(_runtimeWorldToolLastPointerGlobalPosition, out var inspectCell))
		{
			return LookModule.ResolveInspectTarget(_state, _fogTracker, inspectCell.X, inspectCell.Y, inspectCell.Z);
		}

		return LookModule.ResolveInspectTarget(_state, _fogTracker, fallbackCell.X, fallbackCell.Y, fallbackCell.Z);
	}
}
