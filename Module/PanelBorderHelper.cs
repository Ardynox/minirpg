using Godot;

namespace MiniRPG.Module;

/// <summary>
/// 统一的面板边框样式：焦点面板用绿色粗边框，非焦点用暗灰细边框。
/// 缓存两个 StyleBoxFlat 实例避免每帧重建。
/// </summary>
public static class PanelBorderHelper
{
	private static StyleBoxFlat? _focused;
	private static StyleBoxFlat? _idle;

	public static void Apply(PanelContainer panel, bool focused)
	{
		if (panel == null) return;
		panel.AddThemeStyleboxOverride("panel", focused ? GetFocused() : GetIdle());
	}

	private static StyleBoxFlat GetFocused() => _focused ??= Build(UIColors.FocusBorder);
	private static StyleBoxFlat GetIdle() => _idle ??= Build(UIColors.IdleBorder);

	private static StyleBoxFlat Build(Color border) => new()
	{
		BgColor = UIColors.PanelBg,
		BorderColor = border,
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
}
