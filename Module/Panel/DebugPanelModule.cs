using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;

namespace MiniRPG.Module.Panel;

public sealed class DebugPanelModule : IPanel
{
	public interface IHost
	{
		GameState State { get; }
		DebugModule.Result ExecuteSpawnChest();
		DebugModule.Result ExecuteAddGold(int amount);
		DebugModule.Result ExecuteHeal();
		DebugModule.Result ExecuteToggleGodMode();
		DebugModule.Result ExecuteMoveDownFloor();
		DebugModule.Result ExecuteSpawnDialogTestNpcs();
		DebugModule.Result ExecuteSpawnActor(string templateId);
		DebugModule.Result ExecuteQueryWeatherStatus();
		DebugModule.Result ExecuteLockWeather(WeatherType type, WeatherIntensity intensity);
		DebugModule.Result ExecuteUnlockWeather();
		DebugModule.Result ExecuteStepWeather(int turns);
		DebugModule.Result ExecuteClearWeatherAccumulation();
		DebugModule.Result ExecuteToggleFreeBuild();
		DebugModule.Result ExecuteQueryFacilityStatus();
		DebugModule.Result ExecutePlaceFacility(string facilityId, string? directionId);
		DebugModule.Result ExecuteFacilityDeliver();
		DebugModule.Result ExecuteFacilityBuild();
		DebugModule.Result ExecuteExportPreset(string scenarioId);
		DebugModule.Result ExecuteQueryRenderPerfStatus();
		DebugModule.Result ExecuteToggleRevealAll();
	}

	private const string AllFilterId = "all";
	private const string FacingDirectionId = "facing";

	private static readonly string[] SpawnFilterIds = [AllFilterId, Factions.Hostile, Factions.Friendly];
	private static readonly string[] WeatherTypeIds = ["clear", "rain", "fog", "snow", "storm", "thunderstorm", "sandstorm"];
	private static readonly string[] WeatherIntensityIds = ["light", "normal", "heavy"];
	private static readonly string[] DirectionIds = [FacingDirectionId, "north", "east", "south", "west"];

	private readonly PanelContainer _panel;
	private readonly IHost _host;
	private readonly RichTextLabel _header;
	private readonly LineEdit _goldAmountEdit;
	private readonly Button _addGoldButton;
	private readonly Button _goldPreset100Button;
	private readonly Button _goldPreset1000Button;
	private readonly Button _goldPreset5000Button;
	private readonly Button _healButton;
	private readonly Button _spawnChestButton;
	private readonly Button _toggleGodModeButton;
	private readonly Button _downFloorButton;
	private readonly Button _spawnNpcButton;
	private readonly Button _revealAllButton;
	private readonly Button _freeBuildButton;
	private readonly OptionButton _spawnFilterOption;
	private readonly OptionButton _spawnTemplateOption;
	private readonly Button _spawnButton;
	private readonly RichTextLabel _weatherStatus;
	private readonly OptionButton _weatherTypeOption;
	private readonly OptionButton _weatherIntensityOption;
	private readonly LineEdit _weatherStepEdit;
	private readonly Button _weatherStatusButton;
	private readonly Button _weatherLockButton;
	private readonly Button _weatherUnlockButton;
	private readonly Button _weatherStepButton;
	private readonly Button _weatherClearAccumButton;
	private readonly RichTextLabel _facilityStatus;
	private readonly OptionButton _facilityOption;
	private readonly OptionButton _directionOption;
	private readonly Button _facilityPlaceButton;
	private readonly Button _facilityStatusButton;
	private readonly Button _facilityDeliverButton;
	private readonly Button _facilityBuildButton;
	private readonly LineEdit _exportPresetEdit;
	private readonly Button _exportPresetButton;
	private readonly RichTextLabel _resultsText;

	private readonly List<string> _spawnTemplateIds = [];
	private readonly List<string> _facilityIds = [];
	private readonly List<string> _recentLogs = [];

