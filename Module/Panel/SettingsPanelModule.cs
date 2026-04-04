using System;
using Godot;

namespace MiniRPG.Module.Panel;

public enum SettingsPanelMode
{
	InGame,
	Menu,
}

public sealed class SettingsPanelModule : IPanel
{
	private readonly PanelContainer _panel;
	private readonly Button _watchModeButton;
	private readonly Button _layoutEditButton;
	private readonly Button _saveButton;
	private readonly Button _loadButton;
	private readonly Button _backToMenuButton;
	private SettingsPanelMode _mode;

	public string PanelId => "settings";
	public PanelContainer PanelNode => _panel;
	public bool Visible { get => _panel.Visible; set => _panel.Visible = value; }
	public bool ConsumeUnhandledKeys => true;
	public bool Dirty { get; set; }
	public bool OpenedFromMenu => _mode == SettingsPanelMode.Menu;

	public event Action? RenderToggleRequested;
	public event Action? WatchModeToggleRequested;
	public event Action? LayoutEditRequested;
	public event Action? SaveRequested;
	public event Action? LoadRequested;
	public event Action? KeyBindingsRequested;
	public event Action? ReturnToMenuRequested;
	public event Action? CloseRequested;

	public SettingsPanelModule(PanelContainer panel)
	{
		_panel = panel;
		var vbox = panel.GetNode<VBoxContainer>("VBox");

		vbox.GetNode<Button>("RenderToggle").Pressed += () => RenderToggleRequested?.Invoke();
		_watchModeButton = vbox.GetNode<Button>("WatchModeToggle");
		_watchModeButton.Pressed += () => WatchModeToggleRequested?.Invoke();
		_layoutEditButton = vbox.GetNode<Button>("LayoutEditBtn");
		_layoutEditButton.Pressed += () => LayoutEditRequested?.Invoke();
		vbox.GetNode<Button>("KeyBindingsBtn").Pressed += () => KeyBindingsRequested?.Invoke();

		_saveButton = vbox.GetNode<Button>("SaveBtn");
		_saveButton.Pressed += () => SaveRequested?.Invoke();
		_loadButton = vbox.GetNode<Button>("LoadBtn");
		_loadButton.Pressed += () => LoadRequested?.Invoke();

		_backToMenuButton = vbox.GetNode<Button>("BackToMenuBtn");
		_backToMenuButton.Pressed += () => ReturnToMenuRequested?.Invoke();
		vbox.GetNode<Button>("CloseBtn").Pressed += RequestClose;
	}

	public void OpenInGame(bool watchMode)
	{
		_mode = SettingsPanelMode.InGame;
		ApplyContext(inGame: true);
		SetWatchMode(watchMode);
		Visible = true;
	}

	public void OpenFromMenu(bool watchMode)
	{
		_mode = SettingsPanelMode.Menu;
		ApplyContext(inGame: false);
		SetWatchMode(watchMode);
		Visible = true;
	}

	public void Close()
	{
		Visible = false;
	}

	public void SetWatchMode(bool enabled)
	{
		_watchModeButton.Text = enabled ? "看海模式：开启" : "看海模式：关闭";
	}

	public bool HandleCommand(string cmd)
	{
		if (cmd != "close")
			return false;

		RequestClose();
		return true;
	}

	private void ApplyContext(bool inGame)
	{
		_layoutEditButton.Visible = inGame;
		_saveButton.Visible = inGame;
		_loadButton.Visible = inGame;
		_backToMenuButton.Visible = inGame;
	}

	private void RequestClose()
	{
		if (OpenedFromMenu)
		{
			ReturnToMenuRequested?.Invoke();
			return;
		}

		CloseRequested?.Invoke();
	}
}
