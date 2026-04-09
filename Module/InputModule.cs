using System;
using Godot;

namespace MiniRPG.Module;

public enum InputFocus
{
	Action,
	Typing,
	Selection,
	Direction,
}

public partial class InputModule
{
	public event Action<string>? CommandReceived;

	private readonly LineEdit _lineEdit;
	private readonly InputBindingService _bindings;
	private InputFocus _focus = InputFocus.Action;
	private string _directionPrefix = "";
	private Action<int>? _selectionCallback;

	public InputFocus Focus => _focus;

	public void EnterSelection(Action<int> callback, Action? onCancel = null)
	{
		_selectionCallback = callback;
		_onSelectionCancel = onCancel;
		SetFocus(InputFocus.Selection);
	}

	public void CancelSelection()
	{
		_selectionCallback = null;
		_onSelectionCancel = null;
		if (_focus == InputFocus.Selection)
			SetFocus(InputFocus.Action);
	}

	private Action? _onSelectionCancel;

	public InputModule(LineEdit lineEdit, InputBindingService bindings)
	{
		_lineEdit = lineEdit;
		_bindings = bindings;
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
			_ => HandleActionKey(key),
		};
	}

	public bool HandleMouseButtonInput(InputEventMouseButton button)
	{
		if (!button.Pressed || _focus != InputFocus.Action)
			return false;

		if (!_bindings.Resolve(InputBindingContext.Action, button, out _, out var cmd))
			return false;

		if (!string.IsNullOrEmpty(cmd))
			CommandReceived?.Invoke(cmd);

		return true;
	}

	private bool HandleTypingKey(InputEventKey key)
	{
		if (!_bindings.Resolve(InputBindingContext.Typing, key, out var actionId, out _))
			return false;

		if (actionId == "typing_cancel")
		{
			SetFocus(InputFocus.Action);
			return true;
		}

		return false;
	}

	private bool HandleSelectionKey(InputEventKey key)
	{
		if (!_bindings.Resolve(InputBindingContext.Selection, key, out var actionId, out _))
			return false;

		if (actionId == "selection_cancel")
		{
			var cancel = _onSelectionCancel;
			CancelSelection();
			cancel?.Invoke();
			return true;
		}

		if (!actionId.StartsWith("selection_"))
			return false;

		if (!int.TryParse(actionId["selection_".Length..], out var num))
			return false;

		if (num >= 0 && _selectionCallback != null)
		{
			var cb = _selectionCallback;
			_selectionCallback = null;
			_onSelectionCancel = null;
			SetFocus(InputFocus.Action);
			cb(num);
			return true;
		}

		return true;
	}

	private bool HandleDirectionKey(InputEventKey key)
	{
		if (!_bindings.Resolve(InputBindingContext.Direction, key, out var actionId, out _))
		{
			var fallbackDir = key.Keycode switch
			{
				Key.U => "up",
				Key.J => "down",
				_ => null,
			};

			if (fallbackDir == null)
				return false;

			var fallbackPrefix = _directionPrefix;
			SetFocus(InputFocus.Action);
			CommandReceived?.Invoke($":{fallbackPrefix}_{fallbackDir}");
			return true;
		}

		if (actionId == "direction_cancel")
		{
			SetFocus(InputFocus.Action);
			CommandReceived?.Invoke(":dir_cancel");
			return true;
		}

		var dir = actionId switch
		{
			"direction_n" => "n",
			"direction_s" => "s",
			"direction_w" => "w",
			"direction_e" => "e",
			"direction_up" => "up",
			"direction_down" => "down",
			_ => null,
		};

		if (dir == null)
		{
			dir = key.Keycode switch
			{
				Key.U => "up",
				Key.J => "down",
				_ => null,
			};
		}

		if (dir != null)
		{
			var prefix = _directionPrefix;
			SetFocus(InputFocus.Action);
			CommandReceived?.Invoke($":{prefix}_{dir}");
			return true;
		}

		return true;
	}

	private bool HandleActionKey(InputEventKey key)
	{
		if (IsAsteriskKey(key))
		{
			CommandReceived?.Invoke(":inspect_mode");
			return true;
		}

		if (!_bindings.Resolve(InputBindingContext.Action, key, out var actionId, out var cmd))
			return false;

		if (actionId == "open_typing")
		{
			SetFocus(InputFocus.Typing);
			return true;
		}

		if (!string.IsNullOrEmpty(cmd))
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

	private static bool IsAsteriskKey(InputEventKey key) =>
		key.Pressed && (key.Keycode == Key.Asterisk || key.Keycode == Key.KpMultiply || key.Unicode == '*');
}
