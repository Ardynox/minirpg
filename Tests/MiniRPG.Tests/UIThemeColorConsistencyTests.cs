using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using MiniRPG.Module.Panel;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// UITheme.tres 与 UIColors 是 UI 配色的双口径：
/// C# 侧（UIColors 常量）供运行时代码用，Theme 侧（StyleBoxFlat）供场景编辑器用。
/// 核心色值（焦点金、空闲棕、面板底、行底、行悬停、行选中）必须两边对齐，
/// 否则改一处另一处会漂掉。这里不枚举全部色，只守护改色时最易错的核心集合。
/// </summary>
public sealed class UIThemeColorConsistencyTests
{
	private static readonly Regex ColorLiteralRegex = new(
		@"Color\(\s*(-?\d*\.?\d+)\s*,\s*(-?\d*\.?\d+)\s*,\s*(-?\d*\.?\d+)(?:\s*,\s*(-?\d*\.?\d+))?\s*\)",
		RegexOptions.Compiled);

	[Fact]
	public void Core_UIColors_constants_are_present_in_theme_file()
	{
		var themeColors = LoadThemeColors();
		var expected = new (Color Color, string Name)[]
		{
			(UIColors.FocusBorder, nameof(UIColors.FocusBorder)),
			(UIColors.IdleBorder, nameof(UIColors.IdleBorder)),
			(UIColors.PanelBg, nameof(UIColors.PanelBg)),
			(UIColors.RowBg, nameof(UIColors.RowBg)),
			(UIColors.HoverBg, nameof(UIColors.HoverBg)),
			(UIColors.SelectedBg, nameof(UIColors.SelectedBg)),
			(UIColors.TextNormal, nameof(UIColors.TextNormal)),
			(UIColors.TextSelected, nameof(UIColors.TextSelected)),
		};

		var missing = expected
			.Where(entry => !themeColors.Any(c => RgbApproxEqual(c, entry.Color)))
			.Select(entry => $"{entry.Name} = {FormatColor(entry.Color)}")
			.ToArray();

		Assert.True(
			missing.Length == 0,
			"UIColors 里的核心颜色在 UITheme.tres 中找不到匹配的 StyleBox 色值，可能改一处忘了同步另一处：\n  " +
				string.Join("\n  ", missing));
	}

	private static IReadOnlyList<Color> LoadThemeColors()
	{
		var path = Path.Combine(ResolveRepoRoot(), "Assets", "UI", "Themes", "UITheme.tres");
		Assert.True(File.Exists(path), $"UITheme.tres 不存在：{path}");

		var text = File.ReadAllText(path);
		var colors = new List<Color>();
		foreach (Match match in ColorLiteralRegex.Matches(text))
		{
			var r = float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
			var g = float.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
			var b = float.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
			var a = match.Groups[4].Success
				? float.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture)
				: 1f;
			colors.Add(new Color(r, g, b, a));
		}
		return colors;
	}

	private static bool RgbApproxEqual(Color a, Color b, float tolerance = 0.005f)
	{
		return Math.Abs(a.R - b.R) < tolerance
			&& Math.Abs(a.G - b.G) < tolerance
			&& Math.Abs(a.B - b.B) < tolerance;
	}

	private static string FormatColor(Color c) =>
		$"Color({c.R.ToString("0.##", CultureInfo.InvariantCulture)}, "
		+ $"{c.G.ToString("0.##", CultureInfo.InvariantCulture)}, "
		+ $"{c.B.ToString("0.##", CultureInfo.InvariantCulture)})";

	private static string ResolveRepoRoot()
	{
		var current = new DirectoryInfo(AppContext.BaseDirectory);
		while (current != null)
		{
			if (File.Exists(Path.Combine(current.FullName, "project.godot")))
				return current.FullName;
			current = current.Parent;
		}
		throw new Xunit.Sdk.XunitException("Failed to locate repository root from test base directory.");
	}
}
