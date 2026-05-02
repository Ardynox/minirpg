using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Config;

namespace MiniRPG.Module.Panel;

/// <summary>BG3 风格对话面板（复用 DialogPanel 场景节点结构）。</summary>
public sealed class ConversationPanelModule : IPanel
{
	public string PanelId => "conversation";
	public PanelContainer PanelNode => _panel;
	bool IPanel.Visible { get => _panel.Visible; set => _panel.Visible = value; }

	bool IPanel.HandleCommand(string cmd)
	{
		switch (cmd)
		{
			case "up": MoveCursor(-1); return true;
			case "down": MoveCursor(1); return true;
			case "confirm": Confirm(); return true;
			case "close": OnConversationClosed?.Invoke(); return true;
			default:
				if (int.TryParse(cmd, out var num)) { SelectByNumber(num); return true; }
				return false;
		}
	}

	public event Action? OnConversationClosed;
	public event Action<int>? OnOptionSelected;
	void IPanel.OnBlur() { }

	private readonly PanelContainer _panel;
	private readonly RichTextLabel _header;
	private readonly RichTextLabel _dialogText;
	private readonly VBoxContainer _optionList;
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

	public ConversationPanelModule(PanelContainer panel)
	{
		_panel = panel;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_header = vbox.GetNode<RichTextLabel>("HeaderBar/Header");
		_dialogText = vbox.GetNode<RichTextLabel>("DialogText");
		_optionList = vbox.GetNode<VBoxContainer>("OptionList");
	}

	public void Show(string title, string moodIcon, string body, IReadOnlyList<string> options)
	{
		_header.Clear();
		_header.AppendText($"[center]── {title}  {moodIcon} ──[/center]");

		_dialogText.Clear();
		_dialogText.AppendText(body);

		RebuildOptions(options);
		_cursor = 0;
		UpdateRowVisuals();

		Visible = true;
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

	private void RebuildOptions(IReadOnlyList<string> options)
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
			PanelButtonScaleRegistry.Track(PanelId, row);
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
