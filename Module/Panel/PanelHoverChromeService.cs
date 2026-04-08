using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using MiniRPG.Core.Config;

namespace MiniRPG.Module.Panel;

public readonly record struct PanelHoverChromeRegistration(
	string PanelId,
	PanelContainer Panel,
	Control[] TitleDragHandles,
	Action CloseAction
);

public sealed class PanelHoverChromeService
{
	private const float TopBarHeight = 28f;
	private const float TopBarGap = 4f;
	private const float PopupGap = 8f;
	private const float ViewportMargin = 8f;

	private readonly Control _overlayRoot;
	private readonly PanelLayoutService _layoutService;
	private readonly PanelDragService _dragService;
	private readonly Dictionary<string, ChromeState> _states = [];

	private readonly PanelContainer _settingsPopup;
	private readonly HSlider _widthSlider;
	private readonly HSlider _heightSlider;
	private readonly HSlider _buttonScaleSlider;
	private readonly Label _widthValueLabel;
	private readonly Label _heightValueLabel;
	private readonly Label _buttonScaleValueLabel;
	private bool _interactionEnabled;
	private bool _suppressPopupSignals;

	public string? ActiveSettingsPanelId { get; private set; }

	public PanelHoverChromeService(Control overlayRoot, PanelLayoutService layoutService, PanelDragService dragService)
	{
		_overlayRoot = overlayRoot;
		_layoutService = layoutService;
		_dragService = dragService;

		_settingsPopup = BuildSettingsPopup();
		_widthSlider = _settingsPopup.GetNode<HSlider>("Margin/VBox/WidthRow/WidthSlider");
		_heightSlider = _settingsPopup.GetNode<HSlider>("Margin/VBox/HeightRow/HeightSlider");
		_buttonScaleSlider = _settingsPopup.GetNode<HSlider>("Margin/VBox/ScaleRow/ScaleSlider");
		_widthValueLabel = _settingsPopup.GetNode<Label>("Margin/VBox/WidthRow/WidthValue");
		_heightValueLabel = _settingsPopup.GetNode<Label>("Margin/VBox/HeightRow/HeightValue");
		_buttonScaleValueLabel = _settingsPopup.GetNode<Label>("Margin/VBox/ScaleRow/ScaleValue");

		_widthSlider.ValueChanged += value =>
		{
			RefreshPopupValueLabels();
			if (_suppressPopupSignals || ActiveSettingsPanelId == null)
				return;

			_layoutService.SetWidth(ActiveSettingsPanelId, (float)value);
		};
		_heightSlider.ValueChanged += value =>
		{
			RefreshPopupValueLabels();
			if (_suppressPopupSignals || ActiveSettingsPanelId == null)
				return;

			_layoutService.SetHeight(ActiveSettingsPanelId, (float)value);
		};
		_buttonScaleSlider.ValueChanged += value =>
		{
			RefreshPopupValueLabels();
			if (_suppressPopupSignals || ActiveSettingsPanelId == null)
				return;

			_layoutService.SetButtonScale(ActiveSettingsPanelId, (float)value);
		};
		_settingsPopup.GetNode<Button>("Margin/VBox/ResetBtn").Pressed += ResetActivePanelAppearance;
		_buttonScaleSlider.Value = 1f;
		RefreshPopupValueLabels();
	}

	public void Register(PanelHoverChromeRegistration registration)
	{
		if (_states.ContainsKey(registration.PanelId))
			return;

		var chrome = BuildChrome(registration);
		Control.GuiInputEventHandler dragZoneHandler = ev => OnDragStartInput(registration.PanelId, chrome.DragZone, ev);
		chrome.DragZone.GuiInput += dragZoneHandler;

		var titleBindings = new List<HandleBinding>();
		foreach (var handle in registration.TitleDragHandles)
		{
			var currentHandle = handle;
			Control.GuiInputEventHandler titleHandler = ev => OnDragStartInput(registration.PanelId, currentHandle, ev);
			currentHandle.GuiInput += titleHandler;
			titleBindings.Add(new HandleBinding(currentHandle, titleHandler));
		}

		_overlayRoot.AddChild(chrome.TopBar);
		_states[registration.PanelId] = new ChromeState(
			registration,
			chrome.TopBar,
			chrome.DragZone,
			chrome.ButtonHost,
			dragZoneHandler,
			titleBindings);
	}

