using Godot;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 统一的面板边框样式：通过 ThemeTypeVariation 切换焦点/空闲外观，
/// 避免每次调用 AddThemeStyleboxOverride 触发重绘。
/// Theme 中 "FocusedPanel" variation 定义金色粗边框，
/// 默认 PanelContainer 样式为暗灰细边框。
/// 面板由无焦点切到有焦点时，额外触发一次阴影脉动作为注意力反馈。
/// </summary>
public static class PanelBorderHelper
{
	private const string FocusedVariation = "FocusedPanel";
	private const string FloatingVariation = "FloatingPanel";
	private const string FloatingFocusedVariation = "FloatingFocusedPanel";
	private const string WasFocusedMeta = "__panel_border_was_focused";
	private const double PulseDurationSec = 0.32;
	private const int PulseExtraShadow = 12;

	public static void Apply(PanelContainer panel, bool focused, bool floating = false)
	{
		if (panel == null) return;
		string desired;
		if (floating)
			desired = focused ? FloatingFocusedVariation : FloatingVariation;
		else
			desired = focused ? FocusedVariation : "";
		if (panel.ThemeTypeVariation != desired)
			panel.ThemeTypeVariation = desired;

		var wasFocused = panel.HasMeta(WasFocusedMeta) && panel.GetMeta(WasFocusedMeta).AsBool();
		panel.SetMeta(WasFocusedMeta, focused);
		if (focused && !wasFocused)
			TriggerFocusPulse(panel);
	}

	private static void TriggerFocusPulse(PanelContainer panel)
	{
		if (!GodotObject.IsInstanceValid(panel))
			return;

		if (panel.GetThemeStylebox("panel") is not StyleBoxFlat baseStyle)
			return;

		var pulse = (StyleBoxFlat)baseStyle.Duplicate();
		pulse.ShadowSize = baseStyle.ShadowSize + PulseExtraShadow;
		pulse.ShadowColor = new Color(0.95f, 0.82f, 0.35f, 0.55f);
		panel.AddThemeStyleboxOverride("panel", pulse);

		var tree = panel.GetTree();
		if (tree == null)
		{
			panel.RemoveThemeStyleboxOverride("panel");
			return;
		}

		var timer = tree.CreateTimer(PulseDurationSec, processAlways: true);
		timer.Timeout += () =>
		{
			if (GodotObject.IsInstanceValid(panel))
				panel.RemoveThemeStyleboxOverride("panel");
		};
	}
}
