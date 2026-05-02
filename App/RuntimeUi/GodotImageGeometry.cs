using System;
using Godot;

namespace MiniRPG;

/// <summary>
/// Godot.Image 上的纯几何绘制 helper（FillEllipse / FillTriangle / FillRoundRect / Arc / ThickLine /
/// 带 alpha blend 的 SetPixel 等）。两个使用方：
///   ① <see cref="ProceduralFacePartRenderer"/>：肖像 256x256 部件画
///   ② <see cref="MapSpriteRuntimeFactory"/>：地图 sprite 64x64 base human + 8 方向投影
///
/// Godot.Image 自身只暴露 SetPixel/GetPixel/Fill/BlendRect 等基础 API，没有 ellipse/triangle 之类
/// 矢量绘制。这里集中实现一次，避免每个 caller 自己 setpixel 重复造轮子。
///
/// 不依赖任何业务类型，仅用 Godot.Color / Godot.Image。
/// </summary>
internal static class GodotImageGeometry
{
	public static void DrawFilledEllipse(Image image, int cx, int cy, float radiusX, float radiusY, Color color)
	{
		var minX = Math.Max(0, (int)Math.Floor(cx - radiusX));
		var maxX = Math.Min(image.GetWidth() - 1, (int)Math.Ceiling(cx + radiusX));
		var minY = Math.Max(0, (int)Math.Floor(cy - radiusY));
		var maxY = Math.Min(image.GetHeight() - 1, (int)Math.Ceiling(cy + radiusY));
		var rxSq = radiusX * radiusX;
		var rySq = radiusY * radiusY;
		for (var py = minY; py <= maxY; py++)
		for (var px = minX; px <= maxX; px++)
		{
			var dx = (px + 0.5f) - cx;
			var dy = (py + 0.5f) - cy;
			if (dx * dx / rxSq + dy * dy / rySq <= 1f)
				BlendPixel(image, px, py, color);
		}
	}

	public static void DrawFilledRoundRect(Image image, int x, int y, int width, int height, int radius, Color color)
	{
		radius = Math.Min(radius, Math.Min(width, height) / 2);
		var minX = Math.Max(0, x);
		var maxX = Math.Min(image.GetWidth() - 1, x + width - 1);
		var minY = Math.Max(0, y);
		var maxY = Math.Min(image.GetHeight() - 1, y + height - 1);
		var leftCornerX = x + radius;
		var rightCornerX = x + width - 1 - radius;
		var topCornerY = y + radius;
		var bottomCornerY = y + height - 1 - radius;
		var radiusSq = (float)(radius * radius);
		for (var py = minY; py <= maxY; py++)
		for (var px = minX; px <= maxX; px++)
		{
			float dx = 0, dy = 0;
			if (px < leftCornerX)
				dx = leftCornerX - px;
			else if (px > rightCornerX)
				dx = px - rightCornerX;
			if (py < topCornerY)
				dy = topCornerY - py;
			else if (py > bottomCornerY)
				dy = py - bottomCornerY;
			if (dx == 0 && dy == 0)
			{
				BlendPixel(image, px, py, color);
				continue;
			}
			if (dx * dx + dy * dy <= radiusSq)
				BlendPixel(image, px, py, color);
		}
	}

	public static void DrawFilledTriangle(Image image, int x0, int y0, int x1, int y1, int x2, int y2, Color color)
	{
		var minX = Math.Max(0, Math.Min(x0, Math.Min(x1, x2)));
		var maxX = Math.Min(image.GetWidth() - 1, Math.Max(x0, Math.Max(x1, x2)));
		var minY = Math.Max(0, Math.Min(y0, Math.Min(y1, y2)));
		var maxY = Math.Min(image.GetHeight() - 1, Math.Max(y0, Math.Max(y1, y2)));
		for (var py = minY; py <= maxY; py++)
		for (var px = minX; px <= maxX; px++)
		{
			if (PointInTriangle(px + 0.5f, py + 0.5f, x0, y0, x1, y1, x2, y2))
				BlendPixel(image, px, py, color);
		}
	}

	public static void DrawFilledDiamond(Image image, int cx, int cy, int halfWidth, int halfHeight, Color color)
	{
		DrawFilledTriangle(image, cx - halfWidth, cy, cx + halfWidth, cy, cx, cy - halfHeight, color);
		DrawFilledTriangle(image, cx - halfWidth, cy, cx + halfWidth, cy, cx, cy + halfHeight, color);
	}

	public static void DrawFilledRectangle(Image image, int x, int y, int width, int height, Color color)
	{
		var minX = Math.Max(0, x);
		var maxX = Math.Min(image.GetWidth() - 1, x + width - 1);
		var minY = Math.Max(0, y);
		var maxY = Math.Min(image.GetHeight() - 1, y + height - 1);
		for (var py = minY; py <= maxY; py++)
		for (var px = minX; px <= maxX; px++)
			BlendPixel(image, px, py, color);
	}

