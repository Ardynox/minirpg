using System;
using Godot;

namespace MiniRPG.Module;

/// <summary>
/// 主菜单 + 设置面板的 UI 状态管理。
/// 职责：管理三态切换（MainMenu ↔ Settings ↔ InGame）、按钮信号注册、面板显隐。
/// 不包含游戏逻辑——新建/加载/存档等操作通过事件回调通知调用方。
/// </summary>
public class MenuModule
{
	public enum Screen { MainMenu, Settings, InGame }

	private readonly PanelContainer _mainMenu;
	private readonly PanelContainer _settingsPanel;
	private readonly VBoxContainer _gameUI;
	private readonly Button _continueBtn;
	private readonly Button _settingSaveBtn;
	private readonly Button _settingLoadBtn;
	private readonly Button _settingBackToMenuBtn;

	private bool _settingsFromMenu;

	public Screen CurrentScreen { get; private set; } = Screen.MainMenu;
	public bool SettingsOpen { get; private set; }
	public bool InMenu => CurrentScreen != Screen.InGame;

	/// <summary>菜单按钮事件：调用方订阅后执行对应的游戏逻辑。</summary>
	public event Action? OnContinue;
	public event Action? OnNewGame;
	public event Action? OnLoadGame;
	public event Action? OnQuit;
	public event Action? OnBackToMenu;

	public MenuModule(Node root)
	{
		_mainMenu = root.GetNode<PanelContainer>("MainMenu");
		_settingsPanel = root.GetNode<PanelContainer>("SettingsPanel");
		_gameUI = root.GetNode<VBoxContainer>("UI");
		_continueBtn = root.GetNode<Button>("MainMenu/Center/VBox/ContinueBtn");

		_settingSaveBtn = root.GetNode<Button>("SettingsPanel/VBox/SaveBtn");
		_settingLoadBtn = root.GetNode<Button>("SettingsPanel/VBox/LoadBtn");
		_settingBackToMenuBtn = root.GetNode<Button>("SettingsPanel/VBox/BackToMenuBtn");

		root.GetNode<Button>("MainMenu/Center/VBox/ContinueBtn").Pressed += () => OnContinue?.Invoke();
		root.GetNode<Button>("MainMenu/Center/VBox/NewGameBtn").Pressed += () => OnNewGame?.Invoke();
		root.GetNode<Button>("MainMenu/Center/VBox/LoadGameBtn").Pressed += () => OnLoadGame?.Invoke();
		root.GetNode<Button>("MainMenu/Center/VBox/SettingsBtn").Pressed += OpenSettingsFromMenu;
		root.GetNode<Button>("MainMenu/Center/VBox/QuitBtn").Pressed += () => OnQuit?.Invoke();

		root.GetNode<Button>("SettingsPanel/VBox/CloseBtn").Pressed += CloseSettings;
		_settingBackToMenuBtn.Pressed += () => OnBackToMenu?.Invoke();
	}

	/// <summary>切换到主菜单画面。</summary>
	public void ShowMainMenu(bool hasSave)
	{
		CurrentScreen = Screen.MainMenu;
		_mainMenu.Visible = true;
		_gameUI.Visible = false;
		_settingsPanel.Visible = false;
		SettingsOpen = false;
		_continueBtn.Visible = hasSave;
	}

	/// <summary>切换到游戏画面。</summary>
	public void EnterGame()
	{
		CurrentScreen = Screen.InGame;
		_mainMenu.Visible = false;
		_gameUI.Visible = true;
	}

	/// <summary>ESC 键：游戏中切换设置面板。</summary>
	public void ToggleSettings(bool gameStarted)
	{
		_settingsFromMenu = false;
		SettingsOpen = !SettingsOpen;
		_settingsPanel.Visible = SettingsOpen;
		if (SettingsOpen) UpdateSettingsContext(gameStarted);
	}

	private void OpenSettingsFromMenu()
	{
		_settingsFromMenu = true;
		CurrentScreen = Screen.Settings;
		_settingsPanel.Visible = true;
		_mainMenu.Visible = false;
		UpdateSettingsContext(false);
	}

	private void CloseSettings()
	{
		if (_settingsFromMenu)
		{
			_settingsFromMenu = false;
			_settingsPanel.Visible = false;
			OnBackToMenu?.Invoke();
		}
		else
		{
			SettingsOpen = false;
			_settingsPanel.Visible = false;
		}
	}

	private void UpdateSettingsContext(bool gameStarted)
	{
		var inGame = gameStarted && !_settingsFromMenu;
		_settingSaveBtn.Visible = inGame;
		_settingLoadBtn.Visible = inGame;
		_settingBackToMenuBtn.Visible = inGame;
	}
}
