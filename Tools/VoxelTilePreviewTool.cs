using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using MiniRPG.Module.Render;

namespace MiniRPG.Tools;

public partial class VoxelTilePreviewTool : Control
{
	private const int BrowserPageSize = 24;
	private const float PreviewScale = 2.0f;

	private const float LeftDarken = 0.65f;
	private const float RightDarken = 0.80f;
	private const float WallLeftSideDarken = 0.52f;
	private const float WallRightSideDarken = 0.68f;

	private const float WallTopEdgeStrength = 0.30f;
	private const float SolidTopEdgeStrength = 0.22f;
	private const float NonSolidTopEdgeStrength = 0.12f;
	private const float WallSideEdgeStrength = 0.20f;
	private const float SolidSideEdgeStrength = 0.12f;
	private const float NonSolidSideEdgeStrength = 0.06f;

	private static readonly Dictionary<string, Color> TerrainFallbackColors = new(StringComparer.OrdinalIgnoreCase)
	{
		[Terrains.Floor] = new("#8a8678"),
		[Terrains.Grass] = new("#4a5c2d"),
		[Terrains.GrassBlock] = new("#6d5c33"),
		[Terrains.Dirt] = new("#7a5a32"),
		[Terrains.Stone] = new("#6f7074"),
		[Terrains.Sand] = new("#c7b37a"),
		[Terrains.Snow] = new("#d7d9dd"),
		[Terrains.Gravel] = new("#80828a"),
		[Terrains.WallSoil] = new("#6e4b2c"),
		[Terrains.WallStone] = new("#5d6068"),
		[Terrains.WallGranite] = new("#777a83"),
		[Terrains.WallObsidian] = new("#2b2731"),
		[Terrains.WallIron] = new("#6f8aa0"),
		[Terrains.Mountain] = new("#5c6068"),
		[Terrains.Tree] = new("#5a4023"),
		[Terrains.Water] = new("#3b6690"),
		[Terrains.Lava] = new("#b14422"),
		[Terrains.Swamp] = new("#5a5f37"),
		[Terrains.Marsh] = new("#6b6e4b"),
		[Terrains.Ice] = new("#8aa4c0"),
		[Terrains.Fungus] = new("#695976"),
		[Terrains.CrystalVein] = new("#6f7fb1"),
		[Terrains.OreCoal] = new("#3e4046"),
		[Terrains.OreIron] = new("#657583"),
		[Terrains.OreCopper] = new("#905a44"),
		[Terrains.OreGold] = new("#b69132"),
		[Terrains.OreCrystal] = new("#8d7ab4"),
		[Terrains.Rubble] = new("#57504a"),
	};

	private enum FaceSlot
	{
		Top,
		LeftSide,
		RightSide,
	}

	private readonly Dictionary<string, Texture2D?> _textureCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Image?> _imageCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, PzTileCatalogEntry> _catalogByPath = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, VoxelTileMappingEntry> _mappings = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<TerrainDef> _terrains = [];
	private readonly List<PzTileCatalogEntry> _filteredEntries = [];

	private PzTileCatalogDocument _catalog = new();
	private FaceSlot _activeSlot = FaceSlot.Top;
	private int _browserPage;
	private int _selectedTerrainIndex;
	private bool _isRefreshingUi;

	private ItemList _terrainList = null!;
	private Label _terrainListSummaryLabel = null!;
	private Label _mappingStatusLabel = null!;
	private Label _globalStatusLabel = null!;
	private Label _browserSummaryLabel = null!;
	private Label _pageLabel = null!;
	private LineEdit _searchEdit = null!;
	private OptionButton _groupFilter = null!;
	private OptionButton _tagFilter = null!;
	private CheckBox _mappingEligibleOnlyCheck = null!;
	private VBoxContainer _browserResults = null!;
	private Button _previousPageButton = null!;
	private Button _nextPageButton = null!;
	private ColorPickerButton _sideColorPicker = null!;
	private Button _applyColorButton = null!;
	private Button _clearSlotButton = null!;
	private Button _saveButton = null!;

	private Button _topSlotButton = null!;
	private Button _leftSlotButton = null!;
	private Button _rightSlotButton = null!;
	private TextureRect _topSlotPreview = null!;
	private TextureRect _leftSlotPreview = null!;
	private TextureRect _rightSlotPreview = null!;
	private Label _topSlotTitleLabel = null!;
	private Label _leftSlotTitleLabel = null!;
	private Label _rightSlotTitleLabel = null!;
	private Label _topSlotMetaLabel = null!;
	private Label _leftSlotMetaLabel = null!;
	private Label _rightSlotMetaLabel = null!;

	private Sprite2D _topSprite = null!;
	private Sprite2D _leftSprite = null!;
	private Sprite2D _rightSprite = null!;

	public override void _Ready()
	{
		LoadData();
		BuildUi();
		RefreshFilterOptions();
		RefreshBrowser(resetPage: true);
		RebuildTerrainList();
		if (_terrains.Count > 0)
			SelectTerrain(0);
		else
			RefreshSelectionState();
	}

	private void BuildUi()
	{
		var background = new ColorRect
		{
			Color = new Color(0.06f, 0.07f, 0.10f),
		};
		background.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(background);

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_top", 12);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_bottom", 12);
		AddChild(margin);

		var root = new HBoxContainer();
		root.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		root.SizeFlagsVertical = SizeFlags.ExpandFill;
		root.AddThemeConstantOverride("separation", 12);
		margin.AddChild(root);

