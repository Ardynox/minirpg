using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.World;

namespace MiniRPG.Module.Render;

/// <summary>
/// 地形面纹理图集：将所有地形的 top/left/right 面纹理打包进单张大纹理。
/// 渲染时所有 DrawTextureRectRegion 共用同一张图集 → Godot CanvasItem 自动合批 → draw call 从数千降至个位数。
/// </summary>
public sealed class TerrainAtlas
{
	/// <summary>单个地形在图集中的三面区域。</summary>
	public readonly record struct FaceRegions(Rect2 Top, Rect2 Left, Rect2 Right);

	private ImageTexture? _atlasTexture;
	private readonly Dictionary<string, FaceRegions> _regions = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>打包后的单张图集纹理，所有面共用。</summary>
	public Texture2D? AtlasTexture => _atlasTexture;

	/// <summary>是否已完成构建。</summary>
	public bool IsBuilt => _atlasTexture != null;

	/// <summary>
	/// 查询指定地形的图集区域。
	/// </summary>
	public bool TryGetRegions(string terrainStringId, out FaceRegions regions) =>
		_regions.TryGetValue(terrainStringId, out regions);

	/// <summary>
	/// 构建图集：遍历所有已注册地形，为每种地形生成 top/left/right 面并打包进单张纹理。
	/// 应在 Init 阶段调用一次。
	/// </summary>
	public void Build()
	{
		_regions.Clear();
		_atlasTexture?.Dispose();
		_atlasTexture = null;

		var terrains = TerrainRegistry.All;
		if (terrains.Count == 0)
			return;

		// ── 第一遍：为每个地形生成三面 Image ──
		var faceEntries = new List<(string TerrainId, Image Top, Image Left, Image Right)>();
		foreach (var terrain in terrains)
		{
			if (string.IsNullOrWhiteSpace(terrain.StringId))
				continue;
			if (terrain.StringId is Terrains.Void or Terrains.Air)
				continue;

			var top = GenerateTopFace(terrain);
			var left = GenerateSideFace(terrain, isRight: false);
			var right = GenerateSideFace(terrain, isRight: true);
			faceEntries.Add((terrain.StringId, top, left, right));
		}

		if (faceEntries.Count == 0)
			return;

		// ── 布局计算 ──
		// Row 0: top faces  (128×64 each)
		// Row 1: left faces (64×176 each)
		// Row 2: right faces (64×176 each)

		var topW = TopFaceWidth;
		var topH = TopFaceHeight;
		var sideW = SideTextureWidth;
		var sideH = SideTextureHeight;
		var count = faceEntries.Count;

		// top row: N faces × 128 wide
		var topRowWidth = count * topW;
		// side rows: N faces × 64 wide
		var sideRowWidth = count * sideW;

		var atlasWidth = Math.Max(topRowWidth, sideRowWidth);
		var atlasHeight = topH + sideH + sideH; // 64 + 176 + 176 = 416

		// Round up to next multiple of 4 for GPU alignment
		atlasWidth = (atlasWidth + 3) & ~3;
		atlasHeight = (atlasHeight + 3) & ~3;

		// ── 第二遍：Blit 到图集 Image ──
		var atlasImage = Image.CreateEmpty(atlasWidth, atlasHeight, false, Image.Format.Rgba8);

		for (var i = 0; i < faceEntries.Count; i++)
		{
			var entry = faceEntries[i];

			// Top face → row 0
			var topX = i * topW;
			var topY = 0;
			BlitImage(atlasImage, entry.Top, topX, topY);

			// Left face → row 1
			var leftX = i * sideW;
			var leftY = topH;
			BlitImage(atlasImage, entry.Left, leftX, leftY);

			// Right face → row 2
			var rightX = i * sideW;
			var rightY = topH + sideH;
			BlitImage(atlasImage, entry.Right, rightX, rightY);

			_regions[entry.TerrainId] = new FaceRegions(
				new Rect2(topX, topY, topW, topH),
				new Rect2(leftX, leftY, sideW, sideH),
				new Rect2(rightX, rightY, sideW, sideH));
		}

		_atlasTexture = ImageTexture.CreateFromImage(atlasImage);
	}

	// ══════════════════════════════════════════════════════
	//  面生成 — 从 IsometricVoxelRenderer 提取的核心算法
	// ══════════════════════════════════════════════════════

	private const int TopFaceWidth = 128;
	private const int TopFaceHeight = 64;
	private const int SideTextureWidth = 64;
	private const int SideTextureHeight = 176;
	private const int SideFaceHeight = (int)IsoCoordUtil.ZStep; // 84

