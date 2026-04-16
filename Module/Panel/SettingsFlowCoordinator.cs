using System;
using MiniRPG.Core.Config;

namespace MiniRPG.Module.Panel;

public enum SettingsEntryContext
{
	MainMenu,
	InGamePause,
}

public enum SettingsTab
{
	General,
	Controls,
	Session,
}

public enum PauseMenuAction
{
	Resume,
	QuickSave,
	QuickLoad,
	OpenSettings,
	OpenMultiplayerRoom,
	ReturnToMenu,
}

public readonly record struct SettingsUiState(
	SettingsEntryContext Context,
	string CurrentLocale,
	bool RenderReady,
	bool WatchModeEnabled,
	bool FastTurnModeEnabled,
	bool MapEditorActive,
	bool CanOpenSessionTab,
	bool EnableKeyboardTargeting,
	bool EnableDebugPanel,
	float MapZoomMin,
	float MapZoomMax,
	float MapZoomCurrent,
	float UiFontScale,
	bool HighContrastEnabled,
	string ColorBlindMode,
	AutoNavigationInterruptPolicy AutoNavigationInterruptPolicy);

public interface ISettingsFlowPanel
{
	bool Visible { get; }
}

public interface ISettingsFlowFocusHost
{
	bool IsFocused(ISettingsFlowPanel panel);
	void SetFocus(ISettingsFlowPanel? panel);
	void PushFocus(ISettingsFlowPanel? panel);
	void OnPanelClosed(ISettingsFlowPanel panel);
}

public sealed class PanelManagerSettingsFlowFocusHost(PanelManager panels) : ISettingsFlowFocusHost
{
	public bool IsFocused(ISettingsFlowPanel panel) =>
		ReferenceEquals(panels.Focused, panel);

	public void SetFocus(ISettingsFlowPanel? panel) =>
		panels.SetFocus(panel as IPanel);

	public void PushFocus(ISettingsFlowPanel? panel) =>
		panels.PushFocus(panel as IPanel);

	public void OnPanelClosed(ISettingsFlowPanel panel)
	{
		if (panel is IPanel focusablePanel)
			panels.OnPanelClosed(focusablePanel);
	}
}

public interface IPauseMenuOverlay : ISettingsFlowPanel
{
	void Open();
	void Close();
	void RefreshTexts();
	event Action<PauseMenuAction>? ActionRequested;
}

public interface ISettingsOverlay : ISettingsFlowPanel
{
	SettingsEntryContext Context { get; }
	bool IsCapturingKeyBindings { get; }
	void Open(SettingsEntryContext context, SettingsTab initialTab);
	void Close();
	void ApplyState(SettingsUiState state);
	void RefreshTexts();
	bool HandleKeyInput(Godot.InputEventKey key);
	bool HandleMouseInput(Godot.InputEvent @event);
	event Action? BackRequested;
	event Action? RenderToggleRequested;
	event Action? WatchModeToggleRequested;
	event Action? FastTurnModeToggleRequested;
	event Action? KeyboardTargetingToggleRequested;
	event Action? AutoNavigationInterruptPolicyCycleRequested;
	event Action? DebugPanelToggleRequested;
	event Action? MapZoomMinDecreaseRequested;
	event Action? MapZoomMinIncreaseRequested;
	event Action? MapZoomMaxDecreaseRequested;
	event Action? MapZoomMaxIncreaseRequested;
	event Action? UiFontScaleDecreaseRequested;
	event Action? UiFontScaleIncreaseRequested;
	event Action? HighContrastToggleRequested;
	event Action<string>? ColorBlindModeChangeRequested;
	event Action? SaveRequested;
	event Action? LoadRequested;
	event Action? MapEditorToggleRequested;
	event Action? LayoutEditRequested;
	event Action<string>? LanguageChangedRequested;
}

public sealed class SettingsFlowCoordinator
{
	private readonly ISettingsFlowFocusHost _focusHost;
	private readonly IPauseMenuOverlay _pauseMenu;
	private readonly ISettingsOverlay _settings;
	private SettingsUiState _state;

	public bool PauseMenuVisible => _pauseMenu.Visible;
	public bool SettingsVisible => _settings.Visible;
	public bool HasVisibleOverlay => PauseMenuVisible || SettingsVisible;
	public bool IsCapturingKeyBindings => SettingsVisible && _settings.IsCapturingKeyBindings;

	public event Action? QuickSaveRequested;
	public event Action? QuickLoadRequested;
	public event Action? MultiplayerRoomRequested;
	public event Action? ReturnToMenuRequested;
	public event Action? MainMenuRestoreRequested;
	public event Action? RenderToggleRequested;
	public event Action? WatchModeToggleRequested;
	public event Action? FastTurnModeToggleRequested;
	public event Action? KeyboardTargetingToggleRequested;
	public event Action? AutoNavigationInterruptPolicyCycleRequested;
	public event Action? DebugPanelToggleRequested;
	public event Action? MapZoomMinDecreaseRequested;
	public event Action? MapZoomMinIncreaseRequested;
	public event Action? MapZoomMaxDecreaseRequested;
	public event Action? MapZoomMaxIncreaseRequested;
	public event Action? UiFontScaleDecreaseRequested;
	public event Action? UiFontScaleIncreaseRequested;
	public event Action? HighContrastToggleRequested;
	public event Action<string>? ColorBlindModeChangeRequested;
	public event Action? SaveRequested;
	public event Action? LoadRequested;
	public event Action? MapEditorToggleRequested;
	public event Action? LayoutEditRequested;
	public event Action<string>? LanguageChangedRequested;

