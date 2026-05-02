using System;
using Godot;
using static MiniRPG.GodotImageGeometry;

namespace MiniRPG;

/// <summary>
/// 在没有真实部件 PNG 资产时，按 id 程序绘制 8 类部件占位图，
/// 让 PortraitComposer 的合成管线先打通。
/// 批 2 用 image_gen 铺真实素材后，本类只在 fallback 路径触发。
/// </summary>
internal static class ProceduralFacePartRenderer
{
	public const int CanvasWidth = 256;
	public const int CanvasHeight = 256;

	private static readonly Vector2I HeadCenter = new(CanvasWidth / 2, 130);

	/// <summary>
	/// 在 image 上按层叠顺序绘制一个完整肖像。颜色直接用，不再二次染色。
	/// </summary>
	public static void DrawFullPortrait(Image image, FaceCustomizationData data)
	{
		DrawHeadShapeLayer(image, data);
		DrawEarsLayer(image, data);
		DrawEyesLayer(image, data);
		DrawEyebrowsLayer(image, data);
		DrawNoseLayer(image, data);
		DrawMouthLayer(image, data);
		DrawHairLayer(image, data);
		if (!string.IsNullOrWhiteSpace(data.BeardId) && data.BeardId != "clean_shaven")
			DrawBeardLayer(image, data);
	}

	internal static void DrawHeadShapeLayer(Image image, FaceCustomizationData data) =>
		DrawHeadShape(image, data.HeadShapeId, ToGodot(data.SkinTone));

	internal static void DrawEarsLayer(Image image, FaceCustomizationData data) =>
		DrawEars(image, data.EarsId, ToGodot(data.SkinTone));

	internal static void DrawEyesLayer(Image image, FaceCustomizationData data) =>
		DrawEyes(image, data.EyesId, ToGodot(data.EyeColor));

	internal static void DrawEyebrowsLayer(Image image, FaceCustomizationData data) =>
		DrawEyebrows(image, data.EyebrowsId, ToGodot(data.HairColor));

	internal static void DrawNoseLayer(Image image, FaceCustomizationData data) =>
		DrawNose(image, data.NoseId, MultiplyAlpha(ToGodot(data.SkinTone), 0.55f));

	internal static void DrawMouthLayer(Image image, FaceCustomizationData data) =>
		DrawMouth(image, data.MouthId, new Color(0.62f, 0.30f, 0.30f, 1f));

	internal static void DrawHairLayer(Image image, FaceCustomizationData data) =>
		DrawHair(image, data.HairId, ToGodot(data.HairColor));

	internal static void DrawBeardLayer(Image image, FaceCustomizationData data)
	{
		if (string.IsNullOrWhiteSpace(data.BeardId))
			return;
		DrawBeard(image, data.BeardId!, ToGodot(data.HairColor));
	}

	private static void DrawHeadShape(Image image, string id, Color skin)
	{
		var c = HeadCenter;
		switch (id)
		{
			case "round":
				DrawFilledEllipse(image, c.X, c.Y, 78f, 88f, skin);
				break;
			case "oval":
				DrawFilledEllipse(image, c.X, c.Y, 70f, 96f, skin);
				break;
			case "square":
				DrawFilledRoundRect(image, c.X - 78, c.Y - 86, 156, 172, 14, skin);
				break;
			case "long":
				DrawFilledEllipse(image, c.X, c.Y + 6, 64f, 110f, skin);
				break;
			case "heart":
				DrawFilledEllipse(image, c.X, c.Y - 18, 88f, 70f, skin);
				DrawFilledTriangle(image, c.X - 60, c.Y + 24, c.X + 60, c.Y + 24, c.X, c.Y + 96, skin);
				break;
			case "diamond":
				DrawFilledDiamond(image, c.X, c.Y, 78, 102, skin);
				break;
			case "triangle":
				DrawFilledTriangle(image, c.X - 84, c.Y - 70, c.X + 84, c.Y - 70, c.X, c.Y + 100, skin);
				break;
			case "wide":
				DrawFilledEllipse(image, c.X, c.Y, 96f, 84f, skin);
				break;
			default:
				DrawFilledEllipse(image, c.X, c.Y, 78f, 88f, skin);
				break;
		}
	}

