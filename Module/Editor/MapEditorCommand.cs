using MiniRPG.Core.Data;
using MiniRPG.Core.World;

namespace MiniRPG.Module.Editor;

public interface IMapEditorCommand
{
	void Execute(WorldMap world);
	void Undo(WorldMap world);
}

public sealed class SetTerrainCommand(int x, int y, int z, string newTerrainId, string oldTerrainId) : IMapEditorCommand
{
	public void Execute(WorldMap world) => world.SetTerrain(x, y, z, newTerrainId);
	public void Undo(WorldMap world) => world.SetTerrain(x, y, z, oldTerrainId);
}

public sealed class SetFixtureCommand(int x, int y, int z, string newFixtureId, string newGlyph, string oldFixtureId, string oldGlyph) : IMapEditorCommand
{
	public void Execute(WorldMap world) => world.SetFixture(x, y, z, newGlyph, newFixtureId);
	public void Undo(WorldMap world) => world.SetFixture(x, y, z, oldGlyph, oldFixtureId);
}

public sealed class EraseTerrainCommand(int x, int y, int z, string oldTerrainId) : IMapEditorCommand
{
	public void Execute(WorldMap world) => world.SetTerrain(x, y, z, Terrains.Air);
	public void Undo(WorldMap world) => world.SetTerrain(x, y, z, oldTerrainId);
}

public sealed class EraseFixtureCommand(int x, int y, int z, string oldFixtureId, string oldGlyph) : IMapEditorCommand
{
	public void Execute(WorldMap world) => world.SetFixture(x, y, z, string.Empty, string.Empty);
	public void Undo(WorldMap world) => world.SetFixture(x, y, z, oldGlyph, oldFixtureId);
}
