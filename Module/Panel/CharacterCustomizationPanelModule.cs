using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 独立的「捏脸」面板，是 <see cref="IModalInputLayer"/> 平级模态。
/// 通常由 <see cref="MiniRPG.Module.CharacterCreationModule"/> 弹出（点击主面板里的「捏脸」按钮）。
/// 用户在面板里调整 8 类部件 + 4 个颜色，点「应用」确认；点「取消」/Esc 弃改。
/// 状态管理：内部维护一个 <see cref="FaceCustomizationData"/> 工作副本，确认时通过事件传出。
/// </summary>
public sealed class CharacterCustomizationPanelModule : IModalInputLayer
{
	private const float PreviewAnchorX = 0.5f;
	private const float PreviewAnchorY = 0.5f;
	private static readonly StringName PressedSignalName = "pressed";

	private readonly PanelContainer _panel;
	private readonly Label _titleLabel;
	private readonly Label _categoryLabel;
	private readonly VBoxContainer _categoryList;
	private readonly Label _previewLabel;
	private readonly SubViewportContainer _previewViewportContainer;
	private readonly SubViewport _previewViewport;
	private readonly Node2D _previewRoot;
	private readonly Sprite2D _previewSprite;
	private readonly Label _entriesLabel;
	private readonly ScrollContainer _entriesScroll;
	private readonly VBoxContainer _entriesList;
	private readonly VBoxContainer _colorsPanel;
	private readonly Label _skinLabel;
	private readonly ColorPickerButton _skinPicker;
	private readonly Label _hairLabel;
	private readonly ColorPickerButton _hairPicker;
	private readonly Label _eyeLabel;
	private readonly ColorPickerButton _eyePicker;
	private readonly Label _clothesLabel;
	private readonly ColorPickerButton _clothesPicker;
	private readonly Button _randomizeBtn;
	private readonly Button _resetBtn;
	private readonly Button _cancelBtn;
	private readonly Button _applyBtn;

	private readonly PortraitComposer _portraitComposer = new();
	private readonly Random _random = new();
	private readonly Dictionary<FacePartCategory, Button> _categoryButtons = new();
	private Button? _colorsCategoryBtn;

	private FaceCustomizationData _workingFace = FaceCustomizationData.CreateDefault();
	private FaceCustomizationData _initialFace = FaceCustomizationData.CreateDefault();
	private FacePartCategory? _selectedCategory;
	private bool _colorsTabSelected;
	private bool _suppressColorPickerEvents;

	public event Action<FaceCustomizationData>? Applied;
	public event Action? Canceled;

