using System;
using Godot;

namespace MiniRPG.Module;

public partial class InputModule
{
	public event Action<string>? CommandReceived;

	private readonly LineEdit _lineEdit;
	private bool _typingMode;
	private bool _selectionMode;

	public bool IsTypingMode => _typingMode;
	public bool IsSelectionMode => _selectionMode;

	public InputModule(LineEdit lineEdit)
	{
		_lineEdit = lineEdit;
		_lineEdit.TextSubmitted += OnTextSubmitted;
		_lineEdit.FocusExited += () => _typingMode = false;
		EnterActionMode();
	}

	public void EnterActionMode()
	{
		_typingMode = false;
		_selectionMode = false;
		_lineEdit.ReleaseFocus();
	}

	public void EnterTypingMode()
	{
		_typingMode = true;
		_selectionMode = false;
		_lineEdit.GrabFocus();
	}

	public void EnterSelectionMode()
	{
		_selectionMode = true;
		_typingMode = false;
		_lineEdit.ReleaseFocus();
	}

	public bool HandleKeyInput(InputEventKey key)
	{
		if (!key.Pressed)
			return false;

		if (_typingMode)
		{
			if (key.Keycode == Key.Escape)
			{
				EnterActionMode();
				return true;
			}
			return false;
		}

		if (_selectionMode)
		{
			if (key.Keycode == Key.Escape)
			{
				EnterActionMode();
				CommandReceived?.Invoke(":select_cancel");
				return true;
			}
			var num = key.Keycode switch
			{
				Key.Key1 => 1, Key.Key2 => 2, Key.Key3 => 3,
				Key.Key4 => 4, Key.Key5 => 5, Key.Key6 => 6,
				Key.Key7 => 7, Key.Key8 => 8, Key.Key9 => 9,
				_ => -1,
			};
			if (num > 0)
			{
				CommandReceived?.Invoke($":select_{num}");
				return true;
			}
			return false;
		}

		var cmd = key.Keycode switch
		{
			Key.W      => "w",
			Key.S      => "s",
			Key.A      => "a",
			Key.D      => "d",
			Key.L      => "look",
			Key.R      => ":render",
			Key.F      => ":interact",
			Key.I      => ":inventory",
			Key.Space  => "enter",
			Key.Escape => ":settings",
			Key.Enter  => ":typing",
			Key.T      => ":typing",
			Key.F5     => ":quicksave",
			Key.F9     => ":quickload",
			_          => null,
		};

		if (cmd is null)
			return false;

		switch (cmd)
		{
			case ":typing":
				EnterTypingMode();
				break;
			default:
				CommandReceived?.Invoke(cmd);
				break;
		}

		return true;
	}

	private void OnTextSubmitted(string text)
	{
		var cmd = text.Trim().ToLowerInvariant();
		_lineEdit.Clear();
		EnterActionMode();
		if (cmd.Length > 0)
			CommandReceived?.Invoke(cmd);
	}
}
