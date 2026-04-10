using System;
using MiniRPG.Module.Panel;
using Xunit;

namespace MiniRPG.Tests;

public sealed class SettingsFlowCoordinatorTests
{
	[Fact]
	public void OpenSettings_FromMainMenu_BackRestoresMainMenu()
	{
		var focusHost = new FakeFocusHost();
		var pause = new FakePauseMenuOverlay();
		var settings = new FakeSettingsOverlay();

		var coordinator = new SettingsFlowCoordinator(focusHost, pause, settings);
		var restoreRequested = 0;
		coordinator.MainMenuRestoreRequested += () => restoreRequested++;
		coordinator.ApplyState(new SettingsUiState(
			SettingsEntryContext.MainMenu,
			"en",
			RenderReady: true,
			WatchModeEnabled: false,
			MapEditorActive: false,
			CanOpenSessionTab: false,
			EnableKeyboardTargeting: false,
			EnableDebugPanel: true,
			CanOpenWeatherLab: false,
			WeatherLabPanelOpen: false,
			MapZoomMin: 0.6f,
			MapZoomMax: 2.4f,
			MapZoomCurrent: 1.0f));

		coordinator.OpenSettings(SettingsEntryContext.MainMenu, SettingsTab.Controls);

		Assert.False(pause.Visible);
		Assert.True(settings.Visible);
		Assert.Equal(SettingsEntryContext.MainMenu, settings.Context);
		Assert.Equal(SettingsTab.Controls, settings.LastInitialTab);
		Assert.Same(settings, focusHost.Focused);

		settings.TriggerBack();

		Assert.False(settings.Visible);
		Assert.Equal(1, restoreRequested);
		Assert.Null(focusHost.Focused);
	}

	[Fact]
	public void OpenPauseMenu_ThenSettings_BackReturnsToPauseMenu()
	{
		var focusHost = new FakeFocusHost();
		var pause = new FakePauseMenuOverlay();
		var settings = new FakeSettingsOverlay();

		var coordinator = new SettingsFlowCoordinator(focusHost, pause, settings);
		coordinator.ApplyState(new SettingsUiState(
			SettingsEntryContext.InGamePause,
			"en",
			RenderReady: true,
			WatchModeEnabled: true,
			MapEditorActive: false,
			CanOpenSessionTab: true,
			EnableKeyboardTargeting: true,
			EnableDebugPanel: true,
			CanOpenWeatherLab: false,
			WeatherLabPanelOpen: false,
			MapZoomMin: 0.6f,
			MapZoomMax: 2.4f,
			MapZoomCurrent: 1.0f));

		coordinator.OpenPauseMenu();

		Assert.True(pause.Visible);
		Assert.False(settings.Visible);
		Assert.Same(pause, focusHost.Focused);

		pause.TriggerAction(PauseMenuAction.OpenSettings);

		Assert.True(pause.Visible);
		Assert.True(settings.Visible);
		Assert.Equal(SettingsEntryContext.InGamePause, settings.Context);
		Assert.Equal(SettingsTab.General, settings.LastInitialTab);
		Assert.Same(settings, focusHost.Focused);

		settings.TriggerBack();

		Assert.True(pause.Visible);
		Assert.False(settings.Visible);
		Assert.Same(pause, focusHost.Focused);

		coordinator.CloseActiveOverlay();

		Assert.False(pause.Visible);
		Assert.Null(focusHost.Focused);
	}

	[Fact]
	public void WeatherLabToggle_ForwardedFromSettingsOverlay()
	{
		var focusHost = new FakeFocusHost();
		var pause = new FakePauseMenuOverlay();
		var settings = new FakeSettingsOverlay();
		var coordinator = new SettingsFlowCoordinator(focusHost, pause, settings);
		var toggleCount = 0;
		coordinator.WeatherLabToggleRequested += () => toggleCount++;

		coordinator.ApplyState(new SettingsUiState(
			SettingsEntryContext.InGamePause,
			"en",
			RenderReady: true,
			WatchModeEnabled: false,
			MapEditorActive: false,
			CanOpenSessionTab: true,
			EnableKeyboardTargeting: false,
			EnableDebugPanel: true,
			CanOpenWeatherLab: true,
			WeatherLabPanelOpen: false,
			MapZoomMin: 0.6f,
			MapZoomMax: 2.4f,
			MapZoomCurrent: 1.0f));

		settings.TriggerWeatherLabToggle();

		Assert.Equal(1, toggleCount);
	}

