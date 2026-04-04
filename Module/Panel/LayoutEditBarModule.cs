using System;
using Godot;

namespace MiniRPG.Module.Panel;

public sealed class LayoutEditBarModule
{
	private readonly PanelContainer _panel;

	public bool Visible => _panel.Visible;

	public event Action? ApplyRequested;
	public event Action? CancelRequested;
	public event Action? ResetRequested;

	public LayoutEditBarModule(PanelContainer panel)
	{
		_panel = panel;
		var actionBar = panel.GetNode<HBoxContainer>("MarginContainer/VBox/ActionBar");
		actionBar.GetNode<Button>("ApplyBtn").Pressed += () => ApplyRequested?.Invoke();
		actionBar.GetNode<Button>("CancelBtn").Pressed += () => CancelRequested?.Invoke();
		actionBar.GetNode<Button>("ResetBtn").Pressed += () => ResetRequested?.Invoke();
	}

	public void Open()
	{
		_panel.Visible = true;
	}

	public void Close()
	{
		_panel.Visible = false;
	}
}
