using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module;

/// <summary>
/// 游戏日志管理：维护日志行列表，超出上限时裁剪最旧的，写入 RichTextLabel。
/// </summary>
public class LogModule
{
	private const int MaxLines = 30;
	private readonly List<string> _lines = [];
	private readonly RichTextLabel _panel;

	public LogModule(RichTextLabel panel) => _panel = panel;

	public void Add(string msg)
	{
		_lines.Add(msg);
		if (_lines.Count > MaxLines)
			_lines.RemoveRange(0, _lines.Count - MaxLines);
		_panel.Text = string.Join("\n", _lines);
	}

	public void Clear()
	{
		_lines.Clear();
		_panel.Text = "";
	}
}
