using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Needs;

/// <summary>
/// 精神崩溃类型。
/// </summary>
public enum MentalBreakType
{
	/// <summary>暴走：攻击最近的人（不分敌友）。</summary>
	Berserk,

	/// <summary>呆滞：原地不动若干回合。</summary>
	Catatonic,

	/// <summary>暴食：疯狂吃背包里的食物。</summary>
	BingeEating,

	/// <summary>逃跑：跑向远离所有人的方向。</summary>
	Flee,
}

/// <summary>
/// 精神崩溃运行时状态（存在 Actor 上）。
/// </summary>
public sealed class MentalBreakState
{
	public MentalBreakType Type { get; set; }
	public int StartTurn { get; set; }
	public int Duration { get; set; }

	/// <summary>是否仍在崩溃中。</summary>
	public bool IsActive(int currentTurn) => currentTurn - StartTurn < Duration;
}

/// <summary>
/// 精神崩溃模块：当 mood 过低时触发崩溃行为。
/// 
/// 机制：
/// - mood ≤ 15 时，每回合有概率触发 mental break
/// - mood ≤ 5 时概率大幅增加
/// - 崩溃持续若干回合，期间角色不受玩家控制
/// - 崩溃结束后 mood 回升一点（宣泄效果）
/// 
/// 调用方式：在 NeedSystem.Sync() 之后调用 MentalBreakModule.Check()
/// </summary>
public static class MentalBreakModule
{
	/// <summary>崩溃触发的 mood 阈值。</summary>
	private const float BreakThreshold = 15f;

	/// <summary>极端崩溃阈值。</summary>
	private const float ExtremeThreshold = 5f;

	/// <summary>崩溃后 mood 回升量。</summary>
	private const float PostBreakMoodBoost = 10f;

	/// <summary>崩溃冷却回合数。</summary>
	private const int BreakCooldownTurns = 50;

	/// <summary>
	/// 检查角色是否应该触发精神崩溃。
	/// 在每回合 NeedSystem.Sync() 之后调用。
	/// </summary>
	public static List<GameEvent> Check(GameState state, Actor actor)
	{
		var events = new List<GameEvent>();

		// 已经在崩溃中 → 推进崩溃
		if (actor.MentalBreak != null)
		{
			if (!actor.MentalBreak.IsActive(state.Turn))
			{
				// 崩溃结束
				events.Add(new GameEvent("mental_break_end")
				{
					InitiatorId = actor.Id,
					InitiatorActorName = actor.DisplayName,
					EffectType = actor.MentalBreak.Type.ToString(),
				});

				// 宣泄后 mood 回升
				actor.MoodValue = Math.Min(100f, actor.MoodValue + PostBreakMoodBoost);
				actor.MentalBreak = null;
			}

			return events;
		}

		// mood 高于阈值 → 不触发
		if (actor.MoodValue > BreakThreshold)
			return events;

		// 冷却检查
		if (state.Turn - actor.LastMentalBreakTurn < BreakCooldownTurns)
			return events;

		// 概率计算
		var rng = new Random(state.RngSeed + state.Turn + actor.Id.GetHashCode());
		var chance = actor.MoodValue <= ExtremeThreshold ? 0.25f : 0.08f;

		if (rng.NextDouble() > chance)
			return events;

		// 触发崩溃
		var breakType = SelectBreakType(rng, actor);
		var duration = breakType switch
		{
			MentalBreakType.Berserk => rng.Next(8, 15),
			MentalBreakType.Catatonic => rng.Next(15, 30),
			MentalBreakType.BingeEating => rng.Next(5, 10),
			MentalBreakType.Flee => rng.Next(10, 20),
			_ => 10,
		};

		actor.MentalBreak = new MentalBreakState
		{
			Type = breakType,
			StartTurn = state.Turn,
			Duration = duration,
		};
		actor.LastMentalBreakTurn = state.Turn;

		events.Add(new GameEvent("mental_break")
		{
			InitiatorId = actor.Id,
			InitiatorActorName = actor.DisplayName,
			EffectType = breakType.ToString(),
		});

		return events;
	}

