using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Health;
using MiniRPG.Core.Map;
using MiniRPG.Core.World;

namespace MiniRPG.Core.Combat;

/// <summary>
/// Shared action entry points used by both player input and AI.
/// </summary>
public static class ActionModule
{
	public static List<GameEvent> TryMove(GameState state, Actor actor, int dx, int dy)
	{
		if (dx != 0 || dy != 0)
		{
			actor.FacingX = dx;
			actor.FacingY = dy;
		}

		var events = new List<GameEvent>();
		var nx = actor.X + dx;
		var ny = actor.Y + dy;

		if (!MapModule.InBounds(state, nx, ny) || MapModule.IsWall(state, nx, ny))
		{
			events.Add(new GameEvent("hit_wall") { InitiatorId = actor.Id });
			return events;
		}

		var occupants = ActorModule.GetAllAt(state, nx, ny);
		var enemy = occupants.FirstOrDefault(
			other => other.Id != actor.Id && AI.FactionRelation.IsHostile(actor.Faction, other.Faction));

		if (enemy != null)
		{
			events.AddRange(TryAttack(state, actor, enemy));
			return events;
		}

		ActorModule.MoveActor(state, actor.Id, nx, ny);
		events.Add(new GameEvent("actor_moved")
		{
			InitiatorId = actor.Id,
			TargetX = nx,
			TargetY = ny,
		});
		return events;
	}

	public static List<GameEvent> TryAttack(
		GameState state,
		Actor attacker,
		Actor target,
		InteractionDef? actionDef = null,
		Limb? targetLimb = null)
	{
		if (actionDef == null || (actionDef.EffectType != "block" && targetLimb == null))
		{
			var fallback = PickAttack(state, attacker, target);
			if (fallback == null)
				return [];

			actionDef ??= fallback.Value.Action;
			targetLimb ??= fallback.Value.Limb;
		}

		var targetType = ResolveSkillTargetType(actionDef!);
		var result = targetType == SkillTargetType.Self
			? TryCastSkill(
				state,
				attacker,
				actionDef!.Id,
				SkillTargetType.Self,
				targetActor: null,
				targetLimb: null)
			: TryCastSkill(
				state,
				attacker,
				actionDef!.Id,
				SkillTargetType.Actor,
				targetActor: target,
				targetLimb: targetLimb);
		return result.Events;
	}

	public static List<GameEvent> TryDig(GameState state, Actor actor, int tx, int ty, int tz, InteractionDef skill) =>
		TryCastSkill(
			state,
			actor,
			skill.Id,
			SkillTargetType.Cell,
			targetActor: null,
			targetLimb: null,
			targetX: tx,
			targetY: ty,
			targetZ: tz).Events;

	public static List<Actor> GetInteractTargets(GameState state, Actor actor)
	{
		var targets = new List<Actor>();
		foreach (var (dx, dy) in GridDirections.CardinalWithOrigin)
		{
			foreach (var other in ActorModule.GetAllAt(state, actor.X + dx, actor.Y + dy))
			{
				if (other.Id != actor.Id)
					targets.Add(other);
			}
		}

		return targets;
	}

	public static List<GameEvent> TryInteract(GameState state, Actor initiator, Actor target, InteractionDef def) =>
		InteractionModule.Execute(state, initiator, target, def);

	public static SkillTargetType ResolveSkillTargetType(InteractionDef skill)
	{
		if (skill.Id is "strip_corpse" or "butcher_corpse" or "harvest_corpse")
			return SkillTargetType.Item;
		if (skill.Id == "tend_self")
			return SkillTargetType.Self;
		if (skill.Id == "tend_other")
			return SkillTargetType.Actor;
		if (skill.EffectType == "reload")
			return SkillTargetType.Self;
		if (skill.EffectType is "dig" or "light_fire" or "extinguish_fire")
			return SkillTargetType.Cell;
		if (skill.EffectType == "identify")
			return SkillTargetType.Actor;
		if (skill.EffectType == "operate")
			return SkillTargetType.Actor;
		if (skill.EffectType == "tend")
			return skill.Range == 0 ? SkillTargetType.Self : SkillTargetType.Actor;
		if (skill.EffectType == "block" || skill.Range == 0)
			return SkillTargetType.Self;
		return SkillTargetType.Actor;
	}

