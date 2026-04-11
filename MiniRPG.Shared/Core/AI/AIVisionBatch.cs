using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Health;
using MiniRPG.Core.World;

namespace MiniRPG.Core.AI;

/// <summary>
/// AI 过回合快照缓存：在单次 AdvanceAuto 调用期间由 TimelineTurnManager 管理生命周期，
/// 供 AIVisionBatch 复用，将 BuildActorSnapshots 从 O(N)×N 降为 O(N)×1。
/// </summary>
public sealed class TimelineSnapshotCache
{
	internal List<VisionActorSnapshot> Snapshots { get; } = new();
	internal Dictionary<string, VisionActorSnapshot> ById { get; } = new(StringComparer.Ordinal);
	public bool IsDirty { get; set; } = true;

	/// <summary>
	/// 刷新所有 snapshot 的坐标、IsDead、SightCapacity。
	/// 此时 ComputeCapacities() 均为缓存命中，每次调用代价约 O(1)。
	/// 同步更新 ById 映射以保持观察者查找时的一致性。
	/// </summary>
	internal void RefreshFromActors()
	{
		for (var i = 0; i < Snapshots.Count; i++)
		{
			var snap = Snapshots[i];
			var a = snap.Actor;
			var dead = CombatModule.IsDead(a);
			var sight = a.GetCapacity(Caps.Sight);
			if (snap.X != a.X
				|| snap.Y != a.Y
				|| snap.Z != a.Z
				|| snap.IsDead != dead
				|| snap.SightCapacity != sight)
			{
				var updated = snap with
				{
					X = a.X,
					Y = a.Y,
					Z = a.Z,
					IsDead = dead,
					SightCapacity = sight,
				};
				Snapshots[i] = updated;
				ById[a.Id] = updated;
			}
		}
	}
}

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
	public double LocalContextMs { get; init; }
	public double CandidateScanMs { get; init; }
	public double DeadCheckMs { get; init; }
	public double SightCapacityMs { get; init; }
	public double RankMs { get; init; }
	public double LosMs { get; init; }
	public int DeadChecks { get; init; }
	public int CapacityCalls { get; init; }
	public int MaxCandidatesPerObserver { get; init; }
	public int MaxShortlistPerObserver { get; init; }
}

internal readonly record struct VisionActorSnapshot(
	Actor Actor,
	int X,
	int Y,
	int Z,
	string Faction,
	bool IsDead,
	float SightCapacity);

public static class AIVisionBatch
{
	public static AIVisionMetrics LastMetrics { get; private set; } = new();

	public static Dictionary<string, Perception> Build(GameState state, IReadOnlyList<AIVisionRequest> requests) =>
		BuildCore(state, requests, captureDetailedProfile: false);

	public static Dictionary<string, Perception> BuildProfiled(GameState state, IReadOnlyList<AIVisionRequest> requests) =>
		BuildCore(state, requests, captureDetailedProfile: true);

