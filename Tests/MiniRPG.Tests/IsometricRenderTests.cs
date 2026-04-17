using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using MiniRPG.Core.World.Generators;
using MiniRPG.Module.Editor;
using MiniRPG.Module.Render;
using MiniRPG.Module.WorldTool;
using Xunit;

namespace MiniRPG.Tests;

public sealed class IsometricRenderTests
{
	public IsometricRenderTests()
	{
		LocalizationService.Initialize();
		LocalizationService.SetLocale("en", notify: false);
		TerrainRegistry.Load("terrains.json");
		PresetDB.Load();
	}

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
	public void IsoCoordUtil_CompareSortOrder_BreaksSameDiagonalDepthTiesLeftToRight()
	{
		var cells = new List<(int X, int Y, int Z)>
		{
			(2, -1, 0),
			(0, 1, 0),
			(1, 0, 0),
		};

		cells.Sort(static (a, b) => IsoCoordUtil.CompareSortOrder(a.X, a.Y, a.Z, b.X, b.Y, b.Z));

		for (var i = 1; i < cells.Count; i++)
		{
			var previousScreenX = IsoCoordUtil.WorldToScreen(cells[i - 1].X, cells[i - 1].Y, cells[i - 1].Z).X;
			var currentScreenX = IsoCoordUtil.WorldToScreen(cells[i].X, cells[i].Y, cells[i].Z).X;
			Assert.True(previousScreenX < currentScreenX, "same diagonal ties should sort from left to right");
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
	public void IsometricVoxelRenderer_GetVisibleWorldWindow_WithoutViewport_UsesFallbackBaseline()
	{
		var renderer = new IsometricVoxelRenderer(new GameState(), new FogOfWarTracker(), viewW: 20, viewH: 20);

		var window = renderer.GetVisibleWorldWindow();

		Assert.Equal(10, window.HalfX);
		Assert.Equal(10, window.HalfY);
	}

	[Fact]
	public void IsometricVoxelRenderer_GetVisibleDepthWindow_WithoutViewport_UsesFallbackBaseline()
	{
		var renderer = new IsometricVoxelRenderer(new GameState(), new FogOfWarTracker(), viewW: 20, viewH: 20);

		var window = renderer.GetVisibleDepthWindow();

		Assert.Equal(4, window.Above);
		Assert.Equal(2, window.Below);
	}

	[Fact]
	public void IsometricVoxelRenderer_CalculateVisibleWorldWindow_ForDefaultZoom_IsNotSmallerThanLegacyBaseline()
	{
		var fallback = new VisibleWorldWindow(HalfX: 13, HalfY: 7);

		var window = VoxelViewportMath.CalculateVisibleWorldWindow(
			new Vector2I(1280, 960),
			Vector2.One,
			fallback);

		Assert.True(window.HalfX >= fallback.HalfX);
		Assert.True(window.HalfY >= fallback.HalfY);
		Assert.True(window.HalfX + window.HalfY > fallback.HalfX + fallback.HalfY);
	}

	[Fact]
	public void IsometricVoxelRenderer_CalculateVisibleWorldWindow_UsesCombinedScreenWidthAndHeightCoverage()
	{
		var fallback = new VisibleWorldWindow(HalfX: 13, HalfY: 7);

		var window = VoxelViewportMath.CalculateVisibleWorldWindow(
			new Vector2I(1280, 960),
			Vector2.One,
			fallback);

		Assert.Equal(21, window.HalfX);
		Assert.Equal(21, window.HalfY);
	}

	[Fact]
	public void IsometricVoxelRenderer_CalculateVisibleWorldWindow_ForZoomedOutView_ExpandsCoverage()
	{
		var fallback = new VisibleWorldWindow(HalfX: 13, HalfY: 7);
		var defaultZoomWindow = VoxelViewportMath.CalculateVisibleWorldWindow(
			new Vector2I(1280, 960),
			Vector2.One,
			fallback);

		var zoomedOutWindow = VoxelViewportMath.CalculateVisibleWorldWindow(
			new Vector2I(1280, 960),
			new Vector2(0.6f, 0.6f),
			fallback);

		Assert.True(zoomedOutWindow.HalfX > defaultZoomWindow.HalfX);
		Assert.True(zoomedOutWindow.HalfY > defaultZoomWindow.HalfY);
	}

	[Fact]
	public void IsometricVoxelRenderer_CalculateVisibleDepthWindow_ForDefaultZoom_ExpandsBeyondLegacyDepth()
	{
		var visibleWorldWindow = new VisibleWorldWindow(HalfX: 21, HalfY: 21);
		var fallback = new VisibleDepthWindow(Above: 4, Below: 2);

		var window = VoxelViewportMath.CalculateVisibleDepthWindow(
			new Vector2I(1280, 960),
			Vector2.One,
			visibleWorldWindow,
			fallback);

		Assert.Equal(31, window.Above);
		Assert.Equal(31, window.Below);
	}

	[Fact]
	public void IsometricVoxelRenderer_CalculateVisibleDepthWindow_ForMinimumZoom_ReachesConfiguredCap()
	{
		var visibleWorldWindow = new VisibleWorldWindow(HalfX: 29, HalfY: 29);
		var fallback = new VisibleDepthWindow(Above: 4, Below: 2);

		var window = VoxelViewportMath.CalculateVisibleDepthWindow(
			new Vector2I(1280, 960),
			new Vector2(0.6f, 0.6f),
			visibleWorldWindow,
			fallback);

		Assert.Equal(44, window.Above);
		Assert.Equal(44, window.Below);
	}

	[Fact]
	public void IsometricVoxelRenderer_CalculateVisibleWorldWindow_ForMinimumZoom_CoversLargerSquareWindow()
	{
		var fallback = new VisibleWorldWindow(HalfX: 13, HalfY: 7);

		var window = VoxelViewportMath.CalculateVisibleWorldWindow(
			new Vector2I(1280, 960),
			new Vector2(0.6f, 0.6f),
			fallback);

		Assert.Equal(29, window.HalfX);
		Assert.Equal(29, window.HalfY);
	}

	[Fact]
	public void IsometricVoxelRenderer_CalculateVisibleWorldWindow_CapsExtremeZoomOutCoverage()
	{
		var fallback = new VisibleWorldWindow(HalfX: 13, HalfY: 7);

		var window = VoxelViewportMath.CalculateVisibleWorldWindow(
			new Vector2I(1280, 960),
			new Vector2(0.2f, 0.2f),
			fallback);

		Assert.Equal(48, window.HalfX);
		Assert.Equal(48, window.HalfY);
	}

	[Fact]
	public void IsometricVoxelRenderer_CalculateVisibleDepthWindow_CapsExtremeZoomOutCoverage()
	{
		var visibleWorldWindow = new VisibleWorldWindow(HalfX: 48, HalfY: 48);
		var fallback = new VisibleDepthWindow(Above: 4, Below: 2);

		var window = VoxelViewportMath.CalculateVisibleDepthWindow(
			new Vector2I(1280, 960),
			new Vector2(0.2f, 0.2f),
			visibleWorldWindow,
			fallback);

		Assert.Equal(48, window.Above);
		Assert.Equal(48, window.Below);
	}

	[Fact]
	public void IsometricVoxelRenderer_CalculateVisibleMapRect_ExpandsFromCameraByViewportAndOverscan()
	{
		var rect = VoxelViewportMath.CalculateVisibleMapRect(
			new Vector2I(1280, 960),
			new Vector2(100f, 200f),
			Vector2.One);

		AssertVector2Approx(new Vector2(-604f, -344f), rect.Position);
		AssertVector2Approx(new Vector2(1408f, 1088f), rect.Size);
	}

	[Fact]
	public void IsometricVoxelRenderer_IsVoxelScreenVisible_CullsCellsOutsideVisibleMapRect()
	{
		var visibleRect = VoxelViewportMath.CalculateVisibleMapRect(
			new Vector2I(1280, 960),
			Vector2.Zero,
			Vector2.One);

		Assert.True(VoxelViewportMath.IsVoxelScreenVisible(Vector2.Zero, visibleRect));
		Assert.False(VoxelViewportMath.IsVoxelScreenVisible(new Vector2(0f, 1400f), visibleRect));
	}

	[Fact]
	public void IsometricVoxelRenderer_ResolveFacilityFootprintScreenCenter_UsesFootprintBoundsCenter()
	{
		var footprint = new List<ZoneCell>
		{
			new(0, 0, 0),
			new(1, 0, 0),
			new(0, 1, 0),
			new(1, 1, 0),
		};

		var method = typeof(IsometricVoxelRenderer).GetMethod(
			"ResolveFacilityFootprintScreenCenter",
			BindingFlags.NonPublic | BindingFlags.Static);

		Assert.NotNull(method);

		var center = Assert.IsType<Vector2>(method!.Invoke(null, [footprint]));

		AssertVector2Approx(new Vector2(0f, 32f), center);
	}

	[Fact]
	public void IsometricVoxelRenderer_ResolveFacilitySpritePosition_UsesOpaqueBottomInsteadOfFullFrameBottom()
	{
		var method = typeof(IsometricVoxelRenderer).GetMethod(
			"ResolveFacilitySpritePosition",
			BindingFlags.NonPublic | BindingFlags.Static,
			null,
			[
				typeof(Vector2),
				typeof(Vector2),
				typeof(Vector2),
				typeof(Vector2),
				typeof(int),
			],
			null);

		Assert.NotNull(method);

		var position = Assert.IsType<Vector2>(method!.Invoke(
			null,
			[
				new Vector2(32f, 64f),
				new Vector2(256f, 256f),
				new Vector2(0.5f, 0.5f),
				Vector2.Zero,
				236,
			]));

		AssertVector2Approx(new Vector2(-32f, -54f), position);
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
			ShowInfoOverlay: false,
			GhostRenderId: Terrains.Floor,
			GhostGlyph: "#",
			HideResolvedTargetInWorld: false);

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
	public void IsometricVoxelRenderer_ResolveHoverHighlightCell_RuntimeSelectFallsBackToRawHoverWhenNoResolvedTargetExists()
	{
		var rawHover = new Vector3I(9, 4, 2);
		var previewState = new WorldToolPreviewState(
			WorldToolMode.Select,
			WorldToolCategory.Terrain,
			WorldToolPreviewKind.Terrain,
			rawHover,
			ResolvedTargetCell: null,
			BrushId: Terrains.Floor,
			BrushGlyph: "#",
			CanApply: false,
			ShowGhost: false,
			ShowInfoOverlay: false,
			GhostRenderId: null,
			GhostGlyph: null,
			ResolvedEntityId: null,
			HideResolvedTargetInWorld: false,
			GhostFacility: null);

		var highlightCell = IsometricVoxelRenderer.ResolveHoverHighlightCell(rawHover, previewState);

		Assert.Equal(rawHover, highlightCell);
	}

	[Fact]
	public void IsometricVoxelRenderer_SetRuntimeView_UpdatesRuntimeViewCenter()
	{
		var state = new GameState
		{
			PlayerX = 1,
			PlayerY = 2,
			PlayerZ = 0,
			World = CreateAirOnlyWorld(),
		};
		var renderer = new IsometricVoxelRenderer(state, new FogOfWarTracker(), viewW: 20, viewH: 20);

		renderer.SetEditorView(active: false, centerX: 0, centerY: 0, centerZ: 0);
		renderer.SetRuntimeView(
			active: true,
			RuntimeCameraSnapshot.Create(RuntimeCameraMode.LayerPan, centerX: 7, centerY: 8, centerZ: 9));

		Assert.Equal(7, GetPrivateField<int>(renderer, "_viewCenterX"));
		Assert.Equal(8, GetPrivateField<int>(renderer, "_viewCenterY"));
		Assert.Equal(9, GetPrivateField<int>(renderer, "_viewCenterZ"));
	}

	[Fact]
	public void IsometricVoxelRenderer_PresentActorMotion_InterpolatesActorVisualWorldPosition()
	{
		var (state, player) = CreateRendererMotionState();
		var renderer = new IsometricVoxelRenderer(state, new FogOfWarTracker { RevealAll = true }, viewW: 20, viewH: 20);

		renderer.PresentActorMotion(new ActorMotionPresentationRequest(
			player.Id,
			SourceX: 1,
			SourceY: 1,
			SourceZ: 0,
			TargetX: 2,
			TargetY: 1,
			TargetZ: 0,
			ActorMotionTimingTier.PlayerSlow,
			Blocking: true));
		renderer.AdvanceAnimations(ActorMotionTiming.PlayerSlowSeconds * 0.5d);

		var visual = renderer.ResolveActorVisualWorldPosition(player.Id);
		var expectedX = 1f + ActorMotionTracker.ApplyDampedProgress(0.5f);

		Assert.InRange(visual.X, expectedX - 0.01f, expectedX + 0.01f);
		Assert.Equal(1f, visual.Y);
		Assert.Equal(0f, visual.Z);
	}

	[Fact]
	public void IsometricVoxelRenderer_RuntimeCameraFollowsActiveActorVisualPosition()
	{
		var (state, player) = CreateRendererMotionState();
		var renderer = new IsometricVoxelRenderer(state, new FogOfWarTracker { RevealAll = true }, viewW: 20, viewH: 20);
		renderer.SetRuntimeView(
			active: true,
			RuntimeCameraSnapshot.Create(RuntimeCameraMode.FollowActor, centerX: player.X, centerY: player.Y, centerZ: player.Z));
		renderer.PresentActorMotion(new ActorMotionPresentationRequest(
			player.Id,
			SourceX: 1,
			SourceY: 1,
			SourceZ: 0,
			TargetX: 2,
			TargetY: 1,
			TargetZ: 0,
			ActorMotionTimingTier.PlayerSlow,
			Blocking: true));
		renderer.AdvanceAnimations(ActorMotionTiming.PlayerSlowSeconds * 0.5d);

		var target = renderer.ResolveRuntimeCameraScreenTarget();
		var expectedX = 1f + ActorMotionTracker.ApplyDampedProgress(0.5f);

		AssertVector2Approx(IsoCoordUtil.WorldToScreen(expectedX, 1f, 0f), target);
	}

	[Fact]
	public void IsometricVoxelRenderer_BlockingActorMotion_ClearsWhenDurationCompletes()
	{
		var (state, player) = CreateRendererMotionState();
		var renderer = new IsometricVoxelRenderer(state, new FogOfWarTracker { RevealAll = true }, viewW: 20, viewH: 20);
		renderer.PresentActorMotion(new ActorMotionPresentationRequest(
			player.Id,
			SourceX: 1,
			SourceY: 1,
			SourceZ: 0,
			TargetX: 2,
			TargetY: 1,
			TargetZ: 0,
			ActorMotionTimingTier.PlayerSlow,
			Blocking: true));

		Assert.True(renderer.HasBlockingActorMotion);

		renderer.AdvanceAnimations(ActorMotionTiming.PlayerSlowSeconds + 0.01d);

		Assert.False(renderer.HasBlockingActorMotion);
	}

	[Fact]
	public void IsometricVoxelRenderer_NonBlockingActorMotion_DoesNotLockTimeline()
	{
		var (state, player) = CreateRendererMotionState();
		var renderer = new IsometricVoxelRenderer(state, new FogOfWarTracker { RevealAll = true }, viewW: 20, viewH: 20);
		renderer.PresentActorMotion(new ActorMotionPresentationRequest(
			player.Id,
			SourceX: 1,
			SourceY: 1,
			SourceZ: 0,
			TargetX: 2,
			TargetY: 1,
			TargetZ: 0,
			ActorMotionTimingTier.NpcFast,
			Blocking: false,
			DurationSecondsOverride: ActorMotionTiming.NpcRushSeconds));

		Assert.True(renderer.HasAnyActorMotion);
		Assert.False(renderer.HasBlockingActorMotion);
	}

	[Fact]
	public void IsometricVoxelRenderer_PresentActorMotion_UsesDurationOverrideWhenProvided()
	{
		var (state, player) = CreateRendererMotionState();
		var renderer = new IsometricVoxelRenderer(state, new FogOfWarTracker { RevealAll = true }, viewW: 20, viewH: 20);

		renderer.PresentActorMotion(new ActorMotionPresentationRequest(
			player.Id,
			SourceX: 1,
			SourceY: 1,
			SourceZ: 0,
			TargetX: 2,
			TargetY: 1,
			TargetZ: 0,
			ActorMotionTimingTier.NpcFast,
			Blocking: true,
			DurationSecondsOverride: ActorMotionTiming.NpcRushSeconds));
		renderer.AdvanceAnimations(ActorMotionTiming.NpcRushSeconds * 0.5d);

		var visual = renderer.ResolveActorVisualWorldPosition(player.Id);
		var expectedX = 1f + ActorMotionTracker.ApplyDampedProgress(0.5f);

		Assert.InRange(visual.X, expectedX - 0.01f, expectedX + 0.01f);
	}

	[Fact]
	public void ActorMotionTracker_ApplyDampedProgress_PreservesBoundsAndEasesOut()
	{
		Assert.Equal(0f, ActorMotionTracker.ApplyDampedProgress(0f));
		Assert.Equal(1f, ActorMotionTracker.ApplyDampedProgress(1f));
		Assert.Equal(0f, ActorMotionTracker.ApplyDampedProgress(-1f));
		Assert.Equal(1f, ActorMotionTracker.ApplyDampedProgress(2f));
		Assert.True(ActorMotionTracker.ApplyDampedProgress(0.5f) > 0.5f);
	}

	[Fact]
	public void IsometricVoxelRenderer_ResolveSmoothedCameraPosition_MovesTowardTargetWithoutJumping()
	{
		var current = new Vector2(-64f, 32f);
		var target = new Vector2(0f, 64f);

		var next = IsometricVoxelRenderer.ResolveSmoothedCameraPosition(
			current,
			target,
			delta: 1f / 60f,
			lerpSpeed: 4f,
			snapDistanceSquared: 0.01f);

		Assert.True(next.DistanceTo(current) > 0.01f);
		Assert.True(next.DistanceTo(target) > 0.01f);
		Assert.True(next.DistanceTo(target) < current.DistanceTo(target));
	}

	[Fact]
	public void IsometricVoxelRenderer_CompareEntityDrawCommands_PrefersVisualScreenOrder()
	{
		var frontVisual = new Vector2(0f, 96f);
		var backVisual = new Vector2(0f, 32f);
		var staleFrontSortKey = IsoCoordUtil.SortKey(1, 1, 0);
		var staleBackSortKey = IsoCoordUtil.SortKey(3, 3, 0);

		Assert.True(IsometricVoxelRenderer.CompareEntityDrawCommands(
			frontVisual,
			staleFrontSortKey,
			backVisual,
			staleBackSortKey) > 0);
	}

	[Fact]
	public void IsometricVoxelRenderer_TryPickIsometricCell_RuntimeViewUsesCurrentLayerCell()
	{
		var world = CreateAirOnlyWorld();
		world.SetTerrain(5, 6, 0, Terrains.Stone);
		world.SetTerrain(5, 6, 3, Terrains.Dirt);
		var state = new GameState { World = world };
		var renderer = new IsometricVoxelRenderer(state, new FogOfWarTracker(), viewW: 20, viewH: 20);
		renderer.SetRuntimeView(
			active: true,
			RuntimeCameraSnapshot.Create(RuntimeCameraMode.LayerPan, centerX: 5, centerY: 6, centerZ: 2));

		var method = typeof(IsometricVoxelRenderer).GetMethod("TryPickIsometricCell", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(method);

		var screen = IsoCoordUtil.WorldToScreen(5, 6, 2);
		object?[] args = [screen, null];

		var picked = (bool)method!.Invoke(renderer, args)!;

		Assert.True(picked);
		Assert.Equal(new Vector3I(5, 6, 2), Assert.IsType<Vector3I>(args[1]));
	}

	[Fact]
	public void IsometricVoxelRenderer_TryPickInspectCell_RuntimeViewPrefersVisibleActorLayer()
	{
		var world = CreateAirOnlyWorld();
		world.SetTerrain(5, 6, 0, Terrains.Floor);
		world.SetTerrain(5, 6, 3, Terrains.Floor);
		var player = new Actor
		{
			Id = "player",
			DisplayName = "Hero",
			Faction = Factions.Player,
			X = 5,
			Y = 5,
			Z = 0,
		};
		var hawk = new Actor
		{
			Id = "hawk",
			DisplayName = "Hawk",
			Faction = Factions.Hostile,
			X = 5,
			Y = 6,
			Z = 3,
		};
		var state = new GameState
		{
			PlayerId = player.Id,
			PlayerX = player.X,
			PlayerY = player.Y,
			PlayerZ = player.Z,
			World = world,
			Actors = new Dictionary<string, Actor>
			{
				[player.Id] = player,
				[hawk.Id] = hawk,
			},
		};
		state.World.RebuildLoadedActorIndex(state.Actors);
		var fog = new FogOfWarTracker
		{
			RevealAll = true,
		};
		var renderer = new IsometricVoxelRenderer(state, fog, viewW: 20, viewH: 20);
		renderer.SetRuntimeView(
			active: true,
			RuntimeCameraSnapshot.Create(RuntimeCameraMode.LayerPan, centerX: 5, centerY: 6, centerZ: 0));

		var method = typeof(IsometricVoxelRenderer).GetMethod("TryPickInspectCell", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(method);

		var screen = IsoCoordUtil.WorldToScreen(5, 6, 3);
		object?[] args = [screen, null];

		var picked = (bool)method!.Invoke(renderer, args)!;

		Assert.True(picked);
		Assert.Equal(new Vector3I(5, 6, 3), Assert.IsType<Vector3I>(args[1]));
	}

	[Fact]
	public void IsometricVoxelRenderer_CollectHoverHighlights_RuntimePreviewIgnoresUnknownFog()
	{
		var hoverCell = new Vector3I(3, 4, 2);
		var renderer = new IsometricVoxelRenderer(
			new GameState { World = CreateAirOnlyWorld() },
			new FogOfWarTracker(),
			viewW: 20,
			viewH: 20)
		{
			HoverWorldCell = hoverCell,
		};
		var previewState = new WorldToolPreviewState(
			WorldToolMode.Select,
			WorldToolCategory.Terrain,
			WorldToolPreviewKind.Terrain,
			hoverCell,
			ResolvedTargetCell: hoverCell,
			BrushId: Terrains.Floor,
			BrushGlyph: "#",
			CanApply: true,
			ShowGhost: false,
			ShowInfoOverlay: true,
			GhostRenderId: null,
			GhostGlyph: null,
			ResolvedEntityId: null,
			HideResolvedTargetInWorld: false,
			GhostFacility: null);

		InvokeCollectHoverHighlights(renderer, hoverCell, previewState);

		Assert.Single(GetPrivateField<IList>(renderer, "_highlightCommands").Cast<object>());
		Assert.Equal(1, GetPrivateField<int>(renderer, "_highlightCommandCount"));
	}

	[Fact]
	public void IsometricVoxelRenderer_EditorPlacementGhost_UsesThirtyPercentAlpha()
	{
		Assert.True(Mathf.Abs(IsometricVoxelRenderer.EditorPlacementGhostAlpha - 0.30f) < 0.0001f);
	}

	[Fact]
	[SupportedOSPlatform("windows")]
	public void FacilityMapAssets_MultiTileFacilities_ProvideDistinctDirectionalFrames()
	{
		using var bedSheet = LoadFacilitySheet("facility_bed_4dir.png");
		using var marketStallSheet = LoadFacilitySheet("facility_market_stall_4dir.png");

		Assert.False(AreFramesIdentical(bedSheet, 0, 1));
		Assert.False(AreFramesIdentical(bedSheet, 0, 2));
		Assert.False(AreFramesIdentical(marketStallSheet, 0, 1));
		Assert.False(AreFramesIdentical(marketStallSheet, 0, 3));
	}

	[Fact]
	[SupportedOSPlatform("windows")]
	public void FacilityMapAssets_DirectionalFrames_ShareConsistentGroundLine()
	{
		using var bedSheet = LoadFacilitySheet("facility_bed_4dir.png");
		using var marketStallSheet = LoadFacilitySheet("facility_market_stall_4dir.png");

		AssertGroundLineTolerance(bedSheet, tolerance: 2);
		AssertGroundLineTolerance(marketStallSheet, tolerance: 2);
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
			ShowInfoOverlay: false,
			GhostRenderId: null,
			GhostGlyph: null,
			HideResolvedTargetInWorld: false);
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
			ShowInfoOverlay: true,
			GhostRenderId: null,
			GhostGlyph: null,
			HideResolvedTargetInWorld: false);

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
			ShowInfoOverlay: false,
			GhostRenderId: Terrains.Floor,
			GhostGlyph: "#",
			HideResolvedTargetInWorld: false);
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
			ShowInfoOverlay: false,
			GhostRenderId: Entities.Door,
			GhostGlyph: "D",
			HideResolvedTargetInWorld: false);

		Assert.True(IsometricVoxelRenderer.ShouldDrawEditorPlacementGhost(terrainHoverState));
		Assert.True(IsometricVoxelRenderer.ShouldDrawEditorPlacementGhost(fixtureHoverState));
		Assert.Equal(3, IsometricVoxelRenderer.GetEditorPlacementGhostCommandCount(terrainHoverState));
		Assert.Equal(1, IsometricVoxelRenderer.GetEditorPlacementGhostCommandCount(fixtureHoverState));
	}

