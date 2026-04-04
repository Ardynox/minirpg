using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniRPG.Core.World;

/// <summary>
/// Chunk 管理器：负责 chunk 的按需生成、缓存、加载/卸载。
/// 围绕玩家位置维护一个加载窗口，超出范围的 chunk 被卸载（脏 chunk 先持久化）。
/// </summary>
public class ChunkManager
{
	private readonly Dictionary<ChunkCoord, ChunkData> _loaded = new();
	private IMapGenerator _generator;
	private readonly int _worldSeed;

	/// <summary>水平方向加载半径（chunk 为单位）。视口 21x11 ÷ 32 < 1，取 2 留余量。</summary>
	public int LoadRadiusXY { get; set; } = 2;

	/// <summary>Z 方向加载半径。多层预览需要 ±1。</summary>
	public int LoadRadiusZ { get; set; } = 1;

	/// <summary>缓存中保留的最大 chunk 数。超出后 LRU 卸载。</summary>
	public int MaxCachedChunks { get; set; } = 512;

	/// <summary>最小模拟器，对远离玩家的已加载 chunk 执行简化模拟。</summary>
	public IChunkSimulator Simulator { get; set; } = new NullSimulator();

	/// <summary>脏 chunk 持久化回调。由 SaveModule 设置。</summary>
	public Action<ChunkCoord, ChunkData>? OnChunkUnload { get; set; }

	/// <summary>尝试从持久化加载 chunk 的回调。返回 null = 无存档，需重新生成。</summary>
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
		LoadRadiusXY = config.ChunkLoadRadiusXy;
		LoadRadiusZ = config.ChunkLoadRadiusZ;
		MaxCachedChunks = config.MaxCachedChunks;
	}

	/// <summary>获取 chunk。已缓存则直接返回，否则尝试从存档加载或重新生成。</summary>
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

	/// <summary>根据玩家当前位置更新已加载 chunk 的集合。</summary>
	public void UpdateLoadedChunks(WorldCoord center, int currentTurn)
	{
		var cc = CoordUtil.WorldToChunk(center);

		for (var cz = cc.Cz - LoadRadiusZ; cz <= cc.Cz + LoadRadiusZ; cz++)
		for (var cy = cc.Cy - LoadRadiusXY; cy <= cc.Cy + LoadRadiusXY; cy++)
		for (var cx = cc.Cx - LoadRadiusXY; cx <= cc.Cx + LoadRadiusXY; cx++)
		{
			var coord = new ChunkCoord(cx, cy, cz);
			var chunk = GetOrLoad(coord);
			chunk.LastAccessTurn = currentTurn;
		}

		EvictDistant(cc, currentTurn);
	}

	/// <summary>卸载超出加载范围且超出缓存上限的 chunk（LRU 策略）。</summary>
	private void EvictDistant(ChunkCoord center, int currentTurn)
	{
		if (_loaded.Count <= MaxCachedChunks) return;

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

	/// <summary>每回合对已加载的远距离 chunk 执行最小模拟。</summary>
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

	/// <summary>检查指定 chunk 是否已加载。</summary>
	public bool IsLoaded(ChunkCoord coord) => _loaded.ContainsKey(coord);

	/// <summary>强制卸载所有 chunk（存盘时使用）。</summary>
	public void UnloadAll()
	{
		foreach (var (coord, chunk) in _loaded)
		{
			if (chunk.Dirty)
				OnChunkUnload?.Invoke(coord, chunk);
		}
		_loaded.Clear();
	}
}
