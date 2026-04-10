using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Module;

namespace MiniRPG.Module.Panel;

public sealed class SettingsPanelModule : ISettingsOverlay, IPanel
{
	private const string ContentScrollPath = "Margin/VBox/ContentScroll";
	private const string GeneralPagePath = "Margin/VBox/ContentScroll/Pages/GeneralPage";
	private const string ControlsPagePath = "Margin/VBox/ContentScroll/Pages/ControlsPage";
	private const string SessionPagePath = "Margin/VBox/ContentScroll/Pages/SessionPage";
	private const string LanguageRowPath = GeneralPagePath + "/DisplaySection/Margin/VBox/LanguageRow";
	private const string RenderRowPath = GeneralPagePath + "/DisplaySection/Margin/VBox/RenderRow";
	private const string MapZoomMinRowPath = GeneralPagePath + "/DisplaySection/Margin/VBox/MapZoomMinRow";
	private const string MapZoomMaxRowPath = GeneralPagePath + "/DisplaySection/Margin/VBox/MapZoomMaxRow";
	private const string WatchModeRowPath = GeneralPagePath + "/GameplaySection/Margin/VBox/WatchModeRow";
	private const string FastTurnModeRowPath = GeneralPagePath + "/GameplaySection/Margin/VBox/FastTurnModeRow";
	private const string KeyboardTargetingRowPath = ControlsPagePath + "/ControlOptionsSection/Margin/VBox/KeyboardTargetingRow";
	private const string DebugPanelRowPath = ControlsPagePath + "/ControlOptionsSection/Margin/VBox/DebugPanelRow";
	private const string BindingsRowPath = ControlsPagePath + "/BindingsSection/Margin/VBox/BindingsRow";
	private const string KeyBindingsRootPath = ControlsPagePath + "/BindingsSection/Margin/VBox/KeyBindingsView";
	private const string SaveRowPath = SessionPagePath + "/SaveLoadSection/Margin/VBox/SaveRow";
	private const string LoadRowPath = SessionPagePath + "/SaveLoadSection/Margin/VBox/LoadRow";
	private const string MapEditorRowPath = SessionPagePath + "/ToolsSection/Margin/VBox/MapEditorRow";
	private const string WeatherLabRowPath = SessionPagePath + "/ToolsSection/Margin/VBox/WeatherLabRow";
	private const string LayoutEditRowPath = SessionPagePath + "/ToolsSection/Margin/VBox/LayoutEditRow";

	private readonly PanelContainer _panel;
	private readonly Label _titleLabel;
	private readonly Label _subtitleLabel;
	private readonly Label _footerHintLabel;
	private readonly ScrollContainer _contentScroll;
	private readonly Button _generalTabButton;
	private readonly Button _controlsTabButton;
	private readonly Button _sessionTabButton;
	private readonly Control _generalPage;
	private readonly Control _controlsPage;
	private readonly Control _sessionPage;
	private readonly Label _displaySectionTitle;
	private readonly Label _gameplaySectionTitle;
	private readonly Label _controlsSectionTitle;
	private readonly Label _bindingsSectionTitle;
	private readonly Label _saveLoadSectionTitle;
	private readonly Label _toolsSectionTitle;
	private readonly Label _languageTitleLabel;
	private readonly Label _languageStatusLabel;
	private readonly Label _renderTitleLabel;
	private readonly Label _renderStatusLabel;
	private readonly Label _mapZoomMinTitleLabel;
	private readonly Label _mapZoomMinStatusLabel;
	private readonly Label _mapZoomMaxTitleLabel;
	private readonly Label _mapZoomMaxStatusLabel;
	private readonly Label _watchModeTitleLabel;
	private readonly Label _watchModeStatusLabel;
	private readonly Label _fastTurnModeTitleLabel;
	private readonly Label _fastTurnModeStatusLabel;
	private readonly Label _keyboardTargetingTitleLabel;
	private readonly Label _keyboardTargetingStatusLabel;
	private readonly Label _debugPanelTitleLabel;
	private readonly Label _debugPanelStatusLabel;
	private readonly Label _bindingsTitleLabel;
	private readonly Label _bindingsStatusLabel;
	private readonly Label _saveTitleLabel;
	private readonly Label _saveStatusLabel;
	private readonly Label _loadTitleLabel;
	private readonly Label _loadStatusLabel;
	private readonly Label _mapEditorTitleLabel;
	private readonly Label _mapEditorStatusLabel;
	private readonly Label _weatherLabTitleLabel;
	private readonly Label _weatherLabStatusLabel;
	private readonly Label _layoutEditTitleLabel;
	private readonly Label _layoutEditStatusLabel;
	private readonly OptionButton _languageOption;
	private readonly Button _renderToggleButton;
	private readonly Button _mapZoomMinDecreaseButton;
	private readonly Button _mapZoomMinIncreaseButton;
	private readonly Button _mapZoomMaxDecreaseButton;
	private readonly Button _mapZoomMaxIncreaseButton;
	private readonly CheckButton _watchModeButton;
	private readonly CheckButton _fastTurnModeButton;
	private readonly CheckButton _keyboardTargetingButton;
	private readonly CheckButton _debugPanelToggleButton;
	private readonly Button _bindingsButton;
	private readonly Button _saveButton;
	private readonly Button _loadButton;
	private readonly Button _mapEditorButton;
	private readonly Button _weatherLabButton;
	private readonly Button _layoutEditButton;
	private readonly Button _backButton;
	private readonly Control _keyBindingsRoot;
	private readonly KeyBindingsView _keyBindingsView;
	private readonly Dictionary<SettingsTab, Button> _tabButtons;
	private readonly Dictionary<SettingsPanelRowId, PanelContainer> _rowPanels;
	private readonly SettingsPanelSelectionModel _selectionModel = new();
	private readonly StyleBoxFlat _rowNormalStyle;
	private readonly StyleBoxFlat _rowSelectedStyle;

