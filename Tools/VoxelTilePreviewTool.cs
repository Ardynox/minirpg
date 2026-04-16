using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MiniRPG.Core.World;
using MiniRPG.Module.Render;

namespace MiniRPG.Tools;

public partial class VoxelTilePreviewTool : Control
{
	private const string VoxelTileRoot = "res://Assets/Art/Generated/voxel_tiles";
	private const string PzTileRoot = "res://Assets/Art/PZ_Tiles";
	private const string MappingPath = "Data/voxel_tile_mapping.json";

	private static readonly string[] TileScanDirs =
	[
		"res://Assets/Art/Generated/voxel_tiles",
		"res://Assets/Art/Generated/voxel_tiles/ores",
		"res://Assets/Art/PZ_Tiles/floors",
		"res://Assets/Art/PZ_Tiles/floors_exterior_natural_01",
		"res://Assets/Art/PZ_Tiles/floors_interior_tilesandwood_01",
		"res://Assets/Art/PZ_Tiles/floors_burnt_01",
		"res://Assets/Art/PZ_Tiles/floors_rugs_01",
		"res://Assets/Art/PZ_Tiles/walls_exterior_house_01",
		"res://Assets/Art/PZ_Tiles/walls_exterior_house_02",
		"res://Assets/Art/PZ_Tiles/walls_exterior_house_03",
		"res://Assets/Art/PZ_Tiles/walls_exterior_wooden_01",
		"res://Assets/Art/PZ_Tiles/walls_exterior_wooden_02",
		"res://Assets/Art/PZ_Tiles/walls_interior_house_01",
		"res://Assets/Art/PZ_Tiles/walls_interior_house_02",
		"res://Assets/Art/PZ_Tiles/walls_interior_house_03",
		"res://Assets/Art/PZ_Tiles/walls_interior_house_04",
		"res://Assets/Art/PZ_Tiles/walls_interior_house_05",
		"res://Assets/Art/PZ_Tiles/walls_commercial_01",
		"res://Assets/Art/PZ_Tiles/walls_commercial_02",
		"res://Assets/Art/PZ_Tiles/walls_commercial_03",
		"res://Assets/Art/PZ_Tiles/overlays",
		"res://Assets/Art/PZ_Tiles/erosion",
		"res://Assets/Art/PZ_Tiles/carpentry_01",
		"res://Assets/Art/PZ_Tiles/constructedobjects_01",
		"res://Assets/Art/PZ_Tiles/fencing_01",
		"res://Assets/Art/PZ_Tiles/fixtures_doors_01",
		"res://Assets/Art/PZ_Tiles/fixtures_doors_02",
		"res://Assets/Art/PZ_Tiles/fixtures_stairs_01",
		"res://Assets/Art/PZ_Tiles/fixtures_windows_01",
		"res://Assets/Art/PZ_Tiles/fixtures_fireplaces_01",
		"res://Assets/Art/PZ_Tiles/fixtures_bathroom_01",
		"res://Assets/Art/PZ_Tiles/fixtures_bathroom_02",
		"res://Assets/Art/PZ_Tiles/fixtures_counters_01",
		"res://Assets/Art/PZ_Tiles/fixtures_sinks_01",
		"res://Assets/Art/PZ_Tiles/furniture_seating_indoor_01",
		"res://Assets/Art/PZ_Tiles/furniture_seating_indoor_02",
		"res://Assets/Art/PZ_Tiles/furniture_seating_outdoor_01",
		"res://Assets/Art/PZ_Tiles/furniture_storage_01",
		"res://Assets/Art/PZ_Tiles/furniture_storage_02",
		"res://Assets/Art/PZ_Tiles/furniture_tables_high_01",
		"res://Assets/Art/PZ_Tiles/furniture_tables_low_01",
		"res://Assets/Art/PZ_Tiles/furniture_bedding_01",
		"res://Assets/Art/PZ_Tiles/furniture_shelving_01",
		"res://Assets/Art/PZ_Tiles/appliances_cooking_01",
		"res://Assets/Art/PZ_Tiles/appliances_refrigeration_01",
		"res://Assets/Art/PZ_Tiles/appliances_television_01",
		"res://Assets/Art/PZ_Tiles/appliances_misc_01",
		"res://Assets/Art/PZ_Tiles/lighting_indoor_01",
		"res://Assets/Art/PZ_Tiles/lighting_outdoor_01",
		"res://Assets/Art/PZ_Tiles/vegetation_farm_01",
		"res://Assets/Art/PZ_Tiles/vegetation_farming_01",
		"res://Assets/Art/PZ_Tiles/vegetation_ornamental_01",
		"res://Assets/Art/PZ_Tiles/vegetation_indoor_01",
		"res://Assets/Art/PZ_Tiles/Fire_01",
		"res://Assets/Art/PZ_Tiles/Fire_02",
		"res://Assets/Art/PZ_Tiles/Fire_03",
		"res://Assets/Art/PZ_Tiles/roofs_01",
		"res://Assets/Art/PZ_Tiles/roofs_02",
		"res://Assets/Art/PZ_Tiles/street_curbs_01",
		"res://Assets/Art/PZ_Tiles/street_roadsigns_01",
		"res://Assets/Art/PZ_Tiles/weapons_01",
		"res://Assets/Art/PZ_Tiles/food_01",
		"res://Assets/Art/PZ_Tiles/food_02",
		"res://Assets/Art/PZ_Tiles/stashes_01",
		"res://Assets/Art/PZ_Tiles/trash_01",
		"res://Assets/Art/PZ_Tiles/security_01",
		"res://Assets/Art/PZ_Tiles/recreational_01",
		"res://Assets/Art/PZ_Tiles/jumbo_trees",
	];

	private static readonly string[] Categories = ["terrain", "fixture", "entity", "item"];

