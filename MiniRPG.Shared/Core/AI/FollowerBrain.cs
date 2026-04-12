using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.AI;

/// <summary>
/// 队伍跟随者大脑：非激活的队伍成员自动跟随激活角色。
/// 行为优先级：
///   1. 攻击相邻敌人
///   2. 跟随激活角色（保持 2 格距离内）
///   3. 空闲
/// </summary>
public class FollowerBrain : IBrainModule
{
	public const string BrainId = "party_follower";

	/// <summary>跟随距离：超过此距离开始移动。</summary>
	private const int FollowDistance = 2;

	/// <summary>最大跟随距离：超过此距离放弃跟随（可能被传送）。</summary>
	private const int MaxFollowDistance = 20;

	private static readonly (int Dx, int Dy)[] Dirs = [(0, -1), (0, 1), (-1, 0), (1, 0)];

	public Decision Decide(Perception perception, Random rng)
	{
		var self = perception.Self;

		// 1. 攻击相邻敌人
		var attackDecision = TryAttackNearby(self, perception);
		if (attackDecision != null)
			return attackDecision;

		// 2. 跟随激活角色
		var followTarget = FindFollowTarget(self, perception);
		if (followTarget != null)
		{
			var dist = Math.Abs(followTarget.X - self.X) + Math.Abs(followTarget.Y - self.Y);
			if (dist > FollowDistance)
				return BuildMoveTo(self, followTarget.X, followTarget.Y, perception);
		}

		// 3. 空闲
		return new Decision { Type = DecisionType.Idle };
	}

	private static Decision? TryAttackNearby(Actor self, Perception perception)
	{
		var skills = CombatModule.GetAttackActions(self)
			.Where(skill => !self.IsSkillOnCooldown(skill.Id))
			.OrderByDescending(skill => skill.Power)
			.ToList();
		if (skills.Count == 0)
			return null;

		var adjacentEnemy = perception.NearbyActors
			.Where(other => !CombatModule.IsDead(other)
				&& FactionRelation.IsHostile(self.Faction, other.Faction)
				&& other.Limbs.Count > 0
				&& Math.Abs(other.X - self.X) + Math.Abs(other.Y - self.Y) == 1)
			.OrderBy(other => other.Id, StringComparer.Ordinal)
			.FirstOrDefault();

		if (adjacentEnemy == null)
			return null;

		var skill = skills.FirstOrDefault(s => s.Range >= 1);
		if (skill == null)
			return null;

		return new Decision
		{
			Type = DecisionType.Attack,
			TargetActorId = adjacentEnemy.Id,
			ActionDefId = skill.Id,
		};
	}

	private static Actor? FindFollowTarget(Actor self, Perception perception)
	{
		if (perception.State == null)
			return null;

		var activeId = PartyModule.GetActiveId(perception.State);
		if (string.Equals(activeId, self.Id, StringComparison.Ordinal))
			return null;

		return ActorModule.GetById(perception.State, activeId);
	}

	private static Decision BuildMoveTo(Actor self, int targetX, int targetY, Perception perception)
	{
		if (perception.State != null)
		{
			var z = perception.Floor;
			var state = perception.State;
			var step = World.Pathfinding.NextStep(
				self.X, self.Y, targetX, targetY,
				(x, y) => Health.FireSystem.IsSafeWalkableForActor(state, self, x, y, z));
			if (step != null)
				return new Decision { Type = DecisionType.MoveTo, TargetPos = step };
		}

		// 简单贪心
		(int, int)? best = null;
		var bestDist = Math.Abs(targetX - self.X) + Math.Abs(targetY - self.Y);
		foreach (var (dx, dy) in Dirs)
		{
			var nx = self.X + dx;
			var ny = self.Y + dy;
			if (!IsWalkable(perception, nx, ny))
				continue;

			var dist = Math.Abs(targetX - nx) + Math.Abs(targetY - ny);
			if (dist < bestDist)
			{
				bestDist = dist;
				best = (nx, ny);
			}
		}

		return best == null
			? new Decision { Type = DecisionType.Idle }
			: new Decision { Type = DecisionType.MoveTo, TargetPos = best };
	}

	private static bool IsWalkable(Perception perception, int x, int y)
	{
		if (perception.NearbyWalkable.TryGetValue((x, y), out var walkable))
			return walkable;

		return perception.State != null
			&& Health.FireSystem.IsSafeWalkableForActor(perception.State, perception.Self, x, y, perception.Floor);
	}
}
