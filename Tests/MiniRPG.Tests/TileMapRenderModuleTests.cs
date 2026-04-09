using System.Reflection;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class TileMapRenderModuleTests
{
	[Fact]
	public void ResolveAnimatedFrames_PrefersClipCoords_WhenPresent()
	{
		var entry = new TileMapRenderModule.TileSourceEntry
		{
			SourceId = 42,
			IsAnimated = true,
			FrameSourceIds = [101, 102],
			FrameCoords =
			[
				new TileMapRenderModule.TileFrameCoordEntry { AtlasX = 3, AtlasY = 4 },
				new TileMapRenderModule.TileFrameCoordEntry { AtlasX = 4, AtlasY = 4 },
			],
		};

		var frames = TileMapRenderModule.ResolveAnimatedFrames(entry);

		Assert.Collection(
			frames,
			frame =>
			{
				Assert.Equal(42, frame.SourceId);
				Assert.Equal(new Vector2I(3, 4), frame.Coord);
			},
			frame =>
			{
				Assert.Equal(42, frame.SourceId);
				Assert.Equal(new Vector2I(4, 4), frame.Coord);
			});
	}

	[Fact]
	public void ResolveAnimatedFrames_FallsBackToLegacyFrameSourceIds()
	{
		var entry = new TileMapRenderModule.TileSourceEntry
		{
			SourceId = 42,
			IsAnimated = true,
			FrameSourceIds = [7, -1, 9],
		};

		var frames = TileMapRenderModule.ResolveAnimatedFrames(entry);

		Assert.Collection(
			frames,
			frame =>
			{
				Assert.Equal(7, frame.SourceId);
				Assert.Equal(Vector2I.Zero, frame.Coord);
			},
			frame =>
			{
				Assert.Equal(9, frame.SourceId);
				Assert.Equal(Vector2I.Zero, frame.Coord);
			});
	}

	[Fact(Skip = "Requires Godot runtime-initialized native objects; headless unit host may crash with AccessViolation.")]
	public void ToggleRenderMode_AlwaysForcesIsoOnlyAndHidesTilemapLayers()
	{
		LocalizationService.Initialize();
		var module = new TileMapRenderModule(new GameState(), new FogOfWarTracker(), viewW: 20, viewH: 20);

		SetField(module, "_isometricMode", false);
		SetField(module, "_voxelRoot", new Node2D { Visible = false });
		SetField(module, "_groundLayer", new TileMapLayer { Visible = true });
		SetField(module, "_memoryLayer", new TileMapLayer { Visible = true });
		SetField(module, "_overlayLayer", new TileMapLayer { Visible = true });
		SetField(module, "_memoryOverlayLayer", new TileMapLayer { Visible = true });
		SetField(module, "_peripheralLayer", new TileMapLayer { Visible = true });
		SetField(module, "_peripheralOverlayLayer", new TileMapLayer { Visible = true });
		SetField(module, "_peripheralEntityLayer", new TileMapLayer { Visible = true });
		SetField(module, "_entityLayer", new TileMapLayer { Visible = true });
		SetField(module, "_fogLayer", new TileMapLayer { Visible = true });
		SetField(module, "_editorHighlightBaseLayer", new TileMapLayer { Visible = true });
		SetField(module, "_editorHighlightOverlayLayer", new TileMapLayer { Visible = true });
		SetField(module, "_weatherOverlayRoot", new Node2D { Visible = true });
		SetField(module, "_peripheralWeatherOverlayRoot", new Node2D { Visible = true });
		SetField(module, "_memoryWeatherOverlayRoot", new Node2D { Visible = true });
		SetField(module, "_groundItemRoot", new Node2D { Visible = true });
		SetField(module, "_peripheralGroundItemRoot", new Node2D { Visible = true });
		SetField(module, "_weatherFxRoot", new Node2D { Visible = true });
		SetField(module, "_peripheralWeatherFxRoot", new Node2D { Visible = true });
		SetField(module, "_entitySpriteRoot", new Node2D { Visible = true });
		SetField(module, "_peripheralEntitySpriteRoot", new Node2D { Visible = true });

		var message = module.ToggleRenderMode();

		Assert.True(module.IsIsometricMode);
		Assert.Equal(LocalizationService.T("render.view_mode.iso_only"), message);
		Assert.True(((Node2D)GetField(module, "_voxelRoot")).Visible);

		Assert.False(((TileMapLayer)GetField(module, "_groundLayer")).Visible);
		Assert.False(((TileMapLayer)GetField(module, "_memoryLayer")).Visible);
		Assert.False(((TileMapLayer)GetField(module, "_overlayLayer")).Visible);
		Assert.False(((TileMapLayer)GetField(module, "_memoryOverlayLayer")).Visible);
		Assert.False(((TileMapLayer)GetField(module, "_peripheralLayer")).Visible);
		Assert.False(((TileMapLayer)GetField(module, "_peripheralOverlayLayer")).Visible);
		Assert.False(((TileMapLayer)GetField(module, "_peripheralEntityLayer")).Visible);
		Assert.False(((TileMapLayer)GetField(module, "_entityLayer")).Visible);
		Assert.False(((TileMapLayer)GetField(module, "_fogLayer")).Visible);
		Assert.False(((TileMapLayer)GetField(module, "_editorHighlightBaseLayer")).Visible);
		Assert.False(((TileMapLayer)GetField(module, "_editorHighlightOverlayLayer")).Visible);
		Assert.False(((Node2D)GetField(module, "_weatherOverlayRoot")).Visible);
		Assert.False(((Node2D)GetField(module, "_peripheralWeatherOverlayRoot")).Visible);
		Assert.False(((Node2D)GetField(module, "_memoryWeatherOverlayRoot")).Visible);
		Assert.False(((Node2D)GetField(module, "_groundItemRoot")).Visible);
		Assert.False(((Node2D)GetField(module, "_peripheralGroundItemRoot")).Visible);
		Assert.False(((Node2D)GetField(module, "_weatherFxRoot")).Visible);
		Assert.False(((Node2D)GetField(module, "_peripheralWeatherFxRoot")).Visible);
		Assert.False(((Node2D)GetField(module, "_entitySpriteRoot")).Visible);
		Assert.False(((Node2D)GetField(module, "_peripheralEntitySpriteRoot")).Visible);
	}

	[Fact(Skip = "Requires Godot runtime-initialized native objects; headless unit host may crash with AccessViolation.")]
	public void ToggleRenderMode_RepeatedCalls_StayInIsoOnlyState()
	{
		LocalizationService.Initialize();
		var module = new TileMapRenderModule(new GameState(), new FogOfWarTracker(), viewW: 20, viewH: 20);

		SetField(module, "_isometricMode", false);
		SetField(module, "_voxelRoot", new Node2D { Visible = false });
		SetField(module, "_groundLayer", new TileMapLayer { Visible = true });
		SetField(module, "_memoryLayer", new TileMapLayer { Visible = true });
		SetField(module, "_overlayLayer", new TileMapLayer { Visible = true });
		SetField(module, "_memoryOverlayLayer", new TileMapLayer { Visible = true });
		SetField(module, "_peripheralLayer", new TileMapLayer { Visible = true });
		SetField(module, "_peripheralOverlayLayer", new TileMapLayer { Visible = true });
		SetField(module, "_peripheralEntityLayer", new TileMapLayer { Visible = true });
		SetField(module, "_entityLayer", new TileMapLayer { Visible = true });
		SetField(module, "_fogLayer", new TileMapLayer { Visible = true });
		SetField(module, "_editorHighlightBaseLayer", new TileMapLayer { Visible = true });
		SetField(module, "_editorHighlightOverlayLayer", new TileMapLayer { Visible = true });
		SetField(module, "_weatherOverlayRoot", new Node2D { Visible = true });
		SetField(module, "_peripheralWeatherOverlayRoot", new Node2D { Visible = true });
		SetField(module, "_memoryWeatherOverlayRoot", new Node2D { Visible = true });
		SetField(module, "_groundItemRoot", new Node2D { Visible = true });
		SetField(module, "_peripheralGroundItemRoot", new Node2D { Visible = true });
		SetField(module, "_weatherFxRoot", new Node2D { Visible = true });
		SetField(module, "_peripheralWeatherFxRoot", new Node2D { Visible = true });
		SetField(module, "_entitySpriteRoot", new Node2D { Visible = true });
		SetField(module, "_peripheralEntitySpriteRoot", new Node2D { Visible = true });

		var first = module.ToggleRenderMode();
		var second = module.ToggleRenderMode();

		Assert.True(module.IsIsometricMode);
		Assert.Equal(LocalizationService.T("render.view_mode.iso_only"), first);
		Assert.Equal(LocalizationService.T("render.view_mode.iso_only"), second);
		Assert.True(((Node2D)GetField(module, "_voxelRoot")).Visible);
		Assert.False(((TileMapLayer)GetField(module, "_groundLayer")).Visible);
		Assert.False(((TileMapLayer)GetField(module, "_entityLayer")).Visible);
	}

	[Fact(Skip = "Requires Godot runtime-initialized native objects in test host.")]
	public void StepZoom_WithoutCamera_ReturnsFalse()
	{
		var module = new TileMapRenderModule(new GameState(), new FogOfWarTracker(), viewW: 20, viewH: 20);

		var changed = module.StepZoom(1);

		Assert.False(changed);
		Assert.Equal(1.0f, module.Zoom);
	}

	[Fact(Skip = "Requires Godot runtime-initialized native objects in test host.")]
	public void StepZoom_RespectsBoundsAndStopsAtLimit()
	{
		var module = new TileMapRenderModule(new GameState(), new FogOfWarTracker(), viewW: 20, viewH: 20);
		SetField(module, "_camera", new Camera2D());

		Assert.True(module.StepZoom(1));
		Assert.Equal(1.1f, module.Zoom, 3);
		Assert.True(module.StepZoom(1));
		Assert.Equal(1.2f, module.Zoom, 3);
		Assert.True(module.StepZoom(1));
		Assert.Equal(1.25f, module.Zoom, 3);
		Assert.False(module.StepZoom(1));
		Assert.Equal(1.25f, module.Zoom, 3);

		Assert.True(module.StepZoom(-1));
		Assert.Equal(1.15f, module.Zoom, 3);
		Assert.True(module.StepZoom(-1));
		Assert.Equal(1.05f, module.Zoom, 3);
		Assert.True(module.StepZoom(-1));
		Assert.Equal(0.95f, module.Zoom, 3);
		Assert.True(module.StepZoom(-1));
		Assert.Equal(0.9f, module.Zoom, 3);
		Assert.False(module.StepZoom(-1));
		Assert.Equal(0.9f, module.Zoom, 3);
	}

	private static void SetField(object target, string fieldName, object? value)
	{
		var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(field);
		field!.SetValue(target, value);
	}

	private static object GetField(object target, string fieldName)
	{
		var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(field);
		return field!.GetValue(target)!;
	}
}
