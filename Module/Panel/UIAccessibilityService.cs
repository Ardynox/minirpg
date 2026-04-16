using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Panel;

public enum UIContrastMode
{
	Normal,
	High,
}

public enum UIColorBlindMode
{
	None,
	Protanopia,    // 红色盲：削弱红通道
	Deuteranopia,  // 绿色盲：削弱绿通道
	Tritanopia,    // 蓝色盲：削弱蓝通道
}

/// <summary>
/// UI 可访问性配置服务：高对比度与色盲替代配色。
/// <para>
/// 与 <see cref="UIScaleService"/> 同级独立，不相互依赖。启动时从 <c>AppSettingsStore</c> 读取
/// 当前模式并 <see cref="Apply"/> 一次；Settings 面板切换时再调 Apply。
/// </para>
/// <para>
/// 实现策略：对几组 Theme 内置类型的 <c>font_color</c> 与 <c>font_outline_color</c> 做 override，
/// 首次 Apply 时快照 baseline，后续模式切回 Normal 时按 baseline 恢复。
/// 不改 StyleBoxFlat（避免和 <see cref="UIScaleService"/> 冲突）。
/// </para>
/// <para>
/// 当前版本只实现接入点和数据结构，实际 override 集合在 <see cref="OverrideEntries"/> 中列出，
/// 若后续对单项视觉要做细调只需在那里扩展，不用改 Apply 流程。
/// </para>
/// </summary>
public static class UIAccessibilityService
{
	public static UIContrastMode CurrentContrastMode { get; private set; } = UIContrastMode.Normal;
	public static UIColorBlindMode CurrentColorBlindMode { get; private set; } = UIColorBlindMode.None;

	public static event Action<UIContrastMode, UIColorBlindMode>? Changed;

	/// <summary>需要根据模式调色的 Theme 条目（ThemeType + StyleName）。</summary>
	private static readonly (string ThemeType, string ColorName)[] OverrideEntries =
	[
		("Label", "font_color"),
		("RichTextLabel", "default_color"),
		("ActionButton", "font_color"),
		("ActionButton", "font_hover_color"),
		("ActionButton", "font_pressed_color"),
		("ActionButton", "font_disabled_color"),
		("HeaderLabel", "font_color"),
		("HintLabel", "font_color"),
	];

	private static readonly Dictionary<(string, string), Color> BaselineCache = new();

	public static void Apply(Theme? theme, UIContrastMode contrast, UIColorBlindMode colorBlind)
	{
		CurrentContrastMode = contrast;
		CurrentColorBlindMode = colorBlind;

		if (theme != null)
			ApplyToTheme(theme, contrast, colorBlind);

		Changed?.Invoke(contrast, colorBlind);
	}

	private static void ApplyToTheme(Theme theme, UIContrastMode contrast, UIColorBlindMode colorBlind)
	{
		foreach (var entry in OverrideEntries)
		{
			if (!theme.HasColor(entry.ColorName, entry.ThemeType))
				continue;

			if (!BaselineCache.TryGetValue(entry, out var baseline))
			{
				baseline = theme.GetColor(entry.ColorName, entry.ThemeType);
				BaselineCache[entry] = baseline;
			}

			var target = baseline;
			if (contrast == UIContrastMode.High)
				target = LiftToHighContrast(baseline);
			if (colorBlind != UIColorBlindMode.None)
				target = ShiftForColorBlind(target, colorBlind);

			theme.SetColor(entry.ColorName, entry.ThemeType, target);
		}
	}

	private static Color LiftToHighContrast(Color src)
	{
		// 把亮度推向 1，保留 hue；灰阶值 < 0.5 的会被"撑亮"到至少 0.85
		var maxChannel = Mathf.Max(src.R, Mathf.Max(src.G, src.B));
		if (maxChannel >= 0.85f)
			return src;

		var lift = 0.85f / Mathf.Max(maxChannel, 0.05f);
		return new Color(
			Mathf.Min(src.R * lift, 1f),
			Mathf.Min(src.G * lift, 1f),
			Mathf.Min(src.B * lift, 1f),
			src.A);
	}

	private static Color ShiftForColorBlind(Color src, UIColorBlindMode mode)
	{
		// 简化：对受影响通道做衰减，用亮度保持视觉层级；不做 LMS 专业矩阵，避免视觉偏差过大。
		return mode switch
		{
			UIColorBlindMode.Protanopia => new Color(src.R * 0.55f, src.G, src.B, src.A),
			UIColorBlindMode.Deuteranopia => new Color(src.R, src.G * 0.55f, src.B, src.A),
			UIColorBlindMode.Tritanopia => new Color(src.R, src.G, src.B * 0.55f, src.A),
			_ => src,
		};
	}
}
