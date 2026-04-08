using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Debug;
using MiniRPG.Core.Weather;
using MiniRPG.Module.Render;

namespace MiniRPG.Module.Panel;

internal sealed class WeatherLabPanelModule
{
	internal interface IHost
	{
		GameState State { get; }
		DebugModule.Result ExecuteLockWeather(WeatherType type, WeatherIntensity intensity);
		DebugModule.Result ExecuteUnlockWeather();
		DebugModule.Result ExecuteStepWeather(int turns);
		DebugModule.Result ExecuteClearWeatherAccumulation();
		string? CurrentPresetScenarioId { get; }
		WeatherScreenFxTuningSet WeatherLabTuningSet { get; }
		void NotifyWeatherLabTuningChanged();
	}

	private static readonly string[] WeatherModeIds = ["natural", "locked"];
	private static readonly string[] WeatherTypeIds = ["clear", "rain", "snow", "fog", "sandstorm", "thunderstorm", "storm"];
	private static readonly string[] WeatherIntensityIds = ["light", "normal", "heavy"];

	private static readonly TuningDefinition[] TuningDefinitions =
	[
		new("overlay_alpha_scale", "ui.weather_lab.tuning.overlay_alpha_scale"),
		new("fog_alpha_scale", "ui.weather_lab.tuning.fog_alpha_scale"),
		new("edge_tint_scale", "ui.weather_lab.tuning.edge_tint_scale"),
		new("edge_shadow_scale", "ui.weather_lab.tuning.edge_shadow_scale"),
		new("density_scale", "ui.weather_lab.tuning.density_scale"),
		new("speed_scale", "ui.weather_lab.tuning.speed_scale"),
		new("particle_size_scale", "ui.weather_lab.tuning.particle_size_scale"),
		new("particle_frequency_scale", "ui.weather_lab.tuning.particle_frequency_scale"),
		new("particle_blend_scale", "ui.weather_lab.tuning.particle_blend_scale"),
		new("lightning_flash_scale", "ui.weather_lab.tuning.lightning_flash_scale"),
		new("tint_strength_scale", "ui.weather_lab.tuning.tint_strength_scale"),
	];

	private readonly PanelContainer _panel;
	private readonly IHost _host;
	private readonly Label _titleLabel;
	private readonly Label _previewTitle;
	private readonly Label _previewText;
	private readonly Label _weatherTitle;
	private readonly Label _weatherModeLabel;
	private readonly OptionButton _weatherModeOption;
	private readonly Label _weatherTypeLabel;
	private readonly OptionButton _weatherTypeOption;
	private readonly Label _weatherIntensityLabel;
	private readonly OptionButton _weatherIntensityOption;
	private readonly Button _step1Button;
	private readonly Button _step10Button;
	private readonly Button _step50Button;
	private readonly Button _clearAccumButton;
	private readonly Label _visualTitle;
	private readonly Label _tuningModeLabel;
	private readonly OptionButton _tuningModeOption;
	private readonly VBoxContainer _tuningRowsRoot;
	private readonly Label _actionsTitle;
	private readonly Button _resetModeButton;
	private readonly Button _resetAllButton;
	private readonly Button _copyJsonButton;
	private readonly Label _actionStatus;
	private readonly List<TuningControlRow> _tuningRows = [];

	private WeatherScreenFxMode _currentTuningMode = WeatherScreenFxMode.Rain;
	private bool _suppressWeatherSelection;
	private bool _suppressTuningModeSelection;