	private const float LeftDarken = 0.65f;
	private const float RightDarken = 0.80f;
	private const float WallLeftSideDarken = 0.52f;
	private const float WallRightSideDarken = 0.68f;
	private const int WallSideFaceHeight = SideFaceHeight;

	private const float WallTopEdgeStrength = 0.30f;
	private const float SolidTopEdgeStrength = 0.22f;
	private const float NonSolidTopEdgeStrength = 0.12f;
	private const float WallSideEdgeStrength = 0.20f;
	private const float SolidSideEdgeStrength = 0.12f;
	private const float NonSolidSideEdgeStrength = 0.06f;

	private const string VoxelTileRoot = "res://Assets/Art/Generated/voxel_tiles";

	// ── Fallback terrain colors (when no tile image available) ──
	private static readonly Dictionary<string, Color> TerrainColors = new()
	{
		[Terrains.GrassBlock] = new Color(0.3f, 0.7f, 0.2f),
		[Terrains.Grass] = new Color(0.3f, 0.7f, 0.2f),
		[Terrains.Dirt] = new Color(0.55f, 0.35f, 0.15f),
		[Terrains.Stone] = new Color(0.5f, 0.5f, 0.5f),
		[Terrains.Sand] = new Color(0.9f, 0.85f, 0.6f),
		[Terrains.Water] = new Color(0.2f, 0.4f, 0.8f, 0.7f),
		[Terrains.Mountain] = new Color(0.4f, 0.4f, 0.45f),
		[Terrains.WallStone] = new Color(0.45f, 0.45f, 0.45f),
		[Terrains.WallSoil] = new Color(0.5f, 0.3f, 0.15f),
		[Terrains.WallGranite] = new Color(0.6f, 0.55f, 0.5f),
		[Terrains.WallObsidian] = new Color(0.15f, 0.1f, 0.2f),
		[Terrains.Tree] = new Color(0.15f, 0.5f, 0.1f),
		[Terrains.Snow] = new Color(0.95f, 0.95f, 1.0f),
		[Terrains.Ice] = new Color(0.7f, 0.85f, 1.0f, 0.8f),
		[Terrains.Lava] = new Color(1.0f, 0.3f, 0.0f),
		[Terrains.Floor] = new Color(0.6f, 0.55f, 0.45f),
		[Terrains.Rubble] = new Color(0.5f, 0.45f, 0.35f),
	};

	// ── Tile alias mapping ──
	private static readonly Dictionary<string, string> VoxelTileAliases = new(StringComparer.OrdinalIgnoreCase)
	{
		["grass_top"] = "tile_grass_top.png",
		["grass_side"] = "tile_grass_side.png",
		["dirt"] = "tile_dirt.png",
		["dirt_side"] = "tile_dirt.png",
		["soil"] = "tile_dirt.png",
		["soil_side"] = "tile_dirt.png",
		["stone"] = "tile_stone.png",
		["stone_side"] = "tile_stone.png",
		["sand"] = "tile_sand.png",
		["sand_side"] = "tile_sand.png",
		["gravel"] = "tile_gravel.png",
		["gravel_side"] = "tile_gravel.png",
		["mud"] = "tile_mud.png",
		["mud_side"] = "tile_mud.png",
		["clay"] = "tile_clay.png",
		["clay_side"] = "tile_clay.png",
		["snow"] = "tile_snow.png",
		["snow_side"] = "tile_snow.png",
		["ash"] = "tile_ash.png",
		["ash_side"] = "tile_ash.png",
		["plank"] = "tile_plank.png",
		["plank_side"] = "tile_plank.png",
		["log_top"] = "tile_log_top.png",
		["log_side"] = "tile_log_side.png",
		["brick"] = "tile_brick.png",
		["brick_side"] = "tile_brick.png",
		["brick_mossy"] = "tile_brick_mossy.png",
		["brick_mossy_side"] = "tile_brick_mossy.png",
		["brick_cracked"] = "tile_brick_cracked.png",
		["brick_cracked_side"] = "tile_brick_cracked.png",
		["ore_coal"] = "ores/tile_stone_ore_coal.png",
		["ore_iron"] = "ores/tile_stone_ore_iron.png",
		["ore_copper"] = "ores/tile_stone_ore_copper.png",
		["ore_gold"] = "ores/tile_stone_ore_gold.png",
		["ore_crystal"] = "ores/tile_stone_ore_crystal.png",
		["wall_stone_side"] = "tile_stone.png",
		["wall_granite_side"] = "tile_stone.png",
		["wall_obsidian_side"] = "tile_stone.png",
		["wall_iron_side"] = "tile_stone.png",
		["mountain_side"] = "tile_stone.png",
		["rubble"] = "tile_gravel.png",
		["rubble_side"] = "tile_gravel.png",
	};

