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

	/// <summary>
	/// 草地 overlay（SurfaceCover）变体在图集内的 region，由 <see cref="Build"/> 末尾 procedural 生成并入图。
	/// 顺序即 variant 索引（0..GrassOverlayVariantCount-1）。
	/// </summary>
	private readonly List<Rect2> _grassOverlayRegions = new();

	/// <summary>
	/// 草地 overlay 目标变体数量；与 <c>Module.Render.Surface.GrassOverlayPass.VariantCount</c> 对齐。
	/// 改这里时记得同步对面那一处常量。
	/// </summary>
	private const int GrassOverlayVariantTarget = 6;

	/// <summary>打包后的单张图集纹理，所有面共用。</summary>
	public Texture2D? AtlasTexture => _atlasTexture;

	/// <summary>是否已完成构建。</summary>
	public bool IsBuilt => _atlasTexture != null;

	/// <summary>本次 Build 输出的草地 overlay 变体数量；用于 Wave 2.2 渲染器对齐。</summary>
	public int GrassOverlayVariantCount => _grassOverlayRegions.Count;

	/// <summary>
	/// 查询指定地形的图集区域。
	/// </summary>
	public bool TryGetRegions(string terrainStringId, out FaceRegions regions) =>
		_regions.TryGetValue(terrainStringId, out regions);

	/// <summary>
	/// 查询指定草地 overlay 变体（0..<see cref="GrassOverlayVariantCount"/>-1）在图集中的 region。
	/// 越界返回 false 且 region 为 default。
	/// </summary>
	public bool TryGetGrassOverlayVariant(int index, out Rect2 region)
	{
		if (index < 0 || index >= _grassOverlayRegions.Count)
		{
			region = default;
			return false;
		}
		region = _grassOverlayRegions[index];
		return true;
	}

	/// <summary>
	/// 构建图集：遍历所有已注册地形，为每种地形生成 top/left/right 面并打包进单张纹理。
	/// 应在 Init 阶段调用一次。
	/// </summary>
	public void Build()
	{
		_regions.Clear();
		_grassOverlayRegions.Clear();
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
		// Row 0: top faces      (128×64 each, N 张)
		// Row 1: left faces     (64×176 each, N 张)
		// Row 2: right faces    (64×176 each, N 张)
		// Row 3: grass overlays (128×64 each, GrassOverlayVariantTarget 张)

		var topW = VoxelFaceImageUtil.TopFaceWidth;
		var topH = VoxelFaceImageUtil.TopFaceHeight;
		var sideW = VoxelFaceImageUtil.SideTextureWidth;
		var sideH = VoxelFaceImageUtil.SideTextureHeight;
		var count = faceEntries.Count;

		// top row: N faces × 128 wide
		var topRowWidth = count * topW;
		// side rows: N faces × 64 wide
		var sideRowWidth = count * sideW;
		// overlay row: GrassOverlayVariantTarget faces × 128 wide
		var overlayRowWidth = GrassOverlayVariantTarget * topW;

		var atlasWidth = Math.Max(Math.Max(topRowWidth, sideRowWidth), overlayRowWidth);
		var atlasHeight = topH + sideH + sideH + topH; // 64 + 176 + 176 + 64 = 480

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

		// ── 第三遍：Blit grass overlay 变体到 row 3 ──
		// 6 个 procedural 变体（密度/朝向/簇大小三维差异）；与 GrassOverlayPass.VariantCount 对齐。
		// 共用同一张 _atlasTexture，让 Wave 2.2 渲染器走 DrawTextureRectRegion 自动合批。
		var overlayY = topH + sideH + sideH;
		for (var i = 0; i < GrassOverlayVariantTarget; i++)
		{
			var overlayImage = CreateGrassOverlayImage(i, topW, topH);
			var overlayX = i * topW;
			BlitImage(atlasImage, overlayImage, overlayX, overlayY);
			_grassOverlayRegions.Add(new Rect2(overlayX, overlayY, topW, topH));
		}

		_atlasTexture = ImageTexture.CreateFromImage(atlasImage);

		// 释放图片加载缓存：图集已 blit 完成，原始 Image 不再需要常驻内存
		_imageLoadCache.Clear();
	}

	// ══════════════════════════════════════════════════════
	//  面生成 — 从 IsometricVoxelRenderer 提取的核心算法
	// ══════════════════════════════════════════════════════

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
				return VoxelFaceImageUtil.EnhanceTopFaceEdges(diamond, VoxelTerrainShading.TopEdgeStrength(terrain));
			}
		}

		var topPath = VoxelTilePathResolver.ResolveTopPath(terrain);
		if (!string.IsNullOrWhiteSpace(topPath))
		{
			var sourceImage = LoadImageSource(topPath);
			if (sourceImage != null)
			{
				var diamond = VoxelFaceImageUtil.BuildTopDiamond(sourceImage);
				return VoxelFaceImageUtil.EnhanceTopFaceEdges(diamond, VoxelTerrainShading.TopEdgeStrength(terrain));
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
			var sidePath = VoxelTilePathResolver.ResolveSidePath(terrain);
			if (!string.IsNullOrWhiteSpace(sidePath))
				sideSourceImage = LoadImageSource(sidePath);
		}

		if (sideSourceImage == null)
		{
			var topPath = VoxelTilePathResolver.ResolveTopPath(terrain);
			if (!string.IsNullOrWhiteSpace(topPath))
				sideSourceImage = LoadImageSource(topPath);
		}

		if (sideSourceImage == null)
		{
			var color = TerrainColors.GetValueOrDefault(terrain.StringId, new Color(0.5f, 0.5f, 0.5f));
			sideSourceImage = CreateSolidImage(VoxelFaceImageUtil.SideTextureWidth, VoxelFaceImageUtil.SideTextureHeight, color);
		}

		var wallLike = VoxelTerrainShading.IsWall(terrain);
		var darken = isRight
			? VoxelTerrainShading.RightDarken(terrain)
			: VoxelTerrainShading.LeftDarken(terrain);
		var perSideHeight = isRight
			? (custom is { RightHeight: > 0 } ? custom.RightHeight : 0)
			: (custom is { LeftHeight: > 0 } ? custom.LeftHeight : 0);
		var faceHeight = perSideHeight > 0
			? perSideHeight
			: (wallLike ? VoxelFaceImageUtil.WallSideFaceHeight : VoxelFaceImageUtil.SideFaceHeight);

		var image = VoxelFaceImageUtil.GenerateSideFace(sideSourceImage, isRight, darken, faceHeight);
		VoxelFaceImageUtil.EnhanceSideFaceEdge(image, isRight, VoxelTerrainShading.SideEdgeStrength(terrain));
		return image;
	}

	// ══════════════════════════════════════════════════════
	//  工具方法
	// ══════════════════════════════════════════════════════

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
			if (loaded != null && loaded.GetFormat() != Image.Format.Rgba8)
				loaded.Convert(Image.Format.Rgba8);
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

	// ══════════════════════════════════════════════════════
	//  Grass overlay procedural 生成（路 C，Wave 1）
	//  6 个变体在三个维度上有可见差异：密度（3 档）× 朝向（横/纵/各向同性）× 簇大小。
	//  仅做顶面 diamond 区域，alpha 边缘羽化，避免肉眼 tile-repeat 感。
	// ══════════════════════════════════════════════════════

	/// <summary>
	/// procedural 生成一张草地 overlay：在等距菱形蒙版内基于多频 hash value-noise 铺出 patchy 草丛。
	/// </summary>
	private static Image CreateGrassOverlayImage(int variant, int width, int height)
	{
		// 每个变体：density / freqX / freqY / clumpScale / hueShift
		//   density   : 草盖率，0..1（越高草越多）→ 控密度
		//   freqX/Y   : 各向异性 noise 频率倍数（>1 → 该方向变化更快、视觉上垂直该方向的"条纹"）→ 控朝向
		//   clumpScale: noise 采样基础尺度（像素），越大簇越大、越平滑 → 控簇形状
		//   hueShift  : 草色色调偏移，正→偏黄、负→偏深绿
		var (density, freqX, freqY, clumpScale, hueShift) = variant switch
		{
			0 => (0.30f, 1.0f, 1.0f, 4f, +0.05f),  // V0: 稀疏散点 + 黄绿
			1 => (0.55f, 1.0f, 1.0f, 4f,  0.00f),  // V1: 中密度散点 + 标准绿
			2 => (0.78f, 1.0f, 1.0f, 4f, -0.05f),  // V2: 密集 + 深绿
			3 => (0.55f, 0.4f, 2.0f, 5f,  0.00f),  // V3: 横向条纹（X 频率低、Y 频率高）
			4 => (0.55f, 2.0f, 0.4f, 5f, +0.03f),  // V4: 纵向条纹
			5 => (0.55f, 0.7f, 0.7f, 8f, -0.03f),  // V5: 大簇 cluster
			_ => (0.50f, 1.0f, 1.0f, 4f,  0.00f),
		};

		var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
		var halfW = width / 2;
		var halfH = height / 2;

		var baseR = Math.Clamp(0.30f + hueShift, 0f, 1f);
		var baseG = Math.Clamp(0.70f - 0.04f * hueShift, 0f, 1f);
		var baseB = Math.Clamp(0.20f - hueShift * 0.5f, 0f, 1f);

		var threshold = 1f - density;

		for (var py = 0; py < height; py++)
		for (var px = 0; px < width; px++)
		{
			// diamond 蒙版（与 CreateDiamondImage 同算法）：菱形外保持透明
			var dx = Math.Abs(px - halfW) / (float)halfW;
			var dy = Math.Abs(py - halfH) / (float)halfH;
			if (dx + dy > 1.0f)
			{
				image.SetPixel(px, py, Colors.Transparent);
				continue;
			}

			// 多频 fBm-like value-noise：3 个 octave，频率 1×/2×/4×，振幅 1/0.5/0.25
			var nx = px * freqX / clumpScale;
			var ny = py * freqY / clumpScale;
			var n0 = SmoothNoise01(nx,        ny,        variant * 31 + 7);
			var n1 = SmoothNoise01(nx * 2.0f, ny * 2.0f, variant * 31 + 11) * 0.5f;
			var n2 = SmoothNoise01(nx * 4.0f, ny * 4.0f, variant * 31 + 13) * 0.25f;
			var noise = (n0 + n1 + n2) / 1.75f; // 归一化 0..1

			if (noise < threshold)
			{
				image.SetPixel(px, py, Colors.Transparent);
				continue;
			}

			// 草色 + per-pixel 抖动；alpha 按 strength 羽化让 patch 边缘不锐利
			var strength = (noise - threshold) / Math.Max(1e-4f, 1f - threshold);
			var jr = (HashNoise01(px, py, variant * 7 + 23) - 0.5f) * 0.10f;
			var jg = (HashNoise01(px, py, variant * 7 + 29) - 0.5f) * 0.10f;
			var jb = (HashNoise01(px, py, variant * 7 + 31) - 0.5f) * 0.10f;
			var r = Math.Clamp(baseR + jr, 0f, 1f);
			var g = Math.Clamp(baseG + jg, 0f, 1f);
			var b = Math.Clamp(baseB + jb, 0f, 1f);
			var a = Math.Clamp(0.55f + strength * 0.45f, 0f, 1f); // 0.55..1.0

			image.SetPixel(px, py, new Color(r, g, b, a));
		}

		return image;
	}

	/// <summary>
	/// 双线性插值 + smoothstep 的 value noise → [0, 1)；同输入恒同输出。
	/// </summary>
	private static float SmoothNoise01(float x, float y, int seed)
	{
		var x0 = (int)Math.Floor(x);
		var y0 = (int)Math.Floor(y);
		var fx = x - x0;
		var fy = y - y0;

		var n00 = HashNoise01(x0,     y0,     seed);
		var n10 = HashNoise01(x0 + 1, y0,     seed);
		var n01 = HashNoise01(x0,     y0 + 1, seed);
		var n11 = HashNoise01(x0 + 1, y0 + 1, seed);

		// smoothstep
		fx = fx * fx * (3f - 2f * fx);
		fy = fy * fy * (3f - 2f * fy);

		var n0 = n00 * (1f - fx) + n10 * fx;
		var n1 = n01 * (1f - fx) + n11 * fx;
		return n0 * (1f - fy) + n1 * fy;
	}

	/// <summary>
	/// FNV-1a 风格整数 hash → [0, 1) 浮点；纯函数，同输入恒同输出。
	/// </summary>
	private static float HashNoise01(int x, int y, int seed)
	{
		unchecked
		{
			uint h = 2166136261u ^ (uint)seed;
			h = (h ^ (uint)x) * 16777619u;
			h = (h ^ (uint)y) * 16777619u;
			h ^= h >> 13;
			h *= 0x5BD1E995u;
			h ^= h >> 15;
			return (h & 0x00FFFFFFu) / (float)0x01000000;
		}
	}
}
