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

	private string CellText(string c) => Mode switch
	{
		RenderMode.Ascii => AsciiCell(c),
		RenderMode.Emoji => EmojiCell(c),
		_ => c,
	};

	private static string AsciiCell(string c) => c switch
	{
		"#" => "[color=#555555]██[/color]",
		"." => "[color=#333333]· [/color]",
		"P" => "[color=#44ee44]@·[/color]",
		"M" => "[color=#ee4444]M·[/color]",
		"G" => "[color=#44cc44]G·[/color]",
		"S" => "[color=#44ddaa]S·[/color]",
		"K" => "[color=#cccccc]K·[/color]",
		"T" => "[color=#ffcc44]T·[/color]",
		"E" => "[color=#44aaff]E·[/color]",
		"V" => "[color=#88cc88]V·[/color]",
		"N" => "[color=#aa44ff]N·[/color]",
		"H" => "[color=#aa8844]H·[/color]",
		"D" => "[color=#ffaa00]D·[/color]",
		">" => "[color=#00ccff]▼·[/color]",
		"<" => "[color=#00ccff]▲·[/color]",
		_ => c + " ",
	};

	private static string EmojiCell(string c) => c switch
	{
		"#" => "⬛",
		"." => "⬜",
		"P" => "🙂",
		"M" => "👾",
		"G" => "👺",
		"S" => "🟢",
		"K" => "💀",
		"T" => "🧑‍💼",
		"E" => "👴",
		"V" => "🧑",
		"N" => "🕳️",
		"H" => "🏠",
		"D" => "🚪",
		">" => "⬇️",
		"<" => "⬆️",
		_ => c,
	};
}