	public static bool CanCastSkill(
		GameState state,
		Actor caster,
		string skillId,
		SkillTargetType targetType,
		Actor? targetActor = null,
		int? targetX = null,
		int? targetY = null,
		int? targetZ = null)
	{
		return ValidateCastSkill(
			state,
			caster,
			skillId,
			targetType,
			targetActor,
			targetLimb: null,
			targetItem: null,
			targetLimbId: null,
			targetX: targetX,
			targetY: targetY,
			targetZ: targetZ,
			out _).Success;
	}

	public static ActionExecutionResult TryCastSkill(
		GameState state,
		Actor caster,
		string skillId,
		SkillTargetType targetType,
		string? targetActorId = null,
		string? targetLimbId = null,
		string? targetItemId = null,
		int? targetX = null,
		int? targetY = null,
		int? targetZ = null)
	{
		var targetActor = !string.IsNullOrEmpty(targetActorId)
			? ActorModule.GetById(state, targetActorId)
			: null;
		var targetLimb = targetActor != null && !string.IsNullOrEmpty(targetLimbId)
			? targetActor.Limbs.FirstOrDefault(limb => string.Equals(limb.Id, targetLimbId, StringComparison.Ordinal))
			: null;
		var resolvedTargetX = targetX ?? caster.X;
		var resolvedTargetY = targetY ?? caster.Y;
		var resolvedTargetZ = targetZ ?? caster.Z;
		var targetItem = !string.IsNullOrWhiteSpace(targetItemId)
			? MapModule.FindGroundItem(state, resolvedTargetX, resolvedTargetY, resolvedTargetZ, targetItemId)
			: null;
		return TryCastSkill(
			state,
			caster,
			skillId,
			targetType,
			targetActor,
			targetLimb,
			targetX,
			targetY,
			targetZ,
			targetItem,
			targetLimbId);
	}

	public static ActionExecutionResult TryCastSkill(
		GameState state,
		Actor caster,
		string skillId,
		SkillTargetType targetType,
		Actor? targetActor = null,
		Limb? targetLimb = null,
		int? targetX = null,
		int? targetY = null,
		int? targetZ = null,
		Item? targetItem = null,
		string? targetLimbId = null)
	{
		var validation = ValidateCastSkill(
			state,
			caster,
			skillId,
			targetType,
			targetActor,
			targetLimb,
			targetItem,
			targetLimbId,
			targetX,
			targetY,
			targetZ,
			out var failureResult);
		if (!validation.Success)
			return failureResult;

		UpdateFacingFromTarget(caster, validation.TargetX, validation.TargetY);

		var result = new ActionExecutionResult { Consumed = true };
		switch (validation.Skill!.EffectType)
		{
			case "block":
				var selfLimb = caster.Limbs.FirstOrDefault();
				if (selfLimb == null)
					return CreateFailure(
						caster,
						validation.Skill,
						SkillCastFailureReason.InvalidTarget,
						targetType,
						targetX: caster.X,
						targetY: caster.Y);
				result.Events.AddRange(CombatModule.Attack(state, caster, caster, validation.Skill, selfLimb));
				break;

			case "dig":
				result.Events.AddRange(DigModule.TryDig(
					state,
					caster,
					validation.TargetX,
					validation.TargetY,
					validation.TargetZ,
					validation.Skill));
				break;

			case "melee_attack":
			case "heavy_attack":
			case "poison_attack":
			case "drain_attack":
				result.Events.AddRange(CombatModule.Attack(
					state,
					caster,
					validation.TargetActor!,
					validation.Skill,
					validation.TargetLimb!));
				break;

			case "ranged_attack":
				var ammoResult = FirearmModule.TrySpendAmmoForShot(state, caster, validation.Skill);
				if (!ammoResult.Consumed)
					return ammoResult;
				result.Events.AddRange(ammoResult.Events);
				result.Events.AddRange(CombatModule.Attack(
					state,
					caster,
					validation.TargetActor!,
					validation.Skill,
					validation.TargetLimb!));
				break;

			case "reload":
				result = FirearmModule.TryReload(state, caster, validation.Skill);
				break;

			case "operate":
				result = validation.TargetItem != null
					? SurgeryModule.TryOperateOnCorpse(
						state,
						caster,
						validation.TargetItem,
						validation.Skill,
						validation.TargetLimbId,
						validation.TargetX,
						validation.TargetY,
						validation.TargetZ)
					: SurgeryModule.TryOperateOnActor(
						state,
						caster,
						validation.TargetActor!,
						validation.TargetLimbId,
						validation.Skill);
				break;

			case "tend":
				result = validation.TargetActor == null
					? HealthActionModule.TryTendSelf(state, caster)
					: HealthActionModule.TryTendOther(state, caster, validation.TargetActor);
				break;

			case "light_fire":
				result = HeatActionModule.TryLightFire(
					state,
					caster,
					validation.TargetX,
					validation.TargetY,
					validation.TargetZ);
				break;

			case "extinguish_fire":
				result = FireSystem.TryExtinguish(
					state,
					caster,
					validation.TargetX,
					validation.TargetY,
					validation.TargetZ);
				break;

			default:
				return CreateFailure(
					caster,
					validation.Skill,
					SkillCastFailureReason.Unsupported,
					targetType,
					validation.TargetActor,
					validation.TargetX,
					validation.TargetY);
		}

		if (result.Events.Count == 0)
			return CreateFailure(
				caster,
				validation.Skill,
				SkillCastFailureReason.InvalidTarget,
				targetType,
				validation.TargetActor,
				validation.TargetX,
				validation.TargetY);

		if (validation.Skill.Cooldown > 0)
			caster.StartSkillCooldown(validation.Skill.Id, validation.Skill.Cooldown + 1);

		return result;
	}

