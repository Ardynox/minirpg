using System;
using System.Linq;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;

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

		var stage = FoodFreshnessEvaluator.Evaluate(item, state.Turn);
		if (stage == FoodFreshnessStage.Rotten)
		{
			EmitFoodRejected(state, actor, item, result.Events);
			return result;
		}

		ConsumeFood(state, actor, item, state.Turn, stage, result.Events);
		actor.Inventory.RemoveAt(inventoryIndex);
		EmitFoodConsumed(state, actor, item, stage, result.Events);
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

		var stage = FoodFreshnessEvaluator.Evaluate(item, state.Turn);
		if (stage == FoodFreshnessStage.Rotten)
		{
			// 已经被 PickupItem 摘出来：放回地上，不消耗。
			state.World!.PlaceItem(actor.X, actor.Y, state.PlayerZ, item);
			EmitFoodRejected(state, actor, item, result.Events);
			return result;
		}

		ConsumeFood(state, actor, item, state.Turn, stage, result.Events);
		EmitFoodConsumed(state, actor, item, stage, result.Events);
		result.Consumed = true;
		return result;
	}

	private static void EmitFoodConsumed(GameState state, Actor actor, Item item, FoodFreshnessStage stage, System.Collections.Generic.List<GameEvent> events)
	{
		var foodEvent = new GameEvent("food_consumed")
		{
			ActionName = item.Id,
			EffectType = FreshnessStageId(stage),
		};
		IdentificationModule.PopulateInitiatorIdentity(foodEvent, state, actor);
		IdentificationModule.PopulateTargetIdentity(foodEvent, state, actor);
		IdentificationModule.PopulateItemIdentity(foodEvent, state, item);
		events.Add(foodEvent);
	}

	private static void EmitFoodRejected(GameState state, Actor actor, Item item, System.Collections.Generic.List<GameEvent> events)
	{
		var rejected = new GameEvent("food_rejected")
		{
			ActionName = item.Id,
			EffectType = FreshnessStageId(FoodFreshnessStage.Rotten),
		};
		IdentificationModule.PopulateInitiatorIdentity(rejected, state, actor);
		IdentificationModule.PopulateTargetIdentity(rejected, state, actor);
		IdentificationModule.PopulateItemIdentity(rejected, state, item);
		events.Add(rejected);
	}

	internal static string FreshnessStageId(FoodFreshnessStage stage) => stage switch
	{
		FoodFreshnessStage.Fresh => "fresh",
		FoodFreshnessStage.Stale => "stale",
		FoodFreshnessStage.Spoiled => "spoiled",
		FoodFreshnessStage.Rotten => "rotten",
		_ => "fresh",
	};

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

	private static void ConsumeFood(GameState state, Actor actor, Item item, int currentTurn, FoodFreshnessStage stage, System.Collections.Generic.List<GameEvent> events)
	{
		var nutritionMultiplier = stage switch
		{
			FoodFreshnessStage.Fresh => 1.0f,
			FoodFreshnessStage.Stale => 0.7f,
			FoodFreshnessStage.Spoiled => 0.3f,
			FoodFreshnessStage.Rotten => 0.1f, // 防御性分支；TryConsumeFood 在正常路径已拒绝 Rotten
			_ => 1.0f,
		};
		var nutrition = Math.Min(NeedSystem.NeedMax, item.Tags.GetValueOrDefault(ItemTags.Nutrition, 0) * 20f * nutritionMultiplier);
		var current = NeedSystem.GetNeedValue(actor, NeedIds.Hunger);
		NeedSystem.SetNeedValue(actor, NeedIds.Hunger, current + nutrition);
		NeedSystem.Sync(actor, currentTurn, events, state);

		// Stale / Spoiled / Rotten 三档单独的"吃了变质食物"thought，叠在原来的"raw_meat / 普通 / 精致"上面。
		var freshnessThought = stage switch
		{
			FoodFreshnessStage.Stale => "ate_stale",
			FoodFreshnessStage.Spoiled => "ate_spoiled",
			FoodFreshnessStage.Rotten => "ate_rotten",
			_ => null,
		};
		if (freshnessThought != null)
			NeedSystem.ApplyThought(actor, freshnessThought, currentTurn, NeedThoughtSources.Meal, events, state);

		// 原有的 raw_meat / mood-based food thought，无论新鲜度都加（保持兼容）。
		var thoughtId = ResolveFoodThought(item);
		if (thoughtId != null)
			NeedSystem.ApplyThought(actor, thoughtId, currentTurn, NeedThoughtSources.Meal, events, state);

		// Spoiled / Rotten 直接挂 food_poisoning 健康 condition；severity 和 fresh-turns 比例正相关。
		if (stage == FoodFreshnessStage.Spoiled || stage == FoodFreshnessStage.Rotten)
		{
			var severity = stage == FoodFreshnessStage.Spoiled ? 30f : 60f;
			HealthSystem.AddOrUpdateInjury(
				actor,
				HealthConditionIds.FoodPoisoning,
				limbId: null,
				severity: severity,
				source: $"food:{item.Id}",
				currentTurn,
				events,
				state);
		}
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

	public static NeedActionResult TryConsumeDrink(GameState state, Actor actor, int inventoryIndex)
	{
		var result = new NeedActionResult();
		NeedSystem.Sync(actor, state.Turn, result.Events, state);

		if (inventoryIndex < 0 || inventoryIndex >= actor.Inventory.Count)
			return result;

		var item = actor.Inventory[inventoryIndex];
		if (!IsDrink(item))
			return result;

		ConsumeDrink(state, actor, item, state.Turn, result.Events);
		actor.Inventory.RemoveAt(inventoryIndex);
		var drinkEvent = new GameEvent("drink_consumed")
		{
			ActionName = item.Id,
		};
		IdentificationModule.PopulateInitiatorIdentity(drinkEvent, state, actor);
		IdentificationModule.PopulateTargetIdentity(drinkEvent, state, actor);
		IdentificationModule.PopulateItemIdentity(drinkEvent, state, item);
		result.Events.Add(drinkEvent);
		result.Consumed = true;
		return result;
	}

	public static NeedActionResult TryConsumeDrink(GameState state, Actor actor, string worldDrinkEntityId)
	{
		var result = new NeedActionResult();
		NeedSystem.Sync(actor, state.Turn, result.Events, state);

		if (string.IsNullOrWhiteSpace(worldDrinkEntityId))
			return result;

		var item = state.World!.PickupItem(actor.X, actor.Y, state.PlayerZ, worldDrinkEntityId);
		if (item == null || !IsDrink(item))
			return result;

		ConsumeDrink(state, actor, item, state.Turn, result.Events);
		var drinkEvent = new GameEvent("drink_consumed")
		{
			ActionName = item.Id,
		};
		IdentificationModule.PopulateInitiatorIdentity(drinkEvent, state, actor);
		IdentificationModule.PopulateTargetIdentity(drinkEvent, state, actor);
		IdentificationModule.PopulateItemIdentity(drinkEvent, state, item);
		result.Events.Add(drinkEvent);
		result.Consumed = true;
		return result;
	}

	private static void ConsumeDrink(GameState state, Actor actor, Item item, int currentTurn, System.Collections.Generic.List<GameEvent> events)
	{
		var hydration = Math.Min(NeedSystem.NeedMax, item.Tags.GetValueOrDefault(ItemTags.Hydration, 0) * 20f);
		var current = NeedSystem.GetNeedValue(actor, NeedIds.Thirst);
		NeedSystem.SetNeedValue(actor, NeedIds.Thirst, current + hydration);
		NeedSystem.Sync(actor, currentTurn, events, state);

		NeedSystem.ApplyThought(actor, "drank_water", currentTurn, NeedThoughtSources.Drink, events, state);
	}

	private static bool IsDrink(Item item) =>
		item.Tags.ContainsKey(ItemTags.Hydration);
}
