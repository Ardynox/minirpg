using System;
using System.Collections.Generic;
using Godot;
using static MiniRPG.GodotImageGeometry;

namespace MiniRPG;

/// <summary>
/// 运行时 base human 8 方向 sprite 合成。
///
/// 输入 <see cref="FaceCustomizationData"/>，输出一张 256x2048 的垂直 sheet（8 行 × 256x256 帧），
/// 与 <c>Tools/BaseHumanSpriteGenerator</c> 离线生成的 default PNG **同款锚点 + 同款投影**，
/// 但每个玩家的肤色 / 发色 / 衣色按捏脸数据画进 bitmap，不靠 shader 染色。
///
/// 由 <see cref="MiniRPG.Module.Render.IsometricVoxelRenderer.TryResolveActorSpriteVisual"/>
/// 在玩家分支调用，绕过 <c>entity_render.json</c> 静态条目。
///
/// 缓存：按 <see cref="FaceCustomizationData.ComputeCacheKey"/> + 颜色 hash 缓存 ImageTexture。
/// 同一组捏脸只合成一次，颜色变化时重新生成。
///
/// 投影管线和 <see cref="MonsterMapAssetGenerator"/> / <c>BaseHumanSpriteGenerator</c> 一致：
/// 64x64 normalized base canvas → 8 个方向变换 → 每个方向 64x64 → upscale 到 256x256
/// → 沿 Y 轴堆叠成 256x2048 vertical sheet。
/// </summary>
public sealed class MapSpriteRuntimeFactory
{
	public const int CanvasSize = 64;
	public const int ExportSize = 256;
	public const int DirectionCount = 8;
	private const float TargetAnchorCenterX = 31.5f;
	private const int TargetAnchorBottomY = 63;
	private const int MaxCacheEntries = 8;

	/// <summary>
	/// 8 方向投影变换，与 <c>CharacterSpriteUtilities.DirectionalTransforms</c> 完全一致。
	/// 每条 = (scaleX, scaleY, shearX, brightness, mirror)。两份（Tools / Runtime）必须保持同步，
	/// 否则同一玩家在 character creation 预览（runtime）和将来手动跑 generate-humans
	/// 出来的 default PNG 视觉会脱节。
	/// </summary>
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

	private static MapSpriteRuntimeFactory? _instance;
	public static MapSpriteRuntimeFactory Instance => _instance ??= new MapSpriteRuntimeFactory();

	private readonly Dictionary<string, ImageTexture> _cache = new();
	private readonly Queue<string> _cacheOrder = new();

	/// <summary>B9：按 res 路径缓存已解码的装备 overlay（避免每帧重复 <c>ResourceLoader.Load</c>）。</summary>
	private readonly Dictionary<string, Image?> _overlayNativeCache = new(StringComparer.Ordinal);

	/// <summary>
	/// 不带装备的捏脸 sprite（例如角色创建预览框，actor 还没装备）。
	/// </summary>
	public ImageTexture GetOrBuild(FaceCustomizationData data) =>
		GetOrBuild(data, EquipmentAppearanceData.Empty);