	private static (InteractionDef Action, Limb Limb)? PickAttack(GameState state, Actor attacker, Actor target)
	{
		var actions = CombatModule.GetAttackActions(attacker)
			.Where(skill =>
				skill.Range == 1 &&
				skill.Cooldown == 0 &&
				skill.EffectType is not "ranged_attack" &&
				!attacker.IsSkillOnCooldown(skill.Id))
			.ToList();
		if (actions.Count == 0 || target.Limbs.Count == 0)
			return null;

		var rng = new Random(state.RngSeed + state.Turn + attacker.Id.GetHashCode());
		var limb = CombatModule.PickPreferredTargetLimb(state, attacker, target);
		return limb != null ? (actions[rng.Next(actions.Count)], limb) : null;
	}

	private static int GetEffectiveRange(GameState state, Actor caster, InteractionDef skill)
	{
		var baseRange = Math.Max(0, skill.Range);
		if (skill.EffectType != "ranged_attack"
			|| state.World == null
			|| !state.World.IsWeatherExposed(caster.X, caster.Y, caster.Z))
		{
			return baseRange;
		}

		var weather = WeatherRules.GetLocalWeather(state, caster.X, caster.Y, caster.Z);
		return Math.Max(1, baseRange - WeatherRules.GetRangedPenalty(weather));
	}

