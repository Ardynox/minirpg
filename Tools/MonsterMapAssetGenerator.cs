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
    private static readonly DirectionalTransform[] DirectionalTransforms =
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

    public static void Generate(string rootPath)
    {
        var outputDirectory = Path.Combine(rootPath, "Assets", "Art", "Generated", "monster_map");
        var directionalOutputDirectory = Path.Combine(rootPath, "Assets", "Art", "Generated", "monster_map_iso8");
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(directionalOutputDirectory);

        var assets = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase)
        {
            ["monster_goblin"] = BuildGoblin(),
            ["monster_goblin_miner"] = BuildGoblinMiner(),
            ["monster_skeleton"] = BuildSkeleton(),
            ["monster_elf_ranger"] = BuildElfRanger(),
            ["monster_orc_warrior"] = BuildOrcWarrior(),
            ["monster_orc_shaman"] = BuildOrcShaman(),
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
            using (var directionalSheet = BuildProjectedDirectionalSheet(normalized))
            {
                export.Save(Path.Combine(outputDirectory, entry.Key + ".png"), ImageFormat.Png);
                directionalSheet.Save(
                    Path.Combine(directionalOutputDirectory, entry.Key + "_8dir.png"),
                    ImageFormat.Png);
            }
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

    private static Bitmap BuildGoblin()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 30, 56, 18, 5, 68);
        var skin = Rgba(95, 162, 58);
        var skinDark = Rgba(38, 92, 28);
        var skinLight = Rgba(160, 212, 92);
        var cloth = Rgba(112, 78, 46);
        var clothDark = Rgba(54, 33, 19);
        var wood = Rgba(124, 86, 47);
        var woodDark = Rgba(64, 41, 21);

        FillEllipseOutline(graphics, Box(24, 21, 36, 31), skin, skinDark);
        FillPolygonOutline(graphics, new[] { P(34, 22), P(42, 18), P(36, 27) }, skin, skinDark);
        FillPolygonOutline(graphics, new[] { P(23, 24), P(18, 22), P(23, 28) }, skin, skinDark);
        FillRectangleOutline(graphics, Box(21, 30, 35, 42), skin, skinDark);
        FillPolygonOutline(graphics, new[] { P(20, 41), P(27, 44), P(24, 52), P(17, 48) }, cloth, clothDark);
        FillPolygonOutline(graphics, new[] { P(27, 41), P(35, 44), P(34, 52), P(26, 50) }, cloth, clothDark);
        FillRectangleOutline(graphics, Box(22, 42, 25, 56), skin, skinDark);
        FillRectangleOutline(graphics, Box(29, 43, 32, 56), skin, skinDark);
        FillRectangleOutline(graphics, Box(18, 33, 22, 40), skin, skinDark);
        FillRectangleOutline(graphics, Box(31, 34, 35, 39), skin, skinDark);
        using var woodPen = new Pen(woodDark, 3f);
        using var woodBrush = new SolidBrush(wood);
        using var eyeBrush = new SolidBrush(Rgba(246, 219, 80));
        using var highlightBrush = new SolidBrush(Color.FromArgb(170, skinLight));
        graphics.DrawLine(woodPen, 34, 37, 49, 42);
        graphics.FillEllipse(woodBrush, Box(48, 39, 56, 46));
        graphics.FillRectangle(eyeBrush, Box(31, 24, 33, 25));
        graphics.FillRectangle(highlightBrush, Box(27, 24, 31, 27));
        return bitmap;
    }

    private static Bitmap BuildGoblinMiner()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 30, 56, 18, 5, 70);
        var skin = Rgba(86, 148, 56);
        var skinDark = Rgba(33, 86, 28);
        var skinLight = Rgba(148, 203, 98);
        var helmet = Rgba(134, 114, 88);
        var helmetDark = Rgba(62, 46, 32);
        var cloth = Rgba(94, 80, 56);
        var clothDark = Rgba(46, 35, 24);
        var wood = Rgba(118, 82, 47);
        var metal = Rgba(152, 156, 160);
        var soot = Rgba(48, 42, 39);

        FillEllipseOutline(graphics, Box(24, 22, 35, 31), skin, skinDark);
        FillPolygonOutline(graphics, new[] { P(23, 24), P(18, 21), P(22, 28) }, skin, skinDark);
        FillPolygonOutline(graphics, new[] { P(35, 23), P(40, 20), P(35, 28) }, skin, skinDark);
        FillEllipseOutline(graphics, Box(21, 17, 37, 25), helmet, helmetDark);
        FillRectangleOutline(graphics, Box(23, 15, 35, 18), helmet, helmetDark);
        FillRectangleOutline(graphics, Box(28, 11, 30, 16), Rgba(232, 216, 180), helmetDark);
        FillEllipseOutline(graphics, Box(27, 8, 31, 12), Rgba(255, 176, 64, 220), Rgba(132, 72, 24));
        FillRectangleOutline(graphics, Box(21, 30, 35, 42), cloth, clothDark);
        FillPolygonOutline(graphics, new[] { P(20, 41), P(28, 44), P(25, 52), P(17, 48) }, cloth, clothDark);
        FillPolygonOutline(graphics, new[] { P(27, 41), P(35, 44), P(34, 52), P(26, 50) }, cloth, clothDark);
        FillRectangleOutline(graphics, Box(22, 42, 25, 56), skin, skinDark);
        FillRectangleOutline(graphics, Box(29, 43, 32, 56), skin, skinDark);
        FillRectangleOutline(graphics, Box(18, 33, 22, 40), skin, skinDark);
        FillRectangleOutline(graphics, Box(31, 34, 35, 39), skin, skinDark);
        using var handlePen = new Pen(wood, 3f);
        graphics.DrawLine(handlePen, 33, 35, 48, 22);
        FillPolygonOutline(graphics, new[] { P(45, 18), P(54, 18), P(51, 24), P(44, 23) }, metal, helmetDark);
        FillRectangleOutline(graphics, Box(23, 34, 26, 37), soot, clothDark);
        FillRectangleOutline(graphics, Box(29, 38, 32, 41), soot, clothDark);
        using var eyeBrush = new SolidBrush(Rgba(246, 219, 80));
        using var highlightBrush = new SolidBrush(Color.FromArgb(170, skinLight));
        graphics.FillRectangle(eyeBrush, Box(28, 24, 30, 25));
        graphics.FillRectangle(highlightBrush, Box(25, 24, 28, 27));
        return bitmap;
    }

    private static Bitmap BuildSkeleton()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 31, 56, 20, 6, 72);
        var bone = Rgba(216, 203, 164);
        var boneDark = Rgba(111, 92, 64);
        var armor = Rgba(100, 94, 98);
        var rust = Rgba(144, 84, 58);
        var cloth = Rgba(76, 46, 38);

        FillEllipseOutline(graphics, Box(27, 15, 39, 27), bone, boneDark);
        FillRectangleOutline(graphics, Box(27, 28, 37, 39), bone, boneDark);
        FillRectangleOutline(graphics, Box(24, 39, 38, 43), bone, boneDark);
        FillPolygonOutline(graphics, new[] { P(24, 27), P(18, 33), P(22, 39), P(27, 36) }, armor, boneDark);
        FillPolygonOutline(graphics, new[] { P(31, 39), P(40, 41), P(37, 49), P(28, 46) }, cloth, boneDark);
        FillRectangleOutline(graphics, Box(22, 42, 25, 58), bone, boneDark);
        FillRectangleOutline(graphics, Box(30, 42, 33, 58), bone, boneDark);
        FillRectangleOutline(graphics, Box(18, 31, 21, 42), bone, boneDark);
        FillRectangleOutline(graphics, Box(34, 30, 37, 40), bone, boneDark);
        using var bladePen = new Pen(boneDark, 3f);
        using var bladeBrightPen = new Pen(Rgba(168, 174, 176), 1f);
        using var hiltBrush = new SolidBrush(rust);
        using var eyeBrush = new SolidBrush(Rgba(212, 60, 60));
        using var eyeGlowBrush = new SolidBrush(Color.FromArgb(110, 224, 76, 76));
        graphics.DrawLine(bladePen, 36, 35, 52, 26);
        graphics.DrawLine(bladeBrightPen, 37, 34, 51, 27);
        graphics.FillRectangle(hiltBrush, Box(33, 33, 37, 36));
        graphics.FillEllipse(eyeGlowBrush, Box(29, 20, 34, 25));
        graphics.FillEllipse(eyeGlowBrush, Box(33, 20, 38, 25));
        graphics.FillRectangle(eyeBrush, Box(30, 21, 31, 23));
        graphics.FillRectangle(eyeBrush, Box(35, 21, 36, 23));
        return bitmap;
    }

    private static Bitmap BuildOrcWarrior()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 31, 56, 24, 6, 74);
        var skin = Rgba(104, 124, 96);
        var skinDark = Rgba(50, 67, 46);
        var skinLight = Rgba(159, 176, 145);
        var metal = Rgba(122, 120, 124);
        var metalDark = Rgba(56, 57, 63);
        var leather = Rgba(108, 73, 43);
        var tusk = Rgba(232, 219, 192);

        FillEllipseOutline(graphics, Box(29, 15, 43, 28), skin, skinDark);
        FillRectangleOutline(graphics, Box(19, 27, 40, 45), skin, skinDark);
        FillPolygonOutline(graphics, new[] { P(18, 28), P(11, 35), P(17, 42), P(25, 38) }, metal, metalDark);
        FillPolygonOutline(graphics, new[] { P(20, 27), P(31, 24), P(40, 28), P(38, 42), P(21, 43) }, metal, metalDark);
        FillPolygonOutline(graphics, new[] { P(24, 44), P(37, 45), P(34, 52), P(23, 51) }, leather, metalDark);
        FillRectangleOutline(graphics, Box(22, 44, 27, 59), skin, skinDark);
        FillRectangleOutline(graphics, Box(31, 45, 36, 59), skin, skinDark);
        FillRectangleOutline(graphics, Box(15, 31, 20, 44), skin, skinDark);
        FillRectangleOutline(graphics, Box(37, 30, 41, 41), skin, skinDark);
        using var axeHandlePen = new Pen(Rgba(110, 81, 49), 3f);
        graphics.DrawLine(axeHandlePen, 39, 34, 54, 20);
        FillPolygonOutline(graphics, new[] { P(51, 15), P(58, 17), P(57, 28), P(49, 26), P(53, 21) }, metal, metalDark);
        using var eyeBrush = new SolidBrush(Rgba(236, 228, 94));
        using var highlightBrush = new SolidBrush(Color.FromArgb(150, skinLight));
        using var tuskBrush = new SolidBrush(tusk);
        graphics.FillRectangle(eyeBrush, Box(36, 20, 38, 21));
        graphics.FillRectangle(highlightBrush, Box(32, 19, 36, 22));
        graphics.FillPolygon(tuskBrush, new[] { P(39, 25), P(43, 26), P(39, 30) });
        graphics.FillPolygon(tuskBrush, new[] { P(34, 25), P(37, 27), P(35, 30) });
        return bitmap;
    }

    private static Bitmap BuildOrcShaman()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 31, 56, 24, 6, 74);
        var skin = Rgba(96, 124, 94);
        var skinDark = Rgba(43, 61, 39);
        var skinLight = Rgba(152, 176, 138);
        var cloth = Rgba(104, 78, 58);
        var clothDark = Rgba(50, 37, 28);
        var robe = Rgba(88, 100, 72);
        var robeDark = Rgba(44, 52, 33);
        var leather = Rgba(122, 86, 51);
        var bone = Rgba(218, 210, 188);
        var feather = Rgba(126, 74, 46);
        var featherDark = Rgba(70, 39, 24);
        var crystal = Rgba(112, 222, 136);
        var crystalGlow = Color.FromArgb(120, 92, 201, 114);

        FillPolygonOutline(graphics, new[] { P(28, 18), P(24, 9), P(28, 8), P(32, 16) }, feather, featherDark);
        FillPolygonOutline(graphics, new[] { P(33, 16), P(31, 6), P(36, 6), P(37, 15) }, Rgba(152, 128, 74), featherDark);
        FillPolygonOutline(graphics, new[] { P(38, 18), P(39, 8), P(44, 10), P(41, 17) }, feather, featherDark);
        FillRectangleOutline(graphics, Box(27, 18, 40, 21), leather, clothDark);
        FillEllipseOutline(graphics, Box(28, 16, 42, 29), skin, skinDark);
        FillPolygonOutline(graphics, new[] { P(22, 28), P(31, 24), P(41, 28), P(40, 44), P(23, 45) }, robe, robeDark);
        FillPolygonOutline(graphics, new[] { P(24, 43), P(40, 45), P(36, 56), P(22, 55) }, cloth, clothDark);
        FillRectangleOutline(graphics, Box(22, 44, 27, 59), skin, skinDark);
        FillRectangleOutline(graphics, Box(31, 45, 35, 59), skin, skinDark);
        FillRectangleOutline(graphics, Box(18, 30, 22, 42), skin, skinDark);
        FillRectangleOutline(graphics, Box(38, 31, 42, 40), skin, skinDark);
        FillEllipseOutline(graphics, Box(27, 29, 30, 32), bone, clothDark);
        FillEllipseOutline(graphics, Box(31, 30, 34, 33), bone, clothDark);
        FillEllipseOutline(graphics, Box(35, 29, 38, 32), bone, clothDark);
        using var staffPen = new Pen(leather, 3f);
        graphics.DrawLine(staffPen, 39, 34, 54, 16);
        graphics.DrawLine(staffPen, 46, 24, 52, 10);
        using var glowBrush = new SolidBrush(crystalGlow);
        graphics.FillEllipse(glowBrush, Box(50, 8, 60, 18));
        FillPolygonOutline(graphics, new[] { P(54, 7), P(58, 12), P(54, 17), P(50, 12) }, crystal, Rgba(44, 94, 55));
        using var eyeBrush = new SolidBrush(Rgba(236, 228, 94));
        using var highlightBrush = new SolidBrush(Color.FromArgb(150, skinLight));
        using var tuskBrush = new SolidBrush(bone);
        graphics.FillRectangle(eyeBrush, Box(35, 20, 37, 21));
        graphics.FillRectangle(highlightBrush, Box(31, 19, 35, 23));
        graphics.FillPolygon(tuskBrush, new[] { P(38, 25), P(42, 26), P(38, 30) });
        graphics.FillPolygon(tuskBrush, new[] { P(33, 25), P(36, 27), P(34, 30) });
        return bitmap;
    }

    private static Bitmap BuildElfRanger()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 31, 56, 18, 5, 68);
        var skin = Rgba(214, 190, 158);
        var skinDark = Rgba(108, 82, 64);
        var hood = Rgba(84, 132, 72);
        var hoodDark = Rgba(38, 74, 40);
        var leather = Rgba(114, 83, 54);
        var leatherDark = Rgba(58, 39, 26);
        var cloth = Rgba(74, 116, 70);
        var clothDark = Rgba(34, 63, 35);
        var bow = Rgba(150, 104, 56);
        var bowDark = Rgba(72, 48, 24);
        var feather = Rgba(214, 208, 184);

        FillPolygonOutline(graphics, new[] { P(26, 22), P(31, 14), P(39, 16), P(43, 24), P(40, 30), P(28, 30) }, hood, hoodDark);
        FillEllipseOutline(graphics, Box(30, 21, 37, 27), skin, skinDark);
        FillPolygonOutline(graphics, new[] { P(28, 22), P(24, 20), P(27, 25) }, skin, skinDark);
        FillPolygonOutline(graphics, new[] { P(38, 22), P(42, 19), P(40, 25) }, skin, skinDark);
        FillPolygonOutline(graphics, new[] { P(21, 29), P(30, 25), P(40, 28), P(42, 40), P(33, 46), P(22, 43) }, cloth, clothDark);
        FillRectangleOutline(graphics, Box(28, 29, 36, 39), leather, leatherDark);
        FillPolygonOutline(graphics, new[] { P(24, 43), P(31, 45), P(29, 56), P(22, 55) }, leather, leatherDark);
        FillPolygonOutline(graphics, new[] { P(32, 44), P(38, 45), P(37, 56), P(30, 56) }, leather, leatherDark);
        FillRectangleOutline(graphics, Box(25, 31, 28, 41), skin, skinDark);
        FillRectangleOutline(graphics, Box(37, 30, 40, 39), skin, skinDark);
        FillRectangleOutline(graphics, Box(25, 45, 28, 59), skinDark, skinDark);
        FillRectangleOutline(graphics, Box(32, 45, 35, 59), skinDark, skinDark);
        FillRectangleOutline(graphics, Box(20, 24, 24, 37), leather, leatherDark);
        FillPolygonOutline(graphics, new[] { P(20, 22), P(24, 23), P(24, 26), P(20, 25) }, feather, leatherDark);
        FillPolygonOutline(graphics, new[] { P(19, 18), P(23, 19), P(23, 22), P(19, 21) }, feather, leatherDark);
        using var strapPen = new Pen(leatherDark, 2f);
        using var bowPen = new Pen(bowDark, 2f);
        graphics.DrawLine(strapPen, 22, 28, 35, 38);
        graphics.DrawLine(bowPen, 42, 22, 50, 38);
        graphics.DrawLine(bowPen, 45, 21, 53, 39);
        graphics.DrawLine(new Pen(feather, 1f), 46, 22, 52, 38);
        using var eyeBrush = new SolidBrush(Rgba(246, 222, 108));
        graphics.FillRectangle(eyeBrush, Box(34, 23, 35, 24));
        return bitmap;
    }

    private static Bitmap BuildSlime()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 31, 56, 24, 7, 68);
        var outline = Rgba(24, 93, 44);
        var shell = Rgba(92, 218, 118, 214);
        var core = Rgba(34, 126, 52, 180);
        var highlight = Color.FromArgb(170, 204, 255, 214);

        FillPolygonOutline(graphics, new[] { P(18, 47), P(19, 31), P(26, 25), P(38, 24), P(47, 28), P(49, 43), P(44, 51), P(24, 52) }, shell, outline);
        FillEllipseOutline(graphics, Box(25, 31, 40, 44), core, outline);
        using var eyeBrush = new SolidBrush(Rgba(28, 42, 34));
        using var highlightBrush = new SolidBrush(highlight);
        graphics.FillRectangle(eyeBrush, Box(27, 36, 29, 38));
        graphics.FillRectangle(eyeBrush, Box(37, 36, 39, 38));
        graphics.FillEllipse(highlightBrush, Box(24, 30, 32, 35));
        graphics.FillEllipse(highlightBrush, Box(33, 28, 39, 32));
        return bitmap;
    }

    private static Bitmap BuildSpider()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 31, 56, 30, 7, 72);
        var leg = Rgba(38, 26, 22);
        var body = Rgba(26, 21, 20);
        var abdomen = Rgba(52, 36, 31);
        var mark = Rgba(173, 47, 47);

        using var legPen = new Pen(leg, 3f);
        graphics.DrawLine(legPen, 21, 34, 8, 25);
        graphics.DrawLine(legPen, 21, 37, 6, 35);
        graphics.DrawLine(legPen, 22, 40, 8, 47);
        graphics.DrawLine(legPen, 23, 43, 10, 56);
        graphics.DrawLine(legPen, 41, 34, 56, 25);
        graphics.DrawLine(legPen, 42, 37, 58, 35);
        graphics.DrawLine(legPen, 41, 40, 57, 48);
        graphics.DrawLine(legPen, 39, 43, 54, 56);

        FillEllipseOutline(graphics, Box(27, 26, 40, 37), body, leg);
        FillEllipseOutline(graphics, Box(18, 32, 45, 50), abdomen, leg);
        FillPolygonOutline(graphics, new[] { P(27, 36), P(31, 32), P(35, 36), P(31, 40) }, mark, leg);
        FillPolygonOutline(graphics, new[] { P(35, 38), P(39, 34), P(43, 38), P(39, 42) }, mark, leg);
        using var fangBrush = new SolidBrush(Rgba(232, 224, 210));
        using var eyeBrush = new SolidBrush(Rgba(220, 72, 72));
        graphics.FillPolygon(fangBrush, new[] { P(31, 36), P(33, 41), P(35, 36) });
        foreach (var eye in new[] { Box(28, 30, 30, 32), Box(31, 29, 33, 31), Box(34, 29, 36, 31), Box(37, 30, 39, 32) })
        {
            graphics.FillEllipse(eyeBrush, eye);
        }

        return bitmap;
    }

    private static Bitmap BuildScorpion()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 31, 56, 28, 6, 72);
        var shell = Rgba(120, 58, 37);
        var shellLight = Rgba(158, 81, 51);
        var shellDark = Rgba(56, 25, 18);

        FillEllipseOutline(graphics, Box(25, 34, 35, 41), shellLight, shellDark);
        FillEllipseOutline(graphics, Box(31, 34, 40, 42), shell, shellDark);
        FillEllipseOutline(graphics, Box(37, 35, 45, 43), shell, shellDark);
        FillEllipseOutline(graphics, Box(18, 31, 29, 39), shellLight, shellDark);
        FillPolygonOutline(graphics, new[] { P(17, 33), P(8, 29), P(6, 33), P(11, 39), P(18, 37) }, shellLight, shellDark);
        FillPolygonOutline(graphics, new[] { P(19, 37), P(9, 39), P(6, 45), P(12, 48), P(19, 43) }, shell, shellDark);
        using var legPen = new Pen(shellDark, 2f);
        graphics.DrawLine(legPen, 24, 39, 15, 34);
        graphics.DrawLine(legPen, 26, 42, 14, 44);
        graphics.DrawLine(legPen, 37, 40, 48, 35);
        graphics.DrawLine(legPen, 39, 43, 49, 47);
        using var tailPen = new Pen(shellDark, 3f);
        graphics.DrawLine(tailPen, 42, 35, 48, 27);
        graphics.DrawLine(tailPen, 48, 27, 50, 19);
        graphics.DrawLine(tailPen, 50, 19, 46, 12);
        FillPolygonOutline(graphics, new[] { P(45, 12), P(49, 7), P(52, 14), P(48, 18) }, Rgba(230, 196, 92), shellDark);
        return bitmap;
    }

    private static Bitmap BuildWolf()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 31, 56, 28, 6, 70);
        var fur = Rgba(102, 108, 118);
        var furDark = Rgba(43, 48, 56);
        var furLight = Rgba(182, 186, 190);

        FillPolygonOutline(graphics, new[] { P(17, 34), P(25, 29), P(40, 29), P(48, 33), P(42, 43), P(22, 43) }, fur, furDark);
        FillPolygonOutline(graphics, new[] { P(40, 29), P(49, 25), P(54, 29), P(51, 36), P(41, 36) }, fur, furDark);
        FillPolygonOutline(graphics, new[] { P(18, 34), P(11, 29), P(8, 32), P(14, 38) }, fur, furDark);
        FillPolygon(graphics, new[] { P(45, 24), P(48, 19), P(51, 25) }, furDark);
        FillPolygon(graphics, new[] { P(39, 25), P(42, 20), P(45, 27) }, furDark);
        FillPolygon(graphics, new[] { P(26, 37), P(39, 37), P(42, 41), P(29, 42) }, furLight);
        foreach (var x in new[] { 21, 29, 37, 45 })
        {
            FillRectangleOutline(graphics, Box(x, 42, x + 3, 56), Rgba(86, 77, 66), furDark);
        }

        using var eyeBrush = new SolidBrush(Rgba(247, 226, 104));
        using var toothBrush = new SolidBrush(Rgba(232, 226, 214));
        graphics.FillRectangle(eyeBrush, Box(46, 29, 48, 30));
        graphics.FillPolygon(toothBrush, new[] { P(50, 35), P(53, 34), P(51, 38) });
        graphics.FillPolygon(toothBrush, new[] { P(47, 35), P(49, 34), P(48, 38) });
        return bitmap;
    }

    private static Bitmap BuildBear()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 31, 57, 34, 8, 72);
        var fur = Rgba(124, 87, 52);
        var furDark = Rgba(63, 39, 24);
        var furLight = Rgba(180, 137, 94);

        FillEllipseOutline(graphics, Box(12, 22, 41, 46), fur, furDark);
        FillEllipseOutline(graphics, Box(25, 20, 47, 42), fur, furDark);
        FillEllipseOutline(graphics, Box(38, 22, 54, 37), fur, furDark);
        FillEllipseOutline(graphics, Box(42, 17, 47, 22), furDark, furDark);
        FillEllipseOutline(graphics, Box(48, 18, 53, 23), furDark, furDark);
        FillRectangleOutline(graphics, Box(16, 44, 22, 59), Rgba(96, 63, 39), furDark);
        FillRectangleOutline(graphics, Box(25, 44, 31, 59), Rgba(96, 63, 39), furDark);
        FillRectangleOutline(graphics, Box(35, 44, 41, 59), Rgba(96, 63, 39), furDark);
        FillRectangleOutline(graphics, Box(44, 44, 50, 59), Rgba(96, 63, 39), furDark);
        using var muzzleBrush = new SolidBrush(furLight);
        using var eyeBrush = new SolidBrush(Rgba(26, 22, 20));
        graphics.FillEllipse(muzzleBrush, Box(44, 28, 51, 34));
        graphics.FillRectangle(eyeBrush, Box(49, 28, 50, 30));
        graphics.FillRectangle(eyeBrush, Box(46, 25, 47, 26));
        return bitmap;
    }

    private static Bitmap BuildRat()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 31, 56, 18, 5, 68);
        var fur = Rgba(108, 101, 98);
        var furDark = Rgba(49, 44, 44);
        var pink = Rgba(214, 156, 164);

        FillEllipseOutline(graphics, Box(22, 36, 38, 47), fur, furDark);
        FillEllipseOutline(graphics, Box(35, 33, 46, 42), fur, furDark);
        FillEllipseOutline(graphics, Box(38, 30, 42, 34), pink, furDark);
        FillEllipseOutline(graphics, Box(42, 31, 46, 35), pink, furDark);
        using var tailPen = new Pen(pink, 2f);
        using var whiskerPen = new Pen(pink, 1f);
        using var eyeBrush = new SolidBrush(Rgba(210, 62, 60));
        using var toothBrush = new SolidBrush(Rgba(234, 228, 220));
        using var legPen = new Pen(furDark, 2f);
        graphics.DrawLine(tailPen, 22, 43, 11, 38);
        graphics.DrawLine(tailPen, 11, 38, 6, 40);
        graphics.DrawLine(whiskerPen, 45, 37, 51, 34);
        graphics.DrawLine(whiskerPen, 45, 39, 52, 40);
        graphics.FillRectangle(eyeBrush, Box(43, 36, 45, 37));
        graphics.FillPolygon(toothBrush, new[] { P(46, 40), P(48, 39), P(47, 42) });
        graphics.DrawLine(legPen, 26, 46, 24, 54);
        graphics.DrawLine(legPen, 31, 46, 30, 54);
        graphics.DrawLine(legPen, 37, 45, 40, 53);
        return bitmap;
    }

    private static Bitmap BuildTreant()
    {
        var bitmap = NewCanvas();
        using var graphics = CreateGraphics(bitmap);
        DrawShadow(graphics, 31, 57, 30, 8, 70);
        var bark = Rgba(103, 74, 49);
        var barkDark = Rgba(49, 34, 20);
        var barkLight = Rgba(143, 106, 70);
        var leaf = Rgba(80, 138, 70);
        var moss = Rgba(62, 112, 58);

        FillPolygonOutline(graphics, new[] { P(23, 18), P(32, 12), P(44, 14), P(50, 25), P(44, 31), P(24, 30), P(18, 24) }, leaf, moss);
        FillPolygonOutline(graphics, new[] { P(24, 27), P(41, 25), P(43, 49), P(24, 51) }, bark, barkDark);
        FillPolygonOutline(graphics, new[] { P(16, 30), P(24, 31), P(23, 36), P(10, 41) }, bark, barkDark);
        FillPolygonOutline(graphics, new[] { P(41, 31), P(50, 25), P(56, 31), P(44, 39) }, bark, barkDark);
        FillPolygonOutline(graphics, new[] { P(24, 49), P(18, 60), P(24, 60), P(30, 50) }, bark, barkDark);
        FillPolygonOutline(graphics, new[] { P(36, 49), P(41, 60), P(48, 60), P(41, 50) }, bark, barkDark);
        FillPolygon(graphics, new[] { P(22, 21), P(28, 18), P(33, 20), P(27, 26) }, moss);
        FillPolygon(graphics, new[] { P(37, 17), P(44, 18), P(46, 24), P(39, 25) }, moss);
        using var eyeBrush = new SolidBrush(Rgba(232, 210, 112));
        using var mouthPen = new Pen(barkDark, 2f);
        using var groovePen = new Pen(barkLight, 1f);
        graphics.FillRectangle(eyeBrush, Box(28, 34, 30, 36));
        graphics.FillRectangle(eyeBrush, Box(35, 33, 37, 35));
        graphics.DrawLine(mouthPen, 29, 41, 36, 41);
        graphics.DrawLine(groovePen, 27, 28, 27, 47);
        graphics.DrawLine(groovePen, 32, 27, 32, 45);
        graphics.DrawLine(groovePen, 38, 28, 39, 46);
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

    public static string ValidateIso8Assets(string rootPath)
    {
        var reportDirectory = Path.Combine(rootPath, "Artifacts");
        Directory.CreateDirectory(reportDirectory);

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var reportPath = Path.Combine(reportDirectory, $"iso8_asset_validation_{timestamp}.md");

        var dataPath = Path.Combine(rootPath, "Data", "entity_render.json");
        var importParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["compress/mode"] = "0",
            ["mipmaps/generate"] = "false",
            ["process/fix_alpha_border"] = "true",
        };

        var errors = new List<string>();
        var warnings = new List<string>();
        var checkedCount = 0;

        if (!File.Exists(dataPath))
        {
            errors.Add($"Missing data file: {dataPath}");
        }
        else
        {
            var json = File.ReadAllText(dataPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            foreach (var entry in root.EnumerateObject())
            {
                var entityId = entry.Name;
                var config = entry.Value;

                if (!config.TryGetProperty("texturePath", out var texturePathElement))
                {
                    continue;
                }

                var texturePath = texturePathElement.GetString() ?? string.Empty;
                if (!texturePath.Contains("monster_map_iso8", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                checkedCount++;
                var expectedFileName = $"monster_{entityId}_8dir.png";
                var actualFileName = Path.GetFileName(texturePath);

                if (!actualFileName.EndsWith("_8dir.png", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"[{entityId}] invalid iso8 naming: {actualFileName}");
                }

                if (!actualFileName.Equals(expectedFileName, StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"[{entityId}] naming differs from convention expected '{expectedFileName}', actual '{actualFileName}'");
                }

                var relativePath = texturePath.Replace("res://", string.Empty).Replace('/', Path.DirectorySeparatorChar);
                var absolutePath = Path.Combine(rootPath, relativePath);
                if (!File.Exists(absolutePath))
                {
                    errors.Add($"[{entityId}] missing texture file: {texturePath}");
                    continue;
                }

                var importPath = absolutePath + ".import";
                if (!File.Exists(importPath))
                {
                    errors.Add($"[{entityId}] missing import settings: {Path.GetFileName(importPath)}");
                    continue;
                }

                ValidateImportSettings(importPath, importParameters, entityId, errors, warnings);
            }
        }

        using (var writer = new StreamWriter(reportPath, false))
        {
            writer.WriteLine("# ISO8 Asset Validation Report");
            writer.WriteLine();
            writer.WriteLine($"- GeneratedAt: {DateTime.Now:O}");
            writer.WriteLine($"- CheckedEntries: {checkedCount}");
            writer.WriteLine($"- Errors: {errors.Count}");
            writer.WriteLine($"- Warnings: {warnings.Count}");
            writer.WriteLine();

            writer.WriteLine("## Errors");
            writer.WriteLine();
            if (errors.Count == 0)
            {
                writer.WriteLine("- None");
            }
            else
            {
                foreach (var error in errors)
                {
                    writer.WriteLine($"- {error}");
                }
            }

            writer.WriteLine();
            writer.WriteLine("## Warnings");
            writer.WriteLine();
            if (warnings.Count == 0)
            {
                writer.WriteLine("- None");
            }
            else
            {
                foreach (var warning in warnings)
                {
                    writer.WriteLine($"- {warning}");
                }
            }
        }

        return reportPath;
    }

    private static void ValidateImportSettings(
        string importPath,
        Dictionary<string, string> expectedParameters,
        string entityId,
        List<string> errors,
        List<string> warnings)
    {
        var lines = File.ReadAllLines(importPath);
        var importSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("[") || line.StartsWith("#") || !line.Contains('='))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            importSettings[key] = value;
        }

        foreach (var expected in expectedParameters)
        {
            if (!importSettings.TryGetValue(expected.Key, out var actualValue))
            {
                errors.Add($"[{entityId}] import missing key '{expected.Key}' ({Path.GetFileName(importPath)})");
                continue;
            }

            if (!actualValue.Equals(expected.Value, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"[{entityId}] import key '{expected.Key}' expected '{expected.Value}', actual '{actualValue}'");
            }
        }
    }

    private readonly record struct HumanoidDirectionalSource(string Key, string SheetName, int MaxWidth, int MaxHeight);

    private readonly record struct DirectionalTransform(float ScaleX, float ScaleY, float ShearX, float Brightness, bool Mirror);
}
