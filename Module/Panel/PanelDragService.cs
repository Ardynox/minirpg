using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Panel;

public enum PanelDragAvailability
{
	Always,
	EditModeOnly,
}

public readonly record struct DraggablePanelRegistration(
	string PanelId,
	PanelContainer Panel,
	PanelDragAvailability Availability,
	IReadOnlyList<Control> DragHandles,
	bool DefaultFloating = true
);

public sealed class PanelDragService(PanelLayoutStore store, Control floatingRoot)
{
	private const float PositionEpsilon = 1f;
	private const float DragStartThreshold = 4f;

	private readonly PanelLayoutStore _store = store;
	private readonly Control _floatingRoot = floatingRoot;
	private readonly Dictionary<string, DragState> _states = [];
	private string? _directDragPanelId;
	private bool _editModeActive;
	private DragState? _pendingDragState;
	private DragState? _activeDragState;

	public event Action? LayoutChanged;

	public bool EditModeActive => _editModeActive;
	public string? DirectDragPanelId => _directDragPanelId;

	public void Initialize() => _store.Load();

	public void RemovePersistedPosition(string panelId) => _store.RemovePosition(panelId);

	public bool HandleGlobalInput(InputEvent ev)
	{
		if (_activeDragState != null && !CanTrack(_activeDragState))
			AbortDrag(_activeDragState);

		if (_pendingDragState != null && !CanTrack(_pendingDragState))
			AbortDrag(_pendingDragState);

		return ev switch
		{
			InputEventMouseMotion mm => HandleGlobalMouseMotion(mm),
			InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left => HandleGlobalLeftButton(mb),
			_ => false,
		};
	}

	public void Register(DraggablePanelRegistration registration)
	{
		Unregister(registration.PanelId);

		var panel = registration.Panel;
		var handles = registration.DragHandles;
		var state = new DragState(registration)
		{
			OriginalParent = panel.GetParent(),
			OriginalIndex = panel.GetIndex(),
			PanelVisibilityChanged = () => OnPanelVisibilityChanged(registration.PanelId),
			DefaultGlobalPosition = panel.GlobalPosition,
		};

		foreach (var handle in handles)
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

		AbortDrag(state);

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
			if (state.Registration.Availability != PanelDragAvailability.EditModeOnly)
				continue;

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
			if (state.Registration.Availability != PanelDragAvailability.EditModeOnly)
				continue;

			PersistCurrentPosition(state);
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
			if (state.Registration.Availability != PanelDragAvailability.EditModeOnly || !state.HasSessionSnapshot)
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
			if (state.Registration.Availability != PanelDragAvailability.EditModeOnly)
				continue;

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

	public bool BeginDirectDrag(string panelId, Vector2 pointerGlobalPosition)
	{
		if (_editModeActive || !_states.TryGetValue(panelId, out var state) || !state.Registration.Panel.Visible)
			return false;

		if (_directDragPanelId != null && _directDragPanelId != panelId)
			StopDirectDrag(persistPosition: true);

		PrepareForDragging(state);
		BringToFront(state.Registration.Panel);
		state.DirectDragging = true;
		state.DragOffset = state.Registration.Panel.GlobalPosition - pointerGlobalPosition;
		_directDragPanelId = panelId;
		return true;
	}

	public bool UpdateDirectDrag(Vector2 pointerGlobalPosition)
	{
		if (_directDragPanelId == null || !_states.TryGetValue(_directDragPanelId, out var state) || !state.DirectDragging)
			return false;

		state.Registration.Panel.GlobalPosition = pointerGlobalPosition + state.DragOffset;
		return true;
	}

	public bool StopDirectDrag(bool persistPosition)
	{
		if (_directDragPanelId == null || !_states.TryGetValue(_directDragPanelId, out var state))
			return false;

		state.DirectDragging = false;
		_directDragPanelId = null;

		if (persistPosition)
			PersistPanelPosition(state);

		return true;
	}

	private void ClearSessionState()
	{
		_editModeActive = false;
		_pendingDragState = null;
		_activeDragState = null;
		foreach (var state in _states.Values)
		{
			state.PendingDrag = false;
			state.Dragging = false;
			state.DirectDragging = false;
			state.HasSessionSnapshot = false;
		}
	}

	private void ApplyPersistedLayout(DragState state)
	{
		if (_store.TryGetPosition(state.Registration.PanelId, out var saved))
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

		if (!state.Registration.Panel.Visible && _directDragPanelId == panelId)
		{
			state.DirectDragging = false;
			_directDragPanelId = null;
		}

		if (!state.Registration.Panel.Visible)
		{
			AbortDrag(state);
			return;
		}

		if (state.Registration.Availability == PanelDragAvailability.EditModeOnly && _editModeActive)
			return;

		ApplyPersistedLayout(state);
	}

	private void OnHandleGuiInput(string panelId, Control sourceHandle, InputEvent ev)
	{
		if (!_states.TryGetValue(panelId, out var state) || !state.Registration.Panel.Visible || !CanDrag(state))
			return;

		var swallowInput = state.Registration.Availability == PanelDragAvailability.EditModeOnly;
		if (swallowInput && ev is InputEventMouseButton or InputEventMouseMotion)
			sourceHandle.AcceptEvent();

		if (ev is not InputEventMouseButton mb
			|| mb.ButtonIndex != MouseButton.Left
			|| !mb.Pressed
			|| _pendingDragState != null
			|| _activeDragState != null)
			return;

		state.PendingDrag = true;
		state.PressGlobalPosition = mb.GlobalPosition;
		state.DragOffset = state.Registration.Panel.GlobalPosition - mb.GlobalPosition;
		_pendingDragState = state;
	}

	private bool HandleGlobalMouseMotion(InputEventMouseMotion mm)
	{
		if (_activeDragState != null)
		{
			if (!IsLeftButtonPressed(mm.ButtonMask))
			{
				FinishDrag(_activeDragState, mm.GlobalPosition);
				return true;
			}

			_activeDragState.Registration.Panel.GlobalPosition = mm.GlobalPosition + _activeDragState.DragOffset;
			return true;
		}

		if (_pendingDragState == null)
			return false;

		if (!IsLeftButtonPressed(mm.ButtonMask))
		{
			ClearPendingDrag(_pendingDragState);
			return true;
		}

		if (mm.GlobalPosition.DistanceSquaredTo(_pendingDragState.PressGlobalPosition) < DragStartThreshold * DragStartThreshold)
			return true;

		StartDrag(_pendingDragState, mm.GlobalPosition);
		return true;
	}

	private bool HandleGlobalLeftButton(InputEventMouseButton mb)
	{
		if (mb.Pressed)
			return _activeDragState != null || _pendingDragState != null;

		if (_activeDragState != null)
		{
			FinishDrag(_activeDragState, mb.GlobalPosition);
			return true;
		}

		if (_pendingDragState != null)
		{
			ClearPendingDrag(_pendingDragState);
			return true;
		}

		return false;
	}

	private void StartDrag(DragState state, Vector2 pointerPosition)
	{
		_pendingDragState = null;
		state.PendingDrag = false;
		PrepareForDragging(state);
		BringToFront(state.Registration.Panel);
		state.Dragging = true;
		_activeDragState = state;
		state.Registration.Panel.GlobalPosition = pointerPosition + state.DragOffset;
	}

	private void FinishDrag(DragState state, Vector2 pointerPosition)
	{
		state.Registration.Panel.GlobalPosition = pointerPosition + state.DragOffset;
		state.Dragging = false;
		_activeDragState = null;
		if (state.Registration.Availability == PanelDragAvailability.Always)
		{
			PersistCurrentPosition(state);
			_store.Save();
		}
	}

	private void ClearPendingDrag(DragState state)
	{
		state.PendingDrag = false;
		if (_pendingDragState == state)
			_pendingDragState = null;
	}

	private void AbortDrag(DragState state)
	{
		ClearPendingDrag(state);
		if (_activeDragState == state)
			_activeDragState = null;
		state.Dragging = false;
		RemovePlaceholder(state);
	}

	private bool CanDrag(DragState state)
	{
		return state.Registration.Availability == PanelDragAvailability.Always || _editModeActive;
	}

	private bool CanTrack(DragState state)
	{
		return state.Registration.Panel.Visible && CanDrag(state);
	}

	private void PersistCurrentPosition(DragState state)
	{
		var panel = state.Registration.Panel;
		var isFloating = IsFloating(state);

		if (isFloating)
		{
			RemovePlaceholder(state);

			if (!state.Registration.DefaultFloating)
			{
				_store.SetPosition(state.Registration.PanelId, panel.GlobalPosition);
			}
			else if (IsNear(panel.GlobalPosition, state.DefaultGlobalPosition))
			{
				_store.RemovePosition(state.Registration.PanelId);
			}
			else
			{
				_store.SetPosition(state.Registration.PanelId, panel.GlobalPosition);
			}

			return;
		}

		_store.RemovePosition(state.Registration.PanelId);
	}

	private void PrepareForDragging(DragState state)
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
		NotifyLayoutChanged();
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
		NotifyLayoutChanged();
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
			NotifyLayoutChanged();
			return;
		}