	private static void DrawEars(Image image, string id, Color skin)
	{
		var c = HeadCenter;
		var leftX = c.X - 76;
		var rightX = c.X + 76;
		var ny = c.Y + 4;
		switch (id)
		{
			case "human_round":
				DrawFilledEllipse(image, leftX, ny, 14f, 22f, skin);
				DrawFilledEllipse(image, rightX, ny, 14f, 22f, skin);
				break;
			case "pointed_elf":
				DrawFilledTriangle(image, leftX - 4, ny + 18, leftX + 12, ny - 4, leftX - 18, ny - 24, skin);
				DrawFilledTriangle(image, rightX + 4, ny + 18, rightX - 12, ny - 4, rightX + 18, ny - 24, skin);
				break;
			case "long_elf":
				DrawFilledTriangle(image, leftX - 6, ny + 24, leftX + 14, ny + 6, leftX - 36, ny - 56, skin);
				DrawFilledTriangle(image, rightX + 6, ny + 24, rightX - 14, ny + 6, rightX + 36, ny - 56, skin);
				break;
			case "gnome_large":
				DrawFilledEllipse(image, leftX - 6, ny, 22f, 32f, skin);
				DrawFilledEllipse(image, rightX + 6, ny, 22f, 32f, skin);
				break;
			case "beast_furred":
				DrawFilledTriangle(image, leftX - 6, ny + 8, leftX + 14, ny - 4, leftX - 14, ny - 30, skin);
				DrawFilledTriangle(image, rightX + 6, ny + 8, rightX - 14, ny - 4, rightX + 14, ny - 30, skin);
				break;
			case "orc_tusked":
				DrawFilledTriangle(image, leftX - 4, ny + 18, leftX + 12, ny - 4, leftX - 24, ny - 12, skin);
				DrawFilledTriangle(image, rightX + 4, ny + 18, rightX - 12, ny - 4, rightX + 24, ny - 12, skin);
				break;
			case "torn_half_orc":
				DrawFilledTriangle(image, leftX - 4, ny + 18, leftX + 6, ny - 4, leftX - 12, ny - 16, skin);
				DrawFilledTriangle(image, rightX + 4, ny + 18, rightX - 6, ny - 4, rightX + 12, ny - 16, skin);
				break;
			case "horned_tiefling":
				DrawFilledEllipse(image, leftX, ny, 14f, 22f, skin);
				DrawFilledEllipse(image, rightX, ny, 14f, 22f, skin);
				DrawFilledTriangle(image, c.X - 38, c.Y - 78, c.X - 14, c.Y - 78, c.X - 28, c.Y - 122, new Color(0.18f, 0.10f, 0.06f, 1f));
				DrawFilledTriangle(image, c.X + 14, c.Y - 78, c.X + 38, c.Y - 78, c.X + 28, c.Y - 122, new Color(0.18f, 0.10f, 0.06f, 1f));
				break;
			default:
				DrawFilledEllipse(image, leftX, ny, 14f, 22f, skin);
				DrawFilledEllipse(image, rightX, ny, 14f, 22f, skin);
				break;
		}
	}

