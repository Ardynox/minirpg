namespace MiniRPG.Core.World;

/// <summary>
/// 地形读写服务：所有地形 ID / 硬度的访问都通过此类。
/// </summary>
public class TerrainAccess
{
	private readonly ChunkManager _chunks;

	public TerrainAccess(ChunkManager chunks)
	{
		_chunks = chunks;
	}

	private (ChunkData Chunk, int Lx, int Ly) Resolve(int x, int y, int z)
	{
		var cc = CoordUtil.WorldToChunk(x, y, z);
		var (lx, ly) = CoordUtil.WorldToLocal(x, y);
		var chunk = _chunks.GetOrLoad(cc);
		return (chunk, lx, ly);
	}

	public ushort GetTerrainId(int x, int y, int z)
	{
		var (chunk, lx, ly) = Resolve(x, y, z);
		return chunk.GetTerrainId(lx, ly);
	}

	public TerrainDef GetTerrain(int x, int y, int z) =>
		TerrainRegistry.Get(GetTerrainId(x, y, z));

	public void SetTerrainId(int x, int y, int z, ushort terrainId)
	{
		var (chunk, lx, ly) = Resolve(x, y, z);
		if (chunk.GetTerrainId(lx, ly) == terrainId)
			return;

		chunk.SetTerrain(lx, ly, terrainId);
		MarkAdjacentTerrainGeometryDirty(chunk.Coord);
	}

	public void SetTerrain(int x, int y, int z, string terrainStringId)
	{
		var id = TerrainRegistry.GetId(terrainStringId);
		SetTerrainId(x, y, z, id);
	}

	public byte GetHardness(int x, int y, int z)
	{
		var (chunk, lx, ly) = Resolve(x, y, z);
		return chunk.GetHardness(lx, ly);
	}

	public void SetHardness(int x, int y, int z, byte hardness)
	{
		var (chunk, lx, ly) = Resolve(x, y, z);
		chunk.SetHardness(lx, ly, hardness);
	}

	private void MarkAdjacentTerrainGeometryDirty(ChunkCoord coord)
	{
		MarkLoadedChunkTerrainGeometryDirty(new ChunkCoord(coord.Cx - 1, coord.Cy, coord.Cz));
		MarkLoadedChunkTerrainGeometryDirty(new ChunkCoord(coord.Cx + 1, coord.Cy, coord.Cz));
		MarkLoadedChunkTerrainGeometryDirty(new ChunkCoord(coord.Cx, coord.Cy - 1, coord.Cz));
		MarkLoadedChunkTerrainGeometryDirty(new ChunkCoord(coord.Cx, coord.Cy + 1, coord.Cz));
		MarkLoadedChunkTerrainGeometryDirty(new ChunkCoord(coord.Cx, coord.Cy, coord.Cz - 1));
		MarkLoadedChunkTerrainGeometryDirty(new ChunkCoord(coord.Cx, coord.Cy, coord.Cz + 1));
	}

	private void MarkLoadedChunkTerrainGeometryDirty(ChunkCoord coord)
	{
		if (_chunks.LoadedChunks.TryGetValue(coord, out var chunk))
			chunk.BumpTerrainGeometryRevision();
	}
}