	public WeatherLabPanelModule(PanelContainer panel, IHost host)
	{
		_panel = panel;
		_host = host;

		var root = panel.GetNode<VBoxContainer>("Margin/VBox");
		_titleLabel = root.GetNode<Label>("Title");
		var scrollContent = root.GetNode<VBoxContainer>("BodyScroll/Content");
		_previewTitle = scrollContent.GetNode<Label>("PreviewSection/PreviewTitle");
		_previewText = scrollContent.GetNode<Label>("PreviewSection/PreviewText");
		_weatherTitle = scrollContent.GetNode<Label>("WeatherSection/WeatherTitle");
		_weatherModeLabel = scrollContent.GetNode<Label>("WeatherSection/WeatherGrid/WeatherModeLabel");
		_weatherModeOption = scrollContent.GetNode<OptionButton>("WeatherSection/WeatherGrid/WeatherModeOption");
		_weatherTypeLabel = scrollContent.GetNode<Label>("WeatherSection/WeatherGrid/WeatherTypeLabel");
		_weatherTypeOption = scrollContent.GetNode<OptionButton>("WeatherSection/WeatherGrid/WeatherTypeOption");
		_weatherIntensityLabel = scrollContent.GetNode<Label>("WeatherSection/WeatherGrid/WeatherIntensityLabel");
		_weatherIntensityOption = scrollContent.GetNode<OptionButton>("WeatherSection/WeatherGrid/WeatherIntensityOption");
		var stepRow = scrollContent.GetNode<HBoxContainer>("WeatherSection/StepRow");
		_step1Button = stepRow.GetNode<Button>("Step1Btn");
		_step10Button = stepRow.GetNode<Button>("Step10Btn");
		_step50Button = stepRow.GetNode<Button>("Step50Btn");
		_clearAccumButton = scrollContent.GetNode<Button>("WeatherSection/ClearAccumBtn");
		_visualTitle = scrollContent.GetNode<Label>("VisualSection/VisualTitle");
		_tuningModeLabel = scrollContent.GetNode<Label>("VisualSection/TuningModeRow/TuningModeLabel");
		_tuningModeOption = scrollContent.GetNode<OptionButton>("VisualSection/TuningModeRow/TuningModeOption");
		_tuningRowsRoot = scrollContent.GetNode<VBoxContainer>("VisualSection/TuningRows");
		_actionsTitle = scrollContent.GetNode<Label>("ActionsSection/ActionsTitle");
		var actionRow = scrollContent.GetNode<HBoxContainer>("ActionsSection/ActionsRow");
		_resetModeButton = actionRow.GetNode<Button>("ResetModeBtn");
		_resetAllButton = actionRow.GetNode<Button>("ResetAllBtn");
		_copyJsonButton = actionRow.GetNode<Button>("CopyJsonBtn");
		_actionStatus = scrollContent.GetNode<Label>("ActionsSection/ActionStatus");

		_weatherModeOption.ItemSelected += HandleWeatherModeSelected;
		_weatherTypeOption.ItemSelected += _ => HandleWeatherSelectionChanged();
		_weatherIntensityOption.ItemSelected += _ => HandleWeatherSelectionChanged();
		_step1Button.Pressed += () => ApplyHostResult(_host.ExecuteStepWeather(1));
		_step10Button.Pressed += () => ApplyHostResult(_host.ExecuteStepWeather(10));
		_step50Button.Pressed += () => ApplyHostResult(_host.ExecuteStepWeather(50));
		_clearAccumButton.Pressed += () => ApplyHostResult(_host.ExecuteClearWeatherAccumulation());
		_tuningModeOption.ItemSelected += HandleTuningModeSelected;
		_resetModeButton.Pressed += ResetCurrentMode;
		_resetAllButton.Pressed += ResetAllModes;
		_copyJsonButton.Pressed += CopyJsonToClipboard;

		foreach (var definition in TuningDefinitions)
		{
			var row = new TuningControlRow(definition.Id, value => ApplyTuningValue(definition.Id, value));
			_tuningRowsRoot.AddChild(row.Root);
			_tuningRows.Add(row);
		}

		DisableKeyboardFocus(_panel);
		_panel.Visible = false;
		_actionStatus.Text = string.Empty;
		RefreshTexts();
		Refresh();
	}

	public bool Visible => _panel.Visible;
	public bool Dirty { get; set; } = true;

	public void Open()
	{
		_panel.Visible = true;
		Dirty = true;
		Refresh();
	}

	public void Close()
	{
		_panel.Visible = false;
		_actionStatus.Text = string.Empty;
	}

	public void Toggle()
	{
		if (Visible)
			Close();
		else
			Open();
	}