	private readonly List<string> _terrainIds = [];
	private readonly List<TilePaletteEntry> _palette = [];
	private readonly List<PaletteGroup> _paletteGroups = [];
	private readonly Dictionary<string, VoxelTileMappingEntry> _mappings = new(StringComparer.OrdinalIgnoreCase);

	private string _activeCategory = "terrain";
	private int _selectedTerrain;
	private int _paletteScroll;
	private const int PaletteColumns = 6;
	private const int PaletteTileSize = 64;
	private const int PaletteVisibleRows = 5;

	private enum FaceSlot { Top, LeftSide, RightSide }
	private FaceSlot _activeSlot = FaceSlot.Top;

	private bool _eyedropperMode;
	private Color _sideColor = new(0.45f, 0.45f, 0.45f);

	private Label _statusLabel = null!;
	private Sprite2D _topSprite = null!;
	private Sprite2D _leftSprite = null!;
	private Sprite2D _rightSprite = null!;
	private ItemList _terrainList = null!;
	private ScrollContainer _paletteScroller = null!;
	private TextureRect _topSlotPreview = null!;
	private TextureRect _leftSlotPreview = null!;
	private TextureRect _rightSlotPreview = null!;
	private Button _topSlotBtn = null!;
	private Button _leftSlotBtn = null!;
	private Button _rightSlotBtn = null!;
	private ColorPickerButton _colorPicker = null!;
	private Button _eyedropperBtn = null!;
	private Button _applyColorBtn = null!;
	private Button _saveBtn = null!;
	private Label _paletteLabel = null!;
	private FileDialog _fileDialog = null!;

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
	private Node2D _guidesNode = null!;
	private readonly Dictionary<string, Button> _categoryButtons = new();
	private VBoxContainer _leftPanelVBox = null!;

	public override void _Ready()
	{
		if (TerrainRegistry.All.Count == 0)
		{
			try { TerrainRegistry.Load("terrains.json"); }
			catch { /* standalone mode, terrains not available */ }
		}

		LoadPalette();
		LoadMappings();
		BuildTerrainList();
		BuildUi();
		if (_terrainIds.Count > 0)
			SelectTerrain(0);
	}

	private void LoadPalette()
	{
		_palette.Clear();
		_paletteGroups.Clear();
		foreach (var dir in TileScanDirs)
		{
			if (!DirAccess.DirExistsAbsolute(dir))
				continue;
			var groupName = dir.GetFile();
			var entries = new List<TilePaletteEntry>();
			using var da = DirAccess.Open(dir);
			if (da == null) continue;
			da.ListDirBegin();
			var name = da.GetNext();
			while (!string.IsNullOrEmpty(name))
			{
				if (!da.CurrentIsDir() && name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
					&& !name.EndsWith(".import", StringComparison.OrdinalIgnoreCase))
				{
					var fullPath = $"{dir}/{name}";
					var entry = new TilePaletteEntry(fullPath, name);
					entries.Add(entry);
					_palette.Add(entry);
				}
				name = da.GetNext();
			}
			da.ListDirEnd();
			if (entries.Count > 0)
			{
				entries.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
				_paletteGroups.Add(new PaletteGroup(groupName, entries));
			}
		}
	}

	private void BuildTerrainList()
	{
		_terrainIds.Clear();
		if (_activeCategory == "terrain")
		{
			foreach (var t in TerrainRegistry.All)
			{
				if (string.IsNullOrWhiteSpace(t.StringId)) continue;
				if (t.StringId is Terrains.Void or Terrains.Air) continue;
				_terrainIds.Add(t.StringId);
			}
			foreach (var kv in _mappings)
			{
				if (kv.Value.Category == "terrain" && !_terrainIds.Contains(kv.Key))
					_terrainIds.Add(kv.Key);
			}
			if (_terrainIds.Count == 0)
				_terrainIds.AddRange(["grass_block", "dirt", "stone", "sand", "snow", "gravel"]);
		}
		else
		{
			foreach (var kv in _mappings)
			{
				if (string.Equals(kv.Value.Category, _activeCategory, StringComparison.OrdinalIgnoreCase))
					_terrainIds.Add(kv.Key);
			}
		}
	}

	private void BuildUi()
	{
		var bg = new ColorRect { Color = new Color(0.06f, 0.07f, 0.10f) };
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(bg);

		var root = new MarginContainer();
		root.SetAnchorsPreset(LayoutPreset.FullRect);
		root.AddThemeConstantOverride("margin_left", 12);
		root.AddThemeConstantOverride("margin_top", 12);
		root.AddThemeConstantOverride("margin_right", 12);
		root.AddThemeConstantOverride("margin_bottom", 12);
		AddChild(root);

		var mainH = new HBoxContainer();
		mainH.AddThemeConstantOverride("separation", 10);
		mainH.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		mainH.SizeFlagsVertical = SizeFlags.ExpandFill;
		root.AddChild(mainH);

		BuildLeftPanel(mainH);
		BuildCenterPanel(mainH);
		BuildRightPanel(mainH);

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
	}

