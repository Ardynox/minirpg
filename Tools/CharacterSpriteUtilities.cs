using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

/// <summary>
/// 所有"程序化角色 sprite"生成器（怪物 / 玩家 / NPC）共用的几何绘制 + 8 方向投影管线。
///
/// 设计契约：
///   ① 所有源 bitmap 使用 64x64 canvas（<see cref="CanvasSize"/>），锚点统一在
///      (<see cref="TargetAnchorCenterX"/>, <see cref="TargetAnchorBottomY"/>)。
///   ② 通过 <see cref="BuildProjectedDirectionalSheet"/> 把单帧投影成 8 方向 256x256 sheet
///      （见 <see cref="DirectionalTransforms"/>），与现有 monster_map_iso8 PNG 完全同款锚点 / 帧位 /
///      像素密度，可直接被 IsometricVoxelRenderer + entity_render.json (type:"texture") 消费。
///
/// 使用方法见 <see cref="MonsterMapAssetGenerator"/>（怪物路径）和
/// <see cref="BaseHumanSpriteGenerator"/>（人形路径）。
/// </summary>
internal static class CharacterSpriteUtilities
{
	public const int CanvasSize = 64;
	public const int ExportScale = 4;
	public const int ExportSize = CanvasSize * ExportScale;
	public const int DirectionCount = 8;
	public const double TargetAnchorCenterX = 31.5;
	public const int TargetAnchorBottomY = 63;

	public static readonly DirectionalTransform[] DirectionalTransforms =
	[
		new(0.74f, 1.06f, 0.00f, 1.10f, false),
		new(0.88f, 1.02f, -0.16f, 1.05f, true),
		new(1.00f, 1.00f, 0.00f, 1.00f, true),
		new(0.88f, 0.96f, 0.16f, 0.94f, true),
		new(0.74f, 0.98f, 0.00f, 0.80f, false),
		new(0.88f, 0.96f, -0.16f, 0.94f, false),
		new(1.00f, 1.00f, 0.00f, 1.00f, false),
		new(0.88f, 1.02f, 0.16f, 1.05f, false),
	];

	public static Bitmap NewCanvas() =>
		new(CanvasSize, CanvasSize, PixelFormat.Format32bppArgb);

	public static Graphics CreateGraphics(Image image)
	{
		var graphics = Graphics.FromImage(image);
		graphics.CompositingMode = CompositingMode.SourceOver;
		graphics.CompositingQuality = CompositingQuality.HighSpeed;
		graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
		graphics.PixelOffsetMode = PixelOffsetMode.Half;
		graphics.SmoothingMode = SmoothingMode.None;
		return graphics;
	}

	public static void DrawShadow(Graphics graphics, int centerX, int centerY, int width, int height, int alpha)
	{
		using var brush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
		graphics.FillEllipse(brush, Box(centerX - width / 2, centerY - height / 2, centerX + width / 2, centerY + height / 2));
	}

	public static void FillEllipseOutline(Graphics graphics, Rectangle rectangle, Color fill, Color outline)
	{
		using var outlineBrush = new SolidBrush(outline);
		using var fillBrush = new SolidBrush(fill);
		graphics.FillEllipse(outlineBrush, Expand(rectangle, 1));
		graphics.FillEllipse(fillBrush, rectangle);
	}

	public static void FillRectangleOutline(Graphics graphics, Rectangle rectangle, Color fill, Color outline)
	{
		using var outlineBrush = new SolidBrush(outline);
		using var fillBrush = new SolidBrush(fill);
		graphics.FillRectangle(outlineBrush, Expand(rectangle, 1));
		graphics.FillRectangle(fillBrush, rectangle);
	}

	public static void FillPolygonOutline(Graphics graphics, Point[] points, Color fill, Color outline)
	{
		using var outlineBrush = new SolidBrush(outline);
		using var fillBrush = new SolidBrush(fill);
		graphics.FillPolygon(outlineBrush, Offset(points, -1, -1));
		graphics.FillPolygon(fillBrush, points);
	}

	public static void FillPolygon(Graphics graphics, Point[] points, Color fill)
	{
		using var brush = new SolidBrush(fill);
		graphics.FillPolygon(brush, points);
	}

	public static Rectangle Expand(Rectangle rectangle, int amount) =>
		Rectangle.FromLTRB(rectangle.Left - amount, rectangle.Top - amount, rectangle.Right + amount, rectangle.Bottom + amount);

	public static Rectangle Box(int left, int top, int right, int bottom) =>
		Rectangle.FromLTRB(left, top, right, bottom);

	public static Point[] Offset(Point[] points, int dx, int dy)
	{
		var shifted = new Point[points.Length];
		for (var i = 0; i < points.Length; i++)
			shifted[i] = new Point(points[i].X + dx, points[i].Y + dy);
		return shifted;
	}

	public static Point P(int x, int y) => new(x, y);

	public static Color Rgba(int r, int g, int b, int a = 255) => Color.FromArgb(a, r, g, b);

	public static int ClampToByte(float value) => Math.Clamp((int)Math.Round(value), 0, 255);

