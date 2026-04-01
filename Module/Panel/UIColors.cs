using Godot;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 全局 UI 颜色常量，所有面板模块共享同一套配色。
/// </summary>
public static class UIColors
{
	// ── 行背景 ──
	public static readonly Color RowBg = new(0.12f, 0.12f, 0.15f);
	public static readonly Color RowBgTransparent = new(0, 0, 0, 0);
	public static readonly Color HoverBg = new(0.22f, 0.22f, 0.28f);
	public static readonly Color SelectedBg = new(0.15f, 0.25f, 0.2f);

	// ── 文字 ──
	public static readonly Color TextNormal = new(0.8f, 0.8f, 0.8f);
	public static readonly Color TextSelected = new(0.6f, 1f, 0.7f);
	public static readonly Color TextDim = new(0.5f, 0.5f, 0.5f);
	public static readonly Color TextContainer = new(0.6f, 0.9f, 1f);
	public static readonly Color TextEquipped = new(1f, 0.8f, 0f);

	// ── 边框 / 强调 ──
	public static readonly Color FocusBorder = new(0.3f, 0.8f, 0.4f);
	public static readonly Color IdleBorder = new(0.5f, 0.5f, 0.55f);
	public static readonly Color PanelBg = new(0.1f, 0.1f, 0.13f);
}