	/// <summary>
	/// 带装备的玩家 sprite。装备 hash 拼到 cache key 里，装备变化触发重新生成。
	/// </summary>
	public ImageTexture GetOrBuild(FaceCustomizationData data, EquipmentAppearanceData equipment)
	{
		var key = $"{data.ComputeCacheKey()}::{equipment.ComputeCacheKey()}";
		if (_cache.TryGetValue(key, out var cached))
			return cached;

		var texture = BuildSheetTexture(data, equipment);
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
		foreach (var img in _overlayNativeCache.Values)
			img?.Dispose();
		_overlayNativeCache.Clear();
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

	private ImageTexture BuildSheetTexture(FaceCustomizationData data, EquipmentAppearanceData equipment)
	{
		var useWeaponPng = EquipmentOverlayPaths.HasFullWeaponSet(equipment.Weapon.Kind);
		var useCloakPng = EquipmentOverlayPaths.HasFullCloakSet(equipment.Cloak.Kind);
		var useHelmetPng = EquipmentOverlayPaths.HasFullHelmetSet(equipment.Helmet.Kind);

		using var baseFrame = BuildBaseHumanCanvas(data);
		// 若某类装备在磁盘上凑齐 8 向 PNG，则改在「投影 + 放大到 ExportSize」之后再叠图（B9）；
		// 否则仍走旧路径：在 64×64 规范帧上程序画几何体，再随身体一起被投影。
		if (!useWeaponPng || !useCloakPng || !useHelmetPng)
			DrawEquipmentOverlay(baseFrame, data, equipment, drawCloak: !useCloakPng, drawHelmet: !useHelmetPng, drawWeapon: !useWeaponPng);

		var sheet = Image.CreateEmpty(ExportSize, ExportSize * DirectionCount, false, Image.Format.Rgba8);
		sheet.Fill(new Color(0f, 0f, 0f, 0f));

		for (var directionIndex = 0; directionIndex < DirectionalTransforms.Length; directionIndex++)
		{
			using var projected = ProjectDirectionalFrame(baseFrame, DirectionalTransforms[directionIndex]);
			using var upscaled = UpscaleNearest(projected, ExportSize, ExportSize);

			if (useCloakPng)
				TryBlendEquipmentOverlay(upscaled, EquipmentOverlayPaths.CloakPath(equipment.Cloak.Kind, directionIndex));
			if (useHelmetPng)
				TryBlendEquipmentOverlay(upscaled, EquipmentOverlayPaths.HelmetPath(equipment.Helmet.Kind, directionIndex));
			if (useWeaponPng)
				TryBlendEquipmentOverlay(upscaled, EquipmentOverlayPaths.WeaponPath(equipment.Weapon.Kind, directionIndex));

			sheet.BlendRect(upscaled, new Rect2I(0, 0, ExportSize, ExportSize), new Vector2I(0, directionIndex * ExportSize));
		}

		return ImageTexture.CreateFromImage(sheet);
	}

	/// <summary>
	/// 把装备的 weapon / cloak / helmet 三大件叠加到 base human canvas 上。
	/// 直接画在 64x64 base 上，靠 ProjectDirectionalFrame 自动得到 8 方向版本。
	/// </summary>
	private static void DrawEquipmentOverlay(Image canvas, FaceCustomizationData data, EquipmentAppearanceData equipment) =>
		DrawEquipmentOverlay(canvas, data, equipment, drawCloak: true, drawHelmet: true, drawWeapon: true);

	private static void DrawEquipmentOverlay(
		Image canvas,
		FaceCustomizationData data,
		EquipmentAppearanceData equipment,
		bool drawCloak,
		bool drawHelmet,
		bool drawWeapon)
	{
		var body = CharacterGeometryProfiles.GetBody(data.MapSpriteTemplateId);

		if (drawCloak && equipment.Cloak.Kind != CloakKind.None)
			DrawCloak(canvas, body, equipment.Cloak);

		if (drawHelmet && equipment.Helmet.Kind != HelmetKind.None)
			DrawHelmet(canvas, body, equipment.Helmet);

		if (drawWeapon && equipment.Weapon.Kind != WeaponKind.None)
			DrawWeapon(canvas, body, equipment.Weapon);
	}

	private Image? GetOrLoadNativeOverlay(string resPath)
	{
		if (string.IsNullOrEmpty(resPath))
			return null;
		if (_overlayNativeCache.TryGetValue(resPath, out var cached))
			return cached;

		if (!ResourceLoader.Exists(resPath))
		{
			_overlayNativeCache[resPath] = null;
			return null;
		}

		var tex = ResourceLoader.Load<Texture2D>(resPath);
		var image = tex.GetImage();
		if (image == null)
		{
			_overlayNativeCache[resPath] = null;
			return null;
		}

		var owned = image.Duplicate() as Image;
		if (owned == null)
		{
			_overlayNativeCache[resPath] = null;
			return null;
		}

		if (owned.GetFormat() != Image.Format.Rgba8)
			owned.Convert(Image.Format.Rgba8);
		_overlayNativeCache[resPath] = owned;
		return owned;
	}

	private void TryBlendEquipmentOverlay(Image dest256, string resPath)
	{
		var native = GetOrLoadNativeOverlay(resPath);
		if (native == null)
			return;

		var dw = dest256.GetWidth();
		var dh = dest256.GetHeight();
		if (native.GetWidth() == dw && native.GetHeight() == dh)
		{
			dest256.BlendRect(native, new Rect2I(0, 0, dw, dh), Vector2I.Zero);
			return;
		}

		using var scaled = UpscaleNearest(native, dw, dh);
		dest256.BlendRect(scaled, new Rect2I(0, 0, dw, dh), Vector2I.Zero);
	}

	private static void DrawCloak(Image canvas, CharacterGeometryProfiles.BodyProfile body, CloakAppearance cloak)
	{
		var tint = cloak.Tint;
		var dark = Darken(tint, 0.55f);
		switch (cloak.Kind)
		{
			case CloakKind.ShortCape:
				// 短披风覆盖肩到腰
				DrawFilledRoundRect(canvas, body.TorsoLeft - 2, body.TorsoTop - 1, body.TorsoRight - body.TorsoLeft + 4, body.TorsoBottom - body.TorsoTop, 2, tint);
				DrawArc(canvas, 31, body.TorsoTop, body.TorsoRight - body.TorsoLeft + 5, 1.5f, 0f, MathF.PI, 1f, dark);
				break;
			case CloakKind.LongCloak:
				// 长袍披到大腿
				DrawFilledRoundRect(canvas, body.TorsoLeft - 2, body.TorsoTop - 1,
					body.TorsoRight - body.TorsoLeft + 4, body.LegBottom - body.TorsoTop - 4, 2, tint);
				break;
			case CloakKind.HoodedCloak:
				// 兜帽 + 长披
				DrawFilledEllipse(canvas, 31, body.HeadCenterY - 1, body.HeadRadius + 1.5f, body.HeadRadius + 2f, tint);
				DrawFilledRoundRect(canvas, body.TorsoLeft - 2, body.TorsoTop - 1,
					body.TorsoRight - body.TorsoLeft + 4, body.LegBottom - body.TorsoTop - 4, 2, tint);
				break;
		}
	}

	private static void DrawHelmet(Image canvas, CharacterGeometryProfiles.BodyProfile body, HelmetAppearance helmet)
	{
		var tint = helmet.Tint;
		var dark = Darken(tint, 0.55f);
		switch (helmet.Kind)
		{
			case HelmetKind.Cap:
				// 简单帽子，盖住头顶半圆
				DrawFilledEllipse(canvas, 31, body.HeadCenterY - (int)(body.HeadRadius * 0.4f), body.HeadRadius + 0.5f, body.HeadRadius * 0.55f, tint);
				break;
			case HelmetKind.FullHelm:
				// 全包头盔
				DrawFilledEllipse(canvas, 31, body.HeadCenterY - 1, body.HeadRadius + 1f, body.HeadRadius + 1f, tint);
				DrawArc(canvas, 31, body.HeadCenterY - 1, body.HeadRadius + 1.5f, body.HeadRadius + 1.5f,
					MathF.PI * 0.05f, MathF.PI * 1.95f, 1f, dark);
				// 前面留一个 visor 缝
				var visorY = body.HeadCenterY + 1;
				DrawFilledRectangle(canvas, 28, visorY, 6, 1, new Color(0f, 0f, 0f, 0.55f));
				break;
			case HelmetKind.Crown:
				// 王冠 — 头顶一圈带尖
				var crownY = body.HeadCenterY - (int)(body.HeadRadius * 0.6f);
				DrawFilledRectangle(canvas, 25, crownY, 12, 2, tint);
				DrawFilledTriangle(canvas, 25, crownY, 28, crownY, 26, crownY - 3, tint);
				DrawFilledTriangle(canvas, 28, crownY, 31, crownY, 29, crownY - 4, tint);
				DrawFilledTriangle(canvas, 31, crownY, 34, crownY, 32, crownY - 4, tint);
				DrawFilledTriangle(canvas, 34, crownY, 37, crownY, 35, crownY - 3, tint);
				break;
		}
	}

	private static void DrawWeapon(Image canvas, CharacterGeometryProfiles.BodyProfile body, WeaponAppearance weapon)
	{
		var tint = weapon.Tint;
		var dark = Darken(tint, 0.55f);
		// 武器挂在右手位置（玩家视角的右边 = sprite 的 x>31 一侧）
		var handX = body.TorsoRight + body.ShoulderInset + 2;
		var handY = body.ArmHandY - 2;
		switch (weapon.Kind)
		{
			case WeaponKind.Sword:
				// 一把竖直的剑：长矩形 + 横向护手
				DrawFilledRectangle(canvas, handX - 1, handY - 14, 2, 12, tint);
				DrawFilledRectangle(canvas, handX - 2, handY - 2, 4, 1, dark);
				break;
			case WeaponKind.Axe:
				DrawFilledRectangle(canvas, handX - 1, handY - 14, 2, 12, dark);
				DrawFilledTriangle(canvas, handX - 1, handY - 14, handX + 4, handY - 13, handX - 1, handY - 9, tint);
				break;
			case WeaponKind.Bow:
				// 弓 — 一个弧形 + 弦
				DrawArc(canvas, handX, handY - 8, 6f, 8f, MathF.PI * 1.6f, MathF.PI * 0.4f, 1f, tint);
				DrawFilledRectangle(canvas, handX - 1, handY - 14, 1, 12, dark);
				break;
			case WeaponKind.Staff:
				// 法杖 — 长杆 + 杖头小球
				DrawFilledRectangle(canvas, handX - 1, handY - 16, 2, 14, tint);
				DrawFilledEllipse(canvas, handX, handY - 16, 2f, 2f, dark);
				break;
			case WeaponKind.Mace:
				// 锤子 — 杆 + 顶部圆
				DrawFilledRectangle(canvas, handX - 1, handY - 12, 2, 10, dark);
				DrawFilledEllipse(canvas, handX, handY - 12, 2.5f, 2f, tint);
				break;
		}
	}

	/// <summary>
	/// 画 64x64 base human 单帧（南向 / 正面），锚点 (31.5, 63)。
	/// 分层顺序：阴影 → 腿 → 躯干 / 衣 → 上臂 → 下臂（皮肤）→ 头 → 耳朵 / 角 → 头发 → 胡须 → 眼睛。
	/// 几何参数都从 <see cref="CharacterGeometryProfiles"/> 读，按 FaceCustomizationData 字段分支：
	///   - <c>MapSpriteTemplateId</c> → 体型 (BodyProfile)
	///   - <c>HairId</c> → 发型剪影 (HairProfile)
	///   - <c>BeardId</c> → 胡须剪影 (BeardProfile)
	///   - <c>EarsId</c> → 耳朵 / 角形状 (EarsShape)
	///   - <c>SkinTone / HairColor / ClothesColor</c> → 各层颜色
	/// 投影到 8 方向 + 64x64 后头部细节（眼睛 / 鼻子 / 嘴）实际只剩 1-2 像素，所以那些字段
	/// 不画进 sprite，只在肖像里反映；地图小人体型 / 发型 / 胡须 / 角等"轮廓级"差异看得清。
	/// </summary>
	private static Image BuildBaseHumanCanvas(FaceCustomizationData data)
	{
		var image = Image.CreateEmpty(CanvasSize, CanvasSize, false, Image.Format.Rgba8);
		image.Fill(new Color(0f, 0f, 0f, 0f));

		var body = CharacterGeometryProfiles.GetBody(data.MapSpriteTemplateId);
		var hairProfile = CharacterGeometryProfiles.GetHair(data.HairId);
		var beardProfile = CharacterGeometryProfiles.GetBeard(data.BeardId);
		var earsShape = CharacterGeometryProfiles.GetEars(data.EarsId);

		var skin = ToGodot(data.SkinTone);
		var skinDark = Darken(skin, 0.55f);
		var hair = ToGodot(data.HairColor);
		var hairDark = Darken(hair, 0.5f);
		var cloth = ToGodot(data.ClothesColor);
		var clothDark = Darken(cloth, 0.5f);
		var pant = Darken(cloth, 0.7f);
		var pantDark = Darken(pant, 0.55f);
		var beltLeather = Darken(cloth, 0.6f);
		var hornColor = new Color(0.16f, 0.10f, 0.06f, 1f);
		var eye = new Color(0.16f, 0.12f, 0.09f, 1f);

		DrawShadow(image);
		DrawLegs(image, body, pant, pantDark);
		DrawTorso(image, body, cloth, clothDark, beltLeather);
		DrawArms(image, body, cloth, clothDark, skin, skinDark);
		DrawHead(image, body, skin, skinDark);
		DrawEarsLayer(image, body, earsShape, skin, skinDark, hornColor);
		DrawHairLayer(image, body, hairProfile, hair, hairDark);
		DrawBeardLayer(image, body, beardProfile, hair, hairDark);
		DrawEyes(image, body, eye);

		return image;
	}

	private static void DrawShadow(Image image) =>
		DrawFilledEllipse(image, 31, 56, 10f, 3f, new Color(0f, 0f, 0f, 0.28f));

	private static void DrawLegs(Image image, CharacterGeometryProfiles.BodyProfile body, Color pant, Color pantDark)
	{
		var legHeight = body.LegBottom - body.LegTop;
		DrawFilledRoundRect(image, 25, body.LegTop, 5, legHeight, 1, pant);
		DrawFilledRoundRect(image, 33, body.LegTop, 5, legHeight, 1, pant);
		DrawFilledRectangle(image, 24, body.LegTop, 1, legHeight, pantDark);
		DrawFilledRectangle(image, 30, body.LegTop, 1, legHeight, pantDark);
		DrawFilledRectangle(image, 32, body.LegTop, 1, legHeight, pantDark);
		DrawFilledRectangle(image, 38, body.LegTop, 1, legHeight, pantDark);
	}

	private static void DrawTorso(Image image, CharacterGeometryProfiles.BodyProfile body, Color cloth, Color clothDark, Color belt)
	{
		var width = body.TorsoRight - body.TorsoLeft;
		var height = body.TorsoBottom - body.TorsoTop;
		DrawFilledRoundRect(image, body.TorsoLeft, body.TorsoTop, width, height, 2, cloth);
		// 腰带
		DrawFilledRectangle(image, body.TorsoLeft - 1, body.TorsoBottom, width + 2, 3, belt);
		// 躯干侧暗轮廓
		DrawFilledRectangle(image, body.TorsoLeft - 1, body.TorsoTop, 1, height, clothDark);
		DrawFilledRectangle(image, body.TorsoRight, body.TorsoTop, 1, height, clothDark);
	}

	private static void DrawArms(Image image, CharacterGeometryProfiles.BodyProfile body, Color cloth, Color clothDark, Color skin, Color skinDark)
	{
		var leftArmX = body.TorsoLeft - body.ShoulderInset;
		var rightArmX = body.TorsoRight;
		var armUpperHeight = body.TorsoBottom - body.ArmShoulderY - 5;
		var forearmTopY = body.ArmShoulderY + armUpperHeight;
		var forearmHeight = body.ArmHandY - forearmTopY;

		// 上臂（衣袖）
		DrawFilledRoundRect(image, leftArmX, body.ArmShoulderY, 4, armUpperHeight, 1, cloth);
		DrawFilledRoundRect(image, rightArmX + 1, body.ArmShoulderY, 4, armUpperHeight, 1, cloth);
		DrawFilledRectangle(image, leftArmX - 1, body.ArmShoulderY, 1, armUpperHeight, clothDark);
		DrawFilledRectangle(image, rightArmX + 5, body.ArmShoulderY, 1, armUpperHeight, clothDark);

		// 下臂（皮肤）
		if (forearmHeight > 0)
		{
			DrawFilledRoundRect(image, leftArmX, forearmTopY, 4, forearmHeight, 1, skin);
			DrawFilledRoundRect(image, rightArmX + 1, forearmTopY, 4, forearmHeight, 1, skin);
			DrawFilledRectangle(image, leftArmX - 1, forearmTopY, 1, forearmHeight, skinDark);
			DrawFilledRectangle(image, rightArmX + 5, forearmTopY, 1, forearmHeight, skinDark);
		}
	}

	private static void DrawHead(Image image, CharacterGeometryProfiles.BodyProfile body, Color skin, Color skinDark)
	{
		DrawFilledEllipse(image, 31, body.HeadCenterY, body.HeadRadius, body.HeadRadius, skin);
		DrawArc(image, 31, body.HeadCenterY, body.HeadRadius + 0.5f, body.HeadRadius + 0.5f,
			MathF.PI * 0.05f, MathF.PI * 1.95f, 1f, skinDark);
	}

	private static void DrawEarsLayer(Image image, CharacterGeometryProfiles.BodyProfile body, CharacterGeometryProfiles.EarsShape ears, Color skin, Color skinDark, Color hornColor)
	{
		var headLeft = (int)(31 - body.HeadRadius);
		var headRight = (int)(31 + body.HeadRadius);
		var earY = body.HeadCenterY;
		switch (ears)
		{
			case CharacterGeometryProfiles.EarsShape.HumanRound:
				DrawFilledEllipse(image, headLeft - 1, earY, 1.5f, 2f, skin);
				DrawFilledEllipse(image, headRight + 1, earY, 1.5f, 2f, skin);
				break;
			case CharacterGeometryProfiles.EarsShape.PointedElf:
				DrawFilledTriangle(image, headLeft - 2, earY + 2, headLeft + 1, earY - 1, headLeft - 3, earY - 3, skin);
				DrawFilledTriangle(image, headRight + 2, earY + 2, headRight - 1, earY - 1, headRight + 3, earY - 3, skin);
				break;
			case CharacterGeometryProfiles.EarsShape.LongElf:
				DrawFilledTriangle(image, headLeft - 2, earY + 3, headLeft + 1, earY, headLeft - 5, earY - 6, skin);
				DrawFilledTriangle(image, headRight + 2, earY + 3, headRight - 1, earY, headRight + 5, earY - 6, skin);
				break;
			case CharacterGeometryProfiles.EarsShape.GnomeLarge:
				DrawFilledEllipse(image, headLeft - 2, earY, 2.5f, 3.5f, skin);
				DrawFilledEllipse(image, headRight + 2, earY, 2.5f, 3.5f, skin);
				break;
			case CharacterGeometryProfiles.EarsShape.BeastFurred:
				DrawFilledTriangle(image, headLeft - 1, earY, headLeft + 2, earY - 2, headLeft - 2, earY - 4, skin);
				DrawFilledTriangle(image, headRight + 1, earY, headRight - 2, earY - 2, headRight + 2, earY - 4, skin);
				break;
			case CharacterGeometryProfiles.EarsShape.OrcTusked:
				DrawFilledTriangle(image, headLeft - 2, earY + 2, headLeft + 1, earY, headLeft - 3, earY - 1, skin);
				DrawFilledTriangle(image, headRight + 2, earY + 2, headRight - 1, earY, headRight + 3, earY - 1, skin);
				break;
			case CharacterGeometryProfiles.EarsShape.TornHalfOrc:
				DrawFilledTriangle(image, headLeft - 1, earY + 2, headLeft + 1, earY, headLeft - 1, earY - 2, skin);
				DrawFilledTriangle(image, headRight + 1, earY + 2, headRight - 1, earY, headRight + 1, earY - 2, skin);
				break;
			case CharacterGeometryProfiles.EarsShape.HornedTiefling:
				DrawFilledEllipse(image, headLeft - 1, earY, 1.5f, 2f, skin);
				DrawFilledEllipse(image, headRight + 1, earY, 1.5f, 2f, skin);
				// 头顶两个角
				DrawFilledTriangle(image, 27, body.HeadCenterY - 5, 30, body.HeadCenterY - 5, 28, body.HeadCenterY - 11, hornColor);
				DrawFilledTriangle(image, 32, body.HeadCenterY - 5, 35, body.HeadCenterY - 5, 34, body.HeadCenterY - 11, hornColor);
				break;
		}
	}

	private static void DrawHairLayer(Image image, CharacterGeometryProfiles.BodyProfile body, CharacterGeometryProfiles.HairProfile hair, Color hairColor, Color hairDark)
	{
		if (hair.Shape == CharacterGeometryProfiles.HairShape.Bald)
			return;

		var headTop = body.HeadCenterY + hair.OffsetY;
		var width = body.HeadRadius * hair.WidthScale + 0.5f;
		var height = body.HeadRadius * 0.7f * hair.HeightScale;

		switch (hair.Shape)
		{
			case CharacterGeometryProfiles.HairShape.ShortMessy:
				DrawFilledEllipse(image, 31, headTop, width, height, hairColor);
				// 几个不规则小簇
				DrawFilledEllipse(image, 27, headTop - 1, 1.5f, 1f, hairColor);
				DrawFilledEllipse(image, 35, headTop - 1, 1.5f, 1f, hairColor);
				break;
			case CharacterGeometryProfiles.HairShape.LongFlowing:
				DrawFilledEllipse(image, 31, headTop, width, height, hairColor);
				// 头后长发瀑布
				DrawFilledRoundRect(image, 25, body.HeadCenterY, 12, hair.TailYOffset, 2, hairColor);
				break;
			case CharacterGeometryProfiles.HairShape.Ponytail:
				DrawFilledEllipse(image, 31, headTop, width, height, hairColor);
				// 头后小马尾
				DrawFilledEllipse(image, 31, body.HeadCenterY + hair.TailYOffset, 2f, 4f, hairColor);
				break;
			case CharacterGeometryProfiles.HairShape.Braided:
				DrawFilledEllipse(image, 31, headTop, width, height, hairColor);
				// 单条长辫
				DrawFilledRectangle(image, 30, body.HeadCenterY + 2, 2, hair.TailYOffset, hairColor);
				break;
			case CharacterGeometryProfiles.HairShape.Mohawk:
				DrawFilledRectangle(image, 30, headTop, 2, (int)(height * 1.5f), hairColor);
				DrawFilledTriangle(image, 28, headTop, 33, headTop, 31, headTop - 5, hairColor);
				break;
			case CharacterGeometryProfiles.HairShape.CurlyAfro:
				DrawFilledEllipse(image, 31, headTop, width, height, hairColor);
				DrawFilledEllipse(image, 27, headTop + 2, 2f, 2f, hairColor);
				DrawFilledEllipse(image, 35, headTop + 2, 2f, 2f, hairColor);
				DrawFilledEllipse(image, 31, headTop - 3, 3f, 2.5f, hairColor);
				break;
			case CharacterGeometryProfiles.HairShape.Hooded:
				// 兜帽完全包住头部
				DrawFilledEllipse(image, 31, body.HeadCenterY - 1, body.HeadRadius + 1.5f, body.HeadRadius + 2f, hairColor);
				DrawFilledEllipse(image, 31, body.HeadCenterY + 2, body.HeadRadius - 0.5f, body.HeadRadius - 1f, new Color(0f, 0f, 0f, 0.45f));
				break;
		}

		// 暗轮廓
		if (hair.Shape != CharacterGeometryProfiles.HairShape.Mohawk)
			DrawArc(image, 31, headTop, width + 0.5f, height + 0.5f,
				MathF.PI * 0.05f, MathF.PI * 0.95f, 1f, hairDark);
	}

	private static void DrawBeardLayer(Image image, CharacterGeometryProfiles.BodyProfile body, CharacterGeometryProfiles.BeardProfile beard, Color hairColor, Color hairDark)
	{
		if (beard.Shape == CharacterGeometryProfiles.BeardShape.None)
			return;

		var beardCenterY = body.HeadCenterY + 5;
		var width = body.HeadRadius * beard.WidthScale * 0.7f;
		var height = body.HeadRadius * beard.HeightScale * 0.6f;

		switch (beard.Shape)
		{
			case CharacterGeometryProfiles.BeardShape.LightStubble:
				// 半透明
				var stubble = new Color(hairColor.R, hairColor.G, hairColor.B, 0.45f);
				DrawFilledEllipse(image, 31, beardCenterY, width, height, stubble);
				break;
			case CharacterGeometryProfiles.BeardShape.ShortTrimmed:
				DrawFilledEllipse(image, 31, beardCenterY, width, height, hairColor);
				break;
			case CharacterGeometryProfiles.BeardShape.FullBushy:
				DrawFilledEllipse(image, 31, beardCenterY, width, height, hairColor);
				DrawArc(image, 31, beardCenterY, width + 0.5f, height + 0.5f,
					MathF.PI * 0.05f, MathF.PI * 1.95f, 1f, hairDark);
				break;
			case CharacterGeometryProfiles.BeardShape.DwarfBraided:
				DrawFilledEllipse(image, 31, beardCenterY, width, height * 0.5f, hairColor);
				// 长辫垂到腰
				DrawFilledRectangle(image, 30, beardCenterY + 2, 2, 12, hairColor);
				break;
			case CharacterGeometryProfiles.BeardShape.Goatee:
				DrawFilledTriangle(image,
					31 - (int)width, beardCenterY,
					31 + (int)width, beardCenterY,
					31, beardCenterY + (int)(height * 1.5f),
					hairColor);
				break;
			case CharacterGeometryProfiles.BeardShape.Handlebar:
				// 八字胡
				DrawFilledEllipse(image, 28, beardCenterY - 2, 2f, 0.8f, hairColor);
				DrawFilledEllipse(image, 34, beardCenterY - 2, 2f, 0.8f, hairColor);
				break;
			case CharacterGeometryProfiles.BeardShape.WizardPointed:
				// 长尖须
				DrawFilledTriangle(image,
					31 - (int)width, beardCenterY,
					31 + (int)width, beardCenterY,
					31, beardCenterY + (int)(height * 2.5f),
					hairColor);
				break;
		}
	}

	private static void DrawEyes(Image image, CharacterGeometryProfiles.BodyProfile body, Color eye)
	{
		// 64x64 投影到 8 方向后眼睛只剩 1 像素，不画细节，画两个小色点提示。
		image.SetPixel(28, body.HeadCenterY + 1, eye);
		image.SetPixel(29, body.HeadCenterY + 1, eye);
		image.SetPixel(33, body.HeadCenterY + 1, eye);
		image.SetPixel(34, body.HeadCenterY + 1, eye);
	}

	private static Image ProjectDirectionalFrame(Image normalizedSource, DirectionalTransform transform)
	{
		var frame = Image.CreateEmpty(CanvasSize, CanvasSize, false, Image.Format.Rgba8);
		frame.Fill(new Color(0f, 0f, 0f, 0f));
		var srcWidth = normalizedSource.GetWidth();
		var srcHeight = normalizedSource.GetHeight();
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
				if (sourceX < 0 || sourceX >= srcWidth || sourceY < 0 || sourceY >= srcHeight)
					continue;
				var color = normalizedSource.GetPixel(sourceX, sourceY);
				if (color.A < 0.01f)
					continue;
				frame.SetPixel(x, y, ApplyBrightness(color, transform.Brightness));
			}
		}
		return frame;
	}

	private static Image UpscaleNearest(Image source, int targetWidth, int targetHeight)
	{
		var scaleX = source.GetWidth();
		var scaleY = source.GetHeight();
		var image = Image.CreateEmpty(targetWidth, targetHeight, false, Image.Format.Rgba8);
		image.Fill(new Color(0f, 0f, 0f, 0f));
		for (var py = 0; py < targetHeight; py++)
		{
			var srcY = py * scaleY / targetHeight;
			for (var px = 0; px < targetWidth; px++)
			{
				var srcX = px * scaleX / targetWidth;
				var color = source.GetPixel(srcX, srcY);
				if (color.A > 0f)
					image.SetPixel(px, py, color);
			}
		}
		return image;
	}

	private static Color ToGodot(FaceColorRgba c) => new(c.R, c.G, c.B, c.A);

	private static Color Darken(Color c, float factor) =>
		new(c.R * factor, c.G * factor, c.B * factor, c.A);

	/// <summary>
	/// 8 方向投影参数。和 <c>CharacterSpriteUtilities.DirectionalTransform</c> 等价但独立定义
	/// （Tools 和 App 项目编译隔离，不能共享 record struct）。
	/// </summary>
	private readonly record struct DirectionalTransform(
		float ScaleX,
		float ScaleY,
		float ShearX,
		float Brightness,
		bool Mirror);
}
