using System;
using Godot;

namespace MiniRPG.Module;

public enum InputFocus
{
	Action,
	Typing,
	Selection,
	Direction,
	Inventory,
	Chest,
}

public partial class InputModule
{
	public event Action<string>? CommandReceived;

	private readonly LineEdit _lineEdit;
	private InputFocus _focus = InputFocus.Action;
	private string _directionPrefix = "";

	public InputFocus Focus => _focus;

	public InputModule(LineEdit lineEdit)
	{
		_lineEdit = lineEdit;
		_lineEdit.TextSubmitted += OnTextSubmitted;
		_lineEdit.FocusExited += () =>
		{
			if (_focus == InputFocus.Typing) _focus = InputFocus.Action;
		};
		SetFocus(InputFocus.Action);
	}

	public void SetFocus(InputFocus focus, string directionPrefix = "")
	{
		_focus = focus;
		_directionPrefix = focus == InputFocus.Direction ? directionPrefix : "";

		if (focus == InputFocus.Typing)
			_lineEdit.GrabFocus();
		else
			_lineEdit.ReleaseFocus();
	}

	public void EnterActionMode() => SetFocus(InputFocus.Action);
	public void EnterTypingMode() => SetFocus(InputFocus.Typing);
	public void EnterSelectionMode() => SetFocus(InputFocus.Selection);
	public void EnterInventoryMode() => SetFocus(InputFocus.Inventory);
	public void EnterChestMode() => SetFocus(InputFocus.Chest);
	public void EnterDirectionMode(string prefix) => SetFocus(InputFocus.Direction, prefix);

	public bool HandleKeyInput(InputEventKey key)
	{
		if (!key.Pressed)
			return false;

		return _focus switch
		{
			InputFocus.Typing => HandleTypingKey(key),
			InputFocus.Selection => HandleSelectionKey(key),
			InputFocus.Direction => HandleDirectionKey(key),
			InputFocus.Inventory => HandleInventoryKey(key),
			InputFocus.Chest => HandleChestKey(key),
			_ => HandleActionKey(key),
		};
	}

	private bool HandleTypingKey(InputEventKey key)
	{
		if (key.Keycode == Key.Escape)
		{
			SetFocus(InputFocus.Action);
			return true;
		}
		return false;
	}

	private bool HandleSelectionKey(InputEventKey key)
	{
		if (key.Keycode == Key.Escape)
		{
			SetFocus(InputFocus.Action);
			CommandReceived?.Invoke(":select_cancel");
			return true;
		}
		var num = key.Keycode switch
		{
			Key.Key0 => 0,
			Key.Key1 => 1, Key.Key2 => 2, Key.Key3 => 3,
			Key.Key4 => 4, Key.Key5 => 5, Key.Key6 => 6,
			Key.Key7 => 7, Key.Key8 => 8, Key.Key9 => 9,
			_ => -1,
		};
		if (num >= 0)
		{
			CommandReceived?.Invoke($":select_{num}");
			return true;
		}
		return false;
	}

	private bool HandleDirectionKey(InputEventKey key)
	{
		if (key.Keycode == Key.Escape)
		{
			SetFocus(InputFocus.Action);
			CommandReceived?.Invoke(":dir_cancel");
			return true;
		}
		var dir = key.Keycode switch
		{
			Key.W or Key.Up    => "n",
			Key.S or Key.Down  => "s",
			Key.A or Key.Left  => "w",
			Key.D or Key.Right => "e",
			_ => (string?)null,
		};
		if (dir != null)
		{
			var prefix = _directionPrefix;
			SetFocus(InputFocus.Action);
			CommandReceived?.Invoke($":{prefix}_{dir}");
			return true;
		}
		return false;
	}

	private bool HandleInventoryKey(InputEventKey key)
	{
		var invCmd = key.Keycode switch
		{
			Key.W or Key.Up    => ":inv_up",
			Key.S or Key.Down  => ":inv_down",
			Key.A or Key.Left  => ":inv_filter_prev",
			Key.D or Key.Right => ":inv_filter_next",
			Key.E             => ":inv_equip",
			Key.U             => ":inv_use",
			Key.Q             => ":inv_drop",
			Key.R             => ":inv_sort",
			Key.Tab           => key.ShiftPressed ? ":inv_filter_prev" : ":inv_filter_next",
			Key.I or Key.Escape => ":inv_close",
			_ => (string?)null,
		};
		if (invCmd != null)
			CommandReceived?.Invoke(invCmd);
		return true;
	}

	private bool HandleChestKey(InputEventKey key)
	{
		var chestCmd = key.Keycode switch
		{
			Key.W or Key.Up    => ":chest_up",
			Key.S or Key.Down  => ":chest_down",
			Key.E             => ":chest_take",
			Key.P             => ":chest_put",
			Key.Escape        => ":chest_close",
			_ => (string?)null,
		};
		if (chestCmd != null)
			CommandReceived?.Invoke(chestCmd);
		return true;
	}

	private bool HandleActionKey(InputEventKey key)
	{
		var cmd = key.Keycode switch
		{
			Key.W      => "w",
			Key.S      => "s",
			Key.A      => "a",
			Key.D      => "d",
			Key.L      => "look",
			Key.R      => ":render",
			Key.F or Key.O => ":interact",
			Key.I      => ":inventory",
			Key.G      => ":dig",
			Key.K      => ":skills",
			Key.Tab    => ":minimap",
			Key.M      => ":fogmap",
			Key.C      => ":fogmap_center",
			Key.Space  => "enter",
			Key.Escape => ":settings",
			Key.Enter  => ":typing",
			Key.T      => ":typing",
			Key.F5     => ":quicksave",
			Key.F9     => ":quickload",
			Key.Comma  => ":status_prev",
			Key.Period => ":status_next",
			_          => null,
		};

		if (cmd is null)
			return false;

		if (cmd == ":typing")
			SetFocus(InputFocus.Typing);
		else
			CommandReceived?.Invoke(cmd);

		return true;
	}

	private void OnTextSubmitted(string text)
	{
		var cmd = text.Trim().ToLowerInvariant();
		_lineEdit.Clear();
		SetFocus(InputFocus.Action);
		if (cmd.Length > 0)
			CommandReceived?.Invoke(cmd);
	}
}
