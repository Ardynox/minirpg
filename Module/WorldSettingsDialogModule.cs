using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Map;

namespace MiniRPG.Module;

public sealed class WorldSettingsDialogModule : IModalInputLayer
{
	private const string BasicFormPath = "Margin/VBox/BasicSection/BasicForm";
	private const string AdvancedFormPath = "Margin/VBox/AdvancedSection/AdvancedForm";

	private readonly PanelContainer _panel;
	private readonly LineEdit _worldNameEdit;
	private readonly LineEdit _seedEdit;
	private readonly Button _randomizeSeedButton;
	private readonly OptionButton _generatorOption;
	private readonly CheckButton _advancedToggle;
	private readonly Control _advancedSection;
	private readonly LineEdit _climateEdit;
	private readonly LineEdit _startSeasonEdit;
	private readonly LineEdit _civilizationLevelEdit;
	private readonly LineEdit _monsterDensityEdit;
	private readonly LineEdit _npcDensityEdit;
	private readonly LineEdit _lootAbundanceEdit;
	private readonly LineEdit _nestIntensityEdit;
	private readonly LineEdit _weatherVolatilityEdit;
	private readonly Label _validationLabel;
	private readonly Button _confirmButton;

	private readonly List<string> _generatorIds = [];
	private bool _suppressGeneratorSelection;

	public event Action<string, WorldSettings>? ConfirmRequested;
	public event Action? CancelRequested;

	public WorldSettingsDialogModule(PanelContainer panel)
	{
		_panel = panel;
		_worldNameEdit = panel.GetNode<LineEdit>($"{BasicFormPath}/WorldNameEdit");
		_seedEdit = panel.GetNode<LineEdit>($"{BasicFormPath}/SeedRow/SeedEdit");
		_randomizeSeedButton = panel.GetNode<Button>($"{BasicFormPath}/SeedRow/RandomizeSeedBtn");
		_generatorOption = panel.GetNode<OptionButton>($"{BasicFormPath}/GeneratorOption");
		_advancedToggle = panel.GetNode<CheckButton>("Margin/VBox/AdvancedToggle");
		_advancedSection = panel.GetNode<Control>("Margin/VBox/AdvancedSection");
		_climateEdit = panel.GetNode<LineEdit>($"{AdvancedFormPath}/ClimateEdit");
		_startSeasonEdit = panel.GetNode<LineEdit>($"{AdvancedFormPath}/StartSeasonEdit");
		_civilizationLevelEdit = panel.GetNode<LineEdit>($"{AdvancedFormPath}/CivilizationLevelEdit");
		_monsterDensityEdit = panel.GetNode<LineEdit>($"{AdvancedFormPath}/MonsterDensityEdit");
		_npcDensityEdit = panel.GetNode<LineEdit>($"{AdvancedFormPath}/NpcDensityEdit");
		_lootAbundanceEdit = panel.GetNode<LineEdit>($"{AdvancedFormPath}/LootAbundanceEdit");
		_nestIntensityEdit = panel.GetNode<LineEdit>($"{AdvancedFormPath}/NestIntensityEdit");
		_weatherVolatilityEdit = panel.GetNode<LineEdit>($"{AdvancedFormPath}/WeatherVolatilityEdit");
		_validationLabel = panel.GetNode<Label>("Margin/VBox/Validation");
		_confirmButton = panel.GetNode<Button>("Margin/VBox/Actions/ConfirmBtn");

		_worldNameEdit.TextChanged += _ => RefreshValidation();
		_seedEdit.TextChanged += _ => RefreshValidation();
		_climateEdit.TextChanged += _ => RefreshValidation();
		_startSeasonEdit.TextChanged += _ => RefreshValidation();
		_civilizationLevelEdit.TextChanged += _ => RefreshValidation();
		_monsterDensityEdit.TextChanged += _ => RefreshValidation();
		_npcDensityEdit.TextChanged += _ => RefreshValidation();
		_lootAbundanceEdit.TextChanged += _ => RefreshValidation();
		_nestIntensityEdit.TextChanged += _ => RefreshValidation();
		_weatherVolatilityEdit.TextChanged += _ => RefreshValidation();
		_randomizeSeedButton.Pressed += RandomizeSeed;
		_generatorOption.ItemSelected += _ => RefreshValidation();
		_advancedToggle.Toggled += RefreshAdvancedSectionVisibility;
		_confirmButton.Pressed += TryConfirm;
		panel.GetNode<Button>("Margin/VBox/Actions/CancelBtn").Pressed += () => CancelRequested?.Invoke();

		RefreshTexts();
	}

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public void Open(string suggestedName, WorldSettings? initialSettings = null)
	{
		var settings = initialSettings?.Clone() ?? WorldSettings.CreateDefault();
		BuildGeneratorOptions(settings.GeneratorId);
		_worldNameEdit.Text = string.IsNullOrWhiteSpace(suggestedName) ? "New World" : suggestedName.Trim();
		_seedEdit.Text = settings.Seed.ToString(CultureInfo.InvariantCulture);
		_climateEdit.Text = settings.ClimateId;
		_startSeasonEdit.Text = settings.StartSeasonId;
		_civilizationLevelEdit.Text = settings.CivilizationLevelId;
		_monsterDensityEdit.Text = settings.MonsterDensityPercent.ToString(CultureInfo.InvariantCulture);
		_npcDensityEdit.Text = settings.NpcDensityPercent.ToString(CultureInfo.InvariantCulture);
		_lootAbundanceEdit.Text = settings.LootAbundancePercent.ToString(CultureInfo.InvariantCulture);
		_nestIntensityEdit.Text = settings.NestIntensityPercent.ToString(CultureInfo.InvariantCulture);
		_weatherVolatilityEdit.Text = settings.WeatherVolatilityPercent.ToString(CultureInfo.InvariantCulture);
		var showAdvanced = ShouldShowAdvancedSettings(settings);
		_advancedToggle.ButtonPressed = showAdvanced;
		RefreshAdvancedSectionVisibility(showAdvanced);
		Visible = true;
		_worldNameEdit.GrabFocus();
		_worldNameEdit.SelectAll();
		RefreshValidation();
	}

