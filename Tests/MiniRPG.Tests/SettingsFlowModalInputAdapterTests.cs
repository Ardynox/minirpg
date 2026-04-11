using System;
using MiniRPG.Module.Panel;
using Xunit;

namespace MiniRPG.Tests;

public sealed class SettingsFlowModalInputAdapterTests
{
	[Fact]
	public void HandleInput_ReturnsFalse_WhenNoOverlayIsVisible()
	{
		var harness = new Harness();

		Assert.False(harness.Adapter.Visible);
		Assert.False(harness.Adapter.HandleKeyInputCore(
			harness.SettingsOverlay.HandleKey,
			harness.HandlePanelKey));
		Assert.False(harness.Adapter.HandleMouseInputCore(
			harness.SettingsOverlay.HandleMouse,
			harness.CloseFocusedPanel,
			isPressedRightClick: true));
		Assert.Equal(0, harness.SettingsOverlay.HandleKeyCalls);
		Assert.Equal(0, harness.SettingsOverlay.HandleMouseCalls);
		Assert.Equal(0, harness.PanelKeyCalls);
		Assert.Equal(0, harness.CloseFocusedCalls);
		Assert.Equal(0, harness.FlushMapCalls);
	}

	[Fact]
	public void HandleKeyInput_UsesSettingsOverlayBeforePanelManager()
	{
		var harness = new Harness();
		harness.SettingsOverlay.HandleKeyResult = true;
		harness.OpenSettingsOverlay();

		var handled = harness.Adapter.HandleKeyInputCore(
			harness.SettingsOverlay.HandleKey,
			harness.HandlePanelKey);

		Assert.True(handled);
		Assert.Equal(1, harness.SettingsOverlay.HandleKeyCalls);
		Assert.Equal(0, harness.PanelKeyCalls);
	}

	[Fact]
	public void HandleKeyInput_FallsBackToPanelManager_WhenSettingsOverlayDoesNotConsume()
	{
		var harness = new Harness
		{
			PanelKeyResult = true,
		};
		harness.OpenSettingsOverlay();

		var handled = harness.Adapter.HandleKeyInputCore(
			harness.SettingsOverlay.HandleKey,
			harness.HandlePanelKey);

		Assert.True(handled);
		Assert.Equal(1, harness.SettingsOverlay.HandleKeyCalls);
		Assert.Equal(1, harness.PanelKeyCalls);
	}

	[Fact]
	public void HandleMouseInput_RightClickFlushesOnlyWhenFocusedPanelCloses()
	{
		var harness = new Harness
		{
			CloseFocusedResult = true,
		};
		harness.OpenSettingsOverlay();

		var handled = harness.Adapter.HandleMouseInputCore(
			harness.SettingsOverlay.HandleMouse,
			harness.CloseFocusedPanel,
			isPressedRightClick: true);

		Assert.True(handled);
		Assert.Equal(1, harness.SettingsOverlay.HandleMouseCalls);
		Assert.Equal(1, harness.CloseFocusedCalls);
		Assert.Equal(1, harness.FlushMapCalls);
	}

	[Fact]
	public void HandleMouseInput_DoesNotFlushForNonRightClick()
	{
		var harness = new Harness
		{
			CloseFocusedResult = true,
		};
		harness.OpenSettingsOverlay();

		var handled = harness.Adapter.HandleMouseInputCore(
			harness.SettingsOverlay.HandleMouse,
			harness.CloseFocusedPanel,
			isPressedRightClick: false);

		Assert.False(handled);
		Assert.Equal(1, harness.SettingsOverlay.HandleMouseCalls);
		Assert.Equal(0, harness.CloseFocusedCalls);
		Assert.Equal(0, harness.FlushMapCalls);
	}

	[Fact]
	public void HandleMouseInput_DoesNotFlushWhenNoFocusedPanelCloses()
	{
		var harness = new Harness();
		harness.OpenSettingsOverlay();

		var handled = harness.Adapter.HandleMouseInputCore(
			harness.SettingsOverlay.HandleMouse,
			harness.CloseFocusedPanel,
			isPressedRightClick: true);

		Assert.False(handled);
		Assert.Equal(1, harness.SettingsOverlay.HandleMouseCalls);
		Assert.Equal(1, harness.CloseFocusedCalls);
		Assert.Equal(0, harness.FlushMapCalls);
	}

	private sealed class Harness
	{
		private readonly FakeFocusHost _focusHost = new();
		private readonly FakePauseMenuOverlay _pauseOverlay = new();

