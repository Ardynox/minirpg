using System;
using System.Collections.Generic;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using MiniRPG.Core.Job;
using MiniRPG.Core.Needs;
using MiniRPG.Core.Social;
using MiniRPG.Core.World;

namespace MiniRPG.Core.AI.Utility;

public static class InputResolver
{
	private static readonly Dictionary<string, Func<InputContext, float>> _resolvers = new(StringComparer.Ordinal);

	static InputResolver()
	{
		RegisterSurvivalInputs();
		RegisterHealthInputs();
		RegisterCombatInputs();
		RegisterNeedInputs();
		RegisterJobInputs();
		RegisterSocialInputs();
		RegisterPersonalityInputs();
		RegisterEnvironmentInputs();
		RegisterAwarenessInputs();
		RegisterMiscInputs();
	}

	public static float Resolve(string inputId, InputContext ctx)
	{
		if (_resolvers.TryGetValue(inputId, out var resolver))
			return resolver(ctx);
		return 0f;
	}

	public static void Register(string inputId, Func<InputContext, float> resolver) =>
		_resolvers[inputId] = resolver;

	private static void RegisterSurvivalInputs()
	{
		_resolvers["self_on_fire"] = static ctx =>
			HasCondition(ctx.Self, HealthConditionIds.OnFire) ? 1f : 0f;

		_resolvers["self_on_fire_severity"] = static ctx =>
			GetConditionSeverity(ctx.Self, HealthConditionIds.OnFire) / 100f;

		_resolvers["cell_fire_danger"] = static ctx =>
		{
			if (ctx.State?.World == null) return 0f;
			var intensity = FireSystem.GetFireIntensityAt(ctx.State, ctx.Self.X, ctx.Self.Y, ctx.Self.Z);
			for (var i = 0; i < Dirs.Length; i++)
			{
				var (dx, dy) = Dirs[i];
				intensity += FireSystem.GetFireIntensityAt(ctx.State, ctx.Self.X + dx, ctx.Self.Y + dy, ctx.Self.Z);
			}
			return Math.Min(intensity / 30f, 1f);
		};

		_resolvers["nearby_fire_exists"] = static ctx =>
		{
			if (ctx.State?.World == null) return 0f;
			var radius = 6;
			for (var dy = -radius; dy <= radius; dy++)
			{
				for (var dx = -radius; dx <= radius; dx++)
				{
					if (Math.Abs(dx) + Math.Abs(dy) > radius) continue;
					if (FireSystem.IsDangerousCell(ctx.State, ctx.Self.X + dx, ctx.Self.Y + dy, ctx.Self.Z))
						return 1f;
				}
			}
			return 0f;
		};

		_resolvers["has_hypothermia"] = static ctx =>
			HasCondition(ctx.Self, HealthConditionIds.Hypothermia) ? 1f : 0f;

		_resolvers["has_heatstroke"] = static ctx =>
			HasCondition(ctx.Self, HealthConditionIds.Heatstroke) ? 1f : 0f;

		_resolvers["is_cold"] = static ctx =>
		{
			if (ctx.State == null) return 0f;
			var exposure = GetExposure(ctx);
			if (!exposure.HasWeatherData) return 0f;
			if (HasCondition(ctx.Self, HealthConditionIds.Hypothermia)) return 1f;
			if (exposure.AmbientTemperature <= 2f) return 1f;
			if (ctx.Self.WetnessValue >= 60f && exposure.AmbientTemperature <= 8f) return 1f;
			return 0f;
		};

		_resolvers["is_hot"] = static ctx =>
		{
			if (ctx.State == null) return 0f;
			var exposure = GetExposure(ctx);
			if (!exposure.HasWeatherData) return 0f;
			if (HasCondition(ctx.Self, HealthConditionIds.Heatstroke)) return 1f;
			return exposure.AmbientTemperature >= 34f ? 1f : 0f;
		};

		_resolvers["cold_severity"] = static ctx =>
		{
			if (ctx.State == null) return 0f;
			var exposure = GetExposure(ctx);
			if (!exposure.HasWeatherData) return 0f;
			var temp = exposure.AmbientTemperature;
			if (temp >= 10f) return 0f;
			return Math.Min((10f - temp) / 30f, 1f);
		};

		_resolvers["hot_severity"] = static ctx =>
		{
			if (ctx.State == null) return 0f;
			var exposure = GetExposure(ctx);
			if (!exposure.HasWeatherData) return 0f;
			var temp = exposure.AmbientTemperature;
			if (temp <= 28f) return 0f;
			return Math.Min((temp - 28f) / 20f, 1f);
		};

		_resolvers["has_portable_heat"] = static ctx =>
		{
			foreach (var item in ctx.Self.Inventory)
			{
				if (!item.Equipped && item.PortableWarmthC > 0)
					return 1f;
			}
			return 0f;
		};

		_resolvers["nearby_campfire"] = static ctx =>
		{
			if (ctx.State?.World == null) return 0f;
			var radius = 8;
			for (var dy = -radius; dy <= radius; dy++)
			{
				for (var dx = -radius; dx <= radius; dx++)
				{
					if (Math.Abs(dx) + Math.Abs(dy) > radius) continue;
					var x = ctx.Self.X + dx;
					var y = ctx.Self.Y + dy;
					if (ctx.State.World.HasFixture(x, y, ctx.Self.Z, Entities.Campfire))
						return 1f;
				}
			}
			return 0f;
		};
	}

