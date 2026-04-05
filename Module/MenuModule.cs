using System;
using Godot;

namespace MiniRPG.Module;

public class MenuModule
{
	public enum Screen { MainMenu, Settings, InGame }

	private readonly PanelContainer _mainMenu;
	private readonly VBoxContainer _gameUI;
	private readonly Button _continueBtn;

	public Screen CurrentScreen { get; private set; } = Screen.MainMenu;
	public bool InMenu => CurrentScreen != Screen.InGame;

	public event Action? OnContinue;
	public event Action? OnNewGame;
	public event Action? OnLoadGame;
	public event Action? OnMapEditor;
	public event Action? OnQuit;
	public event Action? OnAutoTest;
	public event Action? OnOpenSettings;

	public MenuModule(Node root)
	{
		_mainMenu = root.GetNode<PanelContainer>("MainMenu");
		_gameUI = root.GetNode<VBoxContainer>("UI");
		_continueBtn = root.GetNode<Button>("MainMenu/Center/VBox/ContinueBtn");

		root.GetNode<Button>("MainMenu/Center/VBox/ContinueBtn").Pressed += () => OnContinue?.Invoke();
		root.GetNode<Button>("MainMenu/Center/VBox/NewGameBtn").Pressed += () => OnNewGame?.Invoke();
		root.GetNode<Button>("MainMenu/Center/VBox/MapEditorBtn").Pressed += () => OnMapEditor?.Invoke();
		root.GetNode<Button>("MainMenu/Center/VBox/LoadGameBtn").Pressed += () => OnLoadGame?.Invoke();
		root.GetNode<Button>("MainMenu/Center/VBox/SettingsBtn").Pressed += () => OnOpenSettings?.Invoke();
		root.GetNode<Button>("MainMenu/Center/VBox/AutoTestBtn").Pressed += () => OnAutoTest?.Invoke();
		root.GetNode<Button>("MainMenu/Center/VBox/QuitBtn").Pressed += () => OnQuit?.Invoke();
	}

	public void ShowMainMenu(bool hasSave)
	{
		CurrentScreen = Screen.MainMenu;
		_mainMenu.Visible = true;
		_gameUI.Visible = false;
		_continueBtn.Visible = hasSave;
	}

	public void EnterGame()
	{
		CurrentScreen = Screen.InGame;
		_mainMenu.Visible = false;
		_gameUI.Visible = true;
	}

	public void ShowSettingsFromMenu()
	{
		CurrentScreen = Screen.Settings;
		_mainMenu.Visible = false;
		_gameUI.Visible = false;
	}
}
