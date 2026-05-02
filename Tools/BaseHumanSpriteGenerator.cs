using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using static CharacterSpriteUtilities;

/// <summary>
/// 程序化生成"人形角色 8 方向 sprite sheet"的 generator。
///
/// 与 <see cref="MonsterMapAssetGenerator"/> 同款管线（共用 <see cref="CharacterSpriteUtilities"/>），
/// 输出 256x256 ×8 行的 vertical sheet，可被 <c>Data/entity_render.json</c> 的
/// <c>type:"texture"</c> + <c>useFacing:true</c> 直接消费。
///
/// 批 1：无参版本，固定 default 肤色 / 中性体型 / 短发 / 棕褐衣。
/// 批 2：会扩展为接受 <c>FaceCustomizationData</c> 参数化生成。
/// 批 4：会扩展为分层绘制（base + hair + clothing），按 hair / clothing / build 字段映射几何形状。
/// </summary>
public static class BaseHumanSpriteGenerator
{
	/// <summary>
	/// 写入 default base human 8 方向 sheet 到
	/// <c>Assets/Art/Generated/character_map_iso8/base_human_default_8dir.png</c>。
	/// </summary>
	public static string Generate(string rootPath)
	{
		var outputDirectory = Path.Combine(
			rootPath, "Assets", "Art", "Generated", "character_map_iso8");
		Directory.CreateDirectory(outputDirectory);

		using var bitmap = BuildDefaultHuman();
		using var normalized = NormalizeAnchor(bitmap);
		using var sheet = BuildProjectedDirectionalSheet(normalized);
		var path = Path.Combine(outputDirectory, "base_human_default_8dir.png");
		sheet.Save(path, ImageFormat.Png);
		return path;
	}

	/// <summary>
	/// 默认 base human：中性体型 + 标准肤色 + 短棕发 + 棕褐外衣。
	/// 几何分层（前后顺序）：阴影 → 腿 → 躯干（衣） → 上臂（衣） → 下臂（皮肤） → 头 → 头发剪影 → 眼。
	/// 比例参考 MonsterMapAssetGenerator 的 BuildOrcWarrior（保持视觉一致），但去掉肌肉感、武器、獠牙等怪物特征。
	/// </summary>
	public static Bitmap BuildDefaultHuman()
	{
		var bitmap = NewCanvas();
		using var graphics = CreateGraphics(bitmap);

		var skin = Rgba(232, 196, 162);          // 标准肤色
		var skinDark = Rgba(140, 96, 70);
		var hair = Rgba(96, 64, 38);             // 短棕发
		var hairDark = Rgba(48, 30, 18);
		var clothBody = Rgba(118, 88, 56);       // 棕褐 tunic
		var clothDark = Rgba(60, 42, 26);
		var clothPant = Rgba(82, 58, 38);        // 深棕 trousers
		var pantDark = Rgba(44, 30, 19);
		var beltLeather = Rgba(70, 46, 28);

		DrawShadow(graphics, 31, 56, 20, 6, 72);

		// 腿 (大腿到小腿)
		FillRectangleOutline(graphics, Box(25, 45, 30, 58), clothPant, pantDark);
		FillRectangleOutline(graphics, Box(33, 45, 38, 58), clothPant, pantDark);

		// 躯干 (上身衣)
		FillRectangleOutline(graphics, Box(23, 30, 40, 46), clothBody, clothDark);
		FillRectangleOutline(graphics, Box(22, 43, 41, 46), beltLeather, clothDark);

		// 上臂 (衣袖) + 下臂 (皮肤)
		FillRectangleOutline(graphics, Box(19, 30, 23, 38), clothBody, clothDark);
		FillRectangleOutline(graphics, Box(40, 30, 44, 38), clothBody, clothDark);
		FillRectangleOutline(graphics, Box(19, 38, 22, 44), skin, skinDark);
		FillRectangleOutline(graphics, Box(41, 38, 44, 44), skin, skinDark);

		// 头 (椭圆) — 居中
		FillEllipseOutline(graphics, Box(25, 16, 38, 29), skin, skinDark);

		// 头发 (短乱发剪影) — 头部上半的扁椭圆
		FillEllipseOutline(graphics, Box(24, 13, 39, 22), hair, hairDark);

		// 眼睛 (两个小色点)
		using var eyeBrush = new SolidBrush(Rgba(42, 30, 22));
		graphics.FillRectangle(eyeBrush, Box(28, 22, 29, 23));
		graphics.FillRectangle(eyeBrush, Box(34, 22, 35, 23));

		return bitmap;
	}

	public static IReadOnlyList<string> GenerateAll(string rootPath)
	{
		var outputs = new List<string>();
		outputs.Add(Generate(rootPath));
		return outputs;
	}
}