	private static ValidatedCast ValidateCastSkill(
		GameState state,
		Actor caster,
		string skillId,
		SkillTargetType targetType,
		Actor? targetActor,
		Limb? targetLimb,
		Item? targetItem,
		string? targetLimbId,
		int? targetX,
		int? targetY,
		int? targetZ,
		out ActionExecutionResult failureResult)
	{
		failureResult = new ActionExecutionResult();

		if (string.IsNullOrWhiteSpace(skillId))
		{
			failureResult = CreateFailure(caster, null, SkillCastFailureReason.UnknownSkill, targetType, targetActor, targetX, targetY);
			return ValidatedCast.Invalid;
		}

		var skill = SkillQuery.GetAll(caster).FirstOrDefault(def => string.Equals(def.Id, skillId, StringComparison.Ordinal));
		if (skill == null)
		{
			var reason = InteractionDefs.Get(skillId) == null
				? SkillCastFailureReason.UnknownSkill
				: SkillCastFailureReason.Unavailable;
			failureResult = CreateFailure(caster, InteractionDefs.Get(skillId), reason, targetType, targetActor, targetX, targetY);
			return ValidatedCast.Invalid;
		}

		if (!SkillQuery.IsUnifiedCastSkill(skill))
		{
			failureResult = CreateFailure(caster, skill, SkillCastFailureReason.Unsupported, targetType, targetActor, targetX, targetY);
			return ValidatedCast.Invalid;
		}

		var expectedTargetType = ResolveSkillTargetType(skill);
		if (targetType != expectedTargetType)
		{
			failureResult = CreateFailure(caster, skill, SkillCastFailureReason.InvalidTargetType, targetType, targetActor, targetX, targetY);
			return ValidatedCast.Invalid;
		}

		if (caster.IsSkillOnCooldown(skill.Id))
		{
			failureResult = CreateFailure(
				caster,
				skill,
				SkillCastFailureReason.Cooldown,
				targetType,
				targetActor,
				targetX,
				targetY,
				caster.GetSkillCooldown(skill.Id));
			return ValidatedCast.Invalid;
		}

		if (targetType == SkillTargetType.Self)
		{
			if (skill.EffectType == "tend")
			{
				HealthSystem.Sync(caster, state.Turn, DefaultEnvironmentExposureProvider.Instance.Capture(state, caster));
				if (!HealthSystem.HasTreatableCondition(caster, state.Turn))
				{
					failureResult = CreateFailure(caster, skill, SkillCastFailureReason.InvalidTarget, targetType, targetX: caster.X, targetY: caster.Y);
					return ValidatedCast.Invalid;
				}
			}

			return new ValidatedCast
			{
				Success = true,
				Skill = skill,
				TargetX = caster.X,
				TargetY = caster.Y,
				TargetZ = caster.Z,
			};
		}

		if (targetType == SkillTargetType.Actor)
		{
			if (targetActor == null)
			{
				failureResult = CreateFailure(caster, skill, SkillCastFailureReason.MissingTarget, targetType, null, targetX, targetY);
				return ValidatedCast.Invalid;
			}

			if (!SkillQuery.CanUseAgainst(caster, targetActor, skill)
				|| (skill.EffectType != "operate" && targetActor.Limbs.Count == 0))
			{
				failureResult = CreateFailure(caster, skill, SkillCastFailureReason.InvalidTarget, targetType, targetActor, targetActor.X, targetActor.Y);
				return ValidatedCast.Invalid;
			}

			if (!IsWithinRange(caster.X, caster.Y, targetActor.X, targetActor.Y, GetEffectiveRange(state, caster, skill)))
			{
				failureResult = CreateFailure(caster, skill, SkillCastFailureReason.OutOfRange, targetType, targetActor, targetActor.X, targetActor.Y);
				return ValidatedCast.Invalid;
			}

			if (RequiresLineOfSight(skill)
				&& (state.World == null
					|| !VisibilityUtil.HasLineOfSight(state.World, caster.X, caster.Y, targetActor.X, targetActor.Y, caster.Z)))
			{
				failureResult = CreateFailure(caster, skill, SkillCastFailureReason.NoLineOfSight, targetType, targetActor, targetActor.X, targetActor.Y);
				return ValidatedCast.Invalid;
			}

			if (skill.EffectType == "tend")
			{
				HealthSystem.Sync(targetActor, state.Turn, DefaultEnvironmentExposureProvider.Instance.Capture(state, targetActor));
				if (!HealthSystem.HasTreatableCondition(targetActor, state.Turn))
				{
					failureResult = CreateFailure(caster, skill, SkillCastFailureReason.InvalidTarget, targetType, targetActor, targetActor.X, targetActor.Y);
					return ValidatedCast.Invalid;
				}

				return new ValidatedCast
				{
					Success = true,
					Skill = skill,
					TargetActor = targetActor,
					TargetX = targetActor.X,
					TargetY = targetActor.Y,
					TargetZ = targetActor.Z,
				};
			}

			if (skill.EffectType == "operate")
			{
				var resolvedOperateLimbId = !string.IsNullOrWhiteSpace(targetLimbId)
					? targetLimbId
					: targetLimb?.Id ?? targetActor.Limbs.FirstOrDefault()?.Id;
				if (string.IsNullOrWhiteSpace(resolvedOperateLimbId))
				{
					failureResult = CreateFailure(caster, skill, SkillCastFailureReason.InvalidTarget, targetType, targetActor, targetActor.X, targetActor.Y);
					return ValidatedCast.Invalid;
				}

				return new ValidatedCast
				{
					Success = true,
					Skill = skill,
					TargetActor = targetActor,
					TargetLimb = targetLimb,
					TargetLimbId = resolvedOperateLimbId,
					TargetX = targetActor.X,
					TargetY = targetActor.Y,
					TargetZ = targetActor.Z,
				};
			}

			var resolvedLimb = targetLimb;
			if (resolvedLimb == null)
				resolvedLimb = CombatModule.PickPreferredTargetLimb(state, caster, targetActor);
			if (resolvedLimb == null)
			{
				failureResult = CreateFailure(caster, skill, SkillCastFailureReason.InvalidTarget, targetType, targetActor, targetActor.X, targetActor.Y);
				return ValidatedCast.Invalid;
			}

			return new ValidatedCast
			{
				Success = true,
				Skill = skill,
				TargetActor = targetActor,
				TargetLimb = resolvedLimb,
				TargetLimbId = resolvedLimb.Id,
				TargetX = targetActor.X,
				TargetY = targetActor.Y,
				TargetZ = targetActor.Z,
			};
		}

		if (targetType == SkillTargetType.Item)
		{
			if (targetItem == null)
			{
				failureResult = CreateFailure(caster, skill, SkillCastFailureReason.MissingTarget, targetType, null, targetX, targetY);
				return ValidatedCast.Invalid;
			}

			var resolvedTargetX = targetX ?? caster.X;
			var resolvedTargetY = targetY ?? caster.Y;
			var resolvedTargetZ = targetZ ?? caster.Z;
			if (!IsWithinRange(caster.X, caster.Y, resolvedTargetX, resolvedTargetY, GetEffectiveRange(state, caster, skill)))
			{
				failureResult = CreateFailure(caster, skill, SkillCastFailureReason.OutOfRange, targetType, null, resolvedTargetX, resolvedTargetY);
				return ValidatedCast.Invalid;
			}

			if (skill.EffectType != "operate")
			{
				failureResult = CreateFailure(caster, skill, SkillCastFailureReason.Unsupported, targetType, null, resolvedTargetX, resolvedTargetY);
				return ValidatedCast.Invalid;
			}

			return new ValidatedCast
			{
				Success = true,
				Skill = skill,
				TargetItem = targetItem,
				TargetLimbId = targetLimbId,
				TargetX = resolvedTargetX,
				TargetY = resolvedTargetY,
				TargetZ = resolvedTargetZ,
			};
		}

		if (targetX == null || targetY == null || targetZ == null || state.World == null)
		{
			failureResult = CreateFailure(caster, skill, SkillCastFailureReason.MissingTarget, targetType, null, targetX, targetY);
			return ValidatedCast.Invalid;
		}

		if (skill.EffectType == "extinguish_fire")
		{
			if (!FireSystem.CanExtinguishAt(state, caster, targetX.Value, targetY.Value, targetZ.Value, out var extinguishFailureReason))
			{
				failureResult = CreateFailure(caster, skill, extinguishFailureReason, targetType, null, targetX, targetY);
				return ValidatedCast.Invalid;
			}

			return new ValidatedCast
			{
				Success = true,
				Skill = skill,
				TargetX = targetX.Value,
				TargetY = targetY.Value,
				TargetZ = targetZ.Value,
			};
		}

		if (!IsWithinRange(caster.X, caster.Y, targetX.Value, targetY.Value, GetEffectiveRange(state, caster, skill)))
		{
			failureResult = CreateFailure(caster, skill, SkillCastFailureReason.OutOfRange, targetType, null, targetX, targetY);
			return ValidatedCast.Invalid;
		}

		if (RequiresLineOfSight(skill)
			&& !VisibilityUtil.HasLineOfSight(state.World, caster.X, caster.Y, targetX.Value, targetY.Value, caster.Z))
		{
			failureResult = CreateFailure(caster, skill, SkillCastFailureReason.NoLineOfSight, targetType, null, targetX, targetY);
			return ValidatedCast.Invalid;
		}

		if (skill.EffectType == "light_fire")
		{
			if (!HeatActionModule.CanLightFireAt(state, caster, targetX.Value, targetY.Value, targetZ.Value, out var fireFailureReason))
			{
				failureResult = CreateFailure(caster, skill, fireFailureReason, targetType, null, targetX, targetY);
				return ValidatedCast.Invalid;
			}

			return new ValidatedCast
			{
				Success = true,
				Skill = skill,
				TargetX = targetX.Value,
				TargetY = targetY.Value,
				TargetZ = targetZ.Value,
			};
		}

		var terrain = state.World.GetTerrain(targetX.Value, targetY.Value, targetZ.Value);
		var hardness = state.World.GetHardness(targetX.Value, targetY.Value, targetZ.Value);
		if (!terrain.Solid || hardness == 0)
		{
			failureResult = CreateFailure(caster, skill, SkillCastFailureReason.InvalidTerrain, targetType, null, targetX, targetY);
			return ValidatedCast.Invalid;
		}

		if (skill.TerrainMaterial.Length > 0 && terrain.Material != skill.TerrainMaterial)
		{
			failureResult = CreateFailure(caster, skill, SkillCastFailureReason.InvalidTerrainMaterial, targetType, null, targetX, targetY);
			return ValidatedCast.Invalid;
		}

		return new ValidatedCast
		{
			Success = true,
			Skill = skill,
			TargetX = targetX.Value,
			TargetY = targetY.Value,
			TargetZ = targetZ.Value,
		};
	}