	[Fact]
	public void IsometricVoxelRenderer_EditorPlacementGhost_SupportsTerrainAndFixtureDemolishStates()
	{
		var terrainHoverState = new MapEditorHoverState(
			MapEditorToolMode.Demolish,
			MapEditorBrushCategory.Terrain,
			MapEditorHoverStateKind.Terrain,
			new Vector3I(4, 4, 0),
			new Vector3I(4, 4, 0),
			Terrains.Floor,
			"#",
			CanApply: true,
			ShowGhost: true,
			ShowInfoOverlay: false,
			GhostRenderId: Terrains.WallStone,
			GhostGlyph: "#",
			HideResolvedTargetInWorld: true);
		var fixtureHoverState = new MapEditorHoverState(
			MapEditorToolMode.Demolish,
			MapEditorBrushCategory.Fixture,
			MapEditorHoverStateKind.Fixture,
			new Vector3I(5, 5, 0),
			new Vector3I(5, 5, 0),
			Entities.Nest,
			"N",
			CanApply: true,
			ShowGhost: true,
			ShowInfoOverlay: false,
			GhostRenderId: Entities.Door,
			GhostGlyph: "D",
			HideResolvedTargetInWorld: true);

		Assert.True(IsometricVoxelRenderer.ShouldDrawEditorPlacementGhost(terrainHoverState));
		Assert.True(IsometricVoxelRenderer.ShouldDrawEditorPlacementGhost(fixtureHoverState));
		Assert.Equal(3, IsometricVoxelRenderer.GetEditorPlacementGhostCommandCount(terrainHoverState));
		Assert.Equal(1, IsometricVoxelRenderer.GetEditorPlacementGhostCommandCount(fixtureHoverState));
	}