	private SettingsUiState _state;
	private bool _suppressLanguageChange;
	private bool _suppressToggleSignals;

	public string PanelId => "settings";
	public PanelContainer PanelNode => _panel;
	public bool Visible { get => _panel.Visible; set => _panel.Visible = value; }
	public bool ConsumeUnhandledKeys => true;
	public bool Dirty { get; set; }
	public SettingsEntryContext Context => _state.Context;
	public bool IsCapturingKeyBindings => _selectionModel.KeyBindingsMode && _keyBindingsView.IsCapturing;

	public event Action? BackRequested;
	public event Action? RenderToggleRequested;
	public event Action? WatchModeToggleRequested;
	public event Action? FastTurnModeToggleRequested;
	public event Action? KeyboardTargetingToggleRequested;
	public event Action? DebugPanelToggleRequested;
	public event Action? MapZoomMinDecreaseRequested;
	public event Action? MapZoomMinIncreaseRequested;
	public event Action? MapZoomMaxDecreaseRequested;
	public event Action? MapZoomMaxIncreaseRequested;
	public event Action? SaveRequested;
	public event Action? LoadRequested;
	public event Action? MapEditorToggleRequested;
	public event Action? WeatherLabToggleRequested;
	public event Action? LayoutEditRequested;
	public event Action<string>? LanguageChangedRequested;

	internal static bool ShouldShowWatchMode(SettingsUiState state) =>
		state.Context == SettingsEntryContext.InGamePause;

	internal static string GetSubtitleKey(SettingsUiState state) =>
		state.Context == SettingsEntryContext.InGamePause
			? "ui.settings.subtitle.in_game"
			: "ui.settings.subtitle.main_menu";

	internal static string GetFooterHintKey(bool keyBindingsMode) =>
		keyBindingsMode
			? "ui.settings.hint.bindings"
			: "ui.settings.hint.default";

	internal static string GetRenderStatusKey(SettingsUiState state) =>
		state.RenderReady
			? "ui.settings.render.status.ready"
			: "ui.settings.render.status.unavailable";

	internal static string GetWatchModeStatusKey(SettingsUiState state) =>
		state.WatchModeEnabled
			? "ui.settings.watch_mode.status.on"
			: "ui.settings.watch_mode.status.off";

	internal static string GetFastTurnModeStatusKey(SettingsUiState state) =>
		state.FastTurnModeEnabled
			? "ui.settings.fast_turn_mode.status.on"
			: "ui.settings.fast_turn_mode.status.off";

	internal static string GetKeyboardTargetingStatusKey(SettingsUiState state) =>
		state.EnableKeyboardTargeting
			? "ui.settings.keyboard_targeting.status.on"
			: "ui.settings.keyboard_targeting.status.off";

	internal static string GetDebugPanelStatusKey(SettingsUiState state) =>
		state.EnableDebugPanel
			? "ui.settings.debug_panel.status.on"
			: "ui.settings.debug_panel.status.off";

	internal static string GetBindingsStatusKey(bool keyBindingsMode, bool isCapturing) =>
		isCapturing
			? "ui.settings.key_bindings.status.capturing"
			: keyBindingsMode
				? "ui.settings.key_bindings.status.active"
				: "ui.settings.key_bindings.status.idle";

	internal static string GetBindingsButtonKey(bool keyBindingsMode) =>
		keyBindingsMode
			? "ui.settings.key_bindings.return"
			: "ui.settings.key_bindings.open";

	internal static string GetMapEditorStatusKey(SettingsUiState state) =>
		state.MapEditorActive
			? "ui.settings.map_editor.status.active"
			: "ui.settings.map_editor.status.inactive";

	internal static string GetWeatherLabStatusKey(SettingsUiState state) =>
		!state.CanOpenWeatherLab
			? "ui.settings.weather_lab.status.unavailable"
			: state.WeatherLabPanelOpen
				? "ui.settings.weather_lab.status.active"
				: "ui.settings.weather_lab.status.inactive";

	internal static bool CanActivateRow(SettingsPanelRowId rowId, SettingsUiState state) =>
		rowId switch
		{
			SettingsPanelRowId.Render or SettingsPanelRowId.MapZoomMin or SettingsPanelRowId.MapZoomMax => state.RenderReady,
			SettingsPanelRowId.WeatherLab => state.CanOpenWeatherLab,
			_ => true,
		};

	private static string GetToggleStateKey(bool enabled) =>
		enabled
			? "ui.settings.state.on"
			: "ui.settings.state.off";

