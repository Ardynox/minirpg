using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using MiniRPG.Module.Editor;

namespace MiniRPG;

public partial class ResourceCatalogEditor : Control
{
	private const string DefaultImportRoot = "res://Assets/Art/Tilesets";
	private static readonly Regex NumericTailPattern = new(@"^(?<prefix>.*?)(?<number>\d+)$", RegexOptions.Compiled);

	private enum EditorPane
	{
		Catalog,
		Staging,
	}

	private readonly ResourceCatalogDocument _catalog = ResourceCatalogStore.CreateEmpty();
	private readonly List<ResourceCatalogEntry> _staging = [];
	private readonly HashSet<string> _dirtyCatalogIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Texture2D?> _textureCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Vector2I> _textureSizeCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<string> _visibleCatalogIds = [];
	private readonly List<string> _visibleStagingIds = [];
	private readonly Dictionary<ResourceCatalogEntry, ResourceCatalogRegion?> _entryDisplayRegionOverrides = [];
	private readonly Dictionary<ResourceCatalogFrame, ResourceCatalogRegion?> _frameDisplayRegionOverrides = [];

	private ResourcePreviewControl _preview = null!;
	private TabContainer _listTabs = null!;
	private ItemList _catalogList = null!;
	private ItemList _stagingList = null!;
	private LineEdit _searchEdit = null!;
	private LineEdit _tagFilterEdit = null!;
	private OptionButton _kindFilter = null!;
	private Label _summaryLabel = null!;
	private Label _statusLabel = null!;
	private Label _selectionLabel = null!;
	private Label _validationLabel = null!;
	private Label _previewLabel = null!;
	private Label _sliceInfoLabel = null!;
	private Button _importImageButton = null!;
	private Button _importFolderButton = null!;
	private Button _createAnimationButton = null!;
	private Button _saveButton = null!;
	private Button _backButton = null!;
	private Button _acceptSelectedButton = null!;
	private Button _removeSelectedButton = null!;
	private PanelContainer _batchPanel = null!;
	private LineEdit _batchIdEdit = null!;
	private LineEdit _batchTagsEdit = null!;
	private Button _batchRenameButton = null!;
	private Button _batchTagsButton = null!;
	private OptionButton _kindEdit = null!;
	private LineEdit _idEdit = null!;
	private LineEdit _displayNameEdit = null!;
	private TextEdit _descriptionEdit = null!;
	private LineEdit _tagsEdit = null!;
	private LineEdit _categoryEdit = null!;
	private Label _sourceModeValue = null!;
	private LineEdit _sourceImageValue = null!;
	private LineEdit _sourceFolderValue = null!;
	private SpinBox _regionXSpin = null!;
	private SpinBox _regionYSpin = null!;
	private SpinBox _regionWidthSpin = null!;
	private SpinBox _regionHeightSpin = null!;
	private SpinBox _fpsSpin = null!;
	private ItemList _frameList = null!;
	private Button _frameUpButton = null!;
	private Button _frameDownButton = null!;
	private Button _frameRemoveButton = null!;
	private HSlider _sliceWidthSlider = null!;
	private SpinBox _sliceWidthSpin = null!;
	private HSlider _sliceHeightSlider = null!;
	private SpinBox _sliceHeightSpin = null!;
	private HSlider _sliceOffsetXSlider = null!;
	private SpinBox _sliceOffsetXSpin = null!;
	private HSlider _sliceOffsetYSlider = null!;
	private SpinBox _sliceOffsetYSpin = null!;
	private HSlider _sliceSpacingXSlider = null!;
	private SpinBox _sliceSpacingXSpin = null!;
	private HSlider _sliceSpacingYSlider = null!;
	private SpinBox _sliceSpacingYSpin = null!;
	private Button _stageSlicesButton = null!;
	private Button _resetSliceButton = null!;

	private FileDialog _imageDialog = null!;
	private FileDialog _folderDialog = null!;

	private bool _suppressUiEvents;
	private bool _catalogDirty;
	private string? _activeCatalogId;
	private string? _activeStagingId;
	private string _statusMessage = string.Empty;
	private double _previewAccumulator;
	private int _previewFrameIndex;
	private int _selectedFrameIndex = -1;
	private bool _previewInteractionActive;
	private bool _suppressSliceEvents;

	public override void _Ready()
	{
		BindNodes();

		LocalizationService.Initialize();
		LocalizationService.SetLocale(AppSettingsStore.LoadLocale(), notify: false);
		ConfigureWidgets();
		BuildFileDialogs();
		WireEvents();
		LocalizationService.LocalizeTree(this);

		ResetSliceInputs();
		LoadCatalog();
		RefreshUi(fullRefresh: true);
	}

	public override void _ExitTree()
	{
		LocalizationService.LocaleChanged -= HandleLocaleChanged;
	}

	public override void _Process(double delta)
	{
		var activeEntry = GetActiveEntry();
		if (activeEntry?.Kind != ResourceCatalogKinds.Animation
			|| activeEntry.Frames.Count <= 1
			|| _selectedFrameIndex >= 0)
			return;

		var fps = Math.Max(activeEntry.Fps ?? 10f, 0.1f);
		_previewAccumulator += delta;
		var frameDuration = 1.0 / fps;
		if (_previewAccumulator < frameDuration)
			return;

		_previewAccumulator = 0;
		_previewFrameIndex = (_previewFrameIndex + 1) % activeEntry.Frames.Count;
		RefreshSlicePanel();
		RefreshPreview();
	}

	private void BindNodes()
	{
		_preview = GetNode<ResourcePreviewControl>("Margin/Root/Body/Middle/PreviewPanel/Margin/Preview");
		_listTabs = GetNode<TabContainer>("Margin/Root/Body/Left/ListTabs");
		_catalogList = GetNode<ItemList>("Margin/Root/Body/Left/ListTabs/Catalog/List");
		_stagingList = GetNode<ItemList>("Margin/Root/Body/Left/ListTabs/Staging/List");
		_searchEdit = GetNode<LineEdit>("Margin/Root/Filters/SearchEdit");
		_tagFilterEdit = GetNode<LineEdit>("Margin/Root/Filters/TagFilterEdit");
		_kindFilter = GetNode<OptionButton>("Margin/Root/Filters/KindFilter");
		_summaryLabel = GetNode<Label>("Margin/Root/Toolbar/Summary");
		_statusLabel = GetNode<Label>("Margin/Root/Toolbar/Status");
		_selectionLabel = GetNode<Label>("Margin/Root/Body/Right/Scroll/Content/SelectionLabel");
		_validationLabel = GetNode<Label>("Margin/Root/Body/Right/Scroll/Content/ValidationLabel");
		_previewLabel = GetNode<Label>("Margin/Root/Body/Middle/PreviewInfo");
		_sliceInfoLabel = GetNode<Label>("Margin/Root/Body/Middle/SlicePanel/Margin/Content/SliceInfo");
		_importImageButton = GetNode<Button>("Margin/Root/Toolbar/Actions/ImportImageBtn");
		_importFolderButton = GetNode<Button>("Margin/Root/Toolbar/Actions/ImportFolderBtn");
		_createAnimationButton = GetNode<Button>("Margin/Root/Toolbar/Actions/CreateAnimationBtn");
		_saveButton = GetNode<Button>("Margin/Root/Toolbar/Actions/SaveBtn");
		_backButton = GetNode<Button>("Margin/Root/Toolbar/Actions/BackBtn");
		_acceptSelectedButton = GetNode<Button>("Margin/Root/Body/Right/Scroll/Content/ActionRow/AcceptSelectedBtn");
		_removeSelectedButton = GetNode<Button>("Margin/Root/Body/Right/Scroll/Content/ActionRow/RemoveSelectedBtn");
		_batchPanel = GetNode<PanelContainer>("Margin/Root/Body/Right/Scroll/Content/BatchPanel");
		_batchIdEdit = GetNode<LineEdit>("Margin/Root/Body/Right/Scroll/Content/BatchPanel/Margin/Content/BaseIdEdit");
		_batchTagsEdit = GetNode<LineEdit>("Margin/Root/Body/Right/Scroll/Content/BatchPanel/Margin/Content/TagsEdit");
		_batchRenameButton = GetNode<Button>("Margin/Root/Body/Right/Scroll/Content/BatchPanel/Margin/Content/Buttons/RenameBtn");
		_batchTagsButton = GetNode<Button>("Margin/Root/Body/Right/Scroll/Content/BatchPanel/Margin/Content/Buttons/TagsBtn");
		_kindEdit = GetNode<OptionButton>("Margin/Root/Body/Right/Scroll/Content/PropertiesPanel/Margin/Content/Form/KindEdit");
		_idEdit = GetNode<LineEdit>("Margin/Root/Body/Right/Scroll/Content/PropertiesPanel/Margin/Content/Form/IdEdit");
		_displayNameEdit = GetNode<LineEdit>("Margin/Root/Body/Right/Scroll/Content/PropertiesPanel/Margin/Content/Form/DisplayNameEdit");
		_descriptionEdit = GetNode<TextEdit>("Margin/Root/Body/Right/Scroll/Content/PropertiesPanel/Margin/Content/Form/DescriptionEdit");
		_tagsEdit = GetNode<LineEdit>("Margin/Root/Body/Right/Scroll/Content/PropertiesPanel/Margin/Content/Form/TagsEdit");
		_categoryEdit = GetNode<LineEdit>("Margin/Root/Body/Right/Scroll/Content/PropertiesPanel/Margin/Content/Form/CategoryEdit");
		_sourceModeValue = GetNode<Label>("Margin/Root/Body/Right/Scroll/Content/PropertiesPanel/Margin/Content/Form/SourceModeValue");
		_regionXSpin = GetNode<SpinBox>("Margin/Root/Body/Right/Scroll/Content/PropertiesPanel/Margin/Content/Form/RegionXSpin");
		_regionYSpin = GetNode<SpinBox>("Margin/Root/Body/Right/Scroll/Content/PropertiesPanel/Margin/Content/Form/RegionYSpin");
		_regionWidthSpin = GetNode<SpinBox>("Margin/Root/Body/Right/Scroll/Content/PropertiesPanel/Margin/Content/Form/RegionWidthSpin");
		_regionHeightSpin = GetNode<SpinBox>("Margin/Root/Body/Right/Scroll/Content/PropertiesPanel/Margin/Content/Form/RegionHeightSpin");
		_sourceImageValue = GetNode<LineEdit>("Margin/Root/Body/Right/Scroll/Content/SourcePanel/Margin/Content/SourceImageValue");
		_sourceFolderValue = GetNode<LineEdit>("Margin/Root/Body/Right/Scroll/Content/SourcePanel/Margin/Content/SourceFolderValue");
		_fpsSpin = GetNode<SpinBox>("Margin/Root/Body/Right/Scroll/Content/PropertiesPanel/Margin/Content/Form/FpsSpin");
		_frameList = GetNode<ItemList>("Margin/Root/Body/Right/Scroll/Content/FramesPanel/Margin/Content/FrameList");
		_frameUpButton = GetNode<Button>("Margin/Root/Body/Right/Scroll/Content/FramesPanel/Margin/Content/Buttons/MoveUpBtn");
		_frameDownButton = GetNode<Button>("Margin/Root/Body/Right/Scroll/Content/FramesPanel/Margin/Content/Buttons/MoveDownBtn");
		_frameRemoveButton = GetNode<Button>("Margin/Root/Body/Right/Scroll/Content/FramesPanel/Margin/Content/Buttons/RemoveBtn");
		_sliceWidthSlider = GetNode<HSlider>("Margin/Root/Body/Middle/SlicePanel/Margin/Content/Grid/WidthRow/WidthSlider");
		_sliceWidthSpin = GetNode<SpinBox>("Margin/Root/Body/Middle/SlicePanel/Margin/Content/Grid/WidthRow/WidthSpin");
		_sliceHeightSlider = GetNode<HSlider>("Margin/Root/Body/Middle/SlicePanel/Margin/Content/Grid/HeightRow/HeightSlider");
		_sliceHeightSpin = GetNode<SpinBox>("Margin/Root/Body/Middle/SlicePanel/Margin/Content/Grid/HeightRow/HeightSpin");
		_sliceOffsetXSlider = GetNode<HSlider>("Margin/Root/Body/Middle/SlicePanel/Margin/Content/Grid/OffsetXRow/OffsetXSlider");
		_sliceOffsetXSpin = GetNode<SpinBox>("Margin/Root/Body/Middle/SlicePanel/Margin/Content/Grid/OffsetXRow/OffsetXSpin");
		_sliceOffsetYSlider = GetNode<HSlider>("Margin/Root/Body/Middle/SlicePanel/Margin/Content/Grid/OffsetYRow/OffsetYSlider");
		_sliceOffsetYSpin = GetNode<SpinBox>("Margin/Root/Body/Middle/SlicePanel/Margin/Content/Grid/OffsetYRow/OffsetYSpin");
		_sliceSpacingXSlider = GetNode<HSlider>("Margin/Root/Body/Middle/SlicePanel/Margin/Content/Grid/SpacingXRow/SpacingXSlider");
		_sliceSpacingXSpin = GetNode<SpinBox>("Margin/Root/Body/Middle/SlicePanel/Margin/Content/Grid/SpacingXRow/SpacingXSpin");
		_sliceSpacingYSlider = GetNode<HSlider>("Margin/Root/Body/Middle/SlicePanel/Margin/Content/Grid/SpacingYRow/SpacingYSlider");
		_sliceSpacingYSpin = GetNode<SpinBox>("Margin/Root/Body/Middle/SlicePanel/Margin/Content/Grid/SpacingYRow/SpacingYSpin");
		_stageSlicesButton = GetNode<Button>("Margin/Root/Body/Middle/SlicePanel/Margin/Content/Buttons/StageSlicesBtn");
		_resetSliceButton = GetNode<Button>("Margin/Root/Body/Middle/SlicePanel/Margin/Content/Buttons/ResetBtn");
	}