	private void BuildLeftPanel(HBoxContainer parent)
	{
		var panel = new PanelContainer
		{
			CustomMinimumSize = new Vector2(200, 0),
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		parent.AddChild(panel);

		_leftPanelVBox = new VBoxContainer();
		_leftPanelVBox.AddThemeConstantOverride("separation", 6);
		panel.AddChild(_leftPanelVBox);

		_leftPanelVBox.AddChild(new Label { Text = "分类" });

		var catRow = new HBoxContainer();
		catRow.AddThemeConstantOverride("separation", 2);
		_leftPanelVBox.AddChild(catRow);

		string[] catLabels = ["地形", "设施", "实体", "物品"];
		for (var i = 0; i < Categories.Length; i++)
		{
			var cat = Categories[i];
			var btn = new Button
			{
				Text = catLabels[i],
				ToggleMode = true,
				ButtonPressed = cat == _activeCategory,
				CustomMinimumSize = new Vector2(44, 26),
			};
			var capturedCat = cat;
			btn.Pressed += () => SwitchCategory(capturedCat);
			catRow.AddChild(btn);
			_categoryButtons[cat] = btn;
		}

		_leftPanelVBox.AddChild(new Label { Text = "列表" });

		_terrainList = new ItemList
		{
			SizeFlagsVertical = SizeFlags.ExpandFill,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(180, 300),
			AllowReselect = true,
			SelectMode = ItemList.SelectModeEnum.Single,
		};
		foreach (var id in _terrainIds)
			_terrainList.AddItem(id);
		_terrainList.ItemSelected += idx => SelectTerrain((int)idx);
		_leftPanelVBox.AddChild(_terrainList);

		var addBtn = new Button { Text = "+ 新增" };
		addBtn.Pressed += OnAddNewEntry;
		_leftPanelVBox.AddChild(addBtn);

		_saveBtn = new Button { Text = "保存映射" };
		_saveBtn.Pressed += SaveMappings;
		_leftPanelVBox.AddChild(_saveBtn);

		_statusLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_leftPanelVBox.AddChild(_statusLabel);
	}

	private void SwitchCategory(string category)
	{
		_activeCategory = category;
		foreach (var kv in _categoryButtons)
			kv.Value.ButtonPressed = kv.Key == category;
		BuildTerrainList();
		RefreshTerrainListUi();
		if (_terrainIds.Count > 0)
			SelectTerrain(0);
		else
			ClearPreview();
	}

	private void RefreshTerrainListUi()
	{
		_terrainList.Clear();
		foreach (var id in _terrainIds)
			_terrainList.AddItem(id);
	}

	private void ClearPreview()
	{
		_topSprite.Visible = false;
		_leftSprite.Visible = false;
		_rightSprite.Visible = false;
		_topSlotPreview.Texture = null;
		_leftSlotPreview.Texture = null;
		_rightSlotPreview.Texture = null;
		_statusLabel.Text = $"当前分类: {_activeCategory}\n(无条目)";
	}

	private void OnAddNewEntry()
	{
		var dialog = new AcceptDialog { Title = "新增条目" };
		var vbox = new VBoxContainer();
		dialog.AddChild(vbox);

		vbox.AddChild(new Label { Text = "StringId:" });
		var idEdit = new LineEdit { PlaceholderText = "例: my_terrain", CustomMinimumSize = new Vector2(250, 0) };
		vbox.AddChild(idEdit);

		if (_activeCategory == "terrain")
		{
			vbox.AddChild(new HSeparator());
			vbox.AddChild(new Label { Text = "以下为地形属性 (可选, 留空用默认值)" });

			vbox.AddChild(new Label { Text = "Glyph:" });
			var glyphEdit = new LineEdit { PlaceholderText = "#", CustomMinimumSize = new Vector2(250, 0) };
			vbox.AddChild(glyphEdit);

			vbox.AddChild(new Label { Text = "Material:" });
			var matEdit = new LineEdit { PlaceholderText = "stone", CustomMinimumSize = new Vector2(250, 0) };
			vbox.AddChild(matEdit);

			vbox.AddChild(new Label { Text = "Hardness (0-255):" });
			var hardEdit = new SpinBox { MinValue = 0, MaxValue = 255, Step = 1, Value = 0 };
			vbox.AddChild(hardEdit);

			var solidCheck = new CheckBox { Text = "Solid", ButtonPressed = false };
			vbox.AddChild(solidCheck);

			dialog.Confirmed += () =>
			{
				var newId = idEdit.Text.Trim();
				if (string.IsNullOrWhiteSpace(newId)) return;
				RegisterNewTerrain(newId,
					string.IsNullOrWhiteSpace(glyphEdit.Text) ? "#" : glyphEdit.Text.Trim(),
					string.IsNullOrWhiteSpace(matEdit.Text) ? "stone" : matEdit.Text.Trim(),
					(byte)hardEdit.Value,
					solidCheck.ButtonPressed);
				dialog.QueueFree();
			};
		}
		else
		{
			dialog.Confirmed += () =>
			{
				var newId = idEdit.Text.Trim();
				if (string.IsNullOrWhiteSpace(newId)) return;
				RegisterNewNonTerrain(newId, _activeCategory);
				dialog.QueueFree();
			};
		}

		dialog.Size = new Vector2I(320, 0);
		AddChild(dialog);
		dialog.PopupCentered();
	}

	private void RegisterNewTerrain(string stringId, string glyph, string material, byte hardness, bool solid)
	{
		if (TerrainRegistry.Get(stringId) != null)
		{
			_statusLabel.Text = $"地形 '{stringId}' 已存在";
			return;
		}

		ushort nextId = 0;
		foreach (var t in TerrainRegistry.All)
		{
			if (t != null && t.Id >= nextId)
				nextId = (ushort)(t.Id + 1);
		}

		var def = new TerrainDef
		{
			Id = nextId,
			StringId = stringId,
			Glyph = glyph,
			DefaultHardness = hardness,
			Solid = solid,
			Material = material,
		};
		TerrainRegistry.Register(def);

		SaveTerrainsJson();

		if (!_mappings.ContainsKey(stringId))
			_mappings[stringId] = new VoxelTileMappingEntry { TerrainId = stringId, Category = "terrain" };

		BuildTerrainList();
		RefreshTerrainListUi();
		var idx = _terrainIds.IndexOf(stringId);
		if (idx >= 0) SelectTerrain(idx);
		_statusLabel.Text = $"已注册新地形: {stringId} (ID={nextId})";
	}

	private void RegisterNewNonTerrain(string stringId, string category)
	{
		if (_mappings.ContainsKey(stringId))
		{
			_statusLabel.Text = $"'{stringId}' 已存在";
			return;
		}

		_mappings[stringId] = new VoxelTileMappingEntry { TerrainId = stringId, Category = category };

		BuildTerrainList();
		RefreshTerrainListUi();
		var idx = _terrainIds.IndexOf(stringId);
		if (idx >= 0) SelectTerrain(idx);
		_statusLabel.Text = $"已注册新{category}: {stringId}";
	}

	private void SaveTerrainsJson()
	{
		var terrains = new List<object>();
		foreach (var t in TerrainRegistry.All)
		{
			if (t == null) continue;
			var obj = new Dictionary<string, object>
			{
				["Id"] = t.Id,
				["StringId"] = t.StringId,
				["Glyph"] = t.Glyph,
				["DefaultHardness"] = t.DefaultHardness,
				["Solid"] = t.Solid,
				["Material"] = t.Material,
			};
			if (t.IsOpaque != t.Solid) obj["IsOpaque"] = t.IsOpaque;
			if (!string.IsNullOrWhiteSpace(t.BreaksInto) && t.BreaksInto != "rubble") obj["BreaksInto"] = t.BreaksInto;
			if (!string.IsNullOrWhiteSpace(t.TopTile)) obj["TopTile"] = t.TopTile;
			if (!string.IsNullOrWhiteSpace(t.SideTile)) obj["SideTile"] = t.SideTile;
			terrains.Add(obj);
		}
		var json = JsonSerializer.Serialize(terrains, JsonWriteOpts);
		var fullPath = ProjectSettings.GlobalizePath("res://Data/terrains.json");
		File.WriteAllText(fullPath, json);
	}

	private void BuildCenterPanel(HBoxContainer parent)
	{
		var centerV = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		centerV.AddThemeConstantOverride("separation", 8);
		parent.AddChild(centerV);

		centerV.AddChild(new Label { Text = "体素预览 (拖放贴图到此处)" });

		var previewPanel = new PanelContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(400, 350),
		};
		centerV.AddChild(previewPanel);

		var previewContainer = new SubViewportContainer
		{
			Stretch = true,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		previewPanel.AddChild(previewContainer);

		var vp = new SubViewport
		{
			TransparentBg = false,
			Size = new Vector2I(640, 420),
			Disable3D = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
		};
		previewContainer.AddChild(vp);

		var previewNode = new Node2D();
		vp.AddChild(previewNode);

		previewNode.AddChild(new ColorRect { Color = new Color(0.10f, 0.11f, 0.14f), Size = new Vector2(640, 420) });

		_guidesNode = new GuidesDrawNode { Visible = false };
		previewNode.AddChild(_guidesNode);

		_leftSprite = new Sprite2D { Centered = true, Scale = new Vector2(2f, 2f) };
		_rightSprite = new Sprite2D { Centered = true, Scale = new Vector2(2f, 2f) };
		_topSprite = new Sprite2D { Centered = true, Scale = new Vector2(2f, 2f) };
		previewNode.AddChild(_leftSprite);
		previewNode.AddChild(_rightSprite);
		previewNode.AddChild(_topSprite);

		var slotsH = new HBoxContainer();
		slotsH.AddThemeConstantOverride("separation", 12);
		centerV.AddChild(slotsH);

		(_topSlotBtn, _topSlotPreview) = BuildSlot(slotsH, "顶面", FaceSlot.Top);
		(_leftSlotBtn, _leftSlotPreview) = BuildSlot(slotsH, "左侧", FaceSlot.LeftSide);
		(_rightSlotBtn, _rightSlotPreview) = BuildSlot(slotsH, "右侧", FaceSlot.RightSide);

		var colorRow = new HBoxContainer();
		colorRow.AddThemeConstantOverride("separation", 8);
		centerV.AddChild(colorRow);

		colorRow.AddChild(new Label { Text = "侧面纯色:" });
		_colorPicker = new ColorPickerButton
		{
			Color = _sideColor,
			CustomMinimumSize = new Vector2(60, 30),
		};
		_colorPicker.ColorChanged += c => { _sideColor = c; ApplySideColor(); };
		colorRow.AddChild(_colorPicker);

		_applyColorBtn = new Button { Text = "填充侧面" };
		_applyColorBtn.Pressed += ApplySideColor;
		colorRow.AddChild(_applyColorBtn);

		_eyedropperBtn = new Button { Text = "取色器", ToggleMode = true };
		_eyedropperBtn.Toggled += on => _eyedropperMode = on;
		colorRow.AddChild(_eyedropperBtn);

		var controlsPanel = new VBoxContainer();
		controlsPanel.AddThemeConstantOverride("separation", 4);
		centerV.AddChild(controlsPanel);

		var sideRow = new HBoxContainer();
		sideRow.AddThemeConstantOverride("separation", 12);
		controlsPanel.AddChild(sideRow);

		_showLeftCheck = new CheckBox { Text = "显示左侧", ButtonPressed = true };
		_showLeftCheck.Toggled += _ => OnSideVisibilityChanged();
		sideRow.AddChild(_showLeftCheck);

		_showRightCheck = new CheckBox { Text = "显示右侧", ButtonPressed = true };
		_showRightCheck.Toggled += _ => OnSideVisibilityChanged();
		sideRow.AddChild(_showRightCheck);

		_showGuidesCheck = new CheckBox { Text = "辅助线", ButtonPressed = false };
		_showGuidesCheck.Toggled += on => _guidesNode.Visible = on;
		sideRow.AddChild(_showGuidesCheck);

		var resetBtn = new Button { Text = "重置参数" };
		resetBtn.Pressed += OnResetParams;
		sideRow.AddChild(resetBtn);

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
	}

	private static (HSlider slider, Label label) BuildSliderRow(Control parent, string name, float min, float max, float val, Action<float> onChange)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		parent.AddChild(row);

		var isScale = name.Contains("缩放");
		var label = new Label { Text = isScale ? $"{name}: {val:F2}" : $"{name}: {val:F0}", CustomMinimumSize = new Vector2(100, 0) };
		row.AddChild(label);

		var slider = new HSlider
		{
			MinValue = min,
			MaxValue = max,
			Step = isScale ? 0.05f : 1f,
			Value = val,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(150, 0),
		};
		var capturedName = name;
		var capturedLabel = label;
		slider.ValueChanged += v =>
		{
			capturedLabel.Text = isScale ? $"{capturedName}: {v:F2}" : $"{capturedName}: {v:F0}";
			onChange((float)v);
		};
		row.AddChild(slider);

		return (slider, label);
	}