	public void Update(Vector2 mousePosition, bool enabled)
	{
		_interactionEnabled = enabled;
		if (!enabled)
		{
			_dragService.StopDirectDrag(persistPosition: true);
			foreach (var state in _states.Values)
			{
				state.TopBar.Visible = false;
				state.ButtonHost.Visible = false;
			}

			if (ActiveSettingsPanelId != null)
				CloseSettingsPopup();
			return;
		}

		foreach (var state in _states.Values)
		{
			if (!state.Registration.Panel.Visible)
			{
				state.TopBar.Visible = false;
				state.ButtonHost.Visible = false;
				if (ActiveSettingsPanelId == state.Registration.PanelId)
					CloseSettingsPopup();
				continue;
			}

			UpdateChromeLayout(state);
			state.TopBar.Visible = true;

			var showButtons = _dragService.DirectDragPanelId == state.Registration.PanelId
				|| ActiveSettingsPanelId == state.Registration.PanelId
				|| state.Registration.Panel.GetGlobalRect().HasPoint(mousePosition)
				|| state.TopBar.GetGlobalRect().HasPoint(mousePosition)
				|| (ActiveSettingsPanelId == state.Registration.PanelId
					&& _settingsPopup.Visible
					&& _settingsPopup.GetGlobalRect().HasPoint(mousePosition));

			state.ButtonHost.Visible = showButtons;
			if (showButtons)
				BringChromeToFront(state);
		}

		if (ActiveSettingsPanelId != null && _states.TryGetValue(ActiveSettingsPanelId, out var active))
			PositionPopup(active);
	}

	public bool HandleInput(InputEvent @event, bool enabled)
	{
		if (!enabled)
			return false;

		if (@event is InputEventMouseMotion motion)
		{
			if (_dragService.DirectDragPanelId == null)
				return false;

			if (_dragService.UpdateDirectDrag(motion.GlobalPosition)
				&& _states.TryGetValue(_dragService.DirectDragPanelId, out var dragState))
			{
				UpdateChromeLayout(dragState);
				BringChromeToFront(dragState);
				if (ActiveSettingsPanelId == dragState.Registration.PanelId && _settingsPopup.Visible)
					PositionPopup(dragState);
			}

			return true;
		}

		if (@event is not InputEventMouseButton mb)
			return false;

		if (_dragService.DirectDragPanelId != null && mb.ButtonIndex == MouseButton.Left && !mb.Pressed)
		{
			var draggingPanelId = _dragService.DirectDragPanelId;
			var handled = _dragService.StopDirectDrag(persistPosition: true);
			if (handled && draggingPanelId != null && _states.TryGetValue(draggingPanelId, out var dragState))
			{
				UpdateChromeLayout(dragState);
				if (ActiveSettingsPanelId == dragState.Registration.PanelId && _settingsPopup.Visible)
					PositionPopup(dragState);
			}

			return handled;
		}

		return false;
	}

	public bool IsPointerOverInteractiveChrome(Vector2 globalPosition)
	{
		return IsProtectedChromeInput(globalPosition);
	}

	public void CloseActiveSettings()
	{
		CloseSettingsPopup();
	}

	private void ToggleSettingsPopup(string panelId)
	{
		if (ActiveSettingsPanelId == panelId)
		{
			CloseSettingsPopup();
			return;
		}

		if (!_states.TryGetValue(panelId, out var state))
			return;

		ActiveSettingsPanelId = panelId;
		SyncPopupWithLayout(panelId);
		state.TopBar.Visible = true;
		state.ButtonHost.Visible = true;
		BringChromeToFront(state);
		PositionPopup(state);
		_settingsPopup.Visible = true;
	}

