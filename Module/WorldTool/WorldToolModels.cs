using Godot;
using MiniRPG.Core.Data;

namespace MiniRPG.Module.WorldTool;

public enum WorldToolMode
{
	Select,
	Build,
	Demolish,
}

public enum WorldToolCategory
{
	Terrain,
	Fixture,
	Facility,
	Environment,
}

internal enum WorldToolPreviewKind
{
	Terrain,
	Fixture,
	Facility,
}

internal readonly record struct WorldToolPreviewState(
	WorldToolMode ToolMode,
	WorldToolCategory Category,
	WorldToolPreviewKind Kind,
	Vector3I RawHoverCell,
	Vector3I? ResolvedTargetCell,
	string BrushId,
	string? BrushGlyph,
	bool CanApply,
	bool ShowGhost,
	bool ShowInfoOverlay,
	string? GhostRenderId,
	string? GhostGlyph,
	string? ResolvedEntityId,
	bool HideResolvedTargetInWorld,
	FacilityInstance? GhostFacility);