	public void RefreshTexts()
	{
		_titleLabel.Text = LocalizationService.T("ui.weather_lab.title");
		_previewTitle.Text = LocalizationService.T("ui.weather_lab.preview.title");
		_weatherTitle.Text = LocalizationService.T("ui.weather_lab.weather.title");
		_weatherModeLabel.Text = LocalizationService.T("ui.weather_lab.weather.mode");
		_weatherTypeLabel.Text = LocalizationService.T("ui.weather_lab.weather.type");
		_weatherIntensityLabel.Text = LocalizationService.T("ui.weather_lab.weather.intensity");
		_visualTitle.Text = LocalizationService.T("ui.weather_lab.visual.title");
		_tuningModeLabel.Text = LocalizationService.T("ui.weather_lab.visual.mode");
		_actionsTitle.Text = LocalizationService.T("ui.weather_lab.actions.title");
		_step1Button.Text = LocalizationService.T("ui.weather_lab.weather.step_1");
		_step10Button.Text = LocalizationService.T("ui.weather_lab.weather.step_10");
		_step50Button.Text = LocalizationService.T("ui.weather_lab.weather.step_50");
		_clearAccumButton.Text = LocalizationService.T("ui.weather_lab.weather.clear_accum");
		_resetModeButton.Text = LocalizationService.T("ui.weather_lab.actions.reset_mode");
		_resetAllButton.Text = LocalizationService.T("ui.weather_lab.actions.reset_all");
		_copyJsonButton.Text = LocalizationService.T("ui.weather_lab.actions.copy_json");

		foreach (var row in _tuningRows.Zip(TuningDefinitions))
			row.First.SetLabel(LocalizationService.TOrFallback(row.Second.LabelKey, row.Second.Id));

		Dirty = true;
		if (Visible)
			Refresh();
	}

	public void FlushIfDirty()
	{
		if (!Visible || !Dirty)
			return;

		Refresh();
	}

	public void Refresh()
	{
		Dirty = false;
		RefreshWeatherControls();
		RefreshPreview();
		RefreshTuningModeOptions();
		RefreshTuningValues();
	}

	private void RefreshWeatherControls()
	{
		_suppressWeatherSelection = true;

		var debugOverride = _host.State.Weather?.DebugOverride;
		var selectedModeId = debugOverride == null ? WeatherModeIds[0] : WeatherModeIds[1];
		var selectedTypeId = debugOverride != null
			? WeatherIds.ToId(debugOverride.Type)
			: GetSelectedStaticId(_weatherTypeOption, WeatherTypeIds) ?? WeatherTypeIds[0];
		var selectedIntensityId = debugOverride != null
			? WeatherIds.ToId(debugOverride.Intensity)
			: GetSelectedStaticId(_weatherIntensityOption, WeatherIntensityIds) ?? WeatherIntensityIds[1];

		_weatherModeOption.Clear();
		foreach (var modeId in WeatherModeIds)
			_weatherModeOption.AddItem(LocalizeWeatherMode(modeId));
		SelectStaticId(_weatherModeOption, WeatherModeIds, selectedModeId, WeatherModeIds[0]);

		_weatherTypeOption.Clear();
		foreach (var typeId in WeatherTypeIds)
			_weatherTypeOption.AddItem(LocalizeWeatherType(typeId));
		SelectStaticId(_weatherTypeOption, WeatherTypeIds, selectedTypeId, WeatherTypeIds[0]);

		_weatherIntensityOption.Clear();
		foreach (var intensityId in WeatherIntensityIds)
			_weatherIntensityOption.AddItem(LocalizeWeatherIntensity(intensityId));
		SelectStaticId(_weatherIntensityOption, WeatherIntensityIds, selectedIntensityId, WeatherIntensityIds[1]);

		_suppressWeatherSelection = false;
	}

	private void RefreshPreview()
	{
		if (_host.State.World == null)
		{
			_previewText.Text = LocalizationService.TOrFallback(
				"ui.weather_lab.preview.empty",
				"No active session.");
			return;
		}

		var state = _host.State;
		var sample = WeatherRules.GetLocalWeather(state, state.PlayerX, state.PlayerY, state.PlayerZ);
		var surface = WeatherSurface.GetSurfaceState(state, state.PlayerX, state.PlayerY, state.PlayerZ);
		var controlMode = state.Weather?.DebugOverride == null
			? LocalizationService.T("ui.weather_lab.weather.mode_natural")
			: LocalizationService.T("ui.weather_lab.weather.mode_locked");
		var exposed = surface.IsExposed
			? LocalizationService.T("ui.weather_lab.preview.exposed_yes")
			: LocalizationService.T("ui.weather_lab.preview.exposed_no");

		var line1 = LocalizationService.TOrFallback(
			"ui.weather_lab.preview.line1",
			"Weather: {weather} | Intensity: {intensity} | Exposed: {exposed}",
			("weather", LocalizeWeatherType(sample.TypeId)),
			("intensity", LocalizeWeatherIntensity(sample.IntensityId)),
			("exposed", exposed));
		var line2 = LocalizationService.TOrFallback(
			"ui.weather_lab.preview.line2",
			"Control: {control} | Turn: {turn}",
			("control", controlMode),
			("turn", state.Turn));
		var line3 = LocalizationService.TOrFallback(
			"ui.weather_lab.preview.line3",
			"Accum: snow {snow} | sand {sand} | wet {wet} | ice {ice}",
			("snow", surface.Accumulation.SnowDepth),
			("sand", surface.Accumulation.SandDepth),
			("wet", surface.Accumulation.Wetness),
			("ice", surface.Accumulation.IceDepth));
		_previewText.Text = string.Join('\n', [line1, line2, line3]);
	}