	private static void DrawEyes(Image image, string id, Color eyeColor)
	{
		var c = HeadCenter;
		var leftX = c.X - 26;
		var rightX = c.X + 26;
		var ey = c.Y - 6;
		var sclera = new Color(0.94f, 0.94f, 0.92f, 1f);
		switch (id)
		{
			case "almond":
				DrawFilledEllipse(image, leftX, ey, 14f, 7f, sclera);
				DrawFilledEllipse(image, rightX, ey, 14f, 7f, sclera);
				DrawFilledEllipse(image, leftX, ey, 5f, 5f, eyeColor);
				DrawFilledEllipse(image, rightX, ey, 5f, 5f, eyeColor);
				break;
			case "big_round":
				DrawFilledEllipse(image, leftX, ey, 14f, 12f, sclera);
				DrawFilledEllipse(image, rightX, ey, 14f, 12f, sclera);
				DrawFilledEllipse(image, leftX, ey, 7f, 7f, eyeColor);
				DrawFilledEllipse(image, rightX, ey, 7f, 7f, eyeColor);
				break;
			case "narrow_sharp":
				DrawFilledEllipse(image, leftX, ey, 16f, 4f, sclera);
				DrawFilledEllipse(image, rightX, ey, 16f, 4f, sclera);
				DrawFilledEllipse(image, leftX, ey, 4f, 4f, eyeColor);
				DrawFilledEllipse(image, rightX, ey, 4f, 4f, eyeColor);
				break;
			case "droopy":
				DrawFilledEllipse(image, leftX, ey + 2, 14f, 7f, sclera);
				DrawFilledEllipse(image, rightX, ey + 2, 14f, 7f, sclera);
				DrawFilledEllipse(image, leftX - 2, ey + 4, 5f, 5f, eyeColor);
				DrawFilledEllipse(image, rightX + 2, ey + 4, 5f, 5f, eyeColor);
				break;
			case "upturned":
				DrawFilledEllipse(image, leftX, ey, 14f, 6f, sclera);
				DrawFilledEllipse(image, rightX, ey, 14f, 6f, sclera);
				DrawFilledEllipse(image, leftX + 4, ey - 2, 5f, 5f, eyeColor);
				DrawFilledEllipse(image, rightX - 4, ey - 2, 5f, 5f, eyeColor);
				break;
			case "sleepy":
				DrawFilledEllipse(image, leftX, ey, 14f, 4f, sclera);
				DrawFilledEllipse(image, rightX, ey, 14f, 4f, sclera);
				DrawFilledEllipse(image, leftX, ey, 3f, 3f, eyeColor);
				DrawFilledEllipse(image, rightX, ey, 3f, 3f, eyeColor);
				break;
			case "scarred":
				DrawFilledEllipse(image, leftX, ey, 14f, 7f, sclera);
				DrawFilledEllipse(image, leftX, ey, 5f, 5f, eyeColor);
				DrawThickLine(image, rightX - 16, ey - 16, rightX + 16, ey + 16, 4f, new Color(0.4f, 0.10f, 0.10f, 1f));
				DrawFilledEllipse(image, rightX, ey, 12f, 6f, new Color(0.16f, 0.08f, 0.06f, 1f));
				break;
			case "glowing":
				DrawFilledEllipse(image, leftX, ey, 14f, 8f, new Color(0.98f, 0.98f, 1f, 1f));
				DrawFilledEllipse(image, rightX, ey, 14f, 8f, new Color(0.98f, 0.98f, 1f, 1f));
				DrawFilledEllipse(image, leftX, ey, 7f, 7f, new Color(0.40f, 0.85f, 1f, 1f));
				DrawFilledEllipse(image, rightX, ey, 7f, 7f, new Color(0.40f, 0.85f, 1f, 1f));
				break;
			default:
				goto case "almond";
		}
	}

	private static void DrawEyebrows(Image image, string id, Color hair)
	{
		var c = HeadCenter;
		var leftX = c.X - 26;
		var rightX = c.X + 26;
		var by = c.Y - 22;
		switch (id)
		{
			case "thin_arched":
				DrawArc(image, leftX, by, 16f, 6f, 2.6f, 0.55f, 2f, hair);
				DrawArc(image, rightX, by, 16f, 6f, 2.6f, 0.55f, 2f, hair);
				break;
			case "thick_straight":
				DrawFilledRoundRect(image, leftX - 16, by - 3, 32, 6, 2, hair);
				DrawFilledRoundRect(image, rightX - 16, by - 3, 32, 6, 2, hair);
				break;
			case "bushy_wild":
				DrawFilledEllipse(image, leftX, by, 18f, 5f, hair);
				DrawFilledEllipse(image, rightX, by, 18f, 5f, hair);
				DrawFilledEllipse(image, leftX - 4, by - 3, 6f, 4f, hair);
				DrawFilledEllipse(image, rightX + 4, by - 3, 6f, 4f, hair);
				break;
			case "short_stubby":
				DrawFilledRoundRect(image, leftX - 8, by - 2, 16, 4, 2, hair);
				DrawFilledRoundRect(image, rightX - 8, by - 2, 16, 4, 2, hair);
				break;
			case "scarred_broken":
				DrawFilledRoundRect(image, leftX - 16, by - 2, 12, 4, 2, hair);
				DrawFilledRoundRect(image, leftX + 4, by - 2, 12, 4, 2, hair);
				DrawFilledRoundRect(image, rightX - 16, by - 2, 12, 4, 2, hair);
				DrawFilledRoundRect(image, rightX + 4, by - 2, 12, 4, 2, hair);
				break;
			case "pointed_evil":
				DrawFilledTriangle(image, leftX - 16, by + 4, leftX + 16, by + 4, leftX + 4, by - 8, hair);
				DrawFilledTriangle(image, rightX - 16, by + 4, rightX + 16, by + 4, rightX - 4, by - 8, hair);
				break;
			case "soft_feminine":
				DrawArc(image, leftX, by, 14f, 4f, 2.6f, 0.55f, 1.5f, hair);
				DrawArc(image, rightX, by, 14f, 4f, 2.6f, 0.55f, 1.5f, hair);
				break;
			case "raised_expressive":
				DrawArc(image, leftX, by, 16f, 8f, 2.4f, 0.7f, 2f, hair);
				DrawArc(image, rightX, by, 16f, 8f, 2.4f, 0.7f, 2f, hair);
				break;
			default:
				goto case "thin_arched";
		}
	}

