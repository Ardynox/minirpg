using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module;

public readonly record struct ConfirmDialogAction(string Id, string Text);

public sealed class ConfirmDialogModule : IModalInputLayer
{
	private readonly PanelContainer _panel;
	private readonly Label _titleLabel;
	private readonly Label _messageLabel;
	private readonly Button[] _actionButtons;

	private List<ConfirmDialogAction> _actions = [];
	private int _defaultActionIndex;

	public event Action<string>? ActionSelected;
	public event Action? CancelRequested;

	public ConfirmDialogModule(PanelContainer panel)
	{
		_panel = panel;
		_titleLabel = panel.GetNode<Label>("Margin/VBox/Title");
		_messageLabel = panel.GetNode<Label>("Margin/VBox/Message");
		_actionButtons =
		[
			panel.GetNode<Button>("Margin/VBox/Actions/Action1Btn"),
			panel.GetNode<Button>("Margin/VBox/Actions/Action2Btn"),
			panel.GetNode<Button>("Margin/VBox/Actions/Action3Btn"),
		];

		for (var i = 0; i < _actionButtons.Length; i++)
		{
			var index = i;
			_actionButtons[i].Pressed += () =>
			{
				if (index >= 0 && index < _actions.Count)
					ActionSelected?.Invoke(_actions[index].Id);
			};
		}
	}

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public void Open(string title, string message, IReadOnlyList<ConfirmDialogAction> actions, int defaultActionIndex = 0)
	{
		if (actions.Count == 0 || actions.Count > _actionButtons.Length)
			throw new ArgumentOutOfRangeException(nameof(actions), "Confirm dialog supports between 1 and 3 actions.");

		_actions = new List<ConfirmDialogAction>(actions);
		_defaultActionIndex = Math.Clamp(defaultActionIndex, 0, actions.Count - 1);
		_titleLabel.Text = title;
		_messageLabel.Text = message;
		for (var i = 0; i < _actionButtons.Length; i++)
		{
			var visible = i < _actions.Count;
			_actionButtons[i].Visible = visible;
			_actionButtons[i].Disabled = !visible;
			_actionButtons[i].Text = visible ? _actions[i].Text : string.Empty;
		}

		Visible = true;
		_actionButtons[_defaultActionIndex].GrabFocus();
	}

	public void Close()
	{
		Visible = false;
	}

	public bool HandleKeyInput(InputEventKey key)
	{
		if (!key.Pressed || key.Echo || key.AltPressed || key.CtrlPressed || key.MetaPressed)
			return false;

		if (key.Keycode == Key.Escape)
		{
			CancelRequested?.Invoke();
			return true;
		}

		if (key.Keycode is not (Key.Enter or Key.KpEnter))
			return false;

		var focusOwner = _panel.GetViewport().GuiGetFocusOwner();
		if (focusOwner is Button)
			return false;

		if (_defaultActionIndex >= 0 && _defaultActionIndex < _actions.Count)
		{
			ActionSelected?.Invoke(_actions[_defaultActionIndex].Id);
			return true;
		}

		return false;
	}
}