		public Harness()
		{
			SettingsOverlay = new FakeSettingsOverlay();
			SettingsFlow = new SettingsFlowCoordinator(_focusHost, _pauseOverlay, SettingsOverlay);
			SettingsFlow.ApplyState(new SettingsUiState(
				SettingsEntryContext.InGamePause,
				"en",
				RenderReady: true,
				WatchModeEnabled: false,
				FastTurnModeEnabled: false,
				MapEditorActive: false,
				CanOpenSessionTab: true,
				EnableKeyboardTargeting: false,
				EnableDebugPanel: true,
				CanOpenWeatherLab: false,
				WeatherLabPanelOpen: false,
				MapZoomMin: 0.6f,
				MapZoomMax: 2.4f,
				MapZoomCurrent: 1.0f));

			Adapter = new SettingsFlowModalInputAdapter(SettingsFlow, new PanelManager(), () => FlushMapCalls++);
		}

		public SettingsFlowCoordinator SettingsFlow { get; }
		public FakeSettingsOverlay SettingsOverlay { get; }
		public SettingsFlowModalInputAdapter Adapter { get; }
		public int FlushMapCalls { get; private set; }
		public int PanelKeyCalls { get; private set; }
		public int CloseFocusedCalls { get; private set; }
		public bool PanelKeyResult { get; set; }
		public bool CloseFocusedResult { get; set; }

		public void OpenSettingsOverlay() => SettingsFlow.OpenSettings(SettingsEntryContext.InGamePause, SettingsTab.General);

		public bool HandlePanelKey()
		{
			PanelKeyCalls++;
			return PanelKeyResult;
		}

		public bool CloseFocusedPanel()
		{
			CloseFocusedCalls++;
			return CloseFocusedResult;
		}
	}

	private sealed class FakePauseMenuOverlay : IPauseMenuOverlay
	{
		public bool Visible { get; private set; }

		public event Action<PauseMenuAction>? ActionRequested
		{
			add { }
			remove { }
		}

		public void Open() => Visible = true;

		public void Close() => Visible = false;

		public void RefreshTexts()
		{
		}
	}

	private sealed class FakeSettingsOverlay : ISettingsOverlay
	{
		public bool Visible { get; private set; }
		public SettingsEntryContext Context { get; private set; } = SettingsEntryContext.MainMenu;
		public bool IsCapturingKeyBindings { get; set; }
		public bool HandleKeyResult { get; set; }
		public bool HandleMouseResult { get; set; }
		public int HandleKeyCalls { get; private set; }
		public int HandleMouseCalls { get; private set; }

		public event Action? BackRequested
		{
			add { }
			remove { }
		}
		public event Action? RenderToggleRequested
		{
			add { }
			remove { }
		}
		public event Action? WatchModeToggleRequested
		{
			add { }
			remove { }
		}
		public event Action? FastTurnModeToggleRequested
		{
			add { }
			remove { }
		}
		public event Action? KeyboardTargetingToggleRequested
		{
			add { }
			remove { }
		}
		public event Action? DebugPanelToggleRequested
		{
			add { }
			remove { }
		}
		public event Action? MapZoomMinDecreaseRequested
		{
			add { }
			remove { }
		}
		public event Action? MapZoomMinIncreaseRequested
		{
			add { }
			remove { }
		}
		public event Action? MapZoomMaxDecreaseRequested
		{
			add { }
			remove { }
		}
		public event Action? MapZoomMaxIncreaseRequested
		{
			add { }
			remove { }
		}
		public event Action? SaveRequested
		{
			add { }
			remove { }
		}
		public event Action? LoadRequested
		{
			add { }
			remove { }
		}
		public event Action? WeatherLabToggleRequested
		{
			add { }
			remove { }
		}
		public event Action? MapEditorToggleRequested
		{
			add { }
			remove { }
		}
		public event Action? LayoutEditRequested
		{
			add { }
			remove { }
		}
		public event Action<string>? LanguageChangedRequested
		{
			add { }
			remove { }
		}

		public void Open(SettingsEntryContext context, SettingsTab initialTab)
		{
			Context = context;
			Visible = true;
		}

		public void Close() => Visible = false;

		public void ApplyState(SettingsUiState state) => Context = state.Context;

		public void RefreshTexts()
		{
		}

		public bool HandleKey()
		{
			HandleKeyCalls++;
			return HandleKeyResult;
		}

		public bool HandleMouse()
		{
			HandleMouseCalls++;
			return HandleMouseResult;
		}

		public bool HandleKeyInput(Godot.InputEventKey key) => throw new NotSupportedException();

		public bool HandleMouseInput(Godot.InputEvent @event) => throw new NotSupportedException();
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
