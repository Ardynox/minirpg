using System;
using System.Collections.Generic;
using Godot;
namespace MiniRPG.Module.Panel;

public enum StatusTab { Limb, Capacity, Tag, Buff, Equip }

public class StatusPanelModule
{
	private static readonly StatusTab[] Tabs =
		[StatusTab.Limb, StatusTab.Capacity, StatusTab.Tag, StatusTab.Buff, StatusTab.Equip];

	private static readonly string[] TabLabels = ["肢体", "能力", "标记", "Buff", "装备"];

	private readonly PanelContainer _panel;
	private readonly RichTextLabel _nameInfo;
	private readonly HBoxContainer _filterBar;
	private readonly ScrollContainer _contentScroll;
	private readonly VBoxContainer _itemList;
	private readonly RichTextLabel _hintBar;
	private readonly List<Button> _tabButtons = [];
	private readonly List<Button> _rows = [];

	private StatusTab _currentTab = StatusTab.Limb;
	private int _cursor;
	private int _hoverIndex = -1;
	private Actor? _cachedPlayer;

	public StatusPanelModule(PanelContainer panel)
	{
		_panel = panel;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_nameInfo = vbox.GetNode<RichTextLabel>("NameInfo");
		_filterBar = vbox.GetNode<HBoxContainer>("FilterBar");
		_contentScroll = vbox.GetNode<ScrollContainer>("ContentScroll");
		_itemList = _contentScroll.GetNode<VBoxContainer>("ItemList");
		_hintBar = vbox.GetNode<RichTextLabel>("HintBar");

		BuildTabButtons();
		_hintBar.Clear();
		_hintBar.AppendText("[color=#666666]↑↓选择 ←→分类 Esc退出[/color]");
	}

	private void BuildTabButtons()
	{
		for (var i = 0; i < TabLabels.Length; i++)
		{
			var btn = new Button
			{
				Text = TabLabels[i],
				ToggleMode = true,
				SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
				FocusMode = Control.FocusModeEnum.None,
			};
			var idx = i;
			btn.Pressed += () => SetTab(Tabs[idx]);
			_filterBar.AddChild(btn);
			_tabButtons.Add(btn);
		}
	}

	public void SetTab(StatusTab tab)
	{
		_currentTab = tab;
		_cursor = 0;
		UpdateTabHighlight();
		RefreshContent();
	}

	public void CycleTab(int dir)
	{
		var idx = Array.IndexOf(Tabs, _currentTab);
		idx = (idx + dir + Tabs.Length) % Tabs.Length;
		SetTab(Tabs[idx]);
	}

	/// <summary>InputModule 直接调用：cmd = "up"/"down"/"prev"/"next"/"close"。</summary>
	public void HandleCommand(string cmd, Action? onClose = null)
	{
		switch (cmd)
		{
			case "up": MoveCursor(-1); break;
			case "down": MoveCursor(1); break;
			case "prev": CycleTab(-1); break;
			case "next": CycleTab(1); break;
			case "close": onClose?.Invoke(); break;
		}
	}

	public void MoveCursor(int delta)
	{
		if (_usedRows == 0) return;
		_cursor = Math.Clamp(_cursor + delta, 0, _usedRows - 1);
		UpdateRowVisuals();
		if (_cursor >= 0 && _cursor < _usedRows)
			RowStyleHelper.EnsureVisible(_contentScroll, _rows[_cursor]);
	}

	public void Refresh(Actor? player, int floor, int turn)
	{
		_cachedPlayer = player;
		if (player == null)
		{
			_nameInfo.Clear();
			ClearRows();
			return;
		}

		_nameInfo.Clear();
		_nameInfo.AppendText(BuildNameInfo(player, floor, turn));
		UpdateTabHighlight();
		RefreshContent();
	}

	// ── Content building ─────────────────────────────────

	private void RefreshContent()
	{
		if (_cachedPlayer == null) return;

		ClearRows();
		switch (_currentTab)
		{
			case StatusTab.Limb: BuildLimbRows(_cachedPlayer); break;
			case StatusTab.Capacity: BuildCapacityRows(_cachedPlayer); break;
			case StatusTab.Tag: BuildTagRows(_cachedPlayer); break;
			case StatusTab.Buff: BuildBuffRows(_cachedPlayer); break;
			case StatusTab.Equip: BuildEquipRows(_cachedPlayer); break;
		}

		for (var i = _usedRows; i < _rows.Count; i++)
			_rows[i].Visible = false;

		if (_cursor >= _usedRows)
			_cursor = Math.Max(0, _usedRows - 1);
		UpdateRowVisuals();
	}

	private int _usedRows;

	private void ClearRows()
	{
		_usedRows = 0;
		_hoverIndex = -1;
	}

	private void AddRow(string text)
	{
		var idx = _usedRows;
		if (idx < _rows.Count)
		{
			_rows[idx].Text = text;
			_rows[idx].Visible = true;
		}
		else
		{
			var row = new Button
			{
				Text = text,
				Flat = true,
				FocusMode = Control.FocusModeEnum.None,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				CustomMinimumSize = new Vector2(0, 24),
				Alignment = HorizontalAlignment.Left,
				ClipText = true,
			};
			var capturedIdx = idx;
			row.MouseEntered += () => { _hoverIndex = capturedIdx; UpdateRowVisuals(); };
			row.MouseExited += () => { if (_hoverIndex == capturedIdx) _hoverIndex = -1; UpdateRowVisuals(); };
			row.Pressed += () => { _cursor = capturedIdx; UpdateRowVisuals(); };
			_itemList.AddChild(row);
			_rows.Add(row);
		}
		_usedRows++;
	}