	private void BuildFileDialogs()
	{
		_imageDialog = new FileDialog
		{
			FileMode = FileDialog.FileModeEnum.OpenFile,
			Access = FileDialog.AccessEnum.Filesystem,
			UseNativeDialog = true,
			Filters = ["*.png ; PNG"],
		};
		_folderDialog = new FileDialog
		{
			FileMode = FileDialog.FileModeEnum.OpenDir,
			Access = FileDialog.AccessEnum.Filesystem,
			UseNativeDialog = true,
		};

		var defaultDir = ProjectSettings.GlobalizePath(DefaultImportRoot);
		if (DirAccess.DirExistsAbsolute(defaultDir))
		{
			_imageDialog.CurrentDir = defaultDir;
			_folderDialog.CurrentDir = defaultDir;
		}

		AddChild(_imageDialog);
		AddChild(_folderDialog);
		RefreshDialogTitles();
	}

	private void ConfigureWidgets()
	{
		_catalogList.SelectMode = ItemList.SelectModeEnum.Multi;
		_stagingList.SelectMode = ItemList.SelectModeEnum.Multi;
		_frameList.SelectMode = ItemList.SelectModeEnum.Single;

		_kindFilter.Clear();
		_kindFilter.AddItem(LocalizationService.T("ui.resource_catalog.filter.all"), 0);
		_kindFilter.AddItem(LocalizationService.T("ui.resource_catalog.filter.tile"), 1);
		_kindFilter.AddItem(LocalizationService.T("ui.resource_catalog.filter.animation"), 2);
		_kindFilter.Select(0);

		_kindEdit.Clear();
		_kindEdit.AddItem(LocalizationService.T("ui.resource_catalog.kind.tile"), 0);
		_kindEdit.AddItem(LocalizationService.T("ui.resource_catalog.kind.animation"), 1);
		_kindEdit.Select(0);

		_listTabs.SetTabTitle(0, LocalizationService.T("ui.resource_catalog.tab.catalog"));
		_listTabs.SetTabTitle(1, LocalizationService.T("ui.resource_catalog.tab.staging"));
	}

	private void WireEvents()
	{
		LocalizationService.LocaleChanged += HandleLocaleChanged;

		_importImageButton.Pressed += () => _imageDialog.PopupCenteredRatio(0.7f);
		_importFolderButton.Pressed += () => _folderDialog.PopupCenteredRatio(0.7f);
		_createAnimationButton.Pressed += CreateAnimationFromSelection;
		_saveButton.Pressed += SaveCatalog;
		_backButton.Pressed += CloseEditor;
		_acceptSelectedButton.Pressed += AcceptSelectedStagingEntries;
		_removeSelectedButton.Pressed += RemoveSelectedEntries;
		_batchRenameButton.Pressed += ApplyBatchRename;
		_batchTagsButton.Pressed += ApplyBatchTags;
		_stageSlicesButton.Pressed += StageSlicesFromSelection;
		_resetSliceButton.Pressed += () =>
		{
			ResetSliceInputs();
			OnSliceSettingsChanged();
		};
		_frameUpButton.Pressed += () => MoveFrame(-1);
		_frameDownButton.Pressed += () => MoveFrame(1);
		_frameRemoveButton.Pressed += RemoveSelectedFrame;

		_imageDialog.FileSelected += ImportImage;
		_folderDialog.DirSelected += ImportFolder;

		_searchEdit.TextChanged += _ => RefreshLists();
		_tagFilterEdit.TextChanged += _ => RefreshLists();
		_kindFilter.ItemSelected += _ => RefreshLists();
		_listTabs.TabChanged += _ => RefreshUi(fullRefresh: false);
		_preview.RegionChanged += HandlePreviewRegionChanged;
		_preview.InteractionStateChanged += active =>
		{
			_previewInteractionActive = active;
			if (!active)
				RefreshPreview();
		};

		_catalogList.ItemSelected += index => HandleListSelection(EditorPane.Catalog, index, selected: true);
		_catalogList.MultiSelected += (index, selected) => HandleListSelection(EditorPane.Catalog, index, selected);
		_stagingList.ItemSelected += index => HandleListSelection(EditorPane.Staging, index, selected: true);
		_stagingList.MultiSelected += (index, selected) => HandleListSelection(EditorPane.Staging, index, selected);
		_frameList.ItemSelected += index =>
		{
			_selectedFrameIndex = (int)index;
			_previewFrameIndex = _selectedFrameIndex;
			RefreshSlicePanel();
			RefreshEditorFields();
			RefreshPreview();
			RefreshFrameButtons();
		};

		_idEdit.TextChanged += OnIdChanged;
		_displayNameEdit.TextChanged += value => MutateActiveEntry(entry => entry.DisplayName = value);
		_descriptionEdit.TextChanged += () => MutateActiveEntry(entry => entry.Description = _descriptionEdit.Text);
		_tagsEdit.TextChanged += value => MutateActiveEntry(entry => entry.Tags = ParseTags(value));
		_categoryEdit.TextChanged += value => MutateActiveEntry(entry => entry.Category = value.Trim());
		_kindEdit.ItemSelected += _ => ChangeActiveKind();
		_fpsSpin.ValueChanged += value => MutateActiveEntry(entry => entry.Fps = Math.Max((float)value, 0.1f));
		_regionXSpin.ValueChanged += _ => ApplyRegionFromInputs();
		_regionYSpin.ValueChanged += _ => ApplyRegionFromInputs();
		_regionWidthSpin.ValueChanged += _ => ApplyRegionFromInputs();
		_regionHeightSpin.ValueChanged += _ => ApplyRegionFromInputs();

		WireSliceInputPair(_sliceWidthSlider, _sliceWidthSpin);
		WireSliceInputPair(_sliceHeightSlider, _sliceHeightSpin);
		WireSliceInputPair(_sliceOffsetXSlider, _sliceOffsetXSpin);
		WireSliceInputPair(_sliceOffsetYSlider, _sliceOffsetYSpin);
		WireSliceInputPair(_sliceSpacingXSlider, _sliceSpacingXSpin);
		WireSliceInputPair(_sliceSpacingYSlider, _sliceSpacingYSpin);
	}

