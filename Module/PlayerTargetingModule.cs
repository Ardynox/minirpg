using System;
using System.Linq;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;

namespace MiniRPG.Module;

public enum PlayerTargetSource
{
	Auto,
	Explicit,
	Inspect,
}

public readonly record struct PlayerTargetingContext(
	string? CurrentTargetActorId,
	PlayerTargetSource CurrentTargetSource)
{
	public static PlayerTargetingContext Empty => new(null, PlayerTargetSource.Auto);
}

public readonly record struct PlayerTargetResolution(Actor? Target, PlayerTargetingContext Context);

public readonly record struct TargetSummarySelection(Actor? Target, bool MarkCurrentTarget);

public static class PlayerTargetingModule
{
	public static PlayerTargetingContext SetTarget(Actor target, PlayerTargetSource source) =>
		new(target.Id, source);

	public static PlayerTargetResolution Resolve(GameState state, PlayerTargetingContext context)
	{
		var player = ActorModule.GetPlayer(state);
		if (player == null)
			return new PlayerTargetResolution(null, PlayerTargetingContext.Empty);

		var currentTarget = TryGetValidTarget(state, player, context.CurrentTargetActorId);
		if (currentTarget != null)
		{
			return new PlayerTargetResolution(
				currentTarget,
				new PlayerTargetingContext(currentTarget.Id, context.CurrentTargetSource));
		}

		var fallback = FindAutoTarget(state, player);
		if (fallback == null)
			return new PlayerTargetResolution(null, PlayerTargetingContext.Empty);

		return new PlayerTargetResolution(
			fallback,
			new PlayerTargetingContext(fallback.Id, PlayerTargetSource.Auto));
	}

	public static TargetSummarySelection ResolveSummaryTarget(
		GameState state,
		PlayerTargetingContext context,
		Func<Actor, bool>? visibilityPredicate = null)
	{
		var player = ActorModule.GetPlayer(state);
		if (player == null)
			return new TargetSummarySelection(null, false);

		var resolution = Resolve(state, context);
		if (resolution.Target == null)
			return new TargetSummarySelection(null, false);

		if (resolution.Context.CurrentTargetSource == PlayerTargetSource.Auto)
			return new TargetSummarySelection(resolution.Target, false);

		if (IsPresentableInHud(player, resolution.Target, visibilityPredicate))
			return new TargetSummarySelection(resolution.Target, true);

		return new TargetSummarySelection(FindAutoTarget(state, player), false);
	}

	public static bool TryResolveDirectSkillCast(
		GameState state,
		Actor player,
		PlayerTargetingContext context,
		InteractionDef skill,
		out TimelinePlayerAction? action,
		out PlayerTargetingContext normalizedContext)
	{
		action = null;
		var resolution = Resolve(state, context);
		normalizedContext = resolution.Context;
		if (resolution.Target == null)
			return false;

		if (ActionModule.ResolveSkillTargetType(skill) != SkillTargetType.Actor)
			return false;

		if (!ActionModule.CanCastSkill(
				state,
				player,
				skill.Id,
				SkillTargetType.Actor,
				targetActor: resolution.Target,
				targetX: resolution.Target.X,
				targetY: resolution.Target.Y,
				targetZ: resolution.Target.Z))
		{
			return false;
		}

		action = TimelinePlayerAction.CastSkill(
			skill.Id,
			SkillTargetType.Actor,
			targetActorId: resolution.Target.Id,
			targetX: resolution.Target.X,
			targetY: resolution.Target.Y,
			targetZ: resolution.Target.Z);
		return true;
	}

	public static (int X, int Y, int Z) ResolveCursorOrigin(GameState state, Actor player, PlayerTargetingContext context)
	{
		var resolution = Resolve(state, context);
		if (resolution.Target != null)
			return (resolution.Target.X, resolution.Target.Y, resolution.Target.Z);

		return (player.X, player.Y, player.Z);
	}

	public static bool IsTargetValid(Actor player, Actor? target)
	{
		if (target == null)
			return false;
		if (target.Id == player.Id)
			return false;
		if (target.Z != player.Z)
			return false;
		if (CombatModule.IsDead(target))
			return false;

		return FactionRelation.IsHostile(player.Faction, target.Faction);
	}

	public static bool IsPresentableInHud(Actor player, Actor target, Func<Actor, bool>? visibilityPredicate = null)
	{
		if (!IsTargetValid(player, target))
			return false;
		if (IsAdjacent(player, target))
			return true;
		if (visibilityPredicate?.Invoke(target) == true)
			return true;

		return target.AwarenessState != AwarenessState.Idle;
	}

	public static Actor? FindAutoTarget(GameState state, Actor player)
	{
		var adjacent = state.Actors.Values
			.Where(actor => IsTargetValid(player, actor) && IsAdjacent(player, actor))
			.OrderByDescending(ThreatPriority)
			.ThenBy(actor => actor.Id, StringComparer.Ordinal)
			.FirstOrDefault();
		if (adjacent != null)
			return adjacent;

		return state.Actors.Values
			.Where(actor => IsTargetValid(player, actor) && actor.AwarenessState != AwarenessState.Idle)
			.OrderByDescending(ThreatPriority)
			.ThenBy(actor => Math.Abs(actor.X - player.X) + Math.Abs(actor.Y - player.Y))
			.ThenBy(actor => actor.Id, StringComparer.Ordinal)
			.FirstOrDefault();
	}

	private static Actor? TryGetValidTarget(GameState state, Actor player, string? actorId)
	{
		if (string.IsNullOrWhiteSpace(actorId))
			return null;

		var actor = ActorModule.GetById(state, actorId);
		return IsTargetValid(player, actor) ? actor : null;
	}

	private static bool IsAdjacent(Actor player, Actor target) =>
		Math.Abs(target.X - player.X) + Math.Abs(target.Y - player.Y) == 1;

	private static int ThreatPriority(Actor actor) => actor.AwarenessState switch
	{
		AwarenessState.Alerted => 3,
		AwarenessState.Searching => 2,
		AwarenessState.Suspicious => 1,
		_ => 0,
	};
}