	private void OnSideVisibilityChanged()
	{
		if (_terrainIds.Count == 0) return;
		var terrainId = _terrainIds[_selectedTerrain];
		if (!_mappings.TryGetValue(terrainId, out var entry))
		{
			entry = new VoxelTileMappingEntry { TerrainId = terrainId, Category = _activeCategory };
			_mappings[terrainId] = entry;
		}
		entry.ShowLeftSide = _showLeftCheck.ButtonPressed;
		entry.ShowRightSide = _showRightCheck.ButtonPressed;
		RefreshPreview();
	}

	private void OnTopParamsChanged()
	{
		if (_terrainIds.Count == 0) return;
		var terrainId = _terrainIds[_selectedTerrain];
		if (!_mappings.TryGetValue(terrainId, out var entry))
		{
			entry = new VoxelTileMappingEntry { TerrainId = terrainId, Category = _activeCategory };
			_mappings[terrainId] = entry;
		}
		entry.TopScaleX = (float)_topScaleXSlider.Value;
		entry.TopScaleY = (float)_topScaleYSlider.Value;
		entry.TopOffsetX = (float)_topOffsetXSlider.Value;
		entry.TopOffsetY = (float)_topOffsetYSlider.Value;
		RefreshPreview();
	}

	private void OnLeftSideParamsChanged()
	{
		if (_terrainIds.Count == 0) return;
		var terrainId = _terrainIds[_selectedTerrain];
		if (!_mappings.TryGetValue(terrainId, out var entry))
		{
			entry = new VoxelTileMappingEntry { TerrainId = terrainId, Category = _activeCategory };
			_mappings[terrainId] = entry;
		}
		entry.LeftOffsetX = (float)_leftOffsetXSlider.Value;
		entry.LeftOffsetY = (float)_leftOffsetYSlider.Value;
		entry.LeftHeight = (int)_leftHeightSlider.Value;
		RefreshPreview();
	}