	private void HandleLocaleChanged(string _)
	{
		LocalizationService.LocalizeTree(this);
		ConfigureWidgets();
		RefreshDialogTitles();
		RefreshUi(fullRefresh: true);
	}

	private void RefreshDialogTitles()
	{
		_imageDialog.Title = LocalizationService.T("ui.resource_catalog.dialog.image");
		_folderDialog.Title = LocalizationService.T("ui.resource_catalog.dialog.folder");
	}

	private void LoadCatalog()
	{
		_catalog.Entries.Clear();
		_entryDisplayRegionOverrides.Clear();
		_frameDisplayRegionOverrides.Clear();
		var loaded = ResourceCatalogStore.Load();
		foreach (var entry in loaded.Entries)
			_catalog.Entries.Add(entry);

		_catalog.Version = loaded.Version;
		_catalogDirty = false;
		_dirtyCatalogIds.Clear();
		_statusMessage = LocalizationService.T("ui.resource_catalog.status.loaded", ("count", _catalog.Entries.Count));

		if (_catalog.Entries.Count > 0)
			_activeCatalogId = _catalog.Entries[0].Id;
	}

	private void RefreshUi(bool fullRefresh)
	{
		if (fullRefresh)
			RefreshLists();

		RefreshSummary();
		RefreshSelectionInfo();
		RefreshFrameList();
		RefreshFrameButtons();
		RefreshSlicePanel();
		RefreshEditorFields();
		RefreshPreview();
		RefreshStatus();
	}

	private void WireSliceInputPair(HSlider slider, SpinBox spin)
	{
		slider.ValueChanged += value => HandleSliceSliderChanged(slider, spin, value);
		spin.ValueChanged += value => HandleSliceSpinChanged(slider, spin, value);
	}

	private void HandleSliceSliderChanged(HSlider slider, SpinBox spin, double value)
	{
		if (_suppressSliceEvents)
			return;

		_suppressSliceEvents = true;
		spin.Value = value;
		_suppressSliceEvents = false;
		OnSliceSettingsChanged();
	}

	private void HandleSliceSpinChanged(HSlider slider, SpinBox spin, double value)
	{
		if (_suppressSliceEvents)
			return;

		_suppressSliceEvents = true;
		slider.Value = value;
		_suppressSliceEvents = false;
		OnSliceSettingsChanged();
	}

	private void SetSliceInputPairValue(HSlider slider, SpinBox spin, double value)
	{
		_suppressSliceEvents = true;
		slider.Value = value;
		spin.Value = value;
		_suppressSliceEvents = false;
	}

	private void ConfigureSliceInputPair(HSlider slider, SpinBox spin, double min, double max, bool sliderEnabled)
	{
		var clamped = Math.Clamp(spin.Value, min, max);

		_suppressSliceEvents = true;
		slider.MinValue = min;
		slider.MaxValue = max;
		slider.Step = 1;
		slider.Value = clamped;
		slider.MouseFilter = sliderEnabled ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
		slider.Modulate = sliderEnabled ? Colors.White : new Color(0.62f, 0.62f, 0.62f, 0.85f);

		spin.MinValue = min;
		spin.MaxValue = max;
		spin.Step = 1;
		spin.Value = clamped;
		_suppressSliceEvents = false;
	}

	private void RefreshLists()
	{
		var catalogSelection = GetSelectedIds(EditorPane.Catalog);
		var stagingSelection = GetSelectedIds(EditorPane.Staging);

		_suppressUiEvents = true;
		_visibleCatalogIds.Clear();
		_visibleStagingIds.Clear();
		_catalogList.Clear();
		_stagingList.Clear();

		foreach (var entry in _catalog.Entries.Where(MatchesFilters))
		{
			_visibleCatalogIds.Add(entry.Id);
			_catalogList.AddItem(BuildListLabel(entry, inCatalog: true));
		}

		foreach (var entry in _staging.Where(MatchesFilters))
		{
			_visibleStagingIds.Add(entry.Id);
			_stagingList.AddItem(BuildListLabel(entry, inCatalog: false));
		}

		RestoreSelection(EditorPane.Catalog, catalogSelection);
		RestoreSelection(EditorPane.Staging, stagingSelection);
		EnsureActiveSelection(EditorPane.Catalog);
		EnsureActiveSelection(EditorPane.Staging);
		_suppressUiEvents = false;
		RefreshUi(fullRefresh: false);
	}

	private void RestoreSelection(EditorPane pane, HashSet<string> selection)
	{
		var list = GetListControl(pane);
		var visibleIds = GetVisibleIds(pane);
		list.DeselectAll();

		for (var i = 0; i < visibleIds.Count; i++)
		{
			if (selection.Contains(visibleIds[i]))
				list.Select(i, false);
		}
	}

	private void EnsureActiveSelection(EditorPane pane)
	{
		var list = GetListControl(pane);
		var visibleIds = GetVisibleIds(pane);
		if (visibleIds.Count == 0)
		{
			SetActiveId(pane, null);
			return;
		}

		var activeId = GetActiveId(pane);
		if (!string.IsNullOrEmpty(activeId) && visibleIds.Contains(activeId, StringComparer.OrdinalIgnoreCase))
			return;

		var selectedIds = GetSelectedIds(pane);
		var replacement = selectedIds.Count > 0
			? visibleIds.FirstOrDefault(selectedIds.Contains)
			: visibleIds[0];

		SetActiveId(pane, replacement);
		if (replacement != null)
		{
			var index = visibleIds.IndexOf(replacement);
			if (index >= 0)
				list.Select(index, false);
		}
	}

	private void HandleListSelection(EditorPane pane, long rawIndex, bool selected)
	{
		if (_suppressUiEvents)
			return;

		var index = (int)rawIndex;
		var visibleIds = GetVisibleIds(pane);
		if (index < 0 || index >= visibleIds.Count)
			return;

		if (selected)
			SetActiveId(pane, visibleIds[index]);
		else if (string.Equals(GetActiveId(pane), visibleIds[index], StringComparison.OrdinalIgnoreCase))
			SetActiveId(pane, GetSelectedIds(pane).FirstOrDefault());

		_previewAccumulator = 0;
		_previewFrameIndex = 0;
		var activeEntry = GetActiveEntry();
		if (activeEntry != null)
			NormalizePreviewState(activeEntry);
		else
			_selectedFrameIndex = -1;
		RefreshUi(fullRefresh: false);
	}

	private void RefreshSummary()
	{
		var dirtyLabel = _catalogDirty
			? LocalizationService.T("ui.resource_catalog.summary.unsaved")
			: LocalizationService.T("ui.resource_catalog.summary.saved");
		_summaryLabel.Text = LocalizationService.T(
			"ui.resource_catalog.summary",
			("catalog", _catalog.Entries.Count),
			("staging", _staging.Count),
			("state", dirtyLabel));
	}

	private void RefreshSelectionInfo()
	{
		var pane = GetCurrentPane();
		var selectedCount = GetSelectedIds(pane).Count;
		var active = GetActiveEntry();
		var paneLabel = pane == EditorPane.Catalog
			? LocalizationService.T("ui.resource_catalog.tab.catalog")
			: LocalizationService.T("ui.resource_catalog.tab.staging");

		_selectionLabel.Text = active == null
			? LocalizationService.T("ui.resource_catalog.selection.none", ("pane", paneLabel))
			: LocalizationService.T(
				"ui.resource_catalog.selection.active",
				("pane", paneLabel),
				("count", selectedCount),
				("id", active.Id));
	}

	private void RefreshEditorFields()
	{
		var entry = GetActiveEntry();
		var pane = GetCurrentPane();
		var canEdit = entry != null;
		var selectedCount = GetSelectedIds(pane).Count;
		var showBatch = pane == EditorPane.Staging && selectedCount > 1;

		_suppressUiEvents = true;
		_kindEdit.Selected = entry?.Kind == ResourceCatalogKinds.Animation ? 1 : 0;
		_idEdit.Text = entry?.Id ?? string.Empty;
		_displayNameEdit.Text = entry?.DisplayName ?? string.Empty;
		_descriptionEdit.Text = entry?.Description ?? string.Empty;
		_tagsEdit.Text = entry == null ? string.Empty : string.Join(", ", entry.Tags);
		_categoryEdit.Text = entry?.Category ?? string.Empty;
		_sourceModeValue.Text = entry?.SourceMode ?? "-";
		_sourceImageValue.Text = entry?.SourceImagePath ?? string.Empty;
		_sourceFolderValue.Text = entry?.SourceFolderPath ?? string.Empty;
		_fpsSpin.Value = entry?.Fps ?? 10;
		_suppressUiEvents = false;

		_idEdit.Editable = canEdit;
		_displayNameEdit.Editable = canEdit;
		_descriptionEdit.Editable = canEdit;
		_tagsEdit.Editable = canEdit;
		_categoryEdit.Editable = canEdit;
		_kindEdit.Disabled = !canEdit;
		_fpsSpin.Editable = canEdit && entry?.Kind == ResourceCatalogKinds.Animation;

		_batchPanel.Visible = showBatch;
		_acceptSelectedButton.Visible = pane == EditorPane.Staging;
		_acceptSelectedButton.Disabled = pane != EditorPane.Staging || selectedCount == 0;
		_removeSelectedButton.Disabled = selectedCount == 0;

		RefreshRegionEditors(entry);
		RefreshValidation(entry);
	}

