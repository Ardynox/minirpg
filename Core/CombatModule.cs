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
			var isVital = targetLimb.Tags.ContainsKey("要害");
			events.Add(new GameEvent("limb_destroyed")
			{
				TargetId = target.Id,
				TargetActorName = target.DisplayName,
				LimbName = targetLimb.Name,
			});
			target.DetachLimb(targetLimb);

			if (isVital || IsDead(target))
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
		}

		return events;
	}

	/// <summary>计算伤害值。</summary>
	public static int CalcDamage(Actor attacker, ActionDef action, Actor target)
	{
		var aTags = attacker.ComputeTags();
		var tTags = target.ComputeTags();

		int baseDmg;
		if (action.EffectType == "poison_attack")
			baseDmg = aTags.GetValueOrDefault("毒性", 0) + action.Power * 2;
		else
			baseDmg = aTags.GetValueOrDefault("力量", 0) + action.Power * 2;

		var defense = action.EffectType == "poison_attack"
			? 0
			: tTags.GetValueOrDefault("防御", 0);

		return Math.Max(1, baseDmg - defense);
	}

	/// <summary>没有任何含"要害"tag 的肢体 -> 死亡。</summary>
	public static bool IsDead(Actor actor) =>
		!actor.Limbs.Any(l => l.Tags.ContainsKey("要害"));

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
