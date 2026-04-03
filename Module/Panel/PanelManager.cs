using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 面板管理器：统一管理所有 IPanel 的注册、焦点切换、边框刷新、键盘路由、鼠标点击。
/// Main.cs 和 InputModule 不再需要为每个面板写专门的代码。
/// </summary>
public class PanelManager
{
	public event Action? FocusChanged;

	private readonly List<IPanel> _panels = [];
	private readonly List<PanelContainer> _allNodes = [];
	private IPanel? _focused;

	public IPanel? Focused => _focused;
	public bool HasFocus => _focused != null;
	public string? FocusedId => _focused?.PanelId;

	/// <summary>注册一个面板。可聚焦面板参与焦点切换和键盘路由。</summary>
	public void Register(IPanel panel)
	{
		_panels.Add(panel);
		_allNodes.Add(panel.PanelNode);
	}

	/// <summary>注册一个只参与边框刷新但不参与焦点切换的 PanelContainer（如 MapPanel）。</summary>
	public void RegisterPassive(PanelContainer node)
	{
		_allNodes.Add(node);
	}

	private bool _switching;

	/// <summary>聚焦到指定面板。null = 取消焦点（回到 Action 模式）。</summary>
	public void SetFocus(IPanel? panel)
	{
		if (panel == _focused || _switching) return;
		_switching = true;
		var prev = _focused;
		_focused = panel;
		prev?.OnBlur();
		_focused?.OnFocus();
		_switching = false;

		if (prev != null) PanelBorderHelper.Apply(prev.PanelNode, false);
		if (_focused != null) PanelBorderHelper.Apply(_focused.PanelNode, true);

		FocusChanged?.Invoke();
	}

	/// <summary>按 PanelId 聚焦。</summary>
	public void SetFocus(string? panelId)
	{
		if (panelId == null) { SetFocus((IPanel?)null); return; }
		var panel = _panels.Find(p => p.PanelId == panelId);
		if (panel != null) SetFocus(panel);
	}

	/// <summary>取消焦点（回到 Action 模式）。</summary>
	public void ClearFocus() => SetFocus((IPanel?)null);

	/// <summary>
	/// 将键盘事件转为命令字符串，转发给当前聚焦面板。
	/// 返回 true 表示已消费。如果无面板聚焦返回 false。
	/// </summary>
	public bool HandleKey(InputEventKey key)
	{
		if (_focused == null) return false;
		if (!key.Pressed) return false;

		var cmd = MapKeyToCommand(key);
		if (cmd == null) return true;

		if (cmd == "close"
			|| (cmd == "toggle_inv" && _focused?.PanelId == "inventory")
			|| (cmd == "toggle_quest" && _focused?.PanelId == "quest")
			|| (cmd == "toggle_skills" && _focused?.PanelId == "skill_mgr")
			|| (cmd == "toggle_status" && _focused?.PanelId == "status"))
		{
			ClearFocus();
			return true;
		}

		_focused?.HandleCommand(cmd);
		return true;
	}

	/// <summary>鼠标点击命中测试：返回被点击的面板，如果不是可聚焦面板返回 null。</summary>
	public IPanel? HitTest(Vector2 globalPos)
	{
		foreach (var p in _panels)
		{
			if (!p.Visible || !p.CanFocus) continue;
			if (p.PanelNode.GetGlobalRect().HasPoint(globalPos))
				return p;
		}
		return null;
	}

	/// <summary>刷新所有已注册面板的边框样式。</summary>
	public void RefreshBorders()
	{
		var focusedNode = _focused?.PanelNode;
		foreach (var node in _allNodes)
			PanelBorderHelper.Apply(node, node == focusedNode);
	}

	/// <summary>通用键盘→命令映射。各面板共享：W/S=上下，Esc=关闭，数字=选择，Enter=确认。</summary>
	private static string? MapKeyToCommand(InputEventKey key) => key.Keycode switch
	{
		Key.W or Key.Up    => "up",
		Key.S or Key.Down  => "down",
		Key.A or Key.Left  => "left",
		Key.D or Key.Right => "right",
		Key.Enter          => "confirm",
		Key.Escape         => "close",
		Key.E              => "action1",
		Key.Q              => "action2",
		Key.R              => "action3",
		Key.P              => "action4",
		Key.U              => "action5",
		Key.Tab            => key.ShiftPressed ? "tab_prev" : "tab_next",
		Key.H              => "toggle_status",
		Key.I              => "toggle_inv",
		Key.J              => "toggle_quest",
		Key.K              => "toggle_skills",
		Key.Key1 => "1", Key.Key2 => "2", Key.Key3 => "3",
		Key.Key4 => "4", Key.Key5 => "5", Key.Key6 => "6",
		Key.Key7 => "7", Key.Key8 => "8", Key.Key9 => "9",
		_ => null,
	};
}