	private void RefreshRegionEditors(ResourceCatalogEntry? entry)
	{
		var enabled = TryGetEditableRegionContext(entry, out _, out var size, out var region, out _);
		var resolvedRegion = region ?? new ResourceCatalogRegion
		{
			X = 0,
			Y = 0,
			Width = Math.Max(size.X, 1),
			Height = Math.Max(size.Y, 1),
		};
		var maxX = Math.Max(size.X - resolvedRegion.Width, 0);
		var maxY = Math.Max(size.Y - resolvedRegion.Height, 0);
		var maxWidth = Math.Max(size.X - resolvedRegion.X, 1);
		var maxHeight = Math.Max(size.Y - resolvedRegion.Y, 1);

		_suppressUiEvents = true;
		_sourceModeValue.Text = entry?.SourceMode ?? "-";
		_regionXSpin.MinValue = 0;
		_regionYSpin.MinValue = 0;
		_regionWidthSpin.MinValue = 1;
		_regionHeightSpin.MinValue = 1;
		_regionXSpin.MaxValue = maxX;
		_regionYSpin.MaxValue = maxY;
		_regionWidthSpin.MaxValue = maxWidth;
		_regionHeightSpin.MaxValue = maxHeight;
		_regionXSpin.Value = resolvedRegion.X;
		_regionYSpin.Value = resolvedRegion.Y;
		_regionWidthSpin.Value = resolvedRegion.Width;
		_regionHeightSpin.Value = resolvedRegion.Height;
		_suppressUiEvents = false;

		_regionXSpin.Editable = enabled;
		_regionYSpin.Editable = enabled;
		_regionWidthSpin.Editable = enabled;
		_regionHeightSpin.Editable = enabled;
	}

	private void RefreshValidation(ResourceCatalogEntry? entry)
	{
		if (entry == null)
		{
			_validationLabel.Text = LocalizationService.T("ui.resource_catalog.validation.no_selection");
			return;
		}

		var issues = ValidateEntry(entry);
		if (issues.Count == 0)
		{
			_validationLabel.Text = LocalizationService.T("ui.resource_catalog.validation.ok");
			return;
		}

		_validationLabel.Text = string.Join("\n", issues.Select(issue => $"- {issue}"));
	}

	private void RefreshFrameList()
	{
		var entry = GetActiveEntry();
		_frameList.Clear();

		if (entry?.Kind != ResourceCatalogKinds.Animation || entry.Frames.Count == 0)
		{
			_selectedFrameIndex = -1;
			_previewFrameIndex = 0;
			return;
		}

		foreach (var frame in entry.Frames.OrderBy(frame => frame.Order))
			_frameList.AddItem(BuildFrameLabel(frame));

		_previewFrameIndex = Math.Clamp(_previewFrameIndex, 0, entry.Frames.Count - 1);
		_selectedFrameIndex = _selectedFrameIndex >= 0 && _selectedFrameIndex < entry.Frames.Count
			? _selectedFrameIndex
			: _previewFrameIndex;
		_previewFrameIndex = _selectedFrameIndex;

		if (_selectedFrameIndex >= 0)
			_frameList.Select(_selectedFrameIndex);
	}

	private void RefreshFrameButtons()
	{
		var entry = GetActiveEntry();
		var enabled = entry?.Kind == ResourceCatalogKinds.Animation
			&& _selectedFrameIndex >= 0
			&& _selectedFrameIndex < (entry?.Frames.Count ?? 0);
		_frameUpButton.Disabled = !enabled || _selectedFrameIndex <= 0;
		_frameDownButton.Disabled = !enabled || entry == null || _selectedFrameIndex >= entry.Frames.Count - 1;
		_frameRemoveButton.Disabled = !enabled;
	}

	private void RefreshSlicePanel()
	{
		var entry = GetActiveEntry();
		var hasPreviewSize = TryGetSlicePreviewSize(out var previewSize);
		RefreshSliceInputRanges(hasPreviewSize ? previewSize : null);

		var canSlice = TryGetSliceSource(entry, out _, out var size);
		_stageSlicesButton.Disabled = !canSlice;
		_resetSliceButton.Disabled = false;

		if (!canSlice)
		{
			_sliceInfoLabel.Text = LocalizationService.T("ui.resource_catalog.slice.unavailable");
			return;
		}

		var count = BuildSliceCandidates(entry!, previewOnly: true).Count;
		_sliceInfoLabel.Text = LocalizationService.T(
			"ui.resource_catalog.slice.info",
			("count", count),
			("width", size.X),
			("height", size.Y));
	}

	private void RefreshPreview()
	{
		var entry = GetActiveEntry();
		if (entry == null)
		{
			_preview.SetPreview(null, null, null, editable: false);
			_previewLabel.Text = LocalizationService.T("ui.resource_catalog.preview.none");
			return;
		}

		if (entry.Kind == ResourceCatalogKinds.Animation)
		{
			if (entry.Frames.Count == 0)
			{
				_preview.SetPreview(null, null, null, editable: false);
				_previewLabel.Text = LocalizationService.T("ui.resource_catalog.preview.empty_animation");
				return;
			}

			var frameIndex = _selectedFrameIndex >= 0
				? _selectedFrameIndex
				: Math.Clamp(_previewFrameIndex, 0, entry.Frames.Count - 1);
			var frame = entry.Frames[frameIndex];
			var texture = GetTexture(frame.ImagePath);
			var frameRegion = TryGetTextureSize(frame.ImagePath, out var frameSize)
				? GetDisplayRegion(entry, frame.Region, frameSize, useDefaultRegion: true, frame)
				: frame.Region?.DeepClone();
			var editableFrame = _selectedFrameIndex >= 0;
			_preview.SetPreview(texture, frameRegion, editableFrame ? BuildGridOverlay() : null, editableFrame);
			_previewLabel.Text = editableFrame
				? LocalizationService.T(
					"ui.resource_catalog.preview.animation_frame",
					("frame", frameIndex + 1),
					("count", entry.Frames.Count),
					("fps", Math.Round(entry.Fps ?? 10f, 2)))
				: LocalizationService.T(
					"ui.resource_catalog.preview.animation",
					("frame", frameIndex + 1),
					("count", entry.Frames.Count),
					("fps", Math.Round(entry.Fps ?? 10f, 2)));
			return;
		}

		var tileTexture = entry.SourceImagePath == null ? null : GetTexture(entry.SourceImagePath);
		var grid = entry.SourceImagePath != null && TryGetTextureSize(entry.SourceImagePath, out var tileSize)
			? BuildGridOverlay()
			: null;
		var tileRegion = entry.SourceImagePath != null && TryGetTextureSize(entry.SourceImagePath, out tileSize)
			? GetDisplayRegion(entry, entry.Region, tileSize, useDefaultRegion: true)
			: entry.Region?.DeepClone();
		_preview.SetPreview(tileTexture, tileRegion, grid, editable: tileTexture != null);
		_previewLabel.Text = LocalizationService.T(
			"ui.resource_catalog.preview.tile",
			("mode", entry.SourceMode),
			("path", entry.SourceImagePath ?? "-"));
	}

	private void RefreshStatus()
	{
		_statusLabel.Text = string.IsNullOrEmpty(_statusMessage)
			? LocalizationService.T("ui.resource_catalog.status.ready")
			: _statusMessage;
	}

	private void OnIdChanged(string value)
	{
		if (_suppressUiEvents)
			return;

		var normalized = NormalizeId(value);
		if (!string.Equals(_idEdit.Text, normalized, StringComparison.Ordinal))
		{
			_suppressUiEvents = true;
			_idEdit.Text = normalized;
			_suppressUiEvents = false;
		}

		MutateActiveEntry(entry => entry.Id = normalized);
	}

	private void ApplyRegionFromInputs()
	{
		if (_suppressUiEvents)
			return;

		ApplyEditedRegion(new ResourceCatalogRegion
		{
			X = (int)_regionXSpin.Value,
			Y = (int)_regionYSpin.Value,
			Width = (int)_regionWidthSpin.Value,
			Height = (int)_regionHeightSpin.Value,
		}, refreshPreview: true);
	}

	private void HandlePreviewRegionChanged(ResourceCatalogRegion region)
	{
		ApplyEditedRegion(region, refreshPreview: false);
	}

	private void ApplyEditedRegion(ResourceCatalogRegion region, bool refreshPreview)
	{
		var entry = GetActiveEntry();
		if (!TryGetEditableRegionContext(entry, out _, out var size, out _, out var frame))
			return;

		var displayRegion = ClampRegionToBounds(region, size);
		var storedRegion = NormalizeRegionForStorage(region, size);
		if (frame == null)
		{
			entry!.Region = storedRegion;
			entry.SourceMode = storedRegion == null ? ResourceCatalogSourceModes.Single : ResourceCatalogSourceModes.Sheet;
			if (storedRegion == null)
				_entryDisplayRegionOverrides[entry] = displayRegion;
			else
				_entryDisplayRegionOverrides.Remove(entry);
		}
		else
		{
			frame.Region = storedRegion;
			if (storedRegion == null)
				_frameDisplayRegionOverrides[frame] = displayRegion;
			else
				_frameDisplayRegionOverrides.Remove(frame);
		}

		MarkEntryDirty(entry!);
		RefreshRegionEditors(entry);
		RefreshValidation(entry);
		RefreshStatus();

		if (frame != null && _selectedFrameIndex >= 0 && _selectedFrameIndex < _frameList.ItemCount)
			_frameList.SetItemText(_selectedFrameIndex, BuildFrameLabel(frame));

		if (refreshPreview || !_previewInteractionActive)
			RefreshPreview();
	}

	private bool TryGetEditableRegionContext(
		ResourceCatalogEntry? entry,
		out string sourcePath,
		out Vector2I size,
		out ResourceCatalogRegion? region,
		out ResourceCatalogFrame? frame)
	{
		sourcePath = string.Empty;
		size = Vector2I.Zero;
		region = null;
		frame = null;
		if (entry == null)
			return false;

		if (entry.Kind == ResourceCatalogKinds.Tile)
		{
			if (string.IsNullOrEmpty(entry.SourceImagePath) || !TryGetTextureSize(entry.SourceImagePath, out size))
				return false;

			sourcePath = entry.SourceImagePath;
			region = GetDisplayRegion(entry, entry.Region, size, useDefaultRegion: true);
			return true;
		}

		if (entry.Kind != ResourceCatalogKinds.Animation
			|| _selectedFrameIndex < 0
			|| _selectedFrameIndex >= entry.Frames.Count)
			return false;

		frame = entry.Frames[_selectedFrameIndex];
		if (string.IsNullOrEmpty(frame.ImagePath) || !TryGetTextureSize(frame.ImagePath, out size))
		{
			frame = null;
			return false;
		}

		sourcePath = frame.ImagePath;
		region = GetDisplayRegion(entry, frame.Region, size, useDefaultRegion: true, frame);
		return true;
	}

