using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 面板内 Button 行的统一样式和滚动辅助。
/// StyleBoxFlat 按视觉状态缓存，避免每次调用都分配新对象。
/// </summary>
public static class RowStyleHelper
{
	private static readonly Dictionary<(Color Bg, bool Selected), StyleBoxFlat> _cache = new();

	public static void Apply(Button row, bool selected, bool hovered,
		Color? textOverride = null, bool transparentBg = false)
	{
		Color bg;
		if (selected) bg = UIColors.SelectedBg;
		else if (hovered) bg = UIColors.HoverBg;
		else bg = transparentBg ? UIColors.RowBgTransparent : UIColors.RowBg;

		var sb = GetOrCreate(bg, selected);

		row.AddThemeStyleboxOverride("normal", sb);
		row.AddThemeStyleboxOverride("hover", sb);
		row.AddThemeStyleboxOverride("pressed", sb);

		var fg = textOverride ?? (selected ? UIColors.TextSelected : UIColors.TextNormal);
		row.AddThemeColorOverride("font_color", fg);
		row.AddThemeColorOverride("font_hover_color", fg);
	}

	private static StyleBoxFlat GetOrCreate(Color bg, bool selected)
	{
		var key = (bg, selected);
		if (_cache.TryGetValue(key, out var cached))
			return cached;

		var sb = new StyleBoxFlat
		{
			BgColor = bg,
			ContentMarginLeft = selected ? 6 : 4,
			ContentMarginRight = 4,
		};

		if (selected)
		{
			sb.BorderWidthLeft = 3;
			sb.BorderColor = UIColors.FocusBorder;
		}

		_cache[key] = sb;
		return sb;
	}

	public static void EnsureVisible(ScrollContainer scroll, Control row)
	{
		var rowTop = row.Position.Y;
		var rowBot = rowTop + row.Size.Y;
		var scrollTop = scroll.ScrollVertical;
		var scrollBot = scrollTop + scroll.Size.Y;

		if (rowTop < scrollTop)
			scroll.ScrollVertical = (int)rowTop;
		else if (rowBot > scrollBot)
			scroll.ScrollVertical = (int)(rowBot - scroll.Size.Y);
	}
}