	private static void RegisterHealthInputs()
	{
		_resolvers["pain_norm"] = static ctx => ctx.Self.PainValue / 100f;
		_resolvers["blood_loss_norm"] = static ctx => ctx.Self.BloodLossValue / 100f;
		_resolvers["wetness_norm"] = static ctx => ctx.Self.WetnessValue / 100f;

		_resolvers["has_treatable_condition"] = static ctx =>
			HealthSystem.HasTreatableCondition(ctx.Self, ctx.State?.Turn ?? 0) ? 1f : 0f;

		_resolvers["worst_condition_severity"] = static ctx =>
		{
			var worst = 0f;
			foreach (var cond in ctx.Self.HealthConditions)
			{
				if (cond.Permanent) continue;
				if (cond.Severity > worst) worst = cond.Severity;
			}
			return worst / 100f;
		};

		_resolvers["infection_severity"] = static ctx =>
			GetConditionSeverity(ctx.Self, HealthConditionIds.Infection) / 100f;

		_resolvers["can_tend_self"] = static ctx =>
			ctx.Self.GetCapacity(Caps.Consciousness) >= 0.1f
			&& ctx.Self.GetCapacity(Caps.Manipulation) >= 0.1f
			&& HealthSystem.HasTreatableCondition(ctx.Self, ctx.State?.Turn ?? 0) ? 1f : 0f;

		_resolvers["adjacent_patient_exists"] = static ctx =>
		{
			if (ctx.State == null) return 0f;
			for (var i = 0; i < Dirs.Length; i++)
			{
				var (dx, dy) = Dirs[i];
				foreach (var other in ActorModule.GetAllAt(ctx.State, ctx.Self.X + dx, ctx.Self.Y + dy, ctx.Self.Z))
				{
					if (other.Id == ctx.Self.Id || CombatModule.IsDead(other)) continue;
					if (FactionRelation.IsHostile(ctx.Self.Faction, other.Faction)) continue;
					if (HealthSystem.HasTreatableCondition(other, ctx.State.Turn))
						return 1f;
				}
			}
			return 0f;
		};

		_resolvers["hp_ratio"] = static ctx =>
		{
			if (ctx.Self.Limbs.Count == 0) return 0f;
			var total = 0f;
			var max = 0f;
			foreach (var limb in ctx.Self.Limbs)
			{
				total += limb.Durability;
				max += limb.MaxDurability;
			}
			return max > 0 ? total / max : 0f;
		};
	}

