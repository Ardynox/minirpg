using Godot;

namespace MiniRPG.Module;

/// <summary>
/// 统一的面板边框样式：焦点面板用绿色粗边框，非焦点用暗灰细边框。
/// </summary>
public static class PanelBorderHelper
{
	private static readonly Color FocusBorder = new(0.3f, 0.8f, 0.4f);
	private static readonly Color IdleBorder = new(0.5f, 0.5f, 0.55f);
	private static readonly Color PanelBg = new(0.1f, 0.1f, 0.13f);

	public static void Apply(PanelContainer panel, bool focused)
	{
		if (panel == null) return;
		var style = new StyleBoxFlat
		{
			BgColor = PanelBg,
			BorderColor = focused ? FocusBorder : IdleBorder,
			BorderWidthTop = 2,
			BorderWidthBottom = 2,
			BorderWidthLeft = 2,
			BorderWidthRight = 2,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4,
			ContentMarginLeft = 2,
			ContentMarginTop = 2,
			ContentMarginRight = 2,
			ContentMarginBottom = 2,
		};
		panel.AddThemeStyleboxOverride("panel", style);
	}
}