	private void OnRightSideParamsChanged()
	{
		if (_terrainIds.Count == 0) return;
		var terrainId = _terrainIds[_selectedTerrain];
		if (!_mappings.TryGetValue(terrainId, out var entry))
		{
			entry = new VoxelTileMappingEntry { TerrainId = terrainId, Category = _activeCategory };
			_mappings[terrainId] = entry;
		}
		entry.RightOffsetX = (float)_rightOffsetXSlider.Value;
		entry.RightOffsetY = (float)_rightOffsetYSlider.Value;
		entry.RightHeight = (int)_rightHeightSlider.Value;
		RefreshPreview();
	}

	private void OnResetParams()
	{
		if (_terrainIds.Count == 0) return;
		var terrainId = _terrainIds[_selectedTerrain];
		if (_mappings.TryGetValue(terrainId, out var entry))
		{
			entry.TopScaleX = 1.0f;
			entry.TopScaleY = 1.0f;
			entry.TopOffsetX = 0f;
			entry.TopOffsetY = 0f;
			entry.LeftOffsetX = 0f;
			entry.LeftOffsetY = 0f;
			entry.LeftHeight = 0;
			entry.RightOffsetX = 0f;
			entry.RightOffsetY = 0f;
			entry.RightHeight = 0;
			entry.ShowLeftSide = true;
			entry.ShowRightSide = true;
		}
		RefreshPreview();
	}

	private (Button btn, TextureRect preview) BuildSlot(HBoxContainer parent, string label, FaceSlot slot)
	{
		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 4);
		parent.AddChild(vbox);

		var btn = new Button { Text = label, CustomMinimumSize = new Vector2(100, 28) };
		btn.Pressed += () => SetActiveSlot(slot);
		vbox.AddChild(btn);

		var preview = new TextureRect
		{
			CustomMinimumSize = new Vector2(80, 80),
			ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
		};
		vbox.AddChild(preview);

		var browseBtn = new Button { Text = "浏览..." };
		browseBtn.Pressed += () => { _activeSlot = slot; _fileDialog.Popup(); };
		vbox.AddChild(browseBtn);