	private static void DrawNose(Image image, string id, Color shadow)
	{
		var c = HeadCenter;
		var nx = c.X;
		var ny = c.Y + 12;
		switch (id)
		{
			case "small_button":
				DrawFilledEllipse(image, nx, ny, 6f, 4f, shadow);
				break;
			case "straight_roman":
				DrawFilledRoundRect(image, nx - 3, ny - 18, 6, 24, 2, shadow);
				DrawFilledEllipse(image, nx, ny + 4, 6f, 4f, shadow);
				break;
			case "hooked":
				DrawFilledTriangle(image, nx - 3, ny - 18, nx + 6, ny + 4, nx - 6, ny + 4, shadow);
				DrawFilledEllipse(image, nx + 1, ny + 4, 6f, 4f, shadow);
				break;
			case "wide_flat":
				DrawFilledEllipse(image, nx, ny + 2, 12f, 5f, shadow);
				break;
			case "long_pointed":
				DrawFilledTriangle(image, nx - 4, ny - 22, nx + 4, ny - 22, nx, ny + 8, shadow);
				break;
			case "crooked_broken":
				DrawFilledTriangle(image, nx - 6, ny - 16, nx + 2, ny - 8, nx - 2, ny + 6, shadow);
				DrawFilledTriangle(image, nx - 2, ny - 8, nx + 6, ny + 2, nx + 2, ny + 8, shadow);
				break;
			case "upturned":
				DrawFilledRoundRect(image, nx - 3, ny - 12, 6, 14, 2, shadow);
				DrawFilledEllipse(image, nx, ny + 4, 8f, 4f, shadow);
				break;
			case "broad_bulbous":
				DrawFilledEllipse(image, nx, ny + 6, 12f, 8f, shadow);
				break;
			default:
				goto case "small_button";
		}
	}

	private static void DrawMouth(Image image, string id, Color lip)
	{
		var c = HeadCenter;
		var mx = c.X;
		var my = c.Y + 36;
		var teeth = new Color(0.94f, 0.94f, 0.88f, 1f);
		switch (id)
		{
			case "small_neutral":
				DrawFilledRoundRect(image, mx - 14, my - 2, 28, 4, 2, lip);
				break;
			case "wide_grin":
				DrawArc(image, mx, my - 8, 30f, 12f, 0.4f, MathF.PI - 0.4f, 4f, lip);
				DrawFilledRoundRect(image, mx - 18, my - 4, 36, 6, 3, teeth);
				break;
			case "smirk":
				DrawArc(image, mx + 4, my - 4, 24f, 8f, 3.0f, 0.6f, 3f, lip);
				break;
			case "frown":
				DrawArc(image, mx, my + 6, 26f, 8f, MathF.PI + 0.4f, MathF.PI - 0.8f, 3f, lip);
				break;
			case "crooked_sneer":
				DrawArc(image, mx - 4, my, 24f, 6f, 2.8f, 0.6f, 3f, lip);
				DrawFilledRoundRect(image, mx - 12, my - 2, 8, 4, 2, lip);
				break;
			case "full_lips":
				DrawFilledEllipse(image, mx, my, 22f, 7f, lip);
				DrawFilledEllipse(image, mx, my + 2, 18f, 6f, MultiplyAlpha(lip, 0.6f));
				break;
			case "thin_tight":
				DrawFilledRoundRect(image, mx - 18, my - 1, 36, 2, 1, lip);
				break;
			case "fanged_toothy":
				DrawArc(image, mx, my - 8, 30f, 12f, 0.4f, MathF.PI - 0.4f, 4f, lip);
				DrawFilledRoundRect(image, mx - 18, my - 4, 36, 6, 3, teeth);
				DrawFilledTriangle(image, mx - 14, my - 4, mx - 8, my - 4, mx - 11, my + 4, teeth);
				DrawFilledTriangle(image, mx + 8, my - 4, mx + 14, my - 4, mx + 11, my + 4, teeth);
				break;
			default:
				goto case "small_neutral";
		}
	}

