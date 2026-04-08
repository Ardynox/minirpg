using System;
using Godot;

namespace MiniRPG.Module;

public sealed class SaveNameDialogModule : IModalInputLayer
{
	private readonly PanelContainer _panel;
	private readonly LineEdit _nameEdit;

	public SaveNameDialogModule(PanelContainer panel)
	{
		_panel = panel;
		_nameEdit = panel.GetNode<LineEdit>("Margin/VBox/NameEdit");
		_nameEdit.TextSubmitted += _ => ConfirmRequested?.Invoke(_nameEdit.Text);
		panel.GetNode<Button>("Margin/VBox/Actions/ConfirmBtn").Pressed += () => ConfirmRequested?.Invoke(_nameEdit.Text);
		panel.GetNode<Button>("Margin/VBox/Actions/CancelBtn").Pressed += () => CancelRequested?.Invoke();
	}

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public event Action<string>? ConfirmRequested;
	public event Action? CancelRequested;

	public void Open(string suggestedName)
	{
		_nameEdit.Text = suggestedName;
		Visible = true;
		_nameEdit.GrabFocus();
		_nameEdit.CaretColumn = _nameEdit.Text.Length;
	}

	public void Close()
	{
		Visible = false;
		_nameEdit.ReleaseFocus();
	}

	public bool HandleKeyInput(InputEventKey key)
	{
		if (!key.Pressed || key.Echo || key.AltPressed || key.CtrlPressed || key.MetaPressed)
			return false;

		if (key.Keycode != Key.Escape)
			return false;

		CancelRequested?.Invoke();
		return true;
	}
}