	private ResourceCatalogRegion? GetDisplayRegion(
		ResourceCatalogEntry entry,
		ResourceCatalogRegion? region,
		Vector2I size,
		bool useDefaultRegion,
		ResourceCatalogFrame? frame = null)
	{
		if (frame != null && _frameDisplayRegionOverrides.TryGetValue(frame, out var frameOverride))
			return frameOverride?.DeepClone();

		if (_entryDisplayRegionOverrides.TryGetValue(entry, out var entryOverride))
			return entryOverride?.DeepClone();

		if (region != null)
			return ClampRegionToBounds(region, size);

		return useDefaultRegion ? BuildDefaultPreviewRegion(size) : null;
	}

	private ResourceCatalogRegion BuildDefaultPreviewRegion(Vector2I size)
	{
		var width = BuildDefaultAxisSize((int)_sliceWidthSpin.Value, size.X);
		var height = BuildDefaultAxisSize((int)_sliceHeightSpin.Value, size.Y);
		return new ResourceCatalogRegion
		{
			X = Math.Max((size.X - width) / 2, 0),
			Y = Math.Max((size.Y - height) / 2, 0),
			Width = width,
			Height = height,
		};
	}

	private static int BuildDefaultAxisSize(int requested, int limit)
	{
		var safeLimit = Math.Max(limit, 1);
		var clamped = Math.Clamp(requested, 1, safeLimit);
		if (safeLimit <= 1)
			return 1;

		return clamped >= safeLimit ? safeLimit - 1 : clamped;
	}

	private static ResourceCatalogRegion ClampRegionToBounds(ResourceCatalogRegion region, Vector2I size)
	{
		var clamped = new ResourceCatalogRegion
		{
			X = Math.Clamp(region.X, 0, Math.Max(size.X - 1, 0)),
			Y = Math.Clamp(region.Y, 0, Math.Max(size.Y - 1, 0)),
		};
		clamped.Width = Math.Clamp(region.Width, 1, Math.Max(size.X - clamped.X, 1));
		clamped.Height = Math.Clamp(region.Height, 1, Math.Max(size.Y - clamped.Y, 1));
		return clamped;
	}

	private static ResourceCatalogRegion? NormalizeRegionForStorage(ResourceCatalogRegion region, Vector2I size)
	{
		var clamped = ClampRegionToBounds(region, size);

		return clamped.X == 0
			&& clamped.Y == 0
			&& clamped.Width == size.X
			&& clamped.Height == size.Y
				? null
				: clamped;
	}

	private void ChangeActiveKind()
	{
		if (_suppressUiEvents)
			return;

		var entry = GetActiveEntry();
		if (entry == null)
			return;

		var kind = _kindEdit.Selected == 1 ? ResourceCatalogKinds.Animation : ResourceCatalogKinds.Tile;
		ConvertEntryKind(entry, kind);
		AfterEntryMutation(entry);
	}

	private void ConvertEntryKind(ResourceCatalogEntry entry, string targetKind)
	{
		_entryDisplayRegionOverrides.Remove(entry);
		foreach (var frame in entry.Frames)
			_frameDisplayRegionOverrides.Remove(frame);

		entry.Kind = targetKind;
		if (targetKind == ResourceCatalogKinds.Animation)
		{
			if (entry.Frames.Count == 0 && !string.IsNullOrEmpty(entry.SourceImagePath))
			{
				entry.Frames =
				[
					new ResourceCatalogFrame
					{
						ImagePath = entry.SourceImagePath,
						Region = entry.Region?.DeepClone(),
						Order = 0,
					},
				];
			}

			entry.SourceMode = ResourceCatalogSourceModes.Frames;
			entry.Fps ??= 10f;
			return;
		}

		var firstFrame = entry.Frames.OrderBy(frame => frame.Order).FirstOrDefault();
		if (firstFrame != null)
		{
			entry.SourceImagePath = firstFrame.ImagePath;
			entry.Region = firstFrame.Region?.DeepClone();
			entry.SourceMode = entry.Region == null ? ResourceCatalogSourceModes.Single : ResourceCatalogSourceModes.Sheet;
		}
		else
		{
			entry.SourceMode = ResourceCatalogSourceModes.Single;
		}

		entry.SourceFolderPath = null;
		entry.Frames = [];
		entry.Fps = null;
	}

	private void MutateActiveEntry(Action<ResourceCatalogEntry> mutate)
	{
		if (_suppressUiEvents)
			return;

		var entry = GetActiveEntry();
		if (entry == null)
			return;

		mutate(entry);
		AfterEntryMutation(entry);
	}

	private void AfterEntryMutation(ResourceCatalogEntry entry)
	{
		MarkEntryDirty(entry);
		_previewAccumulator = 0;
		NormalizePreviewState(entry);
		_statusMessage = LocalizationService.T("ui.resource_catalog.status.entry_updated", ("id", entry.Id));
		RefreshLists();
	}

	private void MarkEntryDirty(ResourceCatalogEntry entry)
	{
		if (GetCurrentPane() != EditorPane.Catalog)
			return;

		_catalogDirty = true;
		if (!string.IsNullOrEmpty(entry.Id))
			_dirtyCatalogIds.Add(entry.Id);
	}

	private void NormalizePreviewState(ResourceCatalogEntry entry)
	{
		if (entry.Kind != ResourceCatalogKinds.Animation)
		{
			_selectedFrameIndex = -1;
			_previewFrameIndex = 0;
			return;
		}

		if (entry.Frames.Count == 0)
		{
			_selectedFrameIndex = -1;
			_previewFrameIndex = 0;
			return;
		}

		_previewFrameIndex = Math.Clamp(_previewFrameIndex, 0, entry.Frames.Count - 1);
		_selectedFrameIndex = _selectedFrameIndex >= 0 && _selectedFrameIndex < entry.Frames.Count
			? _selectedFrameIndex
			: _previewFrameIndex;
		_previewFrameIndex = _selectedFrameIndex;
	}

	private void AcceptSelectedStagingEntries()
	{
		var selected = GetSelectedEntries(EditorPane.Staging).ToList();
		if (selected.Count == 0)
		{
			_statusMessage = LocalizationService.T("ui.resource_catalog.status.no_staging_selection");
			RefreshStatus();
			return;
		}

		var existingIds = new HashSet<string>(_catalog.Entries.Select(entry => entry.Id), StringComparer.OrdinalIgnoreCase);
		var acceptedIds = new List<string>();
		foreach (var stagingEntry in selected)
		{
			var accepted = stagingEntry.DeepClone();
			accepted.Id = EnsureUniqueId(accepted.Id, existingIds);
			existingIds.Add(accepted.Id);
			if (_entryDisplayRegionOverrides.TryGetValue(stagingEntry, out var entryOverride))
				_entryDisplayRegionOverrides[accepted] = entryOverride?.DeepClone();

			for (var i = 0; i < stagingEntry.Frames.Count && i < accepted.Frames.Count; i++)
			{
				if (_frameDisplayRegionOverrides.TryGetValue(stagingEntry.Frames[i], out var frameOverride))
					_frameDisplayRegionOverrides[accepted.Frames[i]] = frameOverride?.DeepClone();
			}

			_catalog.Entries.Add(accepted);
			_dirtyCatalogIds.Add(accepted.Id);
			acceptedIds.Add(accepted.Id);
		}

		_catalogDirty = true;
		_staging.RemoveAll(entry => selected.Any(candidate => string.Equals(candidate.Id, entry.Id, StringComparison.OrdinalIgnoreCase)));
		_activeStagingId = _staging.FirstOrDefault()?.Id;
		_activeCatalogId = acceptedIds.FirstOrDefault() ?? _activeCatalogId;
		_listTabs.CurrentTab = 0;
		_statusMessage = LocalizationService.T("ui.resource_catalog.status.accepted", ("count", acceptedIds.Count));
		RefreshLists();
	}

	private void RemoveSelectedEntries()
	{
		var pane = GetCurrentPane();
		var selected = GetSelectedEntries(pane).ToList();
		if (selected.Count == 0)
			return;

		if (pane == EditorPane.Staging)
		{
			foreach (var entry in selected)
			{
				_entryDisplayRegionOverrides.Remove(entry);
				foreach (var frame in entry.Frames)
					_frameDisplayRegionOverrides.Remove(frame);
			}

			_staging.RemoveAll(entry => selected.Any(candidate => string.Equals(candidate.Id, entry.Id, StringComparison.OrdinalIgnoreCase)));
			_activeStagingId = _staging.FirstOrDefault()?.Id;
			_statusMessage = LocalizationService.T("ui.resource_catalog.status.removed_staging", ("count", selected.Count));
			RefreshLists();
			return;
		}

		foreach (var entry in selected)
		{
			_entryDisplayRegionOverrides.Remove(entry);
			foreach (var frame in entry.Frames)
				_frameDisplayRegionOverrides.Remove(frame);
		}

		_catalog.Entries.RemoveAll(entry => selected.Any(candidate => string.Equals(candidate.Id, entry.Id, StringComparison.OrdinalIgnoreCase)));
		foreach (var entry in selected)
			_dirtyCatalogIds.Add(entry.Id);
		_catalogDirty = true;
		_activeCatalogId = _catalog.Entries.FirstOrDefault()?.Id;
		_statusMessage = LocalizationService.T("ui.resource_catalog.status.removed_catalog", ("count", selected.Count));
		RefreshLists();
	}

