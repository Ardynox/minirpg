using System;
using System.Collections.Generic;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Job;

namespace MiniRPG.Core.AI.Utility;

public sealed class UtilityBrain
{
	public UtilityBrain()
	{
	}

	public UtilityEvalResult Evaluate(
		GameState state,
		Actor actor,
		Perception perception,
		AIBehaviorContext? behaviorContext,
		SimDetail detail,
		Random rng,
		IReadOnlyList<UtilityActionDef>? actionSubset = null)
	{
		var ctx = new InputContext
		{
			Self = actor,
			Perception = perception,
			State = state,
			BehaviorContext = behaviorContext,
		};

		var actions = actionSubset ?? UtilityActionRegistry.ActionList;
		var bestResult = UtilityEvalResult.None;
		var bestScore = -1f;

		for (var i = 0; i < actions.Count; i++)
		{
			var actionDef = actions[i];
			var result = EvaluateAction(actionDef, ctx, detail, rng);
			if (result.Score > bestScore)
			{
				bestScore = result.Score;
				bestResult = result;
			}
		}

		return bestResult;
	}

	public UtilityEvalResult EvaluateAction(
		UtilityActionDef actionDef,
		InputContext ctx,
		SimDetail detail,
		Random rng)
	{
		if (actionDef.RequiresTarget)
			return EvaluateTargetedAction(actionDef, ctx, detail, rng);

		return EvaluateSimpleAction(actionDef, ctx, rng);
	}

	private UtilityEvalResult EvaluateSimpleAction(UtilityActionDef actionDef, InputContext ctx, Random rng)
	{
		var score = ComputeScore(actionDef, ctx, rng);
		if (score <= 0f)
			return UtilityEvalResult.None;

		return new UtilityEvalResult
		{
			Action = actionDef,
			Score = score,
		};
	}

	private UtilityEvalResult EvaluateTargetedAction(
		UtilityActionDef actionDef,
		InputContext ctx,
		SimDetail detail,
		Random rng)
	{
		var targetType = actionDef.TargetType;
		switch (targetType)
		{
			case "enemy":
				return EvaluateWithEnemyTarget(actionDef, ctx, detail, rng);
			case "ally_fire":
				return EvaluateWithAllyTarget(actionDef, ctx, detail, rng, "rescue_fire");
			case "ally_wounded":
				return EvaluateWithAllyTarget(actionDef, ctx, detail, rng, "tend");
			case "work_ticket":
				return EvaluateWithWorkTicket(actionDef, ctx, rng);
			case "social":
				return EvaluateWithSocialTarget(actionDef, ctx, rng);
			case "follow":
				return EvaluateWithFollowTarget(actionDef, ctx, rng);
			default:
				return EvaluateSimpleAction(actionDef, ctx, rng);
		}
	}

	private UtilityEvalResult EvaluateWithEnemyTarget(
		UtilityActionDef actionDef,
		InputContext ctx,
		SimDetail detail,
		Random rng)
	{
		if (detail == SimDetail.Full)
		{
			var bestResult = UtilityEvalResult.None;
			var bestScore = -1f;
			foreach (var other in ctx.Perception.NearbyActors)
			{
				if (CombatModule.IsDead(other)) continue;
				if (!FactionRelation.IsHostile(ctx.Self.Faction, other.Faction)) continue;
				ctx.TargetActor = other;
				var score = ComputeScore(actionDef, ctx, rng);
				if (score > bestScore)
				{
					bestScore = score;
					bestResult = new UtilityEvalResult
					{
						Action = actionDef,
						Score = score,
						TargetActor = other,
					};
				}
			}
			ctx.TargetActor = null;
			return bestResult;
		}

		var target = TargetResolver.FindBestEnemyTarget(ctx.Self, ctx.Perception, ctx.State, detail);
		if (target == null) return UtilityEvalResult.None;
		ctx.TargetActor = target;
		var s = ComputeScore(actionDef, ctx, rng);
		ctx.TargetActor = null;
		return s <= 0f ? UtilityEvalResult.None : new UtilityEvalResult
		{
			Action = actionDef,
			Score = s,
			TargetActor = target,
		};
	}

