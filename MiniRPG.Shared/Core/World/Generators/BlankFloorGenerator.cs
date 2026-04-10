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
		chunk.Fill(TerrainRegistry.GetId(Terrains.Floor));
		chunk.Entities.Clear();
		chunk.Nests.Clear();
		chunk.ActorIds.Clear();
		chunk.Dirty = false;
	}

	public void PopulateChunk(ChunkData chunk, int worldSeed)
	{
	}
}