	// ── Top face generation ──

	private Image GenerateTopFace(TerrainDef terrain)
	{
		var topPath = ResolveVoxelTopTexturePath(terrain);
		if (!string.IsNullOrWhiteSpace(topPath))
		{
			var sourceTexture = LoadTexture(topPath);
			if (sourceTexture != null)
			{
				var sourceImage = ExtractImage(sourceTexture);
				if (sourceImage != null)
				{
					var diamond = BuildTopDiamondFromTile(sourceImage);
					return EnhanceTopFaceEdges(diamond, GetTopEdgeStrength(terrain));
				}
			}
		}

		// Fallback: solid-color diamond
		var color = TerrainColors.GetValueOrDefault(terrain.StringId, new Color(0.5f, 0.5f, 0.5f));
		return CreateDiamondImage(TopFaceWidth, TopFaceHeight, color);
	}

	// ── Side face generation ──

	private Image GenerateSideFace(TerrainDef terrain, bool isRight)
	{
		Image? sideSourceImage = null;

		// Try side-specific texture first
		var sidePath = ResolveVoxelSideTexturePath(terrain);
		if (!string.IsNullOrWhiteSpace(sidePath))
		{
			var sideTexture = LoadTexture(sidePath);
			if (sideTexture != null)
				sideSourceImage = ExtractImage(sideTexture);
		}

		// Fallback to top texture
		if (sideSourceImage == null)
		{
			var topPath = ResolveVoxelTopTexturePath(terrain);
			if (!string.IsNullOrWhiteSpace(topPath))
			{
				var topTexture = LoadTexture(topPath);
				if (topTexture != null)
					sideSourceImage = ExtractImage(topTexture);
			}
		}

		// Last resort: solid-color side
		if (sideSourceImage == null)
		{
			var color = TerrainColors.GetValueOrDefault(terrain.StringId, new Color(0.5f, 0.5f, 0.5f));
			sideSourceImage = CreateSolidImage(SideTextureWidth, SideTextureHeight, color);
		}

		var wallLike = IsWallTerrain(terrain);
		var darken = isRight
			? (wallLike ? WallRightSideDarken : RightDarken)
			: (wallLike ? WallLeftSideDarken : LeftDarken);
		var faceHeight = wallLike ? WallSideFaceHeight : SideFaceHeight;

		var image = GenerateSideFaceFromTile(sideSourceImage, isRight, darken, faceHeight);
		EnhanceSideFaceEdge(image, isRight, GetSideEdgeStrength(terrain));
		return image;
	}

	// ══════════════════════════════════════════════════════
	//  核心图像算法（从 IsometricVoxelRenderer 原样提取）
	// ══════════════════════════════════════════════════════

	private static Image BuildTopDiamondFromTile(Image tileImage)
	{
		const int targetW = TopFaceWidth;  // 128
		const int targetH = TopFaceHeight; // 64
		var result = Image.CreateEmpty(targetW, targetH, false, Image.Format.Rgba8);
		var srcW = tileImage.GetWidth();
		var srcH = tileImage.GetHeight();
		var halfW = targetW / 2f;
		var halfH = targetH / 2f;

		for (var y = 0; y < targetH; y++)
		{
			for (var x = 0; x < targetW; x++)
			{
				var nx = (x - halfW) / halfW;
				var ny = (y - halfH) / halfH;
				if (Math.Abs(nx) + Math.Abs(ny) > 1f)
				{
					result.SetPixel(x, y, Colors.Transparent);
					continue;
				}

				var u = (nx - ny + 1f) * 0.5f;
				var v = (nx + ny + 1f) * 0.5f;
				var sx = Math.Clamp((int)Math.Round(u * (srcW - 1)), 0, srcW - 1);
				var sy = Math.Clamp((int)Math.Round(v * (srcH - 1)), 0, srcH - 1);
				result.SetPixel(x, y, tileImage.GetPixel(sx, sy));
			}
		}

		return result;
	}

