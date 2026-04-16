using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using MiniRPG.Module.WorldTool;

namespace MiniRPG.Module.Render;

public partial class IsometricVoxelRenderer
{
	private const string HoverDiamondFillTextureKey = "hover_diamond_fill";
	private const string HoverDiamondOutlineTextureKey = "hover_diamond_outline";
	private const string HoverSideLeftFillTextureKey = "hover_side_left_fill";
	private const string HoverSideRightFillTextureKey = "hover_side_right_fill";
	private const string HoverEdgeSegmentTextureKey = "hover_edge_segment";
	internal const float EditorPlacementGhostAlpha = 0.30f;
	private static readonly Color HoverCellTint = new(0.92f, 0.82f, 0.50f, 0.08f);
	private static readonly Color HoverCellOutlineTint = new(0.92f, 0.82f, 0.50f, 0.70f);
	private static readonly Color HoverWallTint = new(0.92f, 0.82f, 0.50f, 0.38f);
	private static readonly HoverHighlightStyle DefaultHoverHighlightStyle = new(HoverCellTint, HoverCellOutlineTint, HoverWallTint);
	private static readonly HoverHighlightStyle EditorBuildPlaceableHoverHighlightStyle = new(
		new Color(0.96f, 0.87f, 0.56f, 0.18f),
		new Color(0.97f, 0.88f, 0.58f, 0.90f),
		new Color(0.96f, 0.87f, 0.56f, 0.50f));
	private static readonly HoverHighlightStyle EditorBuildBlockedHoverHighlightStyle = new(
		new Color(0.90f, 0.80f, 0.48f, 0.06f),
		new Color(0.90f, 0.80f, 0.48f, 0.52f),
		new Color(0.90f, 0.80f, 0.48f, 0.24f));
	private static readonly HoverHighlightStyle EditorSelectHoverHighlightStyle = new(
		new Color(0.78f, 0.88f, 0.96f, 0.16f),
		new Color(0.80f, 0.90f, 0.98f, 0.86f),
		new Color(0.78f, 0.88f, 0.96f, 0.46f));
	private static readonly HoverHighlightStyle EditorDemolishHoverHighlightStyle = new(
		new Color(0.96f, 0.72f, 0.40f, 0.12f),
		new Color(0.97f, 0.74f, 0.42f, 0.82f),
		new Color(0.96f, 0.72f, 0.40f, 0.42f));
	private static readonly HoverHighlightStyle PathPreviewHighlightStyle = new(
		new Color(0.45f, 0.85f, 0.70f, 0.10f),
		new Color(0.50f, 0.90f, 0.75f, 0.55f),
		new Color(0.45f, 0.85f, 0.70f, 0.30f));
	private static readonly Vector2 HoverCellFillScale = new(1.96f, 0.98f);
	private static readonly Vector2 HoverCellOutlineScale = new(2.04f, 1.02f);

	private readonly List<HoverHighlightCommand> _highlightCommands = [];
	private int _highlightCommandCount;

	public Vector3I? HoverWorldCell { get; set; }
	public Vector3I? TargetCursorWorldCell { get; set; }
	public IReadOnlyList<Godot.Vector3I>? PathHighlightCells { get; set; }

	private static HoverHighlightStyle ResolveHoverHighlightStyle(WorldToolPreviewState? previewState)
	{
		if (previewState is not { } hoverState)
			return DefaultHoverHighlightStyle;

		return hoverState.ToolMode switch
		{
			WorldToolMode.Select => EditorSelectHoverHighlightStyle,
			WorldToolMode.Demolish => EditorDemolishHoverHighlightStyle,
			_ => hoverState.CanApply
				? EditorBuildPlaceableHoverHighlightStyle
				: EditorBuildBlockedHoverHighlightStyle,
		};
	}

