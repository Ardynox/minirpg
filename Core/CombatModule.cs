using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniRPG.Core;

/// <summary>
/// 战斗模块：纯函数，基于肢体耐久的伤害系统。
/// </summary>
public static class CombatModule
{
	/// <summary>
	/// 对目标的指定肢体发动攻击，返回产出的事件列表。
	/// </summary>
	public static List<GameEvent> Attack(GameState state,
		Actor attacker, Actor target, ActionDef action, Limb targetLimb)
	{
		var events = new List<GameEvent>();

		if (action.EffectType == "block")
		{
			attacker.AddBuff(new Buff
			{
				Id = $"block_{state.Turn}",
				Name = "格挡",
				RemainingTurns = 1,
				Tags = new() { ["防御"] = 5 },
			});
			events.Add(new GameEvent("combat_block")
			{
				InitiatorId = attacker.Id,
				TargetActorName = attacker.DisplayName,
				ActionName = action.Name,
			});
			return events;
		}

		var damage = CalcDamage(attacker, action, target);
		targetLimb.Durability = Math.Max(0, targetLimb.Durability - damage);

		events.Add(new GameEvent("combat_attack")
		{
			InitiatorId = attacker.Id,
			TargetId = target.Id,
			TargetActorName = target.DisplayName,
			ActionName = action.Name,
			LimbName = targetLimb.Name,
			Damage = damage,
			TargetX = target.X,
			TargetY = target.Y,
		});

		if (action.EffectType == "drain_attack")
		{
			var selfLimbs = attacker.Limbs;
			if (selfLimbs.Count > 0)
			{
				var rng = new Random(state.RngSeed + state.Turn + attacker.Id.GetHashCode());
				var heal = selfLimbs[rng.Next(selfLimbs.Count)];
				heal.Durability = Math.Min(heal.MaxDurability, heal.Durability + damage);
			}
		}

		if (targetLimb.Durability <= 0)
		{
			events.Add(new GameEvent("limb_destroyed")
			{
				TargetId = target.Id,
				TargetActorName = target.DisplayName,
				LimbName = targetLimb.Name,
			});
			target.DetachLimb(targetLimb);

			var vitalStatus = CheckVitalStatus(target);
			if (vitalStatus == "death_instant" || IsDead(target))
			{
				var goldDrop = Math.Max(target.Gold, 5);
				ActorModule.Remove(state, target.Id);
				events.Add(new GameEvent("actor_killed")
				{
					TargetId = target.Id,
					TargetActorName = target.DisplayName,
					TargetX = target.X,
					TargetY = target.Y,
					Damage = goldDrop,
				});
			}
			else if (vitalStatus == "incapacitate")
			{
				events.Add(new GameEvent("actor_incapacitated")
				{
					TargetId = target.Id,
					TargetActorName = target.DisplayName,
					TargetX = target.X,
					TargetY = target.Y,
				});
			}
		}

		return events;
	}

	/// <summary>
	/// 计算伤害值。基于操作能力(manipulation)缩放基础伤害。
	/// </summary>
	public static int CalcDamage(Actor attacker, ActionDef action, Actor target)
	{
		var aCaps = attacker.ComputeCapacities();
		var tTags = target.ComputeTags();

		var manipFactor = aCaps.GetValueOrDefault("manipulation", 0.5f);
		int baseDmg;
		if (action.EffectType == "poison_attack")
		{
			var poisonTag = attacker.ComputeTags().GetValueOrDefault("毒性", 0);
			baseDmg = poisonTag + action.Power * 2;
		}
		else
		{
			baseDmg = (int)(action.Power * 2 * (0.5f + manipFactor));
		}

		var defense = action.EffectType == "poison_attack"
			? 0
			: tTags.GetValueOrDefault("防御", 0);

		return Math.Max(1, baseDmg - defense);
	}

	/// <summary>
	/// 检查 Actor 的致命能力状态。
	/// 返回最严重的 vitalEffect，或 null 表示存活。
	/// 优先级: death_instant > incapacitate > death_slow
	/// </summary>
	public static string? CheckVitalStatus(Actor actor)
	{
		var caps = actor.ComputeCapacities();
		string? worst = null;
		foreach (var def in PresetDB.Capacities.Values)
		{
			if (def.VitalEffect == null) continue;
			var val = caps.GetValueOrDefault(def.Id);
			if (val <= def.ZeroThreshold)
			{
				if (def.VitalEffect == "death_instant")
					return "death_instant";
				if (worst == null || def.VitalEffect == "incapacitate" && worst == "death_slow")
					worst = def.VitalEffect;
			}
		}
		return worst;
	}

	/// <summary>兼容旧接口：检查是否死亡（death_instant 或无要害肢体）。</summary>
	public static bool IsDead(Actor actor)
	{
		var status = CheckVitalStatus(actor);
		return status == "death_instant" || !actor.Limbs.Any(l => l.Tags.ContainsKey("要害"));
	}

	/// <summary>
	/// 获取 Actor 可用的攻击动作（排除 move/look/block 等非攻击类型）。
	/// </summary>
	public static List<ActionDef> GetAttackActions(Actor actor)
	{
		return ActionQuery.GetAvailable(actor, ActionDefs.All)
			.Where(a => a.EffectType is "melee_attack" or "poison_attack" or "drain_attack")
			.ToList();
	}

	/// <summary>
	/// 简单怪物 AI：随机选可用攻击动作 + 随机选玩家肢体。
	/// </summary>
	public static (ActionDef Action, Limb TargetLimb)? MonsterChooseAction(
		GameState state, Actor monster, Actor player)
	{
		var actions = GetAttackActions(monster);
		if (actions.Count == 0 || player.Limbs.Count == 0) return null;

		var rng = new Random(state.RngSeed + state.Turn + monster.Id.GetHashCode());
		var action = actions[rng.Next(actions.Count)];
		var limb = player.Limbs[rng.Next(player.Limbs.Count)];
		return (action, limb);
	}
}
