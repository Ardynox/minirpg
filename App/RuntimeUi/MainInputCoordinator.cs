using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Module;

namespace MiniRPG;

internal sealed class MainInputCoordinator(
	IReadOnlyList<IModalInputLayer> modalLayers,
	Action markInputHandled,
	Func<InputEvent, bool> handlePanelChromeInput,
	Func<InputEvent, bool> handlePanelDragInput,
	Func<InputEventKey, bool> handleLayoutEditKeyInput,
	Func<InputEvent, bool> handleLayoutEditInput,
	Func<InputEventKey, bool> handleMapEditorKeyInput,
	Func<InputEvent, bool> handleMapEditorInput,
	Func<InputEventKey, bool> handleInspectModeKeyInput,
	Func<InputEventKey, bool> handlePanelManagerKeyInput,
	Func<InputEventKey, bool> handleInputModuleKeyInput,
	Func<InputEvent, RuntimeUiModeSnapshot, bool> handleGameplayMouseInput)
{
	private readonly IReadOnlyList<IModalInputLayer> _modalLayers = modalLayers;
	private readonly Action _markInputHandled = markInputHandled;
	private readonly Func<InputEvent, bool> _handlePanelChromeInput = handlePanelChromeInput;
	private readonly Func<InputEvent, bool> _handlePanelDragInput = handlePanelDragInput;
	private readonly Func<InputEventKey, bool> _handleLayoutEditKeyInput = handleLayoutEditKeyInput;
	private readonly Func<InputEvent, bool> _handleLayoutEditInput = handleLayoutEditInput;
	private readonly Func<InputEventKey, bool> _handleMapEditorKeyInput = handleMapEditorKeyInput;
	private readonly Func<InputEvent, bool> _handleMapEditorInput = handleMapEditorInput;
	private readonly Func<InputEventKey, bool> _handleInspectModeKeyInput = handleInspectModeKeyInput;
	private readonly Func<InputEventKey, bool> _handlePanelManagerKeyInput = handlePanelManagerKeyInput;
	private readonly Func<InputEventKey, bool> _handleInputModuleKeyInput = handleInputModuleKeyInput;
	private readonly Func<InputEvent, RuntimeUiModeSnapshot, bool> _handleGameplayMouseInput = handleGameplayMouseInput;

	public bool HandleUnhandledKey(InputEventKey key, RuntimeUiModeSnapshot snapshot) =>
		HandleUnhandledKey(key, snapshot, key.Pressed);

	internal bool HandleUnhandledKey(InputEventKey? key, RuntimeUiModeSnapshot snapshot, bool keyPressed)
	{
		if (snapshot.BusyOperationActive)
		{
			_markInputHandled();
			return true;
		}

		var modalLayer = GetVisibleModalLayer();
		if (modalLayer != null)
		{
			var handledByModal = modalLayer.HandleKeyInput(key!);
			if (handledByModal)
				_markInputHandled();
			return handledByModal;
		}

		if (snapshot.LayoutEditActive)
		{
			if (_handleLayoutEditKeyInput(key!) || keyPressed)
				_markInputHandled();
			return true;
		}

		if (snapshot.MapEditorActive)
		{
			if (_handleMapEditorKeyInput(key!) || keyPressed)
				_markInputHandled();
			return true;
		}

		if (_handleInspectModeKeyInput(key!))
		{
			_markInputHandled();
			return true;
		}

		if (snapshot.InMenu && !snapshot.SettingsOverlayVisible)
			return true;

		if (_handlePanelManagerKeyInput(key!) || (!snapshot.InMenu && _handleInputModuleKeyInput(key!)))
		{
			_markInputHandled();
			return true;
		}

		return false;
	}

	public bool HandleInput(InputEvent @event, RuntimeUiModeSnapshot snapshot) =>
		HandleInput(@event, snapshot, @event is InputEventKey);

	internal bool HandleInput(InputEvent? @event, RuntimeUiModeSnapshot snapshot, bool isKeyEvent)
	{
		var inputEvent = @event!;

		if (snapshot.BusyOperationActive)
		{
			_markInputHandled();
			return true;
		}

		if (snapshot.AllowPanelChrome && _handlePanelChromeInput(inputEvent))
		{
			_markInputHandled();
			return true;
		}

		if (snapshot.AllowPanelDrag && _handlePanelDragInput(inputEvent))
		{
			_markInputHandled();
			return true;
		}

		var modalLayer = GetVisibleModalLayer();
		if (modalLayer != null)
		{
			var handled = isKeyEvent
				? modalLayer.HandleKeyInput((InputEventKey)inputEvent)
				: modalLayer.HandleMouseInput(inputEvent);

			if (handled)
				_markInputHandled();
			return handled;
		}

		if (snapshot.LayoutEditActive)
		{
			if (_handleLayoutEditInput(inputEvent))
				_markInputHandled();
			return true;
		}

		if (snapshot.MapEditorActive)
		{
			if (_handleMapEditorInput(inputEvent))
				_markInputHandled();
			return true;
		}

		if (_handleGameplayMouseInput(inputEvent, snapshot))
		{
			_markInputHandled();
			return true;
		}

		// Do not swallow mouse input globally when gameplay is blocked (e.g. main menu),
		// otherwise Control buttons can hover but never receive click events.
		return snapshot.BlocksGameplayInput && isKeyEvent;
	}

	private IModalInputLayer? GetVisibleModalLayer()
	{
		foreach (var layer in _modalLayers)
		{
			if (layer.Visible)
				return layer;
		}

		return null;
	}
}
