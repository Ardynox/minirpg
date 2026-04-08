using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace MiniRPG.Tools;

public partial class PlaceholderPaintTool : Control
{
	private sealed class PendingOperation
	{
		public required Action Action { get; init; }
	}

	private readonly List<PlaceholderWorkspaceEntry> _visibleEntries = [];
	private readonly HashSet<PlaceholderWorkspaceEntry> _dirtyEntries = [];
	private readonly Dictionary<PlaceholderWorkspaceEntry, HashSet<int>> _dirtyFrames = [];
	private readonly Dictionary<PlaceholderWorkspaceEntry, Dictionary<int, Image>> _frameCache = [];
	private readonly HashSet<string> _deletedImageResPaths = new(StringComparer.OrdinalIgnoreCase);

	private PlaceholderWorkspaceManifest _manifest = PlaceholderWorkspaceCatalog.CreateStarterManifest();
	private PlaceholderWorkspaceManifest _savedManifest = PlaceholderWorkspaceCatalog.CreateStarterManifest();
	private string _workspaceResPath = PlaceholderWorkspaceCatalog.DefaultWorkspaceResPath;
	private string _statusMessage = "就绪。";
	private int _activeEntryIndex = -1;
	private int _activeFrameIndex;
	private bool _workspaceDirty;
	private bool _suppressUiEvents;
	private PendingOperation? _pendingOperation;

	private LineEdit _workspacePathEdit = null!;
	private Label _summaryLabel = null!;
	private Label _statusLabel = null!;
	private LineEdit _searchEdit = null!;
	private OptionButton _categoryFilter = null!;
	private OptionButton _statusFilter = null!;
	private ItemList _entryList = null!;
	private Label _canvasTitleLabel = null!;
	private Label _canvasInfoLabel = null!;
	private Label _hoverInfoLabel = null!;
	private Label _zoomLabel = null!;
	private PlaceholderCanvasControl _canvas = null!;
	private Label _validationLabel = null!;
	private LineEdit _idEdit = null!;
	private LineEdit _nameEdit = null!;
	private TextEdit _descriptionEdit = null!;
	private OptionButton _categoryEdit = null!;
	private OptionButton _statusEdit = null!;
	private OptionButton _presetEdit = null!;
	private SpinBox _widthSpin = null!;
	private SpinBox _heightSpin = null!;
	private LineEdit _tagsEdit = null!;
	private LineEdit _imagePathEdit = null!;
	private Label _frameSummaryLabel = null!;
	private ItemList _frameList = null!;
	private Button _addFrameButton = null!;
	private Button _duplicateFrameButton = null!;
	private Button _deleteFrameButton = null!;
	private CheckButton _overlayToggle = null!;
	private HSlider _overlayOpacitySlider = null!;
	private Label _overlayOpacityLabel = null!;
	private OptionButton _toolEdit = null!;
	private ColorPickerButton _colorButton = null!;
	private CheckButton _gridToggle = null!;
	private Button _undoButton = null!;
	private Button _redoButton = null!;
	private Button _clearButton = null!;
	private Button _zoomInButton = null!;
	private Button _zoomOutButton = null!;
	private Button _resetViewButton = null!;
	private Button _markDoneButton = null!;
	private FileDialog _workspaceDialog = null!;
	private ConfirmationDialog _unsavedDialog = null!;
	private AcceptDialog _messageDialog = null!;
	private ConfirmationDialog _deleteDialog = null!;
	private ConfirmationDialog _entryDialog = null!;
	private LineEdit _newIdEdit = null!;
	private LineEdit _newNameEdit = null!;
	private TextEdit _newDescriptionEdit = null!;
	private OptionButton _newCategoryEdit = null!;
	private OptionButton _newStatusEdit = null!;
	private OptionButton _newPresetEdit = null!;
	private SpinBox _newWidthSpin = null!;
	private SpinBox _newHeightSpin = null!;
	private LineEdit _newTagsEdit = null!;

	private PlaceholderWorkspaceEntry? ActiveEntry =>
		_activeEntryIndex >= 0 && _activeEntryIndex < _manifest.Entries.Count
			? _manifest.Entries[_activeEntryIndex]
			: null;

	public override void _Ready()
	{
		GetTree().AutoAcceptQuit = false;
		BuildUi();
		BuildDialogs();
		WireEvents();
		LoadWorkspace(PlaceholderWorkspaceCatalog.DefaultWorkspaceResPath, null);
	}

	public override void _Notification(int what)
	{
		if (what != NotificationWMCloseRequest)
			return;

		if (HasUnsavedChanges())
		{
			BeginPendingOperation(() => GetTree().Quit());
			return;
		}

		GetTree().Quit();
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
			return;
		if (HasBlockingPopupOpen() || IsTextEditingActive())
			return;

		if (!keyEvent.CtrlPressed && !keyEvent.MetaPressed)
		{
			if (ActiveEntry == null)
				return;

			switch (keyEvent.Keycode)
			{
				case Key.Left:
					if (ChangeActiveFrameByOffset(-1))
						GetViewport().SetInputAsHandled();
					return;
				case Key.Right:
					if (ChangeActiveFrameByOffset(1))
						GetViewport().SetInputAsHandled();
					return;
			}

			return;
		}

		switch (keyEvent.Keycode)
		{
			case Key.S:
				SaveWorkspace(true);
				GetViewport().SetInputAsHandled();
				break;
			case Key.Z when keyEvent.ShiftPressed:
				if (_canvas.CanRedo)
				{
					_canvas.Redo();
					GetViewport().SetInputAsHandled();
				}

				break;
			case Key.Z:
				if (_canvas.CanUndo)
				{
					_canvas.Undo();
					GetViewport().SetInputAsHandled();
				}

				break;
			case Key.Y when !keyEvent.ShiftPressed:
				if (_canvas.CanRedo)
				{
					_canvas.Redo();
					GetViewport().SetInputAsHandled();
				}

				break;
		}
	}

	private void BuildUi()
	{
		var background = new ColorRect { Color = new Color(0.08f, 0.09f, 0.12f, 1f) };
		background.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(background);

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 16);
		margin.AddThemeConstantOverride("margin_top", 16);
		margin.AddThemeConstantOverride("margin_right", 16);
		margin.AddThemeConstantOverride("margin_bottom", 16);
		AddChild(margin);

		var root = CreateVBox(margin, 12);
		root.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		root.SizeFlagsVertical = SizeFlags.ExpandFill;
		root.AddChild(BuildToolbar());

		var metaRow = new HBoxContainer();
		metaRow.AddThemeConstantOverride("separation", 12);
		root.AddChild(metaRow);

