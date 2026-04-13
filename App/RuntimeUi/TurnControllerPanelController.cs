using System;
using MiniRPG.Module;

namespace MiniRPG;

/// <summary>
/// Reusable controller that auto-advances turns at a configurable speed.
/// Drives full turn logic (AdvanceAuto) via a callback, decoupled from any specific UI context.
/// </summary>
internal sealed class TurnControllerPanelController
{
	private readonly TurnControllerPanelModule _module;
	private readonly Action _advanceTurn;
	private readonly Action _flushMap;
	private readonly Func<int> _getTurn;

	private double _timer;
	private int _turnsPerSecond = 5;

	public TurnControllerPanelController(
		TurnControllerPanelModule module,
		Action advanceTurn,
		Action flushMap,
		Func<int> getTurn)
	{
		_module = module;
		_advanceTurn = advanceTurn;
		_flushMap = flushMap;
		_getTurn = getTurn;

		_module.PlayToggled += TogglePlay;
		_module.StepRequested += Step;
		_module.SpeedChanged += HandleSpeedChanged;
		_module.CloseRequested += Close;
		_module.SetSpeed(_turnsPerSecond);
	}

	public bool Playing { get; private set; }
	public bool Visible => _module.Visible;

	public bool IsPointerOver(Godot.Vector2 globalPos) => _module.IsPointerOver(globalPos);

	public void Process(double delta)
	{
		if (!Playing || !_module.Visible)
			return;

		var interval = 1.0 / _turnsPerSecond;
		_timer += delta;
		if (_timer < interval)
			return;

		_timer = 0;
		_advanceTurn();
		_flushMap();
		_module.Refresh(_getTurn(), Playing);
	}

	public void Toggle()
	{
		if (_module.Visible)
			Close();
		else
			Open();
	}

	public void Open()
	{
		_module.Visible = true;
		_module.Refresh(_getTurn(), Playing);
	}

	public void Close()
	{
		Playing = false;
		_timer = 0;
		_module.Visible = false;
		_module.Refresh(_getTurn(), Playing);
	}

	public void RefreshTexts() => _module.RefreshTexts();

	private void TogglePlay()
	{
		Playing = !Playing;
		_timer = 0;
		_module.Refresh(_getTurn(), Playing);
	}

	private void Step()
	{
		Playing = false;
		_timer = 0;
		_advanceTurn();
		_flushMap();
		_module.Refresh(_getTurn(), Playing);
	}

	private void HandleSpeedChanged(int turnsPerSecond)
	{
		_turnsPerSecond = Math.Clamp(turnsPerSecond, 1, 20);
		_module.SetSpeed(_turnsPerSecond);
	}
}
