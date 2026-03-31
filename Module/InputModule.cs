using System;
using Godot;

namespace MiniRPG.Module;

public partial class InputModule
{
	public event Action<string>? CommandReceived;

	private readonly LineEdit _lineEdit;
	private bool _typingMode;
	private bool _selectionMode;
	private bool _directionMode;
	private string _directionPrefix = "";

	public bool IsTypingMode => _typingMode;
	public bool IsSelectionMode => _selectionMode;
	public bool IsDirectionMode => _directionMode;

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
		_directionMode = false;
		_directionPrefix = "";
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
		_directionMode = false;
		_lineEdit.ReleaseFocus();
	}

	/// <summary>进入方向选择模式：下一个方向键输入会发出 "{prefix}_{dir}" 命令。</summary>
	public void EnterDirectionMode(string prefix)
	{
		_directionMode = true;
		_directionPrefix = prefix;
		_typingMode = false;
		_selectionMode = false;
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

		if (_directionMode)
		{
			if (key.Keycode == Key.Escape)
			{
				EnterActionMode();
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
				EnterActionMode();
				CommandReceived?.Invoke($":{prefix}_{dir}");
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
