using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Health;
using MiniRPG.Core.World;

namespace MiniRPG.Core.AI;

public sealed class TimelineSnapshotCache
{
	internal List<VisionActorSnapshot> Snapshots { get; } = new();
	internal Dictionary<string, VisionActorSnapshot> ById { get; } = new(StringComparer.Ordinal);
	public bool IsDirty { get; set; } = true;

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
	public int LosCacheHits { get; init; }
}

internal readonly record struct VisionActorSnapshot(
	Actor Actor,
	int X,
	int Y,
	int Z,
	string Faction,
	bool IsDead,
	float SightCapacity);

internal sealed class VisionLosCache
{
	private readonly Dictionary<long, bool> _cache = new();

	public bool TryGet(string idA, string idB, out bool visible)
	{
		var key = MakeKey(idA, idB);
		return _cache.TryGetValue(key, out visible);
	}

	public void Set(string idA, string idB, bool visible)
	{
		var key = MakeKey(idA, idB);
		_cache[key] = visible;
	}

	public int Count => _cache.Count;

	private static long MakeKey(string a, string b)
	{
		var ha = (long)(uint)a.GetHashCode();
		var hb = (long)(uint)b.GetHashCode();
		return ha <= hb ? (ha << 32) | hb : (hb << 32) | ha;
	}
}

internal sealed class CachedBlocksSightProvider : VisibilityUtil.IBlocksSightProvider
{
	private readonly WorldMap _world;
	private readonly Dictionary<long, bool> _cache = new();

	public CachedBlocksSightProvider(WorldMap world) => _world = world;

	public bool BlocksSight(int x, int y, int z)
	{
		var key = Pack(x, y, z);
		if (_cache.TryGetValue(key, out var cached))
			return cached;
		var result = _world.BlocksSight(x, y, z);
		_cache[key] = result;
		return result;
	}

