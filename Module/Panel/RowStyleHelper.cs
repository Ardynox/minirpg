using Godot;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 面板内 Button 行的统一样式和滚动辅助。
/// 通过 ThemeTypeVariation 切换样式，完全避免运行时 AddThemeColorOverride。
/// </summary>
public static class RowStyleHelper
{
	private const string RowBtn = "RowButton";
	private const string HoverBtn = "HoveredRowButton";
	private const string SelBtn = "SelectedRowButton";
	private const string OpaqueBtn = "OpaqueRowButton";
	private const string OpaqueHoverBtn = "OpaqueHoveredRowButton";
	private const string OpaqueSelBtn = "OpaqueSelectedRowButton";
	private const string ContainerBtn = "ContainerRowButton";

	public static void Apply(Button row, bool selected, bool hovered,
		bool isContainer = false, bool transparentBg = false)
	{
		var desired = ResolveThemeVariation(selected, hovered, isContainer, transparentBg);
		if (row.ThemeTypeVariation != desired)
			row.ThemeTypeVariation = desired;
	}

	public static void EnsureVisible(ScrollContainer scroll, Control row)
	{
		var targetScroll = GetVisibleScrollPosition(row.Position.Y, row.Size.Y, scroll.ScrollVertical, scroll.Size.Y);
		if (targetScroll.HasValue)
			scroll.ScrollVertical = targetScroll.Value;
	}

	private static string ResolveThemeVariation(bool selected, bool hovered, bool isContainer, bool transparentBg)
	{
		if (selected)
			return transparentBg ? SelBtn : OpaqueSelBtn;
		if (hovered)
			return transparentBg ? HoverBtn : OpaqueHoverBtn;
		if (isContainer && transparentBg)
			return ContainerBtn;
		return transparentBg ? RowBtn : OpaqueBtn;
	}

	private static int? GetVisibleScrollPosition(float rowTop, float rowHeight, int scrollTop, float viewportHeight)
	{
		var rowBot = rowTop + rowHeight;
		var scrollBot = scrollTop + viewportHeight;

		if (rowTop < scrollTop)
			return (int)rowTop;
		if (rowBot > scrollBot)
			return (int)(rowBot - viewportHeight);
		return null;
	}
}
