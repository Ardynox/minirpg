using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Module.Render;

namespace MiniRPG.Module.Editor;

internal static class BrushSwatchHelper
{
	public static readonly Dictionary<string, Color> TerrainSwatchColors = new()
	{
		["grass_block"] = new Color(0.3f, 0.7f, 0.2f),
		["grass"] = new Color(0.3f, 0.7f, 0.2f),
		["dirt"] = new Color(0.55f, 0.35f, 0.15f),
		["stone"] = new Color(0.5f, 0.5f, 0.5f),
		["sand"] = new Color(0.9f, 0.85f, 0.6f),
		["water"] = new Color(0.2f, 0.4f, 0.8f),
		["mountain"] = new Color(0.4f, 0.4f, 0.45f),
		["wall_stone"] = new Color(0.45f, 0.45f, 0.45f),
		["wall_soil"] = new Color(0.5f, 0.3f, 0.15f),
		["wall_granite"] = new Color(0.35f, 0.35f, 0.38f),
		["wall_obsidian"] = new Color(0.15f, 0.12f, 0.18f),
		["wall_iron"] = new Color(0.55f, 0.55f, 0.6f),
		["tree"] = new Color(0.15f, 0.45f, 0.1f),
		["lava"] = new Color(1.0f, 0.3f, 0.0f),
		["snow"] = new Color(0.95f, 0.95f, 1.0f),
		["ice"] = new Color(0.7f, 0.85f, 1.0f),
		["floor"] = new Color(0.6f, 0.55f, 0.45f),
		["rubble"] = new Color(0.5f, 0.45f, 0.35f),
		["swamp"] = new Color(0.3f, 0.45f, 0.2f),
		["marsh"] = new Color(0.35f, 0.5f, 0.3f),
		["gravel"] = new Color(0.6f, 0.58f, 0.55f),
		["fungus"] = new Color(0.5f, 0.3f, 0.5f),
		["crystal_vein"] = new Color(0.6f, 0.4f, 0.8f),
		["ore_coal"] = new Color(0.2f, 0.2f, 0.2f),
		["ore_iron"] = new Color(0.55f, 0.45f, 0.35f),
		["ore_copper"] = new Color(0.7f, 0.45f, 0.2f),
	};

	public static readonly Color DefaultSwatchColor = new(0.5f, 0.5f, 0.5f);

	public static void ApplyTerrainSwatchStyle(Button button, string terrainId)
	{
		button.Text = string.Empty;
		var color = TerrainSwatchColors.GetValueOrDefault(terrainId, DefaultSwatchColor);
		var styleNormal = new StyleBoxFlat
		{
			BgColor = color,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
		};
		var stylePressed = new StyleBoxFlat
		{
			BgColor = color,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			BorderColor = new Color(1f, 0.85f, 0.3f),
			BorderWidthBottom = 3,
			BorderWidthTop = 3,
			BorderWidthLeft = 3,
			BorderWidthRight = 3,
		};
		button.AddThemeStyleboxOverride("normal", styleNormal);
		button.AddThemeStyleboxOverride("hover", styleNormal);
		button.AddThemeStyleboxOverride("pressed", stylePressed);
		button.AddThemeStyleboxOverride("focus", stylePressed);
	}

	public static Texture2D? ResolveBrushPreviewTexture(
		Dictionary<BrushPreview, Texture2D?> cache,
		BrushPreview preview)
	{
		if (cache.TryGetValue(preview, out var cached))
			return cached;

		var texture = ResAccess.Get<Texture2D>(preview.TexturePath);
		Texture2D? resolved = null;
		if (texture != null)
			resolved = preview.Region is { } region
				? CreateRegionPreviewTexture(texture, region)
				: texture;

		cache[preview] = resolved;
		return resolved;
	}

	public static bool TryConfigurePreviewButton(
		Button button,
		BrushPreview? preview,
		Dictionary<BrushPreview, Texture2D?> cache)
	{
		if (preview is not { } p)
			return false;

		var texture = ResolveBrushPreviewTexture(cache, p);
		if (texture == null)
			return false;

		button.Text = string.Empty;
		button.Icon = texture;
		button.ExpandIcon = true;
		button.IconAlignment = HorizontalAlignment.Center;
		button.VerticalIconAlignment = VerticalAlignment.Center;
		return true;
	}

	public static void EnsureSelectedBrushVisible(ScrollContainer scroll, GridContainer grid, int selectedIndex)
	{
		if (selectedIndex < 0 || selectedIndex >= grid.GetChildCount())
			return;

		var child = grid.GetChild(selectedIndex);
		if (child is not Control control)
			return;

		var top = control.Position.Y;
		var bottom = top + control.Size.Y;
		var scrollTop = scroll.ScrollVertical;
		var scrollBottom = scrollTop + scroll.Size.Y;

		if (top < scrollTop)
			scroll.ScrollVertical = (int)top;
		else if (bottom > scrollBottom)
			scroll.ScrollVertical = (int)(bottom - scroll.Size.Y);
	}

	private static Texture2D CreateRegionPreviewTexture(Texture2D texture, Rect2I region)
	{
		var clampedRegion = ClampPreviewRegion(texture, region);
		if (clampedRegion.Position == Vector2I.Zero &&
			clampedRegion.Size.X == texture.GetWidth() &&
			clampedRegion.Size.Y == texture.GetHeight())
		{
			return texture;
		}

		return new AtlasTexture
		{
			Atlas = texture,
			Region = new Rect2(
				clampedRegion.Position.X,
				clampedRegion.Position.Y,
				clampedRegion.Size.X,
				clampedRegion.Size.Y),
		};
	}

	private static Rect2I ClampPreviewRegion(Texture2D texture, Rect2I region)
	{
		var textureWidth = Math.Max(1, (int)texture.GetWidth());
		var textureHeight = Math.Max(1, (int)texture.GetHeight());
		var x = Math.Clamp(region.Position.X, 0, textureWidth - 1);
		var y = Math.Clamp(region.Position.Y, 0, textureHeight - 1);
		var width = Math.Clamp(region.Size.X, 1, textureWidth - x);
		var height = Math.Clamp(region.Size.Y, 1, textureHeight - y);
		return new Rect2I(x, y, width, height);
	}
}
