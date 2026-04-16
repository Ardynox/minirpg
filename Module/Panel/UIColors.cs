using Godot;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 全局 UI 颜色常量 — 深色奇幻风（深蓝 + 金铜）配色。
/// 所有面板模块共享同一套配色，避免硬编码 Color。
/// 与 <c>Assets/UI/Themes/UITheme.tres</c> 的 StyleBoxFlat 色值构成双口径；
/// 核心色（FocusBorder / IdleBorder / PanelBg / RowBg / HoverBg / SelectedBg /
/// TextNormal / TextSelected）由 <c>Tests/MiniRPG.Tests/UIThemeColorConsistencyTests.cs</c>
/// 守护，改一处忘了同步另一处会被 xUnit 直接抓出来。新增常量时，若它只出现在 C#
/// 代码里（不参与 Theme 样式），不必登记进守护集合。
/// </summary>
public static class UIColors
{
	// ── 行背景 ──
	public static readonly Color RowBg = new(0.1f, 0.1f, 0.16f);
	public static readonly Color RowBgTransparent = new(0, 0, 0, 0);
	public static readonly Color HoverBg = new(0.14f, 0.14f, 0.22f);
	public static readonly Color SelectedBg = new(0.18f, 0.15f, 0.08f);

	// ── 文字 ──
	public static readonly Color TextNormal = new(0.82f, 0.8f, 0.75f);
	public static readonly Color TextSelected = new(1f, 0.88f, 0.45f);
	public static readonly Color TextDim = new(0.42f, 0.4f, 0.36f);
	public static readonly Color TextContainer = new(0.55f, 0.85f, 1f);
	public static readonly Color TextEquipped = new(1f, 0.82f, 0.2f);
	public static readonly Color TextHeader = new(0.92f, 0.85f, 0.7f);

	// ── 功能色 ──
	public static readonly Color TextCombat = new(1f, 0.45f, 0.4f);
	public static readonly Color TextSocial = new(0.45f, 0.9f, 0.6f);
	public static readonly Color TextUtility = new(0.45f, 0.75f, 1f);
	public static readonly Color TextWarning = new(0.96f, 0.5f, 0.38f);
	public static readonly Color TextSuccess = new(0.5f, 0.92f, 0.55f);
	public static readonly Color TextMoodGood = new(0.5f, 0.92f, 0.55f);

	// ── 边框 / 强调 ──
	public static readonly Color FocusBorder = new(0.85f, 0.7f, 0.3f);
	public static readonly Color IdleBorder = new(0.27f, 0.22f, 0.16f);
	public static readonly Color PanelBg = new(0.085f, 0.092f, 0.148f);

	// ── BBCode hex 常量（用于 RichTextLabel 内嵌颜色） ──
	public const string HexSelected = "#ffe073";
	public const string HexNormal = "#d1ccc0";
	public const string HexDim = "#6b665c";
	public const string HexCombat = "#ff7366";
	public const string HexSocial = "#73e699";
	public const string HexUtility = "#73bfff";
	public const string HexWarning = "#f5804d";
	public const string HexSuccess = "#80eb8c";
	public const string HexHeader = "#ebd9b3";
	public const string HexEquipped = "#ffd133";
}