	public DebugPanelModule(PanelContainer panel, IHost host)
	{
		_panel = panel;
		_host = host;
		var vbox = panel.GetNode<VBoxContainer>("MarginContainer/VBox");
		_header = vbox.GetNode<RichTextLabel>("HeaderBar/Header");
		_goldAmountEdit = vbox.GetNode<LineEdit>("ContentScroll/Content/QuickSection/GoldRow/GoldAmountEdit");
		_addGoldButton = vbox.GetNode<Button>("ContentScroll/Content/QuickSection/GoldRow/AddGoldBtn");
		_goldPreset100Button = vbox.GetNode<Button>("ContentScroll/Content/QuickSection/QuickButtons/Gold100Btn");
		_goldPreset1000Button = vbox.GetNode<Button>("ContentScroll/Content/QuickSection/QuickButtons/Gold1000Btn");
		_goldPreset5000Button = vbox.GetNode<Button>("ContentScroll/Content/QuickSection/QuickButtons/Gold5000Btn");
		_healButton = vbox.GetNode<Button>("ContentScroll/Content/QuickSection/QuickButtons/HealBtn");
		_spawnChestButton = vbox.GetNode<Button>("ContentScroll/Content/QuickSection/QuickButtons/ChestBtn");
		_toggleGodModeButton = vbox.GetNode<Button>("ContentScroll/Content/QuickSection/QuickButtons/GodModeBtn");
		_downFloorButton = vbox.GetNode<Button>("ContentScroll/Content/QuickSection/QuickButtons/DownFloorBtn");
		_spawnNpcButton = vbox.GetNode<Button>("ContentScroll/Content/QuickSection/QuickButtons/SpawnNpcBtn");
		_revealAllButton = vbox.GetNode<Button>("ContentScroll/Content/QuickSection/QuickButtons/RevealAllBtn");
		_freeBuildButton = vbox.GetNode<Button>("ContentScroll/Content/QuickSection/QuickButtons/FreeBuildBtn");
		_spawnFilterOption = vbox.GetNode<OptionButton>("ContentScroll/Content/SpawnSection/FilterRow/SpawnFilterOption");
		_spawnTemplateOption = vbox.GetNode<OptionButton>("ContentScroll/Content/SpawnSection/TemplateRow/SpawnTemplateOption");
		_spawnButton = vbox.GetNode<Button>("ContentScroll/Content/SpawnSection/ActionsRow/SpawnBtn");
		_weatherStatus = vbox.GetNode<RichTextLabel>("ContentScroll/Content/WeatherSection/WeatherStatus");
		_weatherTypeOption = vbox.GetNode<OptionButton>("ContentScroll/Content/WeatherSection/TypeRow/WeatherTypeOption");
		_weatherIntensityOption = vbox.GetNode<OptionButton>("ContentScroll/Content/WeatherSection/IntensityRow/WeatherIntensityOption");
		_weatherStepEdit = vbox.GetNode<LineEdit>("ContentScroll/Content/WeatherSection/StepRow/WeatherStepEdit");
		_weatherStatusButton = vbox.GetNode<Button>("ContentScroll/Content/WeatherSection/ActionsRow/WeatherStatusBtn");
		_weatherLockButton = vbox.GetNode<Button>("ContentScroll/Content/WeatherSection/ActionsRow/WeatherLockBtn");
		_weatherUnlockButton = vbox.GetNode<Button>("ContentScroll/Content/WeatherSection/ActionsRow/WeatherUnlockBtn");
		_weatherStepButton = vbox.GetNode<Button>("ContentScroll/Content/WeatherSection/StepRow/WeatherStepBtn");
		_weatherClearAccumButton = vbox.GetNode<Button>("ContentScroll/Content/WeatherSection/ClearRow/WeatherClearAccumBtn");
		_facilityStatus = vbox.GetNode<RichTextLabel>("ContentScroll/Content/FacilitySection/FacilityStatus");
		_facilityOption = vbox.GetNode<OptionButton>("ContentScroll/Content/FacilitySection/FacilityRow/FacilityOption");
		_directionOption = vbox.GetNode<OptionButton>("ContentScroll/Content/FacilitySection/DirectionRow/DirectionOption");
		_facilityPlaceButton = vbox.GetNode<Button>("ContentScroll/Content/FacilitySection/ActionsRow/FacilityPlaceBtn");
		_facilityStatusButton = vbox.GetNode<Button>("ContentScroll/Content/FacilitySection/ActionsRow/FacilityStatusBtn");
		_facilityDeliverButton = vbox.GetNode<Button>("ContentScroll/Content/FacilitySection/ActionsRow/FacilityDeliverBtn");
		_facilityBuildButton = vbox.GetNode<Button>("ContentScroll/Content/FacilitySection/ActionsRow/FacilityBuildBtn");
		_exportPresetEdit = vbox.GetNode<LineEdit>("ContentScroll/Content/ExportSection/ExportRow/ExportPresetEdit");
		_exportPresetButton = vbox.GetNode<Button>("ContentScroll/Content/ExportSection/ExportRow/ExportPresetBtn");
		_resultsText = vbox.GetNode<RichTextLabel>("ResultsSection/ResultsText");

		_goldAmountEdit.Text = "1000";
		_weatherStepEdit.Text = "1";

		_addGoldButton.Pressed += OnAddGoldPressed;
		_goldPreset100Button.Pressed += () => ApplyHostResult(_host.ExecuteAddGold(100));
		_goldPreset1000Button.Pressed += () => ApplyHostResult(_host.ExecuteAddGold(1000));
		_goldPreset5000Button.Pressed += () => ApplyHostResult(_host.ExecuteAddGold(5000));
		_healButton.Pressed += () => ApplyHostResult(_host.ExecuteHeal());
		_spawnChestButton.Pressed += () => ApplyHostResult(_host.ExecuteSpawnChest());
		_toggleGodModeButton.Pressed += () => ApplyHostResult(_host.ExecuteToggleGodMode());
		_downFloorButton.Pressed += () => ApplyHostResult(_host.ExecuteMoveDownFloor());
		_spawnNpcButton.Pressed += () => ApplyHostResult(_host.ExecuteSpawnDialogTestNpcs());
		_revealAllButton.Pressed += () => ApplyHostResult(_host.ExecuteToggleRevealAll());
		_freeBuildButton.Pressed += () => ApplyHostResult(_host.ExecuteToggleFreeBuild());
		_spawnFilterOption.ItemSelected += _ => RefreshSpawnTemplateOptions();
		_spawnButton.Pressed += OnSpawnPressed;
		_weatherStatusButton.Pressed += () => ApplyHostResult(_host.ExecuteQueryWeatherStatus());
		_weatherLockButton.Pressed += OnWeatherLockPressed;
		_weatherUnlockButton.Pressed += () => ApplyHostResult(_host.ExecuteUnlockWeather());
		_weatherStepButton.Pressed += OnWeatherStepPressed;
		_weatherClearAccumButton.Pressed += () => ApplyHostResult(_host.ExecuteClearWeatherAccumulation());
		_facilityStatusButton.Pressed += () => ApplyHostResult(_host.ExecuteQueryFacilityStatus());
		_facilityPlaceButton.Pressed += OnFacilityPlacePressed;
		_facilityDeliverButton.Pressed += () => ApplyHostResult(_host.ExecuteFacilityDeliver());
		_facilityBuildButton.Pressed += () => ApplyHostResult(_host.ExecuteFacilityBuild());
		_exportPresetButton.Pressed += OnExportPressed;

		foreach (var button in GetButtons())
			button.FocusMode = Control.FocusModeEnum.None;
	}

