using System.Collections.Generic;
using System.Text;
using Godot;

namespace MiniRPG.Module.Render;

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
		if (c.StartsWith("fog:"))
			return Mode == RenderMode.Ascii ? "[color=#111111]░░[/color]" : "⬛";

		if (c.StartsWith("per:"))
		{
			var raw = c[4..];
			if (raw.StartsWith("dim:")) raw = raw[4..];
			return Mode switch
			{
				RenderMode.Ascii => AsciiPerCell(raw),
				RenderMode.Emoji => EmojiPerCell(raw),
				_ => raw,
			};
		}

		if (c.StartsWith("mem:"))
		{
			var raw = c[4..];
			if (raw.StartsWith("dim:")) raw = raw[4..];
			return Mode switch
			{
				RenderMode.Ascii => AsciiMemCell(raw),
				RenderMode.Emoji => EmojiMemCell(raw),
				_ => raw,
			};
		}

		var dim = c.StartsWith("dim:");
		var glyph = dim ? c[4..] : c;

		return Mode switch
		{
			RenderMode.Ascii => dim ? AsciiDimCell(glyph) : AsciiCell(glyph),
			RenderMode.Emoji => dim ? EmojiDimCell(glyph) : EmojiCell(glyph),
			_ => glyph,
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
		"C" => "[color=#cc8844]C·[/color]",
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
		" " => "　",
		"P" => "🙂",
		"M" => "👾",
		"G" => "👺",
		"S" => "🟢",
		"K" => "💀",
		"N" => "🕳️",
		"H" => "🏠",
		"D" => "🚪",
		">" => "⏬",
		"<" => "⏫",
		"!" => "📦",
		"C" => "🗃️",
		"E" => "👴",
		"V" => "😐",
		_ => c,
	};

	/// <summary>暗色 Emoji 版本：用于多层预览中下层内容的渲染。</summary>
	private static string EmojiDimCell(string c) => c switch
	{
		"#" => "⬛",
		"." => "⬜",
		"~" => "⬛",
		_ => "⬛",
	};

	/// <summary>记忆态 ASCII：已探索但当前不可见，极暗色只显示地形轮廓。</summary>
	private static string AsciiMemCell(string c) => c switch
	{
		"#" => "[color=#222222]██[/color]",
		"." => "[color=#1a1a1a]· [/color]",
		"~" => "[color=#112233]~~[/color]",
		"^" => "[color=#333333]^^[/color]",
		"T" => "[color=#112211]♣ [/color]",
		">" => "[color=#113344]▼·[/color]",
		"<" => "[color=#113344]▲·[/color]",
		" " => "  ",
		_ => "[color=#1a1a1a]" + c + " [/color]",
	};

	/// <summary>记忆态 Emoji：已探索但当前不可见，用暗色全尺寸方块区分地形。</summary>
	private static string EmojiMemCell(string c) => c switch
	{
		"#" => "⬛",
		"." => "🟫",
		"~" => "⬛",
		"^" => "⬛",
		"T" => "⬛",
		">" => "🟫",
		"<" => "🟫",
		" " => "⬛",
		_ => "⬛",
	};

	/// <summary>周边感知态 ASCII：在全向视野内但不在朝向锥内，灰色显示实时内容。</summary>
	private static string AsciiPerCell(string c) => c switch
	{
		"#" => "[color=#444444]██[/color]",
		"." => "[color=#2a2a2a]· [/color]",
		"~" => "[color=#1a4466]~~[/color]",
		"^" => "[color=#666666]^^[/color]",
		"T" => "[color=#1a6633]♣ [/color]",
		" " => "  ",
		"P" => "[color=#338833]@·[/color]",
		"M" => "[color=#993333]M·[/color]",
		"G" => "[color=#338833]G·[/color]",
		"S" => "[color=#339977]S·[/color]",
		"K" => "[color=#888888]K·[/color]",
		"E" => "[color=#336688]E·[/color]",
		"V" => "[color=#668866]V·[/color]",
		"N" => "[color=#773399]N·[/color]",
		"H" => "[color=#776633]H·[/color]",
		"D" => "[color=#997700]D·[/color]",
		">" => "[color=#006688]▼·[/color]",
		"<" => "[color=#006688]▲·[/color]",
		"!" => "[color=#998833]!·[/color]",
		"C" => "[color=#886633]C·[/color]",
		_ => "[color=#444444]" + c + " [/color]",
	};

	/// <summary>周边感知态 Emoji：灰色调但保留实时内容（含怪物）。</summary>
	private static string EmojiPerCell(string c) => c switch
	{
		"#" => "⬛",
		"." => "🔲",
		"~" => "🔵",
		"^" => "⬛",
		"T" => "🌑",
		" " => "⬛",
		"P" => "🙂",
		"M" => "👾",
		"G" => "👺",
		"S" => "🟢",
		"K" => "💀",
		"N" => "🕳️",
		"H" => "🏠",
		"D" => "🚪",
		">" => "⏬",
		"<" => "⏫",
		"!" => "📦",
		"C" => "🗃️",
		"E" => "👴",
		"V" => "😐",
		_ => "⬛",
	};
}
