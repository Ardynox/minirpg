using MiniRPG.Core.Config;

namespace MiniRPG.Core.Data;

public static class ItemConditionFormatter
{
	public static string GetConditionLabel(Item item)
	{
		var percent = item.ConditionPercent;
		var key = percent switch
		{
			>= 95f => "item.condition.pristine",
			>= 75f => "item.condition.good",
			>= 45f => "item.condition.worn",
			>= 15f => "item.condition.damaged",
			_ => "item.condition.critical",
		};
		var fallback = percent switch
		{
			>= 95f => "Pristine",
			>= 75f => "Good",
			>= 45f => "Worn",
			>= 15f => "Damaged",
			_ => "Critical",
		};
		return LocalizationService.TOrFallback(key, fallback);
	}

	public static string BuildInlineDurability(Item item) =>
		LocalizationService.TOrFallback(
			"item.inline.durability",
			"Dur {current}/{max} {condition}",
			("current", item.Durability),
			("max", item.MaxDurability),
			("condition", GetConditionLabel(item)));

	public static string BuildDetailDurability(Item item) =>
		LocalizationService.TOrFallback(
			"item.detail.durability",
			"Durability: {current}/{max} ({condition})",
			("current", item.Durability),
			("max", item.MaxDurability),
			("condition", GetConditionLabel(item)));
}
