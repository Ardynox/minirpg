using System;
using Godot;

namespace MiniRPG.Module.Render;

/// <summary>
/// 体素面图像生成工具：top diamond、side parallelogram、边缘增强。
/// 被 IsometricVoxelRenderer（运行时 fallback）和 TerrainAtlas（启动时图集构建）共用。
/// </summary>
internal static class VoxelFaceImageUtil
{
	public const int TopFaceWidth = 128;
	public const int TopFaceHeight = 64;
	public const int SideTextureWidth = 64;
	public const int SideTextureHeight = 176;
	public const int SideFaceHeight = (int)IsoCoordUtil.ZStep;
	public const int WallSideFaceHeight = SideFaceHeight;

	public static Image BuildTopDiamond(Image tileImage)
	{
		const int targetW = TopFaceWidth;
		const int targetH = TopFaceHeight;
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

	public static Image EnhanceTopFaceEdges(Image diamond, float outlineStrength)
	{
		var width = diamond.GetWidth();
		var height = diamond.GetHeight();
		var outlined = (Image)diamond.Duplicate();
		outlineStrength = Math.Clamp(outlineStrength, 0f, 0.75f);

		for (var y = 1; y < height - 1; y++)
		for (var x = 1; x < width - 1; x++)
		{
			var c = diamond.GetPixel(x, y);
			if (c.A <= 0.01f)
				continue;

			var isEdge = diamond.GetPixel(x - 1, y).A <= 0.01f
				|| diamond.GetPixel(x + 1, y).A <= 0.01f
				|| diamond.GetPixel(x, y - 1).A <= 0.01f
				|| diamond.GetPixel(x, y + 1).A <= 0.01f;
			if (!isEdge)
				continue;

			var factor = 1f - outlineStrength;
			outlined.SetPixel(x, y, new Color(c.R * factor, c.G * factor, c.B * factor, c.A));
		}

		return outlined;
	}

	public static Image GenerateSideFace(Image tileImage, bool isRight, float darken, int faceHeight)
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

	public static void EnhanceSideFaceEdge(Image sideFace, bool isRight, float edgeStrength)
	{
		edgeStrength = Math.Clamp(edgeStrength, 0f, 0.75f);
		if (edgeStrength <= 0f)
			return;

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

	public static Color SampleArea(Image img, int cx, int cy, int w, int h)
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
}