	private static bool RequiresLineOfSight(InteractionDef skill) => skill.Range > 1;

	private static void UpdateFacingFromTarget(Actor actor, int targetX, int targetY)
	{
		var dx = Math.Sign(targetX - actor.X);
		var dy = Math.Sign(targetY - actor.Y);
		if (dx == 0 && dy == 0)
			return;

		actor.FacingX = dx;
		actor.FacingY = dy;
	}

	private static bool IsWithinRange(int sourceX, int sourceY, int targetX, int targetY, int range)
	{
		var dx = Math.Abs(targetX - sourceX);
		var dy = Math.Abs(targetY - sourceY);
		return range switch
		{
			<= 0 => dx == 0 && dy == 0,
			1 => dx + dy == 1,
			_ => Math.Max(dx, dy) <= range,
		};
	}

	private static ActionExecutionResult CreateFailure(
		Actor caster,
		InteractionDef? skill,
		SkillCastFailureReason reason,
		SkillTargetType targetType,
		Actor? targetActor = null,
		int? targetX = null,
		int? targetY = null,
		int cooldownRemaining = 0)
	{
		var result = new ActionExecutionResult();
		var failureEvent = new GameEvent("skill_cast_failed")
		{
			TargetX = targetX ?? targetActor?.X ?? caster.X,
			TargetY = targetY ?? targetActor?.Y ?? caster.Y,
			ActionName = skill?.Name,
			EffectType = skill?.EffectType,
			SkillId = skill?.Id,
			FailureReason = ToFailureReasonCode(reason),
			CooldownRemaining = cooldownRemaining,
			ItemName = targetType.ToString(),
		};
		IdentificationModule.PopulateInitiatorIdentity(failureEvent, state: null, caster);
		if (targetActor != null)
			IdentificationModule.PopulateTargetIdentity(failureEvent, state: null, targetActor);
		result.Events.Add(failureEvent);
		return result;
	}

