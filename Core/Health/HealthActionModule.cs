using System;
using System.Linq;
using MiniRPG.Core.Combat;

namespace MiniRPG.Core.Health;

public static class HealthActionModule
{
	public static ActionExecutionResult TryTendSelf(GameState state, Actor actor) =>
		TryTend(state, actor, actor);

	public static ActionExecutionResult TryTendOther(GameState state, Actor actor, Actor target)
	{
		if (AI.FactionRelation.IsHostile(actor.Faction, target.Faction))
			return new ActionExecutionResult();
		if (Math.Abs(actor.X - target.X) + Math.Abs(actor.Y - target.Y) != 1 || actor.Z != target.Z)
			return new ActionExecutionResult();

		return TryTend(state, actor, target);
	}

	private static ActionExecutionResult TryTend(GameState state, Actor healer, Actor patient)
	{
		var result = new ActionExecutionResult();
		HealthSystem.Sync(healer, state.Turn, DefaultEnvironmentExposureProvider.Instance.Capture(state, healer), result.Events, state);
		if (!string.Equals(healer.Id, patient.Id, StringComparison.Ordinal))
			HealthSystem.Sync(patient, state.Turn, DefaultEnvironmentExposureProvider.Instance.Capture(state, patient), result.Events, state);

		if (healer.GetCapacity(Caps.Consciousness) < 0.1f || healer.GetCapacity(Caps.Manipulation) < 0.1f)
			return result;

		var condition = HealthSystem.GetMostSevereTreatableCondition(patient, state.Turn);
		if (condition == null)
			return result;

		var supply = FindBestMedicalSupply(healer);
		var quality = ComputeTreatmentQuality(healer, supply?.Item);
		HealthSystem.ApplyTreatment(patient, condition, quality, state.Turn, result.Events, state);
		if (supply != null)
			InventoryModule.ConsumeAt(healer, supply.Value.Index, 1);

		result.Consumed = true;
		return result;
	}

	private static (int Index, Item Item)? FindBestMedicalSupply(Actor actor)
	{
		(int Index, Item Item)? best = null;
		foreach (var (item, index) in actor.Inventory.Select((item, index) => (item, index)))
		{
			if (!item.Tags.ContainsKey(ItemTags.Healing))
				continue;

			if (best == null
				|| item.Tags[ItemTags.Healing] > best.Value.Item.Tags.GetValueOrDefault(ItemTags.Healing, 0)
				|| (item.Tags[ItemTags.Healing] == best.Value.Item.Tags.GetValueOrDefault(ItemTags.Healing, 0)
					&& (item.Price > best.Value.Item.Price
						|| (item.Price == best.Value.Item.Price
							&& string.CompareOrdinal(item.Id, best.Value.Item.Id) < 0))))
			{
				best = (index, item);
			}
		}

		return best;
	}

	private static float ComputeTreatmentQuality(Actor healer, Item? supply)
	{
		var healerTag = healer.ComputeTags().GetValueOrDefault(ItemTags.Healing, 0);
		var consciousness = healer.GetCapacity(Caps.Consciousness);
		var manipulation = healer.GetCapacity(Caps.Manipulation);
		var supplyTag = supply?.Tags.GetValueOrDefault(ItemTags.Healing, 0) ?? 0;
		var supplyBonus = supply != null
			? Math.Min(0.35f, supplyTag * 0.08f)
			: -0.15f;

		return Math.Clamp(
			0.15f
			+ consciousness * 0.2f
			+ manipulation * 0.25f
			+ Math.Min(0.2f, healerTag * 0.04f)
			+ supplyBonus,
			0.05f,
			1f);
	}
}
