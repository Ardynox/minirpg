using System.Collections.Generic;

namespace MiniRPG.Module.Panel;

internal enum SettingsPanelRowId
{
	Language,
	Render,
	WatchMode,
	KeyboardTargeting,
	DebugPanel,
	KeyBindings,
	Save,
	Load,
	MapEditor,
	WeatherLab,
	LayoutEdit,
}

internal enum SettingsPanelCloseAction
{
	None,
	ExitKeyBindingsMode,
	ClosePanel,
}

internal sealed class SettingsPanelSelectionModel
{
	private readonly Dictionary<SettingsTab, SettingsPanelRowId> _selectedRows = new()
	{
		[SettingsTab.General] = SettingsPanelRowId.Language,
		[SettingsTab.Controls] = SettingsPanelRowId.KeyboardTargeting,
		[SettingsTab.Session] = SettingsPanelRowId.Save,
	};

	private SettingsUiState _state = new(
		SettingsEntryContext.MainMenu,
		LocalizationService.CurrentLocale,
		RenderReady: false,
		WatchModeEnabled: false,
		MapEditorActive: false,
		CanOpenSessionTab: false,
		EnableKeyboardTargeting: false,
		EnableDebugPanel: true,
		CanOpenWeatherLab: false,
		WeatherLabPanelOpen: false);

	public SettingsTab CurrentTab { get; private set; } = SettingsTab.General;
	public bool KeyBindingsMode { get; private set; }
	public SettingsPanelRowId SelectedRow => _selectedRows[CurrentTab];

	public void ApplyState(SettingsUiState state)
	{
		_state = state;
		ClampTabAndSelection();
	}

	public void SetTab(SettingsTab tab)
	{
		if (tab == SettingsTab.Session && !CanShowSessionTab())
			tab = SettingsTab.General;

		CurrentTab = tab;
		if (CurrentTab != SettingsTab.Controls)
			KeyBindingsMode = false;

		ClampSelection(CurrentTab);
	}

	public void CycleTab(int delta)
	{
		var tabs = GetVisibleTabs();
		if (tabs.Count == 0)
			return;

		var index = tabs.IndexOf(CurrentTab);
		if (index < 0)
			index = 0;

		index = (index + delta + tabs.Count) % tabs.Count;
		SetTab(tabs[index]);
	}

	public void MoveSelection(int delta)
	{
		var rows = GetVisibleRows(CurrentTab);
		if (rows.Count == 0)
			return;

		var current = _selectedRows[CurrentTab];
		var index = rows.IndexOf(current);
		if (index < 0)
			index = 0;

		index += delta;
		if (index < 0)
			index = 0;
		if (index >= rows.Count)
			index = rows.Count - 1;

		SetSelectedRow(rows[index]);
	}

	public bool SetSelectedRow(SettingsPanelRowId row)
	{
		var rows = GetVisibleRows(CurrentTab);
		if (!rows.Contains(row))
			return false;

		_selectedRows[CurrentTab] = row;
		if (CurrentTab != SettingsTab.Controls || row != SettingsPanelRowId.KeyBindings)
			KeyBindingsMode = false;
		return true;
	}

	public void EnterKeyBindingsMode()
	{
		if (CurrentTab != SettingsTab.Controls)
			return;

		_selectedRows[SettingsTab.Controls] = SettingsPanelRowId.KeyBindings;
		KeyBindingsMode = true;
	}

	public void ExitKeyBindingsMode() => KeyBindingsMode = false;

	public SettingsPanelCloseAction HandleClose(bool keyBindingsCapturing)
	{
		if (keyBindingsCapturing)
			return SettingsPanelCloseAction.None;

		if (KeyBindingsMode)
		{
			KeyBindingsMode = false;
			return SettingsPanelCloseAction.ExitKeyBindingsMode;
		}

		return SettingsPanelCloseAction.ClosePanel;
	}

	public List<SettingsTab> GetVisibleTabs()
	{
		var tabs = new List<SettingsTab> { SettingsTab.General, SettingsTab.Controls };
		if (CanShowSessionTab())
			tabs.Add(SettingsTab.Session);
		return tabs;
	}

	public List<SettingsPanelRowId> GetVisibleRows(SettingsTab tab)
	{
		return tab switch
		{
			SettingsTab.General => GetGeneralRows(),
			SettingsTab.Controls => GetControlsRows(),
			SettingsTab.Session when CanShowSessionTab() => GetSessionRows(),
			_ => [],
		};
	}

	private void ClampTabAndSelection()
	{
		if (CurrentTab == SettingsTab.Session && !CanShowSessionTab())
			CurrentTab = SettingsTab.General;

		ClampSelection(SettingsTab.General);
		ClampSelection(SettingsTab.Controls);
		ClampSelection(SettingsTab.Session);

		if (CurrentTab != SettingsTab.Controls || _selectedRows[SettingsTab.Controls] != SettingsPanelRowId.KeyBindings)
			KeyBindingsMode = false;
	}

	private void ClampSelection(SettingsTab tab)
	{
		var rows = GetVisibleRows(tab);
		if (rows.Count == 0)
			return;

		if (!_selectedRows.TryGetValue(tab, out var selected) || !rows.Contains(selected))
			_selectedRows[tab] = rows[0];
	}

	private bool CanShowSessionTab() =>
		_state.Context == SettingsEntryContext.InGamePause
		&& _state.CanOpenSessionTab;

	private List<SettingsPanelRowId> GetGeneralRows()
	{
		var rows = new List<SettingsPanelRowId>
		{
			SettingsPanelRowId.Language,
			SettingsPanelRowId.Render,
		};

		if (SettingsPanelModule.ShouldShowWatchMode(_state))
			rows.Add(SettingsPanelRowId.WatchMode);

		return rows;
	}

	private static List<SettingsPanelRowId> GetControlsRows() =>
		[
			SettingsPanelRowId.KeyboardTargeting,
			SettingsPanelRowId.DebugPanel,
			SettingsPanelRowId.KeyBindings,
		];

	private static List<SettingsPanelRowId> GetSessionRows() =>
		[
			SettingsPanelRowId.Save,
			SettingsPanelRowId.Load,
			SettingsPanelRowId.MapEditor,
			SettingsPanelRowId.WeatherLab,
			SettingsPanelRowId.LayoutEdit,
		];
}
