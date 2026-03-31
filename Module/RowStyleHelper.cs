using Godot;

namespace MiniRPG.Module;

/// <summary>
/// 面板内 Button 行的统一样式和滚动辅助。
/// </summary>
public static class RowStyleHelper
{
	public static void Apply(Button row, bool selected, bool hovered,
		Color? textOverride = null, bool transparentBg = false)
	{
		Color bg;
		if (selected) bg = UIColors.SelectedBg;
		else if (hovered) bg = UIColors.HoverBg;
		else bg = transparentBg ? UIColors.RowBgTransparent : UIColors.RowBg;

		var sb = new StyleBoxFlat
		{
			BgColor = bg,
			ContentMarginLeft = 4,
			ContentMarginRight = 4,
		};

		if (selected)
		{
			sb.BorderWidthLeft = 3;
			sb.BorderColor = UIColors.FocusBorder;
			sb.ContentMarginLeft = 6;
		}

		row.AddThemeStyleboxOverride("normal", sb);
		row.AddThemeStyleboxOverride("hover", sb);
		row.AddThemeStyleboxOverride("pressed", sb);

		var fg = textOverride ?? (selected ? UIColors.TextSelected : UIColors.TextNormal);
		row.AddThemeColorOverride("font_color", fg);
		row.AddThemeColorOverride("font_hover_color", fg);
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
