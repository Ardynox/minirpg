namespace MiniRPG.Core.World.Generators;

/// <summary>
/// Minimal editor-friendly generator: every chunk is a blank floor canvas.
/// </summary>
public sealed class BlankFloorGenerator : IMapGenerator
{
	public string Id => "blank_floor";
	public string Name => "Blank Floor";

	public void GenerateChunk(ChunkData chunk, int worldSeed)
	{
		if (chunk.Coord.Cz == 0)
			chunk.Fill(TerrainRegistry.GetId(Terrains.Floor));
		else
			chunk.Fill(TerrainRegistry.GetId(Terrains.Air));
		chunk.Entities.Clear();
		chunk.Nests.Clear();
		chunk.ActorIds.Clear();
		chunk.Dirty = false;
	}

	public void PopulateChunk(ChunkData chunk, int worldSeed)
	{
	}
}
