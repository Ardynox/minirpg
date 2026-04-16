using System;
using Godot;

namespace MiniRPG.Module.Render;

/// <summary>Half-extents (in tile cells) of the visible voxel window around the camera.</summary>
internal readonly record struct VisibleWorldWindow(int HalfX, int HalfY);

/// <summary>Depth layers (in voxel units) to render above / below the camera center.</summary>
internal readonly record struct VisibleDepthWindow(int Above, int Below);

/// <summary>
/// Pure math for the isometric viewport: given the viewport size, camera
/// zoom and default depth hints, compute how many tile cells and voxel
/// layers the renderer needs to iterate, and compute world-space screen
/// rectangles for visibility culling.
/// </summary>
/// <remarks>
/// Extracted from <see cref="IsometricVoxelRenderer"/> so the rendering
/// pass is not responsible for these clamp/overscan rules and so the math
/// can be unit-tested without constructing a full renderer.
/// </remarks>
internal static class VoxelViewportMath
{
	public const int DefaultViewDepthAbove = 4;
	public const int DefaultViewDepthBelow = 2;
	public const int VisibleDepthOverscanLayers = 2;
	public const int MaxVisibleDepthAbove = 48;
	public const int MaxVisibleDepthBelow = 48;
	public const int VisibleWindowOverscanCells = 4;
	public const int MaxVisibleWindowHalfExtent = 48;

	public static VisibleWorldWindow CalculateVisibleWorldWindow(
		Vector2I viewportSize,
		Vector2 zoom,
		VisibleWorldWindow fallback)
	{
		if (viewportSize.X <= 0 || viewportSize.Y <= 0)
			return fallback;

		var zoomX = Math.Max(0.001f, zoom.X);
		var zoomY = Math.Max(0.001f, zoom.Y);
		var localHalfWidth = viewportSize.X * 0.5f / zoomX;
		var localHalfHeight = viewportSize.Y * 0.5f / zoomY
			+ Math.Max(DefaultViewDepthAbove, DefaultViewDepthBelow) * IsoCoordUtil.ZStep;
		var screenDiffRadius = localHalfWidth / IsoCoordUtil.TileHalfW;
		var screenSumRadius = localHalfHeight / IsoCoordUtil.TileHalfH;
		var requiredHalfExtent = Mathf.CeilToInt((screenDiffRadius + screenSumRadius) * 0.5f);
		var expandedHalfExtent = Math.Max(
			requiredHalfExtent + VisibleWindowOverscanCells,
			Math.Max(fallback.HalfX, fallback.HalfY));
		var clampedHalfExtent = Math.Clamp(expandedHalfExtent, 0, MaxVisibleWindowHalfExtent);
		return new VisibleWorldWindow(clampedHalfExtent, clampedHalfExtent);
	}

	public static VisibleDepthWindow CalculateVisibleDepthWindow(
		Vector2I viewportSize,
		Vector2 zoom,
		VisibleWorldWindow visibleWorldWindow,
		VisibleDepthWindow fallback)
	{
		if (viewportSize.X <= 0 || viewportSize.Y <= 0)
			return fallback;

		var zoomY = Math.Max(0.001f, zoom.Y);
		var localHalfHeight = viewportSize.Y * 0.5f / zoomY;
		var visibleDiagonalYOffset = (visibleWorldWindow.HalfX + visibleWorldWindow.HalfY) * IsoCoordUtil.TileHalfH;
		var requiredHalfDepth = Mathf.CeilToInt(
			(localHalfHeight + visibleDiagonalYOffset) / IsoCoordUtil.ZStep) + VisibleDepthOverscanLayers;
		var above = Math.Clamp(Math.Max(fallback.Above, requiredHalfDepth), fallback.Above, MaxVisibleDepthAbove);
		var below = Math.Clamp(Math.Max(fallback.Below, requiredHalfDepth), fallback.Below, MaxVisibleDepthBelow);
		return new VisibleDepthWindow(above, below);
	}

	public static Rect2 CalculateVisibleMapRect(
		Vector2I viewportSize,
		Vector2 cameraPosition,
		Vector2 zoom)
	{
		var zoomX = Math.Max(0.001f, zoom.X);
		var zoomY = Math.Max(0.001f, zoom.Y);
		var halfWidth = viewportSize.X * 0.5f / zoomX + IsoCoordUtil.TileHalfW;
		var halfHeight = viewportSize.Y * 0.5f / zoomY + IsoCoordUtil.ZStep;
		return new Rect2(
			cameraPosition.X - halfWidth,
			cameraPosition.Y - halfHeight,
			halfWidth * 2f,
			halfHeight * 2f);
	}

	public static Rect2 GetVoxelScreenBounds(Vector2 topCenter)
	{
		return new Rect2(
			topCenter.X - IsoCoordUtil.TileHalfW,
			topCenter.Y - IsoCoordUtil.TileHalfH,
			IsoCoordUtil.TileHalfW * 2f,
			IsoCoordUtil.TileHalfH + IsoCoordUtil.ZStep);
	}

	public static bool IsVoxelScreenVisible(Vector2 topCenter, Rect2 visibleMapRect)
		=> GetVoxelScreenBounds(topCenter).Intersects(visibleMapRect);
}