	public static Color ApplyBrightness(Color color, float brightness)
	{
		var r = ClampToByte(color.R * brightness);
		var g = ClampToByte(color.G * brightness);
		var b = ClampToByte(color.B * brightness);
		return Color.FromArgb(color.A, r, g, b);
	}

	public static Bitmap CropToAlpha(Bitmap bitmap)
	{
		var bounds = AlphaBounds(bitmap);
		return bitmap.Clone(bounds, PixelFormat.Format32bppArgb);
	}

	public static Rectangle AlphaBounds(Bitmap bitmap)
	{
		var minX = bitmap.Width;
		var minY = bitmap.Height;
		var maxX = -1;
		var maxY = -1;

		for (var y = 0; y < bitmap.Height; y++)
		{
			for (var x = 0; x < bitmap.Width; x++)
			{
				if (bitmap.GetPixel(x, y).A == 0)
					continue;
				minX = Math.Min(minX, x);
				minY = Math.Min(minY, y);
				maxX = Math.Max(maxX, x);
				maxY = Math.Max(maxY, y);
			}
		}

		if (maxX < minX || maxY < minY)
			throw new InvalidOperationException("Encountered an empty source frame (no opaque pixels).");

		return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
	}

	public static Bitmap NormalizeAnchor(Bitmap source)
	{
		var bounds = AlphaBounds(source);
		var centerX = (bounds.Left + bounds.Right - 1) / 2.0;
		var bottomY = bounds.Bottom - 1;
		var shiftX = (int)Math.Round(TargetAnchorCenterX - centerX);
		var shiftY = TargetAnchorBottomY - bottomY;

		var bitmap = NewCanvas();
		using var graphics = CreateGraphics(bitmap);
		graphics.DrawImage(source, shiftX, shiftY, source.Width, source.Height);
		return bitmap;
	}

	public static Bitmap Upscale(Bitmap source)
	{
		var bitmap = new Bitmap(ExportSize, ExportSize, PixelFormat.Format32bppArgb);
		using var graphics = CreateGraphics(bitmap);
		graphics.DrawImage(source, 0, 0, ExportSize, ExportSize);
		return bitmap;
	}

	public static Bitmap ResizeNearest(Bitmap source, int width, int height)
	{
		var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
		using var graphics = CreateGraphics(bitmap);
		graphics.DrawImage(source, 0, 0, width, height);
		return bitmap;
	}

	/// <summary>
	/// 把一张 normalized canvas（锚点已统一）投影成 8 方向 256x256 vertical sheet，
	/// 与现有 monster_map_iso8 / *_8dir.png 完全同款排布，可直接被
	/// `entity_render.json` 的 <c>type:"texture"</c> + <c>useFacing:true</c> 消费。
	/// </summary>
	public static Bitmap BuildProjectedDirectionalSheet(Bitmap normalizedSource)
	{
		var sheet = new Bitmap(ExportSize, ExportSize * DirectionCount, PixelFormat.Format32bppArgb);
		using var graphics = CreateGraphics(sheet);
		for (var directionIndex = 0; directionIndex < DirectionalTransforms.Length; directionIndex++)
		{
			using var frame = ProjectDirectionalFrame(normalizedSource, DirectionalTransforms[directionIndex]);
			using var export = Upscale(frame);
			graphics.DrawImage(export, 0, directionIndex * ExportSize, ExportSize, ExportSize);
		}
		return sheet;
	}

	public static Bitmap ProjectDirectionalFrame(Bitmap normalizedSource, DirectionalTransform transform)
	{
		var frame = NewCanvas();
		var signedScaleX = transform.Mirror ? -transform.ScaleX : transform.ScaleX;
		for (var y = 0; y < CanvasSize; y++)
		{
			for (var x = 0; x < CanvasSize; x++)
			{
				var relX = x - TargetAnchorCenterX;
				var relY = y - TargetAnchorBottomY;
				var sourceRelY = relY / transform.ScaleY;
				var sourceRelX = (relX - transform.ShearX * sourceRelY) / signedScaleX;
				var sourceX = (int)Math.Round(TargetAnchorCenterX + sourceRelX);
				var sourceY = (int)Math.Round(TargetAnchorBottomY + sourceRelY);
				if (sourceX < 0 || sourceX >= normalizedSource.Width || sourceY < 0 || sourceY >= normalizedSource.Height)
					continue;

				var color = normalizedSource.GetPixel(sourceX, sourceY);
				if (color.A == 0)
					continue;

				frame.SetPixel(x, y, ApplyBrightness(color, transform.Brightness));
			}
		}
		return frame;
	}
}

/// <summary>
/// 8 方向投影参数：(scaleX, scaleY, shearX, brightness, mirror)。
/// scaleX/Y 控制纵深压扁；shearX 让侧后方向有轻微 X 偏移营造"半侧"视角；
/// brightness 让朝光方向（北 / 北侧）更亮，背光方向更暗；mirror 用于复用东半侧帧画西半侧。
/// </summary>
internal readonly record struct DirectionalTransform(
	float ScaleX,
	float ScaleY,
	float ShearX,
	float Brightness,
	bool Mirror);
