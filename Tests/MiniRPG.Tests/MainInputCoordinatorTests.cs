using System.Collections.Generic;
using MiniRPG.Module;
using Xunit;

namespace MiniRPG.Tests;

public sealed class MainInputCoordinatorTests
{
	[Fact]
	public void HandleUnhandledKey_BusyOperation_SwallowsAndMarksHandled()
	{
		var harness = new CoordinatorHarness();

		var handled = harness.Coordinator.HandleUnhandledKey(
			key: null,
			CreateSnapshot(
				busyOperationActive: true,
				blocksGameplayInput: true,
				allowPanelChrome: false,
				allowPanelDrag: false,
				pausesGameplayLoop: true),
			keyPressed: true);

		Assert.True(handled);
		Assert.Equal(1, harness.MarkHandledCount);
		Assert.DoesNotContain("panel_key", harness.Calls);
		Assert.DoesNotContain("input_key", harness.Calls);
	}

	[Fact]
	public void HandleUnhandledKey_VisibleModal_HandlesBeforeGameplay()
	{
		var harness = new CoordinatorHarness();
		harness.ModalA.Visible = true;
		harness.ModalA.HandleKeyResult = true;

		var handled = harness.Coordinator.HandleUnhandledKey(
			key: null,
			CreateSnapshot(hasVisibleModalLayer: true, blocksGameplayInput: true),
			keyPressed: true);

		Assert.True(handled);
		Assert.Equal(["modal_key:A", "mark"], harness.Calls);
		Assert.Equal(1, harness.MarkHandledCount);
	}

	[Fact]
	public void HandleUnhandledKey_VisibleModalWithoutConsume_StillBlocksFallthrough()
	{
		var harness = new CoordinatorHarness();
		harness.ModalA.Visible = true;

		var handled = harness.Coordinator.HandleUnhandledKey(
			key: null,
			CreateSnapshot(hasVisibleModalLayer: true, blocksGameplayInput: true),
			keyPressed: true);

		Assert.True(handled);
		Assert.Equal(["modal_key:A"], harness.Calls);
		Assert.Equal(0, harness.MarkHandledCount);
	}

	[Fact]
	public void HandleUnhandledKey_PanelManagerRunsBeforeInputModule()
	{
		var harness = new CoordinatorHarness
		{
			PanelManagerResult = false,
			InputModuleResult = true,
		};

		var handled = harness.Coordinator.HandleUnhandledKey(key: null, CreateSnapshot(), keyPressed: true);

		Assert.True(handled);
		Assert.Equal(1, harness.MarkHandledCount);
		Assert.True(harness.Calls.IndexOf("panel_key") < harness.Calls.IndexOf("input_key"));
	}

	[Fact]
	public void HandleUnhandledKey_MenuWithoutSettingsOverlay_BlocksGameplayRouting()
	{
		var harness = new CoordinatorHarness();

		var handled = harness.Coordinator.HandleUnhandledKey(
			key: null,
			CreateSnapshot(
				inMenu: true,
				settingsOverlayVisible: false,
				blocksGameplayInput: true),
			keyPressed: true);

		Assert.True(handled);
		Assert.DoesNotContain("panel_key", harness.Calls);
		Assert.DoesNotContain("input_key", harness.Calls);
		Assert.Equal(0, harness.MarkHandledCount);
	}

	[Fact]
	public void HandleUnhandledKey_MapEditorPressedKey_MarksHandledEvenWhenDelegateReturnsFalse()
	{
		var harness = new CoordinatorHarness
		{
			MapEditorKeyResult = false,
		};

		var handled = harness.Coordinator.HandleUnhandledKey(
			key: null,
			CreateSnapshot(
				mapEditorActive: true,
				allowPanelChrome: false,
				allowPanelDrag: false,
				blocksGameplayInput: true),
			keyPressed: true);

		Assert.True(handled);
		Assert.Contains("map_key", harness.Calls);
		Assert.Equal(1, harness.MarkHandledCount);
		Assert.DoesNotContain("panel_key", harness.Calls);
	}

	[Fact]
	public void HandleInput_SkipsPanelChromeAndDragWhenSnapshotDisallows()
	{
		var harness = new CoordinatorHarness();

		var handled = harness.Coordinator.HandleInput(
			@event: null,
			CreateSnapshot(
				mapEditorActive: true,
				allowPanelChrome: false,
				allowPanelDrag: false,
				blocksGameplayInput: true),
			isKeyEvent: false);

		Assert.True(handled);
		Assert.DoesNotContain("panel_chrome", harness.Calls);
		Assert.DoesNotContain("panel_drag", harness.Calls);
		Assert.Contains("map_input", harness.Calls);
	}

