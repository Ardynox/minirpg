using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Module;

namespace MiniRPG.Module.Panel;

/// <summary>
/// Centralized panel focus manager with stack-based restore.
/// </summary>
public class PanelManager
{
	public event Action? FocusChanged;

	private readonly List<IPanel> _panels = [];
	private readonly List<PanelContainer> _allNodes = [];
	private readonly List<IPanel> _focusStack = [];
	private readonly Dictionary<PanelContainer, string> _nodePanelIds = [];
	private readonly InputBindingService? _bindings;
	private IPanel? _focused;
	private bool _switching;
	private Func<string, bool>? _isFloatingCheck;

	public PanelManager(InputBindingService? bindings = null)
	{
		_bindings = bindings;
	}

	public IPanel? Focused => _focused;
	public bool HasFocus => _focused != null;
	public string? FocusedId => _focused?.PanelId;

	public void SetFloatingCheck(Func<string, bool> isFloatingCheck) =>
		_isFloatingCheck = isFloatingCheck;

	public void Register(IPanel panel)
	{
		if (_panels.Contains(panel))
			return;

		_panels.Add(panel);
		RegisterNode(panel.PanelNode, panel.PanelId);
	}

	public void Unregister(IPanel panel)
	{
		if (!_panels.Remove(panel))
			return;

		RemoveFromStack(panel);
		UnregisterNode(panel.PanelNode);
		if (_focused == panel)
			SwitchFocus(PopPreviousFocusable(), clearStack: false);
	}

	public void RegisterPassive(PanelContainer node)
	{
		RegisterNode(node, panelId: null);
	}

	public void UnregisterPassive(PanelContainer node)
	{
		UnregisterNode(node);
	}

	public void RegisterPassive(
		PanelContainer node,
		string panelId,
		bool canFocus,
		bool consumeUnhandledKeys = true,
		bool allowGlobalClose = true)
	{
		if (!canFocus)
		{
			RegisterNode(node, panelId);
			return;
		}

		Register(new PassivePanel(panelId, node, consumeUnhandledKeys, allowGlobalClose));
	}

	public void PruneInvalidPanels()
	{
		for (var i = _panels.Count - 1; i >= 0; i--)
		{
			var panel = _panels[i];
			if (IsPanelInvalid(panel))
				Unregister(panel);
		}

		for (var i = _allNodes.Count - 1; i >= 0; i--)
		{
			var node = _allNodes[i];
			if (IsNodeInvalid(node))
				UnregisterNode(node);
		}
	}

	/// <summary>
	/// Directly switch focus and clear focus stack.
	/// </summary>
	public void SetFocus(IPanel? panel) => SwitchFocus(panel, clearStack: true);

	public void SetFocus(string? panelId)
	{
		if (panelId == null)
		{
			ClearFocus();
			return;
		}

		var panel = _panels.Find(p => p.PanelId == panelId);
		if (panel != null)
			SetFocus(panel);
	}

	public void ClearFocus() => SwitchFocus(null, clearStack: true);

	/// <summary>
	/// Push current focus and focus target panel.
	/// </summary>
	public void PushFocus(IPanel? panel)
	{
		if (_switching || panel == null || !IsFocusable(panel))
			return;

		if (_focused == panel)
			return;

		if (_focused != null)
			_focusStack.Add(_focused);

		SwitchFocus(panel, clearStack: false);
	}

	/// <summary>
	/// Focus change from pointer interaction: push previous focus into stack without clearing history.
	/// </summary>
	public void FocusFromPointer(IPanel? panel, bool clearStack = false)
	{
		if (_switching || panel == null || !IsFocusable(panel))
			return;

		if (_focused == panel)
		{
			if (clearStack)
				SwitchFocus(panel, clearStack: true);
			return;
		}

		RemoveFromStack(panel);

		if (!clearStack && _focused != null)
		{
			RemoveFromStack(_focused);
			_focusStack.Add(_focused);
		}

		SwitchFocus(panel, clearStack);
	}