		return (btn, preview);
	}

	private VBoxContainer _paletteContainer = null!;

	private void BuildRightPanel(HBoxContainer parent)
	{
		var panel = new PanelContainer
		{
			CustomMinimumSize = new Vector2(420, 0),
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		parent.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 6);
		panel.AddChild(vbox);

		_paletteLabel = new Label { Text = $"贴图调色板 ({_palette.Count} 张, {_paletteGroups.Count} 组)" };
		vbox.AddChild(_paletteLabel);

		_paletteScroller = new ScrollContainer
		{
			SizeFlagsVertical = SizeFlags.ExpandFill,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(400, 0),
		};
		vbox.AddChild(_paletteScroller);

		_paletteContainer = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		_paletteContainer.AddThemeConstantOverride("separation", 2);
		_paletteScroller.AddChild(_paletteContainer);

		RebuildPaletteGrid();
	}

	private void RebuildPaletteGrid()
	{
		foreach (var child in _paletteContainer.GetChildren())
			child.QueueFree();

		foreach (var group in _paletteGroups)
		{
			var header = new Button
			{
				Text = $"▶ {group.Name} ({group.Entries.Count})",
				Alignment = HorizontalAlignment.Left,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				ToggleMode = true,
				ButtonPressed = false,
			};
			_paletteContainer.AddChild(header);

			var grid = new GridContainer
			{
				Columns = PaletteColumns,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				Visible = false,
			};
			grid.AddThemeConstantOverride("h_separation", 4);
			grid.AddThemeConstantOverride("v_separation", 4);
			_paletteContainer.AddChild(grid);

			var capturedGrid = grid;
			var capturedHeader = header;
			header.Toggled += on =>
			{
				capturedGrid.Visible = on;
				capturedHeader.Text = on
					? $"▼ {group.Name} ({group.Entries.Count})"
					: $"▶ {group.Name} ({group.Entries.Count})";
			};

			for (var i = 0; i < group.Entries.Count; i++)
			{
				var entry = group.Entries[i];
				var globalIdx = _palette.IndexOf(entry);
				var btn = new Button
				{
					CustomMinimumSize = new Vector2(PaletteTileSize, PaletteTileSize),
					ClipContents = true,
					TooltipText = entry.DisplayName,
				};

				var tex = GD.Load<Texture2D>(entry.Path);
				if (tex != null)
				{
					var rect = new TextureRect
					{
						Texture = tex,
						ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
						StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
						CustomMinimumSize = new Vector2(PaletteTileSize - 4, PaletteTileSize - 4),
						MouseFilter = MouseFilterEnum.Ignore,
					};
					btn.AddChild(rect);
				}

				var idx = globalIdx;
				btn.Pressed += () => OnPaletteTileClicked(idx);
				grid.AddChild(btn);
			}
		}
	}

	private void OnPaletteTileClicked(int index)
	{
		if (_eyedropperMode)
		{
			var tex = GD.Load<Texture2D>(_palette[index].Path);
			if (tex != null)
			{
				var img = tex.GetImage();
				if (img != null)
				{
					var cx = img.GetWidth() / 2;
					var cy = img.GetHeight() / 2;
					_sideColor = img.GetPixel(cx, cy);
					_colorPicker.Color = _sideColor;
					ApplySideColor();
				}
			}
			_eyedropperMode = false;
			_eyedropperBtn.ButtonPressed = false;
			return;
		}

		ApplyTileToSlot(_palette[index].Path);
	}

	public override bool _CanDropData(Vector2 atPosition, Variant data)
	{
		if (data.VariantType == Variant.Type.Dictionary)
		{
			var dict = data.AsGodotDictionary();
			return dict.ContainsKey("tile_path");
		}
		if (data.VariantType == Variant.Type.String)
		{
			var s = data.AsString();
			return s.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	public override void _DropData(Vector2 atPosition, Variant data)
	{
		string? path = null;
		if (data.VariantType == Variant.Type.Dictionary)
		{
			var dict = data.AsGodotDictionary();
			if (dict.TryGetValue("tile_path", out var v))
				path = v.AsString();
		}
		else if (data.VariantType == Variant.Type.String)
		{
			path = data.AsString();
		}

		if (!string.IsNullOrEmpty(path))
			ApplyTileToSlot(path);
	}

	private void SetActiveSlot(FaceSlot slot)
	{
		_activeSlot = slot;
		UpdateSlotHighlights();
	}

	private void UpdateSlotHighlights()
	{
		_topSlotBtn.Modulate = _activeSlot == FaceSlot.Top ? Colors.White : new Color(0.7f, 0.7f, 0.7f);
		_leftSlotBtn.Modulate = _activeSlot == FaceSlot.LeftSide ? Colors.White : new Color(0.7f, 0.7f, 0.7f);
		_rightSlotBtn.Modulate = _activeSlot == FaceSlot.RightSide ? Colors.White : new Color(0.7f, 0.7f, 0.7f);
	}

	private void ApplyTileToSlot(string tilePath)
	{
		if (_terrainIds.Count == 0) return;
		var terrainId = _terrainIds[_selectedTerrain];
		if (!_mappings.TryGetValue(terrainId, out var entry))
		{
			entry = new VoxelTileMappingEntry { TerrainId = terrainId, Category = _activeCategory };
			_mappings[terrainId] = entry;
		}

		var iso = IsPzTile(tilePath);
		switch (_activeSlot)
		{
			case FaceSlot.Top:
				entry.TopTilePath = tilePath;
				entry.TopIsIso = iso;
				break;
			case FaceSlot.LeftSide:
				entry.LeftSideTilePath = tilePath;
				entry.LeftSideMode = "texture";
				entry.LeftIsIso = iso;
				break;
			case FaceSlot.RightSide:
				entry.RightSideTilePath = tilePath;
				entry.RightSideMode = "texture";
				entry.RightIsIso = iso;
				break;
		}

		RefreshPreview();
	}

	private void ApplySideColor()
	{
		if (_terrainIds.Count == 0) return;
		var terrainId = _terrainIds[_selectedTerrain];
		if (!_mappings.TryGetValue(terrainId, out var entry))
		{
			entry = new VoxelTileMappingEntry { TerrainId = terrainId, Category = _activeCategory };
			_mappings[terrainId] = entry;
		}

		entry.LeftSideMode = "color";
		entry.LeftSideColor = ColorToHex(_sideColor);
		entry.RightSideMode = "color";
		entry.RightSideColor = ColorToHex(_sideColor);

		RefreshPreview();
	}

	private void SelectTerrain(int index)
	{
		_selectedTerrain = Math.Clamp(index, 0, _terrainIds.Count - 1);
		if (_terrainList.IsSelected(_selectedTerrain))
			_terrainList.Select(_selectedTerrain);
		else
			_terrainList.Select(_selectedTerrain);
		RefreshPreview();
	}

	private static bool IsPzTile(string? path) =>
		!string.IsNullOrEmpty(path) && (path.Contains("PZ_Tiles_Copy", StringComparison.OrdinalIgnoreCase) || path.Contains("PZ_Tiles", StringComparison.OrdinalIgnoreCase));

	private void RefreshPreview()
	{
		if (_terrainIds.Count == 0) return;
		var terrainId = _terrainIds[_selectedTerrain];
		_mappings.TryGetValue(terrainId, out var entry);

		var topScaleX = entry?.TopScaleX ?? 1.0f;
		var topScaleY = entry?.TopScaleY ?? 1.0f;
		var topOffsetX = entry?.TopOffsetX ?? 0f;
		var topOffsetY = entry?.TopOffsetY ?? 0f;
		var leftOffsetX = entry?.LeftOffsetX ?? 0f;
		var leftOffsetY = entry?.LeftOffsetY ?? 0f;
		var leftHeight = entry?.LeftHeight ?? 0;
		var rightOffsetX = entry?.RightOffsetX ?? 0f;
		var rightOffsetY = entry?.RightOffsetY ?? 0f;
		var rightHeight = entry?.RightHeight ?? 0;
		var showLeft = entry?.ShowLeftSide ?? true;
		var showRight = entry?.ShowRightSide ?? true;
		var effectiveLeftH = leftHeight > 0 ? leftHeight : VoxelFaceImageUtil.SideFaceHeight;
		var effectiveRightH = rightHeight > 0 ? rightHeight : VoxelFaceImageUtil.SideFaceHeight;

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
		_leftHeightLabel.Text = $"左侧高度: {effectiveLeftH:F0}";
		_rightOffsetXLabel.Text = $"右偏移X: {rightOffsetX:F0}";
		_rightOffsetYLabel.Text = $"右偏移Y: {rightOffsetY:F0}";
		_rightHeightLabel.Text = $"右侧高度: {effectiveRightH:F0}";

		var topPath = entry?.TopTilePath;
		var topImage = LoadFaceSource(topPath);
		var topIsPz = IsPzTile(topPath);

		if (topImage != null && topIsPz)
		{
			_topSprite.Texture = ImageTexture.CreateFromImage(topImage);
			var tw = topImage.GetWidth();
			var th = topImage.GetHeight();
			var fitScale = Math.Min(256f / tw, 128f / th);
			fitScale = Math.Max(fitScale, 2f);
			_topSprite.Scale = new Vector2(fitScale * topScaleX, fitScale * topScaleY);
			_topSprite.Position = new Vector2(320 + topOffsetX, 150 + topOffsetY);
		}
		else if (topImage != null)
		{
			var topFace = VoxelFaceImageUtil.EnhanceTopFaceEdges(VoxelFaceImageUtil.BuildTopDiamond(topImage), 0.18f);
			_topSprite.Texture = ImageTexture.CreateFromImage(topFace);
			_topSprite.Scale = new Vector2(2f * topScaleX, 2f * topScaleY);
			_topSprite.Position = new Vector2(320 + topOffsetX, 150 + topOffsetY);
		}
		else
		{
			var placeholder = CreateDiamondImage(VoxelFaceImageUtil.TopFaceWidth, VoxelFaceImageUtil.TopFaceHeight, new Color(0.5f, 0.5f, 0.5f));
			_topSprite.Texture = ImageTexture.CreateFromImage(placeholder);
			_topSprite.Scale = new Vector2(2f * topScaleX, 2f * topScaleY);
			_topSprite.Position = new Vector2(320 + topOffsetX, 150 + topOffsetY);
		}
		_topSprite.Visible = true;
		_topSlotPreview.Texture = topImage != null ? ImageTexture.CreateFromImage(topImage) : null;

		if (showLeft)
		{
			var leftImage = ResolveSideSource(entry, isRight: false);
			var leftFace = VoxelFaceImageUtil.GenerateSideFace(leftImage, false, 0.65f, effectiveLeftH);
			VoxelFaceImageUtil.EnhanceSideFaceEdge(leftFace, false, 0.12f);
			_leftSprite.Texture = ImageTexture.CreateFromImage(leftFace);
			_leftSprite.Scale = new Vector2(2f, 2f);
			_leftSprite.Position = new Vector2(256 + leftOffsetX, 325 + leftOffsetY);
			_leftSprite.Visible = true;
			_leftSlotPreview.Texture = ImageTexture.CreateFromImage(leftImage);
		}
		else
		{
			_leftSprite.Visible = false;
		}

		if (showRight)
		{
			var rightImage = ResolveSideSource(entry, isRight: true);
			var rightFace = VoxelFaceImageUtil.GenerateSideFace(rightImage, true, 0.80f, effectiveRightH);
			VoxelFaceImageUtil.EnhanceSideFaceEdge(rightFace, true, 0.12f);
			_rightSprite.Texture = ImageTexture.CreateFromImage(rightFace);
			_rightSprite.Scale = new Vector2(2f, 2f);
			_rightSprite.Position = new Vector2(384 + rightOffsetX, 325 + rightOffsetY);
			_rightSprite.Visible = true;
			_rightSlotPreview.Texture = ImageTexture.CreateFromImage(rightImage);
		}
		else
		{
			_rightSprite.Visible = false;
		}

		_statusLabel.Text = $"当前: {terrainId}\n顶面: {topPath ?? "(默认)"}\n侧面: {entry?.LeftSideMode ?? "默认"}";
		UpdateSlotHighlights();
	}

	private Image? LoadFaceSource(string? path)
	{
		if (string.IsNullOrWhiteSpace(path)) return null;
		var tex = GD.Load<Texture2D>(path);
		return tex?.GetImage();
	}

	private Image ResolveSideSource(VoxelTileMappingEntry? entry, bool isRight)
	{
		var mode = isRight ? entry?.RightSideMode : entry?.LeftSideMode;
		var path = isRight ? entry?.RightSideTilePath : entry?.LeftSideTilePath;
		var colorHex = isRight ? entry?.RightSideColor : entry?.LeftSideColor;

		if (mode == "color" && !string.IsNullOrEmpty(colorHex))
		{
			var color = new Color(colorHex);
			return CreateSolidImage(64, 64, color);
		}

		if (mode == "texture" && !string.IsNullOrEmpty(path))
		{
			var img = LoadFaceSource(path);
			if (img != null) return img;
		}

		return CreateSolidImage(64, 64, _sideColor);
	}

	private void OnFileDialogSelected(string path)
	{
		ApplyTileToSlot(path);
	}

	private void LoadMappings()
	{
		_mappings.Clear();
		var fullPath = ProjectSettings.GlobalizePath($"res://{MappingPath}");
		if (!File.Exists(fullPath)) return;
		try
		{
			var json = File.ReadAllText(fullPath);
			var doc = JsonSerializer.Deserialize<VoxelTileMappingDocument>(json, JsonOpts);
			if (doc?.Entries == null) return;
			foreach (var e in doc.Entries)
			{
				if (!string.IsNullOrWhiteSpace(e.TerrainId))
					_mappings[e.TerrainId] = e;
			}
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[VoxelTilePreviewTool] Failed to load mappings: {ex.Message}");
		}
	}

	private void SaveMappings()
	{
		var doc = new VoxelTileMappingDocument
		{
			Entries = _mappings.Values.OrderBy(e => e.TerrainId).ToList(),
		};
		var json = JsonSerializer.Serialize(doc, JsonWriteOpts);
		var fullPath = ProjectSettings.GlobalizePath($"res://{MappingPath}");
		var dir = Path.GetDirectoryName(fullPath);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			Directory.CreateDirectory(dir);
		File.WriteAllText(fullPath, json);
		_statusLabel.Text = $"已保存 {_mappings.Count} 条映射到 {MappingPath}";
	}

	private static Image CreateDiamondImage(int w, int h, Color color)
	{
		var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		var halfW = w / 2;
		var halfH = h / 2;
		for (var py = 0; py < h; py++)
		for (var px = 0; px < w; px++)
		{
			var dx = Math.Abs(px - halfW) / (float)halfW;
			var dy = Math.Abs(py - halfH) / (float)halfH;
			image.SetPixel(px, py, dx + dy <= 1.0f ? color : Colors.Transparent);
		}
		return image;
	}

	private static Image CreateSolidImage(int w, int h, Color color)
	{
		var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		image.Fill(color);
		return image;
	}

	private static string ColorToHex(Color c) => $"#{(int)(c.R * 255):x2}{(int)(c.G * 255):x2}{(int)(c.B * 255):x2}";

	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
	};

	private static readonly JsonSerializerOptions JsonWriteOpts = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};
}