	private static void DrawHair(Image image, string id, Color hair)
	{
		var c = HeadCenter;
		switch (id)
		{
			case "short_messy":
				DrawFilledEllipse(image, c.X, c.Y - 60, 90f, 38f, hair);
				DrawFilledEllipse(image, c.X - 30, c.Y - 50, 18f, 14f, hair);
				DrawFilledEllipse(image, c.X + 30, c.Y - 52, 16f, 12f, hair);
				break;
			case "long_flowing":
				DrawFilledEllipse(image, c.X, c.Y - 50, 92f, 50f, hair);
				DrawFilledRoundRect(image, c.X - 90, c.Y - 50, 28, 130, 12, hair);
				DrawFilledRoundRect(image, c.X + 62, c.Y - 50, 28, 130, 12, hair);
				break;
			case "ponytail":
				DrawFilledEllipse(image, c.X, c.Y - 56, 84f, 36f, hair);
				DrawFilledEllipse(image, c.X + 88, c.Y - 24, 16f, 32f, hair);
				break;
			case "braided":
				DrawFilledEllipse(image, c.X, c.Y - 56, 84f, 36f, hair);
				DrawFilledRoundRect(image, c.X - 8, c.Y + 8, 16, 80, 6, hair);
				DrawFilledEllipse(image, c.X, c.Y + 28, 12f, 6f, MultiplyAlpha(hair, 0.6f));
				DrawFilledEllipse(image, c.X, c.Y + 48, 12f, 6f, MultiplyAlpha(hair, 0.6f));
				DrawFilledEllipse(image, c.X, c.Y + 68, 12f, 6f, MultiplyAlpha(hair, 0.6f));
				break;
			case "bald":
				break;
			case "mohawk":
				DrawFilledRoundRect(image, c.X - 10, c.Y - 110, 20, 70, 6, hair);
				DrawFilledTriangle(image, c.X - 14, c.Y - 70, c.X + 14, c.Y - 70, c.X, c.Y - 124, hair);
				break;
			case "curly_afro":
				DrawFilledEllipse(image, c.X, c.Y - 60, 110f, 60f, hair);
				DrawFilledEllipse(image, c.X - 38, c.Y - 28, 24f, 24f, hair);
				DrawFilledEllipse(image, c.X + 38, c.Y - 28, 24f, 24f, hair);
				DrawFilledEllipse(image, c.X, c.Y - 90, 36f, 28f, hair);
				break;
			case "hooded":
				DrawFilledEllipse(image, c.X, c.Y - 32, 110f, 78f, new Color(0.20f, 0.16f, 0.14f, 1f));
				DrawFilledEllipse(image, c.X, c.Y - 8, 78f, 60f, new Color(0f, 0f, 0f, 0.3f));
				break;
			default:
				goto case "short_messy";
		}
	}

	private static void DrawBeard(Image image, string id, Color hair)
	{
		var c = HeadCenter;
		var bx = c.X;
		var by = c.Y + 56;
		switch (id)
		{
			case "light_stubble":
				DrawFilledEllipse(image, bx, by, 56f, 18f, MultiplyAlpha(hair, 0.35f));
				break;
			case "short_trimmed":
				DrawFilledEllipse(image, bx, by, 56f, 22f, hair);
				break;
			case "full_bushy":
				DrawFilledEllipse(image, bx, by + 6, 72f, 42f, hair);
				break;
			case "dwarf_braided":
				DrawFilledEllipse(image, bx, by + 6, 64f, 36f, hair);
				DrawFilledRoundRect(image, bx - 10, by + 30, 20, 64, 8, hair);
				DrawFilledEllipse(image, bx, by + 50, 14f, 6f, MultiplyAlpha(hair, 0.6f));
				DrawFilledEllipse(image, bx, by + 70, 14f, 6f, MultiplyAlpha(hair, 0.6f));
				break;
			case "goatee":
				DrawFilledTriangle(image, bx - 14, by, bx + 14, by, bx, by + 30, hair);
				break;
			case "handlebar":
				DrawFilledRoundRect(image, bx - 28, by - 18, 56, 6, 3, hair);
				DrawFilledTriangle(image, bx - 30, by - 24, bx - 18, by - 18, bx - 36, by - 8, hair);
				DrawFilledTriangle(image, bx + 30, by - 24, bx + 18, by - 18, bx + 36, by - 8, hair);
				break;
			case "wizard_pointed":
				DrawFilledTriangle(image, bx - 26, by - 8, bx + 26, by - 8, bx, by + 90, hair);
				break;
			default:
				break;
		}
	}

	private static Color ToGodot(FaceColorRgba c) => new(c.R, c.G, c.B, c.A);
}