	private static Dictionary<string, Perception> BuildCore(
		GameState state,
		IReadOnlyList<AIVisionRequest> requests,
		bool captureDetailedProfile)
	{
		var result = new Dictionary<string, Perception>(requests.Count);
		var world = state.World;
		if (world == null || requests.Count == 0)
		{
			LastMetrics = new AIVisionMetrics
			{
				ObserverCount = world == null ? 0 : requests.Count,
			};
			return result;
		}

		var batchStart = ProfilingClock.Start();
		var config = GameConfig.AIVision;
		var localContextRadius = Math.Max(0, config.LocalContextRadius);
		int candidateCount = 0;
		int shortlistCount = 0;
		int losChecks = 0;
		int visibleActorCount = 0;
		double localContextMs = 0d;
		double candidateScanMs = 0d;
		double deadCheckMs = 0d;
		double sightCapacityMs = 0d;
		double rankMs = 0d;
		double losMs = 0d;
		int deadChecks = 0;
		int capacityCalls = 0;
		int maxCandidatesPerObserver = 0;
		int maxShortlistPerObserver = 0;
		IReadOnlyList<VisionActorSnapshot> actorSnapshots = Array.Empty<VisionActorSnapshot>();
		var actorSnapshotsById = new Dictionary<string, VisionActorSnapshot>(StringComparer.Ordinal);
		var requiresVisionScan = requests.Any(static request => request.Detail != SimDetail.Summary);
		if (requiresVisionScan)
		{
			var cache = state.SnapshotCache;
			if (cache != null && !cache.IsDirty && cache.Snapshots.Count > 0)
			{
				// Reuse cached snapshots — only refresh mutable fields (cheap due to Actor capacity cache).
				var refreshStart = captureDetailedProfile ? ProfilingClock.Start() : 0L;
				cache.RefreshFromActors();
				if (captureDetailedProfile)
					candidateScanMs += ProfilingClock.ElapsedMs(refreshStart);
				actorSnapshots = cache.Snapshots;
				actorSnapshotsById = cache.ById;
			}
			else
			{
				// When snapshot cache is enabled, compute sight for ALL actors so the cache
				// is reusable across different observer sets across subsequent AI steps.
				// Otherwise only observers need sight capacity (targets never read it).
				var observerIds = new HashSet<string>(StringComparer.Ordinal);
				if (cache != null)
				{
					foreach (var actor in state.Actors.Values)
						observerIds.Add(actor.Id);
				}
				else
				{
					foreach (var req in requests)
						if (req.Detail != SimDetail.Summary)
							observerIds.Add(req.Observer.Id);
				}

				(actorSnapshots, actorSnapshotsById) = BuildActorSnapshots(
					state,
					observerIds,
					captureDetailedProfile,
					ref candidateScanMs,
					ref deadCheckMs,
					ref sightCapacityMs,
					ref deadChecks,
					ref capacityCalls);

				if (cache != null)
				{
					cache.Snapshots.Clear();
					cache.Snapshots.AddRange(actorSnapshots);
					cache.ById.Clear();
					foreach (var kv in actorSnapshotsById)
						cache.ById[kv.Key] = kv.Value;
					cache.IsDirty = false;
				}
			}
		}

		foreach (var request in requests)
		{
			var observer = request.Observer;
			var visibleActors = new List<Actor>();
			Dictionary<(int X, int Y), bool> nearbyWalkable;
			Dictionary<(int X, int Y), string> nearbyFixtures;
			if (captureDetailedProfile)
			{
				var localStart = ProfilingClock.Start();
				nearbyWalkable = BuildLocalWalkable(state, observer, localContextRadius);
				nearbyFixtures = BuildLocalFixtures(state, observer, localContextRadius);
				localContextMs += ProfilingClock.ElapsedMs(localStart);
			}
			else
			{
				nearbyWalkable = BuildLocalWalkable(state, observer, localContextRadius);
				nearbyFixtures = BuildLocalFixtures(state, observer, localContextRadius);
			}

			if (request.Detail != SimDetail.Summary)
			{
				if (!actorSnapshotsById.TryGetValue(observer.Id, out var observerSnapshot))
					observerSnapshot = CreateActorSnapshot(
						observer,
						computeSight: true,
						captureDetailedProfile,
						ref deadCheckMs,
						ref sightCapacityMs,
						ref deadChecks,
						ref capacityCalls);

				var candidateScanStart = captureDetailedProfile ? ProfilingClock.Start() : 0L;
				var candidates = CollectCandidates(
					state,
					observerSnapshot,
					actorSnapshots,
					request.Detail,
					config);
				candidateCount += candidates.Count;
				maxCandidatesPerObserver = Math.Max(maxCandidatesPerObserver, candidates.Count);
				if (captureDetailedProfile)
					candidateScanMs += ProfilingClock.ElapsedMs(candidateScanStart);

				var shortlistLimit = ShortlistLimitFor(request.Detail, config);
				int shortlistEnd;
				if (captureDetailedProfile)
				{
					var rankStart = ProfilingClock.Start();
					shortlistEnd = PartialSortTopK(candidates, observerSnapshot, shortlistLimit);
					rankMs += ProfilingClock.ElapsedMs(rankStart);
				}
				else
				{
					shortlistEnd = PartialSortTopK(candidates, observerSnapshot, shortlistLimit);
				}

				shortlistCount += shortlistEnd;
				maxShortlistPerObserver = Math.Max(maxShortlistPerObserver, shortlistEnd);

				for (var si = 0; si < shortlistEnd; si++)
				{
					var target = candidates[si];
					losChecks++;
					var canSeeTarget = false;
					if (captureDetailedProfile)
					{
						var losStart = ProfilingClock.Start();
						canSeeTarget = VisibilityUtil.HasLineOfSight(
							world,
							observerSnapshot.X,
							observerSnapshot.Y,
							target.X,
							target.Y,
							observerSnapshot.Z);
						losMs += ProfilingClock.ElapsedMs(losStart);
					}
					else
					{
						canSeeTarget = VisibilityUtil.HasLineOfSight(
							world,
							observerSnapshot.X,
							observerSnapshot.Y,
							target.X,
							target.Y,
							observerSnapshot.Z);
					}
					if (!canSeeTarget)
						continue;

					visibleActors.Add(target.Actor);
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

		LastMetrics = new AIVisionMetrics
		{
			ObserverCount = requests.Count,
			CandidateCount = candidateCount,
			ShortlistCount = shortlistCount,
			LosChecks = losChecks,
			VisibleActorCount = visibleActorCount,
			ElapsedMs = ProfilingClock.ElapsedMs(batchStart),
			LocalContextMs = localContextMs,
			CandidateScanMs = candidateScanMs,
			DeadCheckMs = deadCheckMs,
			SightCapacityMs = sightCapacityMs,
			RankMs = rankMs,
			LosMs = losMs,
			DeadChecks = deadChecks,
			CapacityCalls = capacityCalls,
			MaxCandidatesPerObserver = maxCandidatesPerObserver,
			MaxShortlistPerObserver = maxShortlistPerObserver,
		};

		return result;
	}

	private static (IReadOnlyList<VisionActorSnapshot> Actors, Dictionary<string, VisionActorSnapshot> ById) BuildActorSnapshots(
		GameState state,
		HashSet<string> observerIds,
		bool captureDetailedProfile,
		ref double candidateScanMs,
		ref double deadCheckMs,
		ref double sightCapacityMs,
		ref int deadChecks,
		ref int capacityCalls)
	{
		var stageStart = captureDetailedProfile ? ProfilingClock.Start() : 0L;
		var snapshots = new List<VisionActorSnapshot>(state.Actors.Count);
		var snapshotsById = new Dictionary<string, VisionActorSnapshot>(state.Actors.Count, StringComparer.Ordinal);
		foreach (var actor in state.Actors.Values)
		{
			// Only compute sight capacity for observer actors — targets never use it.
			var computeSight = observerIds.Contains(actor.Id);
			var snapshot = CreateActorSnapshot(
				actor,
				computeSight,
				captureDetailedProfile,
				ref deadCheckMs,
				ref sightCapacityMs,
				ref deadChecks,
				ref capacityCalls);
			snapshots.Add(snapshot);
			snapshotsById[actor.Id] = snapshot;
		}

		if (captureDetailedProfile)
			candidateScanMs += ProfilingClock.ElapsedMs(stageStart);

		return (snapshots, snapshotsById);
	}

	private static VisionActorSnapshot CreateActorSnapshot(
		Actor actor,
		bool computeSight,
		bool captureDetailedProfile,
		ref double deadCheckMs,
		ref double sightCapacityMs,
		ref int deadChecks,
		ref int capacityCalls)
	{
		bool isDead;
		if (captureDetailedProfile)
		{
			var deadCheckStart = ProfilingClock.Start();
			isDead = CombatModule.IsDead(actor);
			deadCheckMs += ProfilingClock.ElapsedMs(deadCheckStart);
			deadChecks++;
		}
		else
		{
			isDead = CombatModule.IsDead(actor);
		}

		float sightCapacity;
		if (!computeSight)
		{
			// Target actors: sight capacity is never used — skip the expensive ComputeCapacities call.
			sightCapacity = 1.0f;
		}
		else if (captureDetailedProfile)
		{
			var capacityStart = ProfilingClock.Start();
			sightCapacity = actor.GetCapacity(Caps.Sight);
			sightCapacityMs += ProfilingClock.ElapsedMs(capacityStart);
			capacityCalls++;
		}
		else
		{
			sightCapacity = actor.GetCapacity(Caps.Sight);
		}

		return new VisionActorSnapshot(
			actor,
			actor.X,
			actor.Y,
			actor.Z,
			actor.Faction,
			isDead,
			sightCapacity);
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
			nearbyWalkable[(x, y)] = FireSystem.IsSafeWalkableForActor(state, observer, x, y, observer.Z);
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

	private static List<VisionActorSnapshot> CollectCandidates(
		GameState state,
		VisionActorSnapshot observer,
		IReadOnlyList<VisionActorSnapshot> actorSnapshots,
		SimDetail detail,
		AIVisionConfig config)
	{
		var result = new List<VisionActorSnapshot>();
		var range = RangeFor(detail, config);
		if (state.World != null
			&& observer.Z == 0
			&& state.World.IsWeatherExposed(observer.X, observer.Y, observer.Z))
		{
			var weather = WeatherRules.GetLocalWeather(state, observer.X, observer.Y, observer.Z);
			range = Math.Max(1, (int)MathF.Round(range * WeatherRules.GetAiVisionMultiplier(weather)));
		}

		var vision = VisionRangeScaler.ScaleDirectional(
			range,
			observer.SightCapacity,
			config.RearVisionRatio);
		int frontRange = vision.FrontRadius;
		int rearRange = vision.RearRadius;
		long frontSq = (long)frontRange * frontRange;
		long rearSq = (long)rearRange * rearRange;

		foreach (var other in actorSnapshots)
		{
			if (other.Actor.Id == observer.Actor.Id) continue;
			if (other.Z != observer.Z) continue;
			if (other.IsDead)
				continue;

			int dx = other.X - observer.X;
			int dy = other.Y - observer.Y;
			long distSq = (long)dx * dx + (long)dy * dy;
			int dot = dx * observer.Actor.FacingX + dy * observer.Actor.FacingY;
			long maxSq = dot >= 0 ? frontSq : rearSq;
			if (distSq > maxSq) continue;

			result.Add(other);
		}

		return result;
	}

	private static int CandidateScore(VisionActorSnapshot observer, VisionActorSnapshot target)
	{
		int dx = target.X - observer.X;
		int dy = target.Y - observer.Y;
		long distSq = (long)dx * dx + (long)dy * dy;
		int dot = dx * observer.Actor.FacingX + dy * observer.Actor.FacingY;
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

	/// <summary>
	/// 就地将 list 中 score 最小的 K 个元素移到前 K 位（部分排序）。
	/// 返回实际 top-K 数量（min(K, list.Count)）。
	/// O(N×K) 但 K 通常 ≤8，远快于 O(N log N) 全排序。
	/// </summary>
	private static int PartialSortTopK(List<VisionActorSnapshot> list, VisionActorSnapshot observer, int k)
	{
		var n = list.Count;
		if (n <= k)
		{
			list.Sort((a, b) => CandidateScore(observer, a).CompareTo(CandidateScore(observer, b)));
			return n;
		}

		// 选择排序前 K 个
		for (var i = 0; i < k; i++)
		{
			var bestIdx = i;
			var bestScore = CandidateScore(observer, list[i]);
			for (var j = i + 1; j < n; j++)
			{
				var score = CandidateScore(observer, list[j]);
				if (score < bestScore)
				{
					bestScore = score;
					bestIdx = j;
				}
			}
			if (bestIdx != i)
				(list[i], list[bestIdx]) = (list[bestIdx], list[i]);
		}
		return k;
	}
}
