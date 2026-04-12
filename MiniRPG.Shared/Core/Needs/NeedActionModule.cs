using System;
using System.Linq;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Needs;

public static class NeedActionModule
{
	public static bool HasBedroll(Actor actor) =>
		actor.Inventory.Any(item => string.Equals(item.Id, "bedroll", StringComparison.Ordinal));

	public static float GetBedrollQuality(Actor actor)
	{
		var bedroll = actor.Inventory.FirstOrDefault(item => string.Equals(item.Id, "bedroll", StringComparison.Ordinal));
		if (bedroll == null)
			return 1f;

		var quality = bedroll.Tags.GetValueOrDefault(ItemTags.RestQuality, 1);
		return Math.Max(0.5f, 1f + (quality - 1) * 0.15f);
	}

	public static NeedActionResult TryConsumeFood(GameState state, Actor actor, int inventoryIndex)
	{
		var result = new NeedActionResult();
		NeedSystem.Sync(actor, state.Turn, result.Events, state);

		if (inventoryIndex < 0 || inventoryIndex >= actor.Inventory.Count)
			return result;

		var item = actor.Inventory[inventoryIndex];
		if (!IsFood(item))
			return result;

		ConsumeFood(state, actor, item, state.Turn, result.Events);
		actor.Inventory.RemoveAt(inventoryIndex);
		var foodEvent = new GameEvent("food_consumed")
		{
			ActionName = item.Id,
		};
		IdentificationModule.PopulateInitiatorIdentity(foodEvent, state, actor);
		IdentificationModule.PopulateTargetIdentity(foodEvent, state, actor);
		IdentificationModule.PopulateItemIdentity(foodEvent, state, item);
		result.Events.Add(foodEvent);
		result.Consumed = true;
		return result;
	}

	public static NeedActionResult TryConsumeFood(GameState state, Actor actor, string worldFoodEntityId)
	{
		var result = new NeedActionResult();
		NeedSystem.Sync(actor, state.Turn, result.Events, state);

		if (string.IsNullOrWhiteSpace(worldFoodEntityId))
			return result;

		var item = state.World!.PickupItem(actor.X, actor.Y, state.PlayerZ, worldFoodEntityId);
		if (item == null || !IsFood(item))
			return result;

		ConsumeFood(state, actor, item, state.Turn, result.Events);
		var foodEvent = new GameEvent("food_consumed")
		{
			ActionName = item.Id,
		};
		IdentificationModule.PopulateInitiatorIdentity(foodEvent, state, actor);
		IdentificationModule.PopulateTargetIdentity(foodEvent, state, actor);
		IdentificationModule.PopulateItemIdentity(foodEvent, state, item);
		result.Events.Add(foodEvent);
		result.Consumed = true;
		return result;
	}

	public static NeedActionResult TryRest(GameState state, Actor actor, RestContext context)
	{
		var result = new NeedActionResult();
		var exposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, actor);
		HealthSystem.Sync(actor, state.Turn, exposure, result.Events);
		var profile = NeedCatalog.GetProfileForActor(actor);
		var healthProfile = HealthCatalog.GetProfileForActor(actor);

		if (context.RequiresBedroll && !HasBedroll(actor))
			return result;

		if (!actor.Needs.TryGetValue(NeedIds.Rest, out var restState))
			return result;

		var beforeRest = restState.Current;
		if (beforeRest >= NeedSystem.NeedMax)
			return result;

		var gain = profile.RestGainPerTurn * context.QualityMultiplier;
		restState.Current = Math.Clamp(restState.Current + gain, NeedSystem.NeedMin, NeedSystem.NeedMax);
		if (healthProfile.AllowWetness && actor.WetnessValue > 0f)
		{
			actor.WetnessValue = Math.Max(
				HealthSystem.ValueMin,
				actor.WetnessValue - healthProfile.RestDryingBonusPerTurn * context.QualityMultiplier);
		}
		NeedSystem.Sync(actor, state.Turn, result.Events, state);

		if (beforeRest < profile.SatisfiedThreshold
			&& restState.Current >= profile.SatisfiedThreshold)
		{
			NeedSystem.ApplyThought(actor, context.CompletionThoughtId, state.Turn, NeedThoughtSources.Sleep, result.Events, state);
			ApplySleepExposureThoughts(actor, exposure, state.Turn, result.Events, state);
			if (context.EmitCompletionEvent)
			{
				var completedEvent = new GameEvent("rest_completed")
				{
					ActionName = context.CompletionThoughtId,
				};
				IdentificationModule.PopulateInitiatorIdentity(completedEvent, state, actor);
				IdentificationModule.PopulateTargetIdentity(completedEvent, state, actor);
				result.Events.Add(completedEvent);
			}
		}

		result.Consumed = true;
		return result;
	}

	private static void ApplySleepExposureThoughts(
		Actor actor,
		EnvironmentExposureSnapshot exposure,
		int currentTurn,
		System.Collections.Generic.List<GameEvent> events,
		GameState state)
	{
		var profile = HealthCatalog.GetProfileForActor(actor);
		if (!profile.AllowWetness && !profile.AllowInfection)
			return;

		if (profile.AllowWetness && (actor.WetnessValue >= 35f || exposure.WetnessDelta > 0f))
			NeedSystem.ApplyThought(actor, "slept_wet", currentTurn, $"{HealthThoughtSources.Sleep}:wet", events, state);

		if ((profile.AllowWetness || profile.AllowInfection) && exposure.Cleanliness <= 25f)
			NeedSystem.ApplyThought(actor, "slept_dirty", currentTurn, $"{HealthThoughtSources.Sleep}:dirty", events, state);
	}

	private static void ConsumeFood(GameState state, Actor actor, Item item, int currentTurn, System.Collections.Generic.List<GameEvent> events)
	{
		var nutrition = Math.Min(NeedSystem.NeedMax, item.Tags.GetValueOrDefault(ItemTags.Nutrition, 0) * 20f);
		var current = NeedSystem.GetNeedValue(actor, NeedIds.Hunger);
		NeedSystem.SetNeedValue(actor, NeedIds.Hunger, current + nutrition);
		NeedSystem.Sync(actor, currentTurn, events, state);

		var thoughtId = ResolveFoodThought(item);
		if (thoughtId != null)
			NeedSystem.ApplyThought(actor, thoughtId, currentTurn, NeedThoughtSources.Meal, events, state);
	}

	private static string? ResolveFoodThought(Item item)
	{
		if (string.Equals(item.Id, "raw_meat", StringComparison.Ordinal))
			return "ate_raw";

		var mood = item.Tags.GetValueOrDefault(ItemTags.Mood, 0);
		if (mood >= 5)
			return "ate_lavish";
		if (mood >= 2)
			return "ate_fine";
		return "ate_simple";
	}

	private static bool IsFood(Item item) =>
		string.Equals(item.Category, ItemCategories.Food, StringComparison.Ordinal)
		|| item.Tags.ContainsKey(ItemTags.Nutrition);
}
