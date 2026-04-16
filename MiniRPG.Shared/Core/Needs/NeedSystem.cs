using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Needs;

public static class NeedSystem
{
	public const float NeedMin = 0f;
	public const float NeedMax = 100f;
	public const float DefaultNeedValue = 100f;
	public const float DefaultMoodValue = 50f;

	public static void EnsureInitialized(Actor actor, int currentTurn)
	{
		NeedCatalog.Load();
		var hadNeeds = actor.Needs?.Count > 0;
		actor.Needs ??= new Dictionary<string, NeedState>(StringComparer.Ordinal);
		actor.Thoughts ??= [];
		actor.DialogNeeds ??= new Dictionary<string, float>(StringComparer.Ordinal);

		var profile = NeedCatalog.GetProfileForActor(actor);
		if (!profile.AllowThoughts)
			actor.Thoughts.Clear();

		var allowedNeeds = new HashSet<string>(profile.EnabledNeeds, StringComparer.Ordinal);
		foreach (var needId in actor.Needs.Keys.ToList())
		{
			if (!allowedNeeds.Contains(needId))
				actor.Needs.Remove(needId);
		}

		foreach (var needId in profile.EnabledNeeds)
		{
			if (string.Equals(needId, NeedIds.Mood, StringComparison.Ordinal))
				continue;

			if (actor.Needs.ContainsKey(needId))
				continue;

			var current = DefaultNeedValue;
			if (string.Equals(needId, NeedIds.Hunger, StringComparison.Ordinal)
				&& actor.DialogNeeds.TryGetValue("food", out var foodGap))
			{
				current = ClampNeed((1f - foodGap) * NeedMax);
			}
			else if (string.Equals(needId, NeedIds.Thirst, StringComparison.Ordinal)
				&& actor.DialogNeeds.TryGetValue("water", out var waterGap))
			{
				current = ClampNeed((1f - waterGap) * NeedMax);
			}
			else if (string.Equals(needId, NeedIds.Rest, StringComparison.Ordinal)
				&& actor.DialogNeeds.TryGetValue("rest", out var restGap))
			{
				current = ClampNeed((1f - restGap) * NeedMax);
			}

			actor.Needs[needId] = new NeedState
			{
				Id = needId,
				Current = current,
				Min = NeedMin,
				Max = NeedMax,
				LastUpdatedTurn = currentTurn,
			};
		}

		if (!hadNeeds && actor.NeedsLastUpdatedTurn == 0 && currentTurn != 0)
			actor.NeedsLastUpdatedTurn = currentTurn;

		if (!profile.AllowMood)
		{
			actor.MoodValue = 0f;
		}
		else if (actor.MoodValue <= 0f && actor.DialogMood != 0f)
		{
			actor.MoodValue = ClampNeed(DefaultMoodValue + actor.DialogMood * 50f);
		}
		else if (actor.MoodValue <= 0f)
		{
			actor.MoodValue = DefaultMoodValue;
		}

		ProjectDialogState(actor, profile);
	}

	public static void Sync(Actor actor, int currentTurn, List<GameEvent>? events = null, GameState? state = null)
	{
		EnsureInitialized(actor, currentTurn);
		var profile = NeedCatalog.GetProfileForActor(actor);
		if (!profile.AllowThoughts)
			actor.Thoughts.Clear();

		if (actor.NeedsLastUpdatedTurn > currentTurn)
			actor.NeedsLastUpdatedTurn = currentTurn;

		var delta = Math.Max(0, currentTurn - actor.NeedsLastUpdatedTurn);
		if (delta > 0)
		{
			foreach (var need in actor.Needs.Values)
			{
				var previousStage = NeedCatalog.ResolveStageId(need.Id, need.Current);
				var decay = profile.DecayPerTurn.GetValueOrDefault(need.Id, 0f);
				need.Current = ClampNeed(need.Current - decay * delta);
				need.LastUpdatedTurn = currentTurn;
				EmitStageChange(actor, need.Id, previousStage, NeedCatalog.ResolveStageId(need.Id, need.Current), events, state);
			}
			actor.NeedsLastUpdatedTurn = currentTurn;
		}

		RemoveExpiredThoughts(actor, currentTurn);
		RefreshStageThought(actor, NeedIds.Hunger, NeedThoughtSources.HungerStage, currentTurn, events, state);
		RefreshStageThought(actor, NeedIds.Thirst, NeedThoughtSources.ThirstStage, currentTurn, events, state);
		RefreshStageThought(actor, NeedIds.Rest, NeedThoughtSources.RestStage, currentTurn, events, state);
		RecomputeMood(actor, profile);
		ProjectDialogState(actor, profile);
	}