	public string PanelId => "debug";
	public PanelContainer PanelNode => _panel;
	public bool Visible
	{
		get => _panel.Visible;
		set
		{
			_panel.Visible = value;
			if (value)
				Refresh();
		}
	}

	public bool Dirty { get; set; } = true;
	public bool ConsumeUnhandledKeys => false;

	public void Open()
	{
		Visible = true;
		Dirty = true;
		Refresh();
	}

	public void Close() => Visible = false;

	public bool HandleCommand(string cmd)
	{
		if (cmd == "close")
		{
			Close();
			return true;
		}

		return false;
	}

	public void RefreshTexts()
	{
		Dirty = true;
		Refresh();
	}

	public void FlushIfDirty()
	{
		if (!Dirty)
			return;

		Refresh();
	}

	public void Refresh()
	{
		Dirty = false;
		RenderHeader();
		RefreshSpawnFilterOptions();
		RefreshSpawnTemplateOptions();
		RefreshWeatherOptions();
		RefreshWeatherStatus();
		RefreshFacilityOptions();
		RefreshDirectionOptions();
		RefreshFacilityStatus();
		RefreshQuickActionState();
		RenderRecentLogs();
	}

	private void OnAddGoldPressed()
	{
		var raw = _goldAmountEdit.Text.Trim();
		if (raw.Length == 0)
		{
			_goldAmountEdit.Text = "1000";
			ApplyHostResult(_host.ExecuteAddGold(1000));
			return;
		}

		if (!int.TryParse(raw, out var amount))
		{
			SetLocalResult(LocalizationService.T("ui.debug_panel.validation.gold"));
			return;
		}

		ApplyHostResult(_host.ExecuteAddGold(amount));
	}

