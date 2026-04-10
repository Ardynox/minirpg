using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Module.Render;

namespace MiniRPG.Module;

public sealed class CharacterCreationModule : IModalInputLayer
{
	private const float PreviewCharacterScale = 1.35f;
	private const float PreviewHorizontalAnchor = 0.5f;
	private const float PreviewVerticalAnchor = 296f / 360f;

	private readonly PanelContainer _panel;
	private readonly Label _subtitleLabel;
	private readonly LineEdit _nameEdit;
	private readonly OptionButton _raceOption;
	private readonly OptionButton _professionOption;
	private readonly OptionButton _appearanceOption;
	private readonly Label _validationLabel;
	private readonly Label _summaryLabel;
	private readonly SubViewportContainer _previewViewportContainer;
	private readonly SubViewport _previewViewport;
	private readonly Node2D _previewRoot;
	private readonly FantasyCharacterAnimatable _previewCharacter;
	private readonly Button _confirmButton;

	private readonly List<string> _raceIds = [];
	private readonly List<string> _professionIds = [];
	private readonly List<string> _appearanceIds = [];

	private bool _suppressSelectionEvents;
	private string _selectedRaceId = string.Empty;
	private string _selectedProfessionId = string.Empty;
	private string _selectedAppearanceId = GameState.DefaultPlayerAppearanceId;
	private string _currentPreviewStateKey = string.Empty;
	private string? _worldName;

	public CharacterCreationModule(PanelContainer panel)
	{
		_panel = panel;
		_subtitleLabel = panel.GetNode<Label>("Margin/VBox/Subtitle");
		_nameEdit = panel.GetNode<LineEdit>("Margin/VBox/Body/Form/NameRow/NameEdit");
		_raceOption = panel.GetNode<OptionButton>("Margin/VBox/Body/Form/RaceRow/RaceOption");
		_professionOption = panel.GetNode<OptionButton>("Margin/VBox/Body/Form/ProfessionRow/ProfessionOption");
		_appearanceOption = panel.GetNode<OptionButton>("Margin/VBox/Body/Form/AppearanceRow/AppearanceOption");
		_validationLabel = panel.GetNode<Label>("Margin/VBox/Body/Form/Validation");
		_summaryLabel = panel.GetNode<Label>("Margin/VBox/Body/Form/SummaryPanel/Margin/VBox/Summary");
		_previewViewportContainer = panel.GetNode<SubViewportContainer>("Margin/VBox/Body/PreviewPanel/PreviewFrame/PreviewViewport");
		_previewViewport = _previewViewportContainer.GetNode<SubViewport>("PreviewViewport");
		_previewRoot = _previewViewport.GetNode<Node2D>("PreviewRoot");
		_confirmButton = panel.GetNode<Button>("Margin/VBox/Actions/ConfirmBtn");

		_previewViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		_previewViewportContainer.Resized += UpdatePreviewLayout;
		_previewCharacter = new FantasyCharacterAnimatable
		{
			Name = "PreviewCharacter",
			Scale = new Vector2(PreviewCharacterScale, PreviewCharacterScale),
		};
		_previewRoot.AddChild(_previewCharacter);
		UpdatePreviewLayout();
		_previewCharacter.Configure(PlayerAppearanceCatalog.GetOrDefault(GameState.DefaultPlayerAppearanceId).SheetDir, "Idle");
		_previewCharacter.Visible = true;
		_previewCharacter.SetMovementDirection(1, 0);

		_nameEdit.MaxLength = PlayerCreationOptions.MaxDisplayNameLength;
		_nameEdit.TextChanged += _ => RefreshDerivedUi();
		_nameEdit.TextSubmitted += _ => TryConfirm();
		_raceOption.ItemSelected += HandleRaceSelected;
		_professionOption.ItemSelected += HandleProfessionSelected;
		_appearanceOption.ItemSelected += HandleAppearanceSelected;
		_confirmButton.Pressed += TryConfirm;
		panel.GetNode<Button>("Margin/VBox/Actions/CancelBtn").Pressed += () => CancelRequested?.Invoke();

		RefreshTexts();
	}

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public event Action<PlayerCreationOptions>? ConfirmRequested;
	public event Action? CancelRequested;

	public void Open(PlayerCreationOptions? initialOptions = null, string? worldName = null)
	{
		_worldName = string.IsNullOrWhiteSpace(worldName) ? null : worldName.Trim();
		RefreshTexts();
		ApplyOptions(initialOptions ?? BuildDefaultOptions());
		Visible = true;
		UpdatePreviewLayout();
		_nameEdit.GrabFocus();
		_nameEdit.SelectAll();
	}

	public void Close()
	{
		Visible = false;
		_worldName = null;
		_nameEdit.ReleaseFocus();
	}

