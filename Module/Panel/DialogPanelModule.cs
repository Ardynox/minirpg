using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 对话面板 UI：显示 NPC 名字、对话文本、可选回复列表。
/// 玩家通过数字键或鼠标点击选择选项。
/// </summary>
public class DialogPanelModule : IPanel
{
	public string PanelId => "dialog";
	public PanelContainer PanelNode => _panel;
	bool IPanel.Visible { get => _panel.Visible; set => _panel.Visible = value; }

	bool IPanel.HandleCommand(string cmd)
	{
		switch (cmd)
		{
			case "up": MoveCursor(-1); return true;
			case "down": MoveCursor(1); return true;
			case "confirm": Confirm(); return true;
			default:
				if (int.TryParse(cmd, out var num)) { SelectByNumber(num); return true; }
				return false;
		}
	}

	/// <summary>面板失焦时通知 DialogUIModule 清理对话状态。</summary>
	public event Action? OnDialogClosed;
	void IPanel.OnBlur() => OnDialogClosed?.Invoke();

	public event Action<int>? OnOptionSelected;

	private readonly PanelContainer _panel;
	private readonly RichTextLabel _header;
	private readonly RichTextLabel _dialogText;
	private readonly VBoxContainer _optionList;
	private readonly Label _hintBar;
	private readonly List<Button> _optionRows = [];
	private int _cursor;
	private int _hoverIndex = -1;

	public bool Dirty { get; set; }
	public void FlushIfDirty() { }

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public DialogPanelModule(PanelContainer panel)
	{
		_panel = panel;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_header = vbox.GetNode<RichTextLabel>("Header");
		_dialogText = vbox.GetNode<RichTextLabel>("DialogText");
		_optionList = vbox.GetNode<VBoxContainer>("OptionList");
		_hintBar = vbox.GetNode<Label>("HintBar");
	}

	/// <summary>
	/// 显示一条对话：NPC 名字、情绪图标、对话内容、可选回复。
	/// </summary>
	public void Show(string npcName, string moodIcon, string dialogContent, List<string> options)
	{
		_header.Clear();
		_header.AppendText($"[center]── {npcName}  {moodIcon} ──[/center]");

		_dialogText.Clear();
		_dialogText.AppendText(dialogContent);

		RebuildOptions(options);
		_cursor = 0;
		UpdateRowVisuals();

		Visible = true;
	}

	/// <summary>显示无选项的终结节点（自动关闭提示）。</summary>
	public void ShowEnd(string npcName, string moodIcon, string dialogContent)
	{
		Show(npcName, moodIcon, dialogContent, ["（结束对话）"]);
	}

	public void Close()
	{
		Visible = false;
		ClearOptionRows();
	}

	public void MoveCursor(int delta)
	{
		if (_optionRows.Count == 0) return;
		_cursor = Math.Clamp(_cursor + delta, 0, _optionRows.Count - 1);
		UpdateRowVisuals();
	}

	public void Confirm()
	{
		if (_cursor >= 0 && _cursor < _optionRows.Count)
			OnOptionSelected?.Invoke(_cursor);
	}

	public void SelectByNumber(int number)
	{
		var index = number - 1;
		if (index >= 0 && index < _optionRows.Count)
		{
			_cursor = index;
			UpdateRowVisuals();
			OnOptionSelected?.Invoke(index);
		}
	}

	public int OptionCount => _optionRows.Count;

	private void RebuildOptions(List<string> options)
	{
		ClearOptionRows();

		for (var i = 0; i < options.Count; i++)
		{
			var row = new Button
			{
				Flat = true,
				FocusMode = Control.FocusModeEnum.None,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				CustomMinimumSize = new Vector2(0, 28),
				Alignment = HorizontalAlignment.Left,
				ClipText = true,
				Text = $"  [{i + 1}] {options[i]}",
			};

			var idx = i;
			row.Pressed += () =>
			{
				_cursor = idx;
				UpdateRowVisuals();
				OnOptionSelected?.Invoke(idx);
			};
			row.MouseEntered += () => { _hoverIndex = idx; UpdateRowVisuals(); };
			row.MouseExited += () => { if (_hoverIndex == idx) _hoverIndex = -1; UpdateRowVisuals(); };

			_optionList.AddChild(row);
			_optionRows.Add(row);
		}
	}

	private void ClearOptionRows()
	{
		foreach (var row in _optionRows)
			row.QueueFree();
		_optionRows.Clear();
	}

	private void UpdateRowVisuals()
	{
		for (var i = 0; i < _optionRows.Count; i++)
			RowStyleHelper.Apply(_optionRows[i], i == _cursor, i == _hoverIndex, transparentBg: true);
	}

	/// <summary>获取 Mood 值对应的情绪图标。</summary>
	public static string GetMoodIcon(float mood) => mood switch
	{
		< -0.6f => "[color=red](!)[/color]",
		< -0.3f => "[color=orange](-)[/color]",
		< 0.0f => "[color=yellow](~)[/color]",
		< 0.3f => "[color=gray](.)[/color]",
		< 0.6f => "[color=green](+)[/color]",
		_ => "[color=cyan](*)[/color]",
	};
}