	[Fact]
	public void IsometricVoxelRenderer_HideEditorPreviewTarget_HidesMatchingDemolishTargetsOnly()
	{
		var terrainHoverState = new MapEditorHoverState(
			MapEditorToolMode.Demolish,
			MapEditorBrushCategory.Terrain,
			MapEditorHoverStateKind.Terrain,
			new Vector3I(4, 4, 0),
			new Vector3I(4, 4, 0),
			Terrains.Floor,
			"#",
			CanApply: true,
			ShowGhost: true,
			ShowInfoOverlay: false,
			GhostRenderId: Terrains.WallStone,
			GhostGlyph: "#",
			HideResolvedTargetInWorld: true);
		var fixtureHoverState = new MapEditorHoverState(
			MapEditorToolMode.Demolish,
			MapEditorBrushCategory.Fixture,
			MapEditorHoverStateKind.Fixture,
			new Vector3I(5, 5, 0),
			new Vector3I(5, 5, 0),
			Entities.Nest,
			"N",
			CanApply: true,
			ShowGhost: true,
			ShowInfoOverlay: false,
			GhostRenderId: Entities.Door,
			GhostGlyph: "D",
			HideResolvedTargetInWorld: true);
		var selectState = new MapEditorHoverState(
			MapEditorToolMode.Select,
			MapEditorBrushCategory.Terrain,
			MapEditorHoverStateKind.Terrain,
			new Vector3I(4, 4, 0),
			new Vector3I(4, 4, 0),
			Terrains.Floor,
			"#",
			CanApply: true,
			ShowGhost: false,
			ShowInfoOverlay: true,
			GhostRenderId: null,
			GhostGlyph: null,
			HideResolvedTargetInWorld: false);

		Assert.True(IsometricVoxelRenderer.ShouldHideEditorPreviewTerrain(terrainHoverState, 4, 4, 0));
		Assert.False(IsometricVoxelRenderer.ShouldHideEditorPreviewTerrain(terrainHoverState, 4, 4, -1));
		Assert.True(IsometricVoxelRenderer.ShouldHideEditorPreviewFixture(fixtureHoverState, 5, 5, 0, Entities.Door));
		Assert.False(IsometricVoxelRenderer.ShouldHideEditorPreviewFixture(fixtureHoverState, 5, 5, 0, Entities.Nest));
		Assert.False(IsometricVoxelRenderer.ShouldHideEditorPreviewTerrain(selectState, 4, 4, 0));
	}

