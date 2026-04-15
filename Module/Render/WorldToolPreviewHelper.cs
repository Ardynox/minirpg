using System;
using Godot;
using MiniRPG.Module.Editor;
using MiniRPG.Module.WorldTool;

namespace MiniRPG.Module.Render;

internal static class WorldToolPreviewHelper
{
	public static Vector3I? ResolveHoverHighlightCell(
		Vector3I? hoverWorldCell,
		WorldToolPreviewState? previewState)
	{
		if (previewState is not { } preview)
			return hoverWorldCell;

		return preview.ToolMode switch
		{
			WorldToolMode.Build => preview.ResolvedTargetCell ?? hoverWorldCell,
			WorldToolMode.Select => preview.ResolvedTargetCell ?? hoverWorldCell,
			_ => preview.ResolvedTargetCell,
		};
	}

	public static Vector3I? ResolveHoverHighlightCell(
		bool editorViewActive,
		Vector3I? hoverWorldCell,
		MapEditorHoverState? editorHoverState) =>
		editorViewActive
			? ResolveHoverHighlightCell(hoverWorldCell, MapEditorWorldToolPreviewAdapter.FromMapEditorHoverState(editorHoverState))
			: hoverWorldCell;

	public static bool ShouldDrawPlacementGhost(WorldToolPreviewState? hoverState) =>
		hoverState is
		{
			CanApply: true,
			ShowGhost: true,
			ResolvedTargetCell: { }
		}
		&& (hoverState.Value.Kind != WorldToolPreviewKind.Facility
			? !string.IsNullOrWhiteSpace(hoverState.Value.GhostRenderId)
			: hoverState.Value.GhostFacility != null);

	public static bool ShouldDrawEditorPlacementGhost(MapEditorHoverState? hoverState) =>
		ShouldDrawPlacementGhost(MapEditorWorldToolPreviewAdapter.FromMapEditorHoverState(hoverState));

	public static bool ShouldHidePreviewTerrain(WorldToolPreviewState? hoverState, int worldX, int worldY, int worldZ) =>
		ShouldHidePreviewTarget(
			hoverState,
			WorldToolPreviewKind.Terrain,
			worldX,
			worldY,
			worldZ);

	public static bool ShouldHideEditorPreviewTerrain(MapEditorHoverState? hoverState, int worldX, int worldY, int worldZ) =>
		ShouldHidePreviewTerrain(MapEditorWorldToolPreviewAdapter.FromMapEditorHoverState(hoverState), worldX, worldY, worldZ);

	public static bool ShouldHidePreviewFixture(WorldToolPreviewState? hoverState, int worldX, int worldY, int worldZ, string entityId) =>
		ShouldHidePreviewTarget(
			hoverState,
			WorldToolPreviewKind.Fixture,
			worldX,
			worldY,
			worldZ,
			entityId);

	public static bool ShouldHideEditorPreviewFixture(MapEditorHoverState? hoverState, int worldX, int worldY, int worldZ, string entityId) =>
		ShouldHidePreviewFixture(MapEditorWorldToolPreviewAdapter.FromMapEditorHoverState(hoverState), worldX, worldY, worldZ, entityId);

	public static bool ShouldHidePreviewFacility(WorldToolPreviewState? hoverState, string facilityId) =>
		hoverState is
		{
			HideResolvedTargetInWorld: true,
			Kind: WorldToolPreviewKind.Facility,
			ResolvedEntityId: { Length: > 0 } resolvedEntityId
		}
		&& string.Equals(resolvedEntityId, facilityId, StringComparison.Ordinal);

	public static int GetPlacementGhostCommandCount(WorldToolPreviewState? hoverState)
	{
		if (hoverState is not { } resolved || !ShouldDrawPlacementGhost(resolved))
			return 0;

		return resolved.Kind == WorldToolPreviewKind.Terrain ? 3 : 1;
	}

	public static int GetEditorPlacementGhostCommandCount(MapEditorHoverState? hoverState) =>
		GetPlacementGhostCommandCount(MapEditorWorldToolPreviewAdapter.FromMapEditorHoverState(hoverState));

	internal static bool ShouldHidePreviewTarget(
		WorldToolPreviewState? hoverState,
		WorldToolPreviewKind kind,
		int worldX,
		int worldY,
		int worldZ,
		string? entityId = null)
	{
		if (hoverState is not
			{
				HideResolvedTargetInWorld: true,
				Kind: var hoverKind,
				ResolvedTargetCell: { } targetCell
			} || hoverKind != kind)
		{
			return false;
		}

		if (targetCell.X != worldX || targetCell.Y != worldY || targetCell.Z != worldZ)
			return false;

		return kind != WorldToolPreviewKind.Fixture ||
			(!string.IsNullOrWhiteSpace(entityId) &&
			 string.Equals(hoverState.Value.ResolvedEntityId, entityId, StringComparison.Ordinal));
	}
}
