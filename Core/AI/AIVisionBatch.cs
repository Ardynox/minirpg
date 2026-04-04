using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MiniRPG.Core.World;

namespace MiniRPG.Core.AI;

/// <summary>
/// AI 视觉批处理入口：当前为 CPU 实现，未来 GPU 方案只替换这一层。
/// </summary>
public readonly record struct AIVisionRequest(Actor Observer, SimDetail Detail);

public sealed class AIVisionMetrics
{
	public int ObserverCount { get; init; }
	public int CandidateCount { get; init; }
	public int ShortlistCount { get; init; }
	public int LosChecks { get; init; }
	public int VisibleActorCount { get; init; }
	public double ElapsedMs { get; init; }
}

public static class AIVisionBatch
{
	public static AIVisionMetrics LastMetrics { get; private set; } = new();

	public static Dictionary<string, Perception> Build(GameState state, IReadOnlyList<AIVisionRequest> requests)
	{
		var result = new Dictionary<string, Perception>(requests.Count);
		var world = state.World;
		if (world == null || requests.Count == 0)
		{
			LastMetrics = new AIVisionMetrics { ObserverCount = requests.Count };
			return result;
		}

		var sw = Stopwatch.StartNew();
		var config = GameConfig.AIVision;
		var localContextRadius = Math.Max(0, config.LocalContextRadius);
		int candidateCount = 0;
		int shortlistCount = 0;
		int losChecks = 0;
		int visibleActorCount = 0;

		foreach (var request in requests)
		{
			var observer = request.Observer;
			var visibleActors = new List<Actor>();
			var nearbyWalkable = BuildLocalWalkable(state, observer, localContextRadius);
			var nearbyFixtures = BuildLocalFixtures(state, observer, localContextRadius);

			if (request.Detail != SimDetail.Summary)
			{
				var candidates = CollectCandidates(state, observer, request.Detail, config);
				candidateCount += candidates.Count;

				var shortlist = candidates
					.OrderBy(target => CandidateScore(observer, target))
					.Take(ShortlistLimitFor(request.Detail, config))
					.ToList();

				shortlistCount += shortlist.Count;

				foreach (var target in shortlist)
				{
					losChecks++;
					if (!VisibilityUtil.HasLineOfSight(world, observer.X, observer.Y, target.X, target.Y, observer.Z))
						continue;

					visibleActors.Add(target);
				}

				visibleActorCount += visibleActors.Count;
			}

			result[observer.Id] = new Perception
			{
				Self = observer,
				NearbyActors = visibleActors,
				NearbyWalkable = nearbyWalkable,
				NearbyFixtures = nearbyFixtures,
				Turn = state.Turn,
				Floor = observer.Z,
				State = state,
			};
		}

		sw.Stop();
		LastMetrics = new AIVisionMetrics
		{
			ObserverCount = requests.Count,
			CandidateCount = candidateCount,
			ShortlistCount = shortlistCount,
			LosChecks = losChecks,
			VisibleActorCount = visibleActorCount,
			ElapsedMs = sw.Elapsed.TotalMilliseconds,
		};

		return result;
	}

	private static Dictionary<(int X, int Y), bool> BuildLocalWalkable(
		GameState state,
		Actor observer,
		int localContextRadius)
	{
		var nearbyWalkable = new Dictionary<(int, int), bool>();
		for (int dy = -localContextRadius; dy <= localContextRadius; dy++)
		for (int dx = -localContextRadius; dx <= localContextRadius; dx++)
		{
			var x = observer.X + dx;
			var y = observer.Y + dy;
			nearbyWalkable[(x, y)] = MapModule.IsWalkable(state, x, y, observer.Z);
		}

		return nearbyWalkable;
	}

	private static Dictionary<(int X, int Y), string> BuildLocalFixtures(
		GameState state,
		Actor observer,
		int localContextRadius)
	{
		var nearbyFixtures = new Dictionary<(int, int), string>();
		for (int dy = -localContextRadius; dy <= localContextRadius; dy++)
		for (int dx = -localContextRadius; dx <= localContextRadius; dx++)
		{
			var x = observer.X + dx;
			var y = observer.Y + dy;
			var fixtureId = MapModule.GetFixtureId(state, x, y, observer.Z);
			if (!string.IsNullOrEmpty(fixtureId))
				nearbyFixtures[(x, y)] = fixtureId;
		}

		return nearbyFixtures;
	}

	private static List<Actor> CollectCandidates(
		GameState state,
		Actor observer,
		SimDetail detail,
		AIVisionConfig config)
	{
		var result = new List<Actor>();
		int frontRange = RangeFor(detail, config);
		int rearRange = Math.Max(2, (int)(frontRange * config.RearVisionRatio));
		long frontSq = (long)frontRange * frontRange;
		long rearSq = (long)rearRange * rearRange;

		foreach (var other in state.Actors.Values)
		{
			if (other.Id == observer.Id) continue;
			if (other.Z != observer.Z) continue;
			if (CombatModule.IsDead(other)) continue;

			int dx = other.X - observer.X;
			int dy = other.Y - observer.Y;
			long distSq = (long)dx * dx + (long)dy * dy;
			int dot = dx * observer.FacingX + dy * observer.FacingY;
			long maxSq = dot >= 0 ? frontSq : rearSq;
			if (distSq > maxSq) continue;

			result.Add(other);
		}

		return result;
	}

	private static int CandidateScore(Actor observer, Actor target)
	{
		int dx = target.X - observer.X;
		int dy = target.Y - observer.Y;
		long distSq = (long)dx * dx + (long)dy * dy;
		int dot = dx * observer.FacingX + dy * observer.FacingY;
		int hostilityPenalty = FactionRelation.IsHostile(observer.Faction, target.Faction) ? 0 : 10000;
		int rearPenalty = dot < 0 ? 2000 : 0;

		return hostilityPenalty + rearPenalty + (int)Math.Min(distSq, int.MaxValue / 4);
	}

	private static int RangeFor(SimDetail detail, AIVisionConfig config) => detail switch
	{
		SimDetail.Full => Math.Max(0, config.FullRange),
		SimDetail.Simplified => Math.Max(0, config.SimplifiedRange),
		_ => 0,
	};

	private static int ShortlistLimitFor(SimDetail detail, AIVisionConfig config) => detail switch
	{
		SimDetail.Full => Math.Max(0, config.FullShortlistLimit),
		SimDetail.Simplified => Math.Max(0, config.SimplifiedShortlistLimit),
		_ => 0,
	};
}
