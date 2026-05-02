using System.Collections.Generic;
using Godot;

namespace MiniRPG;

/// <summary>
/// 集中管理 base human 的"分层几何参数"，让 <see cref="MapSpriteRuntimeFactory"/> 的画图代码
/// 读这里的描述驱动，而不是把魔法数字散在画函数里。
///
/// 设计方向：每个 profile = "在 64x64 canvas 上某层应该长什么样"的几何描述（位置 / 大小 /
/// 形状类型 / 是否需要颜色提示），都是 readonly 静态字典，常量级别开销。
///
/// 不依赖任何业务类型；只用 Godot 基础类型（Vector2I / Color）。
/// 所有坐标都按 64x64 canvas，锚点 (31.5, 63)（同 <see cref="CharacterSpriteUtilities"/>）。
/// </summary>
internal static class CharacterGeometryProfiles
{
	/// <summary>体型 profile：按 MapSpriteTemplateId 决定身高 + 躯干宽度比例。</summary>
	public readonly record struct BodyProfile(
		int HeadCenterY,    // 头中心 Y
		float HeadRadius,   // 头椭圆半径
		int TorsoTop,       // 躯干上边
		int TorsoBottom,    // 躯干下边（腰带位置）
		int TorsoLeft,      // 躯干左边
		int TorsoRight,     // 躯干右边
		int LegTop,         // 腿上边（=TorsoBottom + 1）
		int LegBottom,      // 腿下边（接近脚 ≈ 58）
		int ArmShoulderY,   // 上臂顶部 Y
		int ArmHandY,       // 下臂底部 Y（手腕位置）
		int ShoulderInset); // 上臂相对躯干的水平偏移

	public static readonly BodyProfile DefaultBody = new(
		HeadCenterY: 22,
		HeadRadius: 6.5f,
		TorsoTop: 30,
		TorsoBottom: 43,
		TorsoLeft: 23,
		TorsoRight: 40,
		LegTop: 45,
		LegBottom: 58,
		ArmShoulderY: 30,
		ArmHandY: 45,
		ShoulderInset: 4);

	public static readonly Dictionary<string, BodyProfile> BodyByTemplate = new()
	{
		["human_male"] = DefaultBody,
		["human_female"] = DefaultBody with
		{
			TorsoLeft = 24,
			TorsoRight = 39,
		},
		["dwarf"] = DefaultBody with
		{
			HeadCenterY = 24,
			HeadRadius = 7.5f,
			TorsoTop = 32,
			TorsoBottom = 45,
			TorsoLeft = 22,
			TorsoRight = 41,
			LegTop = 47,
			LegBottom = 58,
			ArmShoulderY = 32,
		},
		["orc"] = DefaultBody with
		{
			HeadCenterY = 20,
			HeadRadius = 7f,
			TorsoTop = 28,
			TorsoBottom = 44,
			TorsoLeft = 22,
			TorsoRight = 41,
			ShoulderInset = 5,
		},
	};

	public static BodyProfile GetBody(string mapSpriteTemplateId) =>
		BodyByTemplate.TryGetValue(mapSpriteTemplateId, out var body) ? body : DefaultBody;

	/// <summary>
	/// 发型 profile：覆盖在头顶的剪影几何。
	/// 不同 hair id 不同 silhouette；运行时按 HairColor 染色。
	/// </summary>
	public readonly record struct HairProfile(HairShape Shape, float WidthScale, float HeightScale, int OffsetY, int TailYOffset);

	public enum HairShape { ShortMessy, LongFlowing, Ponytail, Braided, Bald, Mohawk, CurlyAfro, Hooded }

	public static readonly Dictionary<string, HairProfile> HairById = new()
	{
		["short_messy"]  = new(HairShape.ShortMessy,  1.0f, 1.0f, -4, 0),
		["long_flowing"] = new(HairShape.LongFlowing, 1.05f, 1.0f, -4, 16),
		["ponytail"]     = new(HairShape.Ponytail,    1.0f, 1.0f, -4, 8),
		["braided"]      = new(HairShape.Braided,     1.0f, 1.0f, -3, 18),
		["bald"]         = new(HairShape.Bald,        0f, 0f, 0, 0),
		["mohawk"]       = new(HairShape.Mohawk,      0.3f, 1.4f, -8, 0),
		["curly_afro"]   = new(HairShape.CurlyAfro,   1.3f, 1.2f, -5, 0),
		["hooded"]       = new(HairShape.Hooded,      1.2f, 1.0f, -2, 6),
	};

	public static HairProfile GetHair(string hairId) =>
		HairById.TryGetValue(hairId, out var hair) ? hair : HairById["short_messy"];

	/// <summary>
	/// 胡须 profile：覆盖在下巴 / 颈部的剪影。
	/// </summary>
	public readonly record struct BeardProfile(BeardShape Shape, float WidthScale, float HeightScale);

	public enum BeardShape { None, LightStubble, ShortTrimmed, FullBushy, DwarfBraided, Goatee, Handlebar, WizardPointed }

	public static readonly Dictionary<string, BeardProfile> BeardById = new()
	{
		["clean_shaven"]   = new(BeardShape.None, 0f, 0f),
		["light_stubble"]  = new(BeardShape.LightStubble,  1.0f, 0.4f),
		["short_trimmed"]  = new(BeardShape.ShortTrimmed,  1.0f, 0.6f),
		["full_bushy"]     = new(BeardShape.FullBushy,     1.2f, 1.1f),
		["dwarf_braided"]  = new(BeardShape.DwarfBraided,  1.0f, 1.6f),
		["goatee"]         = new(BeardShape.Goatee,        0.4f, 0.8f),
		["handlebar"]      = new(BeardShape.Handlebar,     1.2f, 0.4f),
		["wizard_pointed"] = new(BeardShape.WizardPointed, 0.6f, 1.8f),
	};

	public static BeardProfile GetBeard(string? beardId)
	{
		if (string.IsNullOrWhiteSpace(beardId))
			return BeardById["clean_shaven"];
		return BeardById.TryGetValue(beardId, out var beard) ? beard : BeardById["clean_shaven"];
	}

	/// <summary>
	/// 耳朵 profile：影响头部侧面剪影。pointy elf 加尖头突出；horned tiefling 在头顶加角。
	/// </summary>
	public readonly record struct EarsProfile(EarsShape Shape);

	public enum EarsShape { HumanRound, PointedElf, LongElf, GnomeLarge, BeastFurred, OrcTusked, TornHalfOrc, HornedTiefling }

	public static readonly Dictionary<string, EarsShape> EarsById = new()
	{
		["human_round"]     = EarsShape.HumanRound,
		["pointed_elf"]     = EarsShape.PointedElf,
		["long_elf"]        = EarsShape.LongElf,
		["gnome_large"]     = EarsShape.GnomeLarge,
		["beast_furred"]    = EarsShape.BeastFurred,
		["orc_tusked"]      = EarsShape.OrcTusked,
		["torn_half_orc"]   = EarsShape.TornHalfOrc,
		["horned_tiefling"] = EarsShape.HornedTiefling,
	};

	public static EarsShape GetEars(string earsId) =>
		EarsById.TryGetValue(earsId, out var ears) ? ears : EarsShape.HumanRound;
}
