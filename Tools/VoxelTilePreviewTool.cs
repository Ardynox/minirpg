using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
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
	private const string TerrainCategory = "terrain";

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

	private static readonly string[] Categories = [TerrainCategory, "fixture", "entity", "item"];
	private static readonly string[] CategoryLabels = ["地形", "设施", "实体", "物品"];

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
	private readonly List<string> _entryIds = [];
	private readonly List<PzTileCatalogEntry> _filteredEntries = [];
	private readonly Dictionary<string, Button> _categoryButtons = new(StringComparer.OrdinalIgnoreCase);

	private PzTileCatalogDocument _catalog = new();
	private PzWorldVisualRegistryDocument _worldRegistry = new();
	private string _activeCategory = TerrainCategory;
	private FaceSlot _activeSlot = FaceSlot.Top;
	private int _browserPage;
	private int _selectedTerrainIndex;
	private bool _isRefreshingUi;
	private bool _eyedropperMode;

	private ItemList _terrainList = null!;
	private Label _terrainListSummaryLabel = null!;
	private Label _mappingStatusLabel = null!;
	private Label _globalStatusLabel = null!;
	private Label _browserSummaryLabel = null!;
	private Label _pageLabel = null!;
	private LineEdit _searchEdit = null!;
	private OptionButton _groupFilter = null!;
	private OptionButton _tagFilter = null!;
	private OptionButton _usageDomainFilter = null!;
	private OptionButton _usageRoleFilter = null!;
	private OptionButton _placementFilter = null!;
	private OptionButton _variantGroupFilter = null!;
	private CheckBox _mappingEligibleOnlyCheck = null!;
	private VBoxContainer _browserResults = null!;
	private Button _previousPageButton = null!;
	private Button _nextPageButton = null!;
	private ColorPickerButton _sideColorPicker = null!;
	private Button _applyColorButton = null!;
	private Button _eyedropperButton = null!;
	private Button _clearSlotButton = null!;
	private Button _saveButton = null!;
	private FileDialog _fileDialog = null!;

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
	private GuidesDrawNode _guidesNode = null!;

	private HSlider _topScaleXSlider = null!;
	private HSlider _topScaleYSlider = null!;
	private HSlider _topOffsetXSlider = null!;
	private HSlider _topOffsetYSlider = null!;
	private Label _topScaleXLabel = null!;
	private Label _topScaleYLabel = null!;
	private Label _topOffsetXLabel = null!;
	private Label _topOffsetYLabel = null!;
	private HSlider _leftOffsetXSlider = null!;
	private HSlider _leftOffsetYSlider = null!;
	private HSlider _leftHeightSlider = null!;
	private Label _leftOffsetXLabel = null!;
	private Label _leftOffsetYLabel = null!;
	private Label _leftHeightLabel = null!;
	private HSlider _rightOffsetXSlider = null!;
	private HSlider _rightOffsetYSlider = null!;
	private HSlider _rightHeightSlider = null!;
	private Label _rightOffsetXLabel = null!;
	private Label _rightOffsetYLabel = null!;
	private Label _rightHeightLabel = null!;
	private CheckBox _showLeftCheck = null!;
	private CheckBox _showRightCheck = null!;
	private CheckBox _showGuidesCheck = null!;

	public override void _Ready()
	{
		LoadData();
		BuildUi();
		RefreshFilterOptions();
		RebuildTerrainList();
		if (_entryIds.Count > 0)
			SelectTerrain(0);
		else
		{
			RefreshSelectionState();
			RefreshBrowser(resetPage: true);
		}
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

		_fileDialog = new FileDialog
		{
			FileMode = FileDialog.FileModeEnum.OpenFile,
			Access = FileDialog.AccessEnum.Resources,
			Filters = ["*.png ; PNG Images"],
			Title = "选择贴图",
			Size = new Vector2I(800, 500),
		};
		_fileDialog.FileSelected += OnFileDialogSelected;
		AddChild(_fileDialog);

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

		content.AddChild(new Label { Text = "分类" });

		var categoryRow = new HBoxContainer();
		categoryRow.AddThemeConstantOverride("separation", 4);
		content.AddChild(categoryRow);

		for (var i = 0; i < Categories.Length; i++)
		{
			var category = Categories[i];
			var button = new Button
			{
				Text = CategoryLabels[i],
				ToggleMode = true,
				ButtonPressed = string.Equals(_activeCategory, category, StringComparison.OrdinalIgnoreCase),
				CustomMinimumSize = new Vector2(56, 28),
			};
			var capturedCategory = category;
			button.Pressed += () => SwitchCategory(capturedCategory);
			categoryRow.AddChild(button);
			_categoryButtons[category] = button;
		}

		var toolbar = new HBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		toolbar.AddThemeConstantOverride("separation", 6);
		content.AddChild(toolbar);

		var addButton = new Button
		{
			Text = "+ 新增",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		addButton.Pressed += OnAddNewEntry;
		toolbar.AddChild(addButton);

		_saveButton = new Button
		{
			Text = "保存映射",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		_saveButton.Pressed += SaveMappings;
		toolbar.AddChild(_saveButton);

		_terrainListSummaryLabel = new Label
		{
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		content.AddChild(_terrainListSummaryLabel);

		var listContainer = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		listContainer.AddThemeConstantOverride("separation", 6);
		content.AddChild(listContainer);

		_terrainList = new ItemList
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			AllowReselect = true,
			SelectMode = ItemList.SelectModeEnum.Single,
		};
		_terrainList.ItemSelected += OnTerrainSelected;
		listContainer.AddChild(_terrainList);
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

		_guidesNode = new GuidesDrawNode { Visible = false };
		previewRoot.AddChild(_guidesNode);

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

		_eyedropperButton = new Button
		{
			Text = "取色器",
			ToggleMode = true,
		};
		_eyedropperButton.Toggled += on => _eyedropperMode = on;
		actionRow.AddChild(_eyedropperButton);

		_clearSlotButton = new Button
		{
			Text = "清空当前槽位",
		};
		_clearSlotButton.Pressed += ClearActiveSlot;
		actionRow.AddChild(_clearSlotButton);

		var controlsPanel = new VBoxContainer();
		controlsPanel.AddThemeConstantOverride("separation", 4);
		content.AddChild(controlsPanel);

		var toggleRow = new HBoxContainer();
		toggleRow.AddThemeConstantOverride("separation", 12);
		controlsPanel.AddChild(toggleRow);

		_showLeftCheck = new CheckBox { Text = "显示左侧", ButtonPressed = true };
		_showLeftCheck.Toggled += _ => OnSideVisibilityChanged();
		toggleRow.AddChild(_showLeftCheck);

		_showRightCheck = new CheckBox { Text = "显示右侧", ButtonPressed = true };
		_showRightCheck.Toggled += _ => OnSideVisibilityChanged();
		toggleRow.AddChild(_showRightCheck);

		_showGuidesCheck = new CheckBox { Text = "辅助线", ButtonPressed = false };
		_showGuidesCheck.Toggled += on => _guidesNode.Visible = on;
		toggleRow.AddChild(_showGuidesCheck);

		var resetButton = new Button { Text = "重置参数" };
		resetButton.Pressed += OnResetParams;
		toggleRow.AddChild(resetButton);

		controlsPanel.AddChild(new Label { Text = "── 顶面 ──" });
		(_topScaleXSlider, _topScaleXLabel) = BuildSliderRow(controlsPanel, "顶缩放X", 0.1f, 5.0f, 1.0f, _ => OnTopParamsChanged());
		(_topScaleYSlider, _topScaleYLabel) = BuildSliderRow(controlsPanel, "顶缩放Y", 0.1f, 5.0f, 1.0f, _ => OnTopParamsChanged());
		(_topOffsetXSlider, _topOffsetXLabel) = BuildSliderRow(controlsPanel, "顶偏移X", -128f, 128f, 0f, _ => OnTopParamsChanged());
		(_topOffsetYSlider, _topOffsetYLabel) = BuildSliderRow(controlsPanel, "顶偏移Y", -128f, 128f, 0f, _ => OnTopParamsChanged());

		controlsPanel.AddChild(new Label { Text = "── 左侧面 ──" });
		(_leftOffsetXSlider, _leftOffsetXLabel) = BuildSliderRow(controlsPanel, "左偏移X", -128f, 128f, 0f, _ => OnLeftSideParamsChanged());
		(_leftOffsetYSlider, _leftOffsetYLabel) = BuildSliderRow(controlsPanel, "左偏移Y", -128f, 128f, 0f, _ => OnLeftSideParamsChanged());
		(_leftHeightSlider, _leftHeightLabel) = BuildSliderRow(controlsPanel, "左侧高度", 24f, 256f, VoxelFaceImageUtil.SideFaceHeight, _ => OnLeftSideParamsChanged());

		controlsPanel.AddChild(new Label { Text = "── 右侧面 ──" });
		(_rightOffsetXSlider, _rightOffsetXLabel) = BuildSliderRow(controlsPanel, "右偏移X", -128f, 128f, 0f, _ => OnRightSideParamsChanged());
		(_rightOffsetYSlider, _rightOffsetYLabel) = BuildSliderRow(controlsPanel, "右偏移Y", -128f, 128f, 0f, _ => OnRightSideParamsChanged());
		(_rightHeightSlider, _rightHeightLabel) = BuildSliderRow(controlsPanel, "右侧高度", 24f, 256f, VoxelFaceImageUtil.SideFaceHeight, _ => OnRightSideParamsChanged());

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

		var filterRow2 = new HBoxContainer();
		filterRow2.AddThemeConstantOverride("separation", 8);
		content.AddChild(filterRow2);

		_usageDomainFilter = new OptionButton
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		_usageDomainFilter.ItemSelected += _ => RefreshBrowser(resetPage: true);
		filterRow2.AddChild(_usageDomainFilter);

		_usageRoleFilter = new OptionButton
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		_usageRoleFilter.ItemSelected += _ => RefreshBrowser(resetPage: true);
		filterRow2.AddChild(_usageRoleFilter);

		var filterRow3 = new HBoxContainer();
		filterRow3.AddThemeConstantOverride("separation", 8);
		content.AddChild(filterRow3);

		_placementFilter = new OptionButton
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		_placementFilter.ItemSelected += _ => RefreshBrowser(resetPage: true);
		filterRow3.AddChild(_placementFilter);

		_variantGroupFilter = new OptionButton
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		_variantGroupFilter.ItemSelected += _ => RefreshBrowser(resetPage: true);
		filterRow3.AddChild(_variantGroupFilter);

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

		var browseButton = new Button
		{
			Text = "浏览...",
		};
		browseButton.Pressed += () =>
		{
			SetActiveSlot(slot);
			_fileDialog.Popup();
		};
		content.AddChild(browseButton);

		return (button, preview, titleLabel, metaLabel);
	}

	private static (HSlider Slider, Label Label) BuildSliderRow(Control parent, string name, float min, float max, float value, Action<float> onChange)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		parent.AddChild(row);

		var isScale = name.Contains("缩放", StringComparison.Ordinal);
		var label = new Label
		{
			Text = isScale ? $"{name}: {value:F2}" : $"{name}: {value:F0}",
			CustomMinimumSize = new Vector2(100, 0),
		};
		row.AddChild(label);

		var slider = new HSlider
		{
			MinValue = min,
			MaxValue = max,
			Step = isScale ? 0.05f : 1f,
			Value = value,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(150, 0),
		};
		slider.ValueChanged += v =>
		{
			label.Text = isScale ? $"{name}: {v:F2}" : $"{name}: {v:F0}";
			onChange((float)v);
		};
		row.AddChild(slider);

		return (slider, label);
	}

	private void OnTerrainSelected(long index)
	{
		if (_isRefreshingUi)
			return;

		SelectTerrain((int)index);
	}

	private void SwitchCategory(string category)
	{
		if (string.Equals(_activeCategory, category, StringComparison.OrdinalIgnoreCase))
			return;

		_activeCategory = category;
		foreach (var pair in _categoryButtons)
			pair.Value.SetPressedNoSignal(string.Equals(pair.Key, category, StringComparison.OrdinalIgnoreCase));

		_selectedTerrainIndex = 0;
		RebuildTerrainList();
		if (_entryIds.Count > 0)
			SelectTerrain(0);
		else
		{
			RefreshSelectionState();
			RefreshBrowser(resetPage: true);
		}
	}

	private void OnAddNewEntry()
	{
		var dialog = new AcceptDialog { Title = "新增条目" };
		var vbox = new VBoxContainer();
		dialog.AddChild(vbox);

		vbox.AddChild(new Label { Text = "StringId:" });
		var idEdit = new LineEdit
		{
			PlaceholderText = "例: my_terrain",
			CustomMinimumSize = new Vector2(250, 0),
		};
		vbox.AddChild(idEdit);

		if (string.Equals(_activeCategory, TerrainCategory, StringComparison.OrdinalIgnoreCase))
		{
			vbox.AddChild(new HSeparator());
			vbox.AddChild(new Label { Text = "以下为地形属性 (可选, 留空用默认值)" });

			vbox.AddChild(new Label { Text = "Glyph:" });
			var glyphEdit = new LineEdit { PlaceholderText = "#", CustomMinimumSize = new Vector2(250, 0) };
			vbox.AddChild(glyphEdit);

			vbox.AddChild(new Label { Text = "Material:" });
			var materialEdit = new LineEdit { PlaceholderText = "stone", CustomMinimumSize = new Vector2(250, 0) };
			vbox.AddChild(materialEdit);

			vbox.AddChild(new Label { Text = "Hardness (0-255):" });
			var hardnessEdit = new SpinBox { MinValue = 0, MaxValue = 255, Step = 1, Value = 0 };
			vbox.AddChild(hardnessEdit);

			var solidCheck = new CheckBox { Text = "Solid", ButtonPressed = false };
			vbox.AddChild(solidCheck);

			dialog.Confirmed += () =>
			{
				var newId = idEdit.Text.Trim();
				if (string.IsNullOrWhiteSpace(newId))
					return;

				RegisterNewTerrain(
					newId,
					string.IsNullOrWhiteSpace(glyphEdit.Text) ? "#" : glyphEdit.Text.Trim(),
					string.IsNullOrWhiteSpace(materialEdit.Text) ? "stone" : materialEdit.Text.Trim(),
					(byte)hardnessEdit.Value,
					solidCheck.ButtonPressed);
				dialog.QueueFree();
			};
		}
		else
		{
			dialog.Confirmed += () =>
			{
				var newId = idEdit.Text.Trim();
				if (string.IsNullOrWhiteSpace(newId))
					return;

				RegisterNewNonTerrain(newId, _activeCategory);
				dialog.QueueFree();
			};
		}

		dialog.Size = new Vector2I(340, 0);
		AddChild(dialog);
		dialog.PopupCentered();
	}

	private void RegisterNewTerrain(string stringId, string glyph, string material, byte hardness, bool solid)
	{
		if (TerrainRegistry.Get(stringId) != null)
		{
			_mappingStatusLabel.Text = $"地形 '{stringId}' 已存在";
			return;
		}

		ushort nextId = 0;
		foreach (var terrain in TerrainRegistry.All)
		{
			if (terrain != null && terrain.Id >= nextId)
				nextId = (ushort)(terrain.Id + 1);
		}

		var terrainDef = new TerrainDef
		{
			Id = nextId,
			StringId = stringId,
			Glyph = glyph,
			DefaultHardness = hardness,
			Solid = solid,
			Material = material,
		};
		TerrainRegistry.Register(terrainDef);
		SaveTerrainsJson();

		if (!_mappings.ContainsKey(stringId))
			_mappings[stringId] = new VoxelTileMappingEntry { TerrainId = stringId, Category = TerrainCategory };

		_terrains.Add(terrainDef);
		_selectedTerrainIndex = 0;
		_activeCategory = TerrainCategory;
		foreach (var pair in _categoryButtons)
			pair.Value.SetPressedNoSignal(string.Equals(pair.Key, TerrainCategory, StringComparison.OrdinalIgnoreCase));
		RebuildTerrainList();
		var index = _entryIds.FindIndex(id => string.Equals(id, stringId, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
			SelectTerrain(index);
	}

	private void RegisterNewNonTerrain(string stringId, string category)
	{
		if (_mappings.ContainsKey(stringId))
		{
			_mappingStatusLabel.Text = $"'{stringId}' 已存在";
			return;
		}

		_mappings[stringId] = new VoxelTileMappingEntry
		{
			TerrainId = stringId,
			Category = category,
		};

		RebuildTerrainList();
		var index = _entryIds.FindIndex(id => string.Equals(id, stringId, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
			SelectTerrain(index);
	}

	private void SaveTerrainsJson()
	{
		var terrains = new List<object>();
		foreach (var terrain in TerrainRegistry.All)
		{
			if (terrain == null)
				continue;

			var payload = new Dictionary<string, object?>
			{
				["Id"] = terrain.Id,
				["StringId"] = terrain.StringId,
				["Glyph"] = terrain.Glyph,
				["DefaultHardness"] = terrain.DefaultHardness,
				["Solid"] = terrain.Solid,
				["Material"] = terrain.Material,
			};

			if (terrain.IsOpaque != terrain.Solid)
				payload["IsOpaque"] = terrain.IsOpaque;
			if (!string.IsNullOrWhiteSpace(terrain.BreaksInto) && !string.Equals(terrain.BreaksInto, "rubble", StringComparison.OrdinalIgnoreCase))
				payload["BreaksInto"] = terrain.BreaksInto;
			if (!string.IsNullOrWhiteSpace(terrain.TopTile))
				payload["TopTile"] = terrain.TopTile;
			if (!string.IsNullOrWhiteSpace(terrain.SideTile))
				payload["SideTile"] = terrain.SideTile;

			terrains.Add(payload);
		}

		var outputPath = GameDataLocator.GetProjectDataPathOrThrow("terrains.json");
		var directory = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
			Directory.CreateDirectory(directory);

		File.WriteAllText(outputPath, JsonSerializer.Serialize(terrains, JsonWriteOptions));
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
		_worldRegistry = LoadWorldRegistryOrDefault();
		_catalogByPath.Clear();
		foreach (var entry in _catalog.Entries)
		{
			var normalizedPath = PzTilePathUtility.NormalizeAssetPath(entry.Path);
			if (!string.IsNullOrWhiteSpace(normalizedPath))
				_catalogByPath[normalizedPath] = entry;
		}

		_mappings.Clear();
		var mappingDocument = _worldRegistry.Terrain.Count > 0
			? PzWorldVisualRegistryStore.GenerateVoxelTileMappingDocument(_worldRegistry, _catalog)
			: VoxelTileMappingStore.Load();
		var legacyMappingDocument = VoxelTileMappingStore.Load();
		foreach (var entry in mappingDocument.Entries)
		{
			if (!string.IsNullOrWhiteSpace(entry.TerrainId))
				_mappings[entry.TerrainId] = entry;
		}
		foreach (var entry in legacyMappingDocument.Entries.Where(entry => !string.Equals(entry.Category, TerrainCategory, StringComparison.OrdinalIgnoreCase)))
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

	private PzWorldVisualRegistryDocument LoadWorldRegistryOrDefault()
	{
		try
		{
			var registryPath = PzWorldVisualRegistryStore.GetProjectFilePath();
			if (File.Exists(registryPath))
				return PzWorldVisualRegistryStore.Load();
		}
		catch
		{
			// Fall back to legacy-only loading so the preview tool stays usable.
		}

		return new PzWorldVisualRegistryDocument();
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

		_usageDomainFilter.Clear();
		AddFilterItem(_usageDomainFilter, "全部用途域", string.Empty);
		foreach (var pair in GetListValueCounts(static entry => entry.UsageDomains))
			AddFilterItem(_usageDomainFilter, $"{pair.Key} ({pair.Value})", pair.Key);
		_usageDomainFilter.Select(0);

		_usageRoleFilter.Clear();
		AddFilterItem(_usageRoleFilter, "全部用途角色", string.Empty);
		foreach (var pair in GetListValueCounts(static entry => entry.UsageRoles))
			AddFilterItem(_usageRoleFilter, $"{pair.Key} ({pair.Value})", pair.Key);
		_usageRoleFilter.Select(0);

		_placementFilter.Clear();
		AddFilterItem(_placementFilter, "全部放置面", string.Empty);
		foreach (var pair in GetScalarValueCounts(static entry => entry.Placement))
			AddFilterItem(_placementFilter, $"{pair.Key} ({pair.Value})", pair.Key);
		_placementFilter.Select(0);

		_variantGroupFilter.Clear();
		AddFilterItem(_variantGroupFilter, "全部变体组", string.Empty);
		foreach (var pair in GetScalarValueCounts(static entry => entry.VariantGroup))
			AddFilterItem(_variantGroupFilter, $"{pair.Key} ({pair.Value})", pair.Key);
		_variantGroupFilter.Select(0);
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
		var selectedTerrainName = selectedTerrain != null
			? $"{GameLocalizer.LocalizeTerrainName(selectedTerrain.StringId)} ({selectedTerrain.StringId})"
			: GetSelectedEntryId() ?? "未选中条目";
		_browserSummaryLabel.Text =
			$"资源浏览器: {_filteredEntries.Count}/{_catalog.Entries.Count}\n" +
			$"当前槽位: {GetSlotLabel(_activeSlot)}\n" +
			$"当前条目: {selectedTerrainName}";

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
		_entryIds.Clear();
		if (string.Equals(_activeCategory, TerrainCategory, StringComparison.OrdinalIgnoreCase))
		{
			foreach (var terrain in _terrains)
				_entryIds.Add(terrain.StringId);

			foreach (var extraId in _mappings.Values
				.Where(static entry => string.Equals(entry.Category, TerrainCategory, StringComparison.OrdinalIgnoreCase))
				.Select(static entry => entry.TerrainId)
				.Where(id => !_entryIds.Contains(id, StringComparer.OrdinalIgnoreCase))
				.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase))
			{
				_entryIds.Add(extraId);
			}
		}
		else
		{
			_entryIds.AddRange(_mappings.Values
				.Where(entry => string.Equals(entry.Category, _activeCategory, StringComparison.OrdinalIgnoreCase))
				.Select(static entry => entry.TerrainId)
				.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase));
		}

		for (var i = 0; i < _entryIds.Count; i++)
		{
			var entryId = _entryIds[i];
			var status = CalculateMappingStatus(entryId);
			if (string.Equals(_activeCategory, TerrainCategory, StringComparison.OrdinalIgnoreCase) && TerrainRegistry.Get(entryId) is { } terrain)
			{
				_terrainList.AddItem(BuildTerrainListLabel(terrain, status));
				_terrainList.SetItemTooltip(i, $"{GameLocalizer.LocalizeTerrainName(terrain.StringId)}\nstringId: {terrain.StringId}\n状态: {status.DisplayText}");
			}
			else
			{
				_terrainList.AddItem($"{entryId}\n{_activeCategory} · {status.DisplayText}");
				_terrainList.SetItemTooltip(i, $"stringId: {entryId}\ncategory: {_activeCategory}\n状态: {status.DisplayText}");
			}

			_terrainList.SetItemCustomFgColor(i, GetTerrainStatusColor(status));
		}

		if (_entryIds.Count > 0)
		{
			_selectedTerrainIndex = Math.Clamp(_selectedTerrainIndex, 0, _entryIds.Count - 1);
			_terrainList.Select(_selectedTerrainIndex);
		}

		if (string.Equals(_activeCategory, TerrainCategory, StringComparison.OrdinalIgnoreCase))
		{
			var completed = _terrains.Count(terrain => CalculateMappingStatus(terrain.StringId).IsComplete);
			var invalid = _terrains.Count(terrain => CalculateMappingStatus(terrain.StringId).HasInvalidReference);
			_terrainListSummaryLabel.Text =
				$"terrain 映射: {completed}/{_terrains.Count}\n" +
				$"失效引用: {invalid}\n" +
				$"catalog 资源: {_catalog.Entries.Count}";
		}
		else
		{
			_terrainListSummaryLabel.Text =
				$"{_activeCategory} 条目: {_entryIds.Count}\n" +
				$"已有映射: {_entryIds.Count(id => _mappings.ContainsKey(id))}\n" +
				$"catalog 资源: {_catalog.Entries.Count}";
		}
		_isRefreshingUi = false;
	}

	private void SelectTerrain(int index)
	{
		if (_entryIds.Count == 0)
		{
			_selectedTerrainIndex = 0;
			RefreshSelectionState();
			return;
		}

		_selectedTerrainIndex = Math.Clamp(index, 0, _entryIds.Count - 1);
		_terrainList.Select(_selectedTerrainIndex);
		RefreshSelectionState();
		RefreshBrowser(resetPage: false);
	}

	private void RefreshSelectionState()
	{
		var entryId = GetSelectedEntryId();
		if (string.IsNullOrWhiteSpace(entryId))
		{
			_mappingStatusLabel.Text = $"当前分类: {_activeCategory}\n(无条目)";
			_globalStatusLabel.Text = "请先加载 terrain 和 catalog 数据。";
			ApplySlotView(_topSlotPreview, _topSlotTitleLabel, _topSlotMetaLabel, new SlotViewModel("未设置", "无可用条目", null));
			ApplySlotView(_leftSlotPreview, _leftSlotTitleLabel, _leftSlotMetaLabel, new SlotViewModel("未设置", "无可用条目", null));
			ApplySlotView(_rightSlotPreview, _rightSlotTitleLabel, _rightSlotMetaLabel, new SlotViewModel("未设置", "无可用条目", null));
			SyncEditorControls(null);
			RefreshPreview();
			return;
		}

		var terrain = GetSelectedTerrain();
		var mapping = GetSelectedMapping();
		var status = CalculateMappingStatus(entryId);
		_mappingStatusLabel.Text = terrain != null
			? $"{GameLocalizer.LocalizeTerrainName(terrain.StringId)}\nstringId: {terrain.StringId}\n状态: {status.DisplayText}"
			: $"{entryId}\ncategory: {_activeCategory}\n状态: {status.DisplayText}";
		_globalStatusLabel.Text = BuildGlobalStatusText(entryId, terrain, mapping, status);

		ApplySlotView(_topSlotPreview, _topSlotTitleLabel, _topSlotMetaLabel, DescribeSlot(FaceSlot.Top, mapping));
		ApplySlotView(_leftSlotPreview, _leftSlotTitleLabel, _leftSlotMetaLabel, DescribeSlot(FaceSlot.LeftSide, mapping));
		ApplySlotView(_rightSlotPreview, _rightSlotTitleLabel, _rightSlotMetaLabel, DescribeSlot(FaceSlot.RightSide, mapping));

		var sideColor = GetPreferredSideColor(mapping, terrain, entryId);
		_sideColorPicker.Color = sideColor;
		SyncEditorControls(mapping);
		RefreshPreview();
	}

	private void ApplyColorToActiveSide()
	{
		if (_activeSlot == FaceSlot.Top)
			return;

		var entryId = GetSelectedEntryId();
		if (string.IsNullOrWhiteSpace(entryId))
			return;

		var mapping = EnsureMappingEntry(entryId);
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
		RefreshBrowser(resetPage: false);
	}

	private void ClearActiveSlot()
	{
		var entryId = GetSelectedEntryId();
		if (string.IsNullOrWhiteSpace(entryId))
			return;

		var mapping = EnsureMappingEntry(entryId);
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
		RefreshBrowser(resetPage: false);
	}

	private void ApplyEyedropperColor(PzTileCatalogEntry catalogEntry)
	{
		var image = GetImage(catalogEntry.Path);
		if (image == null)
		{
			_eyedropperMode = false;
			_eyedropperButton.SetPressedNoSignal(false);
			return;
		}

		var centerX = Math.Clamp(image.GetWidth() / 2, 0, image.GetWidth() - 1);
		var centerY = Math.Clamp(image.GetHeight() / 2, 0, image.GetHeight() - 1);
		var centerColor = image.GetPixel(centerX, centerY);
		var sampledColor = centerColor;
		if (centerColor.A <= 0.01f)
		{
			var bestDistance = int.MaxValue;
			var found = false;
			for (var y = 0; y < image.GetHeight(); y++)
			for (var x = 0; x < image.GetWidth(); x++)
			{
				var candidate = image.GetPixel(x, y);
				if (candidate.A <= 0.01f)
					continue;

				var dx = x - centerX;
				var dy = y - centerY;
				var distance = dx * dx + dy * dy;
				if (distance >= bestDistance)
					continue;

				bestDistance = distance;
				sampledColor = candidate;
				found = true;
			}

			if (!found)
				sampledColor = centerColor;
		}

		_sideColorPicker.Color = sampledColor;
		if (_activeSlot != FaceSlot.Top)
			ApplyColorToActiveSide();

		_eyedropperMode = false;
		_eyedropperButton.SetPressedNoSignal(false);
	}

	private void OnFileDialogSelected(string path)
	{
		if (!string.IsNullOrWhiteSpace(path))
			ApplyCatalogEntryToActiveSlot(new PzTileCatalogEntry { Path = path });
	}

	private void OnSideVisibilityChanged()
	{
		var entryId = GetSelectedEntryId();
		if (string.IsNullOrWhiteSpace(entryId))
			return;

		var mapping = EnsureMappingEntry(entryId);
		mapping.ShowLeftSide = _showLeftCheck.ButtonPressed;
		mapping.ShowRightSide = _showRightCheck.ButtonPressed;
		RebuildTerrainList();
		RefreshSelectionState();
	}

	private void OnTopParamsChanged()
	{
		var entryId = GetSelectedEntryId();
		if (string.IsNullOrWhiteSpace(entryId))
			return;

		var mapping = EnsureMappingEntry(entryId);
		mapping.TopScaleX = (float)_topScaleXSlider.Value;
		mapping.TopScaleY = (float)_topScaleYSlider.Value;
		mapping.TopOffsetX = (float)_topOffsetXSlider.Value;
		mapping.TopOffsetY = (float)_topOffsetYSlider.Value;
		RefreshPreview();
	}

	private void OnLeftSideParamsChanged()
	{
		var entryId = GetSelectedEntryId();
		if (string.IsNullOrWhiteSpace(entryId))
			return;

		var mapping = EnsureMappingEntry(entryId);
		mapping.LeftOffsetX = (float)_leftOffsetXSlider.Value;
		mapping.LeftOffsetY = (float)_leftOffsetYSlider.Value;
		mapping.LeftHeight = (int)_leftHeightSlider.Value;
		RefreshPreview();
	}

	private void OnRightSideParamsChanged()
	{
		var entryId = GetSelectedEntryId();
		if (string.IsNullOrWhiteSpace(entryId))
			return;

		var mapping = EnsureMappingEntry(entryId);
		mapping.RightOffsetX = (float)_rightOffsetXSlider.Value;
		mapping.RightOffsetY = (float)_rightOffsetYSlider.Value;
		mapping.RightHeight = (int)_rightHeightSlider.Value;
		RefreshPreview();
	}

	private void OnResetParams()
	{
		var entryId = GetSelectedEntryId();
		if (string.IsNullOrWhiteSpace(entryId))
			return;

		var mapping = EnsureMappingEntry(entryId);
		mapping.TopScaleX = 1.0f;
		mapping.TopScaleY = 1.0f;
		mapping.TopOffsetX = 0f;
		mapping.TopOffsetY = 0f;
		mapping.LeftOffsetX = 0f;
		mapping.LeftOffsetY = 0f;
		mapping.LeftHeight = 0;
		mapping.RightOffsetX = 0f;
		mapping.RightOffsetY = 0f;
		mapping.RightHeight = 0;
		mapping.ShowLeftSide = true;
		mapping.ShowRightSide = true;
		RebuildTerrainList();
		RefreshSelectionState();
	}

	private void SyncEditorControls(VoxelTileMappingEntry? mapping)
	{
		var topScaleX = mapping?.TopScaleX ?? 1.0f;
		var topScaleY = mapping?.TopScaleY ?? 1.0f;
		var topOffsetX = mapping?.TopOffsetX ?? 0f;
		var topOffsetY = mapping?.TopOffsetY ?? 0f;
		var leftOffsetX = mapping?.LeftOffsetX ?? 0f;
		var leftOffsetY = mapping?.LeftOffsetY ?? 0f;
		var leftHeight = mapping?.LeftHeight ?? 0;
		var rightOffsetX = mapping?.RightOffsetX ?? 0f;
		var rightOffsetY = mapping?.RightOffsetY ?? 0f;
		var rightHeight = mapping?.RightHeight ?? 0;
		var showLeft = mapping?.ShowLeftSide ?? true;
		var showRight = mapping?.ShowRightSide ?? true;

		_showLeftCheck.SetPressedNoSignal(showLeft);
		_showRightCheck.SetPressedNoSignal(showRight);
		_topScaleXSlider.SetValueNoSignal(topScaleX);
		_topScaleYSlider.SetValueNoSignal(topScaleY);
		_topOffsetXSlider.SetValueNoSignal(topOffsetX);
		_topOffsetYSlider.SetValueNoSignal(topOffsetY);
		_leftOffsetXSlider.SetValueNoSignal(leftOffsetX);
		_leftOffsetYSlider.SetValueNoSignal(leftOffsetY);
		_leftHeightSlider.SetValueNoSignal(leftHeight > 0 ? leftHeight : VoxelFaceImageUtil.SideFaceHeight);
		_rightOffsetXSlider.SetValueNoSignal(rightOffsetX);
		_rightOffsetYSlider.SetValueNoSignal(rightOffsetY);
		_rightHeightSlider.SetValueNoSignal(rightHeight > 0 ? rightHeight : VoxelFaceImageUtil.SideFaceHeight);

		_topScaleXLabel.Text = $"顶缩放X: {topScaleX:F2}";
		_topScaleYLabel.Text = $"顶缩放Y: {topScaleY:F2}";
		_topOffsetXLabel.Text = $"顶偏移X: {topOffsetX:F0}";
		_topOffsetYLabel.Text = $"顶偏移Y: {topOffsetY:F0}";
		_leftOffsetXLabel.Text = $"左偏移X: {leftOffsetX:F0}";
		_leftOffsetYLabel.Text = $"左偏移Y: {leftOffsetY:F0}";
		_leftHeightLabel.Text = $"左侧高度: {(leftHeight > 0 ? leftHeight : VoxelFaceImageUtil.SideFaceHeight):F0}";
		_rightOffsetXLabel.Text = $"右偏移X: {rightOffsetX:F0}";
		_rightOffsetYLabel.Text = $"右偏移Y: {rightOffsetY:F0}";
		_rightHeightLabel.Text = $"右侧高度: {(rightHeight > 0 ? rightHeight : VoxelFaceImageUtil.SideFaceHeight):F0}";
	}

	private void SaveMappings()
	{
		_worldRegistry.Terrain = BuildTerrainRegistryEntries();
		WriteWorldRegistryAndCompatFiles();
		RefreshSelectionState();
	}

	private List<PzTerrainVisualEntry> BuildTerrainRegistryEntries()
	{
		return _mappings.Values
			.Where(entry => string.Equals(entry.Category, TerrainCategory, StringComparison.OrdinalIgnoreCase))
			.OrderBy(static entry => entry.TerrainId, StringComparer.OrdinalIgnoreCase)
			.Select(entry => new PzTerrainVisualEntry
			{
				TerrainId = entry.TerrainId,
				TopCatalogId = FindCatalogId(entry.TopTilePath),
				TopPath = NormalizePath(entry.TopTilePath),
				TopIsIso = entry.TopIsIso,
				TopScaleX = entry.TopScaleX,
				TopScaleY = entry.TopScaleY,
				TopOffsetX = entry.TopOffsetX,
				TopOffsetY = entry.TopOffsetY,
				Left = new PzTerrainSideVisualSpec
				{
					Mode = entry.LeftSideMode,
					CatalogId = FindCatalogId(entry.LeftSideTilePath),
					Path = NormalizePath(entry.LeftSideTilePath),
					Color = entry.LeftSideColor,
					IsIso = entry.LeftIsIso,
					OffsetX = entry.LeftOffsetX,
					OffsetY = entry.LeftOffsetY,
					Height = entry.LeftHeight,
					Visible = entry.ShowLeftSide,
				},
				Right = new PzTerrainSideVisualSpec
				{
					Mode = entry.RightSideMode,
					CatalogId = FindCatalogId(entry.RightSideTilePath),
					Path = NormalizePath(entry.RightSideTilePath),
					Color = entry.RightSideColor,
					IsIso = entry.RightIsIso,
					OffsetX = entry.RightOffsetX,
					OffsetY = entry.RightOffsetY,
					Height = entry.RightHeight,
					Visible = entry.ShowRightSide,
				},
			})
			.ToList();
	}

	private void WriteWorldRegistryAndCompatFiles()
	{
		var registryPath = PzWorldVisualRegistryStore.GetProjectFilePath();
		EnsureParentDirectory(registryPath);
		File.WriteAllText(registryPath, PzWorldVisualRegistryStore.Serialize(_worldRegistry));

		var terrainCompatDocument = PzWorldVisualRegistryStore.GenerateVoxelTileMappingDocument(_worldRegistry, _catalog);
		foreach (var entry in _mappings.Values
			.Where(entry => !string.Equals(entry.Category, TerrainCategory, StringComparison.OrdinalIgnoreCase))
			.OrderBy(static entry => entry.TerrainId, StringComparer.OrdinalIgnoreCase))
		{
			terrainCompatDocument.Entries.Add(entry);
		}
		terrainCompatDocument.Entries = terrainCompatDocument.Entries
			.OrderBy(static entry => entry.Category, StringComparer.OrdinalIgnoreCase)
			.ThenBy(static entry => entry.TerrainId, StringComparer.OrdinalIgnoreCase)
			.ToList();

		var voxelMappingPath = VoxelTileMappingStore.GetProjectFilePath();
		EnsureParentDirectory(voxelMappingPath);
		File.WriteAllText(voxelMappingPath, VoxelTileMappingStore.Serialize(terrainCompatDocument));

		var itemWorldCompat = PzWorldVisualRegistryStore.GenerateItemWorldRenderConfig(_worldRegistry, _catalog);
		var itemWorldPath = GameDataLocator.GetProjectDataPathOrThrow("item_world_render.json");
		EnsureParentDirectory(itemWorldPath);
		File.WriteAllText(itemWorldPath, JsonSerializer.Serialize(itemWorldCompat, JsonWriteOptions));

		var legacyTileMapping = PzWorldVisualRegistryStore.GenerateLegacyTileMappingDocument(_worldRegistry, _catalog);
		var tileMappingPath = GameDataLocator.GetProjectDataPathOrThrow("tile_mapping.json");
		EnsureParentDirectory(tileMappingPath);
		File.WriteAllText(tileMappingPath, PzWorldVisualRegistryStore.SerializeLegacyTileMapping(legacyTileMapping));
	}

	private string? FindCatalogId(string? path)
	{
		var normalizedPath = NormalizePath(path);
		return _catalogByPath.TryGetValue(normalizedPath, out var entry) ? entry.Id : null;
	}

	private static void EnsureParentDirectory(string filePath)
	{
		var directory = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
			Directory.CreateDirectory(directory);
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

	private SortedDictionary<string, int> GetListValueCounts(Func<PzTileCatalogEntry, IEnumerable<string>> selector)
	{
		var counts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (var entry in _catalog.Entries)
		{
			foreach (var value in selector(entry))
			{
				if (!ShouldExposeTag(value))
					continue;
				counts[value] = counts.TryGetValue(value, out var count) ? count + 1 : 1;
			}
		}

		return counts;
	}

	private SortedDictionary<string, int> GetScalarValueCounts(Func<PzTileCatalogEntry, string> selector)
	{
		var counts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (var entry in _catalog.Entries)
		{
			var value = selector(entry);
			if (!ShouldExposeTag(value))
				continue;
			counts[value] = counts.TryGetValue(value, out var count) ? count + 1 : 1;
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

		var usageDomainValue = GetSelectedFilterValue(_usageDomainFilter);
		if (!string.IsNullOrWhiteSpace(usageDomainValue)
			&& !entry.UsageDomains.Any(value => string.Equals(value, usageDomainValue, StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}

		var usageRoleValue = GetSelectedFilterValue(_usageRoleFilter);
		if (!string.IsNullOrWhiteSpace(usageRoleValue)
			&& !entry.UsageRoles.Any(value => string.Equals(value, usageRoleValue, StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}

		var placementValue = GetSelectedFilterValue(_placementFilter);
		if (!string.IsNullOrWhiteSpace(placementValue)
			&& !string.Equals(entry.Placement, placementValue, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var variantGroupValue = GetSelectedFilterValue(_variantGroupFilter);
		if (!string.IsNullOrWhiteSpace(variantGroupValue)
			&& !string.Equals(entry.VariantGroup, variantGroupValue, StringComparison.OrdinalIgnoreCase))
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
			entry.Placement,
			entry.VariantGroup,
			string.Join(' ', entry.Tags),
			string.Join(' ', entry.UsageDomains),
			string.Join(' ', entry.UsageRoles));
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
		if (_eyedropperMode)
		{
			ApplyEyedropperColor(catalogEntry);
			return;
		}

		var entryId = GetSelectedEntryId();
		if (string.IsNullOrWhiteSpace(entryId))
			return;

		var mapping = EnsureMappingEntry(entryId);
		var normalizedPath = NormalizePath(catalogEntry.Path);
		var isIso = PzTilePathUtility.IsPzTilesAssetPath(normalizedPath);
		switch (_activeSlot)
		{
			case FaceSlot.Top:
				mapping.TopTilePath = normalizedPath;
				mapping.TopIsIso = isIso;
				break;
			case FaceSlot.LeftSide:
				mapping.LeftSideMode = "texture";
				mapping.LeftSideTilePath = normalizedPath;
				mapping.LeftSideColor = null;
				mapping.LeftIsIso = isIso;
				break;
			case FaceSlot.RightSide:
				mapping.RightSideMode = "texture";
				mapping.RightSideTilePath = normalizedPath;
				mapping.RightSideColor = null;
				mapping.RightIsIso = isIso;
				break;
		}

		RebuildTerrainList();
		RefreshSelectionState();
		RefreshBrowser(resetPage: false);
	}

	private VoxelTileMappingEntry EnsureMappingEntry(string terrainId)
	{
		if (_mappings.TryGetValue(terrainId, out var existing))
		{
			if (string.IsNullOrWhiteSpace(existing.Category))
				existing.Category = _activeCategory;
			return existing;
		}

		var created = new VoxelTileMappingEntry
		{
			TerrainId = terrainId,
			Category = _activeCategory,
		};
		_mappings[terrainId] = created;
		return created;
	}

	private TerrainDef? GetSelectedTerrain()
	{
		var entryId = GetSelectedEntryId();
		if (string.IsNullOrWhiteSpace(entryId))
			return null;
		if (!string.Equals(_activeCategory, TerrainCategory, StringComparison.OrdinalIgnoreCase))
			return null;

		return TerrainRegistry.Get(entryId);
	}

	private VoxelTileMappingEntry? GetSelectedMapping()
	{
		var entryId = GetSelectedEntryId();
		if (string.IsNullOrWhiteSpace(entryId))
			return null;

		return _mappings.GetValueOrDefault(entryId);
	}

	private string? GetSelectedEntryId()
	{
		if (_entryIds.Count == 0 || _selectedTerrainIndex < 0 || _selectedTerrainIndex >= _entryIds.Count)
			return null;

		return _entryIds[_selectedTerrainIndex];
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

	private string BuildGlobalStatusText(string entryId, TerrainDef? terrain, VoxelTileMappingEntry? mapping, MappingStatus status)
	{
		var top = DescribeSlot(FaceSlot.Top, mapping);
		var left = DescribeSlot(FaceSlot.LeftSide, mapping);
		var right = DescribeSlot(FaceSlot.RightSide, mapping);
		if (terrain != null && string.Equals(_activeCategory, TerrainCategory, StringComparison.OrdinalIgnoreCase))
		{
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

		return
			$"条目: {entryId}\n" +
			$"顶面: {top.SummaryText}\n" +
			$"左侧: {left.SummaryText}\n" +
			$"右侧: {right.SummaryText}\n" +
			$"当前条目完整: {(status.IsComplete ? "是" : "否")}\n" +
			$"当前分类条目数: {_entryIds.Count}";
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

	private Color GetPreferredSideColor(VoxelTileMappingEntry? mapping, TerrainDef? terrain, string entryId)
	{
		if (_activeSlot == FaceSlot.LeftSide && TryParseColor(mapping?.LeftSideColor, out var leftColor))
			return leftColor;
		if (_activeSlot == FaceSlot.RightSide && TryParseColor(mapping?.RightSideColor, out var rightColor))
			return rightColor;
		if (TryParseColor(mapping?.LeftSideColor, out leftColor))
			return leftColor;
		if (TryParseColor(mapping?.RightSideColor, out rightColor))
			return rightColor;
		return GetFallbackColor(terrain?.StringId ?? entryId);
	}

	private void RefreshPreview()
	{
		var entryId = GetSelectedEntryId();
		if (string.IsNullOrWhiteSpace(entryId))
		{
			_topSprite.Visible = false;
			_leftSprite.Visible = false;
			_rightSprite.Visible = false;
			return;
		}

		var terrain = GetSelectedTerrain();
		var mapping = GetSelectedMapping();
		var topScaleX = mapping?.TopScaleX ?? 1.0f;
		var topScaleY = mapping?.TopScaleY ?? 1.0f;
		var topOffsetX = mapping?.TopOffsetX ?? 0f;
		var topOffsetY = mapping?.TopOffsetY ?? 0f;
		var leftOffsetX = mapping?.LeftOffsetX ?? 0f;
		var leftOffsetY = mapping?.LeftOffsetY ?? 0f;
		var rightOffsetX = mapping?.RightOffsetX ?? 0f;
		var rightOffsetY = mapping?.RightOffsetY ?? 0f;
		var leftHeight = mapping?.LeftHeight ?? 0;
		var rightHeight = mapping?.RightHeight ?? 0;
		var showLeft = mapping?.ShowLeftSide ?? true;
		var showRight = mapping?.ShowRightSide ?? true;
		var effectiveLeftHeight = leftHeight > 0 ? leftHeight : VoxelFaceImageUtil.SideFaceHeight;
		var effectiveRightHeight = rightHeight > 0 ? rightHeight : VoxelFaceImageUtil.SideFaceHeight;

		var topImage = GetImage(mapping?.TopTilePath);
		var topIsIso = mapping?.TopIsIso == true || PzTilePathUtility.IsPzTilesAssetPath(mapping?.TopTilePath);
		if (topImage != null && topIsIso)
		{
			_topSprite.Texture = ImageTexture.CreateFromImage(topImage);
			var fitScale = Math.Min(256f / Math.Max(1, topImage.GetWidth()), 128f / Math.Max(1, topImage.GetHeight()));
			fitScale = Math.Max(fitScale, PreviewScale);
			_topSprite.Scale = new Vector2(fitScale * topScaleX, fitScale * topScaleY);
			_topSprite.Position = new Vector2(320 + topOffsetX, 150 + topOffsetY);
		}
		else if (topImage != null)
		{
			var topDiamond = VoxelFaceImageUtil.EnhanceTopFaceEdges(
				VoxelFaceImageUtil.BuildTopDiamond(topImage),
				terrain != null ? GetTopEdgeStrength(terrain) : NonSolidTopEdgeStrength);
			_topSprite.Texture = ImageTexture.CreateFromImage(topDiamond);
			_topSprite.Scale = new Vector2(PreviewScale * topScaleX, PreviewScale * topScaleY);
			_topSprite.Position = new Vector2(320 + topOffsetX, 150 + topOffsetY);
		}
		else
		{
			var placeholder = CreateDiamondImage(
				VoxelFaceImageUtil.TopFaceWidth,
				VoxelFaceImageUtil.TopFaceHeight,
				GetFallbackColor(entryId));
			_topSprite.Texture = ImageTexture.CreateFromImage(placeholder);
			_topSprite.Scale = new Vector2(PreviewScale * topScaleX, PreviewScale * topScaleY);
			_topSprite.Position = new Vector2(320 + topOffsetX, 150 + topOffsetY);
		}
		_topSprite.Visible = true;

		if (showLeft)
		{
			var leftImage = ResolveSideSourceImage(entryId, mapping, isRight: false);
			var darken = terrain != null && IsWallTerrain(terrain) ? WallLeftSideDarken : LeftDarken;
			var leftFace = VoxelFaceImageUtil.GenerateSideFace(leftImage, false, darken, effectiveLeftHeight);
			VoxelFaceImageUtil.EnhanceSideFaceEdge(leftFace, false, terrain != null ? GetSideEdgeStrength(terrain) : NonSolidSideEdgeStrength);
			_leftSprite.Texture = ImageTexture.CreateFromImage(leftFace);
			_leftSprite.Scale = new Vector2(PreviewScale, PreviewScale);
			_leftSprite.Position = new Vector2(256 + leftOffsetX, 325 + leftOffsetY);
			_leftSprite.Visible = true;
		}
		else
		{
			_leftSprite.Visible = false;
		}

		if (showRight)
		{
			var rightImage = ResolveSideSourceImage(entryId, mapping, isRight: true);
			var darken = terrain != null && IsWallTerrain(terrain) ? WallRightSideDarken : RightDarken;
			var rightFace = VoxelFaceImageUtil.GenerateSideFace(rightImage, true, darken, effectiveRightHeight);
			VoxelFaceImageUtil.EnhanceSideFaceEdge(rightFace, true, terrain != null ? GetSideEdgeStrength(terrain) : NonSolidSideEdgeStrength);
			_rightSprite.Texture = ImageTexture.CreateFromImage(rightFace);
			_rightSprite.Scale = new Vector2(PreviewScale, PreviewScale);
			_rightSprite.Position = new Vector2(384 + rightOffsetX, 325 + rightOffsetY);
			_rightSprite.Visible = true;
		}
		else
		{
			_rightSprite.Visible = false;
		}
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

	private Image ResolveSideSourceImage(string entryId, VoxelTileMappingEntry? mapping, bool isRight)
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
			GetFallbackColor(entryId));
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

	private static readonly JsonSerializerOptions JsonWriteOptions = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};
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

internal partial class GuidesDrawNode : Node2D
{
	public override void _Draw()
	{
		var guideColor = new Color(0f, 0.9f, 0.9f, 0.35f);
		var crossColor = new Color(1f, 1f, 1f, 0.2f);
		const float cx = 320f;
		const float topCy = 150f;
		const float halfW = 128f;
		const float halfH = 64f;

		DrawLine(new Vector2(cx, topCy - halfH), new Vector2(cx + halfW, topCy), guideColor, 1f);
		DrawLine(new Vector2(cx + halfW, topCy), new Vector2(cx, topCy + halfH), guideColor, 1f);
		DrawLine(new Vector2(cx, topCy + halfH), new Vector2(cx - halfW, topCy), guideColor, 1f);
		DrawLine(new Vector2(cx - halfW, topCy), new Vector2(cx, topCy - halfH), guideColor, 1f);

		const float sideH = 176f;
		var leftTop = new Vector2(cx - halfW, topCy);
		var leftBottom = new Vector2(cx - halfW, topCy + sideH);
		var leftMid = new Vector2(cx, topCy + halfH);
		var leftMidBottom = new Vector2(cx, topCy + halfH + sideH);
		DrawLine(leftTop, leftBottom, guideColor, 1f);
		DrawLine(leftMid, leftMidBottom, guideColor, 1f);
		DrawLine(leftBottom, leftMidBottom, guideColor, 1f);

		var rightTop = new Vector2(cx + halfW, topCy);
		var rightBottom = new Vector2(cx + halfW, topCy + sideH);
		var rightMidBottom = new Vector2(cx, topCy + halfH + sideH);
		DrawLine(rightTop, rightBottom, guideColor, 1f);
		DrawLine(leftMid, leftMidBottom, guideColor, 1f);
		DrawLine(rightBottom, rightMidBottom, guideColor, 1f);

		DrawLine(new Vector2(cx - 160, topCy), new Vector2(cx + 160, topCy), crossColor, 1f);
		DrawLine(new Vector2(cx, topCy - 100), new Vector2(cx, topCy + 300), crossColor, 1f);
	}
}
