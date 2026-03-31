using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniRPG.Core;

/// <summary>
/// 通用行动执行模块：玩家输入和 AI 决策共用同一套行动入口。
/// 职责：移动、攻击、交互。输入 = Actor + 意图，输出 = GameEvent 列表。
/// 纯函数，不引用 Godot / UI。
///
/// 调用方：
///   Main.DoMove()        → TryMove()   （玩家）
///   AIDispatcher          → TryMove() / TryAttack()   （AI）
///   CombatUIModule        → TryAttack()（玩家手动战斗）
///   Main.DoInteract()     → TryInteract()
/// </summary>
public static class ActionModule
{
	// REVIEW: CardinalDirs 声明了但从未使用。GetInteractTargets 里重复定义了局部数组。
	//         应统一使用此常量或删除。
	private static readonly (int Dx, int Dy)[] CardinalDirs = [(0, -1), (0, 1), (-1, 0), (1, 0)];

	/// <summary>
	/// 尝试移动 Actor 到 (actor.X+dx, actor.Y+dy)。
	/// 返回事件列表：hit_wall / combat_bump / actor_moved。
	/// BumpAttack 开启时，撞到敌人直接发起自动攻击而非产出 combat_bump 事件。
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

		// REVIEW: 只取第一个敌对目标，同一格有多个敌人时忽略了后续的。
		//         在当前设计中一格多 Actor 合法，但 BumpAttack 只打一个。
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

		ActorModule.MoveActor(state, actor.Id, nx, ny);
		events.Add(new GameEvent("actor_moved") { InitiatorId = actor.Id, TargetX = nx, TargetY = ny });
		return events;
	}

	/// <summary>
	/// 对目标执行攻击。actionDef / targetLimb 为 null 时自动随机选取。
	/// 实际伤害计算委托给 CombatModule.Attack。
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
	/// 扫描 actor 脚下 + 四方向相邻格子中的所有非自身 Actor，作为可交互目标。
	/// </summary>
	// REVIEW: 局部定义了与类级 CardinalDirs 重复的方向数组（多了 (0,0) 脚下）。
	//         建议抽取为包含 (0,0) 的共享常量 SurroundDirs。
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
	/// 执行交互，纯粹委托给 InteractionModule.Execute。
	/// </summary>
	// REVIEW: 该方法只是一行转发，存在价值有限。
	//         如果 ActionModule 的定位是「所有行动的唯一入口」，则保留以保持一致性；
	//         否则调用方可直接调用 InteractionModule.Execute。
	public static List<GameEvent> TryInteract(GameState state, Actor initiator, Actor target,
		InteractionDef def)
	{
		return InteractionModule.Execute(state, initiator, target, def);
	}

	// ── 内部工具 ──────────────────────────────────────────

	/// <summary>BumpAttack 自动攻击：随机选动作 + 随机选目标肢体。</summary>
	private static List<GameEvent> AutoBumpAttack(GameState state, Actor attacker, Actor target)
	{
		var pick = PickAttack(state, attacker, target);
		if (pick == null) return [];
		return CombatModule.Attack(state, attacker, target, pick.Value.Action, pick.Value.Limb);
	}

	/// <summary>随机选取一个攻击动作和一个目标肢体。无可用动作或目标无肢体时返回 null。</summary>
	// REVIEW: 随机种子为 RngSeed + Turn + attacker.Id.GetHashCode()。
	//         同一回合内同一 Actor 多次调用 PickAttack 会得到相同结果（种子一致）。
	//         目前流程中不会重复调用，但若未来需要则应引入递增计数器。
	private static (ActionDef Action, Limb Limb)? PickAttack(
		GameState state, Actor attacker, Actor target)
	{
		var actions = CombatModule.GetAttackActions(attacker);
		if (actions.Count == 0 || target.Limbs.Count == 0) return null;

		var rng = new Random(state.RngSeed + state.Turn + attacker.Id.GetHashCode());
		return (actions[rng.Next(actions.Count)], target.Limbs[rng.Next(target.Limbs.Count)]);
	}
}
