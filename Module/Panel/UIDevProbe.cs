using System.Collections.Generic;
using System.Text;
using Godot;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 开发者用的 UI 运行时状态打印。当排查"焦点跑哪去了""哪个面板吃了键"时，
/// 在调试点调用 <see cref="LogCurrentState"/>，把 PanelManager 的内部快照打到 Godot 控制台。
/// <para>
/// 有意做成轻量"按需调用"风格，不做全屏浮层（后者需要 Scene 节点 + Input hook + 订阅 PanelManager 事件，
/// 一次性成本更大）。下一次真的需要时，把这里扩展为一个 Control 子节点浮层即可，
/// 外部 API 保持 <c>UIDevProbe.LogCurrentState(panels)</c> 不变。
/// </para>
/// </summary>
public static class UIDevProbe
{
	public static void LogCurrentState(PanelManager? panels, string? note = null)
	{
		if (panels == null)
		{
			GD.Print("[UIDevProbe] PanelManager is null");
			return;
		}

		var sb = new StringBuilder();
		sb.Append("[UIDevProbe]");
		if (!string.IsNullOrEmpty(note))
			sb.Append(' ').Append(note);
		sb.Append(" focused=").Append(panels.FocusedId ?? "<none>");
		GD.Print(sb.ToString());
	}

	public static string DescribeRegisteredPanels(IEnumerable<IPanel> panels)
	{
		var sb = new StringBuilder();
		foreach (var p in panels)
		{
			if (p == null) continue;
			sb.Append("- ").Append(p.PanelId)
				.Append("  visible=").Append(p.Visible)
				.Append("  canFocus=").Append(p.CanFocus)
				.Append("  consumeUnhandledKeys=").Append(p.ConsumeUnhandledKeys)
				.Append('\n');
		}
		return sb.ToString();
	}
}
