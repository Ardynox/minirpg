using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using static CharacterSpriteUtilities;

public static class MonsterMapAssetGenerator
{
    // 共享的 8 方向投影 / 几何绘制管线见 CharacterSpriteUtilities.cs。
    // 本文件只保留怪物专属几何（BuildGoblin / BuildOrc / ...）和 voxel tile / ore overlay 等不属于角色路径的代码。
    private const int FrameSize = 128;

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

    public static string GenerateProceduralVoxelTiles(string rootPath)
    {
        var outputDirectory = Path.Combine(rootPath, "Assets", "Art", "Generated", "voxel_tiles");
        var overlaysDirectory = Path.Combine(outputDirectory, "overlays");
        var oresDirectory = Path.Combine(outputDirectory, "ores");
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(overlaysDirectory);
        Directory.CreateDirectory(oresDirectory);

        const int tileSize = 32;
        var baseConfigs = new[]
        {
            new TileConfig("stone", Rgba(124, 127, 132), Rgba(82, 86, 92), Rgba(171, 176, 184), 86, 5, 18, 101),
            new TileConfig("dirt", Rgba(123, 87, 58), Rgba(78, 51, 31), Rgba(159, 117, 82), 74, 3, 16, 211),
            new TileConfig("grass_top", Rgba(83, 149, 71), Rgba(45, 96, 36), Rgba(126, 193, 96), 68, 2, 14, 307),
            new TileConfig("sand", Rgba(214, 193, 134), Rgba(168, 146, 94), Rgba(236, 217, 166), 52, 1, 12, 401),
            new TileConfig("gravel", Rgba(140, 138, 132), Rgba(93, 91, 86), Rgba(186, 184, 176), 82, 3, 15, 499),
            new TileConfig("mud", Rgba(104, 79, 58), Rgba(67, 47, 31), Rgba(138, 108, 81), 62, 2, 14, 557),
            new TileConfig("clay", Rgba(142, 122, 114), Rgba(99, 82, 76), Rgba(182, 160, 150), 55, 2, 11, 631),
            new TileConfig("snow", Rgba(232, 236, 242), Rgba(186, 194, 207), Rgba(250, 252, 255), 48, 1, 10, 709),
            new TileConfig("ash", Rgba(109, 106, 112), Rgba(74, 71, 76), Rgba(145, 141, 149), 64, 2, 13, 773),
            new TileConfig("plank", Rgba(163, 118, 74), Rgba(112, 74, 41), Rgba(199, 153, 103), 36, 1, 9, 853),
            new TileConfig("brick", Rgba(132, 109, 96), Rgba(86, 67, 57), Rgba(168, 145, 130), 34, 2, 9, 919),
            new TileConfig("brick_mossy", Rgba(118, 122, 95), Rgba(71, 84, 58), Rgba(153, 161, 124), 40, 2, 10, 977),
            new TileConfig("brick_cracked", Rgba(124, 103, 94), Rgba(74, 58, 53), Rgba(162, 139, 128), 38, 4, 11, 1031),
        };

        var generated = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        foreach (var config in baseConfigs)
        {
            var tile = BuildProceduralTile(tileSize, config);
            if (config.Name.StartsWith("plank", StringComparison.OrdinalIgnoreCase))
                ApplyWoodPlankLines(tile, config.SeedOffset + 17);
            if (config.Name.StartsWith("brick", StringComparison.OrdinalIgnoreCase))
                ApplyBrickPattern(tile, config.SeedOffset + 29);
            generated[config.Name] = tile;
        }

        var generatedPaths = new List<string>();
        try
        {
            foreach (var entry in generated)
            {
                var path = Path.Combine(outputDirectory, $"tile_{entry.Key}.png");
                entry.Value.Save(path, ImageFormat.Png);
                generatedPaths.Add(Path.GetFileName(path));
            }

            using var grassSide = BuildGrassSideTile(tileSize, generated["dirt"], generated["grass_top"]);
            grassSide.Save(Path.Combine(outputDirectory, "tile_grass_side.png"), ImageFormat.Png);
            generatedPaths.Add("tile_grass_side.png");

            using var logSide = BuildLogSideTile(tileSize, Rgba(132, 94, 62), Rgba(83, 56, 33), Rgba(168, 126, 82), 1177);
            logSide.Save(Path.Combine(outputDirectory, "tile_log_side.png"), ImageFormat.Png);
            generatedPaths.Add("tile_log_side.png");

            using var logTop = BuildLogTopTile(tileSize, Rgba(138, 100, 66), Rgba(86, 59, 34), Rgba(184, 137, 92), 1213);
            logTop.Save(Path.Combine(outputDirectory, "tile_log_top.png"), ImageFormat.Png);
            generatedPaths.Add("tile_log_top.png");

            using var atlas = BuildTileAtlas(tileSize, generated["stone"], generated["dirt"], generated["grass_top"], grassSide);
            atlas.Save(Path.Combine(outputDirectory, "tile_atlas_voxel_basic.png"), ImageFormat.Png);
            generatedPaths.Add("tile_atlas_voxel_basic.png");

            var oreDefs = new[]
            {
                new OreConfig("coal", Rgba(42, 44, 49), Rgba(20, 21, 24), 22, 1901),
                new OreConfig("iron", Rgba(189, 138, 99), Rgba(141, 95, 63), 20, 1931),
                new OreConfig("copper", Rgba(198, 122, 78), Rgba(147, 79, 47), 21, 1973),
                new OreConfig("gold", Rgba(226, 186, 62), Rgba(171, 129, 34), 18, 2017),
                new OreConfig("crystal", Rgba(123, 223, 237), Rgba(73, 154, 173), 16, 2063),
            };

            foreach (var ore in oreDefs)
            {
                using var overlay = BuildOreOverlayTile(tileSize, ore);
                overlay.Save(Path.Combine(oresDirectory, $"overlay_ore_{ore.Name}.png"), ImageFormat.Png);
                generatedPaths.Add($"ores/overlay_ore_{ore.Name}.png");

                using var oreStone = ComposeOverlay(generated["stone"], overlay, 0.92f);
                oreStone.Save(Path.Combine(oresDirectory, $"tile_stone_ore_{ore.Name}.png"), ImageFormat.Png);
                generatedPaths.Add($"ores/tile_stone_ore_{ore.Name}.png");
            }

            for (var stage = 0; stage <= 7; stage++)
            {
                using var crack = BuildCrackOverlay(tileSize, stage, 3111 + stage * 31);
                crack.Save(Path.Combine(overlaysDirectory, $"overlay_crack_{stage}.png"), ImageFormat.Png);
                generatedPaths.Add($"overlays/overlay_crack_{stage}.png");
            }

            using var moss = BuildTintOverlay(tileSize, Rgba(82, 137, 71, 180), 0.38f, 3301);
            moss.Save(Path.Combine(overlaysDirectory, "overlay_moss.png"), ImageFormat.Png);
            generatedPaths.Add("overlays/overlay_moss.png");

            using var frost = BuildTintOverlay(tileSize, Rgba(198, 228, 250, 175), 0.42f, 3349);
            frost.Save(Path.Combine(overlaysDirectory, "overlay_frost.png"), ImageFormat.Png);
            generatedPaths.Add("overlays/overlay_frost.png");

            using var scorch = BuildTintOverlay(tileSize, Rgba(74, 56, 48, 190), 0.35f, 3391);
            scorch.Save(Path.Combine(overlaysDirectory, "overlay_scorch.png"), ImageFormat.Png);
            generatedPaths.Add("overlays/overlay_scorch.png");

            using var wet = BuildTintOverlay(tileSize, Rgba(54, 79, 117, 150), 0.30f, 3433);
            wet.Save(Path.Combine(overlaysDirectory, "overlay_wet.png"), ImageFormat.Png);
            generatedPaths.Add("overlays/overlay_wet.png");
        }
        finally
        {
            foreach (var texture in generated.Values)
                texture.Dispose();
        }

        var readmePath = Path.Combine(outputDirectory, "README.txt");
        var lines = new List<string>
        {
            "Procedural voxel tile output",
            "",
            "Generated resources:",
        };
        lines.AddRange(generatedPaths.ConvertAll(static entry => $"- {entry}"));
        lines.Add(string.Empty);
        lines.Add("Regenerate via:");
        lines.Add("dotnet run --project Tools/MonsterMapAssetGenerator/MonsterMapAssetGenerator.csproj -- generate-voxel-tiles");
        File.WriteAllLines(readmePath, lines);

        return outputDirectory;
    }

