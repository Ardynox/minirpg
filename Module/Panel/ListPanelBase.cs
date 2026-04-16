using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 列表型面板公共基类：统一行池化、hover/cursor、脏刷新样板与滚动可见。
/// 默认实现 <see cref="IFocusableListPanel"/>，把 <c>_cursor</c> / <see cref="MoveCursor"/> / <see cref="OnRowPressed"/>
/// 三件套对外公开，让统一键盘导航（PageUp/PageDown/Home/End）可在不破坏现有 hover / 选中行为的前提下接进来。
/// </summary>
public abstract class ListPanelBase : IPanel, IFocusableListPanel
{
	public abstract string PanelId { get; }
	public abstract PanelContainer PanelNode { get; }
	public virtual bool Visible { get; set; }
	public virtual bool CanFocus => true;
	public virtual bool Dirty { get; set; }

	public int RowCount => GetRowDataCount();
	public int SelectedIndex => _cursor;
	public void MoveSelection(int delta) => MoveCursor(delta, GetRowDataCount());
	public void ActivateSelection()
	{
		var count = GetRowDataCount();
		if (count <= 0) return;
		var idx = Math.Clamp(_cursor, 0, count - 1);
		OnRowPressed(idx);
	}

	public event Action? OnRedrawNeeded;

	protected readonly List<Button> _itemRows = [];
	protected int _cursor;
	protected int _hoverIndex = -1;
	protected ScrollContainer? _itemScroll;
	protected VBoxContainer? _itemList;

	bool IPanel.CanFocus => CanFocus;
	bool IPanel.Visible { get => Visible; set => Visible = value; }

	public virtual void FlushIfDirty()
	{
		if (!Dirty) return;
		Dirty = false;
		Refresh();
	}

	public abstract bool HandleCommand(string cmd);
	public virtual void OnFocus() { }
	public virtual void OnBlur() { }
	public abstract void Refresh();

	void IPanel.FlushIfDirty() => FlushIfDirty();
	void IPanel.OnFocus() => OnFocus();
	void IPanel.OnBlur() => OnBlur();

	protected void BindListNodes(ScrollContainer scroll, VBoxContainer list)
	{
		_itemScroll = scroll;
		_itemList = list;
	}

	protected void MarkDirty()
	{
		Dirty = true;
		OnRedrawNeeded?.Invoke();
	}

	protected virtual Button CreateRow(int index)
	{
		var row = new Button
		{
			Flat = true,
			FocusMode = Control.FocusModeEnum.None,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 30),
			Alignment = HorizontalAlignment.Left,
			ClipText = true,
		};

		var idx = index;
		row.Pressed += () => OnRowPressed(idx);
		row.MouseEntered += () => OnRowHover(idx);
		row.MouseExited += () => OnRowHoverExit(idx);
		row.GuiInput += ev => HandleRowGuiInput(ev, idx);
		PanelButtonScaleRegistry.Track(PanelId, row);
		return row;
	}

	protected virtual void OnRowPressed(int index)
	{
		_cursor = index;
		OnSelectionChanged();
	}

	protected virtual void OnRowHover(int index)
	{
		_hoverIndex = index;
		UpdateRowVisuals(GetRowDataCount());
	}

	protected virtual void OnRowHoverExit(int index)
	{
		if (_hoverIndex == index) _hoverIndex = -1;
		UpdateRowVisuals(GetRowDataCount());
	}

	protected virtual void HandleRowGuiInput(InputEvent ev, int index) { }
	protected abstract int GetRowDataCount();
	protected virtual void OnSelectionChanged() { }

	protected void RebuildRows(int count, Action<Button, int> applyRowContent, string emptyText)
	{
		if (_itemList == null) return;
		var needed = Math.Max(count, 1);

		while (_itemRows.Count > needed)
		{
			_itemRows[^1].QueueFree();
			_itemRows.RemoveAt(_itemRows.Count - 1);
		}
		while (_itemRows.Count < needed)
		{
			var row = CreateRow(_itemRows.Count);
			_itemList.AddChild(row);
			_itemRows.Add(row);
		}

		if (count == 0)
		{
			_itemRows[0].Text = emptyText;
			_itemRows[0].Disabled = true;
			_itemRows[0].ThemeTypeVariation = "DisabledRowButton";
		}
		else
		{
			for (var i = 0; i < count; i++)
			{
				_itemRows[i].Disabled = false;
				applyRowContent(_itemRows[i], i);
			}
		}

		if (_cursor >= count)
			_cursor = Math.Max(0, count - 1);
	}

	protected void MoveCursor(int delta, int count)
	{
		if (count == 0) return;
		_cursor = Math.Clamp(_cursor + delta, 0, count - 1);
		OnSelectionChanged();
		EnsureCurrentVisible();
	}

	/// <summary>
	/// 通用列表键盘命令处理（up/down/page_up/page_down/home/end/confirm）。
	/// 子类可在自己的 <c>HandleCommand</c> 里优先调用 <c>HandleListCommand(cmd)</c>
	/// 拿到默认响应，再处理自己特有的命令。
	/// </summary>
	protected bool HandleListCommand(string cmd)
	{
		var count = GetRowDataCount();
		switch (cmd)
		{
			case "up":
				MoveCursor(-1, count);
				return count > 0;
			case "down":
				MoveCursor(1, count);
				return count > 0;
			case "page_up":
				MoveCursor(-10, count);
				return count > 0;
			case "page_down":
				MoveCursor(10, count);
				return count > 0;
			case "home":
				if (count <= 0) return false;
				_cursor = 0;
				OnSelectionChanged();
				EnsureCurrentVisible();
				return true;
			case "end":
				if (count <= 0) return false;
				_cursor = count - 1;
				OnSelectionChanged();
				EnsureCurrentVisible();
				return true;
			case "confirm":
				ActivateSelection();
				return count > 0;
			default:
				return false;
		}
	}

	protected virtual void UpdateRowVisuals(int count, bool transparentBg = true)
	{
		for (var i = 0; i < _itemRows.Count && i < count; i++)
			RowStyleHelper.Apply(_itemRows[i], i == _cursor, i == _hoverIndex, transparentBg);
	}

	protected void EnsureCurrentVisible()
	{
		if (_itemScroll == null) return;
		if (_cursor >= 0 && _cursor < _itemRows.Count)
			RowStyleHelper.EnsureVisible(_itemScroll, _itemRows[_cursor]);
	}
}