public class VoxelTileMappingDocument
{
	[JsonPropertyName("entries")]
	public List<VoxelTileMappingEntry> Entries { get; set; } = [];
}

public class VoxelTileMappingEntry
{
	[JsonPropertyName("terrainId")]
	public string TerrainId { get; set; } = "";

	[JsonPropertyName("category")]
	public string? Category { get; set; }

	[JsonPropertyName("topTilePath")]
	public string? TopTilePath { get; set; }

	[JsonPropertyName("topIsIso")]
	public bool TopIsIso { get; set; }

	[JsonPropertyName("leftSideMode")]
	public string? LeftSideMode { get; set; }

	[JsonPropertyName("leftSideTilePath")]
	public string? LeftSideTilePath { get; set; }

	[JsonPropertyName("leftSideColor")]
	public string? LeftSideColor { get; set; }

	[JsonPropertyName("leftIsIso")]
	public bool LeftIsIso { get; set; }

	[JsonPropertyName("rightSideMode")]
	public string? RightSideMode { get; set; }

	[JsonPropertyName("rightSideTilePath")]
	public string? RightSideTilePath { get; set; }

	[JsonPropertyName("rightSideColor")]
	public string? RightSideColor { get; set; }

	[JsonPropertyName("rightIsIso")]
	public bool RightIsIso { get; set; }

