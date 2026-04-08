using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniRPG.Core.World;

/// <summary>
/// Manages loaded chunks around the player. Critical chunks are loaded immediately,
/// while the rest of the target window is streamed in over later frames.
/// </summary>
public class ChunkManager
{
	private readonly Dictionary<ChunkCoord, ChunkData> _loaded = new();
	private readonly Queue<ChunkCoord> _pendingLoadQueue = new();
	private readonly int _worldSeed;
	private IMapGenerator _generator;

	public int LoadRadiusXY { get; set; } = 2;
	public int LoadRadiusZ { get; set; } = 1;
	public int CriticalLoadRadiusXY { get; set; } = 1;
	public int BackgroundLoadBudgetPerFrame { get; set; } = 2;
	public int MaxCachedChunks { get; set; } = 512;

	public IChunkSimulator Simulator { get; set; } = new NullSimulator();
	public Action<ChunkCoord, ChunkData>? OnChunkUnload { get; set; }
	public Func<ChunkCoord, ChunkData?>? OnChunkLoad { get; set; }

	public IReadOnlyDictionary<ChunkCoord, ChunkData> LoadedChunks => _loaded;

	public ChunkManager(int worldSeed, IMapGenerator generator)
	{
		_worldSeed = worldSeed;
		_generator = generator;
	}

	public void SetGenerator(IMapGenerator gen) => _generator = gen;

	public void ApplyRuntimeConfig(WorldRuntimeConfig config)
	{
		LoadRadiusXY = Math.Max(0, config.ChunkLoadRadiusXy);
		LoadRadiusZ = Math.Max(0, config.ChunkLoadRadiusZ);
		CriticalLoadRadiusXY = Math.Clamp(config.CriticalChunkLoadRadiusXy, 0, LoadRadiusXY);
		BackgroundLoadBudgetPerFrame = Math.Max(0, config.ChunkBackgroundLoadBudgetPerFrame);
		MaxCachedChunks = Math.Max(1, config.MaxCachedChunks);
	}

	public ChunkData GetOrLoad(ChunkCoord coord)
	{
		if (_loaded.TryGetValue(coord, out var existing))
			return existing;

		var chunk = OnChunkLoad?.Invoke(coord);
		if (chunk == null)
		{
			chunk = new ChunkData { Coord = coord };
			_generator.GenerateChunk(chunk, _worldSeed);
			_generator.PopulateChunk(chunk, _worldSeed);
		}

		_loaded[coord] = chunk;
		return chunk;
	}

	public void UpdateLoadedChunks(WorldCoord center, int currentTurn)
	{
		var chunkCenter = CoordUtil.WorldToChunk(center);
		LoadCriticalWindow(chunkCenter, currentTurn);
		RebuildPendingLoadQueue(chunkCenter, currentTurn);
		EvictDistant(chunkCenter);
	}

	public void ProcessPendingLoads(int currentTurn, int? budgetOverride = null)
	{
		var budget = Math.Max(0, budgetOverride ?? BackgroundLoadBudgetPerFrame);
		while (budget > 0 && _pendingLoadQueue.Count > 0)
		{
			var coord = _pendingLoadQueue.Dequeue();
			var chunk = GetOrLoad(coord);
			chunk.LastAccessTurn = currentTurn;
			budget--;
		}
	}

	public void TickSimulation(WorldCoord playerPos, GameState state)
	{
		var pc = CoordUtil.WorldToChunk(playerPos);
		var nearRadius = Math.Max(0, GameConfig.WorldRuntime.ChunkSimulationNearRadiusXy);
		foreach (var (coord, chunk) in _loaded)
		{
			var dx = Math.Abs(coord.Cx - pc.Cx);
			var dy = Math.Abs(coord.Cy - pc.Cy);
			if (dx > nearRadius || dy > nearRadius)
				Simulator.TickChunk(chunk, state);
		}
	}

