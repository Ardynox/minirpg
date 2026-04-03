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
		string desired;
		if (selected)
			desired = transparentBg ? SelBtn : OpaqueSelBtn;
		else if (hovered)
			desired = transparentBg ? HoverBtn : OpaqueHoverBtn;
		else if (isContainer && transparentBg)
			desired = ContainerBtn;
		else
			desired = transparentBg ? RowBtn : OpaqueBtn;

		if (row.ThemeTypeVariation != desired)
			row.ThemeTypeVariation = desired;
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