		BuildLeftPanel(root);
		BuildCenterPanel(root);
		BuildRightPanel(root);
		UpdateActiveSlotUi();
		UpdateSideActionUi();
	}

	private void BuildLeftPanel(HBoxContainer root)
	{
		var panel = new PanelContainer
		{
			CustomMinimumSize = new Vector2(300, 0),
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		root.AddChild(panel);

		var content = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		content.AddThemeConstantOverride("separation", 8);
		panel.AddChild(content);

		content.AddChild(new Label
		{
			Text = "Terrain 映射",
		});

		_terrainListSummaryLabel = new Label
		{
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		content.AddChild(_terrainListSummaryLabel);

		_terrainList = new ItemList
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			AllowReselect = true,
			SelectMode = ItemList.SelectModeEnum.Single,
		};
		_terrainList.ItemSelected += OnTerrainSelected;
		content.AddChild(_terrainList);

		_saveButton = new Button
		{
			Text = "保存映射",
		};
		_saveButton.Pressed += SaveMappings;
		content.AddChild(_saveButton);
	}

	private void BuildCenterPanel(HBoxContainer root)
	{
		var panel = new PanelContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		root.AddChild(panel);

		var content = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		content.AddThemeConstantOverride("separation", 8);
		panel.AddChild(content);

		_mappingStatusLabel = new Label
		{
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		content.AddChild(_mappingStatusLabel);

		var previewPanel = new PanelContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(520, 360),
		};
		content.AddChild(previewPanel);

		var previewContainer = new SubViewportContainer
		{
			Stretch = true,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		previewPanel.AddChild(previewContainer);

		var viewport = new SubViewport
		{
			TransparentBg = false,
			Size = new Vector2I(640, 420),
			Disable3D = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
		};
		previewContainer.AddChild(viewport);

		var previewRoot = new Node2D();
		viewport.AddChild(previewRoot);
		previewRoot.AddChild(new ColorRect
		{
			Color = new Color(0.10f, 0.11f, 0.14f),
			Size = new Vector2(640, 420),
		});

		_leftSprite = new Sprite2D { Centered = true, Scale = new Vector2(PreviewScale, PreviewScale) };
		_rightSprite = new Sprite2D { Centered = true, Scale = new Vector2(PreviewScale, PreviewScale) };
		_topSprite = new Sprite2D { Centered = true, Scale = new Vector2(PreviewScale, PreviewScale) };
		previewRoot.AddChild(_leftSprite);
		previewRoot.AddChild(_rightSprite);
		previewRoot.AddChild(_topSprite);

		var slotRow = new HBoxContainer();
		slotRow.AddThemeConstantOverride("separation", 10);
		content.AddChild(slotRow);

		(_topSlotButton, _topSlotPreview, _topSlotTitleLabel, _topSlotMetaLabel) = BuildSlotCard(slotRow, "顶面", FaceSlot.Top);
		(_leftSlotButton, _leftSlotPreview, _leftSlotTitleLabel, _leftSlotMetaLabel) = BuildSlotCard(slotRow, "左侧", FaceSlot.LeftSide);
		(_rightSlotButton, _rightSlotPreview, _rightSlotTitleLabel, _rightSlotMetaLabel) = BuildSlotCard(slotRow, "右侧", FaceSlot.RightSide);

		var actionRow = new HBoxContainer();
		actionRow.AddThemeConstantOverride("separation", 8);
		content.AddChild(actionRow);

		actionRow.AddChild(new Label { Text = "当前侧面颜色" });
		_sideColorPicker = new ColorPickerButton
		{
			Color = Colors.White,
			CustomMinimumSize = new Vector2(80, 32),
		};
		actionRow.AddChild(_sideColorPicker);

		_applyColorButton = new Button
		{
			Text = "颜色写入当前槽位",
		};
		_applyColorButton.Pressed += ApplyColorToActiveSide;
		actionRow.AddChild(_applyColorButton);

		_clearSlotButton = new Button
		{
			Text = "清空当前槽位",
		};
		_clearSlotButton.Pressed += ClearActiveSlot;
		actionRow.AddChild(_clearSlotButton);

		_globalStatusLabel = new Label
		{
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		content.AddChild(_globalStatusLabel);
	}

	private void BuildRightPanel(HBoxContainer root)
	{
		var panel = new PanelContainer
		{
			CustomMinimumSize = new Vector2(560, 0),
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		root.AddChild(panel);

		var content = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		content.AddThemeConstantOverride("separation", 8);
		panel.AddChild(content);

		_browserSummaryLabel = new Label
		{
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		content.AddChild(_browserSummaryLabel);

		_searchEdit = new LineEdit
		{
			PlaceholderText = "搜索中文名 / 原文件名 / 目录 / 标签",
		};
		_searchEdit.TextChanged += _ => RefreshBrowser(resetPage: true);
		content.AddChild(_searchEdit);

		var filterRow = new HBoxContainer();
		filterRow.AddThemeConstantOverride("separation", 8);
		content.AddChild(filterRow);

		_groupFilter = new OptionButton
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		_groupFilter.ItemSelected += _ => RefreshBrowser(resetPage: true);
		filterRow.AddChild(_groupFilter);

		_tagFilter = new OptionButton
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		_tagFilter.ItemSelected += _ => RefreshBrowser(resetPage: true);
		filterRow.AddChild(_tagFilter);

		_mappingEligibleOnlyCheck = new CheckBox
		{
			Text = "仅显示可映射资源",
		};
		_mappingEligibleOnlyCheck.Toggled += _ => RefreshBrowser(resetPage: true);
		content.AddChild(_mappingEligibleOnlyCheck);

		var pageRow = new HBoxContainer();
		pageRow.AddThemeConstantOverride("separation", 8);
		content.AddChild(pageRow);

		_previousPageButton = new Button { Text = "上一页" };
		_previousPageButton.Pressed += () => ChangeBrowserPage(-1);
		pageRow.AddChild(_previousPageButton);

		_pageLabel = new Label
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		pageRow.AddChild(_pageLabel);

		_nextPageButton = new Button { Text = "下一页" };
		_nextPageButton.Pressed += () => ChangeBrowserPage(1);
		pageRow.AddChild(_nextPageButton);

		var scroll = new ScrollContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		content.AddChild(scroll);

		_browserResults = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		_browserResults.AddThemeConstantOverride("separation", 6);
		scroll.AddChild(_browserResults);
	}

	private (Button Button, TextureRect Preview, Label TitleLabel, Label MetaLabel) BuildSlotCard(HBoxContainer parent, string title, FaceSlot slot)
	{
		var panel = new PanelContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 200),
		};
		parent.AddChild(panel);

		var content = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		content.AddThemeConstantOverride("separation", 4);
		panel.AddChild(content);

		var button = new Button
		{
			Text = title,
			ToggleMode = true,
		};
		button.Pressed += () => SetActiveSlot(slot);
		content.AddChild(button);

		var preview = new TextureRect
		{
			CustomMinimumSize = new Vector2(120, 90),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
		};
		content.AddChild(preview);

		var titleLabel = new Label
		{
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		content.AddChild(titleLabel);

		var metaLabel = new Label
		{
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		content.AddChild(metaLabel);

		return (button, preview, titleLabel, metaLabel);
	}

	private void OnTerrainSelected(long index)
	{
		if (_isRefreshingUi)
			return;

		SelectTerrain((int)index);
	}

	private void SetActiveSlot(FaceSlot slot)
	{
		_activeSlot = slot;
		UpdateActiveSlotUi();
		UpdateSideActionUi();
		RefreshBrowser(resetPage: false);
	}

	private void UpdateActiveSlotUi()
	{
		_topSlotButton.SetPressedNoSignal(_activeSlot == FaceSlot.Top);
		_leftSlotButton.SetPressedNoSignal(_activeSlot == FaceSlot.LeftSide);
		_rightSlotButton.SetPressedNoSignal(_activeSlot == FaceSlot.RightSide);
	}

	private void UpdateSideActionUi()
	{
		var sideActive = _activeSlot != FaceSlot.Top;
		_applyColorButton.Disabled = !sideActive;
	}

	private void ChangeBrowserPage(int delta)
	{
		_browserPage = Math.Max(0, _browserPage + delta);
		RefreshBrowser(resetPage: false);
	}

	private void LoadData()
	{
		LocalizationService.Initialize();
		try
		{
			LocalizationService.SetLocale(AppSettingsStore.LoadLocale(), notify: false);
		}
		catch
		{
			LocalizationService.SetLocale(LocalizationService.DefaultLocale, notify: false);
		}

		if (TerrainRegistry.Get(Terrains.Floor) == null || TerrainRegistry.Get(Terrains.Water) == null)
			TerrainRegistry.Load("terrains.json");

		_catalog = PzTileCatalogStore.Load();
		_catalogByPath.Clear();
		foreach (var entry in _catalog.Entries)
		{
			var normalizedPath = PzTilePathUtility.NormalizeAssetPath(entry.Path);
			if (!string.IsNullOrWhiteSpace(normalizedPath))
				_catalogByPath[normalizedPath] = entry;
		}

		_mappings.Clear();
		var mappingDocument = VoxelTileMappingStore.Load();
		foreach (var entry in mappingDocument.Entries)
		{
			if (!string.IsNullOrWhiteSpace(entry.TerrainId))
				_mappings[entry.TerrainId] = entry;
		}

		_terrains.Clear();
		foreach (var terrain in TerrainRegistry.All)
		{
			if (terrain == null || string.IsNullOrWhiteSpace(terrain.StringId))
				continue;
			if (terrain.StringId is Terrains.Void or Terrains.Air)
				continue;
			_terrains.Add(terrain);
		}
	}

	private void RefreshFilterOptions()
	{
		_groupFilter.Clear();
		AddFilterItem(_groupFilter, "全部分组", string.Empty);
		foreach (var group in _catalog.Entries
			.Where(static entry => !string.IsNullOrWhiteSpace(entry.Group))
			.GroupBy(static entry => entry.Group)
			.OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase))
		{
			AddFilterItem(_groupFilter, $"{group.Key} ({group.Count()})", group.Key);
		}
		_groupFilter.Select(0);

		_tagFilter.Clear();
		AddFilterItem(_tagFilter, "全部标签", string.Empty);
		foreach (var pair in GetTagCounts())
			AddFilterItem(_tagFilter, $"{pair.Key} ({pair.Value})", pair.Key);
		_tagFilter.Select(0);
	}

	private void RefreshBrowser(bool resetPage)
	{
		if (resetPage)
			_browserPage = 0;

		_filteredEntries.Clear();
		foreach (var entry in _catalog.Entries)
		{
			if (MatchesBrowserFilters(entry))
				_filteredEntries.Add(entry);
		}

		var maxPage = _filteredEntries.Count == 0
			? 0
			: (_filteredEntries.Count - 1) / BrowserPageSize;
		_browserPage = Math.Clamp(_browserPage, 0, maxPage);

		foreach (var child in _browserResults.GetChildren())
			child.QueueFree();

		var selectedTerrain = GetSelectedTerrain();
		var selectedTerrainName = selectedTerrain == null
			? "未选中 terrain"
			: GameLocalizer.LocalizeTerrainName(selectedTerrain.StringId);
		_browserSummaryLabel.Text =
			$"资源浏览器: {_filteredEntries.Count}/{_catalog.Entries.Count}\n" +
			$"当前槽位: {GetSlotLabel(_activeSlot)}\n" +
			$"当前 terrain: {selectedTerrainName}";

		if (_filteredEntries.Count == 0)
		{
			_browserResults.AddChild(new Label
			{
				Text = "没有匹配的资源。",
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			});
		}
		else
		{
			var activeSlotPath = GetActiveSlotPath(GetSelectedMapping());
			var pageEntries = _filteredEntries
				.Skip(_browserPage * BrowserPageSize)
				.Take(BrowserPageSize)
				.ToList();
			foreach (var entry in pageEntries)
				_browserResults.AddChild(BuildBrowserCard(entry, activeSlotPath));
		}

		_pageLabel.Text = $"第 {_browserPage + 1} / {maxPage + 1} 页";
		_previousPageButton.Disabled = _browserPage <= 0;
		_nextPageButton.Disabled = _browserPage >= maxPage;
	}

	private void RebuildTerrainList()
	{
		_isRefreshingUi = true;
		_terrainList.Clear();
		for (var i = 0; i < _terrains.Count; i++)
		{
			var terrain = _terrains[i];
			var status = CalculateMappingStatus(terrain.StringId);
			_terrainList.AddItem(BuildTerrainListLabel(terrain, status));
			_terrainList.SetItemTooltip(i, $"{GameLocalizer.LocalizeTerrainName(terrain.StringId)}\nstringId: {terrain.StringId}\n状态: {status.DisplayText}");
			_terrainList.SetItemCustomFgColor(i, GetTerrainStatusColor(status));
		}

		if (_terrains.Count > 0)
		{
			_selectedTerrainIndex = Math.Clamp(_selectedTerrainIndex, 0, _terrains.Count - 1);
			_terrainList.Select(_selectedTerrainIndex);
		}

		var completed = _terrains.Count(terrain => CalculateMappingStatus(terrain.StringId).IsComplete);
		var invalid = _terrains.Count(terrain => CalculateMappingStatus(terrain.StringId).HasInvalidReference);
		_terrainListSummaryLabel.Text =
			$"terrain 映射: {completed}/{_terrains.Count}\n" +
			$"失效引用: {invalid}\n" +
			$"catalog 资源: {_catalog.Entries.Count}";
		_isRefreshingUi = false;
	}

	private void SelectTerrain(int index)
	{
		if (_terrains.Count == 0)
		{
			_selectedTerrainIndex = 0;
			RefreshSelectionState();
			return;
		}

		_selectedTerrainIndex = Math.Clamp(index, 0, _terrains.Count - 1);
		_terrainList.Select(_selectedTerrainIndex);
		RefreshSelectionState();
		RefreshBrowser(resetPage: false);
	}

	private void RefreshSelectionState()
	{
		if (_terrains.Count == 0)
		{
			_mappingStatusLabel.Text = "没有可编辑的 terrain。";
			_globalStatusLabel.Text = "请先加载 terrain 和 catalog 数据。";
			ApplySlotView(_topSlotPreview, _topSlotTitleLabel, _topSlotMetaLabel, new SlotViewModel("未设置", "无可用 terrain", null));
			ApplySlotView(_leftSlotPreview, _leftSlotTitleLabel, _leftSlotMetaLabel, new SlotViewModel("未设置", "无可用 terrain", null));
			ApplySlotView(_rightSlotPreview, _rightSlotTitleLabel, _rightSlotMetaLabel, new SlotViewModel("未设置", "无可用 terrain", null));
			RefreshPreview();
			return;
		}

		var terrain = _terrains[_selectedTerrainIndex];
		var mapping = GetSelectedMapping();
		var status = CalculateMappingStatus(terrain.StringId);
		_mappingStatusLabel.Text =
			$"{GameLocalizer.LocalizeTerrainName(terrain.StringId)}\n" +
			$"stringId: {terrain.StringId}\n" +
			$"状态: {status.DisplayText}";
		_globalStatusLabel.Text = BuildGlobalStatusText(terrain, mapping, status);

		ApplySlotView(_topSlotPreview, _topSlotTitleLabel, _topSlotMetaLabel, DescribeSlot(FaceSlot.Top, mapping));
		ApplySlotView(_leftSlotPreview, _leftSlotTitleLabel, _leftSlotMetaLabel, DescribeSlot(FaceSlot.LeftSide, mapping));
		ApplySlotView(_rightSlotPreview, _rightSlotTitleLabel, _rightSlotMetaLabel, DescribeSlot(FaceSlot.RightSide, mapping));

		var sideColor = GetPreferredSideColor(mapping, terrain);
		_sideColorPicker.Color = sideColor;
		RefreshPreview();
	}

	private void ApplyColorToActiveSide()
	{
		if (_activeSlot == FaceSlot.Top)
			return;

		var terrain = GetSelectedTerrain();
		if (terrain == null)
			return;

		var mapping = EnsureMappingEntry(terrain.StringId);
		var colorHex = ColorToHex(_sideColorPicker.Color);
		switch (_activeSlot)
		{
			case FaceSlot.LeftSide:
				mapping.LeftSideMode = "color";
				mapping.LeftSideColor = colorHex;
				mapping.LeftSideTilePath = null;
				mapping.LeftIsIso = false;
				break;
			case FaceSlot.RightSide:
				mapping.RightSideMode = "color";
				mapping.RightSideColor = colorHex;
				mapping.RightSideTilePath = null;
				mapping.RightIsIso = false;
				break;
		}

		RebuildTerrainList();
		RefreshSelectionState();
	}

	private void ClearActiveSlot()
	{
		var terrain = GetSelectedTerrain();
		if (terrain == null)
			return;

		var mapping = EnsureMappingEntry(terrain.StringId);
		switch (_activeSlot)
		{
			case FaceSlot.Top:
				mapping.TopTilePath = null;
				mapping.TopIsIso = false;
				break;
			case FaceSlot.LeftSide:
				mapping.LeftSideMode = null;
				mapping.LeftSideTilePath = null;
				mapping.LeftSideColor = null;
				mapping.LeftIsIso = false;
				break;
			case FaceSlot.RightSide:
				mapping.RightSideMode = null;
				mapping.RightSideTilePath = null;
				mapping.RightSideColor = null;
				mapping.RightIsIso = false;
				break;
		}

		RebuildTerrainList();
		RefreshSelectionState();
	}

	private void SaveMappings()
	{
		var document = new VoxelTileMappingDocument
		{
			Entries = _mappings.Values
				.OrderBy(static entry => entry.TerrainId, StringComparer.OrdinalIgnoreCase)
				.ToList(),
		};
		var outputPath = VoxelTileMappingStore.GetProjectFilePath();
		var directory = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
			Directory.CreateDirectory(directory);

		File.WriteAllText(outputPath, VoxelTileMappingStore.Serialize(document));
		RefreshSelectionState();
	}

	private void AddFilterItem(OptionButton optionButton, string label, string value)
	{
		var index = optionButton.ItemCount;
		optionButton.AddItem(label);
		optionButton.SetItemMetadata(index, value);
	}

	private SortedDictionary<string, int> GetTagCounts()
	{
		var counts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (var entry in _catalog.Entries)
		{
			foreach (var tag in entry.Tags)
			{
				if (!ShouldExposeTag(tag))
					continue;
				counts[tag] = counts.TryGetValue(tag, out var count) ? count + 1 : 1;
			}
		}

		return counts;
	}

	private static bool ShouldExposeTag(string tag)
	{
		if (string.IsNullOrWhiteSpace(tag))
			return false;
		if (tag.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
			return false;
		if (tag.Length <= 1)
			return false;
		return true;
	}

	private string GetSelectedFilterValue(OptionButton optionButton)
	{
		if (optionButton.Selected < 0)
			return string.Empty;

		return optionButton.GetItemMetadata(optionButton.Selected).AsString();
	}

	private bool MatchesBrowserFilters(PzTileCatalogEntry entry)
	{
		if (_mappingEligibleOnlyCheck.ButtonPressed && !entry.MappingEligible)
			return false;

		var groupFilterValue = GetSelectedFilterValue(_groupFilter);
		if (!string.IsNullOrWhiteSpace(groupFilterValue)
			&& !string.Equals(entry.Group, groupFilterValue, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var tagFilterValue = GetSelectedFilterValue(_tagFilter);
		if (!string.IsNullOrWhiteSpace(tagFilterValue)
			&& !entry.Tags.Any(tag => string.Equals(tag, tagFilterValue, StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}

		var keyword = _searchEdit.Text.Trim();
		if (string.IsNullOrWhiteSpace(keyword))
			return true;

		var directory = PzTilePathUtility.GetDisplayDirectory(entry.Path);
		var searchable = string.Join(
			" ",
			entry.DisplayNameZh,
			entry.DisplayNameEn,
			entry.OriginalFileName,
			entry.Group,
			directory,
			string.Join(' ', entry.Tags));
		return searchable.Contains(keyword, StringComparison.OrdinalIgnoreCase);
	}

	private Control BuildBrowserCard(PzTileCatalogEntry entry, string? activeSlotPath)
	{
		var button = new Button
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 104),
			Alignment = HorizontalAlignment.Left,
		};
		button.Pressed += () => ApplyCatalogEntryToActiveSlot(entry);

		if (!string.IsNullOrWhiteSpace(activeSlotPath)
			&& string.Equals(activeSlotPath, PzTilePathUtility.NormalizeAssetPath(entry.Path), StringComparison.OrdinalIgnoreCase))
		{
			button.Modulate = new Color(0.84f, 1.0f, 0.88f);
		}

		var row = new HBoxContainer
		{
			MouseFilter = MouseFilterEnum.Ignore,
		};
		row.AddThemeConstantOverride("separation", 8);
		button.AddChild(row);

		var preview = new TextureRect
		{
			Texture = GetTexture(entry.Path),
			CustomMinimumSize = new Vector2(80, 80),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		row.AddChild(preview);

		var textColumn = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		row.AddChild(textColumn);

		textColumn.AddChild(new Label
		{
			Text = entry.DisplayNameZh,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = MouseFilterEnum.Ignore,
		});
		textColumn.AddChild(new Label
		{
			Text = entry.OriginalFileName,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = MouseFilterEnum.Ignore,
		});
		textColumn.AddChild(new Label
		{
			Text = $"{entry.Group} · {PzTilePathUtility.GetDisplayDirectory(entry.Path)}",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = MouseFilterEnum.Ignore,
		});
		textColumn.AddChild(new Label
		{
			Text = string.Join(", ", entry.Tags.Where(ShouldExposeTag).Take(6)),
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = MouseFilterEnum.Ignore,
		});

		return button;
	}

	private void ApplyCatalogEntryToActiveSlot(PzTileCatalogEntry catalogEntry)
	{
		var terrain = GetSelectedTerrain();
		if (terrain == null)
			return;

		var mapping = EnsureMappingEntry(terrain.StringId);
		var normalizedPath = PzTilePathUtility.NormalizeAssetPath(catalogEntry.Path);
		switch (_activeSlot)
		{
			case FaceSlot.Top:
				mapping.TopTilePath = normalizedPath;
				mapping.TopIsIso = string.Equals(catalogEntry.Kind, "iso_tile", StringComparison.OrdinalIgnoreCase);
				break;
			case FaceSlot.LeftSide:
				mapping.LeftSideMode = "texture";
				mapping.LeftSideTilePath = normalizedPath;
				mapping.LeftSideColor = null;
				mapping.LeftIsIso = false;
				break;
			case FaceSlot.RightSide:
				mapping.RightSideMode = "texture";
				mapping.RightSideTilePath = normalizedPath;
				mapping.RightSideColor = null;
				mapping.RightIsIso = false;
				break;
		}

		RebuildTerrainList();
		RefreshSelectionState();
		RefreshBrowser(resetPage: false);
	}

	private VoxelTileMappingEntry EnsureMappingEntry(string terrainId)
	{
		if (_mappings.TryGetValue(terrainId, out var existing))
			return existing;

		var created = new VoxelTileMappingEntry
		{
			TerrainId = terrainId,
			Category = "terrain",
		};
		_mappings[terrainId] = created;
		return created;
	}

	private TerrainDef? GetSelectedTerrain()
	{
		if (_terrains.Count == 0 || _selectedTerrainIndex < 0 || _selectedTerrainIndex >= _terrains.Count)
			return null;

		return _terrains[_selectedTerrainIndex];
	}

	private VoxelTileMappingEntry? GetSelectedMapping()
	{
		var terrain = GetSelectedTerrain();
		if (terrain == null)
			return null;

		return _mappings.GetValueOrDefault(terrain.StringId);
	}

	private string? GetActiveSlotPath(VoxelTileMappingEntry? entry) => _activeSlot switch
	{
		FaceSlot.Top => NormalizePath(entry?.TopTilePath),
		FaceSlot.LeftSide => string.Equals(entry?.LeftSideMode, "texture", StringComparison.OrdinalIgnoreCase)
			? NormalizePath(entry?.LeftSideTilePath)
			: null,
		FaceSlot.RightSide => string.Equals(entry?.RightSideMode, "texture", StringComparison.OrdinalIgnoreCase)
			? NormalizePath(entry?.RightSideTilePath)
			: null,
		_ => null,
	};

	private static string NormalizePath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return string.Empty;
		return PzTilePathUtility.NormalizeAssetPath(path);
	}

	private static string BuildTerrainListLabel(TerrainDef terrain, MappingStatus status)
	{
		var terrainName = GameLocalizer.LocalizeTerrainName(terrain.StringId);
		return $"{terrainName}\n{terrain.StringId} · {status.DisplayText}";
	}

	private static Color GetTerrainStatusColor(MappingStatus status)
	{
		if (status.HasInvalidReference)
			return new Color(1.0f, 0.67f, 0.67f);
		if (status.IsComplete)
			return new Color(0.76f, 0.95f, 0.78f);
		if (!status.HasTop)
			return new Color(1.0f, 0.85f, 0.58f);
		return new Color(0.93f, 0.88f, 0.62f);
	}

	private MappingStatus CalculateMappingStatus(string terrainId)
	{
		if (!_mappings.TryGetValue(terrainId, out var mapping))
			return new MappingStatus(false, false, false, false);

		var hasTop = false;
		var hasInvalidReference = false;
		if (!string.IsNullOrWhiteSpace(mapping.TopTilePath))
		{
			hasTop = TryGetCatalogEntry(mapping.TopTilePath, out _);
			if (!hasTop)
				hasInvalidReference = true;
		}

		var hasLeft = EvaluateSide(mapping.LeftSideMode, mapping.LeftSideTilePath, mapping.LeftSideColor, mapping.ShowLeftSide, ref hasInvalidReference);
		var hasRight = EvaluateSide(mapping.RightSideMode, mapping.RightSideTilePath, mapping.RightSideColor, mapping.ShowRightSide, ref hasInvalidReference);
		return new MappingStatus(hasTop, hasLeft, hasRight, hasInvalidReference);
	}

	private bool EvaluateSide(string? mode, string? tilePath, string? colorHex, bool showSide, ref bool hasInvalidReference)
	{
		if (!showSide)
			return false;

		if (string.Equals(mode, "color", StringComparison.OrdinalIgnoreCase))
		{
			var validColor = TryParseColor(colorHex, out _);
			if (!validColor && !string.IsNullOrWhiteSpace(colorHex))
				hasInvalidReference = true;
			return validColor;
		}

		if (string.Equals(mode, "texture", StringComparison.OrdinalIgnoreCase))
		{
			var validTexture = TryGetCatalogEntry(tilePath, out _);
			if (!validTexture && !string.IsNullOrWhiteSpace(tilePath))
				hasInvalidReference = true;
			return validTexture;
		}

		if (!string.IsNullOrWhiteSpace(tilePath) || !string.IsNullOrWhiteSpace(colorHex))
			hasInvalidReference = true;
		return false;
	}

	private string BuildGlobalStatusText(TerrainDef terrain, VoxelTileMappingEntry? mapping, MappingStatus status)
	{
		var top = DescribeSlot(FaceSlot.Top, mapping);
		var left = DescribeSlot(FaceSlot.LeftSide, mapping);
		var right = DescribeSlot(FaceSlot.RightSide, mapping);
		var completed = _terrains.Count(candidate => CalculateMappingStatus(candidate.StringId).IsComplete);
		var invalid = _terrains.Count(candidate => CalculateMappingStatus(candidate.StringId).HasInvalidReference);
		return
			$"顶面: {top.SummaryText}\n" +
			$"左侧: {left.SummaryText}\n" +
			$"右侧: {right.SummaryText}\n" +
			$"当前条目完整: {(status.IsComplete ? "是" : "否")}\n" +
			$"全局覆盖: terrain 映射 {completed}/{_terrains.Count}，失效引用 {invalid}\n" +
			$"资源根: {PzTilePathUtility.CopyRoot}";
	}

	private SlotViewModel DescribeSlot(FaceSlot slot, VoxelTileMappingEntry? mapping)
	{
		if (slot == FaceSlot.Top)
		{
			if (TryGetCatalogEntry(mapping?.TopTilePath, out var topEntry))
			{
				return new SlotViewModel(
					topEntry.DisplayNameZh,
					$"{topEntry.OriginalFileName}\n{topEntry.Group} · {PzTilePathUtility.GetDisplayDirectory(topEntry.Path)}",
					GetTexture(topEntry.Path),
					$"{topEntry.DisplayNameZh} ({topEntry.OriginalFileName})");
			}

			if (!string.IsNullOrWhiteSpace(mapping?.TopTilePath))
			{
				var normalized = NormalizePath(mapping.TopTilePath);
				return new SlotViewModel(
					"失效引用",
					normalized,
					null,
					$"失效引用: {normalized}");
			}

			return new SlotViewModel("未设置", "请从右侧资源浏览器选择顶面资源。", null, "未设置");
		}

		var isRight = slot == FaceSlot.RightSide;
		var mode = isRight ? mapping?.RightSideMode : mapping?.LeftSideMode;
		var path = isRight ? mapping?.RightSideTilePath : mapping?.LeftSideTilePath;
		var colorHex = isRight ? mapping?.RightSideColor : mapping?.LeftSideColor;

		if (string.Equals(mode, "color", StringComparison.OrdinalIgnoreCase) && TryParseColor(colorHex, out var color))
		{
			return new SlotViewModel(
				"纯色侧面",
				colorHex ?? string.Empty,
				ImageTexture.CreateFromImage(CreateSolidImage(80, 80, color)),
				colorHex ?? "纯色");
		}

		if (string.Equals(mode, "texture", StringComparison.OrdinalIgnoreCase) && TryGetCatalogEntry(path, out var sideEntry))
		{
			return new SlotViewModel(
				sideEntry.DisplayNameZh,
				$"{sideEntry.OriginalFileName}\n{sideEntry.Group} · {PzTilePathUtility.GetDisplayDirectory(sideEntry.Path)}",
				GetTexture(sideEntry.Path),
				$"{sideEntry.DisplayNameZh} ({sideEntry.OriginalFileName})");
		}

		if (!string.IsNullOrWhiteSpace(path))
		{
			var normalized = NormalizePath(path);
			return new SlotViewModel(
				"失效引用",
				normalized,
				null,
				$"失效引用: {normalized}");
		}

		return new SlotViewModel("未设置", "侧面需显式指定贴图或颜色。", null, "未设置");
	}

	private void ApplySlotView(TextureRect preview, Label titleLabel, Label metaLabel, SlotViewModel viewModel)
	{
		preview.Texture = viewModel.PreviewTexture;
		titleLabel.Text = viewModel.Title;
		metaLabel.Text = viewModel.Subtitle;
	}

	private Color GetPreferredSideColor(VoxelTileMappingEntry? mapping, TerrainDef terrain)
	{
		if (_activeSlot == FaceSlot.LeftSide && TryParseColor(mapping?.LeftSideColor, out var leftColor))
			return leftColor;
		if (_activeSlot == FaceSlot.RightSide && TryParseColor(mapping?.RightSideColor, out var rightColor))
			return rightColor;
		if (TryParseColor(mapping?.LeftSideColor, out leftColor))
			return leftColor;
		if (TryParseColor(mapping?.RightSideColor, out rightColor))
			return rightColor;
		return GetFallbackColor(terrain.StringId);
	}

	private void RefreshPreview()
	{
		var terrain = GetSelectedTerrain();
		if (terrain == null)
		{
			_topSprite.Visible = false;
			_leftSprite.Visible = false;
			_rightSprite.Visible = false;
			return;
		}

		var mapping = GetSelectedMapping();
		var topTexture = BuildTopPreviewTexture(terrain, mapping);
		_topSprite.Texture = topTexture;
		_topSprite.Position = new Vector2(320, 150);
		_topSprite.Scale = Vector2.One;
		_topSprite.Visible = topTexture != null;

		var leftTexture = BuildSidePreviewTexture(terrain, mapping, isRight: false, out var showLeft);
		_leftSprite.Texture = leftTexture;
		_leftSprite.Position = new Vector2(256, 325);
		_leftSprite.Scale = Vector2.One;
		_leftSprite.Visible = showLeft && leftTexture != null;

		var rightTexture = BuildSidePreviewTexture(terrain, mapping, isRight: true, out var showRight);
		_rightSprite.Texture = rightTexture;
		_rightSprite.Position = new Vector2(384, 325);
		_rightSprite.Scale = Vector2.One;
		_rightSprite.Visible = showRight && rightTexture != null;
	}

	private Texture2D? BuildTopPreviewTexture(TerrainDef terrain, VoxelTileMappingEntry? mapping)
	{
		var source = GetImage(mapping?.TopTilePath);
		if (source == null)
		{
			var fallback = CreateDiamondImage(
				VoxelFaceImageUtil.TopFaceWidth,
				VoxelFaceImageUtil.TopFaceHeight,
				GetFallbackColor(terrain.StringId));
			return ImageTexture.CreateFromImage(fallback);
		}

		var image = mapping?.TopIsIso == true
			? ScaleToFit(source, VoxelFaceImageUtil.TopFaceWidth, VoxelFaceImageUtil.TopFaceHeight, mapping.TopScaleX, mapping.TopScaleY, mapping.TopOffsetX, mapping.TopOffsetY)
			: VoxelFaceImageUtil.EnhanceTopFaceEdges(VoxelFaceImageUtil.BuildTopDiamond(source), GetTopEdgeStrength(terrain));
		return ImageTexture.CreateFromImage(image);
	}

	private Texture2D? BuildSidePreviewTexture(TerrainDef terrain, VoxelTileMappingEntry? mapping, bool isRight, out bool showSide)
	{
		showSide = isRight ? mapping?.ShowRightSide ?? true : mapping?.ShowLeftSide ?? true;
		if (!showSide)
			return null;

		if (mapping != null)
		{
			var mode = isRight ? mapping.RightSideMode : mapping.LeftSideMode;
			var path = isRight ? mapping.RightSideTilePath : mapping.LeftSideTilePath;
			var isIso = isRight ? mapping.RightIsIso : mapping.LeftIsIso;
			if (string.Equals(mode, "texture", StringComparison.OrdinalIgnoreCase) && isIso)
			{
				var directImage = GetImage(path);
				if (directImage != null)
				{
					var offsetX = isRight ? mapping.RightOffsetX : mapping.LeftOffsetX;
					var offsetY = isRight ? mapping.RightOffsetY : mapping.LeftOffsetY;
					var fitted = ScaleToFit(
						directImage,
						VoxelFaceImageUtil.SideTextureWidth,
						VoxelFaceImageUtil.SideTextureHeight,
						mapping.TopScaleX,
						mapping.TopScaleY,
						offsetX,
						offsetY);
					return ImageTexture.CreateFromImage(fitted);
				}
			}
		}

		var source = ResolveSideSourceImage(terrain, mapping, isRight);
		var wallLike = IsWallTerrain(terrain);
		var darken = isRight
			? (wallLike ? WallRightSideDarken : RightDarken)
			: (wallLike ? WallLeftSideDarken : LeftDarken);
		var customHeight = isRight ? mapping?.RightHeight ?? 0 : mapping?.LeftHeight ?? 0;
		var faceHeight = customHeight > 0
			? customHeight
			: (wallLike ? VoxelFaceImageUtil.WallSideFaceHeight : VoxelFaceImageUtil.SideFaceHeight);
		var image = VoxelFaceImageUtil.GenerateSideFace(source, isRight, darken, faceHeight);
		VoxelFaceImageUtil.EnhanceSideFaceEdge(image, isRight, GetSideEdgeStrength(terrain));
		return ImageTexture.CreateFromImage(image);
	}

	private Image ResolveSideSourceImage(TerrainDef terrain, VoxelTileMappingEntry? mapping, bool isRight)
	{
		var mode = isRight ? mapping?.RightSideMode : mapping?.LeftSideMode;
		var path = isRight ? mapping?.RightSideTilePath : mapping?.LeftSideTilePath;
		var colorHex = isRight ? mapping?.RightSideColor : mapping?.LeftSideColor;
		if (string.Equals(mode, "color", StringComparison.OrdinalIgnoreCase) && TryParseColor(colorHex, out var parsedColor))
			return CreateSolidImage(VoxelFaceImageUtil.SideTextureWidth, VoxelFaceImageUtil.SideTextureWidth, parsedColor);

		var textureImage = GetImage(path);
		if (textureImage != null)
			return textureImage;

		return CreateSolidImage(
			VoxelFaceImageUtil.SideTextureWidth,
			VoxelFaceImageUtil.SideTextureWidth,
			GetFallbackColor(terrain.StringId));
	}

	private bool TryGetCatalogEntry(string? path, out PzTileCatalogEntry entry) =>
		_catalogByPath.TryGetValue(NormalizePath(path), out entry!);

	private Texture2D? GetTexture(string? path)
	{
		var normalized = NormalizePath(path);
		if (string.IsNullOrWhiteSpace(normalized))
			return null;
		if (_textureCache.TryGetValue(normalized, out var cached))
			return cached;

		var loaded = GD.Load<Texture2D>(normalized);
		_textureCache[normalized] = loaded;
		return loaded;
	}

	private Image? GetImage(string? path)
	{
		var normalized = NormalizePath(path);
		if (string.IsNullOrWhiteSpace(normalized))
			return null;
		if (_imageCache.TryGetValue(normalized, out var cached))
			return cached;

		var texture = GetTexture(normalized);
		var image = texture == null ? null : ExtractImage(texture);
		_imageCache[normalized] = image;
		return image;
	}

	private static Image? ExtractImage(Texture2D texture)
	{
		if (texture is AtlasTexture atlas)
		{
			var fullImage = atlas.Atlas?.GetImage();
			if (fullImage == null)
				return null;

			var region = atlas.Region;
			return fullImage.GetRegion(new Rect2I(
				(int)region.Position.X,
				(int)region.Position.Y,
				(int)region.Size.X,
				(int)region.Size.Y));
		}

		return texture.GetImage();
	}

	private static bool TryParseColor(string? colorHex, out Color color)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(colorHex))
			{
				color = new Color(colorHex);
				return true;
			}
		}
		catch
		{
			// Ignore invalid colors.
		}

		color = Colors.Transparent;
		return false;
	}

	private static Image CreateSolidImage(int width, int height, Color color)
	{
		var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
		image.Fill(color);
		return image;
	}

	private static Image CreateDiamondImage(int width, int height, Color color)
	{
		var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
		var halfWidth = width / 2;
		var halfHeight = height / 2;
		for (var y = 0; y < height; y++)
		for (var x = 0; x < width; x++)
		{
			var dx = Math.Abs(x - halfWidth) / (float)halfWidth;
			var dy = Math.Abs(y - halfHeight) / (float)halfHeight;
			image.SetPixel(x, y, dx + dy <= 1.0f ? color : Colors.Transparent);
		}
		return image;
	}

	private static Image ScaleToFit(Image source, int targetWidth, int targetHeight, float scaleX = 1.0f, float scaleY = 1.0f, float offsetX = 0.0f, float offsetY = 0.0f)
	{
		var sourceWidth = source.GetWidth();
		var sourceHeight = source.GetHeight();
		var fitScaleX = targetWidth / (float)sourceWidth * scaleX;
		var fitScaleY = targetHeight / (float)sourceHeight * scaleY;
		var fitScale = Math.Min(fitScaleX, fitScaleY);
		var newWidth = Math.Max(1, (int)Math.Round(sourceWidth * fitScale));
		var newHeight = Math.Max(1, (int)Math.Round(sourceHeight * fitScale));

		var scaled = (Image)source.Duplicate();
		scaled.Resize(newWidth, newHeight, Image.Interpolation.Bilinear);

		var result = Image.CreateEmpty(targetWidth, targetHeight, false, Image.Format.Rgba8);
		var drawX = (targetWidth - newWidth) / 2 + (int)offsetX;
		var drawY = (int)offsetY;
		result.BlitRect(scaled, new Rect2I(0, 0, newWidth, newHeight), new Vector2I(drawX, drawY));
		return result;
	}

	private static Color GetFallbackColor(string terrainId) =>
		TerrainFallbackColors.GetValueOrDefault(terrainId, new Color(0.55f, 0.55f, 0.58f));

	private static float GetTopEdgeStrength(TerrainDef terrain)
	{
		if (IsWallTerrain(terrain))
			return WallTopEdgeStrength;
		return terrain.Solid ? SolidTopEdgeStrength : NonSolidTopEdgeStrength;
	}

	private static float GetSideEdgeStrength(TerrainDef terrain)
	{
		if (IsWallTerrain(terrain))
			return WallSideEdgeStrength;
		return terrain.Solid ? SolidSideEdgeStrength : NonSolidSideEdgeStrength;
	}

	private static bool IsWallTerrain(TerrainDef terrain)
	{
		return terrain.StringId.StartsWith("wall_", StringComparison.OrdinalIgnoreCase)
			|| terrain.StringId.Equals(Terrains.Stone, StringComparison.OrdinalIgnoreCase)
			|| terrain.StringId.Equals(Terrains.Dirt, StringComparison.OrdinalIgnoreCase)
			|| terrain.StringId.Equals(Terrains.Mountain, StringComparison.OrdinalIgnoreCase);
	}

	private static string GetSlotLabel(FaceSlot slot) => slot switch
	{
		FaceSlot.Top => "顶面",
		FaceSlot.LeftSide => "左侧",
		FaceSlot.RightSide => "右侧",
		_ => "未知",
	};

	private static string ColorToHex(Color color) =>
		$"#{(int)Math.Round(color.R * 255):x2}{(int)Math.Round(color.G * 255):x2}{(int)Math.Round(color.B * 255):x2}";
}

internal readonly record struct MappingStatus(bool HasTop, bool HasLeftSide, bool HasRightSide, bool HasInvalidReference)
{
	public bool IsComplete => HasTop && HasLeftSide && HasRightSide && !HasInvalidReference;

	public string DisplayText => HasInvalidReference
		? "引用失效"
		: !HasTop
			? "缺顶面"
			: (!HasLeftSide || !HasRightSide)
				? "缺侧面"
				: "已映射";
}

internal readonly record struct SlotViewModel(string Title, string Subtitle, Texture2D? PreviewTexture, string SummaryText = "");