	public SettingsPanelModule(PanelContainer panel, InputBindingService bindings)
	{
		_panel = panel;
		_titleLabel = panel.GetNode<Label>("Margin/VBox/Header/Title");
		_subtitleLabel = panel.GetNode<Label>("Margin/VBox/Header/Subtitle");
		_footerHintLabel = panel.GetNode<Label>("Margin/VBox/Footer/HintLabel");
		_contentScroll = panel.GetNode<ScrollContainer>(ContentScrollPath);
		var tabBar = panel.GetNode<HBoxContainer>("Margin/VBox/TabBar");
		_generalTabButton = tabBar.GetNode<Button>("GeneralTab");
		_controlsTabButton = tabBar.GetNode<Button>("ControlsTab");
		_sessionTabButton = tabBar.GetNode<Button>("SessionTab");

		_generalPage = panel.GetNode<Control>(GeneralPagePath);
		_controlsPage = panel.GetNode<Control>(ControlsPagePath);
		_sessionPage = panel.GetNode<Control>(SessionPagePath);

		_displaySectionTitle = panel.GetNode<Label>(GeneralPagePath + "/DisplaySection/Margin/VBox/SectionTitle");
		_gameplaySectionTitle = panel.GetNode<Label>(GeneralPagePath + "/GameplaySection/Margin/VBox/SectionTitle");
		_controlsSectionTitle = panel.GetNode<Label>(ControlsPagePath + "/ControlOptionsSection/Margin/VBox/SectionTitle");
		_bindingsSectionTitle = panel.GetNode<Label>(ControlsPagePath + "/BindingsSection/Margin/VBox/SectionTitle");
		_saveLoadSectionTitle = panel.GetNode<Label>(SessionPagePath + "/SaveLoadSection/Margin/VBox/SectionTitle");
		_toolsSectionTitle = panel.GetNode<Label>(SessionPagePath + "/ToolsSection/Margin/VBox/SectionTitle");

		_languageTitleLabel = panel.GetNode<Label>(LanguageRowPath + "/Margin/HBox/Content/Title");
		_languageStatusLabel = panel.GetNode<Label>(LanguageRowPath + "/Margin/HBox/Content/Status");
		_renderTitleLabel = panel.GetNode<Label>(RenderRowPath + "/Margin/HBox/Content/Title");
		_renderStatusLabel = panel.GetNode<Label>(RenderRowPath + "/Margin/HBox/Content/Status");
		_mapZoomMinTitleLabel = panel.GetNode<Label>(MapZoomMinRowPath + "/Margin/HBox/Content/Title");
		_mapZoomMinStatusLabel = panel.GetNode<Label>(MapZoomMinRowPath + "/Margin/HBox/Content/Status");
		_mapZoomMaxTitleLabel = panel.GetNode<Label>(MapZoomMaxRowPath + "/Margin/HBox/Content/Title");
		_mapZoomMaxStatusLabel = panel.GetNode<Label>(MapZoomMaxRowPath + "/Margin/HBox/Content/Status");
		_watchModeTitleLabel = panel.GetNode<Label>(WatchModeRowPath + "/Margin/HBox/Content/Title");
		_watchModeStatusLabel = panel.GetNode<Label>(WatchModeRowPath + "/Margin/HBox/Content/Status");
		_fastTurnModeTitleLabel = panel.GetNode<Label>(FastTurnModeRowPath + "/Margin/HBox/Content/Title");
		_fastTurnModeStatusLabel = panel.GetNode<Label>(FastTurnModeRowPath + "/Margin/HBox/Content/Status");
		_keyboardTargetingTitleLabel = panel.GetNode<Label>(KeyboardTargetingRowPath + "/Margin/HBox/Content/Title");
		_keyboardTargetingStatusLabel = panel.GetNode<Label>(KeyboardTargetingRowPath + "/Margin/HBox/Content/Status");
		_debugPanelTitleLabel = panel.GetNode<Label>(DebugPanelRowPath + "/Margin/HBox/Content/Title");
		_debugPanelStatusLabel = panel.GetNode<Label>(DebugPanelRowPath + "/Margin/HBox/Content/Status");
		_bindingsTitleLabel = panel.GetNode<Label>(BindingsRowPath + "/Margin/HBox/Content/Title");
		_bindingsStatusLabel = panel.GetNode<Label>(BindingsRowPath + "/Margin/HBox/Content/Status");
		_saveTitleLabel = panel.GetNode<Label>(SaveRowPath + "/Margin/HBox/Content/Title");
		_saveStatusLabel = panel.GetNode<Label>(SaveRowPath + "/Margin/HBox/Content/Status");
		_loadTitleLabel = panel.GetNode<Label>(LoadRowPath + "/Margin/HBox/Content/Title");
		_loadStatusLabel = panel.GetNode<Label>(LoadRowPath + "/Margin/HBox/Content/Status");
		_mapEditorTitleLabel = panel.GetNode<Label>(MapEditorRowPath + "/Margin/HBox/Content/Title");
		_mapEditorStatusLabel = panel.GetNode<Label>(MapEditorRowPath + "/Margin/HBox/Content/Status");
		_weatherLabTitleLabel = panel.GetNode<Label>(WeatherLabRowPath + "/Margin/HBox/Content/Title");
		_weatherLabStatusLabel = panel.GetNode<Label>(WeatherLabRowPath + "/Margin/HBox/Content/Status");
		_layoutEditTitleLabel = panel.GetNode<Label>(LayoutEditRowPath + "/Margin/HBox/Content/Title");
		_layoutEditStatusLabel = panel.GetNode<Label>(LayoutEditRowPath + "/Margin/HBox/Content/Status");

		_languageOption = panel.GetNode<OptionButton>(LanguageRowPath + "/Margin/HBox/LanguageOption");
		_renderToggleButton = panel.GetNode<Button>(RenderRowPath + "/Margin/HBox/RenderToggle");
		_mapZoomMinDecreaseButton = panel.GetNode<Button>(MapZoomMinRowPath + "/Margin/HBox/ZoomMinDecreaseBtn");
		_mapZoomMinIncreaseButton = panel.GetNode<Button>(MapZoomMinRowPath + "/Margin/HBox/ZoomMinIncreaseBtn");
		_mapZoomMaxDecreaseButton = panel.GetNode<Button>(MapZoomMaxRowPath + "/Margin/HBox/ZoomMaxDecreaseBtn");
		_mapZoomMaxIncreaseButton = panel.GetNode<Button>(MapZoomMaxRowPath + "/Margin/HBox/ZoomMaxIncreaseBtn");
		_watchModeButton = panel.GetNode<CheckButton>(WatchModeRowPath + "/Margin/HBox/WatchModeToggle");
		_fastTurnModeButton = panel.GetNode<CheckButton>(FastTurnModeRowPath + "/Margin/HBox/FastTurnModeToggle");
		_keyboardTargetingButton = panel.GetNode<CheckButton>(KeyboardTargetingRowPath + "/Margin/HBox/KeyboardTargetingToggle");
		_debugPanelToggleButton = panel.GetNode<CheckButton>(DebugPanelRowPath + "/Margin/HBox/DebugPanelToggle");
		_bindingsButton = panel.GetNode<Button>(BindingsRowPath + "/Margin/HBox/BindingsBtn");
		_saveButton = panel.GetNode<Button>(SaveRowPath + "/Margin/HBox/SaveBtn");
		_loadButton = panel.GetNode<Button>(LoadRowPath + "/Margin/HBox/LoadBtn");
		_mapEditorButton = panel.GetNode<Button>(MapEditorRowPath + "/Margin/HBox/MapEditorBtn");
		_weatherLabButton = panel.GetNode<Button>(WeatherLabRowPath + "/Margin/HBox/WeatherLabBtn");
		_layoutEditButton = panel.GetNode<Button>(LayoutEditRowPath + "/Margin/HBox/LayoutEditBtn");
		_backButton = panel.GetNode<Button>("Margin/VBox/Footer/BackBtn");
		_keyBindingsRoot = panel.GetNode<Control>(KeyBindingsRootPath);
		_keyBindingsView = new KeyBindingsView(_keyBindingsRoot, bindings);

		_rowPanels = new Dictionary<SettingsPanelRowId, PanelContainer>
		{
			[SettingsPanelRowId.Language] = panel.GetNode<PanelContainer>(LanguageRowPath),
			[SettingsPanelRowId.Render] = panel.GetNode<PanelContainer>(RenderRowPath),
			[SettingsPanelRowId.MapZoomMin] = panel.GetNode<PanelContainer>(MapZoomMinRowPath),
			[SettingsPanelRowId.MapZoomMax] = panel.GetNode<PanelContainer>(MapZoomMaxRowPath),
			[SettingsPanelRowId.WatchMode] = panel.GetNode<PanelContainer>(WatchModeRowPath),
			[SettingsPanelRowId.FastTurnMode] = panel.GetNode<PanelContainer>(FastTurnModeRowPath),
			[SettingsPanelRowId.KeyboardTargeting] = panel.GetNode<PanelContainer>(KeyboardTargetingRowPath),
			[SettingsPanelRowId.DebugPanel] = panel.GetNode<PanelContainer>(DebugPanelRowPath),
			[SettingsPanelRowId.KeyBindings] = panel.GetNode<PanelContainer>(BindingsRowPath),
			[SettingsPanelRowId.Save] = panel.GetNode<PanelContainer>(SaveRowPath),
			[SettingsPanelRowId.Load] = panel.GetNode<PanelContainer>(LoadRowPath),
			[SettingsPanelRowId.MapEditor] = panel.GetNode<PanelContainer>(MapEditorRowPath),
			[SettingsPanelRowId.WeatherLab] = panel.GetNode<PanelContainer>(WeatherLabRowPath),
			[SettingsPanelRowId.LayoutEdit] = panel.GetNode<PanelContainer>(LayoutEditRowPath),
		};

		_tabButtons = new Dictionary<SettingsTab, Button>
		{
			[SettingsTab.General] = _generalTabButton,
			[SettingsTab.Controls] = _controlsTabButton,
			[SettingsTab.Session] = _sessionTabButton,
		};

		_rowNormalStyle = CreateRowStyle(
			new Color(0.1f, 0.1f, 0.16f, 0.85f),
			UIColors.IdleBorder,
			leftBorderWidth: 1);
		_rowSelectedStyle = CreateRowStyle(
			new Color(0.18f, 0.15f, 0.08f, 0.95f),
			UIColors.FocusBorder,
			leftBorderWidth: 4);

		_generalTabButton.Pressed += () => SetCurrentTab(SettingsTab.General);
		_controlsTabButton.Pressed += () => SetCurrentTab(SettingsTab.Controls);
		_sessionTabButton.Pressed += () => SetCurrentTab(SettingsTab.Session);
		_renderToggleButton.Pressed += () =>
		{
			SelectRow(SettingsPanelRowId.Render);
			if (!CanActivateRow(SettingsPanelRowId.Render, _state))
				return;

			RenderToggleRequested?.Invoke();
		};
		_mapZoomMinDecreaseButton.Pressed += () =>
		{
			SelectRow(SettingsPanelRowId.MapZoomMin);
			if (!CanActivateRow(SettingsPanelRowId.MapZoomMin, _state))
				return;

			MapZoomMinDecreaseRequested?.Invoke();
		};
		_mapZoomMinIncreaseButton.Pressed += () =>
		{
			SelectRow(SettingsPanelRowId.MapZoomMin);
			if (!CanActivateRow(SettingsPanelRowId.MapZoomMin, _state))
				return;

			MapZoomMinIncreaseRequested?.Invoke();
		};
		_mapZoomMaxDecreaseButton.Pressed += () =>
		{
			SelectRow(SettingsPanelRowId.MapZoomMax);
			if (!CanActivateRow(SettingsPanelRowId.MapZoomMax, _state))
				return;

			MapZoomMaxDecreaseRequested?.Invoke();
		};
		_mapZoomMaxIncreaseButton.Pressed += () =>
		{
			SelectRow(SettingsPanelRowId.MapZoomMax);
			if (!CanActivateRow(SettingsPanelRowId.MapZoomMax, _state))
				return;

			MapZoomMaxIncreaseRequested?.Invoke();
		};
		_watchModeButton.Pressed += () => SelectRow(SettingsPanelRowId.WatchMode);
		_watchModeButton.Toggled += _ =>
		{
			if (_suppressToggleSignals)
				return;

			SelectRow(SettingsPanelRowId.WatchMode);
			WatchModeToggleRequested?.Invoke();
		};
		_fastTurnModeButton.Pressed += () => SelectRow(SettingsPanelRowId.FastTurnMode);
		_fastTurnModeButton.Toggled += _ =>
		{
			if (_suppressToggleSignals)
				return;

			SelectRow(SettingsPanelRowId.FastTurnMode);
			FastTurnModeToggleRequested?.Invoke();
		};
		_keyboardTargetingButton.Pressed += () => SelectRow(SettingsPanelRowId.KeyboardTargeting);
		_keyboardTargetingButton.Toggled += _ =>
		{
			if (_suppressToggleSignals)
				return;

			SelectRow(SettingsPanelRowId.KeyboardTargeting);
			KeyboardTargetingToggleRequested?.Invoke();
		};
		_debugPanelToggleButton.Pressed += () => SelectRow(SettingsPanelRowId.DebugPanel);
		_debugPanelToggleButton.Toggled += _ =>
		{
			if (_suppressToggleSignals)
				return;

			SelectRow(SettingsPanelRowId.DebugPanel);
			DebugPanelToggleRequested?.Invoke();
		};
		_bindingsButton.Pressed += ToggleKeyBindingsModeFromButton;
		_saveButton.Pressed += () =>
		{
			SelectRow(SettingsPanelRowId.Save);
			SaveRequested?.Invoke();
		};
		_loadButton.Pressed += () =>
		{
			SelectRow(SettingsPanelRowId.Load);
			LoadRequested?.Invoke();
		};
		_mapEditorButton.Pressed += () =>
		{
			SelectRow(SettingsPanelRowId.MapEditor);
			MapEditorToggleRequested?.Invoke();
		};
		_weatherLabButton.Pressed += () =>
		{
			SelectRow(SettingsPanelRowId.WeatherLab);
			if (!CanActivateRow(SettingsPanelRowId.WeatherLab, _state))
				return;

			WeatherLabToggleRequested?.Invoke();
		};
		_layoutEditButton.Pressed += () =>
		{
			SelectRow(SettingsPanelRowId.LayoutEdit);
			LayoutEditRequested?.Invoke();
		};
		_backButton.Pressed += () => BackRequested?.Invoke();
		_languageOption.Pressed += () => SelectRow(SettingsPanelRowId.Language);
		_languageOption.ItemSelected += HandleLanguageSelected;

		WireRowSelection(SettingsPanelRowId.Language);
		WireRowSelection(SettingsPanelRowId.Render);
		WireRowSelection(SettingsPanelRowId.MapZoomMin);
		WireRowSelection(SettingsPanelRowId.MapZoomMax);
		WireRowSelection(SettingsPanelRowId.WatchMode);
		WireRowSelection(SettingsPanelRowId.FastTurnMode);
		WireRowSelection(SettingsPanelRowId.KeyboardTargeting);
		WireRowSelection(SettingsPanelRowId.DebugPanel);
		WireRowSelection(SettingsPanelRowId.KeyBindings);
		WireRowSelection(SettingsPanelRowId.Save);
		WireRowSelection(SettingsPanelRowId.Load);
		WireRowSelection(SettingsPanelRowId.MapEditor);
		WireRowSelection(SettingsPanelRowId.WeatherLab);
		WireRowSelection(SettingsPanelRowId.LayoutEdit);

		_state = new SettingsUiState(
			SettingsEntryContext.MainMenu,
			LocalizationService.CurrentLocale,
			RenderReady: false,
			WatchModeEnabled: false,
			FastTurnModeEnabled: true,
			MapEditorActive: false,
			CanOpenSessionTab: false,
			EnableKeyboardTargeting: false,
			EnableDebugPanel: true,
			CanOpenWeatherLab: false,
			WeatherLabPanelOpen: false,
			MapZoomMin: 0.6f,
			MapZoomMax: 2.4f,
			MapZoomCurrent: 1.0f);

		_selectionModel.ApplyState(_state);
		_panel.Visible = false;
		RefreshTexts();
		ApplyState(_state);
	}

