using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Combat;

public static class CombatModule
{
	public const string VitalTag = "vital";
	private const string LegacyVitalTag = "\u8981\u5bb3";
	private const string BlockingTag = "blocking";
	private const string PoisonTag = "poison";

	public static List<GameEvent> Attack(
		GameState state,
		Actor attacker,
		Actor target,
		InteractionDef action,
		Limb targetLimb)
	{
		var events = new List<GameEvent>();

		if (action.EffectType == "block")
		{
			attacker.AddBuff(new Buff
			{
				Id = $"block_{state.Turn}",
				Name = "Block",
				RemainingTurns = 1,
				Tags = new() { [BlockingTag] = 1 },
			});

			var blockEvent = new GameEvent("combat_block")
			{
				ActionName = action.Name,
				EffectType = "block",
				SourceX = attacker.X,
				SourceY = attacker.Y,
			};
			IdentificationModule.PopulateInitiatorIdentity(blockEvent, state, attacker);
			IdentificationModule.PopulateTargetIdentity(blockEvent, state, attacker);
			events.Add(blockEvent);
			return events;
		}

		var damage = CalcDamage(attacker, action, target, targetLimb);
		var damageType = ResolveDamageType(attacker, action);
		targetLimb.Durability = Math.Max(0, targetLimb.Durability - damage);
		target.InvalidateCapacityCache();
		ApplyHealthInjury(state, attacker, target, action, targetLimb, damage, damageType, events);

		var attackEvent = new GameEvent("combat_attack")
		{
			ActionName = action.Name,
			LimbName = targetLimb.Name,
			Damage = damage,
			EffectType = action.EffectType,
			DamageType = damageType,
			SourceX = attacker.X,
			SourceY = attacker.Y,
			TargetX = target.X,
			TargetY = target.Y,
		};
		IdentificationModule.PopulateInitiatorIdentity(attackEvent, state, attacker);
		IdentificationModule.PopulateTargetIdentity(attackEvent, state, target);
		events.Add(attackEvent);

		NeedSystem.ApplyThought(target, "hurt_recently", state.Turn, NeedThoughtSources.Combat, events, state);

		if (action.EffectType == "drain_attack")
		{
			var selfLimbs = attacker.Limbs;
			if (selfLimbs.Count > 0)
			{
				var rng = new Random(state.RngSeed + state.Turn + attacker.Id.GetHashCode());
				var heal = selfLimbs[rng.Next(selfLimbs.Count)];
				heal.Durability = Math.Min(heal.MaxDurability, heal.Durability + damage);
				attacker.InvalidateCapacityCache();
			}
		}

		AppendDamageOutcome(state, target, targetLimb, events);
		return events;
	}

	public static List<GameEvent> ApplyEnvironmentalDamage(
		GameState state,
		Actor target,
		Limb targetLimb,
		int damage,
		string damageType)
	{
		if (damage <= 0)
			return [];

		targetLimb.Durability = Math.Max(0, targetLimb.Durability - damage);
		target.InvalidateCapacityCache();
		var events = new List<GameEvent>();
		NeedSystem.ApplyThought(target, "hurt_recently", state.Turn, NeedThoughtSources.Combat, events, state);
		ApplyHealthInjury(
			state,
			target,
			target,
			new InteractionDef
			{
				Id = "environment_damage",
				Name = "Environment",
				DamageType = damageType,
				EffectType = "melee_attack",
			},
			targetLimb,
			damage,
			damageType,
			events);
		AppendDamageOutcome(state, target, targetLimb, events);
		return events;
	}

	public static int CalcDamage(Actor attacker, InteractionDef action, Actor target, Limb? targetLimb = null)
	{
		if (target.Buffs.Exists(buff => buff.Id == "debug_godmode"))
			return 0;

		var capacities = attacker.ComputeCapacities();
		var damageType = ResolveDamageType(attacker, action);

		float baseDamage;
		if (damageType == DamageTypes.Poison)
		{
			var poisonPower = attacker.ComputeTags().GetValueOrDefault(PoisonTag, 0);
			baseDamage = poisonPower + action.Power * 2;
		}
		else
		{
			var manipulation = capacities.GetValueOrDefault(Caps.Manipulation, 0.5f);
			baseDamage = action.Power * 2 * (0.5f + manipulation);

			var weapon = attacker.Inventory.FirstOrDefault(item => item.Equipped && item.Category == ItemCategories.Weapon);
			if (weapon != null)
				baseDamage += damageType == DamageTypes.Sharp ? weapon.SharpDamage : weapon.BluntDamage;
		}

		float armor = 0f;
		if (damageType != DamageTypes.Poison && targetLimb != null)
		{
			armor = damageType == DamageTypes.Sharp
				? target.GetSharpArmorFor(targetLimb.BodyPart)
				: target.GetBluntArmorFor(targetLimb.BodyPart);
		}

		if (damageType != DamageTypes.Poison && target.ComputeTags().GetValueOrDefault(BlockingTag, 0) > 0)
			armor += 5f;

		return Math.Max(1, (int)(baseDamage - armor));
	}

