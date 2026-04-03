using Godot;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 统一的面板边框样式：通过 ThemeTypeVariation 切换焦点/空闲外观，
/// 避免每次调用 AddThemeStyleboxOverride 触发重绘。
/// Theme 中 "FocusedPanel" variation 定义绿色粗边框，
/// 默认 PanelContainer 样式为暗灰细边框。
/// </summary>
public static class PanelBorderHelper
{
	private const string FocusedVariation = "FocusedPanel";

	public static void Apply(PanelContainer panel, bool focused)
	{
		if (panel == null) return;
		var desired = focused ? FocusedVariation : "";
		if (panel.ThemeTypeVariation != desired)
			panel.ThemeTypeVariation = desired;
	}
}
