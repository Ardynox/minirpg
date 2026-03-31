using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniRPG.Core;

/// <summary>
/// 通用行动执行模块：任何 Actor（玩家 / AI / 自动演化）都走这里。
/// 输入：Actor + 意图，输出：GameEvent 列表。
/// 纯函数，不引用 UI。
/// </summary>
public static class ActionModule
{
	private static readonly (int Dx, int Dy)[] CardinalDirs = [(0, -1), (0, 1), (-1, 0), (1, 0)];

	/// <summary>
	/// 尝试移动 Actor 到 (nx, ny)。
	/// 撞墙 → hit_wall；撞到敌对 Actor → combat_bump 或直接攻击（取决于 BumpAttack 设置）；
	/// 否则 → actor_moved。
	/// </summary>
	public static List<GameEvent> TryMove(GameState state, Actor actor, int dx, int dy)
	{
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
			o => o.Id != actor.Id && AI.FactionRelation.IsHostile(actor.Faction, o.Faction));

		if (enemy != null)
		{
			if (state.BumpAttack)
			{
				events.AddRange(AutoBumpAttack(state, actor, enemy));
			}
			else
			{
				events.Add(new GameEvent("combat_bump")
				{
					TargetX = nx, TargetY = ny,
					TargetActorName = enemy.DisplayName,
					InitiatorId = actor.Id,
					TargetId = enemy.Id,
				});
			}
			return events;
		}

		var blocker = occupants.FirstOrDefault(o => o.Id != actor.Id);
		if (blocker != null)
		{
			events.Add(new GameEvent("hit_wall") { InitiatorId = actor.Id });
			return events;
		}

		ActorModule.MoveActor(state, actor.Id, nx, ny);
		events.Add(new GameEvent("actor_moved") { InitiatorId = actor.Id, TargetX = nx, TargetY = ny });
		return events;
	}

	/// <summary>
	/// 对目标执行攻击。选择动作 + 目标肢体，调用 CombatModule.Attack。
	/// actionDef / targetLimb 为 null 时自动随机选择。
	/// </summary>
	public static List<GameEvent> TryAttack(GameState state, Actor attacker, Actor target,
		ActionDef? actionDef = null, Limb? targetLimb = null)
	{
		if (actionDef == null || targetLimb == null)
		{
			var fallback = PickAttack(state, attacker, target);
			if (fallback == null) return [];
			actionDef ??= fallback.Value.Action;
			targetLimb ??= fallback.Value.Limb;
		}

		return CombatModule.Attack(state, attacker, target, actionDef, targetLimb);
	}

	/// <summary>
	/// 扫描指定 Actor 相邻 + 脚下的所有可交互目标（排除自身）。
	/// </summary>
	public static List<Actor> GetInteractTargets(GameState state, Actor actor)
	{
		var targets = new List<Actor>();
		var dirs = new (int Dx, int Dy)[] { (0, 0), (0, -1), (0, 1), (-1, 0), (1, 0) };
		foreach (var (dx, dy) in dirs)
		{
			foreach (var other in ActorModule.GetAllAt(state, actor.X + dx, actor.Y + dy))
			{
				if (other.Id != actor.Id)
					targets.Add(other);
			}
		}
		return targets;
	}

	/// <summary>
	/// 执行交互。委托给 InteractionModule.Execute。
	/// </summary>
	public static List<GameEvent> TryInteract(GameState state, Actor initiator, Actor target,
		InteractionDef def)
	{
		return InteractionModule.Execute(state, initiator, target, def);
	}

	// ── 内部工具 ──────────────────────────────────────────

	/// <summary>BumpAttack 模式下的自动攻击：随机选动作 + 随机选肢体。</summary>
	private static List<GameEvent> AutoBumpAttack(GameState state, Actor attacker, Actor target)
	{
		var pick = PickAttack(state, attacker, target);
		if (pick == null) return [];
		return CombatModule.Attack(state, attacker, target, pick.Value.Action, pick.Value.Limb);
	}

	private static (ActionDef Action, Limb Limb)? PickAttack(
		GameState state, Actor attacker, Actor target)
	{
		var actions = CombatModule.GetAttackActions(attacker);
		if (actions.Count == 0 || target.Limbs.Count == 0) return null;

		var rng = new Random(state.RngSeed + state.Turn + attacker.Id.GetHashCode());
		return (actions[rng.Next(actions.Count)], target.Limbs[rng.Next(target.Limbs.Count)]);
	}
}