	/// <summary>
	/// 在 (cx,cy) 中心、椭圆半径 (rx,ry) 上绘制角度区间 [start, end] 的厚弧。
	/// 角度按数学正方向（CCW），单位弧度，0 = 右；y 轴向下因此反向。
	/// </summary>
	public static void DrawArc(Image image, int cx, int cy, float rx, float ry, float startAngle, float endAngle, float thickness, Color color)
	{
		var samples = Math.Max(16, (int)Math.Ceiling(MathF.Max(rx, ry) * MathF.Abs(endAngle - startAngle)));
		var step = (endAngle - startAngle) / samples;
		var prevX = cx + rx * MathF.Cos(startAngle);
		var prevY = cy - ry * MathF.Sin(startAngle);
		for (var i = 1; i <= samples; i++)
		{
			var a = startAngle + step * i;
			var nx = cx + rx * MathF.Cos(a);
			var ny = cy - ry * MathF.Sin(a);
			DrawThickLine(image, prevX, prevY, nx, ny, thickness, color);
			prevX = nx;
			prevY = ny;
		}
	}

	public static void DrawThickLine(Image image, float x0, float y0, float x1, float y1, float thickness, Color color)
	{
		var halfThickness = thickness * 0.5f;
		var minX = Math.Max(0, (int)Math.Floor(Math.Min(x0, x1) - halfThickness - 1f));
		var maxX = Math.Min(image.GetWidth() - 1, (int)Math.Ceiling(Math.Max(x0, x1) + halfThickness + 1f));
		var minY = Math.Max(0, (int)Math.Floor(Math.Min(y0, y1) - halfThickness - 1f));
		var maxY = Math.Min(image.GetHeight() - 1, (int)Math.Ceiling(Math.Max(y0, y1) + halfThickness + 1f));
		var dx = x1 - x0;
		var dy = y1 - y0;
		var lengthSq = dx * dx + dy * dy;
		if (lengthSq <= float.Epsilon)
		{
			DrawFilledEllipse(image, (int)x0, (int)y0, halfThickness, halfThickness, color);
			return;
		}

		var thresholdSq = halfThickness * halfThickness;
		for (var py = minY; py <= maxY; py++)
		for (var px = minX; px <= maxX; px++)
		{
			var pointX = px + 0.5f;
			var pointY = py + 0.5f;
			var projection = ((pointX - x0) * dx + (pointY - y0) * dy) / lengthSq;
			projection = Math.Clamp(projection, 0f, 1f);
			var closestX = x0 + dx * projection;
			var closestY = y0 + dy * projection;
			var ddx = pointX - closestX;
			var ddy = pointY - closestY;
			if (ddx * ddx + ddy * ddy <= thresholdSq)
				BlendPixel(image, px, py, color);
		}
	}

	public static void BlendPixel(Image image, int x, int y, Color color)
	{
		if (color.A >= 0.999f)
		{
			image.SetPixel(x, y, color);
			return;
		}

		var existing = image.GetPixel(x, y);
		var srcA = color.A;
		var dstA = existing.A * (1f - srcA);
		var outA = srcA + dstA;
		if (outA <= float.Epsilon)
			return;

		var outR = (color.R * srcA + existing.R * dstA) / outA;
		var outG = (color.G * srcA + existing.G * dstA) / outA;
		var outB = (color.B * srcA + existing.B * dstA) / outA;
		image.SetPixel(x, y, new Color(outR, outG, outB, outA));
	}

	public static Color MultiplyAlpha(Color c, float scale) => new(c.R, c.G, c.B, c.A * scale);

	public static Color ApplyBrightness(Color c, float brightness)
	{
		return new Color(
			Math.Clamp(c.R * brightness, 0f, 1f),
			Math.Clamp(c.G * brightness, 0f, 1f),
			Math.Clamp(c.B * brightness, 0f, 1f),
			c.A);
	}

	private static bool PointInTriangle(float px, float py, int x0, int y0, int x1, int y1, int x2, int y2)
	{
		var d1 = Sign(px, py, x0, y0, x1, y1);
		var d2 = Sign(px, py, x1, y1, x2, y2);
		var d3 = Sign(px, py, x2, y2, x0, y0);
		var hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
		var hasPos = d1 > 0 || d2 > 0 || d3 > 0;
		return !(hasNeg && hasPos);
	}

	private static float Sign(float px, float py, int ax, int ay, int bx, int by) =>
		(px - bx) * (ay - by) - (ax - bx) * (py - by);
}
