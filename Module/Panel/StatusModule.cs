using System;
using System.Collections.Generic;
using System.Text;
using Godot;
namespace MiniRPG.Module.Panel;

public enum StatusTab { Limb, Capacity, Tag, Buff, Equip }

/// <summary>
/// 状态面板：用单个 RichTextLabel 渲染所有内容行，
/// 避免每行一个 Button 节点导致的 draw call 爆炸。
/// Tab 切换用纯文本标签，光标选中用 ▶ 前缀 + 颜色高亮。
/// </summary>
public class StatusPanelModule : IPanel
{
	public string PanelId => "status";
	public PanelContainer PanelNode => _panel;
	bool IPanel.Visible { get => _panel.Visible; set => _panel.Visible = value; }

	bool IPanel.HandleCommand(string cmd)
	{
		switch (cmd)
		{
			case "up": MoveCursor(-1); return true;
			case "down": MoveCursor(1); return true;
			case "left" or "tab_prev": CycleTab(-1); return true;
			case "right" or "tab_next": CycleTab(1); return true;
		}
		return false;
	}

	private static readonly StatusTab[] Tabs =
		[StatusTab.Limb, StatusTab.Capacity, StatusTab.Tag, StatusTab.Buff, StatusTab.Equip];

	private static readonly string[] TabLabels = ["肢体", "能力", "标记", "Buff", "装备"];

	private readonly PanelContainer _panel;
	private readonly Label _nameInfo;
	private readonly Label _tabLabel;
	private readonly RichTextLabel _contentText;

	private StatusTab _currentTab = StatusTab.Limb;
	private int _cursor;
	private Actor? _cachedPlayer;

	private readonly List<string> _lines = [];

	public bool Dirty { get; set; }
	private int _cachedFloor;
	private int _cachedTurn;

	public void FlushIfDirty()
	{
		if (!Dirty) return;
		Dirty = false;
		Refresh(_cachedPlayer, _cachedFloor, _cachedTurn);
	}

	public StatusPanelModule(PanelContainer panel)
	{
		_panel = panel;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_nameInfo = vbox.GetNode<Label>("NameInfo");
		_tabLabel = vbox.GetNode<Label>("TabLabel");
		_contentText = vbox.GetNode<RichTextLabel>("ContentText");

		UpdateTabLabel();
	}

	public void SetTab(StatusTab tab)
	{
		_currentTab = tab;
		_cursor = 0;
		UpdateTabLabel();
		RenderContent();
	}