	private void OnSpawnPressed()
	{
		var templateId = GetSelectedId(_spawnTemplateOption, _spawnTemplateIds);
		if (string.IsNullOrWhiteSpace(templateId))
		{
			SetLocalResult(LocalizationService.T("ui.debug_panel.validation.spawn_template"));
			return;
		}

		ApplyHostResult(_host.ExecuteSpawnActor(templateId));
	}

	private void OnWeatherLockPressed()
	{
		var typeId = GetSelectedStaticId(_weatherTypeOption, WeatherTypeIds);
		var intensityId = GetSelectedStaticId(_weatherIntensityOption, WeatherIntensityIds);
		if (!WeatherIds.TryParseType(typeId, out var type) || !WeatherIds.TryParseIntensity(intensityId, out var intensity))
		{
			SetLocalResult(LocalizationService.T("ui.debug_panel.validation.weather"));
			return;
		}

		ApplyHostResult(_host.ExecuteLockWeather(type, intensity));
	}

	private void OnWeatherStepPressed()
	{
		var raw = _weatherStepEdit.Text.Trim();
		if (raw.Length == 0)
		{
			_weatherStepEdit.Text = "1";
			ApplyHostResult(_host.ExecuteStepWeather(1));
			return;
		}

		if (!int.TryParse(raw, out var turns) || turns <= 0)
		{
			SetLocalResult(LocalizationService.T("ui.debug_panel.validation.step"));
			return;
		}

		ApplyHostResult(_host.ExecuteStepWeather(turns));
	}

	private void OnFacilityPlacePressed()
	{
		var facilityId = GetSelectedId(_facilityOption, _facilityIds);
		if (string.IsNullOrWhiteSpace(facilityId))
		{
			SetLocalResult(LocalizationService.T("ui.debug_panel.validation.facility"));
			return;
		}

		var directionId = GetSelectedStaticId(_directionOption, DirectionIds);
		if (string.Equals(directionId, FacingDirectionId, StringComparison.Ordinal))
			directionId = null;

		ApplyHostResult(_host.ExecutePlaceFacility(facilityId, directionId));
	}

	private void OnExportPressed()
	{
		var scenarioId = _exportPresetEdit.Text.Trim();
		ApplyHostResult(_host.ExecuteExportPreset(scenarioId));
	}

	private void ApplyHostResult(DebugModule.Result result)
	{
		_recentLogs.Clear();
		if (result.Logs != null)
			_recentLogs.AddRange(result.Logs);
		Dirty = true;
		Refresh();
	}

	private void SetLocalResult(string message)
	{
		_recentLogs.Clear();
		_recentLogs.Add(message);
		RenderRecentLogs();
	}

	private void RenderHeader()
	{
		_header.Clear();
		_header.AppendText($"[center]{LocalizationService.T("ui.debug_panel.title")}[/center]");
	}

	private void RefreshQuickActionState()
	{
		_toggleGodModeButton.Text = DebugModule.IsGodModeEnabled(_host.State)
			? LocalizationService.T("ui.debug_panel.quick.god.disable")
			: LocalizationService.T("ui.debug_panel.quick.god.enable");
		_freeBuildButton.Text = _host.State.RuntimeFreeBuild
			? LocalizationService.TOrFallback("ui.debug_panel.quick.free_build.disable", "Free Build: ON")
			: LocalizationService.TOrFallback("ui.debug_panel.quick.free_build.enable", "Free Build: OFF");
	}