	public void Open(SettingsEntryContext context, SettingsTab initialTab)
	{
		_state = _state with { Context = context };
		ApplyState(_state);
		SetCurrentTab(initialTab);
		Visible = true;
	}

	public void Close() => Visible = false;

	public void ApplyState(SettingsUiState state)
	{
		_state = state;
		_selectionModel.ApplyState(state);

		_sessionTabButton.Visible = CanShowSessionTab();
		_rowPanels[SettingsPanelRowId.WatchMode].Visible = ShouldShowWatchMode(state);

		SetCurrentLocale(state.CurrentLocale);
		UpdateDynamicStateTexts();
		UpdateTabState();
		UpdatePageVisibility();
		RefreshSelectionUi();
	}

	public void RefreshTexts()
	{
		_titleLabel.Text = LocalizationService.T("ui.settings.title");
		_generalTabButton.Text = LocalizationService.T("ui.settings.tab.general");
		_controlsTabButton.Text = LocalizationService.T("ui.settings.tab.controls");
		_sessionTabButton.Text = LocalizationService.T("ui.settings.tab.session");
		_displaySectionTitle.Text = LocalizationService.T("ui.settings.section.display");
		_gameplaySectionTitle.Text = LocalizationService.T("ui.settings.section.gameplay");
		_controlsSectionTitle.Text = LocalizationService.T("ui.settings.section.controls");
		_bindingsSectionTitle.Text = LocalizationService.T("ui.settings.section.bindings");
		_saveLoadSectionTitle.Text = LocalizationService.T("ui.settings.section.save_load");
		_toolsSectionTitle.Text = LocalizationService.T("ui.settings.section.tools");
		_languageTitleLabel.Text = LocalizationService.T("ui.settings.language.title");
		_renderTitleLabel.Text = LocalizationService.T("ui.settings.render.title");
		_mapZoomMinTitleLabel.Text = LocalizationService.T("ui.settings.map_zoom_min.title");
		_mapZoomMaxTitleLabel.Text = LocalizationService.T("ui.settings.map_zoom_max.title");
		_watchModeTitleLabel.Text = LocalizationService.T("ui.settings.watch_mode.title");
		_fastTurnModeTitleLabel.Text = LocalizationService.T("ui.settings.fast_turn_mode.title");
		_keyboardTargetingTitleLabel.Text = LocalizationService.T("ui.settings.keyboard_targeting.title");
		_debugPanelTitleLabel.Text = LocalizationService.T("ui.settings.debug_panel.title");
		_bindingsTitleLabel.Text = LocalizationService.T("ui.settings.key_bindings.title");
		_saveTitleLabel.Text = LocalizationService.T("ui.settings.save.title");
		_loadTitleLabel.Text = LocalizationService.T("ui.settings.load.title");
		_mapEditorTitleLabel.Text = LocalizationService.T("ui.settings.map_editor.title");
		_weatherLabTitleLabel.Text = LocalizationService.T("ui.settings.weather_lab.title");
		_layoutEditTitleLabel.Text = LocalizationService.T("ui.settings.layout_edit.title");
		_renderToggleButton.Text = LocalizationService.T("ui.settings.render.action");
		_bindingsButton.Text = LocalizationService.T(GetBindingsButtonKey(_selectionModel.KeyBindingsMode));
		_saveButton.Text = LocalizationService.T("ui.settings.save");
		_loadButton.Text = LocalizationService.T("ui.settings.load");
		_layoutEditButton.Text = LocalizationService.T("ui.settings.layout_edit");
		_backButton.Text = LocalizationService.T("ui.settings.back");
		_keyBindingsView.RefreshTexts();
		SetCurrentLocale(_state.CurrentLocale);
		UpdateDynamicStateTexts();
		UpdateTabState();
		RefreshSelectionUi();
	}