	private UtilityEvalResult EvaluateWithAllyTarget(
		UtilityActionDef actionDef,
		InputContext ctx,
		SimDetail detail,
		Random rng,
		string purpose)
	{
		if (detail == SimDetail.Full)
		{
			var bestResult = UtilityEvalResult.None;
			var bestScore = -1f;
			foreach (var other in ctx.Perception.NearbyActors)
			{
				if (CombatModule.IsDead(other)) continue;
				if (other.Id == ctx.Self.Id) continue;
				if (FactionRelation.IsHostile(ctx.Self.Faction, other.Faction)) continue;
				ctx.TargetActor = other;
				var score = ComputeScore(actionDef, ctx, rng);
				if (score > bestScore)
				{
					bestScore = score;
					bestResult = new UtilityEvalResult
					{
						Action = actionDef,
						Score = score,
						TargetActor = other,
					};
				}
			}
			ctx.TargetActor = null;
			return bestResult;
		}

		var target = TargetResolver.FindBestAllyTarget(ctx.Self, ctx.Perception, ctx.State, detail, purpose);
		if (target == null) return UtilityEvalResult.None;
		ctx.TargetActor = target;
		var s = ComputeScore(actionDef, ctx, rng);
		ctx.TargetActor = null;
		return s <= 0f ? UtilityEvalResult.None : new UtilityEvalResult
		{
			Action = actionDef,
			Score = s,
			TargetActor = target,
		};
	}

	private static UtilityEvalResult EvaluateWithWorkTicket(
		UtilityActionDef actionDef,
		InputContext ctx,
		Random rng)
	{
		if (ctx.State == null) return UtilityEvalResult.None;
		var reserved = JobScheduler.GetReservedTicket(ctx.State, ctx.Self);
		var ticket = reserved ?? JobScheduler.FindBestTicket(ctx.State, ctx.Self);
		if (ticket == null) return UtilityEvalResult.None;
		ctx.TargetTicket = ticket;
		var score = ComputeScore(actionDef, ctx, null);
		ctx.TargetTicket = null;
		return score <= 0f ? UtilityEvalResult.None : new UtilityEvalResult
		{
			Action = actionDef,
			Score = score,
			TargetTicket = ticket,
		};
	}

	private static UtilityEvalResult EvaluateWithFollowTarget(
		UtilityActionDef actionDef,
		InputContext ctx,
		Random rng)
	{
		if (ctx.State == null) return UtilityEvalResult.None;
		var leader = TargetResolver.FindFollowTarget(ctx.State, ctx.Self);
		if (leader == null) return UtilityEvalResult.None;
		ctx.TargetActor = leader;
		var score = ComputeScore(actionDef, ctx, rng);
		ctx.TargetActor = null;
		return score <= 0f ? UtilityEvalResult.None : new UtilityEvalResult
		{
			Action = actionDef,
			Score = score,
			TargetActor = leader,
		};
	}

	private static UtilityEvalResult EvaluateWithSocialTarget(
		UtilityActionDef actionDef,
		InputContext ctx,
		Random rng)
	{
		var target = TargetResolver.FindBestSocialTarget(ctx.Self, ctx.Perception, ctx.State);
		if (target == null) return UtilityEvalResult.None;
		ctx.TargetActor = target;
		var score = ComputeScore(actionDef, ctx, rng);
		ctx.TargetActor = null;
		return score <= 0f ? UtilityEvalResult.None : new UtilityEvalResult
		{
			Action = actionDef,
			Score = score,
			TargetActor = target,
		};
	}

	private static float ComputeScore(UtilityActionDef actionDef, InputContext ctx, Random? rng)
	{
		var considerations = actionDef.Considerations;
		if (considerations.Count == 0)
			return actionDef.BonusScore;

		var product = 1f;
		for (var i = 0; i < considerations.Count; i++)
		{
			var c = considerations[i];
			var raw = InputResolver.Resolve(c.Input, ctx);
			var mapped = c.Curve.Evaluate(raw);
			if (mapped <= 0f)
				return 0f;
			product *= mapped;
		}

		var n = considerations.Count;
		var compensated = n > 1 ? MathF.Pow(product, 1f / n) : product;
		var final = actionDef.BonusScore * compensated;

		if (rng != null)
			final *= 1f + (float)(rng.NextDouble() * 0.1 - 0.05);

		return final;
	}
}