	private void CloseSettingsPopup()
	{
		ActiveSettingsPanelId = null;
		_settingsPopup.Visible = false;
	}

	private void ResetActivePanelAppearance()
	{
		if (ActiveSettingsPanelId == null)
			return;

		_layoutService.ResetAppearance(ActiveSettingsPanelId);
		SyncPopupWithLayout(ActiveSettingsPanelId);
	}

	private void SyncPopupWithLayout(string panelId)
	{
		var stored = _layoutService.GetStoredAppearance(panelId);
		var resolved = _layoutService.GetResolvedAppearance(panelId);
		_suppressPopupSignals = true;
		_widthSlider.Value = stored.Width ?? 0f;
		_heightSlider.Value = stored.Height ?? 0f;
		_buttonScaleSlider.Value = stored.ButtonScale ?? (resolved.ButtonScale ?? 1f);
		_suppressPopupSignals = false;
		RefreshPopupValueLabels();
	}

	private void UpdateChromeLayout(ChromeState state)
	{
		var panelRect = state.Registration.Panel.GetGlobalRect();
		var position = new Vector2(
			panelRect.Position.X,
			Mathf.Max(0f, panelRect.Position.Y - TopBarHeight - TopBarGap));
		var size = new Vector2(panelRect.Size.X, TopBarHeight);

		state.TopBar.GlobalPosition = position;
		state.TopBar.Size = size;
		state.TopBar.CustomMinimumSize = size;
	}

	private void PositionPopup(ChromeState state)
	{
		var popupSize = _settingsPopup.Size;
		if (popupSize.X <= 0f || popupSize.Y <= 0f)
			popupSize = _settingsPopup.CustomMinimumSize;

		var panelRect = state.Registration.Panel.GetGlobalRect();
		var topBarRect = state.TopBar.GetGlobalRect();
		var viewportRect = _overlayRoot.GetViewportRect();
		_settingsPopup.Position = ResolvePopupPosition(panelRect, topBarRect, popupSize, viewportRect);
		BringSettingsPopupToFront();
	}

	private static Vector2 ResolvePopupPosition(Rect2 panelRect, Rect2 topBarRect, Vector2 popupSize, Rect2 viewportRect)
	{
		var minX = viewportRect.Position.X + ViewportMargin;
		var maxX = Mathf.Max(minX, viewportRect.End.X - popupSize.X - ViewportMargin);
		var minY = viewportRect.Position.Y + ViewportMargin;
		var maxY = Mathf.Max(minY, viewportRect.End.Y - popupSize.Y - ViewportMargin);

		var preferredRightX = panelRect.End.X + PopupGap;
		var preferredLeftX = panelRect.Position.X - popupSize.X - PopupGap;
		var rightFits = preferredRightX <= maxX;
		var leftFits = preferredLeftX >= minX;

		float resolvedX;
		if (leftFits)
		{
			resolvedX = preferredLeftX;
		}
		else if (rightFits)
		{
			resolvedX = preferredRightX;
		}
		else
		{
			var freeRight = viewportRect.End.X - ViewportMargin - panelRect.End.X - PopupGap;
			var freeLeft = panelRect.Position.X - viewportRect.Position.X - ViewportMargin - PopupGap;
			var preferredX = freeLeft >= freeRight ? preferredLeftX : preferredRightX;
			resolvedX = Mathf.Clamp(preferredX, minX, maxX);
		}

		var resolvedY = Mathf.Clamp(topBarRect.Position.Y, minY, maxY);
		return new Vector2(resolvedX, resolvedY);
	}