	/// <summary>
	/// Restore previous focus from stack. If no valid previous panel exists, focus is cleared.
	/// </summary>
	public bool RestorePreviousFocus()
	{
		if (_switching)
			return false;

		var next = PopPreviousFocusable();
		SwitchFocus(next, clearStack: false);
		return next != null;
	}

	/// <summary>
	/// Notify manager that panel has been closed externally.
	/// </summary>
	public void OnPanelClosed(IPanel panel)
	{
		RemoveFromStack(panel);

		if (_focused == panel)
			RestorePreviousFocus();
	}

	/// <summary>
	/// Close current focused panel, then restore previous focus from stack.
	/// </summary>
	public bool CloseFocused()
	{
		var focused = _focused;
		if (focused == null || !focused.AllowGlobalClose)
			return false;

		focused.HandleCommand("close");
		if (_focused == focused)
			OnPanelClosed(focused);

		return true;
	}

	public bool HandleKey(InputEventKey key)
	{
		var focused = _focused;
		if (focused == null || !key.Pressed)
			return false;

		// Recover when panel visibility changed without explicit OnPanelClosed call.
		if (!focused.Visible)
		{
			OnPanelClosed(focused);
			focused = _focused;
			if (focused == null)
				return false;
		}

		var cmd = MapKeyToCommand(key);
		if (cmd == null)
			return focused.ConsumeUnhandledKeys;

		if (cmd == "close")
			return CloseFocused() || focused.ConsumeUnhandledKeys;

		var handled = focused.HandleCommand(cmd);
		if (!handled && ShouldTrapDirectionalInput(focused, cmd))
			return true;

		return handled || focused.ConsumeUnhandledKeys;
	}

	public IPanel? HitTest(Vector2 globalPos)
	{
		// Prefer Godot's actual topmost hovered control when panels overlap.
		var hovered = _panels.Count > 0 ? _panels[0].PanelNode.GetViewport()?.GuiGetHoveredControl() : null;
		var hoveredPanel = FindPanelForControl(hovered);
		if (hoveredPanel != null
			&& hoveredPanel.Visible
			&& hoveredPanel.CanFocus
			&& hoveredPanel.PanelNode.GetGlobalRect().HasPoint(globalPos))
		{
			return hoveredPanel;
		}

		// Fallback to reverse registration order, which better matches draw order than forward scan.
		for (var i = _panels.Count - 1; i >= 0; i--)
		{
			var p = _panels[i];
			if (!p.Visible || !p.CanFocus)
				continue;

			if (p.PanelNode.GetGlobalRect().HasPoint(globalPos))
				return p;
		}

		return null;
	}

	public void RefreshBorders()
	{
		var focusedNode = _focused?.PanelNode;
		foreach (var node in _allNodes)
		{
			var panelId = FindPanelIdForNode(node);
			var floating = panelId != null && _isFloatingCheck != null && _isFloatingCheck(panelId);
			PanelBorderHelper.Apply(node, node == focusedNode, floating);
		}
	}

	private void SwitchFocus(IPanel? panel, bool clearStack)
	{
		if (_switching)
			return;

		if (panel != null && !IsFocusable(panel))
			return;

		var prev = _focused;
		if (prev == panel)
		{
			if (clearStack)
				_focusStack.Clear();
			return;
		}

		_switching = true;
		_focused = panel;
		prev?.OnBlur();
		_focused?.OnFocus();
		_switching = false;

		if (clearStack)
			_focusStack.Clear();

		if (prev != null)
		{
			var prevFloating = _isFloatingCheck != null && _isFloatingCheck(prev.PanelId);
			PanelBorderHelper.Apply(prev.PanelNode, false, prevFloating);
		}
		if (_focused != null)
		{
			var focusedFloating = _isFloatingCheck != null && _isFloatingCheck(_focused.PanelId);
			PanelBorderHelper.Apply(_focused.PanelNode, true, focusedFloating);
		}

		FocusChanged?.Invoke();
	}

