using System.Collections.Generic;
using System.Text;
using Godot;

namespace MiniRPG.Module;

public enum RenderMode
{
	Ascii,
	Emoji,
}

/// <summary>
/// 渲染模块：将二维字符地图转成可显示字符串。
/// 支持 ASCII（等宽 + BBCode 着色）和 Emoji（全 Emoji 等宽）两种模式。
/// 支持 "dim:" 前缀的暗色字符（多层预览视图模式使用）。
/// </summary>
public class RenderModule
{
	public RenderMode Mode { get; private set; } = RenderMode.Emoji;

	public bool UsesBBCode => Mode == RenderMode.Ascii;

	public void SetMode(RenderMode mode) => Mode = mode;

	public RenderMode ToggleMode()
	{
		Mode = Mode == RenderMode.Ascii ? RenderMode.Emoji : RenderMode.Ascii;
		return Mode;
	}

	public string RenderMap(List<List<string>> map)
	{
		var sb = new StringBuilder();
		foreach (var row in map)
		{
			foreach (var c in row)
				sb.Append(CellText(c));
			sb.Append('\n');
		}
		return sb.ToString();
	}

	public void ApplyFont(RichTextLabel panel)
	{
		if (Mode == RenderMode.Ascii)
		{
			var font = new SystemFont();
			font.FontNames = ["Consolas", "Courier New", "Liberation Mono", "monospace"];
			panel.AddThemeFontOverride("normal_font", font);
			panel.AddThemeFontSizeOverride("normal_font_size", 22);
		}
		else
		{
			panel.RemoveThemeFontOverride("normal_font");
			panel.RemoveThemeFontSizeOverride("normal_font_size");
		}
	}

	private string CellText(string c)
	{
		var dim = c.StartsWith("dim:");
		var raw = dim ? c[4..] : c;

		return Mode switch
		{
			RenderMode.Ascii => dim ? AsciiDimCell(raw) : AsciiCell(raw),
			RenderMode.Emoji => dim ? EmojiDimCell(raw) : EmojiCell(raw),
			_ => raw,
		};
	}

	private static string AsciiCell(string c) => c switch
	{
		"#" => "[color=#555555]██[/color]",
		"." => "[color=#333333]· [/color]",
		"~" => "[color=#2266cc]~~[/color]",
		"^" => "[color=#888888]^^[/color]",
		"T" => "[color=#22aa44]♣ [/color]",
		" " => "  ",
		"P" => "[color=#44ee44]@·[/color]",
		"M" => "[color=#ee4444]M·[/color]",
		"G" => "[color=#44cc44]G·[/color]",
		"S" => "[color=#44ddaa]S·[/color]",
		"K" => "[color=#cccccc]K·[/color]",
		"E" => "[color=#44aaff]E·[/color]",
		"V" => "[color=#88cc88]V·[/color]",
		"N" => "[color=#aa44ff]N·[/color]",
		"H" => "[color=#aa8844]H·[/color]",
		"D" => "[color=#ffaa00]D·[/color]",
		">" => "[color=#00ccff]▼·[/color]",
		"<" => "[color=#00ccff]▲·[/color]",
		"!" => "[color=#ffee44]!·[/color]",
		_ => c + " ",
	};

	/// <summary>暗色版本：用于多层预览中下层内容的渲染。</summary>
	private static string AsciiDimCell(string c) => c switch
	{
		"#" => "[color=#222222]░░[/color]",
		"." => "[color=#1a1a1a]· [/color]",
		"~" => "[color=#113355]~~[/color]",
		"^" => "[color=#444444]^^[/color]",
		"T" => "[color=#114422]♣ [/color]",
		_ => "[color=#222222]" + c + " [/color]",
	};

	private static string EmojiCell(string c) => c switch
	{
		"#" => "⬛",
		"." => "⬜",
		"~" => "🟦",
		"^" => "🔺",
		"T" => "🌲",
		" " => "  ",
		"P" => "🙂",
		"M" => "👾",
		"G" => "👺",
		"S" => "🟢",
		"K" => "💀",
		"N" => "🕳️",
		"H" => "🏠",
		"D" => "🚪",
		">" => "⬇️",
		"<" => "⬆️",
		"!" => "📦",
		"E" => "👴",
		"V" => "😐",
		_ => c,
	};

	/// <summary>暗色 Emoji 版本：用半透明方块或暗色符号表示下层。</summary>
	private static string EmojiDimCell(string c) => c switch
	{
		"#" => "◾",
		"." => "◽",
		"~" => "🔵",
		_ => "◾",
	};
}
