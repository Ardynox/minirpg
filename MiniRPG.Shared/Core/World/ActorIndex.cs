using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.World;

/// <summary>
/// Actor 空间索引服务：管理 Actor 在 Chunk 中的注册和空间查询。
/// </summary>
public class ActorIndex
{
	private readonly ChunkManager _chunks;

	public ActorIndex(ChunkManager chunks)
	{
		_chunks = chunks;
	}

	/// <summary>将 Actor 注册到其所在 chunk。</summary>
	public void RegisterActor(Actor actor)
	{
		var cc = CoordUtil.WorldToChunk(actor.X, actor.Y, actor.Z);
		if (_chunks.IsLoaded(cc))
			_chunks.GetOrLoad(cc).ActorIds.Add(actor.Id);
	}

	/// <summary>Actor 移动后更新 chunk 注册。</summary>
	public void UpdateActorChunk(Actor actor, int oldX, int oldY, int oldZ)
	{
		var oldCc = CoordUtil.WorldToChunk(oldX, oldY, oldZ);
		var newCc = CoordUtil.WorldToChunk(actor.X, actor.Y, actor.Z);
		if (oldCc == newCc) return;

		if (_chunks.IsLoaded(oldCc))
			_chunks.GetOrLoad(oldCc).ActorIds.Remove(actor.Id);
		if (_chunks.IsLoaded(newCc))
			_chunks.GetOrLoad(newCc).ActorIds.Add(actor.Id);
	}

	/// <summary>从 chunk 中注销 Actor。</summary>
	public void UnregisterActor(Actor actor)
	{
		var cc = CoordUtil.WorldToChunk(actor.X, actor.Y, actor.Z);
		if (_chunks.IsLoaded(cc))
			_chunks.GetOrLoad(cc).ActorIds.Remove(actor.Id);
	}

	/// <summary>
	/// 重新构建当前已加载 chunk 的 actor 索引。
	/// 主要用于读档后恢复了 state.Actors，但 WorldMap 刚重建的场景。
	/// </summary>
	public void RebuildLoadedActorIndex(Dictionary<string, Actor> actors)
	{
		foreach (var chunk in _chunks.LoadedChunks.Values)
			chunk.ActorIds.Clear();

		foreach (var actor in actors.Values)
			RegisterActor(actor);
	}

	/// <summary>
	/// 按坐标查询 Actor。
	/// 已加载 chunk 优先走 chunk.ActorIds 索引；未加载 chunk 回退为全表扫描。
	/// </summary>
	public List<Actor> GetActorsAt(int x, int y, int z, Dictionary<string, Actor> actors)
	{
		if (actors.Count == 0)
			return [];

		var coord = CoordUtil.WorldToChunk(x, y, z);
		if (!_chunks.IsLoaded(coord))
			return ScanActorsAt(x, y, z, actors);

		var chunk = _chunks.GetOrLoad(coord);
		if (chunk.ActorIds.Count == 0)
			return [];

		var result = new List<Actor>(chunk.ActorIds.Count);
		foreach (var actorId in chunk.ActorIds)
		{
			if (!actors.TryGetValue(actorId, out var actor))
				continue;

			if (actor.X == x && actor.Y == y && actor.Z == z)
				result.Add(actor);
		}

		result.Sort(static (left, right) => string.Compare(left.Id, right.Id, StringComparison.Ordinal));
		return result;
	}

	public Actor? GetActorAt(int x, int y, int z, Dictionary<string, Actor> actors) =>
		GetActorsAt(x, y, z, actors).FirstOrDefault();

	public Actor? GetHostileActorAt(int x, int y, int z, Dictionary<string, Actor> actors) =>
		GetActorsAt(x, y, z, actors).FirstOrDefault(static actor => actor.Faction == Factions.Hostile);

	public void CollectActorsInRange(
		int cx, int cy, int cz,
		int rangeXY, int rangeZ,
		Dictionary<string, Actor> actors,
		List<Actor> result)
	{
		var minChunkX = CoordUtil.WorldToChunkAxis(cx - rangeXY);
		var maxChunkX = CoordUtil.WorldToChunkAxis(cx + rangeXY);
		var minChunkY = CoordUtil.WorldToChunkAxis(cy - rangeXY);
		var maxChunkY = CoordUtil.WorldToChunkAxis(cy + rangeXY);
		var minZ = cz - rangeZ;
		var maxZ = cz + rangeZ;

		for (var chunkZ = minZ; chunkZ <= maxZ; chunkZ++)
		for (var chunkY = minChunkY; chunkY <= maxChunkY; chunkY++)
		for (var chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
		{
			var coord = new ChunkCoord(chunkX, chunkY, chunkZ);
			if (!_chunks.IsLoaded(coord)) continue;
			var chunk = _chunks.GetOrLoad(coord);
			if (chunk.ActorIds.Count == 0) continue;
			foreach (var actorId in chunk.ActorIds)
			{
				if (!actors.TryGetValue(actorId, out var actor)) continue;
				result.Add(actor);
			}
		}
	}

	private static List<Actor> ScanActorsAt(int x, int y, int z, Dictionary<string, Actor> actors) =>
		actors.Values
			.Where(actor => actor.X == x && actor.Y == y && actor.Z == z)
			.OrderBy(static actor => actor.Id, StringComparer.Ordinal)
			.ToList();
}
