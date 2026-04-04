using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Panel;

public readonly record struct DraggablePanelRegistration(
	string PanelId,
	PanelContainer Panel,
	bool MakeTopLevel = true
);

public sealed class PanelDragService(PanelLayoutStore store, Control floatingRoot)
{
	private readonly PanelLayoutStore _store = store;
	private readonly Control _floatingRoot = floatingRoot;
	private readonly Dictionary<string, DragState> _states = [];

	public void Initialize() => _store.Load();

	public void Register(DraggablePanelRegistration registration)
	{
		Unregister(registration.PanelId);

		var state = new DragState(registration)
		{
			PanelVisibilityChanged = () => OnPanelVisibilityChanged(registration.PanelId),
		};

		foreach (var handle in CollectDragHandles(registration.Panel))
		{
			var currentHandle = handle;
			Control.GuiInputEventHandler handler = ev => OnHandleGuiInput(registration.PanelId, currentHandle, ev);
			currentHandle.GuiInput += handler;
			state.HandleBindings.Add(new HandleBinding(currentHandle, handler));
		}

		registration.Panel.VisibilityChanged += state.PanelVisibilityChanged;
		_states[registration.PanelId] = state;

		if (registration.MakeTopLevel)
			ReparentToFloating(registration.PanelId);

		if (registration.Panel.Visible)
			ApplySavedPosition(registration.PanelId);
	}

	public void Unregister(string panelId)
	{
		if (!_states.TryGetValue(panelId, out var state))
			return;

		foreach (var binding in state.HandleBindings)
			binding.Handle.GuiInput -= binding.Handler;

		if (state.PanelVisibilityChanged != null)
			state.Registration.Panel.VisibilityChanged -= state.PanelVisibilityChanged;

		_states.Remove(panelId);
	}

	public bool ApplySavedPosition(string panelId)
	{
		if (!_states.TryGetValue(panelId, out var state))
			return false;

		if (!_store.TryGet(panelId, out var saved))
			return false;

		state.Registration.Panel.GlobalPosition = saved;
		return true;
	}

	private void OnPanelVisibilityChanged(string panelId)
	{
		if (!_states.TryGetValue(panelId, out var state))
			return;

		if (!state.Registration.Panel.Visible)
			return;

		ApplySavedPosition(panelId);
	}

	private void ReparentToFloating(string panelId)
	{
		if (!_states.TryGetValue(panelId, out var state))
			return;

		if (state.FloatingPrepared)
			return;

		var panel = state.Registration.Panel;
		if (!panel.IsInsideTree())
			return;

		var parent = panel.GetParent();
		if (parent != null && parent != _floatingRoot)
		{
			var keepPos = panel.GlobalPosition;
			parent.RemoveChild(panel);
			_floatingRoot.AddChild(panel);
			panel.GlobalPosition = keepPos;
		}

		state.FloatingPrepared = true;
	}

	private void OnHandleGuiInput(string panelId, Control sourceHandle, InputEvent ev)
	{
		if (!_states.TryGetValue(panelId, out var state))
			return;

		if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
		{
			if (mb.Pressed)
			{
				if (IsInButtonTree(sourceHandle) || IsHoveringButton(state.Registration.Panel))
					return;

				state.Dragging = true;
				state.DragOffset = state.Registration.Panel.GlobalPosition - mb.GlobalPosition;
				return;
			}

			if (!state.Dragging)
				return;

			state.Dragging = false;
			_store.Set(panelId, state.Registration.Panel.GlobalPosition);
			_store.Save();
			return;
		}

		if (ev is InputEventMouseMotion mm && state.Dragging)
			state.Registration.Panel.GlobalPosition = mm.GlobalPosition + state.DragOffset;
	}

	private static List<Control> CollectDragHandles(PanelContainer panel)
	{
		var handles = new List<Control> { panel };
		CollectRecursive(panel, handles);
		return handles;
	}

	private static void CollectRecursive(Node node, List<Control> handles)
	{
		foreach (var child in node.GetChildren())
		{
			if (child is not Control ctrl)
				continue;

			if (ctrl.Name.ToString().StartsWith("__drag_", StringComparison.Ordinal))
				continue;

			// Non-button region is draggable; button subtree is excluded.
			if (ctrl is BaseButton)
				continue;

			handles.Add(ctrl);
			CollectRecursive(ctrl, handles);
		}
	}

	private static bool IsInButtonTree(Control ctrl)
	{
		Control? current = ctrl;
		while (current != null)
		{
			if (current is BaseButton)
				return true;
			current = current.GetParent() as Control;
		}

		return false;
	}

	private static bool IsHoveringButton(Control panel)
	{
		var hovered = panel.GetViewport()?.GuiGetHoveredControl();
		if (hovered == null)
			return false;
		return IsInButtonTree(hovered);
	}

	private sealed class HandleBinding(Control handle, Control.GuiInputEventHandler handler)
	{
		public Control Handle { get; } = handle;
		public Control.GuiInputEventHandler Handler { get; } = handler;
	}

	private sealed class DragState(DraggablePanelRegistration registration)
	{
		public readonly DraggablePanelRegistration Registration = registration;
		public readonly List<HandleBinding> HandleBindings = [];
		public bool Dragging;
		public bool FloatingPrepared;
		public Vector2 DragOffset;
		public Action? PanelVisibilityChanged;
	}
}