	private static Image EnhanceTopFaceEdges(Image diamond, float outlineStrength)
	{
		var width = diamond.GetWidth();
		var height = diamond.GetHeight();
		var outlined = (Image)diamond.Duplicate();
		outlineStrength = Math.Clamp(outlineStrength, 0f, 0.75f);

		for (var y = 1; y < height - 1; y++)
		for (var x = 1; x < width - 1; x++)
		{
			var c = diamond.GetPixel(x, y);
			if (c.A <= 0.01f) continue;

			var isEdge = diamond.GetPixel(x - 1, y).A <= 0.01f
				|| diamond.GetPixel(x + 1, y).A <= 0.01f
				|| diamond.GetPixel(x, y - 1).A <= 0.01f
				|| diamond.GetPixel(x, y + 1).A <= 0.01f;
			if (!isEdge) continue;

			var factor = 1f - outlineStrength;
			outlined.SetPixel(x, y, new Color(c.R * factor, c.G * factor, c.B * factor, c.A));
		}

		return outlined;
	}

	private static Image GenerateSideFaceFromTile(Image tileImage, bool isRight, float darken, int faceHeight)
	{
		var iw = SideTextureWidth;
		var ih = SideTextureHeight;
		var faceH = Math.Clamp(faceHeight, 24, ih - 8);
		var img = Image.CreateEmpty(iw, ih, false, Image.Format.Rgba8);
		var tw = tileImage.GetWidth();
		var th = tileImage.GetHeight();
		var gradientDepth = faceH >= WallSideFaceHeight - 6 ? 0.22f : 0.17f;

		for (var px = 0; px < iw; px++)
		{
			var t = px / (float)(iw - 1);
			var sampleX = isRight
				? (tw - 1) - (int)Math.Round(t * (tw - 1))
				: (int)Math.Round(t * (tw - 1));
			var pyStart = isRight
				? (int)Math.Round((iw - 1 - px) * IsoCoordUtil.TileHalfH / (double)(iw - 1))
				: (int)Math.Round(px * IsoCoordUtil.TileHalfH / (double)(iw - 1));

			for (var dy = 0; dy < faceH; dy++)
			{
				var py = pyStart + dy;
				if (py >= ih) break;

				var sampleY = Math.Clamp((int)Math.Round(((dy + (th - faceH)) / (float)(th - 1)) * (th - 1)), 0, th - 1);
				var color = SampleArea(tileImage, sampleX, sampleY, tw, th);
				var gradient = 1.0f - (dy / (float)faceH) * gradientDepth;
				var c = color * new Color(darken * gradient, darken * gradient, darken * gradient, 1f);
				c.A = color.A;
				img.SetPixel(px, py, c);
			}
		}

		return img;
	}

	private static void EnhanceSideFaceEdge(Image sideFace, bool isRight, float edgeStrength)
	{
		edgeStrength = Math.Clamp(edgeStrength, 0f, 0.75f);
		if (edgeStrength <= 0f) return;

		var width = sideFace.GetWidth();
		var height = sideFace.GetHeight();
		for (var y = 0; y < height; y++)
		{
			var x = isRight ? width - 1 : 0;
			while (x >= 0 && x < width)
			{
				var c = sideFace.GetPixel(x, y);
				if (c.A > 0.01f)
				{
					var factor = 1f - edgeStrength;
					sideFace.SetPixel(x, y, new Color(c.R * factor, c.G * factor, c.B * factor, c.A));
					var nextX = isRight ? x - 1 : x + 1;
					if (nextX >= 0 && nextX < width)
					{
						var c2 = sideFace.GetPixel(nextX, y);
						if (c2.A > 0.01f)
						{
							var factor2 = 1f - edgeStrength * 0.5f;
							sideFace.SetPixel(nextX, y, new Color(c2.R * factor2, c2.G * factor2, c2.B * factor2, c2.A));
						}
					}
					break;
				}
				x += isRight ? -1 : 1;
			}
		}
	}

	// ══════════════════════════════════════════════════════
	//  工具方法
	// ══════════════════════════════════════════════════════

	private static string ResolveVoxelTopTexturePath(TerrainDef terrain)
	{
		var token = string.IsNullOrWhiteSpace(terrain.TopTile) ? terrain.StringId : terrain.TopTile;
		var fileName = ResolveVoxelFileName(token, isTop: true);
		return string.IsNullOrWhiteSpace(fileName) ? string.Empty : $"{VoxelTileRoot}/{fileName}";
	}

	private static string ResolveVoxelSideTexturePath(TerrainDef terrain)
	{
		var token = string.IsNullOrWhiteSpace(terrain.SideTile) ? terrain.StringId : terrain.SideTile;
		var fileName = ResolveVoxelFileName(token, isTop: false);
		return string.IsNullOrWhiteSpace(fileName) ? string.Empty : $"{VoxelTileRoot}/{fileName}";
	}