	private static void RegisterCombatInputs()
	{
		_resolvers["has_enemy_in_vision"] = static ctx =>
		{
			foreach (var other in ctx.Perception.NearbyActors)
			{
				if (CombatModule.IsDead(other)) continue;
				if (FactionRelation.IsHostile(ctx.Self.Faction, other.Faction))
					return 1f;
			}
			return 0f;
		};

		_resolvers["has_adjacent_enemy"] = static ctx =>
		{
			foreach (var other in ctx.Perception.NearbyActors)
			{
				if (CombatModule.IsDead(other)) continue;
				if (!FactionRelation.IsHostile(ctx.Self.Faction, other.Faction)) continue;
				if (AIUtil.IsAdjacent3D(ctx.Self, other))
					return 1f;
			}
			return 0f;
		};

		_resolvers["enemy_count_norm"] = static ctx =>
		{
			var count = 0;
			foreach (var other in ctx.Perception.NearbyActors)
			{
				if (CombatModule.IsDead(other)) continue;
				if (FactionRelation.IsHostile(ctx.Self.Faction, other.Faction))
					count++;
			}
			return Math.Min(count / 5f, 1f);
		};

		_resolvers["closest_enemy_distance_inv"] = static ctx =>
		{
			var best = int.MaxValue;
			foreach (var other in ctx.Perception.NearbyActors)
			{
				if (CombatModule.IsDead(other)) continue;
				if (!FactionRelation.IsHostile(ctx.Self.Faction, other.Faction)) continue;
				var d = AIUtil.Distance3D(ctx.Self, other);
				if (d < best) best = d;
			}
			return best == int.MaxValue ? 0f : Math.Max(0f, 1f - best / 10f);
		};

		_resolvers["has_attack_skill"] = static ctx =>
		{
			var skills = CombatModule.GetAttackActions(ctx.Self);
			foreach (var s in skills)
			{
				if (!ctx.Self.IsSkillOnCooldown(s.Id))
					return 1f;
			}
			return 0f;
		};

		_resolvers["can_attack_target"] = static ctx =>
		{
			if (ctx.TargetActor == null || ctx.State == null) return 0f;
			var skills = CombatModule.GetAttackActions(ctx.Self);
			foreach (var s in skills)
			{
				if (ctx.Self.IsSkillOnCooldown(s.Id)) continue;
				if (ActionModule.CanCastSkill(ctx.State, ctx.Self, s.Id, SkillTargetType.Actor, targetActor: ctx.TargetActor))
					return 1f;
			}
			return 0f;
		};

		_resolvers["target_hp_ratio"] = static ctx =>
		{
			if (ctx.TargetActor == null) return 0f;
			if (ctx.TargetActor.Limbs.Count == 0) return 0f;
			var total = 0f;
			var max = 0f;
			foreach (var limb in ctx.TargetActor.Limbs)
			{
				total += limb.Durability;
				max += limb.MaxDurability;
			}
			return max > 0 ? total / max : 0f;
		};

		_resolvers["self_hp_advantage"] = static ctx =>
		{
			var selfHp = Resolve("hp_ratio", ctx);
			var targetHp = Resolve("target_hp_ratio", ctx);
			return Clamp01((selfHp - targetHp + 1f) / 2f);
		};

		_resolvers["target_is_hostile"] = static ctx =>
			ctx.TargetActor != null && FactionRelation.IsHostile(ctx.Self.Faction, ctx.TargetActor.Faction) ? 1f : 0f;
	}

	private static void RegisterNeedInputs()
	{
		_resolvers["hunger_urgency"] = static ctx =>
		{
			if (!ctx.Self.Needs.TryGetValue(NeedIds.Hunger, out var need)) return 0f;
			return 1f - need.Current / need.Max;
		};

		_resolvers["rest_urgency"] = static ctx =>
		{
			if (!ctx.Self.Needs.TryGetValue(NeedIds.Rest, out var need)) return 0f;
			return 1f - need.Current / need.Max;
		};

		_resolvers["mood_norm"] = static ctx => ctx.Self.MoodValue / 100f;
		_resolvers["mood_crisis"] = static ctx => ctx.Self.MoodValue <= 15f ? 1f : 0f;
		_resolvers["mood_critical"] = static ctx => ctx.Self.MoodValue <= 5f ? 1f : 0f;

		_resolvers["food_available"] = static ctx =>
		{
			foreach (var item in ctx.Self.Inventory)
			{
				if (item.Category == ItemCategories.Food || item.Tags.ContainsKey("饱腹"))
					return 1f;
			}
			if (ctx.State?.World == null) return 0f;
			var radius = 6;
			for (var dy = -radius; dy <= radius; dy++)
			{
				for (var dx = -radius; dx <= radius; dx++)
				{
					if (Math.Abs(dx) + Math.Abs(dy) > radius) continue;
					var x = ctx.Self.X + dx;
					var y = ctx.Self.Y + dy;
					if (FireSystem.IsDangerousCell(ctx.State, x, y, ctx.Self.Z)) continue;
					foreach (var entity in ctx.State.World.GetEntitiesByType(x, y, ctx.Self.Z, CellEntityType.Item))
					{
						var templateId = WorldMap.ResolveGroundItemTemplateId(entity);
						if (!PresetDB.Items.TryGetValue(templateId, out var preset)) continue;
						if (string.Equals(preset.Category, ItemCategories.Food, StringComparison.Ordinal)
							|| preset.Tags.ContainsKey("饱腹"))
							return 1f;
					}
				}
			}
			return 0f;
		};

		_resolvers["has_bedroll"] = static ctx =>
			NeedActionModule.HasBedroll(ctx.Self) ? 1f : 0f;

		_resolvers["at_home"] = static ctx =>
			ctx.Self.HasHomePosition && ctx.Self.X == ctx.Self.HomeX && ctx.Self.Y == ctx.Self.HomeY && ctx.Self.Z == ctx.Self.HomeZ ? 1f : 0f;

		_resolvers["has_mental_break"] = static ctx =>
			ctx.Self.MentalBreak != null ? 1f : 0f;
	}