	public bool HandleCommand(string cmd)
	{
		if (_selectionModel.KeyBindingsMode)
		{
			if (_keyBindingsView.HandleCommand(cmd))
			{
				UpdateDynamicStateTexts();
				RefreshSelectionUi();
				return true;
			}

			if (cmd == "close")
			{
				HandleKeyBindingsClose();
				return true;
			}
		}

		switch (cmd)
		{
			case "up":
				_selectionModel.MoveSelection(-1);
				RefreshSelectionUi();
				return true;
			case "down":
				_selectionModel.MoveSelection(1);
				RefreshSelectionUi();
				return true;
			case "left":
			case "tab_prev":
				_selectionModel.CycleTab(-1);
				UpdateAfterTabChange();
				return true;
			case "right":
			case "tab_next":
				_selectionModel.CycleTab(1);
				UpdateAfterTabChange();
				return true;
			case "confirm":
				ActivateSelectedRow();
				return true;
			case "close":
				BackRequested?.Invoke();
				return true;
			default:
				return false;
		}
	}

	public bool HandleKeyInput(InputEventKey key)
	{
		if (!_selectionModel.KeyBindingsMode)
			return false;

		var handled = _keyBindingsView.HandleKey(key);
		if (handled)
		{
			UpdateDynamicStateTexts();
			RefreshSelectionUi();
		}

		return handled;
	}

