using System.Collections.Generic;
using System.Text;
using Godot;
using MiniRPG.Core;

namespace MiniRPG.Module;

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
	private readonly RichTextLabel _contentBox;
	private readonly List<Button> _tabButtons = [];

	private StatusTab _currentTab = StatusTab.Limb;

	public StatusPanelModule(PanelContainer panel)
	{
		_panel = panel;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_nameInfo = vbox.GetNode<RichTextLabel>("NameInfo");
		_filterBar = vbox.GetNode<HBoxContainer>("FilterBar");
		_contentScroll = vbox.GetNode<ScrollContainer>("ContentScroll");
		_contentBox = _contentScroll.GetNode<RichTextLabel>("ContentBox");

		BuildTabButtons();
	}

	private void BuildTabButtons()
	{
		for (var i = 0; i < TabLabels.Length; i++)
		{
			var btn = new Button
			{
				Text = TabLabels[i],
				ToggleMode = true,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				FocusMode = Control.FocusModeEnum.None,
				CustomMinimumSize = new Vector2(0, 24),
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
		UpdateTabHighlight();
		RefreshContent();
	}

	public void CycleTab(int dir)
	{
		var idx = System.Array.IndexOf(Tabs, _currentTab);
		idx = (idx + dir + Tabs.Length) % Tabs.Length;
		SetTab(Tabs[idx]);
	}

	public void Refresh(Actor? player, int floor, int turn)
	{
		if (player == null)
		{
			_nameInfo.Clear();
			_contentBox.Clear();
			return;
		}

		_nameInfo.Clear();
		_nameInfo.AppendText(BuildNameInfo(player, floor, turn));
		UpdateTabHighlight();
		RefreshContent(player);
	}

	private void RefreshContent(Actor? player = null)
	{
		player ??= GetCachedPlayer();
		if (player == null) return;

		_contentBox.Clear();
		var text = _currentTab switch
		{
			StatusTab.Limb => BuildLimbInfo(player),
			StatusTab.Capacity => BuildCapacityInfo(player),
			StatusTab.Tag => BuildTagInfo(player),
			StatusTab.Buff => BuildBuffInfo(player),
			StatusTab.Equip => BuildEquipInfo(player),
			_ => "",
		};
		_contentBox.AppendText(text);
		_contentScroll.ScrollVertical = 0;
	}

	private void UpdateTabHighlight()
	{
		for (var i = 0; i < _tabButtons.Count; i++)
			_tabButtons[i].ButtonPressed = Tabs[i] == _currentTab;
	}

	private Actor? _cachedPlayer;
	private Actor? GetCachedPlayer() => _cachedPlayer;

	public void Refresh(Actor? player, int floor, int turn, bool _)
	{
		_cachedPlayer = player;
		Refresh(player, floor, turn);
	}

	// ── Static build helpers (kept from old StatusModule) ─

	private static string BuildNameInfo(Actor player, int floor, int turn)
	{
		var sb = new StringBuilder();
		sb.Append($"[b]{player.DisplayName}[/b]");
		if (player.Race != null)
			sb.Append($"  {player.Race.Name}");
		if (player.Profession != null)
			sb.Append($" · {player.Profession.Name}");
		sb.AppendLine();
		sb.AppendLine($"[color=#ffcc00]金币: {player.Gold}G[/color]  深度: Z{floor}  回合: {turn}");
		return sb.ToString();
	}

	private static string BuildLimbInfo(Actor player)
	{
		if (player.Limbs.Count == 0)
			return "[color=#ff4444]无肢体 — 致命状态[/color]";

		var sb = new StringBuilder();
		foreach (var limb in player.Limbs)
		{
			var ratio = limb.MaxDurability > 0
				? (float)limb.Durability / limb.MaxDurability
				: 0f;
			var hpColor = ratio > 0.6f ? "#44ee44" : ratio > 0.3f ? "#ffcc00" : "#ff4444";
			var vital = limb.Tags.ContainsKey("要害") ? " [color=#ff4444]*[/color]" : "";

			sb.Append($"[color={hpColor}]{limb.Name}{vital}[/color]");
			sb.Append($"  {limb.Durability}/{limb.MaxDurability}");

			var capParts = new List<string>();
			foreach (var (capId, weight) in limb.Capacities)
			{
				var def = PresetDB.GetCapacity(capId);
				var name = def?.Name ?? capId;
				var pct = (int)(weight * ratio * 100);
				capParts.Add($"{name}{pct}%");
			}
			if (capParts.Count > 0)
				sb.Append($"  [color=#888888]{string.Join(" ", capParts)}[/color]");

			sb.AppendLine();
		}
		sb.Append("[color=#888888][color=#ff4444]*[/color] = 要害[/color]");
		return sb.ToString();
	}

	private static string BuildCapacityInfo(Actor player)
	{
		var caps = player.ComputeCapacities();
		if (caps.Count == 0)
			return "[color=#888888]无[/color]";

		var sb = new StringBuilder();
		foreach (var (capId, val) in caps)
		{
			var def = PresetDB.GetCapacity(capId);
			var name = def?.Name ?? capId;
			var pct = (int)(val * 100);
			var color = pct >= 80 ? "#44ee44" : pct >= 40 ? "#ffcc00" : "#ff4444";
			sb.Append($"{name}: [color={color}]{pct}%[/color]");

			if (def?.VitalEffect != null)
			{
				var effectLabel = def.VitalEffect switch
				{
					"death_instant" => "[color=#ff4444]致命[/color]",
					"incapacitate" => "[color=#ff8844]昏迷[/color]",
					"death_slow" => "[color=#ffaa44]缓死[/color]",
					_ => "",
				};
				if (effectLabel.Length > 0)
					sb.Append($" {effectLabel}");
			}
			sb.AppendLine();
		}
		return sb.ToString();
	}

	private static string BuildTagInfo(Actor player)
	{
		var tags = player.ComputeTags();
		if (tags.Count == 0)
			return "[color=#888888]无[/color]";

		var sb = new StringBuilder();
		foreach (var (key, val) in tags)
			sb.AppendLine($"{key}: [color=#aaaaff]{val}[/color]");
		return sb.ToString();
	}

	private static string BuildBuffInfo(Actor player)
	{
		if (player.Buffs.Count == 0)
			return "[color=#888888]无[/color]";

		var sb = new StringBuilder();
		foreach (var buff in player.Buffs)
		{
			var turns = buff.RemainingTurns < 0 ? "永久" : $"{buff.RemainingTurns}回合";
			sb.Append($"[color=#aa88ff]{buff.Name}[/color] ({turns})");
			foreach (var (key, val) in buff.Tags)
				sb.Append($" {key}{(val >= 0 ? "+" : "")}{val}");
			sb.AppendLine();
		}
		return sb.ToString();
	}

	private static string BuildEquipInfo(Actor player)
	{
		var hasAny = false;
		var sb = new StringBuilder();

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
					sb.AppendLine($"[color=#aaaaaa]{limb.Name}:[/color]");
					limbHasEquip = true;
				}
				hasAny = true;

				sb.Append($"  [color=#44ccff]{item.Name}[/color] ({slot.Layer})");
				var stats = new List<string>();
				if (item.SharpDamage > 0) stats.Add($"锐伤{item.SharpDamage:F0}");
				if (item.BluntDamage > 0) stats.Add($"钝伤{item.BluntDamage:F0}");
				if (item.SharpArmor > 0) stats.Add($"锐防{item.SharpArmor:F0}");
				if (item.BluntArmor > 0) stats.Add($"钝防{item.BluntArmor:F0}");
				if (stats.Count > 0) sb.Append($" {string.Join(" ", stats)}");
				sb.AppendLine();
			}
		}

		if (!hasAny)
			return "[color=#888888]无装备[/color]";

		var weightLine = $"负重: {player.CarryWeight:F1}/{player.MaxCarryWeight:F1}kg";
		if (player.IsOverweight) weightLine = $"[color=#ff4444]{weightLine} 超重！[/color]";
		sb.AppendLine(weightLine);

		return sb.ToString();
	}
}