	private static string ResolveDamageType(Actor attacker, InteractionDef action)
	{
		if (!string.IsNullOrEmpty(action.DamageType))
			return action.DamageType;

		var weapon = attacker.Inventory.FirstOrDefault(item => item.Equipped && item.Category == ItemCategories.Weapon);
		if (weapon != null && weapon.SharpDamage > weapon.BluntDamage)
			return DamageTypes.Sharp;

		return DamageTypes.Blunt;
	}

	public static string? CheckVitalStatus(Actor actor)
	{
		if (actor.HasCachedVitalStatus)
			return actor.CachedVitalStatus;

		var capacities = actor.ComputeCapacities();
		string? worst = null;
		foreach (var def in PresetDB.Capacities.Values)
		{
			if (def.VitalEffect == null)
				continue;

			var value = capacities.GetValueOrDefault(def.Id);
			if (value <= def.ZeroThreshold)
			{
				if (def.VitalEffect == "death_instant")
				{
					actor.SetCachedVitalStatus("death_instant");
					return "death_instant";
				}
				if (worst == null || def.VitalEffect == "incapacitate" && worst == "death_slow")
					worst = def.VitalEffect;
			}
		}

		actor.SetCachedVitalStatus(worst);
		return worst;
	}

	public static bool IsDead(Actor actor)
	{
		var status = CheckVitalStatus(actor);
		return status == "death_instant" || !actor.Limbs.Any(IsVitalLimb);
	}

	public static bool IsVitalLimb(Limb limb) =>
		limb.Tags.ContainsKey(VitalTag) || limb.Tags.ContainsKey(LegacyVitalTag);

	public static Limb? PickPreferredTargetLimb(GameState state, Actor attacker, Actor target)
	{
		if (target.Limbs.Count == 0)
			return null;

		var vital = target.Limbs.FirstOrDefault(IsVitalLimb);
		if (vital != null)
			return vital;

		var torso = target.Limbs.FirstOrDefault(limb => limb.BodyPart == BodyParts.Torso);
		if (torso != null)
			return torso;

		var candidates = target.Limbs.Where(limb => !IsVitalLimb(limb)).ToList();
		if (candidates.Count == 0)
			candidates = [.. target.Limbs];

		var rng = new Random(state.RngSeed + state.Turn + attacker.Id.GetHashCode() + target.Id.GetHashCode());
		return candidates[rng.Next(candidates.Count)];
	}

	public static List<InteractionDef> GetAttackActions(Actor actor) =>
		SkillQuery.GetAttackSkills(actor);

	public static (InteractionDef Action, Limb TargetLimb)? MonsterChooseAction(
		GameState state,
		Actor monster,
		Actor player)
	{
		var actions = GetAttackActions(monster);
		if (actions.Count == 0 || player.Limbs.Count == 0)
			return null;

		var rng = new Random(state.RngSeed + state.Turn + monster.Id.GetHashCode());
		var action = actions[rng.Next(actions.Count)];
		var limb = PickPreferredTargetLimb(state, monster, player);
		return limb == null ? null : (action, limb);
	}