	private bool IsFocusable(IPanel panel)
	{
		if (IsPanelInvalid(panel))
			return false;

		return panel.CanFocus && panel.Visible;
	}

	private string? FindPanelIdForNode(PanelContainer node)
	{
		return _nodePanelIds.TryGetValue(node, out var panelId) ? panelId : null;
	}

	private IPanel? FindPanelForControl(Control? control)
	{
		Control? current = control;
		while (current != null)
		{
			foreach (var panel in _panels)
			{
				if (panel.PanelNode == current)
					return panel;
			}

			current = current.GetParent() as Control;
		}

		return null;
	}

	private IPanel? PopPreviousFocusable()
	{
		while (_focusStack.Count > 0)
		{
			var idx = _focusStack.Count - 1;
			var panel = _focusStack[idx];
			_focusStack.RemoveAt(idx);

			if (panel != _focused && IsFocusable(panel))
				return panel;
		}

		return null;
	}

	private void RemoveFromStack(IPanel panel)
	{
		for (var i = _focusStack.Count - 1; i >= 0; i--)
		{
			if (_focusStack[i] == panel)
				_focusStack.RemoveAt(i);
		}
	}

	private string? MapKeyToCommand(InputEventKey key)
	{
		if (_bindings != null && _bindings.Resolve(InputBindingContext.Action, key, out var actionId, out _))
		{
			var cmd = actionId switch
			{
				"move_north" => "up",
				"move_south" => "down",
				"move_west" => "left",
				"move_east" => "right",
				"open_settings" => "close",
				"interact" => "confirm",
				_ => null,
			};
			if (cmd != null) return cmd;
		}

		return key.Keycode switch
		{
			Key.Enter => "confirm",
			Key.Escape => "close",
			Key.E => "action1",
			Key.Q => "action2",
			Key.R => "action3",
			Key.P => "action4",
			Key.U => "action5",
			Key.Tab => key.ShiftPressed ? "tab_prev" : "tab_next",
			Key.Key1 => "1",
			Key.Key2 => "2",
			Key.Key3 => "3",
			Key.Key4 => "4",
			Key.Key5 => "5",
			Key.Key6 => "6",
			Key.Key7 => "7",
			Key.Key8 => "8",
			Key.Key9 => "9",
			_ => null,
		};
	}

	private static bool ShouldTrapDirectionalInput(IPanel focused, string cmd)
	{
		if (focused.PanelId == "map")
			return false;

		return cmd is "up" or "down" or "left" or "right";
	}

	private static bool IsPanelInvalid(IPanel panel) => IsNodeInvalid(panel.PanelNode);

	private static bool IsNodeInvalid(PanelContainer node) => !GodotObject.IsInstanceValid(node) || node.IsQueuedForDeletion();

	private void RegisterNode(PanelContainer node, string? panelId)
	{
		if (!_allNodes.Contains(node))
			_allNodes.Add(node);

		if (panelId == null)
			_nodePanelIds.Remove(node);
		else
			_nodePanelIds[node] = panelId;
	}

	private void UnregisterNode(PanelContainer node)
	{
		_allNodes.Remove(node);
		_nodePanelIds.Remove(node);
	}

	private sealed class PassivePanel(string panelId, PanelContainer panelNode, bool consumeUnhandledKeys, bool allowGlobalClose) : IPanel
	{
		public string PanelId => panelId;
		public PanelContainer PanelNode => panelNode;
		public bool Visible { get => panelNode.Visible; set => panelNode.Visible = value; }
		public bool CanFocus => true;
		public bool ConsumeUnhandledKeys => consumeUnhandledKeys;
		public bool AllowGlobalClose => allowGlobalClose;
		public bool Dirty { get; set; }

		public bool HandleCommand(string cmd) => false;
	}
}