	// ── Limb tab ─────────────────────────────────────────

	private void BuildLimbRows(Actor player)
	{
		if (player.Limbs.Count == 0)
		{
			AddRow("无肢体 — 致命状态");
			return;
		}
		foreach (var limb in player.Limbs)
		{
			var ratio = limb.MaxDurability > 0
				? (float)limb.Durability / limb.MaxDurability : 0f;
			var vital = limb.Tags.ContainsKey("要害") ? " *" : "";
			var capParts = new List<string>();
			foreach (var (capId, weight) in limb.Capacities)
			{
				var def = PresetDB.GetCapacity(capId);
				var name = def?.Name ?? capId;
				var pct = (int)(weight * ratio * 100);
				capParts.Add($"{name}{pct}%");
			}
			var caps = capParts.Count > 0 ? $"  {string.Join(" ", capParts)}" : "";
			AddRow($"{limb.Name}{vital}  {limb.Durability}/{limb.MaxDurability}{caps}");
		}
	}

	// ── Capacity tab ─────────────────────────────────────

	private void BuildCapacityRows(Actor player)
	{
		var caps = player.ComputeCapacities();
		if (caps.Count == 0) { AddRow("无"); return; }
		foreach (var (capId, val) in caps)
		{
			var def = PresetDB.GetCapacity(capId);
			var name = def?.Name ?? capId;
			var pct = (int)(val * 100);
			var effect = "";
			if (def?.VitalEffect != null)
			{
				effect = def.VitalEffect switch
				{
					"death_instant" => " [致命]",
					"incapacitate" => " [昏迷]",
					"death_slow" => " [缓死]",
					_ => "",
				};
			}
			AddRow($"{name}: {pct}%{effect}");
		}
	}

	// ── Tag tab ──────────────────────────────────────────

	private void BuildTagRows(Actor player)
	{
		var tags = player.ComputeTags();
		if (tags.Count == 0) { AddRow("无"); return; }
		foreach (var (key, val) in tags)
			AddRow($"{key}: {val}");
	}

	// ── Buff tab ─────────────────────────────────────────

	private void BuildBuffRows(Actor player)
	{
		if (player.Buffs.Count == 0) { AddRow("无"); return; }
		foreach (var buff in player.Buffs)
		{
			var turns = buff.RemainingTurns < 0 ? "永久" : $"{buff.RemainingTurns}回合";
			var tagParts = new List<string>();
			foreach (var (key, val) in buff.Tags)
				tagParts.Add($"{key}{(val >= 0 ? "+" : "")}{val}");
			var tagStr = tagParts.Count > 0 ? $" {string.Join(" ", tagParts)}" : "";
			AddRow($"{buff.Name} ({turns}){tagStr}");
		}
	}

	// ── Equip tab ────────────────────────────────────────

	private void BuildEquipRows(Actor player)
	{
		var hasAny = false;
		foreach (var limb in player.Limbs)
		{
			if (limb.EquipSlots.Count == 0) continue;
			var limbHasEquip = false;
			foreach (var slot in limb.EquipSlots)
			{
				if (slot.ItemId == null) continue;
				var item = player.Inventory.Find(i => i.Id == slot.ItemId);
				if (item == null) continue;

				if (!limbHasEquip)
				{
					AddRow($"── {limb.Name} ──");
					limbHasEquip = true;
				}
				hasAny = true;

				var stats = new List<string>();
				if (item.SharpDamage > 0) stats.Add($"锐伤{item.SharpDamage:F0}");
				if (item.BluntDamage > 0) stats.Add($"钝伤{item.BluntDamage:F0}");
				if (item.SharpArmor > 0) stats.Add($"锐防{item.SharpArmor:F0}");
				if (item.BluntArmor > 0) stats.Add($"钝防{item.BluntArmor:F0}");
				var statStr = stats.Count > 0 ? $" {string.Join(" ", stats)}" : "";
				AddRow($"  {item.Name} ({slot.Layer}){statStr}");
			}
		}

		if (!hasAny)
		{
			AddRow("无装备");
			return;
		}

		var weight = $"负重: {player.CarryWeight:F1}/{player.MaxCarryWeight:F1}kg";
		if (player.IsOverweight) weight += " 超重！";
		AddRow(weight);
	}

	// ── Visual helpers ───────────────────────────────────

	private void UpdateTabHighlight()
	{
		for (var i = 0; i < _tabButtons.Count; i++)
			_tabButtons[i].ButtonPressed = Tabs[i] == _currentTab;
	}

	private void UpdateRowVisuals()
	{
		for (var i = 0; i < _usedRows; i++)
			RowStyleHelper.Apply(_rows[i], i == _cursor, i == _hoverIndex);
	}

	// ── Name info (header) ───────────────────────────────

	private static string BuildNameInfo(Actor player, int floor, int turn)
	{
		var name = $"[b]{player.DisplayName}[/b]";
		if (player.Race != null)
			name += $"  {player.Race.Name}";
		if (player.Profession != null)
			name += $" · {player.Profession.Name}";
		name += $"\n[color=#ffcc00]金币: {player.Gold}G[/color]  深度: Z{floor}  回合: {turn}";
		return name;
	}
}