	private static void RegisterJobInputs()
	{
		_resolvers["is_worker"] = static ctx =>
			string.Equals(ctx.Self.BrainId, WorkBrainIds.DomainWorker, StringComparison.Ordinal) ? 1f : 0f;

		_resolvers["has_reserved_ticket"] = static ctx =>
		{
			if (ctx.State == null) return 0f;
			return JobScheduler.GetReservedTicket(ctx.State, ctx.Self) != null ? 1f : 0f;
		};

		_resolvers["has_matching_ticket"] = static ctx =>
		{
			if (ctx.State == null) return 0f;
			if (ctx.TargetTicket != null) return 1f;
			return JobScheduler.FindBestTicket(ctx.State, ctx.Self) != null ? 1f : 0f;
		};

		_resolvers["ticket_priority_norm"] = static ctx =>
			ctx.TargetTicket != null ? Math.Min(ctx.TargetTicket.Priority / 60f, 1f) : 0f;

		_resolvers["ticket_distance_inv"] = static ctx =>
		{
			if (ctx.TargetTicket == null || ctx.State == null) return 0f;
			if (!ctx.State.Facilities.TryGetValue(ctx.TargetTicket.FacilityId, out var facility)) return 0f;
			var dist = Math.Abs(facility.AnchorX - ctx.Self.X) + Math.Abs(facility.AnchorY - ctx.Self.Y);
			return Math.Max(0f, 1f - dist / 30f);
		};

		_resolvers["job_specialization_match"] = static ctx =>
		{
			if (ctx.TargetTicket == null || ctx.TargetTicket.Type != WorkTicketType.ProduceRecipe) return 0.5f;
			var recipe = RecipeRegistry.Get(ctx.TargetTicket.RecipeId);
			if (recipe == null) return 0.5f;
			var tags = ctx.Self.ComputeTags();
			var matched = 0;
			var total = 0;
			foreach (var (capId, _) in recipe.RequiredCapacities)
			{
				total++;
				if (ctx.Self.GetCapacity(capId) >= 0.5f) matched++;
			}
			return total == 0 ? 0.5f : (float)matched / total;
		};
	}

