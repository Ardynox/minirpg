using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Module.Panel;

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
	private readonly TextureRect _raceIconPreview;
	private readonly OptionButton _professionOption;
	private readonly TextureRect _professionIconPreview;
	private readonly Button _customizeButton;
	private readonly Label _validationLabel;
	private readonly Label _summaryLabel;
	private readonly SubViewportContainer _previewViewportContainer;
	private readonly SubViewport _previewViewport;
	private readonly Node2D _previewRoot;
	private readonly Sprite2D _mapSpritePreview;
	private readonly Sprite2D _portraitPreviewSprite;
	private readonly PortraitComposer _portraitComposer = new();
	private readonly Button _confirmButton;
	private readonly Label _starterKitTitleLabel;
	private readonly Button _resetStarterKitBtn;
	private readonly CheckBox _starterKitDebugCheck;
	private readonly VBoxContainer _starterKitListContainer;

	private readonly List<string> _raceIds = [];
	private readonly List<string> _professionIds = [];
	private readonly Dictionary<string, int> _starterKitCounts = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Button> _starterKitMinusButtons = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Button> _starterKitPlusButtons = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Label> _starterKitCountLabels = new(StringComparer.Ordinal);

	private bool _suppressSelectionEvents;
	private string _selectedRaceId = string.Empty;
	private string _selectedProfessionId = string.Empty;
	private string? _worldName;
	private FaceCustomizationData _faceCustomization = FaceCustomizationData.CreateDefault();
	private string _currentPortraitCacheKey = string.Empty;
	private bool _starterKitDirty;
	private bool _starterKitDebugUnlock;

	public CharacterCreationModule(PanelContainer panel)
	{
		_panel = panel;
		_subtitleLabel = panel.GetNode<Label>("Margin/VBox/Subtitle");
		_nameEdit = panel.GetNode<LineEdit>("Margin/VBox/Body/Form/NameRow/NameEdit");
		_raceIconPreview = panel.GetNode<TextureRect>("Margin/VBox/Body/Form/RaceRow/RacePickerRow/RaceIcon");
		_raceOption = panel.GetNode<OptionButton>("Margin/VBox/Body/Form/RaceRow/RacePickerRow/RaceOption");
		_professionIconPreview = panel.GetNode<TextureRect>("Margin/VBox/Body/Form/ProfessionRow/ProfessionPickerRow/ProfessionIcon");
		_professionOption = panel.GetNode<OptionButton>("Margin/VBox/Body/Form/ProfessionRow/ProfessionPickerRow/ProfessionOption");
		_customizeButton = panel.GetNode<Button>("Margin/VBox/Body/Form/CustomizeRow/CustomizeBtn");
		_validationLabel = panel.GetNode<Label>("Margin/VBox/Body/Form/Validation");
		_summaryLabel = panel.GetNode<Label>("Margin/VBox/Body/Form/SummaryPanel/Margin/VBox/Summary");
		_previewViewportContainer = panel.GetNode<SubViewportContainer>("Margin/VBox/Body/PreviewPanel/PreviewFrame/PreviewViewport");
		_previewViewport = _previewViewportContainer.GetNode<SubViewport>("PreviewViewport");
		_previewRoot = _previewViewport.GetNode<Node2D>("PreviewRoot");
		_confirmButton = panel.GetNode<Button>("Margin/VBox/Actions/ConfirmBtn");
		_starterKitTitleLabel = panel.GetNode<Label>("Margin/VBox/Body/Form/StarterKitRow/StarterKitLabel");
		_resetStarterKitBtn = panel.GetNode<Button>("Margin/VBox/Body/Form/StarterKitRow/StarterKitToolbar/ResetToProfessionBtn");
		_starterKitDebugCheck = panel.GetNode<CheckBox>("Margin/VBox/Body/Form/StarterKitRow/StarterKitToolbar/DebugUnlockCheck");
		_starterKitListContainer = panel.GetNode<VBoxContainer>("Margin/VBox/Body/Form/StarterKitRow/StarterKitScroll/StarterKitList");

		_previewViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		_previewViewportContainer.Resized += UpdatePreviewLayout;
		_mapSpritePreview = new Sprite2D
		{
			Name = "MapSpritePreview",
			Centered = true,
			Visible = true,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			RegionEnabled = true,
		};
		_previewRoot.AddChild(_mapSpritePreview);
		_portraitPreviewSprite = new Sprite2D
		{
			Name = "PortraitPreviewSprite",
			Centered = true,
			Visible = true,
			TextureFilter = CanvasItem.TextureFilterEnum.Linear,
		};
		_previewRoot.AddChild(_portraitPreviewSprite);
		UpdatePreviewLayout();
		ApplyMapSpritePreview();
		RefreshPortraitPreview();

		_nameEdit.MaxLength = PlayerCreationOptions.MaxDisplayNameLength;
		_nameEdit.TextChanged += _ => RefreshDerivedUi();
		_nameEdit.TextSubmitted += _ => TryConfirm();
		_raceOption.ItemSelected += HandleRaceSelected;
		_professionOption.ItemSelected += HandleProfessionSelected;
		_customizeButton.Pressed += () => OpenFaceCustomizationRequested?.Invoke(_faceCustomization.Clone());
		_confirmButton.Pressed += TryConfirm;
		panel.GetNode<Button>("Margin/VBox/Actions/CancelBtn").Pressed += () => CancelRequested?.Invoke();

		_resetStarterKitBtn.Pressed += HandleResetStarterKitToProfession;
		_starterKitDebugCheck.Toggled += HandleStarterKitDebugToggled;
		BuildStarterKitRows();

		RefreshTexts();
	}

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public event Action<PlayerCreationOptions>? ConfirmRequested;
	public event Action? CancelRequested;
	/// <summary>
	/// 用户点了「捏脸」按钮，请求打开独立的 <see cref="MiniRPG.Module.Panel.CharacterCustomizationPanelModule"/>。
	/// 参数是当前主面板上的 <see cref="FaceCustomizationData"/> 工作副本（克隆，调用方可自由修改）。
	/// </summary>
	public event Action<FaceCustomizationData>? OpenFaceCustomizationRequested;

	public void Open(PlayerCreationOptions? initialOptions = null, string? worldName = null)
	{
		_worldName = string.IsNullOrWhiteSpace(worldName) ? null : worldName.Trim();
		RefreshTexts();
		var options = initialOptions ?? BuildDefaultOptions();
		_faceCustomization = options.ResolveFaceCustomization();
		ApplyOptions(options);
		Visible = true;
		UpdatePreviewLayout();
		ApplyMapSpritePreview();
		RefreshPortraitPreview();
		_nameEdit.GrabFocus();
		_nameEdit.SelectAll();
	}

	public void Close()
	{
		Visible = false;
		_worldName = null;
		_nameEdit.ReleaseFocus();
	}

	/// <summary>
	/// 接收来自 <see cref="MiniRPG.Module.Panel.CharacterCustomizationPanelModule"/> 「应用」的捏脸结果。
	/// </summary>
	public void ApplyFaceCustomization(FaceCustomizationData face)
	{
		_faceCustomization = face.Clone();
		ApplyMapSpritePreview();
		RefreshPortraitPreview();
		RefreshDerivedUi();
	}

	public FaceCustomizationData GetCurrentFaceCustomization() => _faceCustomization.Clone();

	public void RefreshTexts()
	{
		_subtitleLabel.Text = !string.IsNullOrWhiteSpace(_worldName)
			? LocalizationService.T("ui.character_creation.subtitle.world", ("world", _worldName))
			: LocalizationService.T("ui.character_creation.subtitle");
		_customizeButton.Text = LocalizationService.T("ui.character_creation.open_face_editor");
		_starterKitTitleLabel.Text = LocalizationService.T("ui.character_creation.starter_kit.title");
		_resetStarterKitBtn.Text = LocalizationService.T("ui.character_creation.starter_kit.reset_to_profession");
		_starterKitDebugCheck.Text = LocalizationService.T("ui.character_creation.starter_kit.debug_unlock");
		RefreshStarterKitItemNames();
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
			ResolveInitialProfessionId(options));
		SelectById(_raceOption, _raceIds, _selectedRaceId);
		SelectById(_professionOption, _professionIds, _selectedProfessionId);
		_starterKitDebugUnlock = options.StartingItemsDebugOverride;
		_starterKitDebugCheck.SetPressedNoSignal(_starterKitDebugUnlock);
		if (options.StartingItems != null)
		{
			LoadStarterKitFromList(options.StartingItems);
			_starterKitDirty = true;
		}
		else
		{
			LoadStarterKitFromProfession(_selectedProfessionId);
			_starterKitDirty = false;
		}
		_suppressSelectionEvents = false;
		RefreshDerivedUi();
	}

	private void RefreshOptionLists(
		string? preferredRaceId = null,
		string? preferredProfessionId = null)
	{
		var selectedRaceId = preferredRaceId ?? ResolveCurrentRaceId();
		var selectedProfessionId = preferredProfessionId ?? ResolveCurrentProfessionId();

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

		_selectedRaceId = NormalizeSelection(selectedRaceId, _raceIds) ?? _raceIds.FirstOrDefault() ?? string.Empty;
		_selectedProfessionId = NormalizeSelection(selectedProfessionId, _professionIds) ?? _professionIds.FirstOrDefault() ?? string.Empty;
	}

	private void RefreshDerivedUi()
	{
		var canConfirm = CanConfirm();
		_confirmButton.Disabled = !canConfirm;
		_validationLabel.Visible = !canConfirm;
		_summaryLabel.Text = BuildSummary();
		RefreshStarterKitButtons();
		RefreshRaceProfessionIcons();
		UpdatePreview();
	}

	private void RefreshRaceProfessionIcons()
	{
		var raceTex = string.IsNullOrEmpty(_selectedRaceId)
			? null
			: PlaceholderUiIconCatalog.TryLoadTexture(PlaceholderUiIconCatalog.PathForRace(_selectedRaceId));
		_raceIconPreview.Texture = raceTex;
		_raceIconPreview.Visible = raceTex != null;

		var profTex = string.IsNullOrEmpty(_selectedProfessionId)
			? null
			: PlaceholderUiIconCatalog.TryLoadTexture(PlaceholderUiIconCatalog.PathForProfession(_selectedProfessionId));
		_professionIconPreview.Texture = profTex;
		_professionIconPreview.Visible = profTex != null;
	}

	private bool CanConfirm() =>
		!string.IsNullOrWhiteSpace(PlayerCreationOptions.NormalizeDisplayName(_nameEdit.Text))
		&& !string.IsNullOrWhiteSpace(_selectedRaceId)
		&& !string.IsNullOrWhiteSpace(_selectedProfessionId);

	private string BuildSummary()
	{
		var displayName = PlayerCreationOptions.NormalizeDisplayName(_nameEdit.Text);
		if (string.IsNullOrWhiteSpace(displayName))
			displayName = LocalizationService.T("ui.character_creation.summary.pending_name");

		var raceName = PresetDB.Races.GetValueOrDefault(_selectedRaceId)?.Name ?? _selectedRaceId;
		var professionName = PresetDB.Professions.GetValueOrDefault(_selectedProfessionId)?.Name ?? _selectedProfessionId;
		var appearanceName = LocalizationService.T("ui.character_creation.summary.face_custom");

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
		ApplyMapSpritePreview();
		RefreshPortraitPreview();
	}

	/// <summary>
	/// 用 <see cref="MapSpriteRuntimeFactory"/> 按当前捏脸数据合成 8 方向 sheet，
	/// 在预览框里显示南方向（第 0 行）那一帧——和地图上玩家小人正面朝向时是同一张。
	/// </summary>
	private void ApplyMapSpritePreview()
	{
		var sheet = MapSpriteRuntimeFactory.Instance.GetOrBuild(_faceCustomization);
		_mapSpritePreview.Texture = sheet;
		// 第 0 行 = 北向（背面），第 4 行 = 南向（正面）。预览给玩家看用南向更符合直觉。
		const int southRowIndex = 4;
		_mapSpritePreview.RegionRect = new Rect2(
			0,
			southRowIndex * MapSpriteRuntimeFactory.ExportSize,
			MapSpriteRuntimeFactory.ExportSize,
			MapSpriteRuntimeFactory.ExportSize);
		_mapSpritePreview.Visible = true;
	}

	private void RefreshPortraitPreview()
	{
		var nextKey = _faceCustomization.ComputeCacheKey();
		if (_currentPortraitCacheKey == nextKey && _portraitPreviewSprite.Texture != null)
			return;

		_currentPortraitCacheKey = nextKey;
		_portraitPreviewSprite.Texture = _portraitComposer.Compose(_faceCustomization);
		_portraitPreviewSprite.Visible = true;
	}

	private void UpdatePreviewLayout()
	{
		var viewportSize = _previewViewport.Size;
		if (viewportSize.X <= 0 || viewportSize.Y <= 0)
			return;

		// 肖像居中偏左，地图 sprite 居中偏右——两块并排显示让玩家同时看到「头像」与「地图小人」。
		_portraitPreviewSprite.Position = new Vector2(
			viewportSize.X * 0.32f,
			viewportSize.Y * PreviewVerticalAnchor);
		_portraitPreviewSprite.Scale = ComputePortraitScale(viewportSize);

		_mapSpritePreview.Position = new Vector2(
			viewportSize.X * 0.72f,
			viewportSize.Y * PreviewVerticalAnchor);
		_mapSpritePreview.Scale = new Vector2(PreviewCharacterScale, PreviewCharacterScale);
	}

	private static Vector2 ComputePortraitScale(Vector2 viewportSize)
	{
		var available = MathF.Min(viewportSize.X * 0.85f, viewportSize.Y * 0.85f);
		var scale = available / PortraitComposer.CanvasWidth;
		if (scale < 0.4f)
			scale = 0.4f;
		return new Vector2(scale, scale);
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
		FaceCustomization = _faceCustomization.Clone(),
		StartingItems = BuildStartingItems(),
		StartingItemsDebugOverride = _starterKitDebugUnlock,
	};

	/// <summary>
	/// 把 UI 上的 starter kit 计数转成 <see cref="PlayerStartingItem"/> 列表。
	/// 玩家没改过（!_starterKitDirty）→ 返回 null，让 <c>StarterKitResolver</c> 走"职业默认包"路径；
	/// 改过 → 哪怕全置 0 也返回非 null 列表（玩家明确选了"什么都不带"）。
	/// </summary>
	private IReadOnlyList<PlayerStartingItem>? BuildStartingItems()
	{
		if (!_starterKitDirty)
			return null;

		var entries = new List<PlayerStartingItem>(_starterKitCounts.Count);
		foreach (var (itemId, count) in _starterKitCounts)
		{
			if (count <= 0) continue;
			entries.Add(new PlayerStartingItem(itemId, count));
		}
		return entries;
	}

	private PlayerCreationOptions BuildDefaultOptions()
	{
		var defaults = PlayerCreationOptions.CreateDefault();
		return new PlayerCreationOptions
		{
			DisplayName = defaults.DisplayName,
			RaceId = ResolveInitialRaceId(defaults),
			ProfessionId = ResolveInitialProfessionId(defaults),
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
		// 玩家没动过 starter kit → 自动跟随新职业的默认包；动过 → 保持玩家编辑结果。
		if (!_starterKitDirty)
			LoadStarterKitFromProfession(_selectedProfessionId);
		RefreshDerivedUi();
	}

	private static string ResolveSelection(long index, IReadOnlyList<string> ids, string fallback)
	{
		if (index >= 0 && index < ids.Count)
			return ids[(int)index];

		return ids.Count > 0 ? ids[0] : fallback;
	}

	// ── 初始携带物品 UI ─────────────────────────────────────

	/// <summary>
	/// 一次性按 <see cref="StarterKitCatalog"/> 的分类铺出每个白名单 itemId 的 [−] N [+] 行。
	/// 行内 Button / Label 引用挂到 <c>_starterKitMinus/Plus/CountLabel</c> 字典里供 Refresh 复用。
	/// 该方法只在构造函数里调一次：分类与白名单是配置静态数据，运行时不会变。
	/// </summary>
	private void BuildStarterKitRows()
	{
		foreach (var child in _starterKitListContainer.GetChildren())
			child.QueueFree();
		_starterKitMinusButtons.Clear();
		_starterKitPlusButtons.Clear();
		_starterKitCountLabels.Clear();

		foreach (var category in StarterKitCatalog.GetCategories())
		{
			var categoryHeader = new Label
			{
				Name = $"Category_{category.Id}",
				Text = LocalizeStarterKitCategory(category.Id, category.Limit),
			};
			categoryHeader.AddThemeFontSizeOverride("font_size", 12);
			_starterKitListContainer.AddChild(categoryHeader);

			foreach (var itemId in category.Items)
			{
				var row = new HBoxContainer
				{
					Name = $"Row_{itemId}",
				};
				row.AddThemeConstantOverride("separation", 6);
				_starterKitListContainer.AddChild(row);

				var nameLabel = new Label
				{
					Name = "ItemName",
					Text = ResolveItemDisplayName(itemId),
					SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				};
				row.AddChild(nameLabel);

				var capturedId = itemId;
				var minusBtn = new Button { Name = "Minus", Text = "−" };
				minusBtn.Pressed += () => HandleStarterKitDecrement(capturedId);
				row.AddChild(minusBtn);

				var countLabel = new Label
				{
					Name = "Count",
					Text = "0",
					CustomMinimumSize = new Vector2(28, 0),
					HorizontalAlignment = HorizontalAlignment.Center,
				};
				row.AddChild(countLabel);

				var plusBtn = new Button { Name = "Plus", Text = "+" };
				plusBtn.Pressed += () => HandleStarterKitIncrement(capturedId);
				row.AddChild(plusBtn);

				_starterKitMinusButtons[itemId] = minusBtn;
				_starterKitPlusButtons[itemId] = plusBtn;
				_starterKitCountLabels[itemId] = countLabel;
				_starterKitCounts.TryAdd(itemId, 0);
			}
		}
	}

	private void RefreshStarterKitItemNames()
	{
		foreach (var (itemId, label) in _starterKitCountLabels)
		{
			var row = label.GetParent() as HBoxContainer;
			if (row == null) continue;
			if (row.GetNodeOrNull<Label>("ItemName") is { } nameLabel)
				nameLabel.Text = ResolveItemDisplayName(itemId);
		}

		foreach (var category in StarterKitCatalog.GetCategories())
		{
			if (_starterKitListContainer.GetNodeOrNull<Label>($"Category_{category.Id}") is { } header)
				header.Text = LocalizeStarterKitCategory(category.Id, category.Limit);
		}
	}

	private void RefreshStarterKitButtons()
	{
		var categoryUsed = ComputeCategoryUsage();
		foreach (var (itemId, countLabel) in _starterKitCountLabels)
		{
			var count = _starterKitCounts.GetValueOrDefault(itemId);
			countLabel.Text = count.ToString();

			if (_starterKitMinusButtons.TryGetValue(itemId, out var minus))
				minus.Disabled = count <= 0;

			if (_starterKitPlusButtons.TryGetValue(itemId, out var plus))
			{
				bool atEntryCap = count >= StarterKitResolver.PerEntryMaxCount;
				bool categoryFull = false;
				if (!_starterKitDebugUnlock)
				{
					var cat = StarterKitCatalog.GetCategoryFor(itemId);
					if (cat != null && categoryUsed.GetValueOrDefault(cat.Id) >= cat.Limit)
						categoryFull = true;
				}
				plus.Disabled = atEntryCap || categoryFull;
			}
		}
	}

	private Dictionary<string, int> ComputeCategoryUsage()
	{
		var used = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var (itemId, count) in _starterKitCounts)
		{
			if (count <= 0) continue;
			var cat = StarterKitCatalog.GetCategoryFor(itemId);
			if (cat == null) continue;
			used[cat.Id] = used.GetValueOrDefault(cat.Id) + count;
		}
		return used;
	}

	private void HandleStarterKitDecrement(string itemId)
	{
		var current = _starterKitCounts.GetValueOrDefault(itemId);
		if (current <= 0) return;
		_starterKitCounts[itemId] = current - 1;
		_starterKitDirty = true;
		RefreshDerivedUi();
	}

	private void HandleStarterKitIncrement(string itemId)
	{
		var cat = StarterKitCatalog.GetCategoryFor(itemId);
		if (cat == null) return;

		var current = _starterKitCounts.GetValueOrDefault(itemId);
		if (current >= StarterKitResolver.PerEntryMaxCount) return;

		if (!_starterKitDebugUnlock)
		{
			var used = ComputeCategoryUsage().GetValueOrDefault(cat.Id);
			if (used >= cat.Limit) return;
		}

		_starterKitCounts[itemId] = current + 1;
		_starterKitDirty = true;
		RefreshDerivedUi();
	}

	private void HandleResetStarterKitToProfession()
	{
		LoadStarterKitFromProfession(_selectedProfessionId);
		_starterKitDirty = false;
		RefreshDerivedUi();
	}

	private void HandleStarterKitDebugToggled(bool pressed)
	{
		_starterKitDebugUnlock = pressed;
		RefreshDerivedUi();
	}

	private void LoadStarterKitFromList(IEnumerable<PlayerStartingItem> entries)
	{
		ClearStarterKitCounts();
		foreach (var entry in entries)
		{
			if (entry == null) continue;
			if (string.IsNullOrWhiteSpace(entry.ItemId)) continue;
			if (!_starterKitCounts.ContainsKey(entry.ItemId)) continue;
			var capped = Math.Min(StarterKitResolver.PerEntryMaxCount, Math.Max(0, entry.Count));
			_starterKitCounts[entry.ItemId] = capped;
		}
	}

	private void LoadStarterKitFromProfession(string professionId)
	{
		var defaultEntries = StarterKitResolver.ResolveDefault(professionId);
		LoadStarterKitFromList(defaultEntries);
	}

	private void ClearStarterKitCounts()
	{
		foreach (var key in _starterKitCounts.Keys.ToList())
			_starterKitCounts[key] = 0;
	}

	private static string LocalizeStarterKitCategory(string categoryId, int limit) =>
		LocalizationService.T(
			"ui.character_creation.starter_kit.category",
			("name", LocalizationService.T($"ui.character_creation.starter_kit.category.{categoryId}")),
			("limit", limit.ToString()));

	private static string ResolveItemDisplayName(string itemId)
	{
		if (PresetDB.Items.TryGetValue(itemId, out var preset)
			&& !string.IsNullOrWhiteSpace(preset.Name))
		{
			return preset.Name;
		}
		return itemId;
	}
}