	public static float GetNeedValue(Actor actor, string needId)
	{
		EnsureInitialized(actor, actor.NeedsLastUpdatedTurn);
		return actor.Needs.TryGetValue(needId, out var need)
			? need.Current
			: NeedMax;
	}

	public static float GetNeedValueSnapshot(Actor actor, string needId)
	{
		if (actor.Needs == null)
			return NeedMax;

		return actor.Needs.TryGetValue(needId, out var need)
			? need.Current
			: NeedMax;
	}

	public static void SetNeedValue(Actor actor, string needId, float current)
	{
		EnsureInitialized(actor, actor.NeedsLastUpdatedTurn);
		if (actor.Needs.TryGetValue(needId, out var state))
			state.Current = ClampNeed(current);
	}

	public static float GetCapacityMultiplier(Actor actor, string capacityId)
	{
		EnsureInitialized(actor, actor.NeedsLastUpdatedTurn);
		var multiplier = 1f;

		foreach (var need in actor.Needs.Values)
		{
			var stageId = NeedCatalog.ResolveStageId(need.Id, need.Current);
			if (string.IsNullOrEmpty(stageId))
				continue;

			var stageDef = NeedCatalog.GetNeed(need.Id)?.Stages
				.FirstOrDefault(stage => string.Equals(stage.Id, stageId, StringComparison.Ordinal));
			if (stageDef == null || stageDef.CapacityMultipliers.Count == 0)
				continue;

			if (stageDef.CapacityMultipliers.TryGetValue(capacityId, out var stageMultiplier))
				multiplier *= stageMultiplier;
		}

		return multiplier;
	}

	public static IReadOnlyList<ThoughtState> GetTopThoughts(Actor actor, int currentTurn, int maxCount = 3)
	{
		Sync(actor, currentTurn);
		return GetTopThoughtsSnapshot(actor, currentTurn, maxCount);
	}

	public static IReadOnlyList<ThoughtState> GetTopThoughtsSnapshot(Actor actor, int currentTurn, int maxCount = 3)
	{
		var thoughts = actor.Thoughts ?? [];
		return thoughts
			.Where(thought => thought.ExpiresOnTurn < 0 || thought.ExpiresOnTurn > currentTurn)
			.OrderByDescending(thought => Math.Abs(thought.MoodOffset))
			.ThenBy(thought => thought.Id, StringComparer.Ordinal)
			.Take(maxCount)
			.ToList();
	}

	public static bool ApplyThought(Actor actor, string thoughtId, int currentTurn, string source, List<GameEvent>? events = null, GameState? state = null)
	{
		var def = NeedCatalog.GetThought(thoughtId);
		if (def == null)
			return false;

		return ApplyTemporaryThought(actor, thoughtId, def.MoodOffset, def.DurationTurns, currentTurn, source, events, state);
	}

	public static bool ApplyTemporaryThought(
		Actor actor,
		string thoughtId,
		float moodOffset,
		int durationTurns,
		int currentTurn,
		string source,
		List<GameEvent>? events = null,
		GameState? state = null)
	{
		EnsureInitialized(actor, currentTurn);
		var profile = NeedCatalog.GetProfileForActor(actor);
		if (!profile.AllowThoughts)
			return false;

		var existing = actor.Thoughts.FirstOrDefault(thought =>
			string.Equals(thought.Id, thoughtId, StringComparison.Ordinal)
			&& string.Equals(thought.Source, source, StringComparison.Ordinal));
		var expiresOnTurn = durationTurns < 0 ? -1 : currentTurn + durationTurns;
		if (existing != null)
		{
			existing.MoodOffset = moodOffset;
			existing.ExpiresOnTurn = expiresOnTurn;
		}
		else
		{
			actor.Thoughts.Add(new ThoughtState
			{
				Id = thoughtId,
				MoodOffset = moodOffset,
				ExpiresOnTurn = expiresOnTurn,
				Source = source,
			});
		}

		RecomputeMood(actor, profile);
		ProjectDialogState(actor, profile);
		EmitThoughtApplied(actor, thoughtId, source, moodOffset, events, state);
		return true;
	}

