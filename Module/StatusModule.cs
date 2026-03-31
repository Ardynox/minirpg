using System.Collections.Generic;
using System.Text;
using MiniRPG.Core;

namespace MiniRPG.Module;

/// <summary>
/// 角色状态面板渲染：以肢体为核心，展示角色的身体状态、能力百分比、Buff 和装备。
/// </summary>
public static class StatusModule
{
	public static string BuildNameInfo(Actor player, int floor, int turn)
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

	public static string BuildLimbInfo(Actor player)
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

	/// <summary>
	/// 显示能力百分比总览，替代旧的 tag 聚合显示。
	/// </summary>
	public static string BuildCapacityInfo(Actor player)
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

	/// <summary>
	/// 显示从所有来源聚合后的 tag 总值（仅保留非能力类标记）。
	/// </summary>
	public static string BuildTagInfo(Actor player)
	{
		var tags = player.ComputeTags();
		if (tags.Count == 0)
			return "[color=#888888]无[/color]";

		var sb = new StringBuilder();
		foreach (var (key, val) in tags)
			sb.AppendLine($"{key}: [color=#aaaaff]{val}[/color]");
		return sb.ToString();
	}

	public static string BuildBuffInfo(Actor player)
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

	public static string BuildEquipInfo(Actor player)
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