	private static string ResolveVoxelFileName(string token, bool isTop)
	{
		if (VoxelTileAliases.TryGetValue(token, out var alias))
			return alias;

		return token switch
		{
			"grass_block" => isTop ? "tile_grass_top.png" : "tile_grass_side.png",
			"grass" => isTop ? "tile_grass_top.png" : "tile_grass_side.png",
			"tree" => "tile_grass_side.png",
			"fungus" => "tile_grass_side.png",
			"dirt" => "tile_dirt.png",
			"swamp" => "tile_dirt.png",
			"marsh" => "tile_dirt.png",
			"sand" => "tile_dirt.png",
			"wall_soil" => "tile_dirt.png",
			"stone" => "tile_stone.png",
			"gravel" => "tile_stone.png",
			"mountain" => "tile_stone.png",
			"rubble" => "tile_stone.png",
			"floor" => "tile_stone.png",
			"wall_stone" => "tile_stone.png",
			"wall_granite" => "tile_stone.png",
			"wall_obsidian" => "tile_stone.png",
			"wall_iron" => "tile_stone.png",
			"crystal_vein" => "tile_stone.png",
			_ => string.Empty,
		};
	}

	private static float GetTopEdgeStrength(TerrainDef terrain)
	{
		if (IsWallTerrain(terrain)) return WallTopEdgeStrength;
		return terrain.Solid ? SolidTopEdgeStrength : NonSolidTopEdgeStrength;
	}

	private static float GetSideEdgeStrength(TerrainDef terrain)
	{
		if (IsWallTerrain(terrain)) return WallSideEdgeStrength;
		return terrain.Solid ? SolidSideEdgeStrength : NonSolidSideEdgeStrength;
	}

	private static bool IsWallTerrain(TerrainDef terrain)
	{
		return terrain.StringId.StartsWith("wall_", StringComparison.OrdinalIgnoreCase)
			|| terrain.StringId.Equals(Terrains.Stone, StringComparison.OrdinalIgnoreCase)
			|| terrain.StringId.Equals(Terrains.Dirt, StringComparison.OrdinalIgnoreCase)
			|| terrain.StringId.Equals(Terrains.Mountain, StringComparison.OrdinalIgnoreCase);
	}

	private readonly Dictionary<string, Texture2D?> _textureLoadCache = new(StringComparer.OrdinalIgnoreCase);

	private Texture2D? LoadTexture(string path)
	{
		if (_textureLoadCache.TryGetValue(path, out var cached))
			return cached;
		var loaded = GD.Load<Texture2D>(path);
		_textureLoadCache[path] = loaded;
		return loaded;
	}

	private static Image? ExtractImage(Texture2D texture)
	{
		if (texture is AtlasTexture atlas)
		{
			var fullImage = atlas.Atlas?.GetImage();
			if (fullImage == null) return null;
			var region = atlas.Region;
			return fullImage.GetRegion(new Rect2I(
				(int)region.Position.X, (int)region.Position.Y,
				(int)region.Size.X, (int)region.Size.Y));
		}
		return texture.GetImage();
	}

	private static Image CreateDiamondImage(int w, int h, Color color)
	{
		var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		var halfW = w / 2;
		var halfH = h / 2;
		for (var py = 0; py < h; py++)
		for (var px = 0; px < w; px++)
		{
			var dx = Math.Abs(px - halfW) / (float)halfW;
			var dy = Math.Abs(py - halfH) / (float)halfH;
			image.SetPixel(px, py, dx + dy <= 1.0f ? color : Colors.Transparent);
		}
		return image;
	}

	private static Image CreateSolidImage(int w, int h, Color color)
	{
		var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		image.Fill(color);
		return image;
	}

	private static Color SampleArea(Image img, int cx, int cy, int w, int h)
	{
		float r = 0, g = 0, b = 0, a = 0;
		var count = 0;
		for (var dy = -1; dy <= 1; dy++)
		for (var dx = -1; dx <= 1; dx++)
		{
			var sx = Math.Clamp(cx + dx, 0, w - 1);
			var sy = Math.Clamp(cy + dy, 0, h - 1);
			var c = img.GetPixel(sx, sy);
			if (c.A < 0.01f) continue;
			r += c.R; g += c.G; b += c.B; a += c.A;
			count++;
		}
		if (count == 0) return new Color(0.5f, 0.5f, 0.5f);
		return new Color(r / count, g / count, b / count, a / count);
	}

	/// <summary>将 src 图像复制到 dest 的 (destX, destY) 位置。</summary>
	private static void BlitImage(Image dest, Image src, int destX, int destY)
	{
		var srcW = src.GetWidth();
		var srcH = src.GetHeight();
		dest.BlitRect(src, new Rect2I(0, 0, srcW, srcH), new Vector2I(destX, destY));
	}
}
