using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using MiniRPG.Core.World.Generators;
using MiniRPG.Module.Editor;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class IsometricRenderTests
{
	[Fact]
	public void IsoCoordUtil_WorldToScreen_And_ScreenToWorld_AreConsistent()
	{
		for (var z = -3; z <= 3; z++)
		{
			for (var y = -8; y <= 8; y += 2)
			{
				for (var x = -8; x <= 8; x += 2)
				{
					var screen = IsoCoordUtil.WorldToScreen(x, y, z);
					var (wx, wy) = IsoCoordUtil.ScreenToWorld(screen, z);
					var (cx, cy) = IsoCoordUtil.ScreenToWorldCell(screen, z);

					Assert.True(Mathf.Abs(wx - x) < 0.0001f, $"wx mismatch at ({x},{y},{z})");
					Assert.True(Mathf.Abs(wy - y) < 0.0001f, $"wy mismatch at ({x},{y},{z})");
					Assert.Equal(x, cx);
					Assert.Equal(y, cy);
				}
			}
		}
	}

	[Fact]
	public void IsoCoordUtil_SortKey_FollowsDiagonalThenDepthOrder()
	{
		var cells = new List<(int X, int Y, int Z)>
		{
			(0, 0, 0),
			(1, 0, 0),
			(0, 1, -1),
			(0, 1, 1),
			(2, 0, 0),
			(1, 1, 2),
			(1, 1, -2),
		};

		cells.Sort(static (a, b) => IsoCoordUtil.SortKey(a.X, a.Y, a.Z).CompareTo(IsoCoordUtil.SortKey(b.X, b.Y, b.Z)));

		for (var i = 1; i < cells.Count; i++)
		{
			var prev = cells[i - 1];
			var curr = cells[i];
			var prevDiag = prev.X + prev.Y;
			var currDiag = curr.X + curr.Y;

			Assert.True(prevDiag <= currDiag, "diagonal order should be non-decreasing");
			if (prevDiag == currDiag)
				Assert.True(prev.Z >= curr.Z, "within same diagonal, deeper z should come first");
		}
	}

	[Fact]
	public void IsometricVoxelRenderer_GetVisionTint_MapsAllBands()
	{
		var state = new GameState { PlayerX = 0, PlayerY = 0, PlayerZ = 0 };
		var fog = new FogOfWarTracker();
		SetFogStateForTest(fog);
		var renderer = new IsometricVoxelRenderer(state, fog, viewW: 20, viewH: 20);

		var focused = InvokeVisionTint(renderer, 1, 1, 0);
		var peripheral = InvokeVisionTint(renderer, 2, 2, 0);
		var memory = InvokeVisionTint(renderer, 3, 3, 0);
		var unknown = InvokeVisionTint(renderer, 4, 4, 0);

		AssertColorApprox(Colors.White, focused);
		AssertColorApprox(new Color(0.78f, 0.78f, 0.82f, 1f), peripheral);
		AssertColorApprox(new Color(0.42f, 0.42f, 0.48f, 0.88f), memory);
		AssertColorApprox(new Color(0f, 0f, 0f, 1f), unknown);
	}

	[Fact]
	public void IsoCoordUtil_RoundTrip_RemainsStable_ForLargeAndNegativeCoordinates()
	{
		var samples = new (int X, int Y, int Z)[]
		{
			(-512, 1024, -8),
			(2048, -1024, 4),
			(-4096, -4096, 12),
			(8192, 8192, -12),
		};

		foreach (var (x, y, z) in samples)
		{
			var screen = IsoCoordUtil.WorldToScreen(x, y, z);
			var (cx, cy) = IsoCoordUtil.ScreenToWorldCell(screen, z);
			Assert.Equal(x, cx);
			Assert.Equal(y, cy);
		}
	}

	[Fact]
	public void IsometricVoxelRenderer_TryPickEditorCell_PreservesTargetLayer()
	{
		var state = new GameState
		{
			World = new WorldMap(worldSeed: 7, new BlankFloorGenerator()),
		};
		var renderer = new IsometricVoxelRenderer(state, new FogOfWarTracker(), viewW: 20, viewH: 20);
		var method = typeof(IsometricVoxelRenderer).GetMethod("TryPickEditorCell", BindingFlags.NonPublic | BindingFlags.Instance);

		Assert.NotNull(method);

		var expected = new Vector3I(6, -2, -3);
		var screen = IsoCoordUtil.WorldToScreen(expected.X, expected.Y, expected.Z);
		object?[] args = [screen, expected.Z, null];

		var picked = (bool)method!.Invoke(renderer, args)!;

		Assert.True(picked);
		Assert.Equal(expected, Assert.IsType<Vector3I>(args[2]));
	}

	[Fact]
	public void IsometricVoxelRenderer_BuildHoverVolumeGeometry_UsesOneCellDepth()
	{
		var geometry = InvokeHoverVolumeGeometry(new Vector2(24f, 48f));
		var left = GetGeometryPoint(geometry, "Left");
		var bottom = GetGeometryPoint(geometry, "Bottom");
		var right = GetGeometryPoint(geometry, "Right");
		var lowerLeft = GetGeometryPoint(geometry, "LowerLeft");
		var lowerBottom = GetGeometryPoint(geometry, "LowerBottom");
		var lowerRight = GetGeometryPoint(geometry, "LowerRight");

		AssertVector2Approx(new Vector2(24f - IsoCoordUtil.TileHalfW, 48f), left);
		AssertVector2Approx(new Vector2(24f, 48f + IsoCoordUtil.TileHalfH), bottom);
		AssertVector2Approx(new Vector2(24f + IsoCoordUtil.TileHalfW, 48f), right);
		AssertVector2Approx(left + new Vector2(0f, IsoCoordUtil.ZStep), lowerLeft);
		AssertVector2Approx(bottom + new Vector2(0f, IsoCoordUtil.ZStep), lowerBottom);
		AssertVector2Approx(right + new Vector2(0f, IsoCoordUtil.ZStep), lowerRight);
	}

	[Fact]
	public void IsometricVoxelRenderer_HoverTextureKeys_IncludeTopAndVolumeMarkers()
	{
		var method = typeof(IsometricVoxelRenderer).GetMethod("GetHoverTextureKeys", BindingFlags.NonPublic | BindingFlags.Static);

		Assert.NotNull(method);

		var keys = Assert.IsType<string[]>(method!.Invoke(null, null));

		Assert.Contains("hover_diamond_outline", keys);
		Assert.Contains("hover_diamond_fill", keys);
		Assert.Contains("hover_side_left_fill", keys);
		Assert.Contains("hover_side_right_fill", keys);
		Assert.Contains("hover_edge_segment", keys);
	}

	[Fact]
	public void IsometricVoxelRenderer_ResolveHoverHighlightCell_UsesEditorHoverTarget()
	{
		var rawHover = new Vector3I(4, 5, 0);
		var hoverState = new MapEditorHoverState(
			MapEditorToolMode.Build,
			MapEditorBrushCategory.Terrain,
			MapEditorHoverStateKind.Terrain,
			rawHover,
			new Vector3I(4, 5, -1),
			Terrains.Floor,
			"#",
			CanApply: true,
			ShowGhost: true,
			ShowInfoOverlay: false);

		var highlightCell = IsometricVoxelRenderer.ResolveHoverHighlightCell(
			editorViewActive: true,
			rawHover,
			hoverState);

		Assert.Equal(hoverState.ResolvedTargetCell, highlightCell);
	}

	[Fact]
	public void IsometricVoxelRenderer_ResolveHoverHighlightCell_FallsBackToRawHoverWhenNoEditorStateExists()
	{
		var rawHover = new Vector3I(7, 8, 1);

		var highlightCell = IsometricVoxelRenderer.ResolveHoverHighlightCell(
			editorViewActive: true,
			rawHover,
			editorHoverState: null);

		Assert.Equal(rawHover, highlightCell);
	}

	[Fact]
	public void IsometricVoxelRenderer_EditorPlacementGhost_UsesThirtyPercentAlpha()
	{
		Assert.True(Mathf.Abs(IsometricVoxelRenderer.EditorPlacementGhostAlpha - 0.30f) < 0.0001f);
	}

	[Fact]
	public void IsometricVoxelRenderer_EditorPlacementGhost_IsHiddenWhenHoverStateCannotBuild()
	{
		var blockedBuildState = new MapEditorHoverState(
			MapEditorToolMode.Build,
			MapEditorBrushCategory.Terrain,
			MapEditorHoverStateKind.Terrain,
			new Vector3I(2, 2, 0),
			new Vector3I(2, 2, 0),
			Terrains.Floor,
			"#",
			CanApply: false,
			ShowGhost: false,
			ShowInfoOverlay: false);
		var selectState = new MapEditorHoverState(
			MapEditorToolMode.Select,
			MapEditorBrushCategory.Terrain,
			MapEditorHoverStateKind.Terrain,
			new Vector3I(2, 2, 0),
			new Vector3I(2, 2, 1),
			Terrains.Floor,
			"#",
			CanApply: true,
			ShowGhost: false,
			ShowInfoOverlay: true);

		Assert.False(IsometricVoxelRenderer.ShouldDrawEditorPlacementGhost(blockedBuildState));
		Assert.False(IsometricVoxelRenderer.ShouldDrawEditorPlacementGhost(selectState));
		Assert.Equal(0, IsometricVoxelRenderer.GetEditorPlacementGhostCommandCount(blockedBuildState));
		Assert.Equal(0, IsometricVoxelRenderer.GetEditorPlacementGhostCommandCount(selectState));
	}

	[Fact]
	public void IsometricVoxelRenderer_EditorPlacementGhost_SupportsTerrainAndFixtureBuildStates()
	{
		var terrainHoverState = new MapEditorHoverState(
			MapEditorToolMode.Build,
			MapEditorBrushCategory.Terrain,
			MapEditorHoverStateKind.Terrain,
			new Vector3I(1, 1, 0),
			new Vector3I(1, 1, 0),
			Terrains.Floor,
			"#",
			CanApply: true,
			ShowGhost: true,
			ShowInfoOverlay: false);
		var fixtureHoverState = new MapEditorHoverState(
			MapEditorToolMode.Build,
			MapEditorBrushCategory.Fixture,
			MapEditorHoverStateKind.Fixture,
			new Vector3I(3, 3, 0),
			new Vector3I(3, 3, 0),
			Entities.Door,
			"D",
			CanApply: true,
			ShowGhost: true,
			ShowInfoOverlay: false);

		Assert.True(IsometricVoxelRenderer.ShouldDrawEditorPlacementGhost(terrainHoverState));
		Assert.True(IsometricVoxelRenderer.ShouldDrawEditorPlacementGhost(fixtureHoverState));
		Assert.Equal(3, IsometricVoxelRenderer.GetEditorPlacementGhostCommandCount(terrainHoverState));
		Assert.Equal(1, IsometricVoxelRenderer.GetEditorPlacementGhostCommandCount(fixtureHoverState));
	}

	private static Color InvokeVisionTint(IsometricVoxelRenderer renderer, int x, int y, int z)
	{
		var method = typeof(IsometricVoxelRenderer).GetMethod("GetVisionTint", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(method);
		return (Color)method!.Invoke(renderer, [x, y, z])!;
	}

	private static object InvokeHoverVolumeGeometry(Vector2 topCenter)
	{
		var method = typeof(IsometricVoxelRenderer).GetMethod("BuildHoverVolumeGeometry", BindingFlags.NonPublic | BindingFlags.Static);
		Assert.NotNull(method);
		return method!.Invoke(null, [topCenter])!;
	}

	private static Vector2 GetGeometryPoint(object geometry, string propertyName)
	{
		var property = geometry.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
		Assert.NotNull(property);
		return Assert.IsType<Vector2>(property!.GetValue(geometry));
	}

	private static void SetFogStateForTest(FogOfWarTracker fog)
	{
		var fogType = typeof(FogOfWarTracker);
		var directionalField = fogType.GetField("_directionalVisible", BindingFlags.NonPublic | BindingFlags.Instance);
		var fullField = fogType.GetField("_fullVisible", BindingFlags.NonPublic | BindingFlags.Instance);
		var seenField = fogType.GetField("_seen", BindingFlags.NonPublic | BindingFlags.Instance);

		Assert.NotNull(directionalField);
		Assert.NotNull(fullField);
		Assert.NotNull(seenField);

		directionalField!.SetValue(fog, new HashSet<(int X, int Y, int Z)> { (1, 1, 0) });
		fullField!.SetValue(fog, new HashSet<(int X, int Y, int Z)> { (1, 1, 0), (2, 2, 0) });

		var seen = new Dictionary<int, HashSet<long>>
		{
			[0] =
			[
				Pack(1, 1),
				Pack(2, 2),
				Pack(3, 3),
			],
		};
		seenField.SetValue(fog, seen);
	}

	private static long Pack(int x, int y) => ((long)x << 32) | (uint)y;

	private static void AssertColorApprox(Color expected, Color actual)
	{
		const float eps = 0.0001f;
		Assert.True(Mathf.Abs(expected.R - actual.R) < eps, $"R mismatch: expected {expected.R}, got {actual.R}");
		Assert.True(Mathf.Abs(expected.G - actual.G) < eps, $"G mismatch: expected {expected.G}, got {actual.G}");
		Assert.True(Mathf.Abs(expected.B - actual.B) < eps, $"B mismatch: expected {expected.B}, got {actual.B}");
		Assert.True(Mathf.Abs(expected.A - actual.A) < eps, $"A mismatch: expected {expected.A}, got {actual.A}");
	}

	private static void AssertVector2Approx(Vector2 expected, Vector2 actual)
	{
		const float eps = 0.0001f;
		Assert.True(Mathf.Abs(expected.X - actual.X) < eps, $"X mismatch: expected {expected.X}, got {actual.X}");
		Assert.True(Mathf.Abs(expected.Y - actual.Y) < eps, $"Y mismatch: expected {expected.Y}, got {actual.Y}");
	}

}
