using System;
using Godot;

namespace MiniRPG.Module;

/// <summary>
/// 输入模块：管理动作模式（键盘直接操作）与打字模式（LineEdit 文本输入）的切换，
/// 将所有输入统一转化为 CommandReceived 事件交给外部处理。
/// </summary>
public partial class InputModule
{
	public event Action<string>? CommandReceived;

	private readonly LineEdit _lineEdit;
	private bool _typingMode;

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
		_lineEdit.ReleaseFocus();
	}

	public void EnterTypingMode()
	{
		_typingMode = true;
		_lineEdit.GrabFocus();
	}

	/// <summary>由 Main._UnhandledInput 调用，返回 true 表示事件已消费。</summary>
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

		var cmd = key.Keycode switch
		{
			Key.W     => "w",
			Key.S     => "s",
			Key.A     => "a",
			Key.D     => "d",
			Key.J     => "atk",
			Key.L     => "look",
			Key.Enter => ":typing",
			Key.T     => ":typing",
			_         => null,
		};

		if (cmd is null)
			return false;

		if (cmd == ":typing")
		{
			EnterTypingMode();
		}
		else
		{
			CommandReceived?.Invoke(cmd);
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