	private void RandomizeSeed()
	{
		var seed = Random.Shared.Next();
		if (Random.Shared.Next(0, 2) == 0)
			seed = -seed;

		_seedEdit.Text = seed.ToString(CultureInfo.InvariantCulture);
	}

	private void RefreshAdvancedSectionVisibility(bool visible)
	{
		_advancedSection.Visible = visible;
	}

	public void Close()
	{
		Visible = false;
		_worldNameEdit.ReleaseFocus();
	}

	public void RefreshTexts()
	{
		var selectedGeneratorId = ResolveSelectedGeneratorId();
		BuildGeneratorOptions(selectedGeneratorId);
		RefreshValidation();
	}

	public bool HandleKeyInput(InputEventKey key)
	{
		var focusOwner = _panel.GetViewport().GuiGetFocusOwner();
		var result = ModalInputLogic.HandleKey(key, blockConfirm: focusOwner is Button or OptionButton);
		if (!result.Handled)
			return false;

		if (result.CancelRequested)
			CancelRequested?.Invoke();
		if (result.ConfirmRequested)
			TryConfirm();
		return true;
	}

	private void BuildGeneratorOptions(string? preferredGeneratorId)
	{
		_suppressGeneratorSelection = true;
		_generatorIds.Clear();
		_generatorOption.Clear();

		foreach (var generator in MapGenModule.AllGenerators.Values
			.Where(static generator => !string.Equals(generator.Id, "blank_floor", StringComparison.Ordinal))
			.OrderBy(static generator => generator.Name, StringComparer.CurrentCulture))
		{
			_generatorIds.Add(generator.Id);
			_generatorOption.AddItem(generator.Name);
		}

		var selectedId = !string.IsNullOrWhiteSpace(preferredGeneratorId) && _generatorIds.Contains(preferredGeneratorId)
			? preferredGeneratorId
			: _generatorIds.FirstOrDefault() ?? "room_corridor";
		var index = Math.Max(0, _generatorIds.IndexOf(selectedId));
		if (_generatorIds.Count > 0)
			_generatorOption.Select(index);
		_suppressGeneratorSelection = false;
	}