	private void RefreshTuningModeOptions()
	{
		_suppressTuningModeSelection = true;
		_tuningModeOption.Clear();
		foreach (var mode in WeatherScreenFxTuningSet.EditableModes)
			_tuningModeOption.AddItem(LocalizeTuningMode(mode));

		var selectedIndex = WeatherScreenFxTuningSet.EditableModes
			.ToList()
			.FindIndex(mode => mode == _currentTuningMode);
		if (selectedIndex < 0)
		{
			_currentTuningMode = WeatherScreenFxMode.Rain;
			selectedIndex = 0;
		}

		if (WeatherScreenFxTuningSet.EditableModes.Count > 0)
			_tuningModeOption.Select(selectedIndex);
		_suppressTuningModeSelection = false;
	}

	private void RefreshTuningValues()
	{
		var profile = _host.WeatherLabTuningSet.GetProfile(_currentTuningMode);
		foreach (var row in _tuningRows)
			row.SetValue(profile.GetValue(row.Id));
	}

	private void HandleWeatherModeSelected(long index)
	{
		if (_suppressWeatherSelection)
			return;

		var modeId = index >= 0 && index < WeatherModeIds.Length
			? WeatherModeIds[index]
			: WeatherModeIds[0];
		if (string.Equals(modeId, "natural", StringComparison.Ordinal))
			ApplyHostResult(_host.ExecuteUnlockWeather());
		else
			ApplyCurrentWeatherLock();
	}

	private void HandleWeatherSelectionChanged()
	{
		if (_suppressWeatherSelection)
			return;

		var selectedModeId = GetSelectedStaticId(_weatherModeOption, WeatherModeIds) ?? WeatherModeIds[0];
		if (string.Equals(selectedModeId, "locked", StringComparison.Ordinal))
			ApplyCurrentWeatherLock();
		else
			Dirty = true;
	}

	private void ApplyCurrentWeatherLock()
	{
		var typeId = GetSelectedStaticId(_weatherTypeOption, WeatherTypeIds) ?? WeatherTypeIds[0];
		var intensityId = GetSelectedStaticId(_weatherIntensityOption, WeatherIntensityIds) ?? WeatherIntensityIds[1];
		if (!WeatherIds.TryParseType(typeId, out var type) || !WeatherIds.TryParseIntensity(intensityId, out var intensity))
			return;

		ApplyHostResult(_host.ExecuteLockWeather(type, intensity));
	}

	private void HandleTuningModeSelected(long index)
	{
		if (_suppressTuningModeSelection || index < 0 || index >= WeatherScreenFxTuningSet.EditableModes.Count)
			return;

		_currentTuningMode = WeatherScreenFxTuningSet.EditableModes[(int)index];
		RefreshTuningValues();
	}

	private void ApplyTuningValue(string id, float value)
	{
		_host.WeatherLabTuningSet.GetProfile(_currentTuningMode).SetValue(id, value);
		_host.NotifyWeatherLabTuningChanged();
	}

	private void ResetCurrentMode()
	{
		_host.WeatherLabTuningSet.ResetMode(_currentTuningMode);
		_host.NotifyWeatherLabTuningChanged();
		SetActionStatus(LocalizationService.T("ui.weather_lab.actions.reset_mode_done"));
		Dirty = true;
		Refresh();
	}

	private void ResetAllModes()
	{
		_host.WeatherLabTuningSet.ResetAll();
		_host.NotifyWeatherLabTuningChanged();
		SetActionStatus(LocalizationService.T("ui.weather_lab.actions.reset_all_done"));
		Dirty = true;
		Refresh();
	}

	private void CopyJsonToClipboard()
	{
		var json = _host.WeatherLabTuningSet.ToJson();
		DisplayServer.ClipboardSet(json);
		SetActionStatus(LocalizationService.T("ui.weather_lab.actions.copy_done"));
	}