	[Fact]
	public void IsometricVoxelRenderer_BuildChunkTerrainSurfaceEntries_IncludesSingleVisibleBlock()
	{
		var world = CreateAirOnlyWorld();
		world.SetTerrain(10, 12, 0, Terrains.WallStone);
		var chunk = world.Chunks.GetOrLoad(CoordUtil.WorldToChunk(10, 12, 0));

		var entries = ChunkTerrainSurfaceCacheStore.BuildEntries(
			world,
			chunk,
			out var emptyCellCount,
			out var occludedCellCount,
			out var hiddenFaceCellCount);

		var entry = Assert.Single(entries);
		Assert.Equal(10, entry.WorldX);
		Assert.Equal(12, entry.WorldY);
		Assert.Equal(0, entry.WorldZ);
		Assert.True(entry.DrawTop);
		Assert.True(entry.DrawLeftSide);
		Assert.True(entry.DrawRightSide);
		Assert.False(entry.ShadowTop);
		Assert.Equal(ChunkData.Area - 1, emptyCellCount);
		Assert.Equal(0, occludedCellCount);
		Assert.Equal(0, hiddenFaceCellCount);
	}

	[Fact]
	public void IsometricVoxelRenderer_BuildChunkTerrainSurfaceEntries_OmitsFullyOccludedBlock()
	{
		var world = CreateAirOnlyWorld();
		world.SetTerrain(14, 9, 0, Terrains.WallStone);
		world.SetTerrain(15, 9, 0, Terrains.WallStone);
		world.SetTerrain(14, 10, 0, Terrains.WallStone);
		world.SetTerrain(14, 9, -1, Terrains.WallStone);
		var chunk = world.Chunks.GetOrLoad(CoordUtil.WorldToChunk(14, 9, 0));

		var entries = ChunkTerrainSurfaceCacheStore.BuildEntries(
			world,
			chunk,
			out _,
			out var occludedCellCount,
			out _);

		Assert.DoesNotContain(entries, entry => entry.WorldX == 14 && entry.WorldY == 9 && entry.WorldZ == 0);
		Assert.True(occludedCellCount >= 1);
	}