	public CharacterCustomizationPanelModule(PanelContainer panel)
	{
		_panel = panel;
		_titleLabel = panel.GetNode<Label>("Margin/VBox/Title");
		_categoryLabel = panel.GetNode<Label>("Margin/VBox/Body/CategoryColumn/CategoryLabel");
		_categoryList = panel.GetNode<VBoxContainer>("Margin/VBox/Body/CategoryColumn/CategoryList");
		_previewLabel = panel.GetNode<Label>("Margin/VBox/Body/PreviewColumn/PreviewLabel");
		_previewViewportContainer = panel.GetNode<SubViewportContainer>("Margin/VBox/Body/PreviewColumn/PreviewFrame/PreviewViewport");
		_previewViewport = _previewViewportContainer.GetNode<SubViewport>("PreviewViewport");
		_previewRoot = _previewViewport.GetNode<Node2D>("PreviewRoot");
		_entriesLabel = panel.GetNode<Label>("Margin/VBox/Body/EntriesColumn/EntriesLabel");
		_entriesScroll = panel.GetNode<ScrollContainer>("Margin/VBox/Body/EntriesColumn/EntriesScroll");
		_entriesList = panel.GetNode<VBoxContainer>("Margin/VBox/Body/EntriesColumn/EntriesScroll/EntriesList");
		_colorsPanel = panel.GetNode<VBoxContainer>("Margin/VBox/Body/EntriesColumn/ColorsPanel");
		_skinLabel = panel.GetNode<Label>("Margin/VBox/Body/EntriesColumn/ColorsPanel/SkinRow/SkinLabel");
		_skinPicker = panel.GetNode<ColorPickerButton>("Margin/VBox/Body/EntriesColumn/ColorsPanel/SkinRow/SkinPicker");
		_hairLabel = panel.GetNode<Label>("Margin/VBox/Body/EntriesColumn/ColorsPanel/HairRow/HairLabel");
		_hairPicker = panel.GetNode<ColorPickerButton>("Margin/VBox/Body/EntriesColumn/ColorsPanel/HairRow/HairPicker");
		_eyeLabel = panel.GetNode<Label>("Margin/VBox/Body/EntriesColumn/ColorsPanel/EyeRow/EyeLabel");
		_eyePicker = panel.GetNode<ColorPickerButton>("Margin/VBox/Body/EntriesColumn/ColorsPanel/EyeRow/EyePicker");
		_clothesLabel = panel.GetNode<Label>("Margin/VBox/Body/EntriesColumn/ColorsPanel/ClothesRow/ClothesLabel");
		_clothesPicker = panel.GetNode<ColorPickerButton>("Margin/VBox/Body/EntriesColumn/ColorsPanel/ClothesRow/ClothesPicker");
		_randomizeBtn = panel.GetNode<Button>("Margin/VBox/Actions/RandomizeBtn");
		_resetBtn = panel.GetNode<Button>("Margin/VBox/Actions/ResetBtn");
		_cancelBtn = panel.GetNode<Button>("Margin/VBox/Actions/CancelBtn");
		_applyBtn = panel.GetNode<Button>("Margin/VBox/Actions/ApplyBtn");

		_previewViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		_previewViewportContainer.Resized += UpdatePreviewLayout;
		_previewSprite = new Sprite2D
		{
			Name = "PortraitPreview",
			Centered = true,
			TextureFilter = CanvasItem.TextureFilterEnum.Linear,
		};
		_previewRoot.AddChild(_previewSprite);

		BuildCategoryButtons();
		WireColorPickers();

		_randomizeBtn.Pressed += RandomizeFace;
		_resetBtn.Pressed += ResetToInitial;
		_cancelBtn.Pressed += () => Canceled?.Invoke();
		_applyBtn.Pressed += () => Applied?.Invoke(_workingFace.Clone());

		RefreshTexts();
	}

	public bool Visible
	{
		get => _panel.Visible;
		private set => _panel.Visible = value;
	}

	public void Open(FaceCustomizationData initial)
	{
		_initialFace = initial.Clone();
		_workingFace = initial.Clone();
		RefreshTexts();
		SyncColorPickersFromWorking();
		SelectCategory(FacePartCategory.HeadShape);
		Visible = true;
		_panel.MoveToFront();
		UpdatePreviewLayout();
		RefreshPortraitPreview();
		_applyBtn.GrabFocus();
	}

	public void Close()
	{
		Visible = false;
	}

	public void RefreshTexts()
	{
		_titleLabel.Text = LocalizationService.T("ui.face_edit.title");
		_categoryLabel.Text = LocalizationService.T("ui.face_edit.category_header");
		_previewLabel.Text = LocalizationService.T("ui.face_edit.preview_header");
		_entriesLabel.Text = LocalizationService.T("ui.face_edit.entries_header");
		_skinLabel.Text = LocalizationService.T("ui.face_edit.color.skin");
		_hairLabel.Text = LocalizationService.T("ui.face_edit.color.hair");
		_eyeLabel.Text = LocalizationService.T("ui.face_edit.color.eye");
		_clothesLabel.Text = LocalizationService.T("ui.face_edit.color.clothes");
		_randomizeBtn.Text = LocalizationService.T("ui.face_edit.randomize");
		_resetBtn.Text = LocalizationService.T("ui.face_edit.reset");
		_cancelBtn.Text = LocalizationService.T("ui.face_edit.cancel");
		_applyBtn.Text = LocalizationService.T("ui.face_edit.apply");

		foreach (var (category, button) in _categoryButtons)
			button.Text = LocalizationService.T($"ui.face_edit.category.{CategoryKey(category)}");

		if (_colorsCategoryBtn != null)
			_colorsCategoryBtn.Text = LocalizationService.T("ui.face_edit.category.colors");
	}

