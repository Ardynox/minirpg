using System;
using Godot;
using MiniRPG.Core.Config;

namespace MiniRPG.Module;

public class MenuModule
{
	public enum Screen { MainMenu, Settings, MultiplayerHub, InGame }

	private readonly PanelContainer _mainMenu;
	private readonly VBoxContainer _gameUI;
	private readonly Button _continueBtn;
	private readonly Button _worldsBtn;
	private readonly Button _multiplayerBtn;
	private readonly Button _mapEditorBtn;
	private readonly Button _weatherLabBtn;
	private readonly Button _autoTestBtn;

	public Screen CurrentScreen { get; private set; } = Screen.MainMenu;
	public bool InMenu => CurrentScreen != Screen.InGame;

	public event Action? OnContinue;
	public event Action? OnWorlds;
	public event Action? OnMapEditor;
	public event Action? OnMultiplayer;
	public event Action? OnWeatherLab;
	public event Action? OnQuit;
	public event Action? OnAutoTest;
	public event Action? OnOpenSettings;

	public MenuModule(Node root)
	{
		_mainMenu = root.GetNode<PanelContainer>("OverlayLayer/MainMenu");
		_gameUI = root.GetNode<VBoxContainer>("HudLayer/UI");
		_continueBtn = root.GetNode<Button>("OverlayLayer/MainMenu/Content/Center/VBox/ContinueBtn");
		_worldsBtn = root.GetNode<Button>("OverlayLayer/MainMenu/Content/Center/VBox/WorldsBtn");
		_multiplayerBtn = root.GetNode<Button>("OverlayLayer/MainMenu/Content/Center/VBox/MultiplayerBtn");
		_mapEditorBtn = root.GetNode<Button>("OverlayLayer/MainMenu/Content/Center/VBox/MapEditorBtn");
		_weatherLabBtn = root.GetNode<Button>("OverlayLayer/MainMenu/Content/Center/VBox/WeatherLabBtn");
		_autoTestBtn = root.GetNode<Button>("OverlayLayer/MainMenu/Content/Center/VBox/AutoTestBtn");

		_continueBtn.Pressed += () => OnContinue?.Invoke();
		_worldsBtn.Pressed += () => OnWorlds?.Invoke();
		_multiplayerBtn.Pressed += () => OnMultiplayer?.Invoke();
		_mapEditorBtn.Pressed += () => OnMapEditor?.Invoke();
		_weatherLabBtn.Pressed += () => OnWeatherLab?.Invoke();
		root.GetNode<Button>("OverlayLayer/MainMenu/Content/Center/VBox/SettingsBtn").Pressed += () => OnOpenSettings?.Invoke();
		_autoTestBtn.Pressed += () => OnAutoTest?.Invoke();
		root.GetNode<Button>("OverlayLayer/MainMenu/Content/Center/VBox/QuitBtn").Pressed += () => OnQuit?.Invoke();
	}

	public void ShowMainMenu(bool canContinue, bool resourcesReady, string? continueButtonText = null)
	{
		CurrentScreen = Screen.MainMenu;
		_mainMenu.Visible = true;
		_gameUI.Visible = false;
		RefreshMainMenuState(canContinue, resourcesReady, continueButtonText);
	}

	public void RefreshMainMenuState(bool canContinue, bool resourcesReady, string? continueButtonText = null)
	{
		_continueBtn.Visible = true;
		_continueBtn.Disabled = !canContinue || !resourcesReady;
		_continueBtn.Text = string.IsNullOrWhiteSpace(continueButtonText)
			? LocalizationService.T("ui.main_menu.continue")
			: continueButtonText;
		_worldsBtn.Disabled = !resourcesReady;
		_multiplayerBtn.Disabled = !resourcesReady;
		_mapEditorBtn.Disabled = !resourcesReady;
		_weatherLabBtn.Disabled = !resourcesReady;
		_autoTestBtn.Disabled = !resourcesReady;
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

	public void ShowMultiplayerHub()
	{
		CurrentScreen = Screen.MultiplayerHub;
		_mainMenu.Visible = false;
		_gameUI.Visible = false;
	}
}