	private void ApplyBatchRename()
	{
		var selected = GetSelectedEntries(EditorPane.Staging).ToList();
		if (selected.Count == 0)
			return;

		var baseId = NormalizeId(_batchIdEdit.Text);
		if (string.IsNullOrEmpty(baseId))
		{
			_statusMessage = LocalizationService.T("ui.resource_catalog.status.batch_id_required");
			RefreshStatus();
			return;
		}

		var existing = CombinedIds(except: selected);
		for (var i = 0; i < selected.Count; i++)
		{
			var targetId = selected.Count == 1
				? baseId
				: $"{baseId}_{i + 1:00}";
			selected[i].Id = EnsureUniqueId(targetId, existing);
			existing.Add(selected[i].Id);
		}

		_statusMessage = LocalizationService.T("ui.resource_catalog.status.batch_renamed", ("count", selected.Count));
		RefreshLists();
	}

	private void ApplyBatchTags()
	{
		var selected = GetSelectedEntries(EditorPane.Staging).ToList();
		if (selected.Count == 0)
			return;

		var tags = ParseTags(_batchTagsEdit.Text);
		foreach (var entry in selected)
			entry.Tags = [.. tags];

		_statusMessage = LocalizationService.T("ui.resource_catalog.status.batch_tagged", ("count", selected.Count));
		RefreshLists();
	}

	private void CreateAnimationFromSelection()
	{
		var pane = GetCurrentPane();
		var selected = GetSelectedEntriesInVisibleOrder(pane)
			.Where(entry => entry.Kind == ResourceCatalogKinds.Tile && !string.IsNullOrEmpty(entry.SourceImagePath))
			.ToList();
		if (selected.Count == 0)
		{
			_statusMessage = LocalizationService.T("ui.resource_catalog.status.no_tile_selection");
			RefreshStatus();
			return;
		}

		var first = selected[0];
		var baseId = EnsureUniqueId($"{NormalizeId(first.Id)}_anim", CombinedIds());
		var animation = new ResourceCatalogEntry
		{
			Id = baseId,
			Kind = ResourceCatalogKinds.Animation,
			DisplayName = $"{first.DisplayName} {LocalizationService.T("ui.resource_catalog.animation_suffix")}",
			Description = string.Empty,
			Tags = MergeTags(selected),
			Category = first.Category,
			SourceMode = ResourceCatalogSourceModes.Frames,
			SourceFolderPath = null,
			Frames = selected
				.Select((entry, index) => new ResourceCatalogFrame
				{
					ImagePath = entry.SourceImagePath!,
					Region = entry.Region?.DeepClone(),
					Order = index,
				})
				.ToList(),
			Fps = 10f,
		};

		_staging.Add(animation);
		_activeStagingId = animation.Id;
		_listTabs.CurrentTab = 1;
		_statusMessage = LocalizationService.T("ui.resource_catalog.status.animation_created", ("id", animation.Id));
		RefreshLists();
	}

	private void MoveFrame(int direction)
	{
		var entry = GetActiveEntry();
		if (entry?.Kind != ResourceCatalogKinds.Animation)
			return;

		if (_selectedFrameIndex < 0 || _selectedFrameIndex >= entry.Frames.Count)
			return;

		var targetIndex = _selectedFrameIndex + direction;
		if (targetIndex < 0 || targetIndex >= entry.Frames.Count)
			return;

		(entry.Frames[_selectedFrameIndex], entry.Frames[targetIndex]) = (entry.Frames[targetIndex], entry.Frames[_selectedFrameIndex]);
		ResequenceFrames(entry);
		_selectedFrameIndex = targetIndex;
		_previewFrameIndex = targetIndex;
		AfterEntryMutation(entry);
	}

	private void RemoveSelectedFrame()
	{
		var entry = GetActiveEntry();
		if (entry?.Kind != ResourceCatalogKinds.Animation)
			return;

		if (_selectedFrameIndex < 0 || _selectedFrameIndex >= entry.Frames.Count)
			return;

		var removedFrame = entry.Frames[_selectedFrameIndex];
		_frameDisplayRegionOverrides.Remove(removedFrame);
		entry.Frames.RemoveAt(_selectedFrameIndex);
		ResequenceFrames(entry);
		_selectedFrameIndex = Math.Clamp(_selectedFrameIndex, 0, entry.Frames.Count - 1);
		_previewFrameIndex = Math.Clamp(_previewFrameIndex, 0, Math.Max(entry.Frames.Count - 1, 0));
		AfterEntryMutation(entry);
	}

	private void OnSliceSettingsChanged()
	{
		RefreshSlicePanel();
		RefreshRegionEditors(GetActiveEntry());
		if (!_previewInteractionActive)
			RefreshPreview();
	}

	private void StageSlicesFromSelection()
	{
		var entry = GetActiveEntry();
		if (entry == null)
			return;

		var candidates = BuildSliceCandidates(entry, previewOnly: false);
		if (candidates.Count == 0)
		{
			_statusMessage = LocalizationService.T("ui.resource_catalog.status.no_slices");
			RefreshStatus();
			return;
		}

		var selectedIds = new HashSet<string>(candidates.Select(candidate => candidate.Id), StringComparer.OrdinalIgnoreCase);
		_staging.AddRange(candidates);
		_activeStagingId = candidates[0].Id;
		_listTabs.CurrentTab = 1;
		_statusMessage = LocalizationService.T("ui.resource_catalog.status.slices_staged", ("count", candidates.Count));
		RefreshLists();

		var stagingSelection = GetSelectedIds(EditorPane.Staging);
		foreach (var id in selectedIds)
			stagingSelection.Add(id);
		RestoreSelection(EditorPane.Staging, stagingSelection);
		RefreshUi(fullRefresh: false);
	}

	private List<ResourceCatalogEntry> BuildSliceCandidates(ResourceCatalogEntry entry, bool previewOnly)
	{
		if (!TryGetSliceSource(entry, out var sourcePath, out var size))
			return [];

		var cellWidth = (int)_sliceWidthSpin.Value;
		var cellHeight = (int)_sliceHeightSpin.Value;
		var offsetX = (int)_sliceOffsetXSpin.Value;
		var offsetY = (int)_sliceOffsetYSpin.Value;
		var spacingX = (int)_sliceSpacingXSpin.Value;
		var spacingY = (int)_sliceSpacingYSpin.Value;
		if (cellWidth <= 0 || cellHeight <= 0)
			return [];

		var baseId = NormalizeId(Path.GetFileNameWithoutExtension(sourcePath));
		var category = !string.IsNullOrEmpty(entry.Category)
			? entry.Category
			: GetCategoryFromPath(sourcePath);
		var existingIds = previewOnly ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : CombinedIds();
		var candidates = new List<ResourceCatalogEntry>();

		var row = 0;
		for (var y = offsetY; y + cellHeight <= size.Y; y += cellHeight + spacingY)
		{
			var col = 0;
			for (var x = offsetX; x + cellWidth <= size.X; x += cellWidth + spacingX)
			{
				var id = $"{baseId}_r{row}_c{col}";
				if (!previewOnly)
					id = EnsureUniqueId(id, existingIds);

				candidates.Add(new ResourceCatalogEntry
				{
					Id = id,
					Kind = ResourceCatalogKinds.Tile,
					DisplayName = $"{HumanizeId(baseId)} R{row} C{col}",
					Description = string.Empty,
					Tags = [.. entry.Tags],
					Category = category,
					SourceMode = ResourceCatalogSourceModes.Sheet,
					SourceImagePath = sourcePath,
					SourceFolderPath = null,
					Region = new ResourceCatalogRegion
					{
						X = x,
						Y = y,
						Width = cellWidth,
						Height = cellHeight,
					},
					Frames = [],
				});

				if (!previewOnly)
					existingIds.Add(id);

				col++;
			}

			row++;
		}

		return candidates;
	}

