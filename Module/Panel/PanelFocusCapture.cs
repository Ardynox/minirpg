using Godot;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 面板打开时自动把 Godot 原生焦点落到第一个可交互 Control 上，
/// 方便键盘/手柄玩家打开面板后直接开始操作，不必先用鼠标或 Tab 找入口。
/// <para>
/// 注意：本项目大量 UI 用 <c>FocusMode = None</c> 走自有 <see cref="PanelManager"/> 焦点体系。
/// 对那些面板而言，原生 GrabFocus 只决定 <c>OptionButton</c> / <c>LineEdit</c> 等仍保留 FocusMode
/// 的控件是否直接响应空格/Enter。其余面板调用本 helper 不会有副作用。
/// </para>
/// <para>
/// 典型调用点：<c>public void OnFocus() { PanelFocusCapture.GrabFirstInteractive(_panel); }</c>
/// </para>
/// </summary>
public static class PanelFocusCapture
{
	public static void GrabFirstInteractive(Node? root)
	{
		if (root == null || !GodotObject.IsInstanceValid(root))
			return;

		var first = FindFirstFocusable(root);
		if (first != null && GodotObject.IsInstanceValid(first))
			first.GrabFocus();
	}

	private static Control? FindFirstFocusable(Node node)
	{
		foreach (var child in node.GetChildren())
		{
			if (child is not Node asNode)
				continue;

			if (asNode is Control control
				&& control.Visible
				&& !control.IsQueuedForDeletion()
				&& control.FocusMode != Control.FocusModeEnum.None
				&& IsInteractive(control))
			{
				return control;
			}

			var found = FindFirstFocusable(asNode);
			if (found != null)
				return found;
		}
		return null;
	}

	private static bool IsInteractive(Control control)
	{
		return control is Button or OptionButton or CheckButton or LineEdit or HSlider or VSlider;
	}
}
