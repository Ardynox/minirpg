using System;
using Godot;
using MiniRPG.Module;
using MiniRPG.Module.Panel;

namespace MiniRPG;

internal sealed class SettingsFlowModalInputAdapter(
	SettingsFlowCoordinator settingsFlow,
	PanelManager panels,
	Action flushMap) : IModalInputLayer
{
	private readonly SettingsFlowCoordinator _settingsFlow = settingsFlow;
	private readonly PanelManager _panels = panels;
	private readonly Action _flushMap = flushMap;

	public bool Visible => _settingsFlow.HasVisibleOverlay;

	public void Close() => _settingsFlow.CloseActiveOverlay();

	public bool HandleKeyInput(InputEventKey key) =>
		HandleKeyInputCore(
			() => _settingsFlow.HandleKeyInput(key),
			() => _panels.HandleKey(key));

	internal bool HandleKeyInputCore(Func<bool> handleSettingsKeyInput, Func<bool> handlePanelKeyInput)
	{
		if (!Visible)
			return false;

		return handleSettingsKeyInput() || handlePanelKeyInput();
	}

	public bool HandleMouseInput(InputEvent @event)
	{
		var isPressedRightClick = @event is InputEventMouseButton overlayMouse
			&& overlayMouse.Pressed
			&& overlayMouse.ButtonIndex == MouseButton.Right;
		return HandleMouseInputCore(
			() => _settingsFlow.HandleMouseInput(@event),
			isPressedRightClick);
	}

	internal bool HandleMouseInputCore(Func<bool> handleSettingsMouseInput, bool isPressedRightClick)
	{
		if (!Visible)
			return false;

		if (handleSettingsMouseInput())
			return true;

		if (!isPressedRightClick || !_panels.CloseFocused())
		{
			return false;
		}

		_flushMap();
		return true;
	}
}