	/// <summary>
	/// 在 AI 行为链中执行崩溃行为。
	/// 返回 true 表示崩溃行为已消耗本回合行动。
	/// </summary>
	public static bool TryExecuteBreakBehavior(GameState state, Actor actor, out List<GameEvent> events)
	{
		events = [];

		if (actor.MentalBreak == null || !actor.MentalBreak.IsActive(state.Turn))
			return false;

		switch (actor.MentalBreak.Type)
		{
			case MentalBreakType.Berserk:
				ExecuteBerserk(state, actor, events);
				break;

			case MentalBreakType.Catatonic:
				// 什么都不做 — 原地呆滞
				break;

			case MentalBreakType.BingeEating:
				ExecuteBingeEating(state, actor, events);
				break;

			case MentalBreakType.Flee:
				ExecuteFlee(state, actor, events);
				break;
		}

		return true;
	}

	/// <summary>获取角色当前是否处于精神崩溃状态。</summary>
	public static bool IsInBreak(Actor actor, int currentTurn) =>
		actor.MentalBreak != null && actor.MentalBreak.IsActive(currentTurn);

	// ── 内部 ──

	private static MentalBreakType SelectBreakType(Random rng, Actor actor)
	{
		// 有食物 → 可能暴食
		var hasFood = actor.Inventory.Any(i =>
			string.Equals(i.Category, "food", StringComparison.OrdinalIgnoreCase));

		var candidates = new List<(MentalBreakType Type, float Weight)>
		{
			(MentalBreakType.Berserk, 2f),
			(MentalBreakType.Catatonic, 3f),
			(MentalBreakType.Flee, 1.5f),
		};

		if (hasFood)
			candidates.Add((MentalBreakType.BingeEating, 2f));

		var total = candidates.Sum(c => c.Weight);
		var roll = (float)(rng.NextDouble() * total);
		var cumulative = 0f;
		foreach (var (type, weight) in candidates)
		{
			cumulative += weight;
			if (roll <= cumulative)
				return type;
		}

		return MentalBreakType.Catatonic;
	}

	private static void ExecuteBerserk(GameState state, Actor actor, List<GameEvent> events)
	{
		// 攻击 1 格内最近的任何角色
		Actor? nearest = null;
		var bestDist = int.MaxValue;

		foreach (var other in state.Actors.Values)
		{
			if (other.Id == actor.Id) continue;
			var dist = Math.Abs(other.X - actor.X) + Math.Abs(other.Y - actor.Y);
			if (dist <= 1 && dist < bestDist)
			{
				bestDist = dist;
				nearest = other;
			}
		}

		if (nearest == null) return;

		// 简单攻击（复用 CombatModule 的逻辑太重，这里直接造成伤害）
		var damage = Math.Max(1, actor.Limbs.Count);
		foreach (var limb in nearest.Limbs)
		{
			if (limb.Durability > 0)
			{
				var dealt = Math.Min(damage, limb.Durability);
				limb.Durability -= dealt;
				break;
			}
		}

		events.Add(new GameEvent("mental_break_attack")
		{
			InitiatorId = actor.Id,
			InitiatorActorName = actor.DisplayName,
			TargetId = nearest.Id,
			TargetActorName = nearest.DisplayName,
		});
	}

	private static void ExecuteBingeEating(GameState state, Actor actor, List<GameEvent> events)
	{
		var food = actor.Inventory.FirstOrDefault(i =>
			string.Equals(i.Category, "food", StringComparison.OrdinalIgnoreCase));

		if (food == null) return;

		actor.Inventory.Remove(food);

		// 恢复一点饥饿
		if (actor.Needs != null && actor.Needs.TryGetValue(NeedIds.Hunger, out var hunger))
			hunger.Current = Math.Max(0, hunger.Current - 15f);

		events.Add(new GameEvent("mental_break_binge")
		{
			InitiatorId = actor.Id,
			InitiatorActorName = actor.DisplayName,
			ItemName = food.Name,
		});
	}

	private static void ExecuteFlee(GameState state, Actor actor, List<GameEvent> events)
	{
		// 远离最近的其他角色
		var nearestDist = int.MaxValue;
		int fleeX = 0, fleeY = 0;

		foreach (var other in state.Actors.Values)
		{
			if (other.Id == actor.Id) continue;
			var dist = Math.Abs(other.X - actor.X) + Math.Abs(other.Y - actor.Y);
			if (dist < nearestDist)
			{
				nearestDist = dist;
				// 远离方向
				fleeX = Math.Sign(actor.X - other.X);
				fleeY = Math.Sign(actor.Y - other.Y);
			}
		}

		if (fleeX == 0 && fleeY == 0)
			fleeX = 1; // 默认向右跑

		var newX = actor.X + fleeX;
		var newY = actor.Y + fleeY;

		if (state.World != null && state.World.IsWalkable(newX, newY, actor.Z))
		{
			actor.X = newX;
			actor.Y = newY;
		}
	}
}
