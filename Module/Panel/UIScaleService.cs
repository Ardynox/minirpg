using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 全局 UI 字号缩放。
/// <para>
/// 通过修改顶层 <see cref="Theme"/> 的 <c>default_font_size</c> 在运行时放大/缩小所有未
/// 单独 override 字号的 Label / Button / 面板文字。HUD 和各面板默认跟随 Theme，
/// 所以一处调整即可全局生效。
/// </para>
/// <para>
/// 除了字号，还会同步缩放关键 <see cref="StyleBoxFlat"/> 的 <c>content_margin_*</c> 和
/// <c>shadow_size</c>，让 1.5x 时按钮不会因 padding 不变而显得挤、阴影比例不协调。
/// 首次 <see cref="Apply"/> 时快照 baseline，后续 Apply 都从 baseline 计算，避免连续缩放时 drift。
/// </para>
/// <para>
/// 只影响 Control 侧字号/padding/shadow，不改 CanvasLayer / Viewport 的全局 scale，
/// 避免把游戏世界（体素地图、角色精灵）也一起拉大。
/// </para>
/// </summary>
public static class UIScaleService
{
	/// <summary>
	/// Theme 里写死的 baseline 字号，与 <c>UITheme.tres</c> 的 <c>default_font_size = 15</c> 对齐。
	/// </summary>
	public const int BaselineFontSize = 15;

	public const float MinScale = 0.75f;
	public const float MaxScale = 1.5f;
	public const float DefaultScale = 1.0f;

	/// <summary>
	/// 会参与按比例缩放的 Theme 条目（ThemeType + StyleName）。
	/// 只挑影响按钮/输入框/面板边距与阴影的常用条目；其它 stylebox 保持固定以避免视觉漂移。
	/// </summary>
	private static readonly (string ThemeType, string StyleName)[] ScalableEntries =
	[
		("ActionButton", "normal"),
		("ActionButton", "hover"),
		("ActionButton", "pressed"),
		("ActionButton", "focus"),
		("ActionButton", "disabled"),
		("OptionButton", "normal"),
		("OptionButton", "hover"),
		("OptionButton", "pressed"),
		("OptionButton", "focus"),
		("TabButton", "normal"),
		("TabButton", "hover"),
		("TabButton", "pressed"),
		("TabButton", "focus"),
		("LineEdit", "normal"),
		("LineEdit", "focus"),
		("LineEdit", "read_only"),
		("PanelContainer", "panel"),
		("FocusedPanel", "panel"),
		("FloatingPanel", "panel"),
		("FloatingFocusedPanel", "panel"),
		("TooltipPanel", "panel"),
		("PopupMenu", "panel"),
	];

	private static readonly Dictionary<StyleBoxFlat, Baseline> BaselineCache = new();

	public static float CurrentScale { get; private set; } = DefaultScale;

	public static event Action<float>? ScaleChanged;

	/// <summary>
	/// 对给定 Theme 应用缩放，并广播 <see cref="ScaleChanged"/>。允许 Theme 为 null（无操作）。
	/// </summary>
	public static void Apply(Theme? theme, float scale)
	{
		var clamped = Clamp(scale);
		CurrentScale = clamped;

		if (theme != null)
		{
			theme.DefaultFontSize = Math.Max(1, (int)Math.Round(BaselineFontSize * clamped));
			ApplyStyleBoxScale(theme, clamped);
		}

		ScaleChanged?.Invoke(clamped);
	}

	public static float Clamp(float scale)
	{
		if (float.IsNaN(scale) || float.IsInfinity(scale))
			return DefaultScale;
		return Math.Clamp(scale, MinScale, MaxScale);
	}

	private static void ApplyStyleBoxScale(Theme theme, float scale)
	{
		foreach (var (themeType, styleName) in ScalableEntries)
		{
			if (!theme.HasStylebox(styleName, themeType))
				continue;

			if (theme.GetStylebox(styleName, themeType) is not StyleBoxFlat box)
				continue;

			if (!BaselineCache.TryGetValue(box, out var baseline))
			{
				baseline = CaptureBaseline(box);
				BaselineCache[box] = baseline;
			}

			box.ContentMarginLeft = baseline.ContentMarginLeft * scale;
			box.ContentMarginTop = baseline.ContentMarginTop * scale;
			box.ContentMarginRight = baseline.ContentMarginRight * scale;
			box.ContentMarginBottom = baseline.ContentMarginBottom * scale;
			box.ShadowSize = Math.Max(0, (int)Math.Round(baseline.ShadowSize * scale));
		}
	}

	private static Baseline CaptureBaseline(StyleBoxFlat box) =>
		new(
			box.ContentMarginLeft,
			box.ContentMarginTop,
			box.ContentMarginRight,
			box.ContentMarginBottom,
			box.ShadowSize);

	private readonly record struct Baseline(
		float ContentMarginLeft,
		float ContentMarginTop,
		float ContentMarginRight,
		float ContentMarginBottom,
		int ShadowSize);
}