	public void RefreshTexts()
	{
		_subtitleLabel.Text = !string.IsNullOrWhiteSpace(_worldName)
			? LocalizationService.T("ui.character_creation.subtitle.world", ("world", _worldName))
			: LocalizationService.T("ui.character_creation.subtitle");
		var currentOptions = BuildCurrentOptions();
		ApplyOptions(currentOptions);
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

	private void ApplyOptions(PlayerCreationOptions options)
	{
		_suppressSelectionEvents = true;
		_nameEdit.Text = PlayerCreationOptions.NormalizeDisplayName(options.DisplayName);
		RefreshOptionLists(
			ResolveInitialRaceId(options),
			ResolveInitialProfessionId(options),
			PlayerAppearanceCatalog.NormalizeId(options.AppearanceId));
		SelectById(_raceOption, _raceIds, _selectedRaceId);
		SelectById(_professionOption, _professionIds, _selectedProfessionId);
		SelectById(_appearanceOption, _appearanceIds, _selectedAppearanceId);
		_suppressSelectionEvents = false;
		RefreshDerivedUi();
	}

	private void RefreshOptionLists(
		string? preferredRaceId = null,
		string? preferredProfessionId = null,
		string? preferredAppearanceId = null)
	{
		var selectedRaceId = preferredRaceId ?? ResolveCurrentRaceId();
		var selectedProfessionId = preferredProfessionId ?? ResolveCurrentProfessionId();
		var selectedAppearanceId = preferredAppearanceId ?? PlayerAppearanceCatalog.NormalizeId(_selectedAppearanceId);

		_raceIds.Clear();
		_raceOption.Clear();
		foreach (var race in PresetDB.Races.Values.OrderBy(static race => race.Name, StringComparer.CurrentCulture))
		{
			_raceIds.Add(race.Id);
			_raceOption.AddItem(race.Name);
		}

		_professionIds.Clear();
		_professionOption.Clear();
		foreach (var profession in PresetDB.Professions.Values.OrderBy(static profession => profession.Name, StringComparer.CurrentCulture))
		{
			_professionIds.Add(profession.Id);
			_professionOption.AddItem(profession.Name);
		}

		_appearanceIds.Clear();
		_appearanceOption.Clear();
		foreach (var appearance in PlayerAppearanceCatalog.All)
		{
			_appearanceIds.Add(appearance.Id);
			_appearanceOption.AddItem(LocalizationService.TOrFallback(
				$"data.player_appearance.{appearance.Id}.name",
				appearance.DisplayName));
		}

		_selectedRaceId = NormalizeSelection(selectedRaceId, _raceIds) ?? _raceIds.FirstOrDefault() ?? string.Empty;
		_selectedProfessionId = NormalizeSelection(selectedProfessionId, _professionIds) ?? _professionIds.FirstOrDefault() ?? string.Empty;
		_selectedAppearanceId = NormalizeSelection(selectedAppearanceId, _appearanceIds) ?? GameState.DefaultPlayerAppearanceId;
	}

	private void RefreshDerivedUi()
	{
		var canConfirm = CanConfirm();
		_confirmButton.Disabled = !canConfirm;
		_validationLabel.Visible = !canConfirm;
		_summaryLabel.Text = BuildSummary();
		UpdatePreview();
	}

	private bool CanConfirm() =>
		!string.IsNullOrWhiteSpace(PlayerCreationOptions.NormalizeDisplayName(_nameEdit.Text))
		&& !string.IsNullOrWhiteSpace(_selectedRaceId)
		&& !string.IsNullOrWhiteSpace(_selectedProfessionId)
		&& !string.IsNullOrWhiteSpace(_selectedAppearanceId);

	private string BuildSummary()
	{
		var displayName = PlayerCreationOptions.NormalizeDisplayName(_nameEdit.Text);
		if (string.IsNullOrWhiteSpace(displayName))
			displayName = LocalizationService.T("ui.character_creation.summary.pending_name");

		var raceName = PresetDB.Races.GetValueOrDefault(_selectedRaceId)?.Name ?? _selectedRaceId;
		var professionName = PresetDB.Professions.GetValueOrDefault(_selectedProfessionId)?.Name ?? _selectedProfessionId;
		var appearance = PlayerAppearanceCatalog.GetOrDefault(_selectedAppearanceId);
		var appearanceName = LocalizationService.TOrFallback(
			$"data.player_appearance.{appearance.Id}.name",
			appearance.DisplayName);

		var tags = BuildTagSummary();
		return LocalizationService.T("ui.character_creation.summary.body",
			("name", displayName),
			("race", raceName),
			("profession", professionName),
			("appearance", appearanceName),
			("traits", tags));
	}

	private string BuildTagSummary()
	{
		var mergedTags = new Dictionary<string, int>(StringComparer.Ordinal);
		if (PresetDB.Races.TryGetValue(_selectedRaceId, out var race))
		{
			foreach (var (tag, value) in race.Tags)
				mergedTags[tag] = mergedTags.GetValueOrDefault(tag) + value;
		}

		if (PresetDB.Professions.TryGetValue(_selectedProfessionId, out var profession))
		{
			foreach (var (tag, value) in profession.Tags)
				mergedTags[tag] = mergedTags.GetValueOrDefault(tag) + value;
		}

		if (mergedTags.Count == 0)
			return LocalizationService.T("ui.character_creation.summary.no_traits");

		return string.Join("  ", mergedTags
			.OrderByDescending(static entry => entry.Value)
			.ThenBy(static entry => entry.Key, StringComparer.Ordinal)
			.Select(static entry => $"{GameLocalizer.LocalizeTagKey(entry.Key)} +{entry.Value}"));
	}

	private void UpdatePreview()
	{
		var appearance = PlayerAppearanceCatalog.GetOrDefault(_selectedAppearanceId);
		var previewStateKey = $"{_selectedRaceId}|{appearance.Id}";
		if (_currentPreviewStateKey == previewStateKey)
			return;

		_currentPreviewStateKey = previewStateKey;
		_previewCharacter.Configure(appearance.SheetDir, appearance.DefaultAnim);
		_previewCharacter.Visible = true;
		_previewCharacter.SetMovementDirection(1, 0);
	}

	private void UpdatePreviewLayout()
	{
		var viewportSize = _previewViewport.Size;
		if (viewportSize.X <= 0 || viewportSize.Y <= 0)
			return;

		_previewCharacter.Position = new Vector2(
			viewportSize.X * PreviewHorizontalAnchor,
			viewportSize.Y * PreviewVerticalAnchor);
	}

	private void TryConfirm()
	{
		if (!CanConfirm())
			return;

		ConfirmRequested?.Invoke(BuildCurrentOptions());
	}

	private PlayerCreationOptions BuildCurrentOptions() => new()
	{
		DisplayName = PlayerCreationOptions.NormalizeDisplayName(_nameEdit.Text),
		RaceId = _selectedRaceId,
		ProfessionId = _selectedProfessionId,
		AppearanceId = _selectedAppearanceId,
	};

	private PlayerCreationOptions BuildDefaultOptions()
	{
		var defaults = PlayerCreationOptions.CreateDefault();
		return new PlayerCreationOptions
		{
			DisplayName = defaults.DisplayName,
			RaceId = ResolveInitialRaceId(defaults),
			ProfessionId = ResolveInitialProfessionId(defaults),
			AppearanceId = PlayerAppearanceCatalog.NormalizeId(defaults.AppearanceId),
		};
	}

	private string ResolveInitialRaceId(PlayerCreationOptions options)
	{
		if (!string.IsNullOrWhiteSpace(options.RaceId) && PresetDB.Races.ContainsKey(options.RaceId))
			return options.RaceId;

		return PresetDB.Races.Values
			.OrderBy(static race => race.Name, StringComparer.CurrentCulture)
			.FirstOrDefault()?.Id
			?? PlayerCreationOptions.CreateDefault().RaceId;
	}

	private string ResolveInitialProfessionId(PlayerCreationOptions options)
	{
		if (!string.IsNullOrWhiteSpace(options.ProfessionId) && PresetDB.Professions.ContainsKey(options.ProfessionId))
			return options.ProfessionId;

		return PresetDB.Professions.Values
			.OrderBy(static profession => profession.Name, StringComparer.CurrentCulture)
			.FirstOrDefault()?.Id
			?? string.Empty;
	}

	private string ResolveCurrentRaceId() => NormalizeSelection(_selectedRaceId, _raceIds) ?? string.Empty;

	private string ResolveCurrentProfessionId() => NormalizeSelection(_selectedProfessionId, _professionIds) ?? string.Empty;

	private static string? NormalizeSelection(string? id, IReadOnlyCollection<string> ids)
	{
		if (string.IsNullOrWhiteSpace(id))
			return null;

		return ids.Contains(id) ? id : null;
	}

	private void SelectById(OptionButton option, IReadOnlyList<string> ids, string? id)
	{
		if (ids.Count == 0)
			return;

		var resolvedId = NormalizeSelection(id, ids) ?? ids[0];
		var index = 0;
		for (var i = 0; i < ids.Count; i++)
		{
			if (ids[i] != resolvedId)
				continue;

			index = i;
			break;
		}
		option.Select(index);
	}

	private void HandleRaceSelected(long index)
	{
		if (_suppressSelectionEvents)
			return;

		_selectedRaceId = ResolveSelection(index, _raceIds, _selectedRaceId);
		RefreshDerivedUi();
	}

	private void HandleProfessionSelected(long index)
	{
		if (_suppressSelectionEvents)
			return;

		_selectedProfessionId = ResolveSelection(index, _professionIds, _selectedProfessionId);
		RefreshDerivedUi();
	}

	private void HandleAppearanceSelected(long index)
	{
		if (_suppressSelectionEvents)
			return;

		_selectedAppearanceId = ResolveSelection(index, _appearanceIds, _selectedAppearanceId);
		RefreshDerivedUi();
	}

	private static string ResolveSelection(long index, IReadOnlyList<string> ids, string fallback)
	{
		if (index >= 0 && index < ids.Count)
			return ids[(int)index];

		return ids.Count > 0 ? ids[0] : fallback;
	}
}