	private void ImportImage(string rawPath)
	{
		if (!TryConvertToResPath(rawPath, out var imagePath, out var error))
		{
			_statusMessage = error;
			RefreshStatus();
			return;
		}

		if (!imagePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
		{
			_statusMessage = LocalizationService.T("ui.resource_catalog.status.only_png");
			RefreshStatus();
			return;
		}

		var entry = BuildTileCandidate(imagePath);
		_staging.Add(entry);
		_activeStagingId = entry.Id;
		_listTabs.CurrentTab = 1;
		_statusMessage = LocalizationService.T("ui.resource_catalog.status.image_imported", ("id", entry.Id));
		RefreshLists();
	}

	private void ImportFolder(string rawPath)
	{
		if (!TryConvertToResPath(rawPath, out var folderPath, out var error))
		{
			_statusMessage = error;
			RefreshStatus();
			return;
		}

		var created = new List<ResourceCatalogEntry>();
		var existingIds = CombinedIds();
		var directPngs = ListDirectPngFiles(folderPath);
		if (directPngs.Count > 0)
		{
			if (LooksLikeFrameSequence(directPngs.Select(path => Path.GetFileNameWithoutExtension(path) ?? string.Empty)))
				created.Add(BuildAnimationCandidate(folderPath, directPngs, existingIds));
			else
				created.AddRange(directPngs.Select(path => BuildTileCandidate(path, existingIds)));
		}

		foreach (var subdir in ListDirectDirectories(folderPath))
		{
			var childPngs = ListDirectPngFiles(subdir);
			if (childPngs.Count == 0)
				continue;

			if (LooksLikeFrameSequence(childPngs.Select(path => Path.GetFileNameWithoutExtension(path) ?? string.Empty)))
				created.Add(BuildAnimationCandidate(subdir, childPngs, existingIds));
			else
				created.AddRange(childPngs.Select(path => BuildTileCandidate(path, existingIds)));
		}

		if (created.Count == 0)
		{
			_statusMessage = LocalizationService.T("ui.resource_catalog.status.folder_empty");
			RefreshStatus();
			return;
		}

		_staging.AddRange(created);
		_activeStagingId = created[0].Id;
		_listTabs.CurrentTab = 1;
		_statusMessage = LocalizationService.T("ui.resource_catalog.status.folder_imported", ("count", created.Count));
		RefreshLists();
	}

	private ResourceCatalogEntry BuildTileCandidate(string imagePath)
	{
		return BuildTileCandidate(imagePath, CombinedIds());
	}

	private ResourceCatalogEntry BuildTileCandidate(string imagePath, ISet<string> existingIds)
	{
		var id = EnsureUniqueId(NormalizeId(Path.GetFileNameWithoutExtension(imagePath)), existingIds);
		existingIds.Add(id);
		return new ResourceCatalogEntry
		{
			Id = id,
			Kind = ResourceCatalogKinds.Tile,
			DisplayName = HumanizeId(id),
			Description = string.Empty,
			Tags = [],
			Category = GetCategoryFromPath(imagePath),
			SourceMode = ResourceCatalogSourceModes.Single,
			SourceImagePath = imagePath,
			SourceFolderPath = null,
			Region = null,
			Frames = [],
		};
	}

	private ResourceCatalogEntry BuildAnimationCandidate(string folderPath, IReadOnlyList<string> framePaths)
	{
		return BuildAnimationCandidate(folderPath, framePaths, CombinedIds());
	}

	private ResourceCatalogEntry BuildAnimationCandidate(string folderPath, IReadOnlyList<string> framePaths, ISet<string> existingIds)
	{
		var id = EnsureUniqueId(NormalizeId(Path.GetFileName(folderPath.TrimEnd('/'))), existingIds);
		existingIds.Add(id);
		return new ResourceCatalogEntry
		{
			Id = id,
			Kind = ResourceCatalogKinds.Animation,
			DisplayName = HumanizeId(id),
			Description = string.Empty,
			Tags = [],
			Category = GetCategoryFromPath(folderPath),
			SourceMode = ResourceCatalogSourceModes.Frames,
			SourceFolderPath = folderPath,
			Frames = framePaths
				.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
				.Select((path, index) => new ResourceCatalogFrame
				{
					ImagePath = path,
					Region = null,
					Order = index,
				})
				.ToList(),
			Fps = 10f,
		};
	}

	private void SaveCatalog()
	{
		var validationErrors = CollectCatalogValidationErrors();
		if (validationErrors.Count > 0)
		{
			_statusMessage = LocalizationService.T(
				"ui.resource_catalog.status.validation_failed",
				("count", validationErrors.Count));
			RefreshStatus();
			RefreshValidation(GetActiveEntry());
			return;
		}

		try
		{
			ResourceCatalogStore.Save(_catalog);
			_catalogDirty = false;
			_dirtyCatalogIds.Clear();
			_statusMessage = LocalizationService.T("ui.resource_catalog.status.saved", ("count", _catalog.Entries.Count));
			RefreshLists();
		}
		catch (Exception ex)
		{
			_statusMessage = ex.Message;
			RefreshStatus();
		}
	}

	private void CloseEditor()
	{
		if (_catalogDirty)
		{
			SaveCatalog();
			if (_catalogDirty)
				return;
		}

		GetTree().Quit();
	}

	private List<string> CollectCatalogValidationErrors()
	{
		var errors = new List<string>();
		foreach (var entry in _catalog.Entries)
			errors.AddRange(ValidateEntry(entry));
		return errors;
	}

	private List<string> ValidateEntry(ResourceCatalogEntry entry)
	{
		var issues = new List<string>();
		if (string.IsNullOrWhiteSpace(entry.Id))
			issues.Add(LocalizationService.T("ui.resource_catalog.validation.id_required"));

		if (HasDuplicateId(entry))
			issues.Add(LocalizationService.T("ui.resource_catalog.validation.duplicate_id"));

		if (entry.Kind == ResourceCatalogKinds.Tile)
		{
			if (string.IsNullOrWhiteSpace(entry.SourceImagePath))
				issues.Add(LocalizationService.T("ui.resource_catalog.validation.image_required"));
			else if (!Godot.FileAccess.FileExists(entry.SourceImagePath))
				issues.Add(LocalizationService.T("ui.resource_catalog.validation.image_missing"));

			if (entry.Region != null)
			{
				if (entry.Region.Width <= 0 || entry.Region.Height <= 0)
					issues.Add(LocalizationService.T("ui.resource_catalog.validation.region_invalid"));
				else if (TryGetTextureSize(entry.SourceImagePath!, out var imageSize)
					&& (entry.Region.X < 0
						|| entry.Region.Y < 0
						|| entry.Region.X + entry.Region.Width > imageSize.X
						|| entry.Region.Y + entry.Region.Height > imageSize.Y))
					issues.Add(LocalizationService.T("ui.resource_catalog.validation.region_bounds"));
			}
		}
		else
		{
			if (entry.Frames.Count == 0)
				issues.Add(LocalizationService.T("ui.resource_catalog.validation.frames_required"));
			if ((entry.Fps ?? 0) <= 0)
				issues.Add(LocalizationService.T("ui.resource_catalog.validation.fps_invalid"));

			for (var i = 0; i < entry.Frames.Count; i++)
			{
				var frame = entry.Frames[i];
				if (string.IsNullOrWhiteSpace(frame.ImagePath) || !Godot.FileAccess.FileExists(frame.ImagePath))
				{
					issues.Add(LocalizationService.T("ui.resource_catalog.validation.frame_missing", ("index", i + 1)));
					continue;
				}

				if (frame.Region != null
					&& TryGetTextureSize(frame.ImagePath, out var size)
					&& (frame.Region.X < 0
						|| frame.Region.Y < 0
						|| frame.Region.Width <= 0
						|| frame.Region.Height <= 0
						|| frame.Region.X + frame.Region.Width > size.X
						|| frame.Region.Y + frame.Region.Height > size.Y))
					issues.Add(LocalizationService.T("ui.resource_catalog.validation.frame_region_bounds", ("index", i + 1)));
			}
		}

		return issues.Distinct(StringComparer.Ordinal).ToList();
	}

	private bool HasDuplicateId(ResourceCatalogEntry target)
	{
		var duplicates = _catalog.Entries.Count(entry => string.Equals(entry.Id, target.Id, StringComparison.OrdinalIgnoreCase));
		duplicates += _staging.Count(entry => string.Equals(entry.Id, target.Id, StringComparison.OrdinalIgnoreCase));
		return duplicates > 1;
	}

	private bool MatchesFilters(ResourceCatalogEntry entry)
	{
		var kindFilter = _kindFilter.GetItemId(_kindFilter.Selected);
		if (kindFilter == 1 && entry.Kind != ResourceCatalogKinds.Tile)
			return false;
		if (kindFilter == 2 && entry.Kind != ResourceCatalogKinds.Animation)
			return false;

		var search = _searchEdit.Text.Trim();
		if (!string.IsNullOrEmpty(search))
		{
			var haystack = string.Join(
				" ",
				entry.Id,
				entry.DisplayName,
				entry.Description,
				entry.Category,
				string.Join(' ', entry.Tags),
				entry.SourceImagePath ?? string.Empty,
				entry.SourceFolderPath ?? string.Empty);
			if (!haystack.Contains(search, StringComparison.OrdinalIgnoreCase))
				return false;
		}

		var tagTokens = ParseTags(_tagFilterEdit.Text);
		if (tagTokens.Count == 0)
			return true;

		return tagTokens.All(token => entry.Tags.Any(tag => string.Equals(tag, token, StringComparison.OrdinalIgnoreCase)));
	}

	private string BuildListLabel(ResourceCatalogEntry entry, bool inCatalog)
	{
		var marker = inCatalog && _dirtyCatalogIds.Contains(entry.Id) ? "*" : " ";
		var duplicate = HasDuplicateId(entry) ? "!" : " ";
		var kind = entry.Kind == ResourceCatalogKinds.Animation ? "ANIM" : "TILE";
		return $"{marker}{duplicate} [{kind}] {entry.Id}";
	}

	private string BuildFrameLabel(ResourceCatalogFrame frame)
	{
		var region = frame.Region == null
			? "-"
			: $"{frame.Region.X},{frame.Region.Y},{frame.Region.Width},{frame.Region.Height}";
		return $"{frame.Order + 1:00} | {Path.GetFileName(frame.ImagePath)} | {region}";
	}

	private HashSet<string> CombinedIds(IEnumerable<ResourceCatalogEntry>? except = null)
	{
		var excluded = except == null ? new HashSet<ResourceCatalogEntry>() : new HashSet<ResourceCatalogEntry>(except);
		var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var entry in _catalog.Entries)
		{
			if (!excluded.Contains(entry))
				ids.Add(entry.Id);
		}

		foreach (var entry in _staging)
		{
			if (!excluded.Contains(entry))
				ids.Add(entry.Id);
		}

		return ids;
	}

	private List<ResourceCatalogEntry> GetSelectedEntries(EditorPane pane)
	{
		var selectedIds = GetSelectedIds(pane);
		var source = pane == EditorPane.Catalog ? _catalog.Entries : _staging;
		return source.Where(entry => selectedIds.Contains(entry.Id)).ToList();
	}