	private static void AppendDamageOutcome(GameState state, Actor target, Limb targetLimb, List<GameEvent> events)
	{
		if (targetLimb.Durability > 0)
			return;

		HealthSystem.AddOrUpdateInjury(
			target,
			HealthConditionIds.MissingLimb,
			targetLimb.Id,
			targetLimb.MaxDurability,
			"health:missing_limb",
			state.Turn,
			events,
			state);
		var droppedItems = InventoryModule.OnLimbDestroyed(target, targetLimb);
		var limbDestroyedEvent = new GameEvent("limb_destroyed")
		{
			LimbName = targetLimb.Name,
		};
		IdentificationModule.PopulateTargetIdentity(limbDestroyedEvent, state, target);
		events.Add(limbDestroyedEvent);
		NeedSystem.ApplyThought(target, "limb_destroyed", state.Turn, NeedThoughtSources.Combat, events, state);

		foreach (var dropped in droppedItems)
		{
			MapModule.PlaceItem(state, target.X, target.Y, target.Z, dropped);
			var droppedEvent = new GameEvent("item_dropped")
			{
				TargetX = target.X,
				TargetY = target.Y,
				TargetZ = target.Z,
			};
			IdentificationModule.PopulateTargetIdentity(droppedEvent, state, target);
			IdentificationModule.PopulateItemIdentity(droppedEvent, state, dropped);
			events.Add(droppedEvent);
		}

		if (SurgeryModule.CreateSeveredLimbItem(targetLimb.Id) is { } severedLimb)
		{
			MapModule.PlaceItem(state, target.X, target.Y, target.Z, severedLimb);
			var severedEvent = new GameEvent("item_dropped")
			{
				TargetX = target.X,
				TargetY = target.Y,
				TargetZ = target.Z,
			};
			IdentificationModule.PopulateTargetIdentity(severedEvent, state, target);
			IdentificationModule.PopulateItemIdentity(severedEvent, state, severedLimb);
			events.Add(severedEvent);
		}

		target.DetachLimb(targetLimb);
		HealthSystem.Sync(target, state.Turn, DefaultEnvironmentExposureProvider.Instance.Capture(state, target), events, state);
		var vitalStatus = CheckVitalStatus(target);
		if (vitalStatus == "death_instant" || IsDead(target))
		{
			var goldDrop = Math.Max(target.Gold, 5);
			var killedEvent = new GameEvent("actor_killed")
			{
				TargetX = target.X,
				TargetY = target.Y,
				TargetZ = target.Z,
				Damage = goldDrop,
			};
			IdentificationModule.PopulateTargetIdentity(killedEvent, state, target);
			SurgeryModule.TrySpawnCorpseOnDeath(state, target, events, killedEvent.Type);
			ActorModule.Remove(state, target.Id);
			events.Add(killedEvent);
		}
		else if (vitalStatus == "incapacitate")
		{
			var incapacitatedEvent = new GameEvent("actor_incapacitated")
			{
				TargetX = target.X,
				TargetY = target.Y,
			};
			IdentificationModule.PopulateTargetIdentity(incapacitatedEvent, state, target);
			events.Add(incapacitatedEvent);
		}
	}

	private static void ApplyHealthInjury(
		GameState state,
		Actor attacker,
		Actor target,
		InteractionDef action,
		Limb attackLimb,
		int damage,
		string damageType,
		List<GameEvent> events)
	{
		var conditionId = damageType switch
		{
			DamageTypes.Sharp => HealthConditionIds.CutWound,
			DamageTypes.Poison => HealthConditionIds.ToxicWound,
			DamageTypes.Fire => HealthConditionIds.BurnWound,
			_ => HealthConditionIds.BluntTrauma,
		};
		HealthSystem.AddOrUpdateInjury(
			target,
			conditionId,
			attackLimb.Id,
			damage,
			$"{attacker.Id}:{action.Id}",
			state.Turn,
			events,
			state);
		TryPlaceBloodFilth(state, target, conditionId, damage, events);
		HealthSystem.Sync(target, state.Turn, DefaultEnvironmentExposureProvider.Instance.Capture(state, target), events, state);
	}

	private static void TryPlaceBloodFilth(GameState state, Actor target, string conditionId, int damage, List<GameEvent> events)
	{
		if (state.World == null || damage <= 0)
			return;

		var profile = HealthCatalog.GetProfileForActor(target);
		var condition = HealthCatalog.GetCondition(conditionId);
		if (!profile.AllowBleeding || condition == null || condition.BleedPerSeverity <= 0f)
			return;
		if (state.World.GetEntitiesByType(target.X, target.Y, target.Z, CellEntityType.Hazard)
			.Any(entity => string.Equals(entity.EntityId, Entities.BloodFilth, StringComparison.Ordinal)))
		{
			return;
		}

		state.World.PushEntity(target.X, target.Y, target.Z, new CellEntity
		{
			Type = CellEntityType.Hazard,
			Glyph = "*",
			EntityId = Entities.BloodFilth,
		});

		var filthEvent = new GameEvent("filth_created")
		{
			ActionName = Entities.BloodFilth,
			TargetX = target.X,
			TargetY = target.Y,
		};
		IdentificationModule.PopulateTargetIdentity(filthEvent, state, target);
		events.Add(filthEvent);
	}
}