	private static long Pack(int x, int y, int z) =>
		((long)(ushort)x << 32) | ((long)(ushort)y << 16) | (ushort)z;
}

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
		var maxVerticalLayers = Math.Max(0, config.MaxVerticalVisionLayers);
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
				var refreshStart = captureDetailedProfile ? ProfilingClock.Start() : 0L;
				cache.RefreshFromActors();
				if (captureDetailedProfile)
					candidateScanMs += ProfilingClock.ElapsedMs(refreshStart);
				actorSnapshots = cache.Snapshots;
				actorSnapshotsById = cache.ById;
			}
			else
			{
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

		var losCache = new VisionLosCache();
		var blocksSightCache = new CachedBlocksSightProvider(world);

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
				var candidates = CollectCandidates3D(
					state,
					observerSnapshot,
					actorSnapshots,
					actorSnapshotsById,
					request.Detail,
					config,
					maxVerticalLayers);
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

					if (losCache.TryGet(observer.Id, target.Actor.Id, out var cachedVisible))
					{
						if (cachedVisible)
							visibleActors.Add(target.Actor);
						continue;
					}

					bool canSeeTarget;
					if (captureDetailedProfile)
					{
						var losStart = ProfilingClock.Start();
						canSeeTarget = VisibilityUtil.HasLineOfSight3D(
							blocksSightCache,
							observerSnapshot.X,
							observerSnapshot.Y,
							observerSnapshot.Z,
							target.X,
							target.Y,
							target.Z);
						losMs += ProfilingClock.ElapsedMs(losStart);
					}
					else
					{
						canSeeTarget = VisibilityUtil.HasLineOfSight3D(
							blocksSightCache,
							observerSnapshot.X,
							observerSnapshot.Y,
							observerSnapshot.Z,
							target.X,
							target.Y,
							target.Z);
					}

					losCache.Set(observer.Id, target.Actor.Id, canSeeTarget);

					if (canSeeTarget)
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
			LosCacheHits = losCache.Count,
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
			var fixtureId = state.World!.GetFixtureId(x, y, observer.Z);
			if (!string.IsNullOrEmpty(fixtureId))
				nearbyFixtures[(x, y)] = fixtureId;
		}

		return nearbyFixtures;
	}

	private static List<VisionActorSnapshot> CollectCandidates3D(
		GameState state,
		VisionActorSnapshot observer,
		IReadOnlyList<VisionActorSnapshot> actorSnapshots,
		Dictionary<string, VisionActorSnapshot> snapshotsById,
		SimDetail detail,
		AIVisionConfig config,
		int maxVerticalLayers)
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
		int maxRange = Math.Max(frontRange, rearRange);

		var actorIndex = state.World?.Actors;
		var useIndex = actorIndex != null && state.World!.Chunks.LoadedChunks.Count > 0;
		if (useIndex)
		{
			var nearby = new List<Actor>();
			actorIndex!.CollectActorsInRange(
				observer.X, observer.Y, observer.Z,
				maxRange, maxVerticalLayers,
				state.Actors, nearby);

			if (nearby.Count > 0 || state.Actors.Count <= 1)
			{
				foreach (var actor in nearby)
				{
					if (actor.Id == observer.Actor.Id) continue;
					if (!snapshotsById.TryGetValue(actor.Id, out var other)) continue;
					if (other.IsDead) continue;

					int dx = other.X - observer.X;
					int dy = other.Y - observer.Y;
					int dz = other.Z - observer.Z;
					long distSq = (long)dx * dx + (long)dy * dy + (long)dz * dz;
					int dot = dx * observer.Actor.FacingX + dy * observer.Actor.FacingY;
					long maxSq = dot >= 0 ? frontSq : rearSq;
					if (distSq > maxSq) continue;

					result.Add(other);
				}
				return result;
			}
		}

		foreach (var other in actorSnapshots)
		{
			if (other.Actor.Id == observer.Actor.Id) continue;
			if (other.IsDead) continue;

			int dz = other.Z - observer.Z;
			if (Math.Abs(dz) > maxVerticalLayers) continue;

			int dx = other.X - observer.X;
			int dy = other.Y - observer.Y;
			long distSq = (long)dx * dx + (long)dy * dy + (long)dz * dz;
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
		int dz = target.Z - observer.Z;
		long distSq = (long)dx * dx + (long)dy * dy + (long)dz * dz;
		int dot = dx * observer.Actor.FacingX + dy * observer.Actor.FacingY;
		int hostilityPenalty = FactionRelation.IsHostile(observer.Faction, target.Faction) ? 0 : 10000;
		int rearPenalty = dot < 0 ? 2000 : 0;
		int crossLayerPenalty = dz != 0 ? 1000 : 0;

		return hostilityPenalty + rearPenalty + crossLayerPenalty + (int)Math.Min(distSq, int.MaxValue / 4);
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

	private static int PartialSortTopK(List<VisionActorSnapshot> list, VisionActorSnapshot observer, int k)
	{
		var n = list.Count;
		if (n == 0) return 0;

		var scores = new int[n];
		for (var i = 0; i < n; i++)
			scores[i] = CandidateScore(observer, list[i]);

		if (n <= k)
		{
			for (var i = 1; i < n; i++)
			{
				var key = scores[i];
				var keySnap = list[i];
				var j = i - 1;
				while (j >= 0 && scores[j] > key)
				{
					scores[j + 1] = scores[j];
					list[j + 1] = list[j];
					j--;
				}
				scores[j + 1] = key;
				list[j + 1] = keySnap;
			}
			return n;
		}

		for (var i = 0; i < k; i++)
		{
			var bestIdx = i;
			var bestScore = scores[i];
			for (var j = i + 1; j < n; j++)
			{
				if (scores[j] < bestScore)
				{
					bestScore = scores[j];
					bestIdx = j;
				}
			}
			if (bestIdx != i)
			{
				(list[i], list[bestIdx]) = (list[bestIdx], list[i]);
				(scores[i], scores[bestIdx]) = (scores[bestIdx], scores[i]);
			}
		}
		return k;
	}
}