	private void CollectHoverHighlights(
		int cx,
		int cy,
		int cz,
		int halfW,
		int halfH,
		int zMin,
		int zMax,
		Rect2? visibleMapRect,
		WorldToolPreviewState? previewState)
	{
		_highlightCommands.Clear();
		_highlightCommandCount = 0;
		var targetCursorCell = TargetCursorWorldCell;
		var allowHighlightWithoutVision = !_editorViewActive && previewState != null;
		var hoverCell = ResolveHoverHighlightCell(HoverWorldCell, previewState);
		TryAddHoverHighlightCommand(
			hoverCell,
			targetCursorCell is { } cursor && cursor == hoverCell
				? EditorSelectHoverHighlightStyle
				: ResolveHoverHighlightStyle(previewState),
			previewState,
			allowHighlightWithoutVision,
			cx,
			cy,
			halfW,
			halfH,
			zMin,
			zMax,
			visibleMapRect);
		if (targetCursorCell is { } targetCell && targetCell != hoverCell)
		{
			TryAddHoverHighlightCommand(
				targetCell,
				EditorSelectHoverHighlightStyle,
				previewState: null,
				allowHighlightWithoutVision: false,
				cx,
				cy,
				halfW,
				halfH,
				zMin,
				zMax,
				visibleMapRect);
		}

		if (PathHighlightCells is { Count: > 0 } pathCells)
		{
			for (var i = 0; i < pathCells.Count; i++)
			{
				var pathCell = pathCells[i];
				if (targetCursorCell is { } tc && tc.X == pathCell.X && tc.Y == pathCell.Y && tc.Z == pathCell.Z)
					continue;
				if (hoverCell is { } hc && hc.X == pathCell.X && hc.Y == pathCell.Y && hc.Z == pathCell.Z)
					continue;
				TryAddHoverHighlightCommand(
					pathCell,
					PathPreviewHighlightStyle,
					previewState: null,
					allowHighlightWithoutVision: false,
					cx,
					cy,
					halfW,
					halfH,
					zMin,
					zMax,
					visibleMapRect);
			}
		}

		_highlightCommandCount = _highlightCommands.Count + GetPlacementGhostCommandCount(previewState);
	}

	private void TryAddHoverHighlightCommand(
		Vector3I? cell,
		HoverHighlightStyle style,
		WorldToolPreviewState? previewState,
		bool allowHighlightWithoutVision,
		int cx,
		int cy,
		int halfW,
		int halfH,
		int zMin,
		int zMax,
		Rect2? visibleMapRect)
	{
		if (cell is not { } hover)
			return;
		if (Math.Abs(hover.X - cx) > halfW || Math.Abs(hover.Y - cy) > halfH)
			return;
		if (hover.Z < zMin || hover.Z > zMax)
			return;
		if (!allowHighlightWithoutVision
			&& _fogTracker.GetVisionBand(hover.X, hover.Y, hover.Z) == PlayerVisionBand.Unknown)
		{
			return;
		}

		var basePos = IsoCoordUtil.WorldToScreen(hover.X, hover.Y, hover.Z);
		if (visibleMapRect is { } mapRect && !VoxelViewportMath.IsVoxelScreenVisible(basePos, mapRect))
			return;

		_highlightCommands.Add(new HoverHighlightCommand(
			basePos,
			hover,
			style,
			IsoCoordUtil.SortKey(hover.X, hover.Y, hover.Z) + 9000 + _highlightCommands.Count,
			previewState));
	}

