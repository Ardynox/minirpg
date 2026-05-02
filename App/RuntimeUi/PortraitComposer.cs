using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG;

/// <summary>
/// 输入 <see cref="FaceCustomizationData"/>，输出合成后的肖像 <see cref="Texture2D"/>。
/// 双轨：
///   ① <see cref="FacePartEntry.ImagePath"/> 非空且加载成功 → 把 PNG 按 anchor 叠加到 canvas。
///      此时颜色通道（SkinTone/HairColor 等）暂不参与，等批 4 染色 shader 接通后再说。
///   ② 加载失败、ImagePath 为空 → 调 <see cref="ProceduralFacePartRenderer"/> 单层程序生成。
/// 结果按 <see cref="FaceCustomizationData.ComputeCacheKey"/> + 当前 catalog 版本缓存。
/// </summary>
public sealed class PortraitComposer
{
	public const int CanvasWidth = ProceduralFacePartRenderer.CanvasWidth;
	public const int CanvasHeight = ProceduralFacePartRenderer.CanvasHeight;
	private const int MaxCacheEntries = 16;

	private readonly Dictionary<string, ImageTexture> _cache = new();
	private readonly Queue<string> _cacheOrder = new();
	private readonly Dictionary<string, Texture2D?> _partTextureCache = new();
	private readonly HashSet<string> _missingPartWarnings = new(StringComparer.OrdinalIgnoreCase);

	public Texture2D Compose(FaceCustomizationData data)
	{
		var key = data.ComputeCacheKey();
		if (_cache.TryGetValue(key, out var cached))
			return cached;

		var image = Image.CreateEmpty(CanvasWidth, CanvasHeight, false, Image.Format.Rgba8);
		image.Fill(new Color(0f, 0f, 0f, 0f));

		ComposeLayer(image, FacePartCategory.HeadShape, data.HeadShapeId, fallback: img => ProceduralFacePartRenderer.DrawHeadShapeLayer(img, data));
		ComposeLayer(image, FacePartCategory.Ears, data.EarsId, fallback: img => ProceduralFacePartRenderer.DrawEarsLayer(img, data));
		ComposeLayer(image, FacePartCategory.Eyes, data.EyesId, fallback: img => ProceduralFacePartRenderer.DrawEyesLayer(img, data));
		ComposeLayer(image, FacePartCategory.Eyebrows, data.EyebrowsId, fallback: img => ProceduralFacePartRenderer.DrawEyebrowsLayer(img, data));
		ComposeLayer(image, FacePartCategory.Nose, data.NoseId, fallback: img => ProceduralFacePartRenderer.DrawNoseLayer(img, data));
		ComposeLayer(image, FacePartCategory.Mouth, data.MouthId, fallback: img => ProceduralFacePartRenderer.DrawMouthLayer(img, data));
		ComposeLayer(image, FacePartCategory.Hair, data.HairId, fallback: img => ProceduralFacePartRenderer.DrawHairLayer(img, data));
		if (!string.IsNullOrWhiteSpace(data.BeardId) && data.BeardId != "clean_shaven")
			ComposeLayer(image, FacePartCategory.Beard, data.BeardId!, fallback: img => ProceduralFacePartRenderer.DrawBeardLayer(img, data));

		var texture = ImageTexture.CreateFromImage(image);
		_cache[key] = texture;
		_cacheOrder.Enqueue(key);
		EvictIfFull();
		return texture;
	}

	public void Clear()
	{
		foreach (var tex in _cache.Values)
			tex.Dispose();
		_cache.Clear();
		_cacheOrder.Clear();
		_partTextureCache.Clear();
	}

	private void ComposeLayer(Image canvas, FacePartCategory category, string partId, Action<Image> fallback)
	{
		var entry = FacePartCatalog.Find(category, partId);
		var partImage = TryLoadPartImage(entry);
		if (partImage != null)
		{
			BlendPartImage(canvas, partImage, entry!);
			return;
		}

		fallback(canvas);
	}

	private Texture2D? TryLoadPartImage(FacePartEntry? entry)
	{
		if (entry == null || string.IsNullOrWhiteSpace(entry.ImagePath))
			return null;

		var path = entry.ImagePath!;
		if (_partTextureCache.TryGetValue(path, out var cached))
			return cached;

		Texture2D? texture = null;
		try
		{
			if (ResourceLoader.Exists(path))
				texture = ResourceLoader.Load<Texture2D>(path);
		}
		catch
		{
			texture = null;
		}

		if (texture == null && _missingPartWarnings.Add(path))
			GD.Print($"[PortraitComposer] part image '{path}' missing; using procedural fallback.");

		_partTextureCache[path] = texture;
		return texture;
	}

	private static void BlendPartImage(Image canvas, Texture2D texture, FacePartEntry entry)
	{
		var srcImage = texture.GetImage();
		if (srcImage == null)
			return;

		if (srcImage.GetFormat() != Image.Format.Rgba8)
			srcImage.Convert(Image.Format.Rgba8);

		var srcSize = new Vector2I(srcImage.GetWidth(), srcImage.GetHeight());
		var srcRect = new Rect2I(Vector2I.Zero, srcSize);
		var destX = (canvas.GetWidth() - srcSize.X) / 2 + entry.AnchorOffsetX;
		var destY = (canvas.GetHeight() - srcSize.Y) / 2 + entry.AnchorOffsetY;
		canvas.BlendRect(srcImage, srcRect, new Vector2I(destX, destY));
	}

	private void EvictIfFull()
	{
		while (_cacheOrder.Count > MaxCacheEntries)
		{
			var oldestKey = _cacheOrder.Dequeue();
			if (_cache.Remove(oldestKey, out var tex))
				tex.Dispose();
		}
	}
}