	private static void RegisterSocialInputs()
	{
		_resolvers["ally_on_fire_nearby"] = static ctx =>
		{
			foreach (var other in ctx.Perception.NearbyActors)
			{
				if (CombatModule.IsDead(other)) continue;
				if (FactionRelation.IsHostile(ctx.Self.Faction, other.Faction)) continue;
				if (other.Id == ctx.Self.Id) continue;
				if (HasCondition(other, HealthConditionIds.OnFire))
					return 1f;
			}
			return 0f;
		};

		_resolvers["ally_wounded_nearby"] = static ctx =>
		{
			foreach (var other in ctx.Perception.NearbyActors)
			{
				if (CombatModule.IsDead(other)) continue;
				if (FactionRelation.IsHostile(ctx.Self.Faction, other.Faction)) continue;
				if (other.Id == ctx.Self.Id) continue;
				if (HealthSystem.HasTreatableCondition(other, ctx.State?.Turn ?? 0))
					return 1f;
			}
			return 0f;
		};

		_resolvers["relationship_opinion"] = static ctx =>
		{
			if (ctx.TargetActor == null || ctx.State == null) return 0.5f;
			var opinion = SocialModule.GetOpinion(ctx.State, ctx.Self.Id, ctx.TargetActor.Id);
			return (opinion + 100f) / 200f;
		};

		_resolvers["target_is_friend"] = static ctx =>
		{
			if (ctx.TargetActor == null || ctx.State == null) return 0f;
			return SocialModule.IsFriend(ctx.State, ctx.Self.Id, ctx.TargetActor.Id) ? 1f : 0f;
		};

		_resolvers["target_is_rival"] = static ctx =>
		{
			if (ctx.TargetActor == null || ctx.State == null) return 0f;
			return SocialModule.IsRival(ctx.State, ctx.Self.Id, ctx.TargetActor.Id) ? 1f : 0f;
		};

		_resolvers["social_cooldown_ready"] = static ctx =>
		{
			if (ctx.State == null) return 0f;
			if (!ctx.State.SocialState.SocialCooldowns.TryGetValue(ctx.Self.Id, out var lastTurn))
				return 1f;
			return ctx.State.Turn - lastTurn >= 10 ? 1f : 0f;
		};

		_resolvers["has_social_target_nearby"] = static ctx =>
		{
			foreach (var other in ctx.Perception.NearbyActors)
			{
				if (CombatModule.IsDead(other)) continue;
				if (other.Id == ctx.Self.Id) continue;
			if (FactionRelation.IsHostile(ctx.Self.Faction, other.Faction)) continue;
			if (AIUtil.Distance3D(ctx.Self, other) <= 2)
				return 1f;
			}
			return 0f;
		};

		_resolvers["target_on_fire"] = static ctx =>
			ctx.TargetActor != null && HasCondition(ctx.TargetActor, HealthConditionIds.OnFire) ? 1f : 0f;

		_resolvers["target_wounded"] = static ctx =>
			ctx.TargetActor != null && HealthSystem.HasTreatableCondition(ctx.TargetActor, ctx.State?.Turn ?? 0) ? 1f : 0f;
	}

	private static void RegisterPersonalityInputs()
	{
		_resolvers["personality_bravery"] = static ctx => GetPersonality(ctx.Self, "bravery");
		_resolvers["personality_altruism"] = static ctx => GetPersonality(ctx.Self, "altruism");
		_resolvers["personality_diligence"] = static ctx => GetPersonality(ctx.Self, "diligence");
		_resolvers["personality_curiosity"] = static ctx => GetPersonality(ctx.Self, "curiosity");
		_resolvers["personality_aggression"] = static ctx => GetPersonality(ctx.Self, "aggression");
		_resolvers["personality_sociability"] = static ctx => GetPersonality(ctx.Self, "sociability");
		_resolvers["personality_patience"] = static ctx => GetPersonality(ctx.Self, "patience");
		_resolvers["personality_greed"] = static ctx => GetPersonality(ctx.Self, "greed");
		_resolvers["personality_loyalty"] = static ctx => GetPersonality(ctx.Self, "loyalty");
		_resolvers["personality_caution"] = static ctx => GetPersonality(ctx.Self, "caution");
	}

	private static void RegisterEnvironmentInputs()
	{
		_resolvers["ambient_temperature_norm"] = static ctx =>
		{
			var exposure = GetExposure(ctx);
			if (!exposure.HasWeatherData) return 0.5f;
			return Clamp01((exposure.AmbientTemperature + 20f) / 60f);
		};

		_resolvers["is_indoors"] = static ctx =>
		{
			var exposure = GetExposure(ctx);
			return exposure.IsIndoors ? 1f : 0f;
		};

		_resolvers["is_overweight"] = static ctx => ctx.Self.IsOverweight ? 1f : 0f;
	}