	public bool HandleMouseInput(InputEvent @event)
	{
		if (!_selectionModel.KeyBindingsMode)
			return false;

		var handled = _keyBindingsView.HandleMouseInput(@event);
		if (handled)
		{
			UpdateDynamicStateTexts();
			RefreshSelectionUi();
		}

		return handled;
	}

	private static StyleBoxFlat CreateRowStyle(Color background, Color border, int leftBorderWidth) =>
		new()
		{
			BgColor = background,
			BorderColor = border,
			BorderWidthLeft = leftBorderWidth,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4,
		};

	private void WireRowSelection(SettingsPanelRowId rowId)
	{
		_rowPanels[rowId].GuiInput += @event =>
		{
			if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
				SelectRow(rowId);
		};
	}

	private void SetCurrentTab(SettingsTab tab)
	{
		_selectionModel.SetTab(tab);
		UpdateAfterTabChange();
	}

	private void UpdateAfterTabChange()
	{
		if (_selectionModel.CurrentTab == SettingsTab.Controls)
			_keyBindingsView.Refresh();

		UpdateDynamicStateTexts();
		UpdateTabState();
		UpdatePageVisibility();
		RefreshSelectionUi();
	}

	private void UpdateTabState()
	{
		foreach (var (tab, button) in _tabButtons)
			button.ButtonPressed = tab == _selectionModel.CurrentTab;
	}