	public SettingsFlowCoordinator(
		ISettingsFlowFocusHost focusHost,
		IPauseMenuOverlay pauseMenu,
		ISettingsOverlay settings)
	{
		_focusHost = focusHost;
		_pauseMenu = pauseMenu;
		_settings = settings;
		_state = new SettingsUiState(
			SettingsEntryContext.MainMenu,
			LocalizationService.CurrentLocale,
			RenderReady: false,
			WatchModeEnabled: false,
			FastTurnModeEnabled: true,
			MapEditorActive: false,
			CanOpenSessionTab: false,
			EnableKeyboardTargeting: false,
			EnableDebugPanel: true,
			MapZoomMin: 0.6f,
			MapZoomMax: 2.4f,
			MapZoomCurrent: 1.0f,
			UiFontScale: 1.0f,
			HighContrastEnabled: false,
			ColorBlindMode: "none",
			AutoNavigationInterruptPolicy: AutoNavigationInterruptPolicy.ConservativeStop);

		_pauseMenu.ActionRequested += HandlePauseAction;
		_settings.BackRequested += CloseActiveOverlay;
		_settings.RenderToggleRequested += () => RenderToggleRequested?.Invoke();
		_settings.WatchModeToggleRequested += () => WatchModeToggleRequested?.Invoke();
		_settings.FastTurnModeToggleRequested += () => FastTurnModeToggleRequested?.Invoke();
		_settings.KeyboardTargetingToggleRequested += () => KeyboardTargetingToggleRequested?.Invoke();
		_settings.AutoNavigationInterruptPolicyCycleRequested += () => AutoNavigationInterruptPolicyCycleRequested?.Invoke();
		_settings.DebugPanelToggleRequested += () => DebugPanelToggleRequested?.Invoke();
		_settings.MapZoomMinDecreaseRequested += () => MapZoomMinDecreaseRequested?.Invoke();
		_settings.MapZoomMinIncreaseRequested += () => MapZoomMinIncreaseRequested?.Invoke();
		_settings.MapZoomMaxDecreaseRequested += () => MapZoomMaxDecreaseRequested?.Invoke();
		_settings.MapZoomMaxIncreaseRequested += () => MapZoomMaxIncreaseRequested?.Invoke();
		_settings.UiFontScaleDecreaseRequested += () => UiFontScaleDecreaseRequested?.Invoke();
		_settings.UiFontScaleIncreaseRequested += () => UiFontScaleIncreaseRequested?.Invoke();
		_settings.HighContrastToggleRequested += () => HighContrastToggleRequested?.Invoke();
		_settings.ColorBlindModeChangeRequested += mode => ColorBlindModeChangeRequested?.Invoke(mode);
		_settings.SaveRequested += () => SaveRequested?.Invoke();
		_settings.LoadRequested += () => LoadRequested?.Invoke();
		_settings.MapEditorToggleRequested += () => MapEditorToggleRequested?.Invoke();
		_settings.LayoutEditRequested += () => LayoutEditRequested?.Invoke();
		_settings.LanguageChangedRequested += locale => LanguageChangedRequested?.Invoke(locale);
	}

	public void OpenPauseMenu()
	{
		if (_pauseMenu.Visible)
			return;

		_pauseMenu.Open();
		_focusHost.PushFocus(_pauseMenu);
	}

	public void OpenSettings(SettingsEntryContext context, SettingsTab initialTab)
	{
		_state = _state with { Context = context };
		_settings.ApplyState(_state);
		_settings.Open(context, initialTab);

		if (context == SettingsEntryContext.MainMenu)
		{
			if (_pauseMenu.Visible)
			{
				_pauseMenu.Close();
				_focusHost.OnPanelClosed(_pauseMenu);
			}

			_focusHost.SetFocus(_settings);
			return;
		}

		if (!_pauseMenu.Visible)
		{
			_pauseMenu.Open();
			_focusHost.PushFocus(_pauseMenu);
		}

		if (!_focusHost.IsFocused(_settings))
			_focusHost.PushFocus(_settings);
	}

	public void CloseActiveOverlay()
	{
		if (_settings.Visible)
		{
			var context = _settings.Context;
			_settings.Close();
			_focusHost.OnPanelClosed(_settings);
			if (context == SettingsEntryContext.MainMenu)
				MainMenuRestoreRequested?.Invoke();
			return;
		}

		if (_pauseMenu.Visible)
		{
			_pauseMenu.Close();
			_focusHost.OnPanelClosed(_pauseMenu);
		}
	}

	public void ApplyState(SettingsUiState state)
	{
		_state = state;
		_settings.ApplyState(state);
	}

	public void RefreshTexts()
	{
		_pauseMenu.RefreshTexts();
		_settings.RefreshTexts();
	}

	public bool HandleKeyInput(Godot.InputEventKey key) =>
		_settings.Visible && _settings.HandleKeyInput(key);

	public bool HandleMouseInput(Godot.InputEvent @event) =>
		_settings.Visible && _settings.HandleMouseInput(@event);

	private void HandlePauseAction(PauseMenuAction action)
	{
		switch (action)
		{
			case PauseMenuAction.Resume:
				CloseActiveOverlay();
				break;
			case PauseMenuAction.OpenSettings:
				OpenSettings(SettingsEntryContext.InGamePause, SettingsTab.General);
				break;
			case PauseMenuAction.QuickSave:
				QuickSaveRequested?.Invoke();
				break;
			case PauseMenuAction.QuickLoad:
				QuickLoadRequested?.Invoke();
				break;
			case PauseMenuAction.OpenMultiplayerRoom:
				MultiplayerRoomRequested?.Invoke();
				break;
			case PauseMenuAction.ReturnToMenu:
				ReturnToMenuRequested?.Invoke();
				break;
		}
	}
}