	private void ApplyHostResult(DebugModule.Result result)
	{
		if (result.Logs.Count > 0)
			SetActionStatus(string.Join('\n', result.Logs.Where(static line => !string.IsNullOrWhiteSpace(line))));

		Dirty = true;
		Refresh();
	}

	private void SetActionStatus(string message) =>
		_actionStatus.Text = message;

	private static string LocalizeWeatherMode(string modeId) => modeId switch
	{
		"locked" => LocalizationService.T("ui.weather_lab.weather.mode_locked"),
		_ => LocalizationService.T("ui.weather_lab.weather.mode_natural"),
	};

	private static string LocalizeWeatherType(string typeId) =>
		LocalizationService.TOrFallback($"weather.type.{typeId}", typeId);

	private static string LocalizeWeatherIntensity(string intensityId) =>
		LocalizationService.TOrFallback($"weather.intensity.{intensityId}", intensityId);

	private static string LocalizeTuningMode(WeatherScreenFxMode mode) =>
		LocalizationService.TOrFallback($"ui.weather_lab.visual.mode.{WeatherScreenFxTuningSet.ToId(mode)}", WeatherScreenFxTuningSet.ToId(mode));

	private static string? GetSelectedStaticId(OptionButton button, IReadOnlyList<string> ids)
	{
		var selected = (int)button.Selected;
		return selected >= 0 && selected < ids.Count ? ids[selected] : null;
	}

	private static void SelectStaticId(OptionButton button, IReadOnlyList<string> ids, string? selectedId, string fallbackId)
	{
		var resolvedId = !string.IsNullOrWhiteSpace(selectedId) && ids.Contains(selectedId)
			? selectedId
			: fallbackId;
		var index = 0;
		for (var i = 0; i < ids.Count; i++)
		{
			if (!string.Equals(ids[i], resolvedId, StringComparison.Ordinal))
				continue;

			index = i;
			break;
		}
		if (ids.Count > 0)
			button.Select(index);
	}

	private static void DisableKeyboardFocus(Node node)
	{
		if (node is Control control)
			control.FocusMode = Control.FocusModeEnum.None;

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
				DisableKeyboardFocus(childNode);
		}
	}

	private readonly record struct TuningDefinition(string Id, string LabelKey);

	private sealed class TuningControlRow
	{
		private bool _suppressChanges;
		private readonly Label _label;
		private readonly HSlider _slider;
		private readonly SpinBox _spinBox;
		private readonly Action<float> _onChanged;

		public TuningControlRow(string id, Action<float> onChanged)
		{
			Id = id;
			_onChanged = onChanged;
			var row = new HBoxContainer
			{
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			row.AddThemeConstantOverride("separation", 8);
			_label = new Label
			{
				CustomMinimumSize = new Vector2(170f, 0f),
				SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
				VerticalAlignment = VerticalAlignment.Center,
			};
			_slider = new HSlider
			{
				MinValue = 0d,
				MaxValue = 2d,
				Step = 0.05d,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				Value = 1d,
			};
			_spinBox = new SpinBox
			{
				MinValue = 0d,
				MaxValue = 2d,
				Step = 0.05d,
				Value = 1d,
				CustomMinimumSize = new Vector2(92f, 0f),
			};
			_slider.FocusMode = Control.FocusModeEnum.None;
			_spinBox.FocusMode = Control.FocusModeEnum.None;

			_slider.ValueChanged += HandleValueChanged;
			_spinBox.ValueChanged += HandleValueChanged;

			row.AddChild(_label);
			row.AddChild(_slider);
			row.AddChild(_spinBox);
			Root = row;
		}

		public string Id { get; }
		public Control Root { get; }

		public void SetLabel(string text) =>
			_label.Text = text;

		public void SetValue(float value)
		{
			var clamped = WeatherScreenFxTuningProfile.ClampScale(value);
			_suppressChanges = true;
			_slider.Value = clamped;
			_spinBox.Value = clamped;
			_suppressChanges = false;
		}

		private void HandleValueChanged(double value)
		{
			if (_suppressChanges)
				return;

			var clamped = WeatherScreenFxTuningProfile.ClampScale((float)value);
			_suppressChanges = true;
			_slider.Value = clamped;
			_spinBox.Value = clamped;
			_suppressChanges = false;
			_onChanged(clamped);
		}
	}
}