		if (state.OriginalParent is not Control originalParent || panel.GetParent() == originalParent)
			return;

		panel.GetParent()?.RemoveChild(panel);
		originalParent.AddChild(panel);
		originalParent.MoveChild(panel, Math.Clamp(state.OriginalIndex, 0, originalParent.GetChildCount() - 1));
		NotifyLayoutChanged();
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

	public bool IsFloating(string panelId) =>
		_states.TryGetValue(panelId, out var state) && IsFloating(state);

	private bool IsFloating(DragState state) => state.Registration.Panel.GetParent() == _floatingRoot;

	private void NotifyLayoutChanged() => LayoutChanged?.Invoke();

	private void BringToFront(Control panel)
	{
		if (panel.GetParent() == _floatingRoot)
			_floatingRoot.MoveChild(panel, _floatingRoot.GetChildCount() - 1);
	}

	private void PersistPanelPosition(DragState state)
	{
		var panel = state.Registration.Panel;
		if (!state.Registration.DefaultFloating)
		{
			_store.SetPosition(state.Registration.PanelId, panel.GlobalPosition);
		}
		else if (IsNear(panel.GlobalPosition, state.DefaultGlobalPosition))
		{
			_store.RemovePosition(state.Registration.PanelId);
		}
		else
		{
			_store.SetPosition(state.Registration.PanelId, panel.GlobalPosition);
		}

		_store.Save();
	}

	private static bool IsNear(Vector2 a, Vector2 b)
	{
		return Math.Abs(a.X - b.X) <= PositionEpsilon
			&& Math.Abs(a.Y - b.Y) <= PositionEpsilon;
	}

	private static bool IsLeftButtonPressed(MouseButtonMask buttonMask)
	{
		return (buttonMask & MouseButtonMask.Left) != 0;
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
		public bool PendingDrag;
		public bool Dragging;
		public bool DirectDragging;
		public Vector2 PressGlobalPosition;
		public Vector2 DragOffset;
		public Action? PanelVisibilityChanged;
		public Node? OriginalParent;
		public int OriginalIndex;
		public Control? Placeholder;
		public bool HasSessionSnapshot;
		public bool SessionFloating;
		public Vector2 SessionPosition;
		public Vector2 DefaultGlobalPosition;
	}
}