	public void CycleTab(int dir)
	{
		var idx = Array.IndexOf(Tabs, _currentTab);
		idx = (idx + dir + Tabs.Length) % Tabs.Length;
		SetTab(Tabs[idx]);
	}

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
		if (_lines.Count == 0) return;
		var prev = _cursor;
		_cursor = Math.Clamp(_cursor + delta, 0, _lines.Count - 1);
		if (_cursor != prev)
			RenderContent();
	}

	public void Refresh(Actor? player, int floor, int turn)
	{
		_cachedPlayer = player;
		_cachedFloor = floor;
		_cachedTurn = turn;
		Dirty = false;
		if (player == null)
		{
			_nameInfo.Text = "";
			_contentText.Clear();
			_lines.Clear();
			return;
		}

		_nameInfo.Text = BuildNameInfo(player, floor, turn);
		UpdateTabLabel();
		BuildLines();
		RenderContent();
	}

	private void UpdateTabLabel()
	{
		var sb = new StringBuilder();
		for (var i = 0; i < TabLabels.Length; i++)
		{
			if (i > 0) sb.Append("  ");
			if (Tabs[i] == _currentTab)
				sb.Append($"[{TabLabels[i]}]");
			else
				sb.Append(TabLabels[i]);
		}
		_tabLabel.Text = sb.ToString();
	}

	private void BuildLines()
	{
		_lines.Clear();
		if (_cachedPlayer == null) return;

		switch (_currentTab)
		{
			case StatusTab.Limb: BuildLimbLines(_cachedPlayer); break;
			case StatusTab.Capacity: BuildCapacityLines(_cachedPlayer); break;
			case StatusTab.Tag: BuildTagLines(_cachedPlayer); break;
			case StatusTab.Buff: BuildBuffLines(_cachedPlayer); break;
			case StatusTab.Equip: BuildEquipLines(_cachedPlayer); break;
		}

		if (_cursor >= _lines.Count)
			_cursor = Math.Max(0, _lines.Count - 1);
	}

	private void RenderContent()
	{
		_contentText.Clear();
		if (_lines.Count == 0) return;

		var sb = new StringBuilder();
		for (var i = 0; i < _lines.Count; i++)
		{
			if (i > 0) sb.Append('\n');
			if (i == _cursor)
				sb.Append($"[color=#99ffaa]▶ {_lines[i]}[/color]");
			else
				sb.Append($"  {_lines[i]}");
		}
		_contentText.AppendText(sb.ToString());
	}

	private void BuildLimbLines(Actor player)
	{
		if (player.Limbs.Count == 0) { _lines.Add("无肢体 — 致命状态"); return; }
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
			_lines.Add($"{limb.Name}{vital}  {limb.Durability}/{limb.MaxDurability}{caps}");
		}
	}

	private void BuildCapacityLines(Actor player)
	{
		var caps = player.ComputeCapacities();
		if (caps.Count == 0) { _lines.Add("无"); return; }
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
			_lines.Add($"{name}: {pct}%{effect}");
		}
	}

	private void BuildTagLines(Actor player)
	{
		var tags = player.ComputeTags();
		if (tags.Count == 0) { _lines.Add("无"); return; }
		foreach (var (key, val) in tags)
			_lines.Add($"{key}: {val}");
	}

	private void BuildBuffLines(Actor player)
	{
		if (player.Buffs.Count == 0) { _lines.Add("无"); return; }
		foreach (var buff in player.Buffs)
		{
			var turns = buff.RemainingTurns < 0 ? "永久" : $"{buff.RemainingTurns}回合";
			var tagParts = new List<string>();
			foreach (var (key, val) in buff.Tags)
				tagParts.Add($"{key}{(val >= 0 ? "+" : "")}{val}");
			var tagStr = tagParts.Count > 0 ? $" {string.Join(" ", tagParts)}" : "";
			_lines.Add($"{buff.Name} ({turns}){tagStr}");
		}
	}

	private void BuildEquipLines(Actor player)
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
					_lines.Add($"── {limb.Name} ──");
					limbHasEquip = true;
				}
				hasAny = true;

				var stats = new List<string>();
				if (item.SharpDamage > 0) stats.Add($"锐伤{item.SharpDamage:F0}");
				if (item.BluntDamage > 0) stats.Add($"钝伤{item.BluntDamage:F0}");
				if (item.SharpArmor > 0) stats.Add($"锐防{item.SharpArmor:F0}");
				if (item.BluntArmor > 0) stats.Add($"钝防{item.BluntArmor:F0}");
				var statStr = stats.Count > 0 ? $" {string.Join(" ", stats)}" : "";
				_lines.Add($"  {item.Name} ({slot.Layer}){statStr}");
			}
		}

		if (!hasAny) { _lines.Add("无装备"); return; }

		var weight = $"负重: {player.CarryWeight:F1}/{player.MaxCarryWeight:F1}kg";
		if (player.IsOverweight) weight += " 超重！";
		_lines.Add(weight);
	}

	private static string BuildNameInfo(Actor player, int floor, int turn)
	{
		var sb = new StringBuilder();
		sb.Append(player.DisplayName);
		if (player.Race != null) sb.Append($"  {player.Race.Name}");
		if (player.Profession != null) sb.Append($" · {player.Profession.Name}");
		sb.Append($"\n金币: {player.Gold}G  深度: Z{floor}  回合: {turn}");
		return sb.ToString();
	}
}