	private void OnDragStartInput(string panelId, Control sourceHandle, InputEvent @event)
	{
		if (!_interactionEnabled || @event is not InputEventMouseButton mb)
			return;

		if (!_states.TryGetValue(panelId, out var state) || !state.Registration.Panel.Visible)
			return;

		sourceHandle.AcceptEvent();
		if (mb.ButtonIndex != MouseButton.Left || !mb.Pressed)
			return;

		BringChromeToFront(state);
		if (_dragService.BeginDirectDrag(panelId, mb.GlobalPosition))
			UpdateChromeLayout(state);
	}

	private bool IsProtectedChromeInput(Vector2 globalPosition)
	{
		if (_settingsPopup.Visible && _settingsPopup.GetGlobalRect().HasPoint(globalPosition))
			return true;

		foreach (var state in _states.Values)
		{
			if (!state.Registration.Panel.Visible)
				continue;

			if (state.ButtonHost.Visible && state.ButtonHost.GetGlobalRect().HasPoint(globalPosition))
				return true;

			if (state.TopBar.Visible && state.DragZone.GetGlobalRect().HasPoint(globalPosition))
				return true;

			foreach (var binding in state.TitleHandleBindings)
			{
				if (!GodotObject.IsInstanceValid(binding.Handle) || !binding.Handle.Visible)
					continue;

				if (binding.Handle.GetGlobalRect().HasPoint(globalPosition))
					return true;
			}
		}

		return false;
	}

	private void BringChromeToFront(ChromeState state)
	{
		if (state.TopBar.GetParent() == _overlayRoot)
			_overlayRoot.MoveChild(state.TopBar, _overlayRoot.GetChildCount() - 1);
	}

	private void BringSettingsPopupToFront()
	{
		if (_settingsPopup.GetParent() == _overlayRoot)
			_overlayRoot.MoveChild(_settingsPopup, _overlayRoot.GetChildCount() - 1);
	}

