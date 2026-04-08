using System;
using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Core.Debug;
using MiniRPG.Core.Weather;
using MiniRPG.Module;
using MiniRPG.Module.Panel;
using MiniRPG.Module.Render;

namespace MiniRPG;

internal sealed class WeatherLabPanelController : WeatherLabPanelModule.IHost
{
	private readonly GameState _state;
	private readonly GameSessionModule _session;
	private readonly LogModule _log;
	private readonly WeatherLabPanelModule _panel;
	private readonly string _presetScenarioId;
	private readonly Func<bool> _menuInMenu;
	private readonly Func<bool> _mapEditorActive;
	private readonly Action _markUiDirty;
	private readonly Action _markDebugPanelDirty;
	private readonly Action _flushMap;
	private readonly Action _syncSettingsUiState;
	private readonly Action<WeatherScreenFxTuningSet> _applyWeatherScreenFxTuning;

	private WeatherScreenFxTuningSet _tuningSet = new();

	public WeatherLabPanelController(
		GameState state,
		GameSessionModule session,
		LogModule log,
		PanelContainer panelNode,
		string presetScenarioId,
		Func<bool> menuInMenu,
		Func<bool> mapEditorActive,
		Action markUiDirty,
		Action markDebugPanelDirty,
		Action flushMap,
		Action syncSettingsUiState,
		Action<WeatherScreenFxTuningSet> applyWeatherScreenFxTuning)
	{
		_state = state;
		_session = session;
		_log = log;
		_presetScenarioId = presetScenarioId;
		_menuInMenu = menuInMenu;
		_mapEditorActive = mapEditorActive;
		_markUiDirty = markUiDirty;
		_markDebugPanelDirty = markDebugPanelDirty;
		_flushMap = flushMap;
		_syncSettingsUiState = syncSettingsUiState;
		_applyWeatherScreenFxTuning = applyWeatherScreenFxTuning;
		_panel = new WeatherLabPanelModule(panelNode, this);
	}

	public WeatherScreenFxTuningSet CurrentTuningSet => _tuningSet;

	public bool Visible => _panel.Visible;

	public bool IsWeatherLabSession =>
		string.Equals(_session.CurrentPresetScenarioId, _presetScenarioId, StringComparison.Ordinal);

	public bool CanUse =>
		_session.GameStarted
		&& IsWeatherLabSession
		&& !_menuInMenu()
		&& !_mapEditorActive();

	public void Toggle()
	{
		if (!CanUse)
			return;

		if (Visible)
		{
			Close(resetRuntime: false);
			return;
		}

		Open();
	}

	public void Open()
	{
		if (!CanUse)
			return;

		ApplyCurrentTuning();
		_panel.Open();
		_syncSettingsUiState();
	}

	public void Close(bool resetRuntime)
	{
		_panel.Close();
		if (resetRuntime)
			ResetRuntimeState();

		_syncSettingsUiState();
	}

	public void RefreshSessionState(bool autoOpen)
	{
		if (!CanUse)
		{
			Close(resetRuntime: !IsWeatherLabSession);
			return;
		}

		ApplyCurrentTuning();
		if (autoOpen)
			_panel.Open();
		else
			MarkDirty();

		_syncSettingsUiState();
	}

	public void RefreshTexts() => _panel.RefreshTexts();

	public void MarkDirty() => _panel.Dirty = true;

	public void FlushIfDirty()
	{
		if (Visible && _panel.Dirty)
			_panel.FlushIfDirty();
	}

	GameState WeatherLabPanelModule.IHost.State => _state;
	string? WeatherLabPanelModule.IHost.CurrentPresetScenarioId => _session.CurrentPresetScenarioId;
	WeatherScreenFxTuningSet WeatherLabPanelModule.IHost.WeatherLabTuningSet => _tuningSet;
	DebugModule.Result WeatherLabPanelModule.IHost.ExecuteLockWeather(WeatherType type, WeatherIntensity intensity) =>
		ApplyResult(DebugModule.LockWeather(_state, type, intensity));
	DebugModule.Result WeatherLabPanelModule.IHost.ExecuteUnlockWeather() =>
		ApplyResult(DebugModule.UnlockWeather(_state));
	DebugModule.Result WeatherLabPanelModule.IHost.ExecuteStepWeather(int turns) =>
		ApplyResult(DebugModule.StepWeather(_state, turns));
	DebugModule.Result WeatherLabPanelModule.IHost.ExecuteClearWeatherAccumulation() =>
		ApplyResult(DebugModule.ClearWeatherAccumulation(_state));
	void WeatherLabPanelModule.IHost.NotifyWeatherLabTuningChanged() => HandleTuningChanged();

	private void HandleTuningChanged()
	{
		ApplyCurrentTuning();
		MarkDirty();
		if (_session.GameStarted && !_menuInMenu())
			_flushMap();
	}

	private DebugModule.Result ApplyResult(DebugModule.Result result)
	{
		foreach (var message in result.Logs)
			_log.Add(message);

		if (result.NeedsUiRefresh)
		{
			_markUiDirty();
			_markDebugPanelDirty();
		}

		MarkDirty();
		if (result.NeedsFlush)
			_flushMap();

		return result;
	}

	private void ResetRuntimeState()
	{
		_tuningSet = new WeatherScreenFxTuningSet();
		ApplyCurrentTuning();
		MarkDirty();
	}

	private void ApplyCurrentTuning() => _applyWeatherScreenFxTuning(_tuningSet);
}
