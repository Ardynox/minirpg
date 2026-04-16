using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Module;

namespace MiniRPG;

internal sealed class MainInputCoordinator(
	IReadOnlyList<IModalInputLayer> modalLayers,
	Action markInputHandled,
	InputHandlerSet handlers)
{
	private readonly IReadOnlyList<IModalInputLayer> _modalLayers = modalLayers;
	private readonly Action _markInputHandled = markInputHandled;
	private readonly InputHandlerSet _handlers = handlers;

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
			return true;
		}

		if (snapshot.LayoutEditActive)
		{
			if (_handlers.HandleLayoutEditKeyInput(key!) || keyPressed)
				_markInputHandled();
			return true;
		}

		if (snapshot.MapEditorActive)
		{
			if (_handlers.HandleMapEditorKeyInput(key!) || keyPressed)
				_markInputHandled();
			return true;
		}

		if (_handlers.HandleInspectModeKeyInput(key!))
		{
			_markInputHandled();
			return true;
		}

		if (snapshot.InMenu && !snapshot.SettingsOverlayVisible)
			return true;

		if (_handlers.HandlePanelManagerKeyInput(key!) || (!snapshot.InMenu && _handlers.HandleInputModuleKeyInput(key!)))
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

		if (snapshot.AllowPanelChrome && _handlers.HandlePanelChromeInput(inputEvent))
		{
			_markInputHandled();
			return true;
		}

		if (snapshot.AllowPanelDrag && _handlers.HandlePanelDragInput(inputEvent))
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
			return isKeyEvent || handled;
		}

		if (snapshot.LayoutEditActive)
		{
			if (_handlers.HandleLayoutEditInput(inputEvent))
				_markInputHandled();
			return true;
		}

		if (snapshot.MapEditorActive)
		{
			if (_handlers.HandleMapEditorInput(inputEvent))
			{
				_markInputHandled();
				return true;
			}
			return isKeyEvent;
		}

		if (_handlers.HandleGameplayMouseInput(inputEvent, snapshot))
		{
			_markInputHandled();
			return true;
		}

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