	private List<ResourceCatalogEntry> GetSelectedEntriesInVisibleOrder(EditorPane pane)
	{
		var selectedIds = GetSelectedIds(pane);
		var orderedIds = GetVisibleIds(pane).Where(selectedIds.Contains);
		var source = pane == EditorPane.Catalog ? _catalog.Entries : _staging;
		return orderedIds
			.Select(id => source.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase)))
			.Where(entry => entry != null)
			.Cast<ResourceCatalogEntry>()
			.ToList();
	}

	private HashSet<string> GetSelectedIds(EditorPane pane)
	{
		var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var list = GetListControl(pane);
		var visibleIds = GetVisibleIds(pane);
		foreach (var rawIndex in list.GetSelectedItems())
		{
			var index = (int)rawIndex;
			if (index >= 0 && index < visibleIds.Count)
				result.Add(visibleIds[index]);
		}

		return result;
	}

	private ResourceCatalogEntry? GetActiveEntry()
	{
		var pane = GetCurrentPane();
		var activeId = GetActiveId(pane);
		var source = pane == EditorPane.Catalog ? _catalog.Entries : _staging;
		if (string.IsNullOrEmpty(activeId))
			return source.FirstOrDefault();

		return source.FirstOrDefault(entry => string.Equals(entry.Id, activeId, StringComparison.OrdinalIgnoreCase));
	}

	private EditorPane GetCurrentPane() => _listTabs.CurrentTab == 0 ? EditorPane.Catalog : EditorPane.Staging;

	private ItemList GetListControl(EditorPane pane) => pane == EditorPane.Catalog ? _catalogList : _stagingList;

	private List<string> GetVisibleIds(EditorPane pane) => pane == EditorPane.Catalog ? _visibleCatalogIds : _visibleStagingIds;

	private string? GetActiveId(EditorPane pane) => pane == EditorPane.Catalog ? _activeCatalogId : _activeStagingId;

	private void SetActiveId(EditorPane pane, string? id)
	{
		if (pane == EditorPane.Catalog)
			_activeCatalogId = id;
		else
			_activeStagingId = id;
	}

	private Texture2D? GetTexture(string path)
	{
		if (_textureCache.TryGetValue(path, out var cached))
			return cached;

		var loaded = GD.Load<Texture2D>(path);
		_textureCache[path] = loaded;
		if (loaded != null)
			_textureSizeCache[path] = new Vector2I((int)loaded.GetSize().X, (int)loaded.GetSize().Y);
		return loaded;
	}

	private bool TryGetTextureSize(string path, out Vector2I size)
	{
		if (_textureSizeCache.TryGetValue(path, out size))
			return true;

		var texture = GetTexture(path);
		if (texture == null)
		{
			size = Vector2I.Zero;
			return false;
		}

		var rawSize = texture.GetSize();
		size = new Vector2I((int)rawSize.X, (int)rawSize.Y);
		_textureSizeCache[path] = size;
		return true;
	}

	private bool TryGetSliceSource(ResourceCatalogEntry? entry, out string path, out Vector2I size)
	{
		path = string.Empty;
		size = Vector2I.Zero;
		if (entry?.Kind != ResourceCatalogKinds.Tile || string.IsNullOrEmpty(entry.SourceImagePath))
			return false;

		path = entry.SourceImagePath;
		return TryGetTextureSize(path, out size);
	}

	private bool TryGetSlicePreviewSize(out Vector2I size)
	{
		var entry = GetActiveEntry();
		if (entry == null)
		{
			size = Vector2I.Zero;
			return false;
		}

		if (entry.Kind == ResourceCatalogKinds.Tile && !string.IsNullOrEmpty(entry.SourceImagePath))
			return TryGetTextureSize(entry.SourceImagePath, out size);

		if (entry.Kind == ResourceCatalogKinds.Animation && entry.Frames.Count > 0)
		{
			var frameIndex = _selectedFrameIndex >= 0
				? _selectedFrameIndex
				: Math.Clamp(_previewFrameIndex, 0, entry.Frames.Count - 1);
			var frame = entry.Frames[frameIndex];
			if (!string.IsNullOrEmpty(frame.ImagePath))
				return TryGetTextureSize(frame.ImagePath, out size);
		}

		size = Vector2I.Zero;
		return false;
	}

	private void RefreshSliceInputRanges(Vector2I? size)
	{
		var hasSize = size.HasValue && size.Value.X > 0 && size.Value.Y > 0;
		var bounds = size ?? Vector2I.Zero;

		ConfigureSliceInputPair(_sliceWidthSlider, _sliceWidthSpin, 1, hasSize ? Math.Max(bounds.X, 1) : 4096, hasSize);
		ConfigureSliceInputPair(_sliceHeightSlider, _sliceHeightSpin, 1, hasSize ? Math.Max(bounds.Y, 1) : 4096, hasSize);
		ConfigureSliceInputPair(_sliceOffsetXSlider, _sliceOffsetXSpin, 0, hasSize ? Math.Max(bounds.X - 1, 0) : 4096, hasSize);
		ConfigureSliceInputPair(_sliceOffsetYSlider, _sliceOffsetYSpin, 0, hasSize ? Math.Max(bounds.Y - 1, 0) : 4096, hasSize);
		ConfigureSliceInputPair(_sliceSpacingXSlider, _sliceSpacingXSpin, 0, hasSize ? Math.Max(bounds.X, 0) : 1024, hasSize);
		ConfigureSliceInputPair(_sliceSpacingYSlider, _sliceSpacingYSpin, 0, hasSize ? Math.Max(bounds.Y, 0) : 1024, hasSize);
	}

	private GridOverlay BuildGridOverlay() => new()
	{
		CellWidth = (int)_sliceWidthSpin.Value,
		CellHeight = (int)_sliceHeightSpin.Value,
		OffsetX = (int)_sliceOffsetXSpin.Value,
		OffsetY = (int)_sliceOffsetYSpin.Value,
		SpacingX = (int)_sliceSpacingXSpin.Value,
		SpacingY = (int)_sliceSpacingYSpin.Value,
	};

	private void ResetSliceInputs()
	{
		SetSliceInputPairValue(_sliceWidthSlider, _sliceWidthSpin, 128);
		SetSliceInputPairValue(_sliceHeightSlider, _sliceHeightSpin, 64);
		SetSliceInputPairValue(_sliceOffsetXSlider, _sliceOffsetXSpin, 0);
		SetSliceInputPairValue(_sliceOffsetYSlider, _sliceOffsetYSpin, 0);
		SetSliceInputPairValue(_sliceSpacingXSlider, _sliceSpacingXSpin, 0);
		SetSliceInputPairValue(_sliceSpacingYSlider, _sliceSpacingYSpin, 0);
	}

	private static List<string> ParseTags(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
			return [];

		return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static List<string> MergeTags(IEnumerable<ResourceCatalogEntry> entries)
	{
		var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var entry in entries)
		{
			foreach (var tag in entry.Tags)
				tags.Add(tag);
		}

		return tags.ToList();
	}

	private static string NormalizeId(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
			return string.Empty;

		var chars = raw.Trim().ToLowerInvariant()
			.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
			.ToArray();
		var normalized = new string(chars);
		while (normalized.Contains("__", StringComparison.Ordinal))
			normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
		return normalized.Trim('_');
	}

	private static string EnsureUniqueId(string baseId, ISet<string> existingIds)
	{
		var normalized = string.IsNullOrWhiteSpace(baseId) ? "resource" : NormalizeId(baseId);
		var candidate = normalized;
		var suffix = 1;
		while (existingIds.Contains(candidate))
		{
			suffix++;
			candidate = $"{normalized}_{suffix}";
		}

		return candidate;
	}

	private static string HumanizeId(string rawId)
	{
		if (string.IsNullOrWhiteSpace(rawId))
			return string.Empty;

		var parts = NormalizeId(rawId).Split('_', StringSplitOptions.RemoveEmptyEntries);
		return string.Join(' ', parts.Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
	}

	private static string GetCategoryFromPath(string resPath)
	{
		var directory = Path.GetDirectoryName(resPath.Replace("res://", string.Empty).Replace('/', Path.DirectorySeparatorChar));
		return string.IsNullOrEmpty(directory) ? "root" : Path.GetFileName(directory);
	}

	private static List<string> ListDirectPngFiles(string dirPath)
	{
		var results = new List<string>();
		var dir = DirAccess.Open(dirPath);
		if (dir == null)
			return results;

		dir.ListDirBegin();
		while (true)
		{
			var name = dir.GetNext();
			if (string.IsNullOrEmpty(name))
				break;
			if (dir.CurrentIsDir() || !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
				continue;
			results.Add($"{dirPath}/{name}");
		}
		dir.ListDirEnd();
		results.Sort(StringComparer.OrdinalIgnoreCase);
		return results;
	}

	private static List<string> ListDirectDirectories(string dirPath)
	{
		var results = new List<string>();
		var dir = DirAccess.Open(dirPath);
		if (dir == null)
			return results;

		dir.ListDirBegin();
		while (true)
		{
			var name = dir.GetNext();
			if (string.IsNullOrEmpty(name))
				break;
			if (!dir.CurrentIsDir() || name == "." || name == "..")
				continue;
			results.Add($"{dirPath}/{name}");
		}
		dir.ListDirEnd();
		results.Sort(StringComparer.OrdinalIgnoreCase);
		return results;
	}

	private static bool LooksLikeFrameSequence(IEnumerable<string> names)
	{
		var items = names.Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
		if (items.Count <= 1)
			return false;

		string? commonPrefix = null;
		foreach (var name in items)
		{
			if (name.All(char.IsDigit))
				continue;

			var match = NumericTailPattern.Match(name);
			if (!match.Success)
				return false;

			var prefix = match.Groups["prefix"].Value;
			if (commonPrefix == null)
				commonPrefix = prefix;
			else if (!string.Equals(commonPrefix, prefix, StringComparison.OrdinalIgnoreCase))
				return false;
		}

		return true;
	}

	private static void ResequenceFrames(ResourceCatalogEntry entry)
	{
		for (var i = 0; i < entry.Frames.Count; i++)
			entry.Frames[i].Order = i;
	}

	private bool TryConvertToResPath(string rawPath, out string resPath, out string error)
	{
		error = string.Empty;
		resPath = string.Empty;

		if (string.IsNullOrWhiteSpace(rawPath))
		{
			error = LocalizationService.T("ui.resource_catalog.status.invalid_path");
			return false;
		}

		if (rawPath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
		{
			resPath = rawPath.Replace('\\', '/');
			return true;
		}

		var projectRoot = ProjectSettings.GlobalizePath("res://").Replace('\\', '/').TrimEnd('/');
		var absolutePath = Path.GetFullPath(rawPath).Replace('\\', '/');
		if (!absolutePath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
		{
			error = LocalizationService.T("ui.resource_catalog.status.project_only");
			return false;
		}

		var relative = absolutePath[projectRoot.Length..].TrimStart('/');
		resPath = $"res://{relative}";
		return true;
	}
}