	private void RefreshSpawnFilterOptions()
	{
		var selectedId = GetSelectedStaticId(_spawnFilterOption, SpawnFilterIds) ?? AllFilterId;
		_spawnFilterOption.Clear();
		foreach (var filterId in SpawnFilterIds)
			_spawnFilterOption.AddItem(LocalizeSpawnFilter(filterId));

		SelectStaticId(_spawnFilterOption, SpawnFilterIds, selectedId, AllFilterId);
	}

	private void RefreshSpawnTemplateOptions()
	{
		var selectedId = GetSelectedId(_spawnTemplateOption, _spawnTemplateIds);
		var currentFilter = GetSelectedStaticId(_spawnFilterOption, SpawnFilterIds) ?? AllFilterId;
		var options = DebugModule.GetSpawnTemplateOptions()
			.Where(option => string.Equals(currentFilter, AllFilterId, StringComparison.Ordinal)
				|| string.Equals(option.Group, currentFilter, StringComparison.Ordinal))
			.ToArray();

		_spawnTemplateIds.Clear();
		_spawnTemplateOption.Clear();
		foreach (var option in options)
		{
			_spawnTemplateIds.Add(option.Id);
			_spawnTemplateOption.AddItem(option.Label);
		}

		SelectDynamicId(_spawnTemplateOption, _spawnTemplateIds, selectedId);
		_spawnButton.Disabled = _spawnTemplateIds.Count == 0;
	}

	private void RefreshWeatherOptions()
	{
		var selectedTypeId = GetSelectedStaticId(_weatherTypeOption, WeatherTypeIds);
		var selectedIntensityId = GetSelectedStaticId(_weatherIntensityOption, WeatherIntensityIds);
		if (string.IsNullOrWhiteSpace(selectedTypeId))
			selectedTypeId = _host.State.Weather?.DebugOverride != null
				? WeatherIds.ToId(_host.State.Weather.DebugOverride.Type)
				: WeatherTypeIds[0];
		if (string.IsNullOrWhiteSpace(selectedIntensityId))
			selectedIntensityId = _host.State.Weather?.DebugOverride != null
				? WeatherIds.ToId(_host.State.Weather.DebugOverride.Intensity)
				: WeatherIntensityIds[1];

		_weatherTypeOption.Clear();
		foreach (var weatherTypeId in WeatherTypeIds)
			_weatherTypeOption.AddItem(LocalizeWeatherType(weatherTypeId));
		SelectStaticId(_weatherTypeOption, WeatherTypeIds, selectedTypeId, WeatherTypeIds[0]);

		_weatherIntensityOption.Clear();
		foreach (var intensityId in WeatherIntensityIds)
			_weatherIntensityOption.AddItem(LocalizeWeatherIntensity(intensityId));
		SelectStaticId(_weatherIntensityOption, WeatherIntensityIds, selectedIntensityId, WeatherIntensityIds[1]);
	}

	private void RefreshWeatherStatus() =>
		RenderLines(_weatherStatus, DebugModule.BuildWeatherStatusLines(_host.State), "ui.debug_panel.weather.empty");

	private void RefreshFacilityOptions()
	{
		var selectedId = GetSelectedId(_facilityOption, _facilityIds);
		var options = DebugModule.GetFacilityOptions();
		_facilityIds.Clear();
		_facilityOption.Clear();
		foreach (var option in options)
		{
			_facilityIds.Add(option.Id);
			_facilityOption.AddItem(option.Label);
		}

		SelectDynamicId(_facilityOption, _facilityIds, selectedId);
		_facilityPlaceButton.Disabled = _facilityIds.Count == 0;
	}

	private void RefreshDirectionOptions()
	{
		var selectedId = GetSelectedStaticId(_directionOption, DirectionIds) ?? FacingDirectionId;
		_directionOption.Clear();
		foreach (var directionId in DirectionIds)
			_directionOption.AddItem(LocalizeDirection(directionId));
		SelectStaticId(_directionOption, DirectionIds, selectedId, FacingDirectionId);
	}

	private void RefreshFacilityStatus() =>
		RenderLines(_facilityStatus, DebugModule.BuildFacilityStatusLines(_host.State), "ui.debug_panel.facility.empty");

