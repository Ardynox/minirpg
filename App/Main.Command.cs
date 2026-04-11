using System.Collections.Generic;
using MiniRPG.Core.World;

namespace MiniRPG;

public partial class Main
{
	// ══════════════════════════════════════════════════════
	//  命令分发（InputModule 产出命令字符串 → 此处路由到具体逻辑）
	// ══════════════════════════════════════════════════════

	/// <summary>
	/// 核心命令路由。命令来源：InputModule 的键盘映射或文本输入。
	/// 前缀 ":" 的是快捷键命令，无前缀的是文本命令。
	/// </summary>
	private void OnCommand(string cmd)
	{
		var fogMapVisible = _mapRender?.FogMapVisible == true;

		if (PlayerDead)
		{
			PlayerDead = false;
			ShowMainMenuWithCurrentContinue();
			return;
		}

		if (_playerRestModeActive && cmd != ":rest")
			_playerRestModeActive = false;

		if (MapEditorActive)
			return;

		if (cmd.StartsWith(":dig_") && cmd != ":dig")
		{
			HandleDigDirection(cmd[":dig_".Length..]);
			return;
		}
		if (cmd is ":dig_up" or "dig_up")
		{
			HandleDigDirection("up");
			return;
		}
		if (cmd is ":dig_down" or "dig_down")
		{
			HandleDigDirection("down");
			return;
		}
		if (cmd == ":dir_cancel")
		{
			_log.Add(LocalizationService.T("ui.selection.canceled"));
			return;
		}

		if (cmd is ":settings" or "settings")
		{
			if (fogMapVisible && _mapRender != null)
			{
				_mapRender.FogMapVisible = false;
				_log.Add(LocalizationService.T("log.fog_map.closed"));
				FlushMap();
				return;
			}

			ToggleSettingsPanel();
			return;
		}

		if (cmd == ":inspect_mode")
		{
			HandleInspectToggleCommand();
			return;
		}

		if (cmd == ":debug_panel")
		{
			ToggleDebugPanel();
			return;
		}

		if (IsTimelineInputLocked())
			return;

		switch (cmd)
		{
			case ":quicksave":
			{
				if (IsMultiplayerSession)
				{
					_log.Add(LocalizationService.TOrFallback("ui.multiplayer.disabled.save", "Saving local files is disabled in multiplayer sessions."));
					return;
				}
				var path = _session.GetQuickSavePath();
				DoSave(path, _session.DescribeSavePath(path));
				return;
			}
			case ":quickload":
			{
				if (IsMultiplayerSession)
				{
					_log.Add(LocalizationService.TOrFallback("ui.multiplayer.disabled.load", "Loading local saves is disabled in multiplayer sessions."));
					return;
				}
				_mainAppFlowCoordinator.HandleQuickLoadRequested();
				return;
			}
			case ":interact" or "interact": DoInteract(); return;
			case ":dig": StartDig(); return;
			case ":inventory": ToggleInventory(); return;
			case ":rest": TogglePlayerRestMode(); return;
			case ":skills": ToggleSkillManager(); return;
			case ":skillbar": ToggleSkillBarPanel(); return;
			case ":skill_prev": if (_skillBar.Visible) _skillBar.StepSelection(-1); return;
			case ":skill_next": if (_skillBar.Visible) _skillBar.StepSelection(1); return;
			case ":toggle_status": ToggleStatusPanel(); return;
			case ":cycle_party": _partyHud.CycleActive(); FlushMap(); return;
			case ":quests": ToggleQuestPanel(); return;
			case ":render" or "render": ToggleRender(); return;
			case ":lighting" or "lighting": CycleLightingProfile(); return;
			case ":status_prev": _statusPanelModule.CycleTab(-1); return;
			case ":status_next": _statusPanelModule.CycleTab(1); return;
			case ":minimap": ToggleMinimap(); return;
			case ":fogmap": ToggleFogMap(); return;
			case ":fogmap_center": CenterFogMap(); return;
		}

		if (_settingsFlow.SettingsVisible) return;

		if (fogMapVisible && _mapRender != null)
		{
			switch (cmd)
			{
				case "w": _mapRender.ScrollFogMap(0, -1); FlushMap(); break;
				case "s": _mapRender.ScrollFogMap(0, 1); FlushMap(); break;
				case "a": _mapRender.ScrollFogMap(-1, 0); FlushMap(); break;
				case "d": _mapRender.ScrollFogMap(1, 0); FlushMap(); break;
			}
			return;
		}

		switch (cmd)
		{
			case "w": DoMove(0, -1); break;
			case "s": DoMove(0, 1); break;
			case "a": DoMove(-1, 0); break;
			case "d": DoMove(1, 0); break;
			case "look": DoLook(); break;
			case "enter": DoEnterStairs(); break;
			case ":climb_down":
			case "climb_down": DoClimb(true); break;
			case ":climb_up":
			case "climb_up": DoClimb(false); break;
			case "save":
				if (IsMultiplayerSession)
					_log.Add(LocalizationService.TOrFallback("ui.multiplayer.disabled.save", "Saving local files is disabled in multiplayer sessions."));
				else
					DoSaveCurrent();
				break;
			case "load":
				if (IsMultiplayerSession)
					_log.Add(LocalizationService.TOrFallback("ui.multiplayer.disabled.load", "Loading local saves is disabled in multiplayer sessions."));
				else
					OpenWorldManager(WorldManagerContext.InGame, WorldLaunchTab.Worlds);
				break;
			case "newmap":
				_session.NewGame(PlayerCreationOptions.CreateDefault());
				ClearPlayerTargeting();
				RefreshPlayerCharacterVisual();
				SyncTimelineAutoAdvanceState();
				_log.Add(LocalizationService.T("log.game.new_map_generated"));
				FlushMap();
				break;
			default:
				if (cmd.StartsWith('/'))
					_debugPanelController.HandleCommand(cmd);
				else
					_log.Add(LocalizationService.T("ui.command.unknown"));
				break;
		}
	}

	private void DoInteract() => _gameplayCommandCoordinator.DoInteract();
	private void StartDig() => _gameplayCommandCoordinator.StartDig();
	private void HandleDigDirection(string dir) => _gameplayCommandCoordinator.HandleDigDirection(dir);

	private void PickupGroundItem(Actor player, Item itemInfo)
	{
		var command = new PickupClientCommand
		{
			ActorId = player.Id,
			ItemInstanceId = itemInfo.InstanceId,
		};
		if (TrySubmitClientCommand(command))
		{
			_groundPanel.Invalidate();
			_groundPanel.Refresh();
			return;
		}

		var result = ServerActionGateway.Execute(_state, command);
		ApplyServerActionResult(result);
		_groundPanel.Invalidate();
		_groundPanel.Refresh();
	}

	private void DoLook() => _log.Add(LookModule.BuildLookText(_state, _fogTracker));
}
