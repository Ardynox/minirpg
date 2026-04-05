using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module;

public sealed class SaveBrowserModule
{
	private readonly PanelContainer _panel;
	private readonly Label _titleLabel;
	private readonly Label _subtitleLabel;
	private readonly VBoxContainer _listRoot;
	private readonly Button _backButton;
	private readonly List<Button> _rows = [];

	private IReadOnlyList<SaveSlotInfo> _slots = Array.Empty<SaveSlotInfo>();

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public event Action? CloseRequested;
	public event Action<SaveSlotInfo>? LoadRequested;

	public SaveBrowserModule(PanelContainer panel)
	{
		_panel = panel;
		_titleLabel = panel.GetNode<Label>("Margin/VBox/Title");
		_subtitleLabel = panel.GetNode<Label>("Margin/VBox/Subtitle");
		_listRoot = panel.GetNode<VBoxContainer>("Margin/VBox/Scroll/List");
		_backButton = panel.GetNode<Button>("Margin/VBox/Actions/BackBtn");
		_backButton.Pressed += () => CloseRequested?.Invoke();
	}

	public void Open(IReadOnlyList<SaveSlotInfo> slots, string title, string subtitle)
	{
		_slots = slots;
		_titleLabel.Text = title;
		_subtitleLabel.Text = subtitle;
		RebuildRows();
		Visible = true;
	}

	public void Close()
	{
		Visible = false;
	}

	private void RebuildRows()
	{
		var needed = Math.Max(1, _slots.Count);
		while (_rows.Count > needed)
		{
			_rows[^1].QueueFree();
			_rows.RemoveAt(_rows.Count - 1);
		}

		while (_rows.Count < needed)
		{
			var row = CreateRow(_rows.Count);
			_listRoot.AddChild(row);
			_rows.Add(row);
		}

		if (_slots.Count == 0)
		{
			var row = _rows[0];
			row.Disabled = true;
			row.Text = "No saves available";
			return;
		}

		for (var i = 0; i < _slots.Count; i++)
		{
			var slot = _slots[i];
			var row = _rows[i];
			row.Disabled = false;
			row.Text = $"{slot.DisplayName}\n{slot.Summary}";
		}
	}

	private Button CreateRow(int index)
	{
		var row = new Button
		{
			Flat = false,
			Alignment = HorizontalAlignment.Left,
			FocusMode = Control.FocusModeEnum.None,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 72),
			ClipText = false,
			TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
		};

		row.Pressed += () =>
		{
			if (index < 0 || index >= _slots.Count)
				return;

			LoadRequested?.Invoke(_slots[index]);
		};
		return row;
	}
}