		_summaryLabel = new Label
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			Text = "未加载工作区",
		};
		metaRow.AddChild(_summaryLabel);

		_statusLabel = new Label
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			HorizontalAlignment = HorizontalAlignment.Right,
			Text = "就绪。",
		};
		metaRow.AddChild(_statusLabel);

		var body = new HBoxContainer();
		body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		body.SizeFlagsVertical = SizeFlags.ExpandFill;
		body.AddThemeConstantOverride("separation", 12);
		root.AddChild(body);

		body.AddChild(BuildLeftPanel());
		body.AddChild(BuildCenterPanel());
		body.AddChild(BuildRightPanel());
	}

	private Control BuildToolbar()
	{
		var toolbar = new HBoxContainer();
		toolbar.AddThemeConstantOverride("separation", 10);
		toolbar.AddChild(new Label { Text = "工作区" });

		_workspacePathEdit = new LineEdit
		{
			Editable = false,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		toolbar.AddChild(_workspacePathEdit);
		toolbar.AddChild(CreateActionButton("打开 / 新建", () => _workspaceDialog.PopupCenteredRatio(0.72f)));
		toolbar.AddChild(CreateActionButton("保存当前", () => SaveWorkspace(true)));
		toolbar.AddChild(CreateActionButton("保存全部", () => SaveWorkspace(true)));
		toolbar.AddChild(CreateActionButton("新建条目", () => BeginPendingOperation(OpenNewEntryDialog)));
		return toolbar;
	}

	private Control BuildLeftPanel()
	{
		var panel = new PanelContainer
		{
			CustomMinimumSize = new Vector2(360f, 0f),
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		var content = CreateVBox(CreatePanelMargin(panel), 8);

		_searchEdit = new LineEdit { PlaceholderText = "搜索占位资源" };
		content.AddChild(_searchEdit);

		var filters = new HBoxContainer();
		filters.AddThemeConstantOverride("separation", 8);
		content.AddChild(filters);

		_categoryFilter = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		PopulateCategoryButton(_categoryFilter, true);
		filters.AddChild(_categoryFilter);

		_statusFilter = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		PopulateStatusButton(_statusFilter, true);
		filters.AddChild(_statusFilter);

		_entryList = new ItemList
		{
			AllowReselect = true,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			SelectMode = ItemList.SelectModeEnum.Single,
		};
		content.AddChild(_entryList);

		return panel;
	}

	private Control BuildCenterPanel()
	{
		var panel = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(460f, 0f),
		};
		panel.AddThemeConstantOverride("separation", 10);

		_canvasTitleLabel = new Label { Text = "未选择条目" };
		panel.AddChild(_canvasTitleLabel);

		var canvasPanel = new PanelContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		panel.AddChild(canvasPanel);

		_canvas = new PlaceholderCanvasControl
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0f, 440f),
		};
		CreatePanelMargin(canvasPanel).AddChild(_canvas);

		var infoRow = new HBoxContainer();
		infoRow.AddThemeConstantOverride("separation", 12);
		panel.AddChild(infoRow);

		_canvasInfoLabel = new Label
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			Text = "画布: -",
		};
		infoRow.AddChild(_canvasInfoLabel);

		_hoverInfoLabel = new Label
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			HorizontalAlignment = HorizontalAlignment.Center,
			Text = "悬停: -",
		};
		infoRow.AddChild(_hoverInfoLabel);

		_zoomLabel = new Label
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			HorizontalAlignment = HorizontalAlignment.Right,
			Text = "缩放: 100%",
		};
		infoRow.AddChild(_zoomLabel);

		return panel;
	}

	private Control BuildRightPanel()
	{
		var scroll = new ScrollContainer
		{
			CustomMinimumSize = new Vector2(420f, 0f),
			SizeFlagsVertical = SizeFlags.ExpandFill,
			FollowFocus = true,
		};

		var content = CreateVBox(scroll, 10);
		content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		content.SizeFlagsVertical = SizeFlags.ExpandFill;
		content.AddChild(BuildEntrySection());
		content.AddChild(BuildFramesSection());
		content.AddChild(BuildToolsSection());
		return scroll;
	}

	private Control BuildEntrySection()
	{
		var panel = new PanelContainer();
		var content = CreateVBox(CreatePanelMargin(panel), 8);
		content.AddChild(new Label { Text = "条目" });

		var actions = new HBoxContainer();
		actions.AddThemeConstantOverride("separation", 8);
		content.AddChild(actions);
		_markDoneButton = CreateActionButton("标记已完成", MarkActiveEntryDone, true);
		actions.AddChild(_markDoneButton);
		actions.AddChild(CreateActionButton("复制", DuplicateActiveEntry, true));
		actions.AddChild(CreateActionButton("删除", RequestDeleteActiveEntry, true));

		var form = new GridContainer { Columns = 2 };
		form.AddThemeConstantOverride("h_separation", 8);
		form.AddThemeConstantOverride("v_separation", 6);
		content.AddChild(form);

		_idEdit = new LineEdit();
		AddLabeledField(form, "ID", _idEdit);
		_nameEdit = new LineEdit();
		AddLabeledField(form, "名称", _nameEdit);
		_categoryEdit = new OptionButton();
		PopulateCategoryButton(_categoryEdit, false);
		AddLabeledField(form, "分类", _categoryEdit);
		_statusEdit = new OptionButton();
		PopulateStatusButton(_statusEdit, false);
		AddLabeledField(form, "状态", _statusEdit);
		_presetEdit = new OptionButton();
		PopulatePresetButton(_presetEdit);
		AddLabeledField(form, "尺寸预设", _presetEdit);
		_widthSpin = CreateSpinBox(1, 8192, 256);
		AddLabeledField(form, "宽度", _widthSpin);
		_heightSpin = CreateSpinBox(1, 8192, 256);
		AddLabeledField(form, "高度", _heightSpin);
		_tagsEdit = new LineEdit { PlaceholderText = "标签1, 标签2" };
		AddLabeledField(form, "标签", _tagsEdit);
		_imagePathEdit = new LineEdit { Editable = false };
		AddLabeledField(form, "当前帧路径", _imagePathEdit);
		form.AddChild(new Label { Text = "描述" });
		_descriptionEdit = new TextEdit { CustomMinimumSize = new Vector2(0f, 120f) };
		form.AddChild(_descriptionEdit);

		_validationLabel = new Label
		{
			Text = "未选择条目。",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		content.AddChild(_validationLabel);
		return panel;
	}

	private Control BuildFramesSection()
	{
		var panel = new PanelContainer();
		var content = CreateVBox(CreatePanelMargin(panel), 8);
		content.AddChild(new Label { Text = "序列帧" });

		_frameSummaryLabel = new Label { Text = "第 - 帧 / 共 0 帧" };
		content.AddChild(_frameSummaryLabel);

		_frameList = new ItemList
		{
			AllowReselect = true,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0f, 160f),
			SelectMode = ItemList.SelectModeEnum.Single,
		};
		content.AddChild(_frameList);

		var actions = new HBoxContainer();
		actions.AddThemeConstantOverride("separation", 8);
		content.AddChild(actions);
		_addFrameButton = CreateActionButton("新增帧", AddFrameAfterActive, true);
		_duplicateFrameButton = CreateActionButton("复制帧", DuplicateActiveFrame, true);
		_deleteFrameButton = CreateActionButton("删除帧", DeleteActiveFrame, true);
		actions.AddChild(_addFrameButton);
		actions.AddChild(_duplicateFrameButton);
		actions.AddChild(_deleteFrameButton);

		var toggleRow = new HBoxContainer();
		toggleRow.AddThemeConstantOverride("separation", 8);
		content.AddChild(toggleRow);
		_overlayToggle = new CheckButton
		{
			Text = "显示上一帧",
			ButtonPressed = true,
		};
		toggleRow.AddChild(_overlayToggle);
		_overlayOpacityLabel = new Label
		{
			Text = "35%",
			HorizontalAlignment = HorizontalAlignment.Right,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		toggleRow.AddChild(_overlayOpacityLabel);

		var sliderRow = new HBoxContainer();
		sliderRow.AddThemeConstantOverride("separation", 8);
		content.AddChild(sliderRow);
		sliderRow.AddChild(new Label { Text = "透明度" });
		_overlayOpacitySlider = new HSlider
		{
			MinValue = 0,
			MaxValue = 100,
			Step = 5,
			Value = 35,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		sliderRow.AddChild(_overlayOpacitySlider);

		return panel;
	}

	private Control BuildToolsSection()
	{
		var panel = new PanelContainer();
		var content = CreateVBox(CreatePanelMargin(panel), 8);
		content.AddChild(new Label { Text = "绘制工具" });

		var form = new GridContainer { Columns = 2 };
		form.AddThemeConstantOverride("h_separation", 8);
		form.AddThemeConstantOverride("v_separation", 6);
		content.AddChild(form);

		_toolEdit = new OptionButton();
		foreach (PlaceholderCanvasControl.ToolMode tool in Enum.GetValues<PlaceholderCanvasControl.ToolMode>())
			_toolEdit.AddItem(GetToolLabel(tool));
		_toolEdit.Select(0);
		AddLabeledField(form, "工具", _toolEdit);

		_colorButton = new ColorPickerButton
		{
			Color = Colors.White,
			CustomMinimumSize = new Vector2(0f, 32f),
		};
		AddLabeledField(form, "颜色", _colorButton);

		_gridToggle = new CheckButton
		{
			Text = "显示网格",
			ButtonPressed = true,
		};
		AddLabeledField(form, "网格", _gridToggle);

		var undoRow = new HBoxContainer();
		undoRow.AddThemeConstantOverride("separation", 8);
		content.AddChild(undoRow);
		_undoButton = CreateActionButton("撤销", () => _canvas.Undo(), true);
		_redoButton = CreateActionButton("重做", () => _canvas.Redo(), true);
		undoRow.AddChild(_undoButton);
		undoRow.AddChild(_redoButton);

		var clearRow = new HBoxContainer();
		clearRow.AddThemeConstantOverride("separation", 8);
		content.AddChild(clearRow);
		_clearButton = CreateActionButton("清空画布", ClearCanvas, true);
		clearRow.AddChild(_clearButton);

		var zoomRow = new HBoxContainer();
		zoomRow.AddThemeConstantOverride("separation", 8);
		content.AddChild(zoomRow);
		_zoomOutButton = CreateActionButton("缩小", () => _canvas.ZoomOut(), true);
		_zoomInButton = CreateActionButton("放大", () => _canvas.ZoomIn(), true);
		_resetViewButton = CreateActionButton("重置视图", () => _canvas.ResetView(), true);
		zoomRow.AddChild(_zoomOutButton);
		zoomRow.AddChild(_zoomInButton);
		zoomRow.AddChild(_resetViewButton);

		content.AddChild(new Label
		{
			Text = "鼠标滚轮缩放，中键拖拽或切到平移工具可移动视图。Ctrl+S 保存，Ctrl+Z 撤销，Ctrl+Shift+Z 或 Ctrl+Y 重做，左右方向键切换前后帧。",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		});
		return panel;
	}

	private void BuildDialogs()
	{
		_workspaceDialog = new FileDialog
		{
			FileMode = FileDialog.FileModeEnum.OpenDir,
			Access = FileDialog.AccessEnum.Filesystem,
			UseNativeDialog = true,
			Title = "打开或新建占位工作区",
			CurrentDir = ProjectSettings.GlobalizePath("res://Assets/Art"),
		};
		AddChild(_workspaceDialog);

		_unsavedDialog = new ConfirmationDialog
		{
			Title = "未保存更改",
			DialogText = "继续之前是否保存当前更改？",
		};
		AddChild(_unsavedDialog);
		_unsavedDialog.GetOkButton().Text = "保存";
		_unsavedDialog.AddButton("放弃", true, "discard");

		_messageDialog = new AcceptDialog { Title = "占位画板工具" };
		AddChild(_messageDialog);

		_deleteDialog = new ConfirmationDialog
		{
			Title = "删除条目",
			DialogText = "是否从工作区删除当前选中条目？",
		};
		AddChild(_deleteDialog);
		_deleteDialog.GetOkButton().Text = "删除";

		_entryDialog = new ConfirmationDialog { Title = "新建条目" };
		AddChild(_entryDialog);
		_entryDialog.GetOkButton().Text = "创建";

		var content = CreateVBox(_entryDialog, 8);
		content.CustomMinimumSize = new Vector2(420f, 0f);
		var form = new GridContainer { Columns = 2 };
		form.AddThemeConstantOverride("h_separation", 8);
		form.AddThemeConstantOverride("v_separation", 6);
		content.AddChild(form);

		_newIdEdit = new LineEdit();
		AddLabeledField(form, "ID", _newIdEdit);
		_newNameEdit = new LineEdit();
		AddLabeledField(form, "名称", _newNameEdit);
		_newCategoryEdit = new OptionButton();
		PopulateCategoryButton(_newCategoryEdit, false);
		AddLabeledField(form, "分类", _newCategoryEdit);
		_newStatusEdit = new OptionButton();
		PopulateStatusButton(_newStatusEdit, false);
		AddLabeledField(form, "状态", _newStatusEdit);
		_newPresetEdit = new OptionButton();
		PopulatePresetButton(_newPresetEdit);
		AddLabeledField(form, "尺寸预设", _newPresetEdit);
		_newWidthSpin = CreateSpinBox(1, 8192, 256);
		AddLabeledField(form, "宽度", _newWidthSpin);
		_newHeightSpin = CreateSpinBox(1, 8192, 256);
		AddLabeledField(form, "高度", _newHeightSpin);
		_newTagsEdit = new LineEdit { PlaceholderText = "标签1, 标签2" };
		AddLabeledField(form, "标签", _newTagsEdit);
		form.AddChild(new Label { Text = "描述" });
		_newDescriptionEdit = new TextEdit { CustomMinimumSize = new Vector2(0f, 120f) };
		form.AddChild(_newDescriptionEdit);
	}

	private void WireEvents()
	{
		_workspaceDialog.DirSelected += HandleWorkspaceSelected;
		_unsavedDialog.Confirmed += HandleUnsavedSave;
		_unsavedDialog.CustomAction += HandleUnsavedAction;
		_deleteDialog.Confirmed += DeleteActiveEntry;
		_entryDialog.Confirmed += CreateEntryFromDialog;

		_searchEdit.TextChanged += _ => RefreshEntryList();
		_categoryFilter.ItemSelected += _ => RefreshEntryList();
		_statusFilter.ItemSelected += _ => RefreshEntryList();
		_entryList.ItemSelected += index => HandleEntrySelected((int)index);

		_idEdit.TextChanged += HandleIdChanged;
		_nameEdit.TextChanged += value => MutateActiveEntry(entry => entry.Name = value.Trim());
		_descriptionEdit.TextChanged += () => MutateActiveEntry(entry => entry.Description = _descriptionEdit.Text.Trim());
		_categoryEdit.ItemSelected += _ => ChangeActiveCategory(GetCategoryValue(_categoryEdit, false));
		_statusEdit.ItemSelected += _ => MutateActiveEntry(entry => entry.Status = GetStatusValue(_statusEdit, false));
		_presetEdit.ItemSelected += _ => ApplyPresetSelection();
		_widthSpin.ValueChanged += value => ChangeActiveCanvasSize((int)value, null);
		_heightSpin.ValueChanged += value => ChangeActiveCanvasSize(null, (int)value);
		_tagsEdit.TextChanged += value => MutateActiveEntry(entry => entry.Tags = ParseTags(value));

		_frameList.ItemSelected += index => SelectFrame((int)index, fitToView: false);
		_overlayToggle.Toggled += _ => RefreshFrameSection();
		_overlayOpacitySlider.ValueChanged += value =>
		{
			_overlayOpacityLabel.Text = $"{Mathf.RoundToInt((float)value)}%";
			RefreshOverlay();
			RefreshFrameSection();
		};

		_toolEdit.ItemSelected += index => _canvas.Tool = (PlaceholderCanvasControl.ToolMode)(int)index;
		_colorButton.ColorChanged += color => _canvas.PrimaryColor = color;
		_gridToggle.Toggled += pressed => _canvas.ShowGrid = pressed;

		_canvas.StateChanged += RefreshCanvasState;
		_canvas.ImageModified += HandleCanvasImageModified;
		_canvas.ColorPicked += color =>
		{
			_colorButton.Color = color;
			RefreshCanvasState();
		};
		_canvas.HoverPixelChanged += pixel =>
		{
			_hoverInfoLabel.Text = pixel.HasValue ? $"悬停: {pixel.Value.X}, {pixel.Value.Y}" : "悬停: -";
		};

		_newCategoryEdit.ItemSelected += _ => SyncNewEntryPresetWithCategory();
		_newPresetEdit.ItemSelected += _ => ApplyNewEntryPreset();
		_newNameEdit.TextChanged += value =>
		{
			if (string.IsNullOrWhiteSpace(_newIdEdit.Text))
				_newIdEdit.Text = PlaceholderWorkspaceCatalog.SanitizeId(value);
		};
	}

	private void HandleWorkspaceSelected(string absolutePath)
	{
		if (!PlaceholderWorkspaceStore.TryAbsoluteToResPath(absolutePath, out var resPath))
		{
			ShowMessage("工作区必须位于当前项目目录内。");
			return;
		}

		BeginPendingOperation(() => LoadWorkspace(resPath, null));
	}

	private void LoadWorkspace(string workspaceResPath, string? preferredEntryId)
	{
		try
		{
			PlaceholderWorkspaceStore.EnsureWorkspace(workspaceResPath, true);
			_manifest = PlaceholderWorkspaceStore.LoadManifest(workspaceResPath);
			_savedManifest = _manifest.DeepClone();
			_workspaceResPath = workspaceResPath;
			_workspacePathEdit.Text = workspaceResPath;
			_workspaceDirty = false;
			_dirtyEntries.Clear();
			_dirtyFrames.Clear();
			_frameCache.Clear();
			_deletedImageResPaths.Clear();
			_activeFrameIndex = 0;
			_statusMessage = $"已加载 {_manifest.Entries.Count} 个条目。";
			RefreshEntryList();
			ActivateEntryById(preferredEntryId ?? _manifest.Entries.FirstOrDefault()?.Id, true);
		}
		catch (Exception ex)
		{
			ShowMessage($"加载工作区失败。\n{ex.Message}");
		}
	}

	private void RefreshEntryList()
	{
		var activeEntry = ActiveEntry;
		_visibleEntries.Clear();
		_entryList.Clear();
		foreach (var entry in _manifest.Entries.Where(MatchesFilters))
		{
			_visibleEntries.Add(entry);
			_entryList.AddItem(BuildEntryLabel(entry));
		}

		_summaryLabel.Text = $"总计 {_manifest.Entries.Count} 项 | 可见 {_visibleEntries.Count} 项 | 已修改 {_dirtyEntries.Count} 项";
		_statusLabel.Text = _statusMessage;

		if (activeEntry != null)
		{
			var visibleIndex = _visibleEntries.IndexOf(activeEntry);
			if (visibleIndex >= 0)
			{
				_suppressUiEvents = true;
				_entryList.Select(visibleIndex);
				_suppressUiEvents = false;
			}
		}

		RefreshValidation();
	}

	private void HandleEntrySelected(int visibleIndex)
	{
		if (_suppressUiEvents)
			return;
		if (visibleIndex < 0 || visibleIndex >= _visibleEntries.Count)
			return;

		var target = _visibleEntries[visibleIndex];
		if (ReferenceEquals(target, ActiveEntry))
			return;

		BeginPendingOperation(() => ActivateEntryById(target.Id, true));
	}

	private void ActivateEntryById(string? entryId, bool fitToView)
	{
		PlaceholderWorkspaceEntry? entry = null;
		if (!string.IsNullOrWhiteSpace(entryId))
			entry = _manifest.Entries.FirstOrDefault(item => string.Equals(item.Id, entryId, StringComparison.OrdinalIgnoreCase));
		entry ??= _manifest.Entries.FirstOrDefault();

		if (entry == null)
		{
			_activeEntryIndex = -1;
			_activeFrameIndex = 0;
			_canvas.LoadImage(CreateBlankImage(1, 1), true);
			_canvas.SetOverlayImage(null, 0f);
			_canvasTitleLabel.Text = "未选择条目";
			RefreshEditorFields();
			RefreshFrameSection();
			RefreshCanvasState();
			return;
		}

		_activeEntryIndex = _manifest.Entries.IndexOf(entry);
		_activeFrameIndex = 0;
		EnsureActiveFrameIndex(entry);
		LoadActiveFrameToCanvas(fitToView);
		_statusMessage = $"已选中 {entry.Id}。";
		RefreshEditorFields();
		RefreshFrameSection();
		RefreshCanvasState();
	}

	private void RefreshEditorFields()
	{
		var entry = ActiveEntry;
		_suppressUiEvents = true;
		SetEditorEnabled(entry != null);
		if (entry == null)
		{
			_idEdit.Text = string.Empty;
			_nameEdit.Text = string.Empty;
			_descriptionEdit.Text = string.Empty;
			_tagsEdit.Text = string.Empty;
			_imagePathEdit.Text = string.Empty;
			_widthSpin.Value = 1;
			_heightSpin.Value = 1;
			_canvasTitleLabel.Text = "未选择条目";
			_suppressUiEvents = false;
			RefreshValidation();
			return;
		}

		_idEdit.Text = entry.Id;
		_nameEdit.Text = entry.Name;
		_descriptionEdit.Text = entry.Description;
		_tagsEdit.Text = string.Join(", ", entry.Tags);
		_imagePathEdit.Text = GetEntryImagePathText(entry);
		_widthSpin.Value = entry.Width;
		_heightSpin.Value = entry.Height;
		SelectCategory(_categoryEdit, entry.Category, false);
		SelectStatus(_statusEdit, entry.Status, false);
		SelectPreset(_presetEdit, entry.CanvasPresetId, entry.Width, entry.Height);
		ConfigureSizeEditors(entry.CanvasPresetId == PlaceholderWorkspaceCatalog.CustomPresetId);
		_canvasTitleLabel.Text = $"{entry.Name} ({entry.Id})";
		_suppressUiEvents = false;
		RefreshValidation();
	}

	private void RefreshValidation()
	{
		var entry = ActiveEntry;
		if (entry == null)
		{
			_validationLabel.Text = "未选择条目。";
			return;
		}

		var issues = ValidateEntry(entry);
		_validationLabel.Text = issues.Count == 0 ? "当前条目有效。" : string.Join("\n", issues);
	}

	private void RefreshFrameSection()
	{
		var entry = ActiveEntry;
		_suppressUiEvents = true;
		_frameList.Clear();

		if (entry == null)
		{
			_frameSummaryLabel.Text = "第 - 帧 / 共 0 帧";
			_overlayOpacityLabel.Text = $"{Mathf.RoundToInt((float)_overlayOpacitySlider.Value)}%";
			SetInteractiveState(_frameList, false);
			_addFrameButton.Disabled = true;
			_duplicateFrameButton.Disabled = true;
			_deleteFrameButton.Disabled = true;
			_overlayToggle.Disabled = true;
			SetInteractiveState(_overlayOpacitySlider, false);
			_suppressUiEvents = false;
			RefreshOverlay();
			return;
		}

		EnsureActiveFrameIndex(entry);
		var dirtySet = GetDirtyFrameSet(entry, create: false);
		for (var i = 0; i < entry.Frames.Count; i++)
		{
			var dirty = dirtySet != null && dirtySet.Contains(i) ? "*" : " ";
			var fileName = Path.GetFileName(entry.Frames[i].ImagePath.Replace('\\', '/'));
			_frameList.AddItem($"{dirty} {i + 1:00} | {fileName}");
		}

		if (entry.Frames.Count > 0)
			_frameList.Select(_activeFrameIndex);

		_frameSummaryLabel.Text = $"第 {_activeFrameIndex + 1} 帧 / 共 {entry.Frames.Count} 帧";
		_overlayOpacityLabel.Text = $"{Mathf.RoundToInt((float)_overlayOpacitySlider.Value)}%";
		SetInteractiveState(_frameList, true);
		_addFrameButton.Disabled = false;
		_duplicateFrameButton.Disabled = false;
		_deleteFrameButton.Disabled = entry.Frames.Count <= 1;
		_overlayToggle.Disabled = entry.Frames.Count <= 1;
		SetInteractiveState(_overlayOpacitySlider, _overlayToggle.ButtonPressed && _activeFrameIndex > 0 && entry.Frames.Count > 1);
		_imagePathEdit.Text = GetEntryImagePathText(entry);
		_suppressUiEvents = false;
		RefreshOverlay();
	}

	private void RefreshCanvasState()
	{
		var size = _canvas.CanvasSize;
		_canvasInfoLabel.Text = $"画布: {size.X} x {size.Y}";
		_zoomLabel.Text = $"缩放: {Mathf.RoundToInt(_canvas.Zoom * 100f)}%";
		_gridToggle.SetPressedNoSignal(_canvas.ShowGrid);
		_toolEdit.Select((int)_canvas.Tool);
		_undoButton.Disabled = !_canvas.CanUndo;
		_redoButton.Disabled = !_canvas.CanRedo;
		var enabled = ActiveEntry != null;
		var isDone = string.Equals(ActiveEntry?.Status, PlaceholderWorkspaceCatalog.DoneStatus, StringComparison.OrdinalIgnoreCase);
		_markDoneButton.Text = isDone ? "已完成" : "标记已完成";
		_markDoneButton.Disabled = !enabled || isDone;
		_clearButton.Disabled = !enabled;
		_zoomInButton.Disabled = !enabled;
		_zoomOutButton.Disabled = !enabled;
		_resetViewButton.Disabled = !enabled;
	}

	private void HandleCanvasImageModified()
	{
		var entry = ActiveEntry;
		if (entry == null)
			return;

		MarkEntryDirty(entry, _activeFrameIndex);
		RefreshEntryList();
		RefreshValidation();
		RefreshFrameSection();
	}

	private void HandleIdChanged(string value)
	{
		if (_suppressUiEvents)
			return;

		var entry = ActiveEntry;
		if (entry == null)
			return;

		FlushCanvasToActiveFrameCache();
		PrimeFrameCache(entry);
		var oldPaths = GetEntryFrameResPaths(entry);
		entry.Id = PlaceholderWorkspaceCatalog.SanitizeId(value);
		PlaceholderWorkspaceCatalog.RewriteFramePaths(entry);
		MarkPathsForDeletion(oldPaths, GetEntryFrameResPaths(entry));
		_workspaceDirty = true;
		_dirtyEntries.Add(entry);
		MarkAllFramesDirty(entry);
		_suppressUiEvents = true;
		_idEdit.Text = entry.Id;
		_suppressUiEvents = false;
		_canvasTitleLabel.Text = $"{entry.Name} ({entry.Id})";
		RefreshEntryList();
		RefreshEditorFields();
		RefreshFrameSection();
	}

	private void ChangeActiveCategory(string category)
	{
		var entry = ActiveEntry;
		if (entry == null)
			return;

		FlushCanvasToActiveFrameCache();
		PrimeFrameCache(entry);
		var oldPaths = GetEntryFrameResPaths(entry);
		entry.Category = category;
		PlaceholderWorkspaceCatalog.RewriteFramePaths(entry);
		MarkPathsForDeletion(oldPaths, GetEntryFrameResPaths(entry));
		_workspaceDirty = true;
		_dirtyEntries.Add(entry);
		MarkAllFramesDirty(entry);
		_canvasTitleLabel.Text = $"{entry.Name} ({entry.Id})";
		RefreshEntryList();
		RefreshEditorFields();
		RefreshFrameSection();
		RefreshValidation();
	}

	private void ApplyPresetSelection()
	{
		if (_suppressUiEvents)
			return;

		var entry = ActiveEntry;
		if (entry == null)
			return;

		var preset = GetSelectedPreset(_presetEdit, entry.Width, entry.Height);
		entry.CanvasPresetId = preset.Id;
		if (!preset.IsCustom)
		{
			entry.Width = preset.Width;
			entry.Height = preset.Height;
			ResizeEntryFrames(entry, true);
		}

		_workspaceDirty = true;
		_dirtyEntries.Add(entry);
		_suppressUiEvents = true;
		_widthSpin.Value = entry.Width;
		_heightSpin.Value = entry.Height;
		_suppressUiEvents = false;
		ConfigureSizeEditors(preset.IsCustom);
		RefreshEntryList();
		RefreshFrameSection();
		RefreshValidation();
	}

	private void ChangeActiveCanvasSize(int? width, int? height)
	{
		if (_suppressUiEvents)
			return;

		var entry = ActiveEntry;
		if (entry == null)
			return;

		entry.Width = Math.Max(width ?? entry.Width, 1);
		entry.Height = Math.Max(height ?? entry.Height, 1);
		entry.CanvasPresetId = PlaceholderWorkspaceCatalog.CustomPresetId;
		ResizeEntryFrames(entry, false);
		_workspaceDirty = true;
		_dirtyEntries.Add(entry);
		RefreshEntryList();
		RefreshFrameSection();
		RefreshValidation();
	}

	private void ResizeEntryFrames(PlaceholderWorkspaceEntry entry, bool fitToView)
	{
		FlushCanvasToActiveFrameCache();
		if (_frameCache.TryGetValue(entry, out var cache))
		{
			var keys = cache.Keys.ToList();
			foreach (var key in keys)
				cache[key] = EnsureImageSize(cache[key], entry.Width, entry.Height);
		}

		if (ReferenceEquals(entry, ActiveEntry))
			LoadActiveFrameToCanvas(fitToView);
	}

	private void ClearCanvas()
	{
		if (ActiveEntry != null)
			_canvas.ClearImage();
	}

	private void MutateActiveEntry(Action<PlaceholderWorkspaceEntry> mutation)
	{
		if (_suppressUiEvents)
			return;

		var entry = ActiveEntry;
		if (entry == null)
			return;

		mutation(entry);
		_workspaceDirty = true;
		_dirtyEntries.Add(entry);
		RefreshEntryList();
		RefreshValidation();
		RefreshFrameSection();
		RefreshCanvasState();
	}

	private void SaveWorkspace(bool showSuccessMessage)
	{
		var errors = CollectValidationErrors();
		if (errors.Count > 0)
		{
			ShowMessage(string.Join("\n", errors.Take(12)));
			return;
		}

		try
		{
			FlushCanvasToActiveFrameCache();
			var activeEntryId = ActiveEntry?.Id;
			var activeFrameIndex = _activeFrameIndex;
			foreach (var entry in _dirtyEntries.ToList())
				SaveEntryFrames(entry);

			PlaceholderWorkspaceStore.SaveManifest(_workspaceResPath, _manifest);
			DeleteMarkedImageFiles();
			_manifest = PlaceholderWorkspaceStore.LoadManifest(_workspaceResPath);
			_savedManifest = _manifest.DeepClone();
			_workspaceDirty = false;
			_dirtyEntries.Clear();
			_dirtyFrames.Clear();
			_frameCache.Clear();
			_deletedImageResPaths.Clear();
			_statusMessage = "工作区已保存。";
			RefreshEntryList();
			ActivateEntryById(activeEntryId, false);
			if (ActiveEntry != null && activeFrameIndex > 0)
				SelectFrame(activeFrameIndex, fitToView: false);
			if (showSuccessMessage)
				_statusLabel.Text = _statusMessage;
		}
		catch (Exception ex)
		{
			ShowMessage($"保存工作区失败。\n{ex.Message}");
		}
	}

	private void SaveEntryFrames(PlaceholderWorkspaceEntry entry)
	{
		var cache = PrimeFrameCache(entry);
		for (var i = 0; i < entry.Frames.Count; i++)
		{
			var image = EnsureImageSize(cache[i], entry.Width, entry.Height);
			cache[i] = image;
			SaveImage(GetEntryFrameResPath(entry, i), image);
		}
	}

	private void DuplicateActiveEntry()
	{
		var entry = ActiveEntry;
		if (entry == null)
			return;

		FlushCanvasToActiveFrameCache();
		PrimeFrameCache(entry);

		var duplicate = entry.DeepClone();
		duplicate.Id = PlaceholderWorkspaceCatalog.EnsureUniqueId($"{entry.Id}_copy", _manifest.Entries);
		duplicate.Name = $"{entry.Name} 副本";
		PlaceholderWorkspaceCatalog.RewriteFramePaths(duplicate);
		_manifest.Entries.Insert(_activeEntryIndex + 1, duplicate);
		_workspaceDirty = true;
		_dirtyEntries.Add(duplicate);
		MarkAllFramesDirty(duplicate);

		var sourceCache = PrimeFrameCache(entry);
		var duplicateCache = GetEntryFrameCache(duplicate);
		for (var i = 0; i < duplicate.Frames.Count; i++)
			duplicateCache[i] = DuplicateImage(sourceCache[i]);

		_activeEntryIndex = _manifest.Entries.IndexOf(duplicate);
		_activeFrameIndex = Math.Clamp(_activeFrameIndex, 0, Math.Max(duplicate.Frames.Count - 1, 0));
		LoadActiveFrameToCanvas(true);
		_statusMessage = $"已复制 {entry.Id} 到 {duplicate.Id}。";
		RefreshEntryList();
		RefreshEditorFields();
		RefreshFrameSection();
		RefreshCanvasState();
	}

	private void RequestDeleteActiveEntry()
	{
		if (ActiveEntry != null)
			_deleteDialog.PopupCentered();
	}

	private void DeleteActiveEntry()
	{
		var entry = ActiveEntry;
		if (entry == null)
			return;

		var nextId = _manifest.Entries.Skip(_activeEntryIndex + 1).FirstOrDefault()?.Id
			?? _manifest.Entries.Take(_activeEntryIndex).LastOrDefault()?.Id;

		foreach (var resPath in GetEntryFrameResPaths(entry))
		{
			if (!string.IsNullOrWhiteSpace(resPath))
				_deletedImageResPaths.Add(resPath);
		}

		_manifest.Entries.Remove(entry);
		_dirtyEntries.Remove(entry);
		_dirtyFrames.Remove(entry);
		_frameCache.Remove(entry);
		_workspaceDirty = true;
		_statusMessage = $"已删除 {entry.Id}。";
		RefreshEntryList();
		ActivateEntryById(nextId, true);
	}

	private void MarkActiveEntryDone()
	{
		var entry = ActiveEntry;
		if (entry == null)
			return;
		if (string.Equals(entry.Status, PlaceholderWorkspaceCatalog.DoneStatus, StringComparison.OrdinalIgnoreCase))
			return;

		entry.Status = PlaceholderWorkspaceCatalog.DoneStatus;
		_workspaceDirty = true;
		_dirtyEntries.Add(entry);
		_statusMessage = $"已标记 {entry.Id} 为已完成。";
		RefreshEntryList();
		RefreshEditorFields();
		RefreshValidation();
		RefreshCanvasState();
	}

	private void AddFrameAfterActive()
	{
		var entry = ActiveEntry;
		if (entry == null)
			return;

		FlushCanvasToActiveFrameCache();
		var oldPaths = GetEntryFrameResPaths(entry);
		var oldCache = PrimeFrameCache(entry);
		var insertIndex = Math.Clamp(_activeFrameIndex + 1, 0, entry.Frames.Count);

		entry.Frames.Insert(insertIndex, new PlaceholderWorkspaceFrame());
		PlaceholderWorkspaceCatalog.RewriteFramePaths(entry);
		MarkPathsForDeletion(oldPaths, GetEntryFrameResPaths(entry));

		var rebuiltCache = new Dictionary<int, Image>();
		for (var i = 0; i < entry.Frames.Count; i++)
		{
			if (i < insertIndex && oldCache.TryGetValue(i, out var before))
			{
				rebuiltCache[i] = before;
				continue;
			}

			if (i == insertIndex)
			{
				rebuiltCache[i] = CreateBlankImage(entry.Width, entry.Height);
				continue;
			}

			if (oldCache.TryGetValue(i - 1, out var after))
				rebuiltCache[i] = after;
		}

		_frameCache[entry] = rebuiltCache;
		_workspaceDirty = true;
		_dirtyEntries.Add(entry);
		MarkAllFramesDirty(entry);
		_statusMessage = $"已为 {entry.Id} 新增第 {insertIndex + 1} 帧。";
		SelectFrame(insertIndex, fitToView: false, forceReload: true);
		RefreshEntryList();
		RefreshEditorFields();
		RefreshFrameSection();
	}

	private void DuplicateActiveFrame()
	{
		var entry = ActiveEntry;
		if (entry == null)
			return;

		FlushCanvasToActiveFrameCache();
		var oldPaths = GetEntryFrameResPaths(entry);
		var oldCache = PrimeFrameCache(entry);
		var insertIndex = Math.Clamp(_activeFrameIndex + 1, 0, entry.Frames.Count);
		var sourceImage = DuplicateImage(oldCache[_activeFrameIndex]);

		entry.Frames.Insert(insertIndex, new PlaceholderWorkspaceFrame());
		PlaceholderWorkspaceCatalog.RewriteFramePaths(entry);
		MarkPathsForDeletion(oldPaths, GetEntryFrameResPaths(entry));

		var rebuiltCache = new Dictionary<int, Image>();
		for (var i = 0; i < entry.Frames.Count; i++)
		{
			if (i < insertIndex && oldCache.TryGetValue(i, out var before))
			{
				rebuiltCache[i] = before;
				continue;
			}

			if (i == insertIndex)
			{
				rebuiltCache[i] = sourceImage;
				continue;
			}

			if (oldCache.TryGetValue(i - 1, out var after))
				rebuiltCache[i] = after;
		}

		_frameCache[entry] = rebuiltCache;
		_workspaceDirty = true;
		_dirtyEntries.Add(entry);
		MarkAllFramesDirty(entry);
		_statusMessage = $"已复制第 {_activeFrameIndex + 1} 帧。";
		SelectFrame(insertIndex, fitToView: false, forceReload: true);
		RefreshEntryList();
		RefreshEditorFields();
		RefreshFrameSection();
	}

	private void DeleteActiveFrame()
	{
		var entry = ActiveEntry;
		if (entry == null || entry.Frames.Count <= 1)
			return;

		FlushCanvasToActiveFrameCache();
		var oldPaths = GetEntryFrameResPaths(entry);
		var oldCache = PrimeFrameCache(entry);
		var removedIndex = _activeFrameIndex;

		entry.Frames.RemoveAt(removedIndex);
		PlaceholderWorkspaceCatalog.RewriteFramePaths(entry);
		MarkPathsForDeletion(oldPaths, GetEntryFrameResPaths(entry));

		var rebuiltCache = new Dictionary<int, Image>();
		for (var i = 0; i < entry.Frames.Count; i++)
		{
			var sourceIndex = i >= removedIndex ? i + 1 : i;
			if (oldCache.TryGetValue(sourceIndex, out var preserved))
				rebuiltCache[i] = preserved;
		}

		_frameCache[entry] = rebuiltCache;
		_workspaceDirty = true;
		_dirtyEntries.Add(entry);
		MarkAllFramesDirty(entry);
		_activeFrameIndex = Math.Clamp(removedIndex, 0, Math.Max(entry.Frames.Count - 1, 0));
		_statusMessage = $"已删除第 {removedIndex + 1} 帧。";
		LoadActiveFrameToCanvas(false);
		RefreshEntryList();
		RefreshEditorFields();
		RefreshFrameSection();
		RefreshCanvasState();
	}

	private bool ChangeActiveFrameByOffset(int delta)
	{
		var entry = ActiveEntry;
		if (entry == null)
			return false;

		return SelectFrame(_activeFrameIndex + delta, fitToView: false);
	}

	private bool SelectFrame(int index, bool fitToView, bool forceReload = false)
	{
		var entry = ActiveEntry;
		if (entry == null)
			return false;
		if (index < 0 || index >= entry.Frames.Count)
			return false;
		if (!forceReload && index == _activeFrameIndex)
			return false;

		FlushCanvasToActiveFrameCache();
		_activeFrameIndex = index;
		LoadActiveFrameToCanvas(fitToView);
		_statusMessage = $"已切换到第 {index + 1} 帧。";
		RefreshEditorFields();
		RefreshFrameSection();
		RefreshCanvasState();
		return true;
	}

	private void LoadActiveFrameToCanvas(bool fitToView)
	{
		var entry = ActiveEntry;
		if (entry == null)
			return;

		EnsureActiveFrameIndex(entry);
		_canvas.LoadImage(GetFrameImageCopy(entry, _activeFrameIndex), fitToView);
		RefreshOverlay();
	}

	private void RefreshOverlay()
	{
		var entry = ActiveEntry;
		if (entry == null || !_overlayToggle.ButtonPressed || _activeFrameIndex <= 0)
		{
			_canvas.SetOverlayImage(null, 0f);
			return;
		}

		var opacity = (float)_overlayOpacitySlider.Value / 100f;
		if (opacity <= 0f)
		{
			_canvas.SetOverlayImage(null, 0f);
			return;
		}

		_canvas.SetOverlayImage(GetFrameImageCopy(entry, _activeFrameIndex - 1), opacity);
	}

	private void OpenNewEntryDialog()
	{
		var category = ActiveEntry?.Category ?? PlaceholderWorkspaceCatalog.NpcMapCategory;
		var presetId = PlaceholderWorkspaceCatalog.DefaultPresetForCategory(category);
		var preset = PlaceholderWorkspaceCatalog.ResolvePreset(presetId, 256, 256);

		_newIdEdit.Text = string.Empty;
		_newNameEdit.Text = string.Empty;
		_newDescriptionEdit.Text = string.Empty;
		_newTagsEdit.Text = string.Empty;
		SelectCategory(_newCategoryEdit, category, false);
		SelectStatus(_newStatusEdit, PlaceholderWorkspaceCatalog.TodoStatus, false);
		SelectPreset(_newPresetEdit, preset.Id, preset.Width, preset.Height);
		_newWidthSpin.Value = preset.IsCustom ? 256 : preset.Width;
		_newHeightSpin.Value = preset.IsCustom ? 256 : preset.Height;
		ConfigureNewEntrySizeEditors(preset.IsCustom);
		_entryDialog.PopupCenteredRatio(0.6f);
	}

	private void SyncNewEntryPresetWithCategory()
	{
		var category = GetCategoryValue(_newCategoryEdit, false);
		var presetId = PlaceholderWorkspaceCatalog.DefaultPresetForCategory(category);
		SelectPreset(_newPresetEdit, presetId, 256, 256);
		ApplyNewEntryPreset();
	}

	private void ApplyNewEntryPreset()
	{
		var preset = GetSelectedPreset(_newPresetEdit, (int)_newWidthSpin.Value, (int)_newHeightSpin.Value);
		if (!preset.IsCustom)
		{
			_newWidthSpin.Value = preset.Width;
			_newHeightSpin.Value = preset.Height;
		}

		ConfigureNewEntrySizeEditors(preset.IsCustom);
	}

	private void CreateEntryFromDialog()
	{
		var rawId = string.IsNullOrWhiteSpace(_newIdEdit.Text) ? _newNameEdit.Text : _newIdEdit.Text;
		var id = PlaceholderWorkspaceCatalog.EnsureUniqueId(rawId, _manifest.Entries);
		var name = _newNameEdit.Text.Trim();
		var description = _newDescriptionEdit.Text.Trim();
		if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
		{
			ShowMessage("新条目必须填写名称和描述。");
			return;
		}

		var category = GetCategoryValue(_newCategoryEdit, false);
		var status = GetStatusValue(_newStatusEdit, false);
		var preset = GetSelectedPreset(_newPresetEdit, (int)_newWidthSpin.Value, (int)_newHeightSpin.Value);
		var width = preset.IsCustom ? (int)_newWidthSpin.Value : preset.Width;
		var height = preset.IsCustom ? (int)_newHeightSpin.Value : preset.Height;

		var entry = new PlaceholderWorkspaceEntry
		{
			Id = id,
			Name = name,
			Description = description,
			Category = category,
			Status = status,
			CanvasPresetId = preset.IsCustom ? PlaceholderWorkspaceCatalog.CustomPresetId : preset.Id,
			Width = width,
			Height = height,
			Frames = PlaceholderWorkspaceCatalog.BuildDefaultFrames(category, id),
			Tags = ParseTags(_newTagsEdit.Text),
		};

		_manifest.Entries.Add(entry);
		_workspaceDirty = true;
		_dirtyEntries.Add(entry);
		MarkAllFramesDirty(entry);
		GetEntryFrameCache(entry)[0] = CreateBlankImage(entry.Width, entry.Height);
		_statusMessage = $"已创建 {entry.Id}。";
		RefreshEntryList();
		_activeEntryIndex = _manifest.Entries.IndexOf(entry);
		_activeFrameIndex = 0;
		LoadActiveFrameToCanvas(true);
		RefreshEditorFields();
		RefreshFrameSection();
		RefreshCanvasState();
	}

	private void BeginPendingOperation(Action action)
	{
		if (!HasUnsavedChanges())
		{
			action();
			return;
		}

		_pendingOperation = new PendingOperation { Action = action };
		_unsavedDialog.PopupCentered();
	}

	private void HandleUnsavedSave()
	{
		SaveWorkspace(false);
		if (!HasUnsavedChanges())
			RunPendingOperation();
	}

	private void HandleUnsavedAction(StringName action)
	{
		if (!string.Equals(action.ToString(), "discard", StringComparison.Ordinal))
			return;

		DiscardUnsavedChanges();
		RunPendingOperation();
	}

	private void RunPendingOperation()
	{
		var pending = _pendingOperation;
		_pendingOperation = null;
		pending?.Action();
	}

	private void DiscardUnsavedChanges()
	{
		var preferredEntryId = ActiveEntry?.Id;
		_manifest = _savedManifest.DeepClone();
		_dirtyEntries.Clear();
		_dirtyFrames.Clear();
		_frameCache.Clear();
		_deletedImageResPaths.Clear();
		_workspaceDirty = false;
		_activeFrameIndex = 0;
		_statusMessage = "已放弃未保存更改。";
		RefreshEntryList();
		ActivateEntryById(preferredEntryId, false);
	}

	private bool HasUnsavedChanges() => _workspaceDirty || _dirtyEntries.Count > 0;

	private List<string> CollectValidationErrors()
	{
		var issues = new List<string>();
		var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var entry in _manifest.Entries)
		{
			foreach (var issue in ValidateEntry(entry))
				issues.Add($"{entry.Id}: {issue}");
			if (!string.IsNullOrWhiteSpace(entry.Id) && !seenIds.Add(entry.Id))
				issues.Add($"{entry.Id}: ID 重复");
		}

		return issues.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static List<string> ValidateEntry(PlaceholderWorkspaceEntry entry)
	{
		var issues = new List<string>();
		if (string.IsNullOrWhiteSpace(entry.Id))
			issues.Add("ID 必填");
		if (string.IsNullOrWhiteSpace(entry.Name))
			issues.Add("名称必填");
		if (string.IsNullOrWhiteSpace(entry.Description))
			issues.Add("描述必填");
		if (!PlaceholderWorkspaceCatalog.Categories.Contains(entry.Category, StringComparer.OrdinalIgnoreCase))
			issues.Add("分类无效");
		if (!PlaceholderWorkspaceCatalog.Statuses.Contains(entry.Status, StringComparer.OrdinalIgnoreCase))
			issues.Add("状态无效");
		if (entry.Width <= 0 || entry.Height <= 0)
			issues.Add("尺寸必须大于 0");
		if (entry.Frames.Count <= 0)
			issues.Add("至少需要 1 帧");
		for (var i = 0; i < entry.Frames.Count; i++)
		{
			if (string.IsNullOrWhiteSpace(entry.Frames[i].ImagePath))
				issues.Add($"第 {i + 1} 帧路径无效");
		}

		return issues;
	}

	private bool MatchesFilters(PlaceholderWorkspaceEntry entry)
	{
		var search = _searchEdit.Text.Trim();
		if (!string.IsNullOrWhiteSpace(search))
		{
			var haystack = string.Join(
				" ",
				entry.Id,
				entry.Name,
				entry.Description,
				entry.Category,
				entry.Status,
				string.Join(' ', entry.Tags));
			if (!haystack.Contains(search, StringComparison.OrdinalIgnoreCase))
				return false;
		}

		var category = GetCategoryValue(_categoryFilter, true);
		if (!string.IsNullOrWhiteSpace(category) && !string.Equals(entry.Category, category, StringComparison.OrdinalIgnoreCase))
			return false;
		var status = GetStatusValue(_statusFilter, true);
		if (!string.IsNullOrWhiteSpace(status) && !string.Equals(entry.Status, status, StringComparison.OrdinalIgnoreCase))
			return false;
		return true;
	}

	private string BuildEntryLabel(PlaceholderWorkspaceEntry entry)
	{
		var dirty = _dirtyEntries.Contains(entry) ? "*" : " ";
		return $"{dirty} {entry.Name} | {PlaceholderWorkspaceCatalog.GetCategoryLabel(entry.Category)} | {entry.Width}x{entry.Height} | {entry.Frames.Count}帧 | {PlaceholderWorkspaceCatalog.GetStatusLabel(entry.Status)}";
	}

	private Dictionary<int, Image> GetEntryFrameCache(PlaceholderWorkspaceEntry entry)
	{
		if (!_frameCache.TryGetValue(entry, out var cache))
		{
			cache = new Dictionary<int, Image>();
			_frameCache[entry] = cache;
		}

		return cache;
	}

	private Dictionary<int, Image> PrimeFrameCache(PlaceholderWorkspaceEntry entry)
	{
		var cache = GetEntryFrameCache(entry);
		for (var i = 0; i < entry.Frames.Count; i++)
		{
			if (!cache.ContainsKey(i))
				cache[i] = LoadImageForFrame(entry, i);
		}

		return cache;
	}

	private Image GetFrameImageCopy(PlaceholderWorkspaceEntry entry, int frameIndex) =>
		DuplicateImage(GetFrameImage(entry, frameIndex));

	private Image GetFrameImage(PlaceholderWorkspaceEntry entry, int frameIndex)
	{
		var cache = GetEntryFrameCache(entry);
		if (!cache.TryGetValue(frameIndex, out var image))
		{
			image = LoadImageForFrame(entry, frameIndex);
			cache[frameIndex] = image;
		}

		return image;
	}

	private Image LoadImageForFrame(PlaceholderWorkspaceEntry entry, int frameIndex)
	{
		var resPath = GetEntryFrameResPath(entry, frameIndex);
		return LoadImageFromResPath(resPath, entry.Width, entry.Height);
	}

	private void FlushCanvasToActiveFrameCache()
	{
		var entry = ActiveEntry;
		if (entry == null || _activeFrameIndex < 0 || _activeFrameIndex >= entry.Frames.Count)
			return;

		GetEntryFrameCache(entry)[_activeFrameIndex] = EnsureImageSize(_canvas.GetImageCopy(), entry.Width, entry.Height);
	}

	private void EnsureActiveFrameIndex(PlaceholderWorkspaceEntry entry)
	{
		if (entry.Frames.Count == 0)
		{
			_activeFrameIndex = 0;
			return;
		}

		_activeFrameIndex = Math.Clamp(_activeFrameIndex, 0, entry.Frames.Count - 1);
	}

	private HashSet<int>? GetDirtyFrameSet(PlaceholderWorkspaceEntry entry, bool create)
	{
		if (_dirtyFrames.TryGetValue(entry, out var set))
			return set;
		if (!create)
			return null;

		set = [];
		_dirtyFrames[entry] = set;
		return set;
	}

	private void MarkEntryDirty(PlaceholderWorkspaceEntry entry, int? frameIndex = null)
	{
		_workspaceDirty = true;
		_dirtyEntries.Add(entry);
		if (frameIndex.HasValue)
			GetDirtyFrameSet(entry, create: true)!.Add(frameIndex.Value);
	}

	private void MarkAllFramesDirty(PlaceholderWorkspaceEntry entry)
	{
		var set = GetDirtyFrameSet(entry, create: true)!;
		set.Clear();
		for (var i = 0; i < entry.Frames.Count; i++)
			set.Add(i);
	}

	private List<string> GetEntryFrameResPaths(PlaceholderWorkspaceEntry entry)
	{
		var paths = new List<string>(entry.Frames.Count);
		for (var i = 0; i < entry.Frames.Count; i++)
			paths.Add(GetEntryFrameResPath(entry, i));
		return paths;
	}

	private string GetEntryFrameResPath(PlaceholderWorkspaceEntry entry, int frameIndex)
	{
		if (frameIndex < 0 || frameIndex >= entry.Frames.Count)
			return string.Empty;
		if (string.IsNullOrWhiteSpace(entry.Frames[frameIndex].ImagePath))
			return string.Empty;

		return PlaceholderWorkspaceStore.CombineResPath(_workspaceResPath, entry.Frames[frameIndex].ImagePath);
	}

	private string GetEntryImagePathText(PlaceholderWorkspaceEntry entry)
	{
		var resPath = GetEntryFrameResPath(entry, _activeFrameIndex);
		return string.IsNullOrWhiteSpace(resPath) ? "（需要有效 ID）" : resPath;
	}

	private void MarkPathsForDeletion(IEnumerable<string> oldPaths, IEnumerable<string> newPaths)
	{
		var nextPaths = new HashSet<string>(newPaths.Where(static path => !string.IsNullOrWhiteSpace(path)), StringComparer.OrdinalIgnoreCase);
		foreach (var oldPath in oldPaths.Where(static path => !string.IsNullOrWhiteSpace(path)))
		{
			if (!nextPaths.Contains(oldPath))
				_deletedImageResPaths.Add(oldPath);
		}
	}

	private void DeleteMarkedImageFiles()
	{
		var livePaths = new HashSet<string>(
			_manifest.Entries
				.SelectMany(GetEntryFrameResPaths)
				.Where(static path => !string.IsNullOrWhiteSpace(path)),
			StringComparer.OrdinalIgnoreCase);

		foreach (var resPath in _deletedImageResPaths)
		{
			if (livePaths.Contains(resPath))
				continue;

			var globalPath = PlaceholderWorkspaceStore.GlobalizeResPath(resPath);
			if (File.Exists(globalPath))
				File.Delete(globalPath);
		}
	}

	private static void SaveImage(string resPath, Image image)
	{
		if (string.IsNullOrWhiteSpace(resPath))
			return;

		var globalPath = PlaceholderWorkspaceStore.GlobalizeResPath(resPath);
		var directory = Path.GetDirectoryName(globalPath);
		if (!string.IsNullOrWhiteSpace(directory))
			Directory.CreateDirectory(directory);
		File.WriteAllBytes(globalPath, image.SavePngToBuffer());
	}

	private static Image LoadImageFromResPath(string resPath, int width, int height)
	{
		var image = CreateBlankImage(width, height);
		if (string.IsNullOrWhiteSpace(resPath))
			return image;

		var globalPath = PlaceholderWorkspaceStore.GlobalizeResPath(resPath);
		if (!File.Exists(globalPath))
			return image;

		var loaded = Image.LoadFromFile(globalPath);
		if (loaded == null)
			return image;

		if (loaded.GetFormat() != Image.Format.Rgba8)
			loaded.Convert(Image.Format.Rgba8);

		return EnsureImageSize(loaded, width, height);
	}

	private static Image CreateBlankImage(int width, int height)
	{
		var image = Image.CreateEmpty(Math.Max(width, 1), Math.Max(height, 1), false, Image.Format.Rgba8);
		image.Fill(Colors.Transparent);
		return image;
	}

	private static Image EnsureImageSize(Image image, int width, int height)
	{
		if (image.GetWidth() == width && image.GetHeight() == height)
			return image;

		var resized = CreateBlankImage(width, height);
		var copyWidth = Math.Min(width, image.GetWidth());
		var copyHeight = Math.Min(height, image.GetHeight());
		if (copyWidth > 0 && copyHeight > 0)
			resized.BlitRect(image, new Rect2I(0, 0, copyWidth, copyHeight), Vector2I.Zero);
		return resized;
	}

	private static Image DuplicateImage(Image source) =>
		Image.CreateFromData(source.GetWidth(), source.GetHeight(), false, source.GetFormat(), source.GetData());

	private static List<string> ParseTags(string raw) =>
		raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Where(static tag => !string.IsNullOrWhiteSpace(tag))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

	private Button CreateActionButton(string text, Action action, bool expand = false)
	{
		var button = new Button
		{
			Text = text,
			SizeFlagsHorizontal = expand ? SizeFlags.ExpandFill : SizeFlags.Fill,
		};
		button.Pressed += action;
		return button;
	}

	private static MarginContainer CreatePanelMargin(Control parent)
	{
		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_top", 12);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_bottom", 12);
		parent.AddChild(margin);
		return margin;
	}

	private static VBoxContainer CreateVBox(Node parent, int separation)
	{
		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", separation);
		parent.AddChild(vbox);
		return vbox;
	}

	private static void AddLabeledField(GridContainer grid, string label, Control control)
	{
		grid.AddChild(new Label { Text = label });
		control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		grid.AddChild(control);
	}

	private static SpinBox CreateSpinBox(double min, double max, double value) => new()
	{
		MinValue = min,
		MaxValue = max,
		Value = value,
		Step = 1,
	};

	private static void PopulateCategoryButton(OptionButton button, bool includeAll)
	{
		button.Clear();
		if (includeAll)
			button.AddItem("全部分类");
		foreach (var category in PlaceholderWorkspaceCatalog.Categories)
			button.AddItem(PlaceholderWorkspaceCatalog.GetCategoryLabel(category));
		button.Select(0);
	}

	private static void PopulateStatusButton(OptionButton button, bool includeAll)
	{
		button.Clear();
		if (includeAll)
			button.AddItem("全部状态");
		foreach (var status in PlaceholderWorkspaceCatalog.Statuses)
			button.AddItem(PlaceholderWorkspaceCatalog.GetStatusLabel(status));
		button.Select(0);
	}

	private static void PopulatePresetButton(OptionButton button)
	{
		button.Clear();
		foreach (var preset in PlaceholderWorkspaceCatalog.Presets)
			button.AddItem(preset.Label);
		button.Select(0);
	}

	private static string GetCategoryValue(OptionButton button, bool includeAll)
	{
		if (includeAll && button.Selected <= 0)
			return string.Empty;
		var offset = includeAll ? 1 : 0;
		var index = Math.Clamp(button.Selected - offset, 0, PlaceholderWorkspaceCatalog.Categories.Count - 1);
		return PlaceholderWorkspaceCatalog.Categories[index];
	}

	private static string GetStatusValue(OptionButton button, bool includeAll)
	{
		if (includeAll && button.Selected <= 0)
			return string.Empty;
		var offset = includeAll ? 1 : 0;
		var index = Math.Clamp(button.Selected - offset, 0, PlaceholderWorkspaceCatalog.Statuses.Count - 1);
		return PlaceholderWorkspaceCatalog.Statuses[index];
	}

	private static PlaceholderCanvasPreset GetSelectedPreset(OptionButton button, int width, int height)
	{
		var index = Math.Clamp(button.Selected, 0, PlaceholderWorkspaceCatalog.Presets.Count - 1);
		var preset = PlaceholderWorkspaceCatalog.Presets[index];
		return preset.IsCustom ? new PlaceholderCanvasPreset(preset.Id, preset.Label, width, height, true) : preset;
	}

	private static void SelectCategory(OptionButton button, string category, bool includeAll)
	{
		var offset = includeAll ? 1 : 0;
		var index = PlaceholderWorkspaceCatalog.Categories
			.ToList()
			.FindIndex(item => string.Equals(item, category, StringComparison.OrdinalIgnoreCase));
		button.Select(index >= 0 ? index + offset : 0);
	}

	private static void SelectStatus(OptionButton button, string status, bool includeAll)
	{
		var offset = includeAll ? 1 : 0;
		var index = PlaceholderWorkspaceCatalog.Statuses
			.ToList()
			.FindIndex(item => string.Equals(item, status, StringComparison.OrdinalIgnoreCase));
		button.Select(index >= 0 ? index + offset : 0);
	}

	private static void SelectPreset(OptionButton button, string presetId, int width, int height)
	{
		var preset = PlaceholderWorkspaceCatalog.ResolvePreset(presetId, width, height);
		var index = PlaceholderWorkspaceCatalog.Presets
			.ToList()
			.FindIndex(item => string.Equals(item.Id, preset.Id, StringComparison.OrdinalIgnoreCase));
		button.Select(index >= 0 ? index : PlaceholderWorkspaceCatalog.Presets.Count - 1);
	}

	private static string GetToolLabel(PlaceholderCanvasControl.ToolMode tool) => tool switch
	{
		PlaceholderCanvasControl.ToolMode.Pencil => "铅笔",
		PlaceholderCanvasControl.ToolMode.Eraser => "橡皮",
		PlaceholderCanvasControl.ToolMode.Bucket => "油漆桶",
		PlaceholderCanvasControl.ToolMode.Eyedropper => "取色器",
		PlaceholderCanvasControl.ToolMode.Pan => "平移",
		_ => tool.ToString(),
	};

	private void ConfigureSizeEditors(bool isCustom)
	{
		_widthSpin.Editable = isCustom;
		_heightSpin.Editable = isCustom;
	}

	private void ConfigureNewEntrySizeEditors(bool isCustom)
	{
		_newWidthSpin.Editable = isCustom;
		_newHeightSpin.Editable = isCustom;
	}

	private void SetEditorEnabled(bool enabled)
	{
		_idEdit.Editable = enabled;
		_nameEdit.Editable = enabled;
		_descriptionEdit.Editable = enabled;
		_tagsEdit.Editable = enabled;
		_categoryEdit.Disabled = !enabled;
		_statusEdit.Disabled = !enabled;
		_presetEdit.Disabled = !enabled;
		_toolEdit.Disabled = !enabled;
		_colorButton.Disabled = !enabled;
		_gridToggle.Disabled = !enabled;
	}

	private static void SetInteractiveState(Control control, bool enabled)
	{
		control.MouseFilter = enabled ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
		control.FocusMode = enabled ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
		control.Modulate = enabled ? Colors.White : new Color(1f, 1f, 1f, 0.65f);
	}

	private void ShowMessage(string message)
	{
		_messageDialog.DialogText = message;
		_messageDialog.PopupCenteredRatio(0.48f);
	}

	private bool HasBlockingPopupOpen() =>
		_workspaceDialog.Visible
		|| _unsavedDialog.Visible
		|| _messageDialog.Visible
		|| _deleteDialog.Visible
		|| _entryDialog.Visible;

	private bool IsTextEditingActive()
	{
		var focusOwner = GetViewport().GuiGetFocusOwner();
		return focusOwner is LineEdit or TextEdit or SpinBox;
	}
}