	private void RenderRecentLogs()
	{
		var merged = new List<string>();
		var perf = _host.ExecuteQueryRenderPerfStatus();
		if (perf.Logs != null)
		{
			foreach (var line in perf.Logs)
			{
				if (!string.IsNullOrWhiteSpace(line))
					merged.Add(line);
			}
		}

		if (merged.Count > 0 && _recentLogs.Count > 0)
			merged.Add("----------------");
		merged.AddRange(_recentLogs);
		RenderLines(_resultsText, merged, "ui.debug_panel.results.empty");
	}

	private static void RenderLines(RichTextLabel label, IEnumerable<string> lines, string emptyKey)
	{
		var text = string.Join('\n', lines.Where(static line => !string.IsNullOrWhiteSpace(line)));
		if (string.IsNullOrWhiteSpace(text))
			text = LocalizationService.T(emptyKey);

		label.Clear();
		label.AppendText(text);
	}

	private static string? GetSelectedStaticId(OptionButton button, IReadOnlyList<string> ids)
	{
		var selected = (int)button.Selected;
		return selected >= 0 && selected < ids.Count ? ids[selected] : null;
	}

	private static string? GetSelectedId(OptionButton button, IReadOnlyList<string> ids)
	{
		var selected = (int)button.Selected;
		return selected >= 0 && selected < ids.Count ? ids[selected] : null;
	}

	private static void SelectStaticId(OptionButton button, IReadOnlyList<string> ids, string? selectedId, string fallbackId)
	{
		var resolvedId = !string.IsNullOrWhiteSpace(selectedId) && ids.Contains(selectedId)
			? selectedId
			: fallbackId;
		var index = Math.Max(0, IndexOfId(ids, resolvedId));
		if (ids.Count > 0)
			button.Select(index);
	}

	private static void SelectDynamicId(OptionButton button, IReadOnlyList<string> ids, string? selectedId)
	{
		if (ids.Count == 0)
			return;

		var index = !string.IsNullOrWhiteSpace(selectedId) ? IndexOfId(ids, selectedId) : -1;
		if (index < 0)
			index = 0;
		button.Select(index);
	}

	private static int IndexOfId(IReadOnlyList<string> ids, string? selectedId)
	{
		if (string.IsNullOrWhiteSpace(selectedId))
			return -1;

		for (var i = 0; i < ids.Count; i++)
		{
			if (string.Equals(ids[i], selectedId, StringComparison.Ordinal))
				return i;
		}

		return -1;
	}

	private static string LocalizeSpawnFilter(string filterId) => filterId switch
	{
		AllFilterId => LocalizationService.T("ui.debug_panel.spawn.filter.all"),
		Factions.Hostile => LocalizationService.T("ui.debug_panel.spawn.filter.hostile"),
		Factions.Friendly => LocalizationService.T("ui.debug_panel.spawn.filter.friendly"),
		_ => filterId,
	};

	private static string LocalizeWeatherType(string typeId) =>
		LocalizationService.TOrFallback($"ui.debug_panel.weather.type.{typeId}", typeId);

	private static string LocalizeWeatherIntensity(string intensityId) =>
		LocalizationService.TOrFallback($"ui.debug_panel.weather.intensity.{intensityId}", intensityId);

	private static string LocalizeDirection(string directionId) => directionId switch
	{
		FacingDirectionId => LocalizationService.T("ui.debug_panel.facility.direction.facing"),
		_ => LocalizationService.TOrFallback($"ui.debug_panel.facility.direction.{directionId}", directionId),
	};

	private IEnumerable<Button> GetButtons()
	{
		yield return _addGoldButton;
		yield return _goldPreset100Button;
		yield return _goldPreset1000Button;
		yield return _goldPreset5000Button;
		yield return _healButton;
		yield return _spawnChestButton;
		yield return _toggleGodModeButton;
		yield return _downFloorButton;
		yield return _spawnNpcButton;
		yield return _freeBuildButton;
		yield return _spawnButton;
		yield return _weatherStatusButton;
		yield return _weatherLockButton;
		yield return _weatherUnlockButton;
		yield return _weatherStepButton;
		yield return _weatherClearAccumButton;
		yield return _facilityPlaceButton;
		yield return _facilityStatusButton;
		yield return _facilityDeliverButton;
		yield return _facilityBuildButton;
		yield return _exportPresetButton;
	}
}