	private static string ToFailureReasonCode(SkillCastFailureReason reason) => reason switch
	{
		SkillCastFailureReason.UnknownSkill => "unknown_skill",
		SkillCastFailureReason.Unavailable => "unavailable",
		SkillCastFailureReason.Unsupported => "unsupported",
		SkillCastFailureReason.Cooldown => "cooldown",
		SkillCastFailureReason.InvalidTargetType => "invalid_target_type",
		SkillCastFailureReason.MissingTarget => "missing_target",
		SkillCastFailureReason.InvalidTarget => "invalid_target",
		SkillCastFailureReason.OutOfRange => "out_of_range",
		SkillCastFailureReason.NoLineOfSight => "no_line_of_sight",
		SkillCastFailureReason.InvalidTerrain => "invalid_terrain",
		SkillCastFailureReason.InvalidTerrainMaterial => "invalid_terrain_material",
		_ => "unknown",
	};

	private sealed class ValidatedCast
	{
		public static readonly ValidatedCast Invalid = new();

		public bool Success { get; init; }
		public InteractionDef? Skill { get; init; }
		public Actor? TargetActor { get; init; }
		public Limb? TargetLimb { get; init; }
		public Item? TargetItem { get; init; }
		public string? TargetLimbId { get; init; }
		public int TargetX { get; init; }
		public int TargetY { get; init; }
		public int TargetZ { get; init; }
	}
}
