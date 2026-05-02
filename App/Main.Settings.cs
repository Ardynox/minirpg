using System;
using System.Globalization;
using Godot;

namespace MiniRPG;

public partial class Main
{
	private void HideSettingsPanels()
	{
		_panelChrome.CloseActiveSettings();
		while (_settingsFlow.HasVisibleOverlay)
			_settingsFlow.CloseActiveOverlay();
	}

	private void CloseSettingsOverlayIfVisible()
	{
		if (_settingsFlow.SettingsVisible)
			_settingsFlow.CloseActiveOverlay();
	}

	private static PredictionConfig LoadPredictionConfigFromEnvironment()
	{
		var threshold = ParseIntEnvironment("MINIRPG_PREDICTION_ROLLBACK_THRESHOLD", PredictionConfig.Default.RollbackThresholdManhattan, 0, 8);
		var maxPending = ParseIntEnvironment("MINIRPG_PREDICTION_MAX_PENDING", PredictionConfig.Default.MaxPendingCommands, 8, 256);
		return new PredictionConfig(threshold, maxPending);
	}

	private static float LoadPredictionSmoothingSecondsFromEnvironment()
	{
		var raw = System.Environment.GetEnvironmentVariable("MINIRPG_PREDICTION_SMOOTHING_SECONDS");
		if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
			return 0.10f;
		return Math.Clamp(parsed, 0.02f, 0.35f);
	}

	private static int ParseIntEnvironment(string name, int fallback, int min, int max)
	{
		var raw = System.Environment.GetEnvironmentVariable(name);
		if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
			return fallback;
		return Math.Clamp(parsed, min, max);
	}

	private SettingsUiState BuildSettingsUiState(SettingsEntryContext? context = null)
	{
		var resolvedContext = context ?? (_menu.InMenu
			? SettingsEntryContext.MainMenu
			: SettingsEntryContext.InGamePause);

		return new SettingsUiState(
			resolvedContext,
			LocalizationService.CurrentLocale,
			RenderReady,
			_watchModeEnabled,
			_fastTurnModeEnabled,
			MapEditorActive,
			_session.GameStarted,
			_enableKeyboardTargeting,
			_enableDebugPanel,
			_mapZoomMin,
			_mapZoomMax,
			_mapRender?.Zoom ?? 1.0f,
			UIScaleService.CurrentScale,
			UIAccessibilityService.CurrentContrastMode == Module.Panel.UIContrastMode.High,
			ColorBlindModeToString(UIAccessibilityService.CurrentColorBlindMode),
			_autoNav.InterruptPolicy);
	}

	private static string ColorBlindModeToString(Module.Panel.UIColorBlindMode mode) => mode switch
	{
		Module.Panel.UIColorBlindMode.Protanopia => "protanopia",
		Module.Panel.UIColorBlindMode.Deuteranopia => "deuteranopia",
		Module.Panel.UIColorBlindMode.Tritanopia => "tritanopia",
		_ => "none",
	};

	private void SyncSettingsUiState(SettingsEntryContext? context = null)
	{
		if (_settingsFlow == null)
			return;

		_settingsFlow.ApplyState(BuildSettingsUiState(context));
	}

	private void RefreshMainMenuContinueState()
	{
		if (_session == null || _menu == null)
			return;

		var continueTarget = _session.ResolveContinueTarget();
		_menu.RefreshMainMenuState(
			continueTarget.Kind != ContinueTargetKind.None,
			ResourcesReady,
			_session.BuildContinueButtonText(continueTarget));
	}

	private void ShowMainMenuWithCurrentContinue()
	{
		var continueTarget = _session.ResolveContinueTarget();
		_multiplayerHub?.Close();
		CloseAllInGamePanels();
		_menu.ShowMainMenu(
			continueTarget.Kind != ContinueTargetKind.None,
			ResourcesReady,
			_session.BuildContinueButtonText(continueTarget));
		SetWorldHoverCell(null, flushMap: false);
		CancelAutoNavigation(AutoNavigationStopReason.SessionReset, emitLog: false);
		RefreshRuntimeWorldToolBar();
	}

	private void ToggleKeyboardTargeting()
	{
		_enableKeyboardTargeting = !_enableKeyboardTargeting;
		AppSettingsStore.SaveEnableKeyboardTargeting(_enableKeyboardTargeting);
		SyncSettingsUiState();

		if (!_enableKeyboardTargeting && _skillTargetCursorActive)
			EndSkillTargetCursorMode(restoreFocus: false);
	}

	private void ToggleFastTurnMode()
	{
		_fastTurnModeEnabled = !_fastTurnModeEnabled;
		AppSettingsStore.SaveFastTurnMode(_fastTurnModeEnabled);
		SyncSettingsUiState();
	}

	private void ToggleDebugPanelSetting()
	{
		_enableDebugPanel = !_enableDebugPanel;
		AppSettingsStore.SaveEnableDebugPanel(_enableDebugPanel);
		if (!_enableDebugPanel)
			CloseDebugPanel();
		SyncSettingsUiState();
	}

	private void DecreaseMapZoomMin() => AdjustMapZoomMin(-0.1f);

	private void IncreaseMapZoomMin() => AdjustMapZoomMin(0.1f);

	private void DecreaseMapZoomMax() => AdjustMapZoomMax(-0.1f);

	private void IncreaseMapZoomMax() => AdjustMapZoomMax(0.1f);

	private void DecreaseUiFontScale() => AdjustUiFontScale(-0.05f);

	private void IncreaseUiFontScale() => AdjustUiFontScale(0.05f);

