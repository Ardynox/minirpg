using System;
using Godot;

namespace MiniRPG.Module.WorldTool;

internal sealed class RuntimeWorldToolHeightPanelModule
{
	private readonly PanelContainer _panel;
	private readonly HBoxContainer _headerRow;
	private readonly Control _dragZone;
	private readonly Label _titleLabel;
	private readonly Button _centerButton;
	private readonly HBoxContainer _controlRow;
	private readonly Button _downButton;
	private readonly Label _valueLabel;
	private readonly Button _upButton;

	public RuntimeWorldToolHeightPanelModule(PanelContainer panel)
	{
		_panel = panel;
		_panel.Visible = false;
		_panel.MouseFilter = Control.MouseFilterEnum.Stop;
		_panel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		_panel.OffsetLeft = 16f;
		_panel.OffsetTop = 352f;
		_panel.CustomMinimumSize = new Vector2(216f, 0f);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 10);
		margin.AddThemeConstantOverride("margin_top", 8);
		margin.AddThemeConstantOverride("margin_right", 10);
		margin.AddThemeConstantOverride("margin_bottom", 8);
		_panel.AddChild(margin);

		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 6);
		margin.AddChild(root);

		_headerRow = new HBoxContainer();
		_headerRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_headerRow.Alignment = BoxContainer.AlignmentMode.Center;
		root.AddChild(_headerRow);

		_dragZone = new Control
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(96f, 28f),
			Name = "DragZone",
		};
		_headerRow.AddChild(_dragZone);

		_titleLabel = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			AnchorRight = 1f,
			AnchorBottom = 1f,
			OffsetLeft = 0f,
			OffsetTop = 0f,
			OffsetRight = 0f,
			OffsetBottom = 0f,
		};
		_titleLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_dragZone.AddChild(_titleLabel);

		_centerButton = new Button
		{
			CustomMinimumSize = new Vector2(68f, 0f),
			ThemeTypeVariation = "ActionButton",
		};
		_headerRow.AddChild(_centerButton);

		_controlRow = new HBoxContainer();
		_controlRow.AddThemeConstantOverride("separation", 8);
		_controlRow.Alignment = BoxContainer.AlignmentMode.Center;
		root.AddChild(_controlRow);

		_downButton = new Button
		{
			CustomMinimumSize = new Vector2(36f, 0f),
			ThemeTypeVariation = "ActionButton",
			Text = "▼",
		};
		_controlRow.AddChild(_downButton);

		_valueLabel = new Label
		{
			CustomMinimumSize = new Vector2(72f, 0f),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};
		_controlRow.AddChild(_valueLabel);

		_upButton = new Button
		{
			CustomMinimumSize = new Vector2(36f, 0f),
			ThemeTypeVariation = "ActionButton",
			Text = "▲",
		};
		_controlRow.AddChild(_upButton);

		_downButton.Pressed += () => HeightChanged?.Invoke(-1);
		_upButton.Pressed += () => HeightChanged?.Invoke(1);
		_centerButton.Pressed += () => CenterRequested?.Invoke();

		RefreshTexts();
		Render(0);
	}

	public event Action<int>? HeightChanged;
	public event Action? CenterRequested;

	public PanelContainer PanelNode => _panel;
	public Control DragHandle => _dragZone;

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public bool IsPointerOver(Vector2 globalPosition) =>
		_panel.Visible && _panel.GetGlobalRect().HasPoint(globalPosition);

	public void RefreshTexts()
	{
		_titleLabel.Text = LocalizationService.TOrFallback("ui.runtime_tool.height.title", "View Layer");
		_centerButton.Text = LocalizationService.TOrFallback("ui.runtime_tool.height.center", "Player");
	}

	public void Render(int cameraZ)
	{
		_valueLabel.Text = $"Z: {cameraZ}";
	}
}
