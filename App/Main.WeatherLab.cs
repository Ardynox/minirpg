using Godot;

namespace MiniRPG;

public partial class Main
{
	private const string WeatherLabPresetScenarioId = "weather_lab";

	private void InitializeWeatherLabPanel(Theme uiTheme)
	{
		var panelNode = GetNode<PanelContainer>($"{OverlayRootPath}/WeatherLabPanel");
		panelNode.Theme = uiTheme;
		_weatherLabPanelController = new WeatherLabPanelController(
			_state,
			_session,
			_log,
			panelNode,
			WeatherLabPresetScenarioId,
			() => _menu.InMenu,
			() => MapEditorActive,
			MarkUIDirty,
			() => _debugPanelController?.MarkDirty(),
			FlushMap,
			() => SyncSettingsUiState(),
			tuning => _mapRender?.SetWeatherScreenFxTuning(tuning));
	}

	private void HandleMenuWeatherLab()
	{
		if (!ResourcesReady || _busyOperationActive)
			return;

		_mainAppFlowCoordinator.LoadFromMainMenu(
			LocalizationService.T("ui.main_menu.weather_lab"),
			() => _session.PrepareLoadPresetScenario(WeatherLabPresetScenarioId));
	}
}