	private static void RemoveExpiredThoughts(Actor actor, int currentTurn)
	{
		actor.Thoughts.RemoveAll(thought => thought.ExpiresOnTurn >= 0 && thought.ExpiresOnTurn <= currentTurn);
	}

	private static void RefreshStageThought(
		Actor actor,
		string needId,
		string source,
		int currentTurn,
		List<GameEvent>? events,
		GameState? state)
	{
		var profile = NeedCatalog.GetProfileForActor(actor);
		if (!profile.AllowThoughts)
			return;

		var existing = actor.Thoughts
			.FirstOrDefault(thought => string.Equals(thought.Source, source, StringComparison.Ordinal));
		if (!actor.Needs.TryGetValue(needId, out var need))
		{
			if (existing != null)
				actor.Thoughts.Remove(existing);
			return;
		}

		var stageId = NeedCatalog.ResolveStageId(needId, need.Current);
		if (string.IsNullOrWhiteSpace(stageId))
		{
			if (existing != null)
				actor.Thoughts.Remove(existing);
			return;
		}

		if (existing != null && string.Equals(existing.Id, stageId, StringComparison.Ordinal))
			return;

		if (existing != null)
			actor.Thoughts.Remove(existing);
		ApplyThought(actor, stageId, currentTurn, source, events, state);
	}

	private static void RecomputeMood(Actor actor, NeedProfileDef profile)
	{
		if (!profile.AllowMood)
		{
			actor.MoodValue = 0f;
			return;
		}

		var mood = DefaultMoodValue;
		if (profile.AllowThoughts)
			mood += actor.Thoughts.Sum(thought => thought.MoodOffset);
		actor.MoodValue = ClampNeed(mood);
	}

	private static void ProjectDialogState(Actor actor, NeedProfileDef profile)
	{
		actor.DialogMood = profile.AllowMood
			? (actor.MoodValue - DefaultMoodValue) / DefaultMoodValue
			: 0f;
		actor.DialogNeeds["food"] = actor.Needs.TryGetValue(NeedIds.Hunger, out var hunger)
			? Math.Clamp((NeedMax - hunger.Current) / NeedMax, 0f, 1f)
			: 0f;
		actor.DialogNeeds["water"] = actor.Needs.TryGetValue(NeedIds.Thirst, out var thirst)
			? Math.Clamp((NeedMax - thirst.Current) / NeedMax, 0f, 1f)
			: 0f;
		actor.DialogNeeds["rest"] = actor.Needs.TryGetValue(NeedIds.Rest, out var rest)
			? Math.Clamp((NeedMax - rest.Current) / NeedMax, 0f, 1f)
			: 0f;
	}

	private static void EmitStageChange(
		Actor actor,
		string needId,
		string? previousStage,
		string? nextStage,
		List<GameEvent>? events,
		GameState? state)
	{
		if (events == null || string.Equals(previousStage, nextStage, StringComparison.Ordinal))
			return;

		var stageEvent = new GameEvent("need_stage_changed")
		{
			EffectType = needId,
			ActionName = nextStage ?? "stable",
			ItemName = previousStage ?? "stable",
		};
		IdentificationModule.PopulateInitiatorIdentity(stageEvent, state, actor);
		IdentificationModule.PopulateTargetIdentity(stageEvent, state, actor);
		events.Add(stageEvent);
	}

	private static void EmitThoughtApplied(Actor actor, string thoughtId, string source, float moodOffset, List<GameEvent>? events, GameState? state)
	{
		if (events == null)
			return;

		var thoughtEvent = new GameEvent("thought_applied")
		{
			EffectType = source,
			ActionName = thoughtId,
			Damage = (int)MathF.Round(moodOffset),
			ItemName = moodOffset.ToString("0.##", CultureInfo.InvariantCulture),
		};
		IdentificationModule.PopulateInitiatorIdentity(thoughtEvent, state, actor);
		IdentificationModule.PopulateTargetIdentity(thoughtEvent, state, actor);
		events.Add(thoughtEvent);
	}

	private static float ClampNeed(float value) => Math.Clamp(value, NeedMin, NeedMax);
}