	private ChromeControls BuildChrome(PanelHoverChromeRegistration registration)
	{
		var topBar = new HBoxContainer
		{
			Name = $"__hover_chrome_{registration.PanelId}",
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 60,
		};
		topBar.AddThemeConstantOverride("separation", 4);

		var dragZone = new Control
		{
			Name = "DragZone",
			MouseFilter = Control.MouseFilterEnum.Stop,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		topBar.AddChild(dragZone);

		var buttonHost = new HBoxContainer
		{
			Name = "Buttons",
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		buttonHost.AddThemeConstantOverride("separation", 4);
		topBar.AddChild(buttonHost);

		var settingsButton = BuildChromeButton("ui.panel_chrome.button.settings");
		settingsButton.Pressed += () => ToggleSettingsPopup(registration.PanelId);
		var closeButton = BuildChromeButton("ui.panel_chrome.button.close");
		closeButton.Pressed += registration.CloseAction;

		buttonHost.AddChild(settingsButton);
		buttonHost.AddChild(closeButton);
		LocalizationService.LocalizeTree(topBar);
		return new ChromeControls(topBar, dragZone, buttonHost);
	}

	private Button BuildChromeButton(string textKey)
	{
		var button = new Button
		{
			Text = textKey,
			FocusMode = Control.FocusModeEnum.None,
			ThemeTypeVariation = "ActionButton",
		};
		PanelButtonScaleService.MarkIgnored(button);
		LocalizationService.LocalizeTree(button);
		return button;
	}

	private PanelContainer BuildSettingsPopup()
	{
		var popup = new PanelContainer
		{
			Name = "PanelSettingsPopup",
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Stop,
			ZIndex = 120,
			CustomMinimumSize = new Vector2(360f, 186f),
			Size = new Vector2(360f, 186f),
		};
		_overlayRoot.AddChild(popup);

		var margin = new MarginContainer
		{
			Name = "Margin",
			LayoutMode = 2,
		};
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_top", 10);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_bottom", 10);
		popup.AddChild(margin);

		var vbox = new VBoxContainer
		{
			Name = "VBox",
			LayoutMode = 2,
		};
		vbox.AddThemeConstantOverride("separation", 8);
		margin.AddChild(vbox);

		vbox.AddChild(new Label
		{
			Text = "ui.panel_chrome.popup.title",
			HorizontalAlignment = HorizontalAlignment.Center,
			ThemeTypeVariation = "HeaderLabel",
		});
		vbox.AddChild(BuildSliderRow("ui.panel_chrome.popup.width", "WidthSlider", min: 0, max: 1600, step: 10, rounded: true));
		vbox.AddChild(BuildSliderRow("ui.panel_chrome.popup.height", "HeightSlider", min: 0, max: 1200, step: 10, rounded: true));
		vbox.AddChild(BuildSliderRow("ui.panel_chrome.popup.button_scale", "ScaleSlider", min: 0.5f, max: 2.5f, step: 0.1f, rounded: false));

		var resetButton = new Button
		{
			Name = "ResetBtn",
			Text = "ui.panel_chrome.popup.reset",
			FocusMode = Control.FocusModeEnum.None,
			ThemeTypeVariation = "ActionButton",
		};
		PanelButtonScaleService.MarkIgnored(resetButton);
		vbox.AddChild(resetButton);
		LocalizationService.LocalizeTree(popup);
		return popup;
	}

	private Control BuildSliderRow(string labelKey, string sliderName, float min, float max, float step, bool rounded)
	{
		var row = new HBoxContainer
		{
			Name = sliderName.Replace("Slider", "Row", StringComparison.Ordinal),
			LayoutMode = 2,
		};
		row.AddThemeConstantOverride("separation", 8);

		row.AddChild(new Label
		{
			Text = labelKey,
			CustomMinimumSize = new Vector2(120f, 0f),
			VerticalAlignment = VerticalAlignment.Center,
		});

		var slider = new HSlider
		{
			Name = sliderName,
			LayoutMode = 2,
			MinValue = min,
			MaxValue = max,
			Step = step,
			Rounded = rounded,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			FocusMode = Control.FocusModeEnum.None,
		};
		row.AddChild(slider);

		row.AddChild(new Label
		{
			Name = sliderName.Replace("Slider", "Value", StringComparison.Ordinal),
			CustomMinimumSize = new Vector2(56f, 0f),
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center,
		});
		return row;
	}

	private void RefreshPopupValueLabels()
	{
		_widthValueLabel.Text = FormatDimensionValue((float)_widthSlider.Value);
		_heightValueLabel.Text = FormatDimensionValue((float)_heightSlider.Value);
		_buttonScaleValueLabel.Text = FormatScaleValue((float)_buttonScaleSlider.Value);
	}

	private static string FormatDimensionValue(float value)
	{
		return value <= 0f ? "Auto" : Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture);
	}

	private static string FormatScaleValue(float value)
	{
		return $"{value.ToString("0.0", CultureInfo.InvariantCulture)}x";
	}

	private sealed class HandleBinding(Control handle, Control.GuiInputEventHandler handler)
	{
		public Control Handle { get; } = handle;
		public Control.GuiInputEventHandler Handler { get; } = handler;
	}

	private sealed class ChromeState(
		PanelHoverChromeRegistration registration,
		HBoxContainer topBar,
		Control dragZone,
		HBoxContainer buttonHost,
		Control.GuiInputEventHandler dragZoneHandler,
		List<HandleBinding> titleHandleBindings)
	{
		public PanelHoverChromeRegistration Registration { get; } = registration;
		public HBoxContainer TopBar { get; } = topBar;
		public Control DragZone { get; } = dragZone;
		public HBoxContainer ButtonHost { get; } = buttonHost;
		public Control.GuiInputEventHandler DragZoneHandler { get; } = dragZoneHandler;
		public List<HandleBinding> TitleHandleBindings { get; } = titleHandleBindings;
	}

	private readonly record struct ChromeControls(HBoxContainer TopBar, Control DragZone, HBoxContainer ButtonHost);
}
