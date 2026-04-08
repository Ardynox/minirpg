using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Config;

namespace MiniRPG.Module;

public sealed class KeyBindingsController
{
	private readonly InputBindingService _bindings;

	private InputBindingContext _context = InputBindingContext.Action;
	private string? _selectedActionId;
	private int _captureSlot = -1;
	private IReadOnlyList<BindingActionView> _views = Array.Empty<BindingActionView>();
	private string? _statusKey;
	private (string Name, object? Value)[] _statusArgs = [];

	private static readonly InputBindingContext[] ContextOrder =
	[
		InputBindingContext.Action,
		InputBindingContext.Typing,
		InputBindingContext.Selection,
		InputBindingContext.Direction,
	];

	public InputBindingContext CurrentContext => _context;
	public string? SelectedActionId => _selectedActionId;
	public bool IsCapturing => _captureSlot >= 0;
	public int CaptureSlot => _captureSlot;
	public IReadOnlyList<BindingActionView> CurrentActions => _views;
	public string StatusText => string.IsNullOrEmpty(_statusKey)
		? string.Empty
		: LocalizationService.T(_statusKey, _statusArgs);

	public KeyBindingsController(InputBindingService bindings)
	{
		_bindings = bindings;
		Refresh();
	}

	public void Refresh()
	{
		_views = _bindings.GetActions(_context);
		if (_views.Count == 0)
		{
			_selectedActionId = null;
			return;
		}

		if (_selectedActionId == null || !ContainsAction(_selectedActionId))
			_selectedActionId = _views[0].Id;
	}

	public void SetContext(InputBindingContext context)
	{
		_context = context;
		_selectedActionId = null;
		_captureSlot = -1;
		SetStatus(null);
		Refresh();
	}

	public bool SelectAction(string actionId)
	{
		if (string.IsNullOrEmpty(actionId) || !ContainsAction(actionId))
			return false;

		_selectedActionId = actionId;
		return true;
	}

	public void StartCapture(int slot)
	{
		if (_selectedActionId == null || _views.Count == 0 || slot is < 0 or > 1)
			return;

		_captureSlot = slot;
		SetStatus(slot == 0
			? "ui.key_bindings.status.capture_primary"
			: "ui.key_bindings.status.capture_secondary");
	}

	public bool HandleCommand(string cmd)
	{
		switch (cmd)
		{
			case "up":
				MoveSelection(-1);
				return true;
			case "down":
				MoveSelection(1);
				return true;
			case "left":
			case "tab_prev":
				CycleContext(-1);
				return true;
			case "right":
			case "tab_next":
				CycleContext(1);
				return true;
			case "confirm":
			case "action1":
				StartCapture(0);
				return true;
			case "action2":
				StartCapture(1);
				return true;
			case "action3":
				ResetCurrentContext();
				return true;
			case "action4":
				ResetAllContexts();
				return true;
			case "close":
				if (!IsCapturing)
					return false;

				CancelCapture();
				return true;
			default:
				return false;
		}
	}

	public bool HandleKey(Key keycode, bool ctrl = false, bool alt = false, bool shift = false)
	{
		if (_captureSlot >= 0)
		{
			if (keycode == Key.Escape)
			{
				CancelCapture();
				return true;
			}

			var gesture = InputGesture.FromKey(keycode, ctrl, alt, shift);
			if (gesture.IsEmpty || keycode is Key.Ctrl or Key.Alt or Key.Shift or Key.Meta)
				return true;

			return ApplyCaptureGesture(gesture);
		}

		return keycode switch
		{
			Key.W or Key.Up => HandleCommand("up"),
			Key.S or Key.Down => HandleCommand("down"),
			Key.A or Key.Left => HandleCommand("left"),
			Key.D or Key.Right => HandleCommand("right"),
			Key.Enter => HandleCommand("confirm"),
			Key.Space => HandleCommand("action2"),
			Key.Tab => HandleCommand(shift ? "tab_prev" : "tab_next"),
			_ => false,
		};
	}

	public bool HandleMouseWheel(MouseButton button, bool ctrl = false, bool alt = false, bool shift = false)
	{
		if (_captureSlot < 0 || button is not MouseButton.WheelUp and not MouseButton.WheelDown)
			return false;

		return ApplyCaptureGesture(InputGesture.FromMouseWheel(button, ctrl, alt, shift));
	}

	public void ResetCurrentContext()
	{
		_bindings.ResetContext(_context);
		SetStatus("ui.key_bindings.status.reset_context");
		_captureSlot = -1;
		Refresh();
	}

	public void ResetAllContexts()
	{
		_bindings.ResetAll();
		SetStatus("ui.key_bindings.status.reset_all");
		_captureSlot = -1;
		Refresh();
	}

	private void CycleContext(int delta)
	{
		var idx = Array.IndexOf(ContextOrder, _context);
		idx = (idx + delta + ContextOrder.Length) % ContextOrder.Length;
		SetContext(ContextOrder[idx]);
	}

	private bool ContainsAction(string actionId)
	{
		foreach (var row in _views)
		{
			if (row.Id == actionId)
				return true;
		}

		return false;
	}

	private void MoveSelection(int delta)
	{
		if (_views.Count == 0 || _selectedActionId == null)
			return;

		var idx = -1;
		for (var i = 0; i < _views.Count; i++)
		{
			if (_views[i].Id != _selectedActionId)
				continue;

			idx = i;
			break;
		}

		if (idx < 0)
			idx = 0;

		idx = Math.Clamp(idx + delta, 0, _views.Count - 1);
		_selectedActionId = _views[idx].Id;
	}

	private void CancelCapture()
	{
		_captureSlot = -1;
		SetStatus("ui.key_bindings.status.canceled");
	}

	private bool ApplyCaptureGesture(InputGesture gesture)
	{
		if (_selectedActionId == null || _captureSlot < 0)
			return true;

		var slot = _captureSlot;
		_captureSlot = -1;
		if (_bindings.Rebind(_context, _selectedActionId, slot, gesture))
			SetStatus("ui.key_bindings.status.bound", ("gesture", gesture.ToDisplayString()));
		else
			SetStatus("ui.key_bindings.status.failed");

		Refresh();
		return true;
	}

	private void SetStatus(string? key, params (string Name, object? Value)[] args)
	{
		_statusKey = key;
		_statusArgs = args;
	}
}