    private static Bitmap BuildProceduralTile(int size, TileConfig config)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var rng = new Random(config.SeedOffset + size * 19);

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var grain = SampleValueNoise(x, y, config.SeedOffset, 6, size);
                var modulation = (grain - 0.5f) * config.GrainStrength;
                var color = ShiftColor(config.Base, modulation);
                bitmap.SetPixel(x, y, color);
            }
        }

        for (var i = 0; i < config.SpeckleCount; i++)
        {
            var x = rng.Next(0, size);
            var y = rng.Next(0, size);
            var useLight = (i & 1) == 0;
            var speckle = useLight ? config.Light : config.Dark;
            bitmap.SetPixel(x, y, Blend(bitmap.GetPixel(x, y), speckle, 0.6f));

            if (rng.NextDouble() < 0.28)
            {
                var nx = Math.Clamp(x + rng.Next(-1, 2), 0, size - 1);
                var ny = Math.Clamp(y + rng.Next(-1, 2), 0, size - 1);
                bitmap.SetPixel(nx, ny, Blend(bitmap.GetPixel(nx, ny), speckle, 0.35f));
            }
        }

        for (var crack = 0; crack < config.CrackCount; crack++)
        {
            var y = rng.Next(3, size - 3);
            var thickness = rng.Next(1, 3);
            for (var x = 1; x < size - 1; x++)
            {
                var wobble = (int)Math.Round(Math.Sin((x + crack * 3) * 0.55f) * 1.2f);
                var yy = Math.Clamp(y + wobble, 1, size - 2);
                for (var t = 0; t < thickness; t++)
                {
                    var py = Math.Clamp(yy + t, 0, size - 1);
                    bitmap.SetPixel(x, py, Blend(bitmap.GetPixel(x, py), config.Dark, 0.38f));
                }
            }
        }

        using var graphics = CreateGraphics(bitmap);
        using var borderPen = new Pen(Color.FromArgb(90, config.Dark), 1f);
        graphics.DrawRectangle(borderPen, 0, 0, size - 1, size - 1);
        return bitmap;
    }

    private static Bitmap BuildGrassSideTile(int size, Bitmap dirt, Bitmap grassTop)
    {
        var side = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
                side.SetPixel(x, y, dirt.GetPixel(x, y));
        }

        var capHeight = Math.Max(6, size / 4);
        for (var y = 0; y < capHeight; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var source = grassTop.GetPixel(x, y);
                var alpha = 0.72f - (y / (float)capHeight) * 0.18f;
                side.SetPixel(x, y, Blend(side.GetPixel(x, y), source, alpha));
            }
        }

        var rng = new Random(9291 + size);
        for (var x = 0; x < size; x++)
        {
            var blade = rng.Next(0, 3);
            for (var b = 0; b < blade; b++)
            {
                var y = Math.Clamp(capHeight + b, 0, size - 1);
                side.SetPixel(x, y, Blend(side.GetPixel(x, y), Rgba(72, 133, 56), 0.55f));
            }
        }

        return side;
    }

    private static Bitmap BuildLogSideTile(int size, Color baseColor, Color darkColor, Color lightColor, int seed)
    {
        var tile = BuildProceduralTile(size, new TileConfig("log_side", baseColor, darkColor, lightColor, 42, 0, 9, seed));
        using var graphics = CreateGraphics(tile);
        using var darkPen = new Pen(Color.FromArgb(110, darkColor), 1f);
        using var lightPen = new Pen(Color.FromArgb(85, lightColor), 1f);
        for (var y = 2; y < size; y += 4)
        {
            graphics.DrawLine(darkPen, 0, y, size - 1, y);
            if (y + 1 < size)
                graphics.DrawLine(lightPen, 0, y + 1, size - 1, y + 1);
        }

        return tile;
    }

    private static Bitmap BuildLogTopTile(int size, Color centerColor, Color ringColor, Color barkColor, int seed)
    {
        var tile = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var cx = (size - 1) * 0.5f;
        var cy = (size - 1) * 0.5f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                var dist = (float)Math.Sqrt(dx * dx + dy * dy) / (size * 0.5f);
                var noise = SampleValueNoise(x, y, seed, 4, size);
                var ring = (float)(Math.Sin((dist * 14f) + noise * 4f) * 0.5f + 0.5f);
                var tone = Blend(centerColor, ringColor, Math.Clamp(ring * 0.8f, 0f, 1f));
                if (dist > 0.86f)
                    tone = Blend(tone, barkColor, Math.Clamp((dist - 0.86f) * 5f, 0f, 1f));
                tile.SetPixel(x, y, tone);
            }
        }

        return tile;
    }

    private static void ApplyWoodPlankLines(Bitmap tile, int seed)
    {
        var rng = new Random(seed);
        for (var y = 3; y < tile.Height; y += 6)
        {
            for (var x = 0; x < tile.Width; x++)
            {
                var jitter = rng.Next(-8, 9);
                tile.SetPixel(x, y, ShiftColor(tile.GetPixel(x, y), jitter));
            }
        }
    }

    private static void ApplyBrickPattern(Bitmap tile, int seed)
    {
        var rng = new Random(seed);
        var mortar = Rgba(88, 78, 72);
        for (var y = 0; y < tile.Height; y++)
        {
            if (y % 8 == 0)
            {
                for (var x = 0; x < tile.Width; x++)
                    tile.SetPixel(x, y, Blend(tile.GetPixel(x, y), mortar, 0.65f));
            }
        }

        for (var x = 0; x < tile.Width; x += 8)
        {
            var offset = ((x / 8) & 1) == 0 ? 4 : 0;
            for (var y = offset; y < tile.Height; y += 8)
            {
                tile.SetPixel(x, y, Blend(tile.GetPixel(x, y), mortar, 0.72f));
                if (x + 1 < tile.Width)
                    tile.SetPixel(x + 1, y, Blend(tile.GetPixel(x + 1, y), mortar, 0.42f));
            }
        }

        for (var i = 0; i < 18; i++)
        {
            var x = rng.Next(1, tile.Width - 1);
            var y = rng.Next(1, tile.Height - 1);
            tile.SetPixel(x, y, ShiftColor(tile.GetPixel(x, y), rng.Next(-22, 18)));
        }
    }

    private static Bitmap BuildOreOverlayTile(int size, OreConfig ore)
    {
        var overlay = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var rng = new Random(ore.SeedOffset + size * 7);
        for (var i = 0; i < ore.NuggetCount; i++)
        {
            var x = rng.Next(1, size - 1);
            var y = rng.Next(1, size - 1);
            overlay.SetPixel(x, y, ore.Color);
            if (rng.NextDouble() < 0.55)
                overlay.SetPixel(Math.Clamp(x + rng.Next(-1, 2), 0, size - 1), Math.Clamp(y + rng.Next(-1, 2), 0, size - 1), ore.Shade);
            if (rng.NextDouble() < 0.45)
                overlay.SetPixel(Math.Clamp(x + rng.Next(-1, 2), 0, size - 1), Math.Clamp(y + rng.Next(-1, 2), 0, size - 1), Color.FromArgb(180, ore.Color));
        }

        return overlay;
    }

    private static Bitmap ComposeOverlay(Bitmap baseTile, Bitmap overlay, float alpha)
    {
        var result = new Bitmap(baseTile.Width, baseTile.Height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < result.Height; y++)
        {
            for (var x = 0; x < result.Width; x++)
            {
                var baseColor = baseTile.GetPixel(x, y);
                var over = overlay.GetPixel(x, y);
                if (over.A == 0)
                {
                    result.SetPixel(x, y, baseColor);
                    continue;
                }

                var strength = (over.A / 255f) * alpha;
                result.SetPixel(x, y, Blend(baseColor, over, strength));
            }
        }

        return result;
    }

    private static Bitmap BuildCrackOverlay(int size, int stage, int seed)
    {
        var image = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var rng = new Random(seed + stage * 41);
        var crackColor = Rgba(34, 30, 28, 90 + stage * 18);
        var branches = 2 + stage;
        for (var b = 0; b < branches; b++)
        {
            var x = rng.Next(0, size);
            var y = rng.Next(0, size);
            var length = size / 2 + stage * 2;
            var angle = rng.NextDouble() * Math.PI * 2;
            for (var i = 0; i < length; i++)
            {
                x = Math.Clamp((int)Math.Round(x + Math.Cos(angle)), 0, size - 1);
                y = Math.Clamp((int)Math.Round(y + Math.Sin(angle)), 0, size - 1);
                image.SetPixel(x, y, crackColor);
                if (stage >= 3 && rng.NextDouble() < 0.18)
                    image.SetPixel(Math.Clamp(x + rng.Next(-1, 2), 0, size - 1), Math.Clamp(y + rng.Next(-1, 2), 0, size - 1), Color.FromArgb(crackColor.A / 2, crackColor));
                angle += (rng.NextDouble() - 0.5) * 0.4;
            }
        }

        return image;
    }

    private static Bitmap BuildTintOverlay(int size, Color tint, float coverage, int seed)
    {
        var image = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var noise = SampleValueNoise(x, y, seed, 5, size);
                if (noise < 1f - coverage)
                    continue;

                var strength = Math.Clamp((noise - (1f - coverage)) / Math.Max(coverage, 0.01f), 0f, 1f);
                image.SetPixel(x, y, Color.FromArgb((int)(tint.A * strength), tint));
            }
        }

        return image;
    }

    private static Bitmap BuildTileAtlas(int tileSize, Bitmap stone, Bitmap dirt, Bitmap grassTop, Bitmap grassSide)
    {
        var atlas = new Bitmap(tileSize * 2, tileSize * 2, PixelFormat.Format32bppArgb);
        using var graphics = CreateGraphics(atlas);
        graphics.DrawImage(stone, 0, 0, tileSize, tileSize);
        graphics.DrawImage(dirt, tileSize, 0, tileSize, tileSize);
        graphics.DrawImage(grassTop, 0, tileSize, tileSize, tileSize);
        graphics.DrawImage(grassSide, tileSize, tileSize, tileSize, tileSize);
        return atlas;
    }

    private static float SampleValueNoise(int x, int y, int seed, int cellSize, int size)
    {
        var gx = x / cellSize;
        var gy = y / cellSize;
        var tx = (x % cellSize) / (float)cellSize;
        var ty = (y % cellSize) / (float)cellSize;

        var v00 = Hash01(gx, gy, seed);
        var v10 = Hash01(gx + 1, gy, seed);
        var v01 = Hash01(gx, gy + 1, seed);
        var v11 = Hash01(gx + 1, gy + 1, seed);

        var sx = SmoothStep(tx);
        var sy = SmoothStep(ty);
        var ix0 = Lerp(v00, v10, sx);
        var ix1 = Lerp(v01, v11, sx);
        var value = Lerp(ix0, ix1, sy);

        var edgeShade = 1f - Math.Clamp(DistanceToEdge(x, y, size) / (size * 0.5f), 0f, 1f) * 0.06f;
        return value * edgeShade;
    }

    private static float DistanceToEdge(int x, int y, int size)
    {
        var left = x;
        var right = size - 1 - x;
        var top = y;
        var bottom = size - 1 - y;
        return Math.Min(Math.Min(left, right), Math.Min(top, bottom));
    }

    private static Color ShiftColor(Color color, float delta)
    {
        var r = ClampToByte(color.R + delta);
        var g = ClampToByte(color.G + delta);
        var b = ClampToByte(color.B + delta);
        return Color.FromArgb(color.A, r, g, b);
    }

    private static Color Blend(Color baseColor, Color overlay, float alpha)
    {
        var t = Math.Clamp(alpha, 0f, 1f);
        var r = ClampToByte(baseColor.R + (overlay.R - baseColor.R) * t);
        var g = ClampToByte(baseColor.G + (overlay.G - baseColor.G) * t);
        var b = ClampToByte(baseColor.B + (overlay.B - baseColor.B) * t);
        return Color.FromArgb(baseColor.A, r, g, b);
    }

    private static float Hash01(int x, int y, int seed)
    {
        var n = x * 374761393 ^ y * 668265263 ^ seed * 1442695041;
        n = (n ^ (n >> 13)) * 1274126177;
        n ^= n >> 16;
        var masked = n & 0x7fffffff;
        return masked / (float)int.MaxValue;
    }

    private static float SmoothStep(float t) => t * t * (3f - 2f * t);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

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

    private readonly record struct TileConfig(
        string Name,
        Color Base,
        Color Dark,
        Color Light,
        int SpeckleCount,
        int CrackCount,
        int GrainStrength,
        int SeedOffset);

    private readonly record struct OreConfig(
        string Name,
        Color Color,
        Color Shade,
        int NuggetCount,
        int SeedOffset);
}