	public bool HandleKeyInput(InputEventKey key)
	{
		var focusOwner = _panel.GetViewport().GuiGetFocusOwner();
		var blockConfirm = focusOwner is Button or OptionButton or ColorPickerButton or LineEdit;
		var decision = ModalInputLogic.HandleKey(key, blockConfirm);
		if (!decision.Handled)
			return false;

		if (decision.CancelRequested)
			Canceled?.Invoke();
		else if (decision.ConfirmRequested)
			Applied?.Invoke(_workingFace.Clone());
		return true;
	}

	private void BuildCategoryButtons()
	{
		foreach (FacePartCategory category in Enum.GetValues<FacePartCategory>())
		{
			var btn = new Button
			{
				Name = $"Category_{category}",
				ToggleMode = true,
				Text = $"ui.face_edit.category.{CategoryKey(category)}",
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			var captured = category;
			btn.Pressed += () => SelectCategory(captured);
			_categoryButtons[category] = btn;
			_categoryList.AddChild(btn);
		}

		_colorsCategoryBtn = new Button
		{
			Name = "Category_Colors",
			ToggleMode = true,
			Text = LocalizationService.T("ui.face_edit.category.colors"),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		_colorsCategoryBtn.Pressed += SelectColorsTab;
		_categoryList.AddChild(_colorsCategoryBtn);
	}

	private void WireColorPickers()
	{
		_skinPicker.ColorChanged += color => OnColorChanged(color, isSkin: true, isHair: false, isEye: false, isClothes: false);
		_hairPicker.ColorChanged += color => OnColorChanged(color, isSkin: false, isHair: true, isEye: false, isClothes: false);
		_eyePicker.ColorChanged += color => OnColorChanged(color, isSkin: false, isHair: false, isEye: true, isClothes: false);
		_clothesPicker.ColorChanged += color => OnColorChanged(color, isSkin: false, isHair: false, isEye: false, isClothes: true);
	}

	private void OnColorChanged(Color color, bool isSkin, bool isHair, bool isEye, bool isClothes)
	{
		if (_suppressColorPickerEvents)
			return;

		var faceColor = FromGodotColor(color);
		_workingFace = _workingFace.Clone();
		if (isSkin)
			_workingFace.SkinTone = faceColor;
		else if (isHair)
			_workingFace.HairColor = faceColor;
		else if (isEye)
			_workingFace.EyeColor = faceColor;
		else if (isClothes)
			_workingFace.ClothesColor = faceColor;
		RefreshPortraitPreview();
	}

	private void SyncColorPickersFromWorking()
	{
		_suppressColorPickerEvents = true;
		_skinPicker.Color = ToGodotColor(_workingFace.SkinTone);
		_hairPicker.Color = ToGodotColor(_workingFace.HairColor);
		_eyePicker.Color = ToGodotColor(_workingFace.EyeColor);
		_clothesPicker.Color = ToGodotColor(_workingFace.ClothesColor);
		_suppressColorPickerEvents = false;
	}

	private void SelectCategory(FacePartCategory category)
	{
		_selectedCategory = category;
		_colorsTabSelected = false;
		UpdateCategoryToggleStates();
		_colorsPanel.Visible = false;
		_entriesScroll.Visible = true;
		PopulateEntriesList(category);
	}

	private void SelectColorsTab()
	{
		_selectedCategory = null;
		_colorsTabSelected = true;
		UpdateCategoryToggleStates();
		_colorsPanel.Visible = true;
		_entriesScroll.Visible = false;
		SyncColorPickersFromWorking();
	}

	private void UpdateCategoryToggleStates()
	{
		foreach (var (category, button) in _categoryButtons)
			button.ButtonPressed = !_colorsTabSelected && category == _selectedCategory;

		var colorsBtn = _categoryList.GetNodeOrNull<Button>("Category_Colors");
		if (colorsBtn != null)
			colorsBtn.ButtonPressed = _colorsTabSelected;
	}

	private void PopulateEntriesList(FacePartCategory category)
	{
		ClearChildren(_entriesList);
		var entries = FacePartCatalog.GetEntries(category);
		var currentId = GetCurrentPartId(category);
		foreach (var entry in entries)
		{
			var btn = new Button
			{
				Name = $"Entry_{entry.Id}",
				ToggleMode = true,
				Text = LocalizationService.TOrFallback(
					$"data.face_part.{CategoryKey(category)}.{entry.Id}",
					entry.DisplayName),
				ButtonPressed = string.Equals(entry.Id, currentId, StringComparison.Ordinal),
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			var capturedCategory = category;
			var capturedId = entry.Id;
			btn.Pressed += () => SelectEntry(capturedCategory, capturedId);
			_entriesList.AddChild(btn);
		}
	}

	private void SelectEntry(FacePartCategory category, string id)
	{
		_workingFace = _workingFace.Clone();
		switch (category)
		{
			case FacePartCategory.HeadShape: _workingFace.HeadShapeId = id; break;
			case FacePartCategory.Eyes: _workingFace.EyesId = id; break;
			case FacePartCategory.Eyebrows: _workingFace.EyebrowsId = id; break;
			case FacePartCategory.Nose: _workingFace.NoseId = id; break;
			case FacePartCategory.Mouth: _workingFace.MouthId = id; break;
			case FacePartCategory.Ears: _workingFace.EarsId = id; break;
			case FacePartCategory.Hair: _workingFace.HairId = id; break;
			case FacePartCategory.Beard:
				_workingFace.BeardId = string.Equals(id, "clean_shaven", StringComparison.Ordinal) ? null : id;
				break;
			default:
				return;
		}

		foreach (var child in _entriesList.GetChildren())
		{
			if (child is Button btn)
				btn.ButtonPressed = btn.Name == $"Entry_{id}";
		}
		RefreshPortraitPreview();
	}

	private string GetCurrentPartId(FacePartCategory category) => category switch
	{
		FacePartCategory.HeadShape => _workingFace.HeadShapeId,
		FacePartCategory.Eyes => _workingFace.EyesId,
		FacePartCategory.Eyebrows => _workingFace.EyebrowsId,
		FacePartCategory.Nose => _workingFace.NoseId,
		FacePartCategory.Mouth => _workingFace.MouthId,
		FacePartCategory.Ears => _workingFace.EarsId,
		FacePartCategory.Hair => _workingFace.HairId,
		FacePartCategory.Beard => _workingFace.BeardId ?? "clean_shaven",
		_ => string.Empty,
	};

	private void RandomizeFace()
	{
		_workingFace = new FaceCustomizationData
		{
			HeadShapeId = PickRandomEntryId(FacePartCategory.HeadShape),
			EyesId = PickRandomEntryId(FacePartCategory.Eyes),
			EyebrowsId = PickRandomEntryId(FacePartCategory.Eyebrows),
			NoseId = PickRandomEntryId(FacePartCategory.Nose),
			MouthId = PickRandomEntryId(FacePartCategory.Mouth),
			EarsId = PickRandomEntryId(FacePartCategory.Ears),
			HairId = PickRandomEntryId(FacePartCategory.Hair),
			BeardId = _random.Next(2) == 0 ? null : PickRandomEntryId(FacePartCategory.Beard),
			SkinTone = PickRandomSkinTone(),
			HairColor = PickRandomHairColor(),
			EyeColor = PickRandomEyeColor(),
			ClothesColor = PickRandomClothesColor(),
			MapSpriteTemplateId = _workingFace.MapSpriteTemplateId,
		};
		SyncColorPickersFromWorking();
		if (_selectedCategory.HasValue)
			PopulateEntriesList(_selectedCategory.Value);
		RefreshPortraitPreview();
	}

	private void ResetToInitial()
	{
		_workingFace = _initialFace.Clone();
		SyncColorPickersFromWorking();
		if (_selectedCategory.HasValue)
			PopulateEntriesList(_selectedCategory.Value);
		RefreshPortraitPreview();
	}

	private string PickRandomEntryId(FacePartCategory category)
	{
		var entries = FacePartCatalog.GetEntries(category);
		return entries.Count == 0 ? string.Empty : entries[_random.Next(entries.Count)].Id;
	}

	private FaceColorRgba PickRandomSkinTone()
	{
		var tones = new[]
		{
			FaceColorRgba.Rgb(0.96f, 0.82f, 0.71f),
			FaceColorRgba.Rgb(0.88f, 0.71f, 0.55f),
			FaceColorRgba.Rgb(0.74f, 0.55f, 0.40f),
			FaceColorRgba.Rgb(0.55f, 0.38f, 0.27f),
			FaceColorRgba.Rgb(0.36f, 0.24f, 0.18f),
			FaceColorRgba.Rgb(0.84f, 0.93f, 0.78f),
		};
		return tones[_random.Next(tones.Length)];
	}

	private FaceColorRgba PickRandomHairColor()
	{
		var colors = new[]
		{
			FaceColorRgba.Rgb(0.10f, 0.07f, 0.05f),
			FaceColorRgba.Rgb(0.30f, 0.20f, 0.12f),
			FaceColorRgba.Rgb(0.55f, 0.40f, 0.22f),
			FaceColorRgba.Rgb(0.78f, 0.62f, 0.30f),
			FaceColorRgba.Rgb(0.92f, 0.86f, 0.66f),
			FaceColorRgba.Rgb(0.78f, 0.30f, 0.20f),
			FaceColorRgba.Rgb(0.55f, 0.40f, 0.78f),
			FaceColorRgba.Rgb(0.92f, 0.92f, 0.88f),
		};
		return colors[_random.Next(colors.Length)];
	}

	private FaceColorRgba PickRandomEyeColor()
	{
		var colors = new[]
		{
			FaceColorRgba.Rgb(0.18f, 0.42f, 0.65f),
			FaceColorRgba.Rgb(0.22f, 0.55f, 0.30f),
			FaceColorRgba.Rgb(0.40f, 0.28f, 0.16f),
			FaceColorRgba.Rgb(0.65f, 0.50f, 0.20f),
			FaceColorRgba.Rgb(0.55f, 0.55f, 0.55f),
			FaceColorRgba.Rgb(0.78f, 0.20f, 0.20f),
		};
		return colors[_random.Next(colors.Length)];
	}

	private FaceColorRgba PickRandomClothesColor()
	{
		var colors = new[]
		{
			FaceColorRgba.Rgb(0.46f, 0.32f, 0.22f),
			FaceColorRgba.Rgb(0.20f, 0.32f, 0.55f),
			FaceColorRgba.Rgb(0.55f, 0.25f, 0.20f),
			FaceColorRgba.Rgb(0.30f, 0.45f, 0.32f),
			FaceColorRgba.Rgb(0.22f, 0.22f, 0.26f),
			FaceColorRgba.Rgb(0.85f, 0.78f, 0.62f),
		};
		return colors[_random.Next(colors.Length)];
	}

	private void RefreshPortraitPreview()
	{
		_previewSprite.Texture = _portraitComposer.Compose(_workingFace);
	}

	private void UpdatePreviewLayout()
	{
		var size = _previewViewport.Size;
		if (size.X <= 0 || size.Y <= 0)
			return;
		_previewSprite.Position = new Vector2(size.X * PreviewAnchorX, size.Y * PreviewAnchorY);
		var available = MathF.Min(size.X * 0.85f, size.Y * 0.85f);
		var scale = available / PortraitComposer.CanvasWidth;
		if (scale < 0.4f)
			scale = 0.4f;
		_previewSprite.Scale = new Vector2(scale, scale);
	}

	private static void ClearChildren(Node parent)
	{
		foreach (var child in parent.GetChildren())
			child.QueueFree();
	}

	private static string CategoryKey(FacePartCategory category) => category switch
	{
		FacePartCategory.HeadShape => "head_shape",
		FacePartCategory.Eyes => "eyes",
		FacePartCategory.Eyebrows => "eyebrows",
		FacePartCategory.Nose => "nose",
		FacePartCategory.Mouth => "mouth",
		FacePartCategory.Ears => "ears",
		FacePartCategory.Hair => "hair",
		FacePartCategory.Beard => "beard",
		_ => category.ToString().ToLowerInvariant(),
	};

	private static Color ToGodotColor(FaceColorRgba c) => new(c.R, c.G, c.B, c.A);

	private static FaceColorRgba FromGodotColor(Color c) => FaceColorRgba.Rgb(c.R, c.G, c.B, c.A);
}
