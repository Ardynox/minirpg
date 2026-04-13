using MiniRPG.Module.Editor;

namespace MiniRPG.Module.WorldTool;

internal static class MapEditorWorldToolPreviewAdapter
{
	public static WorldToolPreviewState? FromMapEditorHoverState(MapEditorHoverState? hoverState)
	{
		if (hoverState is not { } resolved)
			return null;

		return new WorldToolPreviewState(
			resolved.ToolMode switch
			{
				MapEditorToolMode.Select => WorldToolMode.Select,
				MapEditorToolMode.Demolish => WorldToolMode.Demolish,
				_ => WorldToolMode.Build,
			},
			resolved.Category switch
			{
				MapEditorBrushCategory.Fixture => WorldToolCategory.Fixture,
				MapEditorBrushCategory.Environment => WorldToolCategory.Environment,
				_ => WorldToolCategory.Terrain,
			},
			resolved.Kind switch
			{
				MapEditorHoverStateKind.Fixture => WorldToolPreviewKind.Fixture,
				_ => WorldToolPreviewKind.Terrain,
			},
			resolved.RawHoverCell,
			resolved.ResolvedTargetCell,
			resolved.BrushId,
			resolved.BrushGlyph,
			resolved.CanApply,
			resolved.ShowGhost,
			resolved.ShowInfoOverlay,
			resolved.GhostRenderId,
			resolved.GhostGlyph,
			resolved.Kind == MapEditorHoverStateKind.Fixture && resolved.HideResolvedTargetInWorld
				? resolved.GhostRenderId
				: null,
			resolved.HideResolvedTargetInWorld,
			null);
	}
}