	[Fact]
	public void IsometricVoxelRenderer_BuildChunkTerrainSurfaceEntries_PreservesShadowTopRule()
	{
		var world = CreateAirOnlyWorld();
		world.SetTerrain(18, 6, 0, Terrains.WallStone);
		world.SetTerrain(18, 6, -1, Terrains.WallStone);
		world.SetTerrain(19, 6, 0, Terrains.WallStone);
		var chunk = world.Chunks.GetOrLoad(CoordUtil.WorldToChunk(18, 6, 0));

		var entries = ChunkTerrainSurfaceCacheStore.BuildEntries(
			world,
			chunk,
			out _,
			out _,
			out _);

		var entry = Assert.Single(entries, item => item.WorldX == 18 && item.WorldY == 6 && item.WorldZ == 0);
		Assert.True(entry.DrawTop);
		Assert.True(entry.DrawLeftSide);
		Assert.False(entry.DrawRightSide);
		Assert.True(entry.ShadowTop);
	}

	[Fact]
	public void IsometricVoxelRenderer_BuildChunkTerrainSurfaceEntries_HidesChunkBoundaryFacesAgainstNeighborChunk()
	{
		var world = CreateAirOnlyWorld();
		world.SetTerrain(31, 31, 0, Terrains.WallStone);
		world.SetTerrain(32, 31, 0, Terrains.WallStone);
		world.SetTerrain(31, 32, 0, Terrains.WallStone);
		var chunk = world.Chunks.GetOrLoad(new ChunkCoord(0, 0, 0));

		var entries = ChunkTerrainSurfaceCacheStore.BuildEntries(
			world,
			chunk,
			out _,
			out _,
			out _);

		var entry = Assert.Single(entries, item => item.WorldX == 31 && item.WorldY == 31 && item.WorldZ == 0);
		Assert.False(entry.DrawLeftSide);
		Assert.False(entry.DrawRightSide);
		Assert.True(entry.DrawTop);
	}