	[Fact]
	public void DebugPanelToggle_ForwardedFromSettingsOverlay()
	{
		var focusHost = new FakeFocusHost();
		var pause = new FakePauseMenuOverlay();
		var settings = new FakeSettingsOverlay();
		var coordinator = new SettingsFlowCoordinator(focusHost, pause, settings);
		var toggleCount = 0;
		coordinator.DebugPanelToggleRequested += () => toggleCount++;

		settings.TriggerDebugPanelToggle();

		Assert.Equal(1, toggleCount);
	}

	[Fact]
	public void MultiplayerRoomAction_ForwardedFromPauseMenu()
	{
		var focusHost = new FakeFocusHost();
		var pause = new FakePauseMenuOverlay();
		var settings = new FakeSettingsOverlay();
		var coordinator = new SettingsFlowCoordinator(focusHost, pause, settings);
		var requestCount = 0;
		coordinator.MultiplayerRoomRequested += () => requestCount++;

		coordinator.OpenPauseMenu();
		pause.TriggerAction(PauseMenuAction.OpenMultiplayerRoom);

		Assert.Equal(1, requestCount);
	}

	private sealed class FakePauseMenuOverlay : IPauseMenuOverlay
	{
		public bool Visible { get; private set; }

		public event Action<PauseMenuAction>? ActionRequested;

		public void Open() => Visible = true;

		public void Close() => Visible = false;

		public void RefreshTexts()
		{
		}

		public void TriggerAction(PauseMenuAction action) => ActionRequested?.Invoke(action);
	}

	private sealed class FakeSettingsOverlay : ISettingsOverlay
	{
		public bool Visible { get; private set; }
		public SettingsEntryContext Context { get; private set; } = SettingsEntryContext.MainMenu;
		public SettingsTab LastInitialTab { get; private set; } = SettingsTab.General;
		public bool IsCapturingKeyBindings { get; set; }

		public event Action? BackRequested;
		public event Action? RenderToggleRequested { add { } remove { } }
		public event Action? WatchModeToggleRequested { add { } remove { } }
		public event Action? KeyboardTargetingToggleRequested { add { } remove { } }
		public event Action? DebugPanelToggleRequested;
		public event Action? MapZoomMinDecreaseRequested { add { } remove { } }
		public event Action? MapZoomMinIncreaseRequested { add { } remove { } }
		public event Action? MapZoomMaxDecreaseRequested { add { } remove { } }
		public event Action? MapZoomMaxIncreaseRequested { add { } remove { } }
		public event Action? SaveRequested { add { } remove { } }
		public event Action? LoadRequested { add { } remove { } }
		public event Action? MapEditorToggleRequested { add { } remove { } }
		public event Action? WeatherLabToggleRequested;
		public event Action? LayoutEditRequested { add { } remove { } }
		public event Action<string>? LanguageChangedRequested { add { } remove { } }

		public void Open(SettingsEntryContext context, SettingsTab initialTab)
		{
			Context = context;
			LastInitialTab = initialTab;
			Visible = true;
		}

		public void Close() => Visible = false;

		public void ApplyState(SettingsUiState state) => Context = state.Context;

		public void RefreshTexts()
		{
		}

		public bool HandleKeyInput(Godot.InputEventKey key) => false;

		public bool HandleMouseInput(Godot.InputEvent @event) => false;

		public void TriggerBack() => BackRequested?.Invoke();

		public void TriggerDebugPanelToggle() => DebugPanelToggleRequested?.Invoke();

		public void TriggerWeatherLabToggle() => WeatherLabToggleRequested?.Invoke();
	}

	private sealed class FakeFocusHost : ISettingsFlowFocusHost
	{
		private readonly System.Collections.Generic.List<ISettingsFlowPanel> _stack = [];

		public ISettingsFlowPanel? Focused { get; private set; }

		public bool IsFocused(ISettingsFlowPanel panel) => ReferenceEquals(Focused, panel);

		public void SetFocus(ISettingsFlowPanel? panel)
		{
			Focused = panel;
			_stack.Clear();
		}

		public void PushFocus(ISettingsFlowPanel? panel)
		{
			if (panel == null || ReferenceEquals(Focused, panel))
				return;

			if (Focused != null)
				_stack.Add(Focused);

			Focused = panel;
		}

		public void OnPanelClosed(ISettingsFlowPanel panel)
		{
			for (var i = _stack.Count - 1; i >= 0; i--)
			{
				if (ReferenceEquals(_stack[i], panel))
					_stack.RemoveAt(i);
			}

			if (!ReferenceEquals(Focused, panel))
				return;

			Focused = null;
			for (var i = _stack.Count - 1; i >= 0; i--)
			{
				if (_stack[i].Visible)
				{
					Focused = _stack[i];
					_stack.RemoveAt(i);
					break;
				}
			}
		}
	}
}
