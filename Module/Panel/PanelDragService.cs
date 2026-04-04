using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Panel;

public readonly record struct DraggablePanelRegistration(
	string PanelId,
	PanelContainer Panel,
	bool DefaultFloating = true
);

public sealed class PanelDragService(PanelLayoutStore store, Control floatingRoot)
{
	private const float PositionEpsilon = 1f;

	private readonly PanelLayoutStore _store = store;
	private readonly Control _floatingRoot = floatingRoot;
	private readonly Dictionary<string, DragState> _states = [];
	private bool _editModeActive;

	public bool EditModeActive => _editModeActive;

	public void Initialize() => _store.Load();

	public void RemovePersistedLayout(string panelId) => _store.Remove(panelId);

	public void Register(DraggablePanelRegistration registration)
	{
		Unregister(registration.PanelId);

		var panel = registration.Panel;
		var state = new DragState(registration)
		{
			OriginalParent = panel.GetParent(),
			OriginalIndex = panel.GetIndex(),
			PanelVisibilityChanged = () => OnPanelVisibilityChanged(registration.PanelId),
			DefaultGlobalPosition = panel.GlobalPosition,
			DefaultPositionCaptured = true,
		};

		foreach (var handle in CollectDragHandles(panel))
		{
			var currentHandle = handle;
			Control.GuiInputEventHandler handler = ev => OnHandleGuiInput(registration.PanelId, currentHandle, ev);
			currentHandle.GuiInput += handler;
			state.HandleBindings.Add(new HandleBinding(currentHandle, handler));
		}

		panel.VisibilityChanged += state.PanelVisibilityChanged;
		_states[registration.PanelId] = state;

		ApplyPersistedLayout(state);
	}

	public void Unregister(string panelId)
	{
		if (!_states.TryGetValue(panelId, out var state))
			return;

		foreach (var binding in state.HandleBindings)
			binding.Handle.GuiInput -= binding.Handler;

		if (state.PanelVisibilityChanged != null)
			state.Registration.Panel.VisibilityChanged -= state.PanelVisibilityChanged;

		RemovePlaceholder(state);
		_states.Remove(panelId);
	}

	public void BeginEditSession()
	{
		if (_editModeActive)
			return;

		_editModeActive = true;
		foreach (var state in _states.Values)
		{
			state.SessionFloating = IsFloating(state);
			state.SessionPosition = state.Registration.Panel.GlobalPosition;
			state.HasSessionSnapshot = true;
		}
	}

	public void ApplyEditSession()
	{
		if (!_editModeActive)
			return;

		foreach (var state in _states.Values)
		{
			var panel = state.Registration.Panel;
			var isFloating = IsFloating(state);

			if (isFloating)
			{
				RemovePlaceholder(state);

				if (!state.Registration.DefaultFloating)
				{
					_store.Set(state.Registration.PanelId, panel.GlobalPosition);
				}
				else if (IsNear(panel.GlobalPosition, state.DefaultGlobalPosition))
				{
					_store.Remove(state.Registration.PanelId);
				}
				else
				{
					_store.Set(state.Registration.PanelId, panel.GlobalPosition);
				}
			}
			else
			{
				_store.Remove(state.Registration.PanelId);
			}
		}

		_store.Save();
		ClearSessionState();
	}

	public void CancelEditSession()
	{
		if (!_editModeActive)
			return;

		foreach (var state in _states.Values)
		{
			if (!state.HasSessionSnapshot)
				continue;

			if (state.SessionFloating)
			{
				EnsureFloating(state);
				state.Registration.Panel.GlobalPosition = state.SessionPosition;
				RemovePlaceholder(state);
			}
			else
			{
				EnsureDocked(state);
			}
		}

		ClearSessionState();
	}

	public void ResetToDefaults()
	{
		if (!_editModeActive)
			return;

		foreach (var state in _states.Values)
		{
			if (state.Registration.DefaultFloating)
			{
				EnsureFloating(state);
				RemovePlaceholder(state);
				state.Registration.Panel.GlobalPosition = state.DefaultGlobalPosition;
			}
			else
			{
				EnsureDocked(state);
			}
		}
	}

	private void ClearSessionState()
	{
		_editModeActive = false;
		foreach (var state in _states.Values)
		{
			state.Dragging = false;
			state.HasSessionSnapshot = false;
		}
	}

	private void ApplyPersistedLayout(DragState state)
	{
		if (_store.TryGet(state.Registration.PanelId, out var saved))
		{
			EnsureFloating(state);
			state.Registration.Panel.GlobalPosition = saved;
			return;
		}

		if (state.Registration.DefaultFloating)
		{
			EnsureFloating(state);
			state.Registration.Panel.GlobalPosition = state.DefaultGlobalPosition;
		}
		else
		{
			EnsureDocked(state);
		}
	}