	private void RefreshValidation()
	{
		var validation = ValidateInputs();
		_confirmButton.Disabled = validation != null;
		_validationLabel.Visible = validation != null;
		_validationLabel.Text = validation ?? string.Empty;
	}

	private string? ValidateInputs()
	{
		if (string.IsNullOrWhiteSpace(_worldNameEdit.Text))
			return LocalizationService.T("ui.world_settings.validation.name_required");

		if (!int.TryParse(_seedEdit.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
			return LocalizationService.T("ui.world_settings.validation.seed_invalid");

		return TryParsePercent(_monsterDensityEdit.Text.Trim()) == null
			|| TryParsePercent(_npcDensityEdit.Text.Trim()) == null
			|| TryParsePercent(_lootAbundanceEdit.Text.Trim()) == null
			|| TryParsePercent(_nestIntensityEdit.Text.Trim()) == null
			|| TryParsePercent(_weatherVolatilityEdit.Text.Trim()) == null
			? LocalizationService.T("ui.world_settings.validation.percent_invalid")
			: null;
	}

	private void TryConfirm()
	{
		if (ValidateInputs() != null)
			return;

		ConfirmRequested?.Invoke(_worldNameEdit.Text.Trim(), BuildSettings());
	}

	private WorldSettings BuildSettings() => new()
	{
		Seed = int.Parse(_seedEdit.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture),
		GeneratorId = ResolveSelectedGeneratorId(),
		ClimateId = NormalizeText(_climateEdit.Text, "temperate"),
		StartSeasonId = NormalizeText(_startSeasonEdit.Text, "spring"),
		CivilizationLevelId = NormalizeText(_civilizationLevelEdit.Text, "frontier"),
		MonsterDensityPercent = TryParsePercent(_monsterDensityEdit.Text.Trim()) ?? 100,
		NpcDensityPercent = TryParsePercent(_npcDensityEdit.Text.Trim()) ?? 100,
		LootAbundancePercent = TryParsePercent(_lootAbundanceEdit.Text.Trim()) ?? 100,
		NestIntensityPercent = TryParsePercent(_nestIntensityEdit.Text.Trim()) ?? 100,
		WeatherVolatilityPercent = TryParsePercent(_weatherVolatilityEdit.Text.Trim()) ?? 100,
	};

	private string ResolveSelectedGeneratorId()
	{
		if (_suppressGeneratorSelection || _generatorIds.Count == 0)
			return "room_corridor";

		var index = (int)_generatorOption.Selected;
		if (index >= 0 && index < _generatorIds.Count)
			return _generatorIds[index];

		return _generatorIds[0];
	}

	private static bool ShouldShowAdvancedSettings(WorldSettings settings)
	{
		var defaults = WorldSettings.CreateDefault();
		return !string.Equals(settings.ClimateId, defaults.ClimateId, StringComparison.Ordinal)
			|| !string.Equals(settings.StartSeasonId, defaults.StartSeasonId, StringComparison.Ordinal)
			|| !string.Equals(settings.CivilizationLevelId, defaults.CivilizationLevelId, StringComparison.Ordinal)
			|| settings.MonsterDensityPercent != defaults.MonsterDensityPercent
			|| settings.NpcDensityPercent != defaults.NpcDensityPercent
			|| settings.LootAbundancePercent != defaults.LootAbundancePercent
			|| settings.NestIntensityPercent != defaults.NestIntensityPercent
			|| settings.WeatherVolatilityPercent != defaults.WeatherVolatilityPercent;
	}

	private static int? TryParsePercent(string raw) =>
		int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
			? parsed
			: null;

	private static string NormalizeText(string raw, string fallback)
	{
		var trimmed = raw.Trim();
		return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
	}
}