	private void UpdatePageVisibility()
	{
		_generalPage.Visible = _selectionModel.CurrentTab == SettingsTab.General;
		_controlsPage.Visible = _selectionModel.CurrentTab == SettingsTab.Controls;
		_sessionPage.Visible = _selectionModel.CurrentTab == SettingsTab.Session && CanShowSessionTab();
	}

	private bool CanShowSessionTab() =>
		_state.Context == SettingsEntryContext.InGamePause
		&& _state.CanOpenSessionTab;

	private void UpdateDynamicStateTexts()
	{
		_subtitleLabel.Text = LocalizationService.T(GetSubtitleKey(_state));
		_footerHintLabel.Text = LocalizationService.T(GetFooterHintKey(_selectionModel.KeyBindingsMode));
		_languageStatusLabel.Text = LocalizationService.T(
			"ui.settings.language.status",
			("locale", LocalizationService.GetLocaleLabel(_state.CurrentLocale)));
		_renderStatusLabel.Text = LocalizationService.T(GetRenderStatusKey(_state));
		_mapZoomMinStatusLabel.Text = LocalizationService.T(
			"ui.settings.map_zoom_min.status",
			("value", _state.MapZoomMin.ToString("0.0")),
			("current", _state.MapZoomCurrent.ToString("0.0")));
		_mapZoomMaxStatusLabel.Text = LocalizationService.T(
			"ui.settings.map_zoom_max.status",
			("value", _state.MapZoomMax.ToString("0.0")),
			("current", _state.MapZoomCurrent.ToString("0.0")));
		_watchModeStatusLabel.Text = LocalizationService.T(GetWatchModeStatusKey(_state));
		_fastTurnModeStatusLabel.Text = LocalizationService.T(GetFastTurnModeStatusKey(_state));
		_keyboardTargetingStatusLabel.Text = LocalizationService.T(GetKeyboardTargetingStatusKey(_state));
		_debugPanelStatusLabel.Text = LocalizationService.T(GetDebugPanelStatusKey(_state));
		_bindingsStatusLabel.Text = LocalizationService.T(GetBindingsStatusKey(_selectionModel.KeyBindingsMode, _keyBindingsView.IsCapturing));
		_saveStatusLabel.Text = LocalizationService.T("ui.settings.save.status");
		_loadStatusLabel.Text = LocalizationService.T("ui.settings.load.status");
		_mapEditorStatusLabel.Text = LocalizationService.T(GetMapEditorStatusKey(_state));
		_weatherLabStatusLabel.Text = LocalizationService.T(GetWeatherLabStatusKey(_state));
		_layoutEditStatusLabel.Text = LocalizationService.T("ui.settings.layout_edit.status");

		_renderToggleButton.Disabled = !CanActivateRow(SettingsPanelRowId.Render, _state);
		_mapZoomMinDecreaseButton.Disabled = !CanActivateRow(SettingsPanelRowId.MapZoomMin, _state);
		_mapZoomMinIncreaseButton.Disabled = !CanActivateRow(SettingsPanelRowId.MapZoomMin, _state);
		_mapZoomMaxDecreaseButton.Disabled = !CanActivateRow(SettingsPanelRowId.MapZoomMax, _state);
		_mapZoomMaxIncreaseButton.Disabled = !CanActivateRow(SettingsPanelRowId.MapZoomMax, _state);
		_mapZoomMinDecreaseButton.Text = LocalizationService.T("ui.settings.map_zoom.decrease");
		_mapZoomMinIncreaseButton.Text = LocalizationService.T("ui.settings.map_zoom.increase");
		_mapZoomMaxDecreaseButton.Text = LocalizationService.T("ui.settings.map_zoom.decrease");
		_mapZoomMaxIncreaseButton.Text = LocalizationService.T("ui.settings.map_zoom.increase");
		_mapEditorButton.Text = LocalizationService.T(
			_state.MapEditorActive
				? "ui.settings.map_editor.exit"
				: "ui.settings.map_editor.enter");
		_weatherLabButton.Text = LocalizationService.T(
			_state.WeatherLabPanelOpen
				? "ui.settings.weather_lab.close"
				: "ui.settings.weather_lab.open");
		_weatherLabButton.Disabled = !CanActivateRow(SettingsPanelRowId.WeatherLab, _state);
		_bindingsButton.Text = LocalizationService.T(GetBindingsButtonKey(_selectionModel.KeyBindingsMode));
		_keyBindingsRoot.Visible = _selectionModel.KeyBindingsMode;

		_suppressToggleSignals = true;
		_watchModeButton.ButtonPressed = _state.WatchModeEnabled;
		_watchModeButton.Text = LocalizationService.T(GetToggleStateKey(_state.WatchModeEnabled));
		_fastTurnModeButton.ButtonPressed = _state.FastTurnModeEnabled;
		_fastTurnModeButton.Text = LocalizationService.T(GetToggleStateKey(_state.FastTurnModeEnabled));
		_keyboardTargetingButton.ButtonPressed = _state.EnableKeyboardTargeting;
		_keyboardTargetingButton.Text = LocalizationService.T(GetToggleStateKey(_state.EnableKeyboardTargeting));
		_debugPanelToggleButton.ButtonPressed = _state.EnableDebugPanel;
		_debugPanelToggleButton.Text = LocalizationService.T(GetToggleStateKey(_state.EnableDebugPanel));
		_suppressToggleSignals = false;
	}