	private void OnPanelVisibilityChanged(string panelId)
	{
		if (!_states.TryGetValue(panelId, out var state))
			return;

		if (!state.Registration.Panel.Visible || _editModeActive)
			return;

		ApplyPersistedLayout(state);
	}

	private void OnHandleGuiInput(string panelId, Control sourceHandle, InputEvent ev)
	{
		if (!_editModeActive || !_states.TryGetValue(panelId, out var state) || !state.Registration.Panel.Visible)
			return;

		switch (ev)
		{
			case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left:
				sourceHandle.AcceptEvent();
				if (mb.Pressed)
				{
					PrepareForEditing(state);
					BringToFront(state.Registration.Panel);
					state.Dragging = true;
					state.DragOffset = state.Registration.Panel.GlobalPosition - mb.GlobalPosition;
				}
				else
				{
					state.Dragging = false;
				}
				break;

			case InputEventMouseMotion mm:
				sourceHandle.AcceptEvent();
				if (state.Dragging)
					state.Registration.Panel.GlobalPosition = mm.GlobalPosition + state.DragOffset;
				break;

			case InputEventMouseButton:
				sourceHandle.AcceptEvent();
				break;
		}
	}

	private void PrepareForEditing(DragState state)
	{
		if (IsFloating(state))
			return;

		var panel = state.Registration.Panel;
		var parent = panel.GetParent() as Control;
		if (parent == null)
			return;

		var keepPos = panel.GlobalPosition;
		state.OriginalParent = parent;
		state.OriginalIndex = panel.GetIndex();

		if (state.Placeholder == null)
		{
			var placeholder = new Control
			{
				Name = $"__layout_placeholder_{state.Registration.PanelId}",
				MouseFilter = Control.MouseFilterEnum.Ignore,
				CustomMinimumSize = panel.Size,
				SizeFlagsHorizontal = panel.SizeFlagsHorizontal,
				SizeFlagsVertical = panel.SizeFlagsVertical,
			};
			parent.RemoveChild(panel);
			parent.AddChild(placeholder);
			parent.MoveChild(placeholder, Math.Clamp(state.OriginalIndex, 0, parent.GetChildCount() - 1));
			state.Placeholder = placeholder;
		}

		_floatingRoot.AddChild(panel);
		panel.GlobalPosition = keepPos;
	}

	private void EnsureFloating(DragState state)
	{
		if (IsFloating(state))
			return;

		var panel = state.Registration.Panel;
		if (!panel.IsInsideTree())
			return;

		var keepPos = panel.GlobalPosition;
		panel.GetParent()?.RemoveChild(panel);
		_floatingRoot.AddChild(panel);
		panel.GlobalPosition = keepPos;
	}

	private void EnsureDocked(DragState state)
	{
		var panel = state.Registration.Panel;
		if (state.Placeholder?.GetParent() is Control placeholderParent)
		{
			var targetIndex = state.Placeholder.GetIndex();
			panel.GetParent()?.RemoveChild(panel);
			placeholderParent.AddChild(panel);
			placeholderParent.MoveChild(panel, targetIndex);
			RemovePlaceholder(state);
			return;
		}

		if (state.OriginalParent is not Control originalParent || panel.GetParent() == originalParent)
			return;

		panel.GetParent()?.RemoveChild(panel);
		originalParent.AddChild(panel);
		originalParent.MoveChild(panel, Math.Clamp(state.OriginalIndex, 0, originalParent.GetChildCount() - 1));
	}

	private void RemovePlaceholder(DragState state)
	{
		if (state.Placeholder == null)
			return;

		var placeholder = state.Placeholder;
		placeholder.GetParent()?.RemoveChild(placeholder);
		placeholder.QueueFree();
		state.Placeholder = null;
	}

	private bool IsFloating(DragState state) => state.Registration.Panel.GetParent() == _floatingRoot;

	private void BringToFront(Control panel)
	{
		if (panel.GetParent() == _floatingRoot)
			_floatingRoot.MoveChild(panel, _floatingRoot.GetChildCount() - 1);
	}

	private static bool IsNear(Vector2 a, Vector2 b)
	{
		return Math.Abs(a.X - b.X) <= PositionEpsilon
			&& Math.Abs(a.Y - b.Y) <= PositionEpsilon;
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

			handles.Add(ctrl);
			CollectRecursive(ctrl, handles);
		}
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
		public Vector2 DragOffset;
		public Action? PanelVisibilityChanged;
		public Node? OriginalParent;
		public int OriginalIndex;
		public Control? Placeholder;
		public bool HasSessionSnapshot;
		public bool SessionFloating;
		public Vector2 SessionPosition;
		public bool DefaultPositionCaptured;
		public Vector2 DefaultGlobalPosition;
	}
}