	private static void RegisterAwarenessInputs()
	{
		_resolvers["awareness_idle"] = static ctx =>
			ctx.Self.AwarenessState == AwarenessState.Idle ? 1f : 0f;

		_resolvers["awareness_suspicious"] = static ctx =>
			ctx.Self.AwarenessState == AwarenessState.Suspicious ? 1f : 0f;

		_resolvers["awareness_alerted"] = static ctx =>
			ctx.Self.AwarenessState == AwarenessState.Alerted ? 1f : 0f;

		_resolvers["awareness_searching"] = static ctx =>
			ctx.Self.AwarenessState == AwarenessState.Searching ? 1f : 0f;

		_resolvers["awareness_not_idle"] = static ctx =>
			ctx.Self.AwarenessState != AwarenessState.Idle ? 1f : 0f;

		_resolvers["has_tracked_target"] = static ctx =>
			!string.IsNullOrWhiteSpace(ctx.Self.AlertTargetActorId) ? 1f : 0f;

		_resolvers["has_nearby_threat"] = static ctx =>
		{
			if (ctx.State == null) return 0f;
			return ThreatDetection.HasNearbyThreat(ctx.State, ctx.Self, ctx.BehaviorContext) ? 1f : 0f;
		};
	}

	private static void RegisterMiscInputs()
	{
		_resolvers["has_home"] = static ctx => ctx.Self.HasHomePosition ? 1f : 0f;

		_resolvers["distance_from_home_norm"] = static ctx =>
		{
			if (!ctx.Self.HasHomePosition || ctx.Self.HomeZ != ctx.Self.Z) return 0f;
			var dist = Math.Abs(ctx.Self.X - ctx.Self.HomeX) + Math.Abs(ctx.Self.Y - ctx.Self.HomeY);
			return Math.Min(dist / 20f, 1f);
		};

		_resolvers["consciousness"] = static ctx => ctx.Self.GetCapacity(Caps.Consciousness);
		_resolvers["manipulation"] = static ctx => ctx.Self.GetCapacity(Caps.Manipulation);
		_resolvers["moving_capacity"] = static ctx => ctx.Self.GetCapacity(Caps.Moving);

		_resolvers["constant_true"] = static _ => 1f;
		_resolvers["constant_half"] = static _ => 0.5f;

		_resolvers["follow_target_distance_norm"] = static ctx =>
		{
			if (ctx.State == null) return 0f;
			var activeId = PartyModule.GetActiveId(ctx.State);
			if (string.IsNullOrEmpty(activeId)) return 0f;
			if (!ctx.State.Actors.TryGetValue(activeId, out var leader)) return 0f;
			if (leader.Z != ctx.Self.Z) return 1f;
			var dist = Math.Abs(ctx.Self.X - leader.X) + Math.Abs(ctx.Self.Y - leader.Y);
			return Math.Min(dist / 20f, 1f);
		};

		_resolvers["is_party_member"] = static ctx =>
		{
			if (ctx.State == null) return 0f;
			return string.Equals(ctx.Self.BrainId, "party_follower", StringComparison.Ordinal) ? 1f : 0f;
		};
	}

	private static float GetPersonality(Actor actor, string trait)
	{
		if (actor.DialogPersonality.TryGetValue(trait, out var val))
			return Clamp01(val);
		return 0.5f;
	}

	private static bool HasCondition(Actor actor, string conditionId)
	{
		foreach (var cond in actor.HealthConditions)
		{
			if (string.Equals(cond.Id, conditionId, StringComparison.Ordinal))
				return true;
		}
		return false;
	}

	private static float GetConditionSeverity(Actor actor, string conditionId)
	{
		foreach (var cond in actor.HealthConditions)
		{
			if (string.Equals(cond.Id, conditionId, StringComparison.Ordinal))
				return cond.Severity;
		}
		return 0f;
	}

	private static EnvironmentExposureSnapshot GetExposure(InputContext ctx)
	{
		if (ctx.BehaviorContext != null)
			return ctx.BehaviorContext.GetCurrentExposure(ctx.Self);
		if (ctx.State != null)
			return DefaultEnvironmentExposureProvider.Instance.Capture(ctx.State, ctx.Self);
		return default;
	}

	private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

	private static readonly (int Dx, int Dy)[] Dirs = [(0, -1), (0, 1), (-1, 0), (1, 0)];
}

public sealed class InputContext
{
	public required Actor Self { get; init; }
	public required Perception Perception { get; init; }
	public GameState? State { get; init; }
	public AIBehaviorContext? BehaviorContext { get; init; }
	public Actor? TargetActor { get; set; }
	public WorkTicket? TargetTicket { get; set; }
}