	[Fact]
	public void WorldMap_SetTerrain_BumpsCurrentAndNeighborChunkTerrainGeometryRevision()
	{
		var world = new WorldMap(9, new BlankFloorGenerator());
		var centerCoord = new ChunkCoord(0, 0, 0);
		var eastCoord = new ChunkCoord(1, 0, 0);
		var southCoord = new ChunkCoord(0, 1, 0);
		var aboveCoord = new ChunkCoord(0, 0, -1);
		var currentChunk = world.Chunks.GetOrLoad(centerCoord);
		var eastChunk = world.Chunks.GetOrLoad(eastCoord);
		var southChunk = world.Chunks.GetOrLoad(southCoord);
		var aboveChunk = world.Chunks.GetOrLoad(aboveCoord);
		var currentRevision = currentChunk.TerrainGeometryRevision;
		var eastRevision = eastChunk.TerrainGeometryRevision;
		var southRevision = southChunk.TerrainGeometryRevision;
		var aboveRevision = aboveChunk.TerrainGeometryRevision;

		world.SetTerrain(5, 5, 0, Terrains.WallStone);

		Assert.True(currentChunk.TerrainGeometryRevision > currentRevision);
		Assert.True(eastChunk.TerrainGeometryRevision > eastRevision);
		Assert.True(southChunk.TerrainGeometryRevision > southRevision);
		Assert.True(aboveChunk.TerrainGeometryRevision > aboveRevision);
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

	private static void InvokeCollectHoverHighlights(
		IsometricVoxelRenderer renderer,
		Vector3I hoverCell,
		WorldToolPreviewState previewState)
	{
		var method = typeof(IsometricVoxelRenderer).GetMethod("CollectHoverHighlights", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(method);
		method!.Invoke(renderer, [hoverCell.X, hoverCell.Y, hoverCell.Z, 10, 10, hoverCell.Z - 2, hoverCell.Z + 2, null, previewState]);
	}

	private static T GetPrivateField<T>(object target, string fieldName)
	{
		var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(field);
		return Assert.IsAssignableFrom<T>(field!.GetValue(target));
	}

	private static Vector2 GetGeometryPoint(object geometry, string propertyName)
	{
		var property = geometry.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
		Assert.NotNull(property);
		return Assert.IsType<Vector2>(property!.GetValue(geometry));
	}

	private static WorldMap CreateAirOnlyWorld() =>
		new(17, new AirOnlyGenerator());

	private static (GameState State, Actor Player) CreateRendererMotionState()
	{
		var world = CreateAirOnlyWorld();
		world.SetTerrain(1, 1, 0, Terrains.Floor);
		world.SetTerrain(2, 1, 0, Terrains.Floor);
		var state = new GameState
		{
			PlayerId = "player",
			PlayerX = 2,
			PlayerY = 1,
			PlayerZ = 0,
			World = world,
		};
		var player = new Actor
		{
			Id = "player",
			DisplayName = "player",
			Faction = Factions.Player,
			X = 2,
			Y = 1,
			Z = 0,
		};
		ActorModule.Add(state, player);
		PartyModule.Initialize(state);
		return (state, player);
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

	[SupportedOSPlatform("windows")]
	private static System.Drawing.Bitmap LoadFacilitySheet(string fileName)
	{
		var path = GetRepoPath("Assets", "Art", "Generated", "facilities", fileName);
		Assert.True(File.Exists(path), $"Missing facility sprite sheet: {path}");
		return new System.Drawing.Bitmap(path);
	}

	[SupportedOSPlatform("windows")]
	private static bool AreFramesIdentical(System.Drawing.Bitmap sheet, int frameA, int frameB)
	{
		for (var y = 0; y < 256; y++)
		{
			for (var x = 0; x < 256; x++)
			{
				if (sheet.GetPixel(x, frameA * 256 + y) != sheet.GetPixel(x, frameB * 256 + y))
					return false;
			}
		}

		return true;
	}

	[SupportedOSPlatform("windows")]
	private static void AssertGroundLineTolerance(System.Drawing.Bitmap sheet, int tolerance)
	{
		var minBottom = int.MaxValue;
		var maxBottom = int.MinValue;
		for (var frame = 0; frame < 4; frame++)
		{
			var frameBottom = 0;
			for (var y = 255; y >= 0; y--)
			{
				var hasVisiblePixel = false;
				for (var x = 0; x < 256; x++)
				{
					if (sheet.GetPixel(x, frame * 256 + y).A == 0)
						continue;

					hasVisiblePixel = true;
					break;
				}

				if (!hasVisiblePixel)
					continue;

				frameBottom = y + 1;
				break;
			}

			Assert.True(frameBottom > 0, $"frame {frame} has no visible pixels");
			minBottom = Math.Min(minBottom, frameBottom);
			maxBottom = Math.Max(maxBottom, frameBottom);
		}

		Assert.True(maxBottom - minBottom <= tolerance, $"ground line mismatch: min={minBottom}, max={maxBottom}");
	}

	private static string GetRepoPath(params string[] parts) =>
		Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", Path.Combine(parts)));

	private sealed class AirOnlyGenerator : IMapGenerator
	{
		public string Id => "air_only";
		public string Name => "Air Only";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
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
}
