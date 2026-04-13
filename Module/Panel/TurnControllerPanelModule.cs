using System;
using Godot;

namespace MiniRPG.Module;

/// <summary>
/// Reusable UI module for the turn controller floating panel.
/// Provides play/pause, single-step, and speed controls for auto-advancing turns.
/// </summary>
public sealed class TurnControllerPanelModule
{
	private readonly PanelContainer _panel;
	private readonly Label _titleLabel;
	private readonly Button _closeBtn;
	private readonly Label _turnLabel;
	private readonly Button _playBtn;
	private readonly Button _stepBtn;
	private readonly Label _speedLabel;
	private readonly HSlider _speedSlider;
	private bool _suppressEvents;

	public TurnControllerPanelModule(PanelContainer panel)
	{
		_panel = panel;
		var root = panel.GetNode<VBoxContainer>("Margin/VBox");
		var titleRow = root.GetNode<HBoxContainer>("TitleRow");
		_titleLabel = titleRow.GetNode<Label>("Title");
		_closeBtn = titleRow.GetNode<Button>("CloseBtn");
		_turnLabel = root.GetNode<Label>("TurnLabel");
		var controlRow = root.GetNode<HBoxContainer>("ControlRow");
		_playBtn = controlRow.GetNode<Button>("PlayBtn");
		_stepBtn = controlRow.GetNode<Button>("StepBtn");
		_speedLabel = controlRow.GetNode<Label>("SpeedLabel");
		_speedSlider = root.GetNode<HSlider>("SpeedSlider");

		_playBtn.Pressed += () => PlayToggled?.Invoke();
		_stepBtn.Pressed += () => StepRequested?.Invoke();
		_closeBtn.Pressed += () => CloseRequested?.Invoke();
		_speedSlider.ValueChanged += value =>
		{
			if (!_suppressEvents)
				SpeedChanged?.Invoke((int)value);
		};

		RefreshTexts();
	}

	public event Action? PlayToggled;
	public event Action? StepRequested;
	public event Action<int>? SpeedChanged;
	public event Action? CloseRequested;

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public bool IsPointerOver(Vector2 globalPos)
		=> _panel.Visible && _panel.GetGlobalRect().HasPoint(globalPos);

	public void Refresh(int turn, bool playing)
	{
		_turnLabel.Text = $"T: {turn}";
		_playBtn.Text = playing ? "⏸" : "▶";
	}

	public void SetSpeed(int turnsPerSecond)
	{
		_suppressEvents = true;
		_speedSlider.Value = turnsPerSecond;
		_suppressEvents = false;
		_speedLabel.Text = $"×{turnsPerSecond}";
	}

	public void RefreshTexts()
	{
		_titleLabel.Text = LocalizationService.TOrFallback("ui.turn_controller.title", "Turn Controller");
		_playBtn.TooltipText = LocalizationService.TOrFallback("ui.turn_controller.play", "Play / Pause");
		_stepBtn.TooltipText = LocalizationService.TOrFallback("ui.turn_controller.step", "Single Step");
	}
}