	private void RefreshSelectionUi()
	{
		var visibleRows = _selectionModel.GetVisibleRows(_selectionModel.CurrentTab);
		foreach (var (rowId, rowPanel) in _rowPanels)
		{
			var isSelected = rowPanel.Visible
				&& visibleRows.Contains(rowId)
				&& rowId == _selectionModel.SelectedRow;
			rowPanel.AddThemeStyleboxOverride("panel", isSelected ? _rowSelectedStyle : _rowNormalStyle);
		}

		EnsureSelectionVisible();
	}

	private void EnsureSelectionVisible()
	{
		if (!_rowPanels.TryGetValue(_selectionModel.SelectedRow, out var rowPanel) || !rowPanel.Visible)
			return;

		_contentScroll.EnsureControlVisible(rowPanel);
	}

	private void SelectRow(SettingsPanelRowId rowId)
	{
		if (_selectionModel.SetSelectedRow(rowId))
		{
			UpdateDynamicStateTexts();
			RefreshSelectionUi();
		}
	}

	private void ActivateSelectedRow()
	{
		var selectedRow = _selectionModel.SelectedRow;
		if (!CanActivateRow(selectedRow, _state))
			return;

		switch (selectedRow)
		{
			case SettingsPanelRowId.Language:
				CycleLanguage();
				break;
			case SettingsPanelRowId.Render:
				RenderToggleRequested?.Invoke();
				break;
			case SettingsPanelRowId.MapZoomMin:
				MapZoomMinIncreaseRequested?.Invoke();
				break;
			case SettingsPanelRowId.MapZoomMax:
				MapZoomMaxIncreaseRequested?.Invoke();
				break;
			case SettingsPanelRowId.WatchMode:
				WatchModeToggleRequested?.Invoke();
				break;
			case SettingsPanelRowId.FastTurnMode:
				FastTurnModeToggleRequested?.Invoke();
				break;
			case SettingsPanelRowId.KeyboardTargeting:
				KeyboardTargetingToggleRequested?.Invoke();
				break;
			case SettingsPanelRowId.DebugPanel:
				DebugPanelToggleRequested?.Invoke();
				break;
			case SettingsPanelRowId.KeyBindings:
				EnterKeyBindingsMode();
				break;
			case SettingsPanelRowId.Save:
				SaveRequested?.Invoke();
				break;
			case SettingsPanelRowId.Load:
				LoadRequested?.Invoke();
				break;
			case SettingsPanelRowId.MapEditor:
				MapEditorToggleRequested?.Invoke();
				break;
			case SettingsPanelRowId.WeatherLab:
				WeatherLabToggleRequested?.Invoke();
				break;
			case SettingsPanelRowId.LayoutEdit:
				LayoutEditRequested?.Invoke();
				break;
		}
	}

	private void ToggleKeyBindingsModeFromButton()
	{
		SelectRow(SettingsPanelRowId.KeyBindings);
		if (_selectionModel.KeyBindingsMode)
		{
			HandleKeyBindingsClose();
			return;
		}

		EnterKeyBindingsMode();
	}

	private void EnterKeyBindingsMode()
	{
		_selectionModel.EnterKeyBindingsMode();
		_keyBindingsView.Refresh();
		UpdateDynamicStateTexts();
		RefreshSelectionUi();
	}

	private void HandleKeyBindingsClose()
	{
		var action = _selectionModel.HandleClose(_keyBindingsView.IsCapturing);
		if (action == SettingsPanelCloseAction.None)
		{
			UpdateDynamicStateTexts();
			RefreshSelectionUi();
			return;
		}
		if (action == SettingsPanelCloseAction.ExitKeyBindingsMode)
		{
			UpdateDynamicStateTexts();
			RefreshSelectionUi();
			return;
		}

		BackRequested?.Invoke();
	}

	private void CycleLanguage()
	{
		var locales = LocalizationService.SupportedLocales;
		if (locales.Count == 0)
			return;

		var normalized = LocalizationService.NormalizeLocale(_state.CurrentLocale);
		var currentIndex = 0;
		for (var i = 0; i < locales.Count; i++)
		{
			if (locales[i] != normalized)
				continue;

			currentIndex = i;
			break;
		}

		var nextIndex = (currentIndex + 1) % locales.Count;
		LanguageChangedRequested?.Invoke(locales[nextIndex]);
	}

	private void SetCurrentLocale(string locale)
	{
		_suppressLanguageChange = true;
		_languageOption.Clear();
		foreach (var supportedLocale in LocalizationService.SupportedLocales)
			_languageOption.AddItem(LocalizationService.GetLocaleLabel(supportedLocale));

		var normalized = LocalizationService.NormalizeLocale(locale);
		for (var i = 0; i < LocalizationService.SupportedLocales.Count; i++)
		{
			if (LocalizationService.SupportedLocales[i] != normalized)
				continue;

			_languageOption.Select(i);
			break;
		}

		_suppressLanguageChange = false;
	}

	private void HandleLanguageSelected(long index)
	{
		SelectRow(SettingsPanelRowId.Language);
		if (_suppressLanguageChange)
			return;
		if (index < 0 || index >= LocalizationService.SupportedLocales.Count)
			return;

		LanguageChangedRequested?.Invoke(LocalizationService.SupportedLocales[(int)index]);
	}
}
