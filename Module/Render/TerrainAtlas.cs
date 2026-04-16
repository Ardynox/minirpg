using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using MiniRPG.Core.Data;
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
		LoadCustomMappings();

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

		var topW = VoxelFaceImageUtil.TopFaceWidth;
		var topH = VoxelFaceImageUtil.TopFaceHeight;
		var sideW = VoxelFaceImageUtil.SideTextureWidth;
		var sideH = VoxelFaceImageUtil.SideTextureHeight;
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

	private const float LeftDarken = 0.65f;
	private const float RightDarken = 0.80f;
	private const float WallLeftSideDarken = 0.52f;
	private const float WallRightSideDarken = 0.68f;

	private const float WallTopEdgeStrength = 0.30f;
	private const float SolidTopEdgeStrength = 0.22f;
	private const float NonSolidTopEdgeStrength = 0.12f;
	private const float WallSideEdgeStrength = 0.20f;
	private const float SolidSideEdgeStrength = 0.12f;
	private const float NonSolidSideEdgeStrength = 0.06f;

	private const string VoxelTileRoot = "res://Assets/Art/Generated/voxel_tiles";

	private Dictionary<string, VoxelTileMappingEntry> _customMappings = new(StringComparer.OrdinalIgnoreCase);

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
		if (_customMappings.TryGetValue(terrain.StringId, out var custom) && !string.IsNullOrWhiteSpace(custom.TopTilePath))
		{
			var customImg = LoadImageSource(custom.TopTilePath);
			if (customImg != null)
			{
				if (custom.TopIsIso)
				{
					var scaled = ScaleToFit(customImg, VoxelFaceImageUtil.TopFaceWidth, VoxelFaceImageUtil.TopFaceHeight, custom.TopScaleX, custom.TopScaleY, custom.TopOffsetX, custom.TopOffsetY);
					return scaled;
				}
				var diamond = VoxelFaceImageUtil.BuildTopDiamond(customImg);
				return VoxelFaceImageUtil.EnhanceTopFaceEdges(diamond, GetTopEdgeStrength(terrain));
			}
		}

		var topPath = ResolveVoxelTopTexturePath(terrain);
		if (!string.IsNullOrWhiteSpace(topPath))
		{
			var sourceImage = LoadImageSource(topPath);
			if (sourceImage != null)
			{
				var diamond = VoxelFaceImageUtil.BuildTopDiamond(sourceImage);
				return VoxelFaceImageUtil.EnhanceTopFaceEdges(diamond, GetTopEdgeStrength(terrain));
			}
		}

		// Fallback: solid-color diamond
		var color = TerrainColors.GetValueOrDefault(terrain.StringId, new Color(0.5f, 0.5f, 0.5f));
		return CreateDiamondImage(VoxelFaceImageUtil.TopFaceWidth, VoxelFaceImageUtil.TopFaceHeight, color);
	}

	// ── Side face generation ──

	private Image GenerateSideFace(TerrainDef terrain, bool isRight)
	{
		if (_customMappings.TryGetValue(terrain.StringId, out var custom))
		{
			var showSide = isRight ? custom.ShowRightSide : custom.ShowLeftSide;
			if (!showSide)
				return Image.CreateEmpty(VoxelFaceImageUtil.SideTextureWidth, VoxelFaceImageUtil.SideTextureHeight, false, Image.Format.Rgba8);
		}

		Image? sideSourceImage = null;

		if (custom != null)
		{
			var mode = isRight ? custom.RightSideMode : custom.LeftSideMode;
			var path = isRight ? custom.RightSideTilePath : custom.LeftSideTilePath;
			var colorHex = isRight ? custom.RightSideColor : custom.LeftSideColor;
			var isIso = isRight ? custom.RightIsIso : custom.LeftIsIso;

			if (mode == "color" && !string.IsNullOrEmpty(colorHex))
			{
				sideSourceImage = CreateSolidImage(VoxelFaceImageUtil.SideTextureWidth, VoxelFaceImageUtil.SideTextureHeight, new Color(colorHex));
			}
			else if (mode == "texture" && !string.IsNullOrEmpty(path))
			{
				var img = LoadImageSource(path);
				if (img != null)
				{
					if (isIso)
					{
						var sideOx = isRight ? custom.RightOffsetX : custom.LeftOffsetX;
						var sideOy = isRight ? custom.RightOffsetY : custom.LeftOffsetY;
						var scaled = ScaleToFit(img, VoxelFaceImageUtil.SideTextureWidth, VoxelFaceImageUtil.SideTextureHeight, custom.TopScaleX, custom.TopScaleY, sideOx, sideOy);
						return scaled;
					}
					sideSourceImage = img;
				}
			}
		}

		if (sideSourceImage == null)
		{
			var sidePath = ResolveVoxelSideTexturePath(terrain);
			if (!string.IsNullOrWhiteSpace(sidePath))
				sideSourceImage = LoadImageSource(sidePath);
		}

		if (sideSourceImage == null)
		{
			var topPath = ResolveVoxelTopTexturePath(terrain);
			if (!string.IsNullOrWhiteSpace(topPath))
				sideSourceImage = LoadImageSource(topPath);
		}

		if (sideSourceImage == null)
		{
			var color = TerrainColors.GetValueOrDefault(terrain.StringId, new Color(0.5f, 0.5f, 0.5f));
			sideSourceImage = CreateSolidImage(VoxelFaceImageUtil.SideTextureWidth, VoxelFaceImageUtil.SideTextureHeight, color);
		}

		var wallLike = IsWallTerrain(terrain);
		var darken = isRight
			? (wallLike ? WallRightSideDarken : RightDarken)
			: (wallLike ? WallLeftSideDarken : LeftDarken);
		var perSideHeight = isRight
			? (custom is { RightHeight: > 0 } ? custom.RightHeight : 0)
			: (custom is { LeftHeight: > 0 } ? custom.LeftHeight : 0);
		var faceHeight = perSideHeight > 0
			? perSideHeight
			: (wallLike ? VoxelFaceImageUtil.WallSideFaceHeight : VoxelFaceImageUtil.SideFaceHeight);

		var image = VoxelFaceImageUtil.GenerateSideFace(sideSourceImage, isRight, darken, faceHeight);
		VoxelFaceImageUtil.EnhanceSideFaceEdge(image, isRight, GetSideEdgeStrength(terrain));
		return image;
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

	private readonly Dictionary<string, Image?> _imageLoadCache = new(StringComparer.OrdinalIgnoreCase);

	private Image? LoadImageSource(string path)
	{
		var normalizedPath = PzTilePathUtility.NormalizeAssetPath(path);
		if (_imageLoadCache.TryGetValue(normalizedPath, out var cached))
			return cached;

		Image? loaded = null;
		var projectPath = PzTilePathUtility.GetProjectFilePath(normalizedPath);
		if (File.Exists(projectPath))
		{
			loaded = Image.LoadFromFile(projectPath);
		}

		_imageLoadCache[normalizedPath] = loaded;
		return loaded;
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

	private static Image ScaleToFit(Image src, int targetW, int targetH, float customScaleX = 1f, float customScaleY = 1f, float customOffsetX = 0f, float customOffsetY = 0f)
	{
		var sw = src.GetWidth();
		var sh = src.GetHeight();

		var scaleX = targetW / (float)sw * customScaleX;
		var scaleY = targetH / (float)sh * customScaleY;
		var scale = Math.Min(scaleX, scaleY);
		var newW = Math.Max(1, (int)Math.Round(sw * scale));
		var newH = Math.Max(1, (int)Math.Round(sh * scale));

		var scaled = (Image)src.Duplicate();
		scaled.Resize(newW, newH, Image.Interpolation.Bilinear);

		var result = Image.CreateEmpty(targetW, targetH, false, Image.Format.Rgba8);
		var offsetX = (targetW - newW) / 2 + (int)customOffsetX;
		var offsetY = (int)customOffsetY;
		result.BlitRect(scaled, new Rect2I(0, 0, newW, newH), new Vector2I(offsetX, offsetY));
		return result;
	}

	/// <summary>将 src 图像复制到 dest 的 (destX, destY) 位置。</summary>
	private static void BlitImage(Image dest, Image src, int destX, int destY)
	{
		var srcW = src.GetWidth();
		var srcH = src.GetHeight();
		dest.BlitRect(src, new Rect2I(0, 0, srcW, srcH), new Vector2I(destX, destY));
	}

	private void LoadCustomMappings()
	{
		_customMappings.Clear();
		try
		{
			var document = VoxelTileMappingStore.Load();
			foreach (var entry in document.Entries)
			{
				if (!string.IsNullOrWhiteSpace(entry.TerrainId))
					_customMappings[entry.TerrainId] = entry;
			}

			if (_customMappings.Count > 0)
				Console.WriteLine($"[TerrainAtlas] Loaded {_customMappings.Count} custom tile mappings.");
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[TerrainAtlas] Failed to load custom mappings: {ex.Message}");
		}
	}
}