	private static Module.Panel.UIContrastMode ParseContrastMode(string? raw) => raw?.Trim().ToLowerInvariant() switch
	{
		"high" => Module.Panel.UIContrastMode.High,
		_ => Module.Panel.UIContrastMode.Normal,
	};

	private static Module.Panel.UIColorBlindMode ParseColorBlindMode(string? raw) => raw?.Trim().ToLowerInvariant() switch
	{
		"protanopia" => Module.Panel.UIColorBlindMode.Protanopia,
		"deuteranopia" => Module.Panel.UIColorBlindMode.Deuteranopia,
		"tritanopia" => Module.Panel.UIColorBlindMode.Tritanopia,
		_ => Module.Panel.UIColorBlindMode.None,
	};

	private void AdjustUiFontScale(float delta)
	{
		var current = UIScaleService.CurrentScale;
		var next = UIScaleService.Clamp(current + delta);
		if (Mathf.IsEqualApprox(next, current))
			return;

		UIScaleService.Apply(_uiTheme, next);
		AppSettingsStore.SaveUiFontScale(next);
		SyncSettingsUiState();
	}

	private void ToggleHighContrast()
	{
		var next = UIAccessibilityService.CurrentContrastMode == Module.Panel.UIContrastMode.High
			? Module.Panel.UIContrastMode.Normal
			: Module.Panel.UIContrastMode.High;
		UIAccessibilityService.Apply(_uiTheme, next, UIAccessibilityService.CurrentColorBlindMode);
		AppSettingsStore.SaveUiContrastMode(next == Module.Panel.UIContrastMode.High ? "high" : "normal");
		SyncSettingsUiState();
	}

	private void HandleColorBlindModeChange(string mode)
	{
		var parsed = ParseColorBlindMode(mode);
		UIAccessibilityService.Apply(_uiTheme, UIAccessibilityService.CurrentContrastMode, parsed);
		AppSettingsStore.SaveUiColorBlindMode(mode);
		SyncSettingsUiState();
	}

	private void AdjustMapZoomMin(float delta)
	{
		var next = Math.Clamp(_mapZoomMin + delta, 0.2f, 4.0f);
		next = Math.Min(next, _mapZoomMax);
		if (Mathf.IsEqualApprox(next, _mapZoomMin))
			return;

		_mapZoomMin = next;
		PersistAndApplyMapZoomRange();
	}

	private void AdjustMapZoomMax(float delta)
	{
		var next = Math.Clamp(_mapZoomMax + delta, 0.2f, 4.0f);
		next = Math.Max(next, _mapZoomMin);
		if (Mathf.IsEqualApprox(next, _mapZoomMax))
			return;

		_mapZoomMax = next;
		PersistAndApplyMapZoomRange();
	}

	private void PersistAndApplyMapZoomRange()
	{
		if (_mapZoomMin > _mapZoomMax)
			(_mapZoomMin, _mapZoomMax) = (_mapZoomMax, _mapZoomMin);

		AppSettingsStore.SaveMapZoomMin(_mapZoomMin);
		AppSettingsStore.SaveMapZoomMax(_mapZoomMax);
		_mapRender?.SetZoomRange(_mapZoomMin, _mapZoomMax);
		SyncSettingsUiState();
		FlushMap();
	}

	private void HandleLanguageChanged(string locale)
	{
		AppSettingsStore.SaveLocale(locale);
		var changed = LocalizationService.SetLocale(locale);
		SyncSettingsUiState();
		if (!changed)
			return;

		GameLocalizer.ApplyPresetTranslations();
		GameLocalizer.RelocalizeGameState(_state);
		MiniRPG.Core.Conversation.ConversationDefLoader.EnsureLoaded();
		_mapEditor.RefreshLocalizedBrushes();
		RefreshLocalizedUi(clearLogs: _session.GameStarted);
		RefreshStartupUi();
	}

	private void RefreshLocalizedUi(bool clearLogs)
	{
		LocalizationService.LocalizeTree(this);
		_multiplayerHub.RefreshTexts();
		_multiplayerRoomPanel.RefreshTexts();
		_multiplayerHubCoordinator.SetTemplates(BuildMultiplayerHubTemplates());
		_settingsFlow.RefreshTexts();
		_audioSettings?.RefreshTexts();
		SyncSettingsUiState();
		_worldManager.RefreshTexts();
		_worldSettingsDialog.RefreshTexts();
		_characterCreation.RefreshTexts();
		_characterCustomization.RefreshTexts();
		_loadRecoveryDialog.RefreshTexts();
		_mapEditorBar.RefreshTexts();
		_runtimeWorldToolSession.RefreshBrushes();
		_runtimeWorldToolBar.RefreshTexts();
		_runtimeWorldToolHeightPanel.RefreshTexts();
		_threatHud.RefreshTexts();
		_targetSummaryHud.RefreshTexts();
		_needsHud.RefreshTexts(ActorModule.GetPlayer(_state), _state.Turn);
		_healthAlerts.RefreshTexts();
		RefreshMainMenuContinueState();
		RefreshStartupUi();
		RefreshMultiplayerRoomPanelState();

		if (MapEditorActive)
			RefreshMapEditorBar();

		RefreshRuntimeWorldToolBar();

		RefreshVisiblePanels();
		if (IsWorldManagerOpen)
			_mainAppFlowCoordinator.RefreshWorldManagerContents();

		if (clearLogs)
		{
			_log.Clear();
			if (MapEditorActive)
				ShowMapEditorHints();
			else
				ShowGameHints();
		}

		MarkUIDirty();
		ProcessDirtyPanels();
		if (_session.GameStarted && !_menu.InMenu && RenderReady)
			FlushMap();
	}
}