	[JsonPropertyName("topScaleX")]
	public float TopScaleX { get; set; } = 1.0f;

	[JsonPropertyName("topScaleY")]
	public float TopScaleY { get; set; } = 1.0f;

	[JsonPropertyName("topOffsetX")]
	public float TopOffsetX { get; set; }

	[JsonPropertyName("topOffsetY")]
	public float TopOffsetY { get; set; }

	[JsonPropertyName("leftOffsetX")]
	public float LeftOffsetX { get; set; }

	[JsonPropertyName("leftOffsetY")]
	public float LeftOffsetY { get; set; }

	[JsonPropertyName("leftHeight")]
	public int LeftHeight { get; set; }

	[JsonPropertyName("rightOffsetX")]
	public float RightOffsetX { get; set; }

	[JsonPropertyName("rightOffsetY")]
	public float RightOffsetY { get; set; }

	[JsonPropertyName("rightHeight")]
	public int RightHeight { get; set; }

	[JsonPropertyName("showLeftSide")]
	public bool ShowLeftSide { get; set; } = true;

	[JsonPropertyName("showRightSide")]
	public bool ShowRightSide { get; set; } = true;
}

internal readonly record struct TilePaletteEntry(string Path, string DisplayName);
internal readonly record struct PaletteGroup(string Name, List<TilePaletteEntry> Entries);

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
