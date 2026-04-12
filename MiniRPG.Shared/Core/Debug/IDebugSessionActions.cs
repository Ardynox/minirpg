namespace MiniRPG.Core.Debug;

/// <summary>
/// Abstraction over session-level actions that debug commands need,
/// so that Core.Debug does not depend on the concrete GameSessionModule.
/// </summary>
public interface IDebugSessionActions
{
	void ChangeFloor(bool goDown);
	string ExportPresetScenario(string id);
}
