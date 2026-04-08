using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class MonsterMapAssetGenerator
{
    private const int CanvasSize = 64;
    private const int ExportScale = 4;
    private const int ExportSize = CanvasSize * ExportScale;
    private const int FrameSize = 128;
    private const int DirectionCount = 8;
    private const double TargetAnchorCenterX = 31.5;
    private const int TargetAnchorBottomY = 63;
    private static readonly string[] HumanoidDirectionalKeys =
    [
        "monster_goblin",
        "monster_skeleton",
        "monster_orc_warrior",
    ];
    private static readonly HumanoidDirectionalSource[] HumanoidDirectionalSources =
    [
        new("monster_goblin", "Enemy 2", 26, 36),
        new("monster_skeleton", "Enemy 1", 28, 38),
        new("monster_orc_warrior", "Enemy 3", 34, 42),
    ];
    private static readonly DirectionalTransform[] DirectionalTransforms =
    [
        new(0.88f, 0.96f, -0.16f, 0.94f, false),
        new(1.00f, 1.00f, 0.00f, 1.00f, false),
        new(0.88f, 1.02f, 0.16f, 1.05f, false),
        new(0.74f, 1.06f, 0.00f, 1.10f, false),
        new(0.88f, 1.02f, -0.16f, 1.05f, true),
        new(1.00f, 1.00f, 0.00f, 1.00f, true),
        new(0.88f, 0.96f, 0.16f, 0.94f, true),
        new(0.74f, 0.98f, 0.00f, 0.80f, false),
    ];

    public static void Generate(string rootPath)
    {
        var outputDirectory = Path.Combine(rootPath, "Assets", "Art", "Generated", "monster_map");
        var directionalOutputDirectory = Path.Combine(rootPath, "Assets", "Art", "Generated", "monster_map_iso8");
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(directionalOutputDirectory);

        var assets = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase)
        {
            ["monster_goblin"] = PlaceHumanoid(rootPath, "Enemy 2", row: 4, col: 1, maxWidth: 26, maxHeight: 36),
            ["monster_skeleton"] = PlaceHumanoid(rootPath, "Enemy 1", row: 4, col: 2, maxWidth: 28, maxHeight: 38),
            ["monster_orc_warrior"] = PlaceHumanoid(rootPath, "Enemy 3", row: 3, col: 1, maxWidth: 34, maxHeight: 42),
            ["monster_slime"] = BuildSlime(),
            ["monster_spider"] = BuildSpider(),
            ["monster_scorpion"] = BuildScorpion(),
            ["monster_wolf"] = BuildWolf(),
            ["monster_bear"] = BuildBear(),
            ["monster_rat"] = BuildRat(),
            ["monster_treant"] = BuildTreant(),
        };

        foreach (var entry in assets)
        {
            using (entry.Value)
            using (var normalized = NormalizeAnchor(entry.Value))
            using (var export = Upscale(normalized))
            {
                export.Save(Path.Combine(outputDirectory, entry.Key + ".png"), ImageFormat.Png);

                if (Array.IndexOf(HumanoidDirectionalKeys, entry.Key) < 0)
                {
                    using var directionalSheet = BuildProjectedDirectionalSheet(normalized);
                    directionalSheet.Save(
                        Path.Combine(directionalOutputDirectory, entry.Key + "_8dir.png"),
                        ImageFormat.Png);
                }
            }
        }

        foreach (var source in HumanoidDirectionalSources)
        {
            using var directionalSheet = BuildHumanoidDirectionalSheet(
                rootPath,
                source.SheetName,
                source.MaxWidth,
                source.MaxHeight);
            directionalSheet.Save(
                Path.Combine(directionalOutputDirectory, source.Key + "_8dir.png"),
                ImageFormat.Png);
        }
    }

    public static void GenerateAnomalyPack(string rootPath)
    {
        var outputDirectory = Path.Combine(rootPath, "Assets", "Art", "Generated", "monster_map_anomaly");
        var directionalOutputDirectory = Path.Combine(rootPath, "Assets", "Art", "Generated", "monster_map_iso8_anomaly");
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(directionalOutputDirectory);

        var assets = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase)
        {
            ["monster_anomaly_void_hound"] = BuildAnomalyVoidHound(),
            ["monster_anomaly_eye_cluster"] = BuildAnomalyEyeCluster(),
            ["monster_anomaly_flesh_bloom"] = BuildAnomalyFleshBloom(),
            ["monster_anomaly_tentacle_maw"] = BuildAnomalyTentacleMaw(),
        };

        foreach (var entry in assets)
        {
            using (entry.Value)
            using (var normalized = NormalizeAnchor(entry.Value))
            using (var export = Upscale(normalized))
            using (var directionalSheet = BuildProjectedDirectionalSheet(normalized))
            {
                export.Save(Path.Combine(outputDirectory, entry.Key + ".png"), ImageFormat.Png);
                directionalSheet.Save(
                    Path.Combine(directionalOutputDirectory, entry.Key + "_8dir.png"),
                    ImageFormat.Png);
            }
        }
    }

    private static Bitmap BuildProjectedDirectionalSheet(Bitmap normalizedSource)
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

    private static Bitmap BuildHumanoidDirectionalSheet(string rootPath, string sheetName, int maxWidth, int maxHeight)
    {
        var path = Path.Combine(
            rootPath,
            "Assets",
            "Art",
            "Tilesets",
            "FantasyKingdom",
            "FantasyKingdomTileset_Godot",
            "Characters",
            sheetName,
            "Idle.png");

        using var sourceSheet = new Bitmap(path);
        var sheet = new Bitmap(ExportSize, ExportSize * DirectionCount, PixelFormat.Format32bppArgb);
        using var graphics = CreateGraphics(sheet);
        for (var directionRow = 0; directionRow < DirectionCount; directionRow++)
        {
            using var frame = LoadHumanoidFrame(sourceSheet, directionRow, 0);
            using var cropped = CropToAlpha(frame);
            using var canvas = NewCanvas();
            using (var canvasGraphics = CreateGraphics(canvas))
            {
                DrawShadow(canvasGraphics, 32, 55, 20, 6, 72);

                var scale = Math.Min((double)maxWidth / cropped.Width, (double)maxHeight / cropped.Height);
                var width = Math.Max(1, (int)Math.Round(cropped.Width * scale));
                var height = Math.Max(1, (int)Math.Round(cropped.Height * scale));
                using var resized = ResizeNearest(cropped, width, height);
                var x = (CanvasSize - resized.Width) / 2;
                var y = 56 - resized.Height;
                canvasGraphics.DrawImage(resized, x, y, resized.Width, resized.Height);
            }

            using var normalized = NormalizeAnchor(canvas);
            using var export = Upscale(normalized);
            graphics.DrawImage(export, 0, directionRow * ExportSize, ExportSize, ExportSize);
        }

        return sheet;
    }

    private static Bitmap ProjectDirectionalFrame(Bitmap normalizedSource, DirectionalTransform transform)
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

    private static Bitmap PlaceHumanoid(string rootPath, string sheetName, int row, int col, int maxWidth, int maxHeight)
    {
        var canvas = NewCanvas();
        using var graphics = CreateGraphics(canvas);
        DrawShadow(graphics, 32, 55, 20, 6, 72);

        using var frame = LoadHumanoidFrame(rootPath, sheetName, row, col);
        var scale = Math.Min((double)maxWidth / frame.Width, (double)maxHeight / frame.Height);
        var width = Math.Max(1, (int)Math.Round(frame.Width * scale));
        var height = Math.Max(1, (int)Math.Round(frame.Height * scale));
        using var resized = ResizeNearest(frame, width, height);
        var x = (CanvasSize - resized.Width) / 2;
        var y = 56 - resized.Height;
        graphics.DrawImage(resized, x, y, resized.Width, resized.Height);
        return canvas;
    }

    private static Bitmap BuildSlime()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 32, 55, 28, 8, 72);
        FillEllipseOutline(graphics, Box(17, 28, 47, 55), Rgba(69, 178, 120), Rgba(22, 84, 54));
        using var bodyBrush = new SolidBrush(Rgba(110, 228, 168));
        using var eyeBrush = new SolidBrush(Rgba(34, 46, 56));
        using var mouthPen = new Pen(Rgba(34, 46, 56), 1f);
        using var highlightBrush = new SolidBrush(Color.FromArgb(180, 188, 255, 228));
        graphics.FillEllipse(bodyBrush, Box(20, 30, 44, 48));
        graphics.FillEllipse(eyeBrush, Box(23, 36, 28, 41));
        graphics.FillEllipse(eyeBrush, Box(35, 36, 40, 41));
        graphics.DrawArc(mouthPen, Box(27, 40, 37, 47), 15, 150);
        graphics.FillEllipse(highlightBrush, Box(25, 32, 33, 36));
        return bitmap;
    }

    private static Bitmap BuildSpider()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 32, 55, 24, 7, 72);
        using var legPen = new Pen(Rgba(44, 24, 20), 2f);
        for (var yOffset = 0; yOffset <= 9; yOffset += 3)
        {
            graphics.DrawLine(legPen, 16, 34 + yOffset, 8, 28 + yOffset);
            graphics.DrawLine(legPen, 48, 34 + yOffset, 56, 28 + yOffset);
            graphics.DrawLine(legPen, 18, 35 + yOffset, 9, 39 + yOffset);
            graphics.DrawLine(legPen, 46, 35 + yOffset, 55, 39 + yOffset);
        }

        FillEllipseOutline(graphics, Box(23, 30, 41, 44), Rgba(34, 26, 31), Rgba(14, 10, 12));
        FillEllipseOutline(graphics, Box(20, 38, 44, 54), Rgba(55, 39, 43), Rgba(14, 10, 12));
        using var eyeBrush = new SolidBrush(Rgba(214, 84, 82));
        foreach (var eyeX in new[] { 26, 30, 34, 38 })
        {
            graphics.FillRectangle(eyeBrush, Box(eyeX, 35, eyeX + 1, 36));
        }

        return bitmap;
    }

    private static Bitmap BuildScorpion()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 32, 55, 26, 7, 72);
        var baseColor = Rgba(141, 101, 45);
        var dark = Rgba(68, 45, 19);

        foreach (var x in new[] { 24, 29, 34, 39 })
        {
            FillEllipseOutline(graphics, Box(x, 38, x + 6, 45), baseColor, dark);
        }

        FillEllipseOutline(graphics, Box(26, 33, 38, 40), Rgba(167, 124, 56), dark);
        using var legPen = new Pen(dark, 2f);
        graphics.DrawLine(legPen, 23, 41, 15, 36);
        graphics.DrawLine(legPen, 41, 41, 49, 36);
        graphics.DrawLine(legPen, 22, 45, 15, 49);
        graphics.DrawLine(legPen, 42, 45, 49, 49);
        FillPolygon(graphics, new[] { P(14, 34), P(9, 30), P(7, 33), P(11, 38) }, baseColor);
        FillPolygon(graphics, new[] { P(50, 34), P(55, 30), P(57, 33), P(53, 38) }, baseColor);

        var tail = new[] { P(40, 36), P(45, 29), P(48, 21), P(45, 16) };
        using var tailPen = new Pen(dark, 3f);
        for (var i = 0; i < tail.Length - 1; i++)
        {
            graphics.DrawLine(tailPen, tail[i], tail[i + 1]);
        }

        FillPolygon(graphics, new[] { P(44, 16), P(47, 11), P(49, 17) }, Rgba(210, 196, 92));
        return bitmap;
    }

    private static Bitmap BuildWolf()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 32, 55, 30, 7, 72);
        var body = Rgba(103, 110, 126);
        var dark = Rgba(43, 48, 56);
        var light = Rgba(171, 176, 188);

        FillRectangleOutline(graphics, Box(20, 31, 42, 43), body, dark);
        FillPolygonOutline(graphics, new[] { P(40, 31), P(48, 28), P(52, 33), P(47, 39), P(40, 37) }, body, dark);
        FillPolygon(graphics, new[] { P(47, 27), P(50, 22), P(52, 28) }, dark);
        FillPolygon(graphics, new[] { P(43, 28), P(45, 23), P(48, 29) }, dark);
        FillPolygon(graphics, new[] { P(18, 32), P(12, 28), P(9, 30), P(15, 36) }, body);

        foreach (var x in new[] { 23, 30, 38, 45 })
        {
            FillRectangleOutline(graphics, Box(x, 42, x + 2, 54), Rgba(82, 72, 61), dark);
        }

        using var lightPen = new Pen(light, 1f);
        using var muzzleBrush = new SolidBrush(Rgba(245, 220, 190));
        using var eyeBrush = new SolidBrush(Rgba(22, 20, 26));
        graphics.DrawLine(lightPen, 44, 34, 50, 34);
        graphics.FillRectangle(muzzleBrush, Box(46, 33, 47, 34));
        graphics.FillRectangle(eyeBrush, Box(48, 31, 49, 32));
        return bitmap;
    }

    private static Bitmap BuildBear()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 32, 55, 34, 8, 72);
        var body = Rgba(118, 84, 52);
        var dark = Rgba(60, 38, 24);
        var light = Rgba(170, 130, 88);

        FillEllipseOutline(graphics, Box(15, 26, 43, 46), body, dark);
        FillEllipseOutline(graphics, Box(37, 24, 53, 39), body, dark);
        FillEllipseOutline(graphics, Box(41, 20, 46, 25), dark, dark);
        FillEllipseOutline(graphics, Box(47, 20, 52, 25), dark, dark);

        foreach (var x in new[] { 18, 25, 35, 43 })
        {
            FillRectangleOutline(graphics, Box(x, 44, x + 4, 57), Rgba(87, 56, 35), dark);
        }

        using var muzzleBrush = new SolidBrush(light);
        using var darkBrush = new SolidBrush(Rgba(20, 18, 22));
        graphics.FillEllipse(muzzleBrush, Box(43, 29, 49, 34));
        graphics.FillRectangle(darkBrush, Box(48, 29, 49, 30));
        graphics.FillRectangle(darkBrush, Box(45, 26, 46, 27));
        return bitmap;
    }

    private static Bitmap BuildRat()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 32, 55, 18, 5, 72);
        var body = Rgba(109, 102, 108);
        var dark = Rgba(52, 47, 52);
        var pink = Rgba(208, 150, 160);

        FillEllipseOutline(graphics, Box(21, 36, 37, 47), body, dark);
        FillEllipseOutline(graphics, Box(35, 34, 46, 42), body, dark);
        using var pinkBrush = new SolidBrush(pink);
        using var darkBrush = new SolidBrush(Rgba(20, 18, 22));
        using var darkPen = new Pen(dark, 2f);
        graphics.FillEllipse(pinkBrush, Box(38, 31, 42, 35));
        graphics.FillEllipse(pinkBrush, Box(42, 32, 46, 36));
        graphics.DrawLine(new Pen(pink, 2f), 20, 43, 11, 38);
        graphics.DrawLine(new Pen(pink, 2f), 11, 38, 7, 39);
        graphics.FillRectangle(darkBrush, Box(43, 37, 44, 38));
        graphics.FillRectangle(pinkBrush, Box(46, 38, 47, 39));
        graphics.DrawLine(darkPen, 25, 46, 24, 54);
        graphics.DrawLine(darkPen, 31, 46, 30, 54);
        graphics.DrawLine(darkPen, 37, 45, 39, 53);
        return bitmap;
    }

    private static Bitmap BuildTreant()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 32, 55, 32, 8, 72);
        var bark = Rgba(111, 84, 55);
        var dark = Rgba(55, 39, 23);
        var leaf = Rgba(66, 128, 67);

        FillRectangleOutline(graphics, Box(23, 20, 41, 48), bark, dark);
        FillPolygonOutline(graphics, new[] { P(15, 27), P(23, 28), P(22, 33), P(11, 35) }, bark, dark);
        FillPolygonOutline(graphics, new[] { P(41, 29), P(50, 24), P(53, 29), P(43, 36) }, bark, dark);
        FillPolygonOutline(graphics, new[] { P(23, 47), P(18, 58), P(24, 58), P(29, 48) }, bark, dark);
        FillPolygonOutline(graphics, new[] { P(36, 47), P(41, 58), P(47, 58), P(41, 48) }, bark, dark);
        FillPolygon(graphics, new[] { P(18, 18), P(25, 11), P(34, 9), P(45, 13), P(50, 24), P(41, 28), P(24, 27) }, leaf);
        using var eyeBrush = new SolidBrush(Rgba(235, 208, 113));
        using var mouthPen = new Pen(dark, 1f);
        graphics.FillRectangle(eyeBrush, Box(27, 28, 28, 30));
        graphics.FillRectangle(eyeBrush, Box(35, 28, 36, 30));
        graphics.DrawLine(mouthPen, 29, 36, 35, 36);
        return bitmap;
    }

    private static Bitmap BuildAnomalyVoidHound()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 32, 55, 30, 7, 84);
        var flesh = Rgba(66, 49, 77);
        var dark = Rgba(24, 16, 31);
        var glow = Rgba(255, 104, 123);
        var bone = Rgba(201, 188, 171);

        FillPolygonOutline(graphics, new[] { P(18, 34), P(25, 29), P(39, 29), P(46, 34), P(41, 43), P(24, 43) }, flesh, dark);
        FillPolygonOutline(graphics, new[] { P(39, 29), P(47, 25), P(52, 29), P(48, 37), P(40, 36) }, flesh, dark);
        FillPolygon(graphics, new[] { P(46, 25), P(49, 20), P(52, 26) }, dark);
        FillPolygon(graphics, new[] { P(41, 26), P(44, 21), P(47, 27) }, dark);
        FillPolygon(graphics, new[] { P(18, 34), P(12, 30), P(8, 32), P(15, 38) }, flesh);
        FillPolygon(graphics, new[] { P(15, 32), P(10, 26), P(8, 28), P(12, 34) }, glow);

        foreach (var x in new[] { 22, 29, 38, 45 })
        {
            FillRectangleOutline(graphics, Box(x, 42, x + 2, 55), Rgba(78, 64, 73), dark);
        }

        FillPolygon(graphics, new[] { P(28, 29), P(30, 24), P(33, 30) }, bone);
        FillPolygon(graphics, new[] { P(33, 29), P(36, 23), P(39, 30) }, bone);

        using var eyeBrush = new SolidBrush(glow);
        using var mouthPen = new Pen(glow, 2f);
        graphics.FillRectangle(eyeBrush, Box(47, 29, 48, 30));
        graphics.DrawLine(mouthPen, 47, 33, 51, 34);
        return bitmap;
    }

    private static Bitmap BuildAnomalyEyeCluster()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 32, 55, 24, 6, 80);
        var mass = Rgba(119, 78, 94);
        var dark = Rgba(42, 24, 34);
        var sclera = Rgba(235, 226, 214);
        var iris = Rgba(201, 57, 79);
        var pupil = Rgba(24, 18, 22);

        using var veinPen = new Pen(Rgba(155, 93, 109), 2f);
        graphics.DrawLine(veinPen, 19, 40, 11, 49);
        graphics.DrawLine(veinPen, 28, 45, 24, 55);
        graphics.DrawLine(veinPen, 37, 45, 40, 55);
        graphics.DrawLine(veinPen, 45, 39, 53, 48);

        FillEllipseOutline(graphics, Box(18, 26, 46, 48), mass, dark);
        FillEllipseOutline(graphics, Box(16, 34, 29, 44), sclera, dark);
        FillEllipseOutline(graphics, Box(26, 29, 38, 40), sclera, dark);
        FillEllipseOutline(graphics, Box(36, 34, 49, 45), sclera, dark);
        FillEllipseOutline(graphics, Box(24, 37, 41, 50), sclera, dark);

        using var irisBrush = new SolidBrush(iris);
        using var pupilBrush = new SolidBrush(pupil);
        foreach (var eye in new[] { Box(20, 36, 25, 41), Box(29, 32, 34, 37), Box(40, 37, 45, 42), Box(30, 40, 35, 45) })
        {
            graphics.FillEllipse(irisBrush, eye);
        }

        foreach (var pupilRect in new[] { Box(22, 37, 23, 40), Box(31, 33, 32, 36), Box(42, 38, 43, 41), Box(32, 41, 33, 44) })
        {
            graphics.FillRectangle(pupilBrush, pupilRect);
        }

        return bitmap;
    }

    private static Bitmap BuildAnomalyFleshBloom()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 32, 55, 28, 7, 82);
        var petal = Rgba(144, 72, 86);
        var petalDark = Rgba(69, 28, 41);
        var inner = Rgba(226, 177, 138);
        var tooth = Rgba(238, 232, 220);

        FillPolygonOutline(graphics, new[] { P(32, 19), P(39, 29), P(32, 37), P(25, 29) }, petal, petalDark);
        FillPolygonOutline(graphics, new[] { P(20, 25), P(29, 30), P(24, 40), P(14, 35) }, petal, petalDark);
        FillPolygonOutline(graphics, new[] { P(44, 25), P(50, 35), P(40, 40), P(35, 30) }, petal, petalDark);
        FillPolygonOutline(graphics, new[] { P(22, 39), P(32, 34), P(40, 40), P(32, 49) }, petal, petalDark);
        FillEllipseOutline(graphics, Box(22, 27, 42, 45), inner, petalDark);
        FillEllipseOutline(graphics, Box(26, 31, 38, 41), Rgba(90, 24, 33), petalDark);

        using var toothBrush = new SolidBrush(tooth);
        foreach (var triangle in new[]
        {
            new[] { P(27, 31), P(29, 28), P(31, 31) },
            new[] { P(31, 31), P(33, 28), P(35, 31) },
            new[] { P(28, 41), P(30, 44), P(32, 41) },
            new[] { P(32, 41), P(34, 44), P(36, 41) },
        })
        {
            graphics.FillPolygon(toothBrush, triangle);
        }

        return bitmap;
    }

    private static Bitmap BuildAnomalyTentacleMaw()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 32, 55, 28, 6, 84);
        var flesh = Rgba(83, 64, 99);
        var dark = Rgba(28, 18, 36);
        var glow = Rgba(151, 220, 219);

        using var tentaclePen = new Pen(flesh, 3f);
        graphics.DrawLine(tentaclePen, 25, 40, 15, 49);
        graphics.DrawLine(tentaclePen, 29, 43, 24, 56);
        graphics.DrawLine(tentaclePen, 35, 43, 39, 56);
        graphics.DrawLine(tentaclePen, 39, 40, 49, 49);
        graphics.DrawLine(tentaclePen, 24, 33, 14, 28);
        graphics.DrawLine(tentaclePen, 40, 33, 50, 27);

        FillEllipseOutline(graphics, Box(20, 24, 44, 46), flesh, dark);
        FillEllipseOutline(graphics, Box(25, 29, 39, 41), Rgba(31, 12, 19), dark);

        using var toothBrush = new SolidBrush(Rgba(232, 228, 218));
        foreach (var triangle in new[]
        {
            new[] { P(26, 31), P(28, 28), P(30, 31) },
            new[] { P(31, 30), P(32, 27), P(34, 30) },
            new[] { P(35, 31), P(37, 28), P(38, 31) },
            new[] { P(27, 39), P(29, 42), P(31, 39) },
            new[] { P(32, 40), P(33, 43), P(35, 40) },
            new[] { P(36, 39), P(38, 42), P(39, 39) },
        })
        {
            graphics.FillPolygon(toothBrush, triangle);
        }

        using var glowBrush = new SolidBrush(glow);
        graphics.FillEllipse(glowBrush, Box(29, 21, 34, 26));
        return bitmap;
    }

    private static Bitmap LoadHumanoidFrame(string rootPath, string sheetName, int row, int col)
    {
        var path = Path.Combine(
            rootPath,
            "Assets",
            "Art",
            "Tilesets",
            "FantasyKingdom",
            "FantasyKingdomTileset_Godot",
            "Characters",
            sheetName,
            "Idle.png");

        using var sheet = new Bitmap(path);
        using var frame = LoadHumanoidFrame(sheet, row, col);
        return CropToAlpha(frame);
    }

    private static Bitmap LoadHumanoidFrame(Bitmap sheet, int row, int col)
    {
        var region = new Rectangle(col * FrameSize, row * FrameSize, FrameSize, FrameSize);
        return sheet.Clone(region, PixelFormat.Format32bppArgb);
    }

    private static Bitmap CropToAlpha(Bitmap bitmap)
    {
        var bounds = AlphaBounds(bitmap);
        return bitmap.Clone(bounds, PixelFormat.Format32bppArgb);
    }

    private static Rectangle AlphaBounds(Bitmap bitmap)
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
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < minX || maxY < minY)
        {
            throw new InvalidOperationException("Encountered an empty humanoid frame.");
        }

        return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    private static Bitmap Upscale(Bitmap source)
    {
        var bitmap = new Bitmap(ExportSize, ExportSize, PixelFormat.Format32bppArgb);
        using var graphics = CreateGraphics(bitmap);
        graphics.DrawImage(source, 0, 0, ExportSize, ExportSize);
        return bitmap;
    }

    private static Bitmap ResizeNearest(Bitmap source, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = CreateGraphics(bitmap);
        graphics.DrawImage(source, 0, 0, width, height);
        return bitmap;
    }

    private static Bitmap NormalizeAnchor(Bitmap source)
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

    private static Bitmap NewCanvas() => new Bitmap(CanvasSize, CanvasSize, PixelFormat.Format32bppArgb);

    private static Graphics CreateGraphics(Image image)
    {
        var graphics = Graphics.FromImage(image);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighSpeed;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.SmoothingMode = SmoothingMode.None;
        return graphics;
    }

    private static void DrawShadow(Graphics graphics, int centerX, int centerY, int width, int height, int alpha)
    {
        using var brush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
        graphics.FillEllipse(brush, Box(centerX - width / 2, centerY - height / 2, centerX + width / 2, centerY + height / 2));
    }

    private static void FillEllipseOutline(Graphics graphics, Rectangle rectangle, Color fill, Color outline)
    {
        using var outlineBrush = new SolidBrush(outline);
        using var fillBrush = new SolidBrush(fill);
        graphics.FillEllipse(outlineBrush, Expand(rectangle, 1));
        graphics.FillEllipse(fillBrush, rectangle);
    }

    private static void FillRectangleOutline(Graphics graphics, Rectangle rectangle, Color fill, Color outline)
    {
        using var outlineBrush = new SolidBrush(outline);
        using var fillBrush = new SolidBrush(fill);
        graphics.FillRectangle(outlineBrush, Expand(rectangle, 1));
        graphics.FillRectangle(fillBrush, rectangle);
    }

    private static void FillPolygonOutline(Graphics graphics, Point[] points, Color fill, Color outline)
    {
        using var outlineBrush = new SolidBrush(outline);
        using var fillBrush = new SolidBrush(fill);
        graphics.FillPolygon(outlineBrush, Offset(points, -1, -1));
        graphics.FillPolygon(fillBrush, points);
    }

    private static void FillPolygon(Graphics graphics, Point[] points, Color fill)
    {
        using var brush = new SolidBrush(fill);
        graphics.FillPolygon(brush, points);
    }

    private static Rectangle Expand(Rectangle rectangle, int amount) =>
        Rectangle.FromLTRB(rectangle.Left - amount, rectangle.Top - amount, rectangle.Right + amount, rectangle.Bottom + amount);

    private static Rectangle Box(int left, int top, int right, int bottom) =>
        Rectangle.FromLTRB(left, top, right, bottom);

    private static Point[] Offset(Point[] points, int dx, int dy)
    {
        var shifted = new Point[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            shifted[i] = new Point(points[i].X + dx, points[i].Y + dy);
        }

        return shifted;
    }

    private static Point P(int x, int y) => new Point(x, y);

    private static Color ApplyBrightness(Color color, float brightness)
    {
        var r = ClampToByte(color.R * brightness);
        var g = ClampToByte(color.G * brightness);
        var b = ClampToByte(color.B * brightness);
        return Color.FromArgb(color.A, r, g, b);
    }

    private static int ClampToByte(float value) => Math.Clamp((int)Math.Round(value), 0, 255);

    private static Color Rgba(int r, int g, int b, int a = 255) => Color.FromArgb(a, r, g, b);

    private readonly record struct HumanoidDirectionalSource(string Key, string SheetName, int MaxWidth, int MaxHeight);

    private readonly record struct DirectionalTransform(float ScaleX, float ScaleY, float ShearX, float Brightness, bool Mirror);
}
