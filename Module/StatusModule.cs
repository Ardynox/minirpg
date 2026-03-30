using System.Collections.Generic;
using System.Text;
using MiniRPG.Core;

namespace MiniRPG.Module;

/// <summary>
/// 角色状态面板渲染：以肢体为核心，展示角色的身体状态、聚合能力、Buff 和装备。
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
		sb.AppendLine($"[color=#ffcc00]金币: {player.Gold}G[/color]  楼层: {floor}  回合: {turn}");
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

			var tagParts = new List<string>();
			foreach (var (key, val) in limb.Tags)
			{
				if (key == "要害") continue;
				tagParts.Add($"{key}+{val}");
			}
			if (tagParts.Count > 0)
				sb.Append($"  [color=#888888]{string.Join(" ", tagParts)}[/color]");

			sb.AppendLine();
		}
		sb.Append("[color=#888888][color=#ff4444]*[/color] = 要害[/color]");
		return sb.ToString();
	}

	/// <summary>
	/// 显示从所有来源聚合后的 tag 总值，让玩家了解当前综合能力。
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
		var equipped = new List<Item>();
		foreach (var item in player.Inventory)
			if (item.Equipped) equipped.Add(item);

		if (equipped.Count == 0)
			return "[color=#888888]无装备[/color]";

		var sb = new StringBuilder();
		foreach (var item in equipped)
		{
			sb.Append($"[color=#44ccff]{item.Name}[/color]");
			if (item.Tags.Count > 0)
			{
				var parts = new List<string>();
				foreach (var (key, val) in item.Tags)
					parts.Add($"{key}+{val}");
				sb.Append($" ({string.Join(", ", parts)})");
			}
			sb.AppendLine();
		}
		return sb.ToString();
	}
}
