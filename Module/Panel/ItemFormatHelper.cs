using System.Collections.Generic;
using System.Text;
namespace MiniRPG.Module.Panel;

/// <summary>
/// 物品文本格式化工具，供背包/宝箱/地面面板共享。
/// </summary>
public static class ItemFormatHelper
{
	public static string InlineStats(Item item)
	{
		var parts = new List<string>();
		if (item.SharpDamage > 0) parts.Add($"锐{item.SharpDamage:F0}");
		if (item.BluntDamage > 0) parts.Add($"钝{item.BluntDamage:F0}");
		if (item.SharpArmor > 0) parts.Add($"锐防{item.SharpArmor:F0}");
		if (item.BluntArmor > 0) parts.Add($"钝防{item.BluntArmor:F0}");
		return string.Join(" ", parts);
	}

	/// <summary>
	/// 生成物品详情的 BBCode 文本（完整版，含装备位置/技能/容器信息）。
	/// </summary>
	public static string BuildDetail(Item item)
	{
		var sb = new StringBuilder();

		sb.Append($"[color=#ffffff]{item.Name}[/color]");
		var catDef = ItemCategoryDef.Get(item.Category);
		sb.AppendLine($"  [color=#888888][{catDef?.Name ?? item.Category}][/color]");

		if (item.SharpDamage > 0 || item.BluntDamage > 0)
		{
			sb.Append("[color=#ff6666]伤害:[/color] ");
			if (item.SharpDamage > 0) sb.Append($"锐{item.SharpDamage:F0} ");
			if (item.BluntDamage > 0) sb.Append($"钝{item.BluntDamage:F0} ");
			sb.AppendLine();
		}

		if (item.SharpArmor > 0 || item.BluntArmor > 0)
		{
			sb.Append("[color=#6699ff]护甲:[/color] ");
			if (item.SharpArmor > 0) sb.Append($"锐防{item.SharpArmor:F0} ");
			if (item.BluntArmor > 0) sb.Append($"钝防{item.BluntArmor:F0} ");
			sb.AppendLine();
		}

		if (item.IsEquippable)
		{
			var layerName = item.Layer switch
			{
				EquipLayer.Skin => "贴身",
				EquipLayer.Middle => "中层",
				EquipLayer.Shell => "外壳",
				EquipLayer.Overhead => "最外",
				_ => item.Layer.ToString(),
			};
			sb.AppendLine($"[color=#66cc99]位置:[/color] {item.BodyPart} / {layerName}");

			if (item.CoveredParts.Count > 0)
				sb.AppendLine($"[color=#66cc99]覆盖:[/color] {string.Join(", ", item.CoveredParts)}");
		}

		if (item.GrantedSkills.Count > 0)
		{
			var skillNames = new List<string>();
			foreach (var sid in item.GrantedSkills)
			{
				var def = InteractionDefs.Get(sid);
				skillNames.Add(def?.Name ?? sid);
			}
			sb.AppendLine($"[color=#cc99ff]技能:[/color] {string.Join(", ", skillNames)}");
		}

		if (item.IsContainer)
		{
			var count = item.Contents?.Count ?? 0;
			sb.AppendLine($"[color=#66ccff]容器:[/color] {count}件物品");
		}

		sb.Append($"[color=#888888]重量: {item.EffectiveWeight:F1}kg  价格: {item.Price}G[/color]");
		return sb.ToString();
	}
}