	public bool IsLoaded(ChunkCoord coord) => _loaded.ContainsKey(coord);

	public void UnloadAll()
	{
		foreach (var (coord, chunk) in _loaded)
		{
			if (chunk.Dirty)
				OnChunkUnload?.Invoke(coord, chunk);
		}

		_loaded.Clear();
		_pendingLoadQueue.Clear();
	}

	private void LoadCriticalWindow(ChunkCoord center, int currentTurn)
	{
		var criticalRadius = Math.Min(CriticalLoadRadiusXY, LoadRadiusXY);
		for (var cy = center.Cy - criticalRadius; cy <= center.Cy + criticalRadius; cy++)
		for (var cx = center.Cx - criticalRadius; cx <= center.Cx + criticalRadius; cx++)
		{
			var coord = new ChunkCoord(cx, cy, center.Cz);
			var chunk = GetOrLoad(coord);
			chunk.LastAccessTurn = currentTurn;
		}
	}

	private void RebuildPendingLoadQueue(ChunkCoord center, int currentTurn)
	{
		_pendingLoadQueue.Clear();

		foreach (var coord in EnumerateBackgroundWindow(center))
		{
			if (_loaded.TryGetValue(coord, out var loaded))
			{
				loaded.LastAccessTurn = currentTurn;
				continue;
			}

			_pendingLoadQueue.Enqueue(coord);
		}
	}

	private IEnumerable<ChunkCoord> EnumerateBackgroundWindow(ChunkCoord center)
	{
		var queued = new List<(ChunkCoord Coord, int Priority)>();
		for (var cz = center.Cz - LoadRadiusZ; cz <= center.Cz + LoadRadiusZ; cz++)
		for (var cy = center.Cy - LoadRadiusXY; cy <= center.Cy + LoadRadiusXY; cy++)
		for (var cx = center.Cx - LoadRadiusXY; cx <= center.Cx + LoadRadiusXY; cx++)
		{
			var dx = Math.Abs(cx - center.Cx);
			var dy = Math.Abs(cy - center.Cy);
			var dz = Math.Abs(cz - center.Cz);
			if (dz == 0 && dx <= CriticalLoadRadiusXY && dy <= CriticalLoadRadiusXY)
				continue;

			var priority = dz * 100 + dx + dy;
			queued.Add((new ChunkCoord(cx, cy, cz), priority));
		}

		queued.Sort((a, b) => a.Priority.CompareTo(b.Priority));
		foreach (var (coord, _) in queued)
			yield return coord;
	}

	private void EvictDistant(ChunkCoord center)
	{
		if (_loaded.Count <= MaxCachedChunks)
			return;

		var evictRadius = LoadRadiusXY + Math.Max(0, GameConfig.WorldRuntime.ChunkEvictPaddingXy);
		var toEvict = new List<ChunkCoord>();

		foreach (var (coord, chunk) in _loaded)
		{
			var dx = Math.Abs(coord.Cx - center.Cx);
			var dy = Math.Abs(coord.Cy - center.Cy);
			var dz = Math.Abs(coord.Cz - center.Cz);

			if (dx > evictRadius || dy > evictRadius || dz > LoadRadiusZ + 1)
				toEvict.Add(coord);
		}

		if (_loaded.Count - toEvict.Count > MaxCachedChunks)
		{
			var remaining = _loaded
				.Where(kv => !toEvict.Contains(kv.Key))
				.OrderBy(kv => kv.Value.LastAccessTurn)
				.Select(kv => kv.Key)
				.Take(_loaded.Count - toEvict.Count - MaxCachedChunks);
			toEvict.AddRange(remaining);
		}

		foreach (var coord in toEvict)
		{
			if (_loaded.TryGetValue(coord, out var chunk))
			{
				if (chunk.Dirty)
					OnChunkUnload?.Invoke(coord, chunk);
				_loaded.Remove(coord);
			}
		}
	}
}
