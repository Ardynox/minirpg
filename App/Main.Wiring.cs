using MiniRPG.Core.World;
using MiniRPG.Module.WorldTool;

namespace MiniRPG;

public partial class Main
{
	private void WireEventHandlers()
	{
		_settingsFlow.RenderToggleRequested += ToggleRender;
		_settingsFlow.WatchModeToggleRequested += ToggleWatchMode;
		_settingsFlow.FastTurnModeToggleRequested += ToggleFastTurnMode;
		_settingsFlow.KeyboardTargetingToggleRequested += ToggleKeyboardTargeting;
		_settingsFlow.AutoNavigationInterruptPolicyCycleRequested += CycleAutoNavigationInterruptPolicy;
		_settingsFlow.DebugPanelToggleRequested += ToggleDebugPanelSetting;
		_settingsFlow.MapZoomMinDecreaseRequested += DecreaseMapZoomMin;
		_settingsFlow.MapZoomMinIncreaseRequested += IncreaseMapZoomMin;
		_settingsFlow.MapZoomMaxDecreaseRequested += DecreaseMapZoomMax;
		_settingsFlow.MapZoomMaxIncreaseRequested += IncreaseMapZoomMax;
		_settingsFlow.MapEditorToggleRequested += () =>
		{
			if (GuardMultiplayer("ui.multiplayer.disabled.map_editor", "Map editor is disabled in multiplayer sessions."))
				return;
			_mainAppFlowCoordinator.ToggleMapEditor();
		};

		_settingsFlow.LayoutEditRequested += _mainAppFlowCoordinator.OpenLayoutEditMode;
		_settingsFlow.SaveRequested += () =>
		{
			if (GuardMultiplayerSave()) return;
			DoSaveCurrent();
		};
		_settingsFlow.LoadRequested += () =>
		{
			if (GuardMultiplayerLoad()) return;
			_mainAppFlowCoordinator.OpenWorldManager(WorldManagerContext.InGame, WorldLaunchTab.Worlds);
		};
		_settingsFlow.LanguageChangedRequested += HandleLanguageChanged;
		_settingsFlow.QuickSaveRequested += () =>
		{
			if (GuardMultiplayerSave()) return;
			var path = _session.GetQuickSavePath();
			DoSave(path, _session.DescribeSavePath(path));
		};
		_settingsFlow.QuickLoadRequested += () =>
		{
			if (GuardMultiplayerLoad()) return;
			_mainAppFlowCoordinator.HandleQuickLoadRequested();
		};
		_settingsFlow.ReturnToMenuRequested += () =>
		{
			if (IsMultiplayerSession)
			{
				HandleMultiplayerReturnToMenu();
				return;
			}

			_mainAppFlowCoordinator.HandleBackToMenu();
		};
		_settingsFlow.MultiplayerRoomRequested += HandleMultiplayerRoomRequested;
		_settingsFlow.MainMenuRestoreRequested += ShowMainMenuWithCurrentContinue;
		_layoutEditBar.ApplyRequested += ApplyLayoutEditMode;
		_layoutEditBar.CancelRequested += CancelLayoutEditMode;
		_layoutEditBar.ResetRequested += ResetLayoutEditMode;
		_worldManager.CloseRequested += _mainAppFlowCoordinator.CloseWorldManager;
		_worldManager.CreateWorldRequested += _mainAppFlowCoordinator.OpenWorldSettingsDialog;
		_worldManager.DeleteWorldRequested += _mainAppFlowCoordinator.HandleWorldManagerDeleteWorldRequested;
		_worldManager.DeleteSaveDataRequested += _mainAppFlowCoordinator.HandleWorldManagerDeleteSaveDataRequested;
		_worldManager.CleanAssetsRequested += _mainAppFlowCoordinator.HandleWorldManagerCleanAssetsRequested;
		_worldManager.CreateCharacterRequested += _mainAppFlowCoordinator.HandleWorldManagerCreateCharacterRequested;
		_worldManager.ContinueCharacterRequested += _mainAppFlowCoordinator.HandleWorldManagerContinueCharacterRequested;
		_worldManager.ScenarioRequested += _mainAppFlowCoordinator.HandleWorldManagerScenarioRequested;
		_worldManager.LegacySaveRequested += slot =>
		{
			if (GuardMultiplayerLoad()) return;
			_mainAppFlowCoordinator.HandleWorldManagerLegacySaveRequested(slot);
		};
		_mapEditorBar.CategorySelected += category => _mapEditorCoordinator.SelectCategory(category);
		_mapEditorBar.ToolModeSelected += toolMode => _mapEditorCoordinator.SelectToolMode(toolMode);
		_mapEditorBar.BrushSelected += index => _mapEditorCoordinator.SelectBrush(index);
		_mapEditorBar.UndoRequested += () => _mapEditorCoordinator.HandleUndo();
		_mapEditorBar.RedoRequested += () => _mapEditorCoordinator.HandleRedo();
		_mapEditorBar.TimeOfDayChanged += turn => _mapEditorCoordinator.HandleTimeOfDayChanged(turn);
		_mapEditorBar.WeatherTypeChanged += index => _mapEditorCoordinator.HandleWeatherTypeChanged(index);
		_mapEditorBar.WeatherIntensityChanged += index => _mapEditorCoordinator.HandleWeatherIntensityChanged(index);
		_mapEditorBar.LightingProfileChanged += index => _mapEditorCoordinator.HandleLightingProfileChanged(index);
		_mapEditorBar.SaveRequested += HandleMapEditorSaveRequested;
		_mapEditorBar.ExitRequested += () => ExitMapEditor();
		_mapEditorBar.CenterRequested += () => _mapEditorCoordinator.CenterOnPlayer();
		_mapEditorBar.HeightChanged += delta => _mapEditorCoordinator.HandleHeightChanged(delta);
		_mapEditorBar.IgnoreConnectivityRequirementChanged += ignore => _mapEditorCoordinator.HandleIgnoreConnectivityRequirementChanged(ignore);
		_mapEditorBar.TurnControllerRequested += () => _mapEditorCoordinator.HandleTurnControllerRequested();
		_runtimeWorldToolBar.ToolModeSelected += toolMode =>
		{
			_runtimeWorldToolSession.SetToolMode(toolMode);
			RefreshRuntimeWorldHoverPresentation(_runtimeWorldToolSession.HoverWorld);
			FlushMap();
		};
		_runtimeWorldToolBar.CategorySelected += category =>
		{
			_runtimeWorldToolSession.SetCategory(category);
			RefreshRuntimeWorldHoverPresentation(_runtimeWorldToolSession.HoverWorld);
			FlushMap();
		};
		_runtimeWorldToolBar.BrushSelected += index =>
		{
			if (_runtimeWorldToolSession.CurrentCategory == WorldToolCategory.Facility)
				_runtimeWorldToolSession.SelectFacilityBrush(index);
			else
				_runtimeWorldToolSession.SelectTerrainBrush(index);
			RefreshRuntimeWorldHoverPresentation(_runtimeWorldToolSession.HoverWorld);
			FlushMap();
		};
		_runtimeWorldToolBar.RotateRequested += delta =>
		{
			_runtimeWorldToolSession.RotateFacility(delta);
			RefreshRuntimeWorldToolBar();
			FlushMap();
		};
		_runtimeWorldToolHeightPanel.HeightChanged += delta => AdjustRuntimeWorldToolHeight(delta);
		_runtimeWorldToolHeightPanel.CenterRequested += CenterRuntimeWorldToolCameraOnPlayer;
		_saveNameDialog.ConfirmRequested += HandleSaveNameConfirmed;
		_saveNameDialog.CancelRequested += CloseSaveNameDialog;
		_characterCreation.ConfirmRequested += _mainAppFlowCoordinator.HandleCharacterCreationConfirmed;
		_characterCreation.CancelRequested += _mainAppFlowCoordinator.HandleCharacterCreationCanceled;
		_worldSettingsDialog.ConfirmRequested += _mainAppFlowCoordinator.HandleWorldSettingsConfirmed;
		_worldSettingsDialog.CancelRequested += _mainAppFlowCoordinator.HandleWorldSettingsCanceled;
		_confirmDialog.ActionSelected += _mainAppFlowCoordinator.HandleConfirmDialogActionSelected;
		_confirmDialog.CancelRequested += _mainAppFlowCoordinator.CloseConfirmDialog;
		_loadRecoveryDialog.RecoveryConfirmed += _mainAppFlowCoordinator.HandleLoadRecoveryConfirmed;
		_loadRecoveryDialog.CancelRequested += _mainAppFlowCoordinator.CloseLoadRecoveryDialog;

		_menu.OnContinue += _mainAppFlowCoordinator.HandleMenuContinue;
		_menu.OnWorlds += _mainAppFlowCoordinator.HandleMenuWorlds;
		_menu.OnMapEditor += HandleMenuMapEditor;
		_menu.OnMultiplayer += HandleMenuMultiplayer;

		_menu.OnAutoTest += HandleAutoTest;
		_menu.OnQuit += () => GetTree().Quit();
		_menu.OnOpenSettings += _mainAppFlowCoordinator.OpenMenuSettingsPanel;
		_multiplayerHub.BackRequested += HandleMultiplayerHubBackRequested;
		_multiplayerHub.SaveSettingsRequested += HandleMultiplayerHubSaveSettingsRequested;
		_multiplayerHub.RefreshRoomsRequested += HandleMultiplayerHubRefreshRequested;
		_multiplayerHub.JoinRoomRequested += HandleMultiplayerHubJoinRoomRequested;
		_multiplayerHub.JoinByCodeRequested += HandleMultiplayerHubJoinByCodeRequested;
		_multiplayerHub.CreateRoomRequested += HandleMultiplayerHubCreateRequested;
		_multiplayerHub.ReconnectRequested += HandleMultiplayerHubReconnectRequested;
		_multiplayerRoomPanel.CloseRequested += CloseMultiplayerRoomPanel;
		_multiplayerRoomPanel.HostCurrentSessionRequested += HandleMultiplayerRoomHostCurrentSessionRequested;
		_multiplayerRoomPanel.AssignPrimaryActorRequested += HandleMultiplayerRoomAssignPrimaryActorRequested;
		_multiplayerRoomPanel.KickPlayerRequested += HandleMultiplayerRoomKickPlayerRequested;
		_multiplayerRoomPanel.ReclaimPrimaryActorRequested += HandleMultiplayerRoomReclaimPrimaryActorRequested;
	}
}