	[Fact]
	public void HandleInput_ReachesGameplayLaneOnlyAfterEarlierLanesPass()
	{
		var harness = new CoordinatorHarness
		{
			GameplayInputResult = true,
		};

		var handled = harness.Coordinator.HandleInput(@event: null, CreateSnapshot(), isKeyEvent: false);

		Assert.True(handled);
		Assert.Equal(1, harness.MarkHandledCount);
		Assert.True(harness.Calls.IndexOf("panel_chrome") < harness.Calls.IndexOf("gameplay_input"));
		Assert.True(harness.Calls.IndexOf("panel_drag") < harness.Calls.IndexOf("gameplay_input"));
	}

	private static RuntimeUiModeSnapshot CreateSnapshot(
		bool busyOperationActive = false,
		bool inMenu = false,
		bool sessionStarted = true,
		bool layoutEditActive = false,
		bool mapEditorActive = false,
		bool settingsOverlayVisible = false,
		bool hasVisibleModalLayer = false,
		bool? allowPanelChrome = null,
		bool? allowPanelDrag = null,
		bool? blocksGameplayInput = null,
		bool suppressHudAndAlerts = false,
		bool pausesGameplayLoop = false) =>
		new(
			BusyOperationActive: busyOperationActive,
			InMenu: inMenu,
			SessionStarted: sessionStarted,
			LayoutEditActive: layoutEditActive,
			MapEditorActive: mapEditorActive,
			SettingsOverlayVisible: settingsOverlayVisible,
			HasVisibleModalLayer: hasVisibleModalLayer,
			AllowPanelChrome: allowPanelChrome ?? (!busyOperationActive && !layoutEditActive && !mapEditorActive && !hasVisibleModalLayer),
			AllowPanelDrag: allowPanelDrag ?? (!busyOperationActive && !mapEditorActive && !hasVisibleModalLayer),
			BlocksGameplayInput: blocksGameplayInput ?? (busyOperationActive || inMenu || layoutEditActive || mapEditorActive || hasVisibleModalLayer),
			SuppressHudAndAlerts: suppressHudAndAlerts,
			PausesGameplayLoop: pausesGameplayLoop);

	private sealed class CoordinatorHarness
	{
		public CoordinatorHarness()
		{
			ModalA = new FakeModalLayer("A", Calls);
			ModalB = new FakeModalLayer("B", Calls);
			Coordinator = new MainInputCoordinator(
				[ModalA, ModalB],
				() =>
				{
					Calls.Add("mark");
					MarkHandledCount++;
				},
				_ =>
				{
					Calls.Add("panel_chrome");
					return PanelChromeResult;
				},
				_ =>
				{
					Calls.Add("panel_drag");
					return PanelDragResult;
				},
				_ =>
				{
					Calls.Add("layout_key");
					return LayoutEditKeyResult;
				},
				_ =>
				{
					Calls.Add("layout_input");
					return LayoutEditInputResult;
				},
				_ =>
				{
					Calls.Add("map_key");
					return MapEditorKeyResult;
				},
				_ =>
				{
					Calls.Add("map_input");
					return MapEditorInputResult;
				},
				_ =>
				{
					Calls.Add("inspect_key");
					return InspectKeyResult;
				},
				_ =>
				{
					Calls.Add("panel_key");
					return PanelManagerResult;
				},
				_ =>
				{
					Calls.Add("input_key");
					return InputModuleResult;
				},
				(_, _) =>
				{
					Calls.Add("gameplay_input");
					return GameplayInputResult;
				});
		}

		public List<string> Calls { get; } = [];
		public FakeModalLayer ModalA { get; }
		public FakeModalLayer ModalB { get; }
		public MainInputCoordinator Coordinator { get; }
		public int MarkHandledCount { get; private set; }
		public bool PanelChromeResult { get; init; }
		public bool PanelDragResult { get; init; }
		public bool LayoutEditKeyResult { get; init; }
		public bool LayoutEditInputResult { get; init; }
		public bool MapEditorKeyResult { get; init; }
		public bool MapEditorInputResult { get; init; }
		public bool InspectKeyResult { get; init; }
		public bool PanelManagerResult { get; init; }
		public bool InputModuleResult { get; init; }
		public bool GameplayInputResult { get; init; }
	}

	private sealed class FakeModalLayer(string name, List<string> calls) : IModalInputLayer
	{
		private readonly string _name = name;
		private readonly List<string> _calls = calls;

		public bool Visible { get; set; }
		public bool HandleKeyResult { get; set; }
		public bool HandleMouseResult { get; set; }

		public void Close() => Visible = false;

		public bool HandleKeyInput(Godot.InputEventKey key)
		{
			_calls.Add($"modal_key:{_name}");
			return HandleKeyResult;
		}

		public bool HandleMouseInput(Godot.InputEvent @event)
		{
			_calls.Add($"modal_mouse:{_name}");
			return HandleMouseResult;
		}
	}
}