	private void RenderHoverHighlights()
	{
		var pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() / 320.0f));
		for (var i = 0; i < _highlightCommands.Count; i++)
		{
			var command = _highlightCommands[i];
			if (command.PreviewState is { } previewState && ShouldDrawPlacementGhost(previewState))
				DrawPlacementGhost(command.ScreenPos, previewState);

			var geometry = BuildHoverVolumeGeometry(command.ScreenPos);
			var fillTint = ApplyEditorPerspectiveAlpha(
				command.Style.FillTint * new Color(1f, 1f, 1f, pulse),
				command.WorldCell.X, command.WorldCell.Y, command.WorldCell.Z);
			var sideTint = ApplyEditorPerspectiveAlpha(
				command.Style.SideTint * new Color(1f, 1f, 1f, 0.75f + pulse * 0.25f),
				command.WorldCell.X, command.WorldCell.Y, command.WorldCell.Z);
			var edgeTint = ApplyEditorPerspectiveAlpha(
				command.Style.OutlineTint * new Color(1f, 1f, 1f, 0.65f + pulse * 0.35f),
				command.WorldCell.X, command.WorldCell.Y, command.WorldCell.Z);
			DrawHoverVolumeFaces(geometry.TopCenter, sideTint);
			DrawHoverVolumeEdges(geometry, edgeTint);
			DrawHoverDiamond(command.ScreenPos, edgeTint, HoverCellOutlineScale, zIndex: 3, textureKey: HoverDiamondOutlineTextureKey);
			DrawHoverDiamond(command.ScreenPos, fillTint, HoverCellFillScale, zIndex: 4, textureKey: HoverDiamondFillTextureKey);
		}
	}

	private void DrawPlacementGhost(Vector2 screenPos, WorldToolPreviewState hoverState)
	{
		switch (hoverState.Kind)
		{
			case WorldToolPreviewKind.Terrain:
				if (string.IsNullOrWhiteSpace(hoverState.GhostRenderId))
					return;
				DrawTerrainPlacementGhost(screenPos, hoverState.GhostRenderId);
				break;
			case WorldToolPreviewKind.Fixture:
				if (string.IsNullOrWhiteSpace(hoverState.GhostRenderId))
					return;
				DrawFixturePlacementGhost(screenPos, hoverState.GhostRenderId, hoverState.GhostGlyph);
				break;
			case WorldToolPreviewKind.Facility:
				DrawFacilityPlacementGhost(hoverState.GhostFacility);
				break;
		}
	}

	private void DrawTerrainPlacementGhost(Vector2 screenPos, string? terrainId)
	{
		if (string.IsNullOrWhiteSpace(terrainId))
			return;

		var terrain = TerrainRegistry.Get(terrainId);
		var atlasTexture = _terrainAtlas.AtlasTexture;
		if (terrain == null || atlasTexture == null)
			return;
		if (terrain.StringId is Terrains.Air or Terrains.Void)
			return;
		if (!_terrainAtlas.TryGetRegions(terrain.StringId, out var regions))
			return;

		var tint = new Color(1f, 1f, 1f, EditorPlacementGhostAlpha);
		DrawAtlasRegionSprite(atlasTexture, regions.Left, ResolveLeftFacePosition(screenPos), tint, zIndex: 1);
		DrawAtlasRegionSprite(atlasTexture, regions.Right, ResolveRightFacePosition(screenPos), tint, zIndex: 1);
		DrawAtlasRegionSprite(atlasTexture, regions.Top, screenPos, tint, zIndex: 2);
	}

	private void DrawFixturePlacementGhost(Vector2 screenPos, string? entityId, string? glyph)
	{
		if (string.IsNullOrWhiteSpace(entityId))
			return;

		var tint = new Color(1f, 1f, 1f, EditorPlacementGhostAlpha);
		if (TryDrawWorldEntitySprite(screenPos, entityId, tint))
			return;

		DrawEntityMarker(screenPos, glyph ?? entityId, tint);
	}

	private void DrawFacilityPlacementGhost(FacilityInstance? facility)
	{
		if (facility == null || _state.World == null)
			return;

		var footprint = _state.World.GetFootprintCells(facility);
		if (footprint.Count == 0)
			return;

		var screenPos = ResolveFacilityFootprintScreenCenter(footprint);
		var tint = new Color(1f, 1f, 1f, EditorPlacementGhostAlpha);
		if (TryDrawFacilitySprite(screenPos, facility, tint))
			return;

		var def = FacilityRegistry.Get(facility.FacilityDefId);
		DrawEntityMarker(screenPos, def?.Glyph ?? facility.FacilityDefId, tint);
	}

	private void DrawHoverVolumeFaces(Vector2 cellPos, Color tint)
	{
		DrawHoverSideFace(ResolveLeftFacePosition(cellPos), tint, HoverSideLeftFillTextureKey, zIndex: 1);
		DrawHoverSideFace(ResolveRightFacePosition(cellPos), tint, HoverSideRightFillTextureKey, zIndex: 1);
	}

	private void DrawHoverVolumeEdges(HoverVolumeGeometry geometry, Color tint)
	{
		DrawHoverEdge(geometry.Left, geometry.LowerLeft, tint, zIndex: 2);
		DrawHoverEdge(geometry.Bottom, geometry.LowerBottom, tint, zIndex: 2);
		DrawHoverEdge(geometry.Right, geometry.LowerRight, tint, zIndex: 2);
		DrawHoverEdge(geometry.LowerLeft, geometry.LowerBottom, tint, zIndex: 2);
		DrawHoverEdge(geometry.LowerBottom, geometry.LowerRight, tint, zIndex: 2);
	}

	private void DrawHoverSideFace(Vector2 pos, Color tint, string textureKey, int zIndex)
	{
		var sprite = AcquireSprite();
		sprite.Centered = true;
		sprite.Texture = GetEntityMarkerTexture(textureKey);
		sprite.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
		sprite.RegionEnabled = false;
		sprite.Skew = 0f;
		sprite.Scale = Vector2.One;
		sprite.Position = pos;
		sprite.Rotation = 0f;
		sprite.ZIndex = zIndex;
		sprite.Modulate = tint;
		sprite.Visible = true;
	}

	private void DrawHoverEdge(Vector2 from, Vector2 to, Color tint, int zIndex)
	{
		var mid = (from + to) * 0.5f;
		var dir = to - from;
		var len = dir.Length();
		if (len < 1f)
			return;

		var sprite = AcquireSprite();
		sprite.Centered = true;
		sprite.Texture = GetEntityMarkerTexture(HoverEdgeSegmentTextureKey);
		sprite.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
		sprite.RegionEnabled = false;
		sprite.Skew = 0f;
		sprite.Scale = new Vector2(len / 32f, 1f);
		sprite.Position = mid;
		sprite.Rotation = dir.Angle();
		sprite.ZIndex = zIndex;
		sprite.Modulate = tint;
		sprite.Visible = true;
	}

	private void DrawHoverDiamond(Vector2 pos, Color tint, Vector2 scale, int zIndex, string textureKey)
	{
		var sprite = AcquireSprite();
		sprite.Centered = true;
		sprite.Texture = GetEntityMarkerTexture(textureKey);
		sprite.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
		sprite.RegionEnabled = false;
		sprite.Scale = scale;
		sprite.Position = pos;
		sprite.Skew = 0f;
		sprite.ZIndex = zIndex;
		sprite.Modulate = tint;
		sprite.Visible = true;
	}

	private void DrawAtlasRegionSprite(Texture2D texture, Rect2 sourceRegion, Vector2 position, Color tint, int zIndex)
	{
		var sprite = AcquireSprite();
		sprite.Centered = true;
		sprite.Texture = texture;
		sprite.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
		sprite.RegionEnabled = true;
		sprite.RegionRect = sourceRegion;
		sprite.Scale = Vector2.One;
		sprite.Position = position;
		sprite.Skew = 0f;
		sprite.Rotation = 0f;
		sprite.ZIndex = zIndex;
		sprite.Modulate = tint;
		sprite.Visible = true;
	}

	private bool TryBuildHoverMarkerTexture(string label, out ImageTexture texture)
	{
		switch (label)
		{
			case HoverDiamondFillTextureKey:
				texture = BuildHoverDiamondTexture(outline: false);
				return true;
			case HoverDiamondOutlineTextureKey:
				texture = BuildHoverDiamondTexture(outline: true);
				return true;
			case HoverSideLeftFillTextureKey:
			case HoverSideRightFillTextureKey:
				texture = BuildHoverSideFillTexture(isRight: label == HoverSideRightFillTextureKey);
				return true;
			case HoverEdgeSegmentTextureKey:
				texture = BuildHoverEdgeTexture();
				return true;
			default:
				texture = null!;
				return false;
		}
	}

	private static ImageTexture BuildHoverDiamondTexture(bool outline)
	{
		const int size = 64;
		var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
		var center = (size - 1) * 0.5f;
		for (var py = 0; py < size; py++)
		for (var px = 0; px < size; px++)
		{
			var nx = Math.Abs(px - center) / (size * 0.5f);
			var ny = Math.Abs(py - center) / (size * 0.5f);
			var d = nx + ny;
			if (d > 1f)
			{
				image.SetPixel(px, py, Colors.Transparent);
				continue;
			}

			if (outline)
			{
				var edgeAlpha = Math.Clamp((d - 0.85f) / 0.15f, 0f, 1f);
				var innerFade = 1f - Math.Clamp((d - 0.15f) / 0.45f, 0f, 1f);
				var alpha = Math.Max(edgeAlpha, innerFade * 0.06f);
				image.SetPixel(px, py, new Color(1f, 1f, 1f, alpha));
			}
			else
			{
				var edgeSoft = 1f - Math.Clamp((d - 0.80f) / 0.20f, 0f, 1f);
				var centerGlow = 1f - Math.Clamp(d / 0.72f, 0f, 1f);
				var alpha = Math.Clamp(0.22f + centerGlow * 0.62f + edgeSoft * 0.22f, 0f, 1f);
				image.SetPixel(px, py, new Color(1f, 1f, 1f, alpha));
			}
		}

		return ImageTexture.CreateFromImage(image);
	}

	private static ImageTexture BuildHoverSideFillTexture(bool isRight)
	{
		var image = Image.CreateEmpty(SideTextureWidth, SideTextureHeight, false, Image.Format.Rgba8);
		for (var px = 0; px < SideTextureWidth; px++)
		{
			var pyStart = isRight
				? (int)Math.Round((SideTextureWidth - 1 - px) * IsoCoordUtil.TileHalfH / (double)(SideTextureWidth - 1))
				: (int)Math.Round(px * IsoCoordUtil.TileHalfH / (double)(SideTextureWidth - 1));
			var outerEdgeFactor = isRight
				? px / (float)(SideTextureWidth - 1)
				: 1f - px / (float)(SideTextureWidth - 1);

			for (var dy = 0; dy < SideFaceHeight; dy++)
			{
				var py = pyStart + dy;
				if (py >= SideTextureHeight)
					break;

				var depthFactor = 1f - dy / (float)Math.Max(1, SideFaceHeight - 1);
				var alpha = Math.Clamp(0.18f + depthFactor * 0.30f + outerEdgeFactor * 0.10f, 0f, 1f);
				image.SetPixel(px, py, new Color(1f, 1f, 1f, alpha));
			}
		}

		return ImageTexture.CreateFromImage(image);
	}

	private static ImageTexture BuildHoverEdgeTexture()
	{
		const int width = 32;
		const int height = 4;
		var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
		var centerX = (width - 1) * 0.5f;
		var centerY = (height - 1) * 0.5f;

		for (var py = 0; py < height; py++)
		for (var px = 0; px < width; px++)
		{
			var nx = Math.Abs(px - centerX) / (width * 0.5f);
			var ny = Math.Abs(py - centerY) / (height * 0.5f);
			var alpha = Math.Clamp((1f - ny * ny) * (0.78f + (1f - nx * nx) * 0.22f), 0f, 1f);
			image.SetPixel(px, py, new Color(1f, 1f, 1f, alpha));
		}

		return ImageTexture.CreateFromImage(image);
	}

	private static HoverVolumeGeometry BuildHoverVolumeGeometry(Vector2 topCenter)
	{
		var verticalOffset = new Vector2(0f, IsoCoordUtil.ZStep);
		var top = topCenter + new Vector2(0f, -IsoCoordUtil.TileHalfH);
		var right = topCenter + new Vector2(IsoCoordUtil.TileHalfW, 0f);
		var bottom = topCenter + new Vector2(0f, IsoCoordUtil.TileHalfH);
		var left = topCenter + new Vector2(-IsoCoordUtil.TileHalfW, 0f);
		return new HoverVolumeGeometry(
			topCenter,
			top,
			right,
			bottom,
			left,
			right + verticalOffset,
			bottom + verticalOffset,
			left + verticalOffset);
	}

	private static string[] GetHoverTextureKeys() =>
	[
		HoverDiamondOutlineTextureKey,
		HoverDiamondFillTextureKey,
		HoverSideLeftFillTextureKey,
		HoverSideRightFillTextureKey,
		HoverEdgeSegmentTextureKey,
	];

	private readonly record struct HoverHighlightStyle(
		Color FillTint,
		Color OutlineTint,
		Color SideTint);

	private readonly record struct HoverHighlightCommand(
		Vector2 ScreenPos,
		Vector3I WorldCell,
		HoverHighlightStyle Style,
		long SortKey,
		WorldToolPreviewState? PreviewState);

	private readonly record struct HoverVolumeGeometry(
		Vector2 TopCenter,
		Vector2 Top,
		Vector2 Right,
		Vector2 Bottom,
		Vector2 Left,
		Vector2 LowerRight,
		Vector2 LowerBottom,
		Vector2 LowerLeft);
}
