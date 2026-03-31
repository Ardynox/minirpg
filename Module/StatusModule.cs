using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core;

namespace MiniRPG.Module;

public enum StatusTab { Limb, Capacity, Tag, Buff, Equip }

public class StatusPanelModule
{
	private static readonly StatusTab[] Tabs =
		[StatusTab.Limb, StatusTab.Capacity, StatusTab.Tag, StatusTab.Buff, StatusTab.Equip];

	private static readonly string[] TabLabels = ["肢体", "能力", "标记", "Buff", "装备"];

	private static readonly Color ColorRowBg = new(0.12f, 0.12f, 0.15f);
	private static readonly Color ColorHoverBg = new(0.18f, 0.18f, 0.22f);
	private static readonly Color ColorSelectedBg = new(0.15f, 0.22f, 0.18f);
	private static readonly Color ColorNormal = new(0.85f, 0.85f, 0.85f);
	private static readonly Color ColorSelected = new(0.6f, 1f, 0.7f);
	private static readonly Color ColorFocusBorder = new(0.3f, 0.8f, 0.4f);

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

	public void MoveCursor(int delta)
	{
		if (_rows.Count == 0) return;
		_cursor = Math.Clamp(_cursor + delta, 0, _rows.Count - 1);
		UpdateRowVisuals();
		EnsureCursorVisible();
	}

	public void Refresh(Actor? player, int floor, int turn)
	{
		if (player == null)
		{
			_nameInfo.Clear();
			ClearRows();
			return;
		}

		_nameInfo.Clear();
		_nameInfo.AppendText(BuildNameInfo(player, floor, turn));
		UpdateTabHighlight();
		RefreshContent(player);
	}

	public void Refresh(Actor? player, int floor, int turn, bool _)
	{
		_cachedPlayer = player;
		Refresh(player, floor, turn);
	}

	// ── Content building ─────────────────────────────────

	private void RefreshContent(Actor? player = null)
	{
		player ??= _cachedPlayer;
		if (player == null) return;

		ClearRows();
		switch (_currentTab)
		{
			case StatusTab.Limb: BuildLimbRows(player); break;
			case StatusTab.Capacity: BuildCapacityRows(player); break;
			case StatusTab.Tag: BuildTagRows(player); break;
			case StatusTab.Buff: BuildBuffRows(player); break;
			case StatusTab.Equip: BuildEquipRows(player); break;
		}

		if (_cursor >= _rows.Count)
			_cursor = Math.Max(0, _rows.Count - 1);
		UpdateRowVisuals();
	}

	private void ClearRows()
	{
		foreach (var r in _rows)
			r.QueueFree();
		_rows.Clear();
		_hoverIndex = -1;
	}

	private void AddRow(string text)
	{
		var idx = _rows.Count;
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
		ApplyRowStyle(row, false, false);
		row.MouseEntered += () => { _hoverIndex = idx; UpdateRowVisuals(); };
		row.MouseExited += () => { if (_hoverIndex == idx) _hoverIndex = -1; UpdateRowVisuals(); };
		row.Pressed += () => { _cursor = idx; UpdateRowVisuals(); };
		_itemList.AddChild(row);
		_rows.Add(row);
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
		for (var i = 0; i < _rows.Count; i++)
			ApplyRowStyle(_rows[i], i == _cursor, i == _hoverIndex);
	}

	private static void ApplyRowStyle(Button row, bool selected, bool hovered)
	{
		Color bg;
		if (selected) bg = ColorSelectedBg;
		else if (hovered) bg = ColorHoverBg;
		else bg = ColorRowBg;

		var sb = new StyleBoxFlat
		{
			BgColor = bg,
			ContentMarginLeft = 4,
			ContentMarginRight = 4,
		};

		if (selected)
		{
			sb.BorderWidthLeft = 3;
			sb.BorderColor = ColorFocusBorder;
			sb.ContentMarginLeft = 6;
		}

		row.AddThemeStyleboxOverride("normal", sb);
		row.AddThemeStyleboxOverride("hover", sb);
		row.AddThemeStyleboxOverride("pressed", sb);
		row.AddThemeColorOverride("font_color", selected ? ColorSelected : ColorNormal);
		row.AddThemeColorOverride("font_hover_color", selected ? ColorSelected : ColorNormal);
	}

	private void EnsureCursorVisible()
	{
		if (_cursor < 0 || _cursor >= _rows.Count) return;
		var row = _rows[_cursor];
		var rowTop = row.Position.Y;
		var rowBot = rowTop + row.Size.Y;
		var scrollTop = _contentScroll.ScrollVertical;
		var scrollBot = scrollTop + _contentScroll.Size.Y;

		if (rowTop < scrollTop)
			_contentScroll.ScrollVertical = (int)rowTop;
		else if (rowBot > scrollBot)
			_contentScroll.ScrollVertical = (int)(rowBot - _contentScroll.Size.Y);
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
