using System;
using System.Text.Json.Serialization;

namespace MiniRPG.Core.Data;

/// <summary>
/// 捏脸系统的主数据结构：8 个部件 id + 4 个颜色 + 1 个地图 sprite 模板 id。
/// 完全独立于 Godot 类型；颜色用 <see cref="FaceColorRgba"/>（0-1 浮点）。
/// 序列化目标：玩家创建参数 / Actor 持久化字段 / 联机协议（未来）。
/// </summary>
public sealed class FaceCustomizationData
{
	[JsonPropertyName("headShapeId")]
	public string HeadShapeId { get; set; } = DefaultHeadShapeId;

	[JsonPropertyName("eyesId")]
	public string EyesId { get; set; } = DefaultEyesId;

	[JsonPropertyName("eyebrowsId")]
	public string EyebrowsId { get; set; } = DefaultEyebrowsId;

	[JsonPropertyName("noseId")]
	public string NoseId { get; set; } = DefaultNoseId;

	[JsonPropertyName("mouthId")]
	public string MouthId { get; set; } = DefaultMouthId;

	[JsonPropertyName("earsId")]
	public string EarsId { get; set; } = DefaultEarsId;

	[JsonPropertyName("hairId")]
	public string HairId { get; set; } = DefaultHairId;

	/// <summary>null 表示净面（clean shaven）。</summary>
	[JsonPropertyName("beardId")]
	public string? BeardId { get; set; }

	[JsonPropertyName("skinTone")]
	public FaceColorRgba SkinTone { get; set; } = FaceColorRgba.DefaultSkin;

	[JsonPropertyName("hairColor")]
	public FaceColorRgba HairColor { get; set; } = FaceColorRgba.DefaultHair;

	[JsonPropertyName("eyeColor")]
	public FaceColorRgba EyeColor { get; set; } = FaceColorRgba.DefaultEye;

	[JsonPropertyName("clothesColor")]
	public FaceColorRgba ClothesColor { get; set; } = FaceColorRgba.DefaultClothes;

	[JsonPropertyName("mapSpriteTemplateId")]
	public string MapSpriteTemplateId { get; set; } = DefaultMapSpriteTemplateId;

	public const string DefaultHeadShapeId = "round";
	public const string DefaultEyesId = "almond";
	public const string DefaultEyebrowsId = "thin_arched";
	public const string DefaultNoseId = "small_button";
	public const string DefaultMouthId = "small_neutral";
	public const string DefaultEarsId = "human_round";
	public const string DefaultHairId = "short_messy";
	public const string DefaultMapSpriteTemplateId = "human_male";

	public FaceCustomizationData Clone() => new()
	{
		HeadShapeId = HeadShapeId,
		EyesId = EyesId,
		EyebrowsId = EyebrowsId,
		NoseId = NoseId,
		MouthId = MouthId,
		EarsId = EarsId,
		HairId = HairId,
		BeardId = BeardId,
		SkinTone = SkinTone,
		HairColor = HairColor,
		EyeColor = EyeColor,
		ClothesColor = ClothesColor,
		MapSpriteTemplateId = MapSpriteTemplateId,
	};

	public static FaceCustomizationData CreateDefault() => new();

	/// <summary>
	/// 用确定性随机生成一个完整的捏脸数据。
	/// 用于 NPC 在 spawn 时按 instanceId hash 派生稳定随机外观（相同 instanceId 多次重生成结果一致）。
	/// 不接 race / sex 等约束，调用方按需后处理（例如把 raceId=elf 的 EarsId 强制锁定 pointed_elf）。
	/// </summary>
	public static FaceCustomizationData CreateRandom(Random rng)
	{
		return new FaceCustomizationData
		{
			HeadShapeId = PickRandom(HeadShapeIds, rng),
			EyesId = PickRandom(EyesIds, rng),
			EyebrowsId = PickRandom(EyebrowsIds, rng),
			NoseId = PickRandom(NoseIds, rng),
			MouthId = PickRandom(MouthIds, rng),
			EarsId = PickRandom(EarsIds, rng),
			HairId = PickRandom(HairIds, rng),
			BeardId = rng.Next(2) == 0 ? null : PickRandom(BeardIds, rng),
			SkinTone = PickRandom(SkinTones, rng),
			HairColor = PickRandom(HairColors, rng),
			EyeColor = PickRandom(EyeColors, rng),
			ClothesColor = PickRandom(ClothesColors, rng),
			MapSpriteTemplateId = DefaultMapSpriteTemplateId,
		};
	}

	private static T PickRandom<T>(T[] pool, Random rng) =>
		pool.Length == 0 ? default! : pool[rng.Next(pool.Length)];

	private static readonly string[] HeadShapeIds =
		["round", "oval", "square", "long", "heart", "diamond", "triangle", "wide"];
	private static readonly string[] EyesIds =
		["almond", "big_round", "narrow_sharp", "droopy", "upturned", "sleepy"];
	private static readonly string[] EyebrowsIds =
		["thin_arched", "thick_straight", "bushy_wild", "short_stubby", "soft_feminine", "raised_expressive"];
	private static readonly string[] NoseIds =
		["small_button", "straight_roman", "hooked", "wide_flat", "long_pointed", "upturned", "broad_bulbous"];
	private static readonly string[] MouthIds =
		["small_neutral", "wide_grin", "smirk", "frown", "full_lips", "thin_tight"];
	private static readonly string[] EarsIds =
		["human_round", "human_round", "human_round", "pointed_elf", "long_elf", "gnome_large"];
	private static readonly string[] HairIds =
		["short_messy", "long_flowing", "ponytail", "braided", "bald", "mohawk", "curly_afro"];
	private static readonly string[] BeardIds =
		["light_stubble", "short_trimmed", "full_bushy", "dwarf_braided", "goatee", "handlebar"];
	private static readonly FaceColorRgba[] SkinTones =
	[
		FaceColorRgba.Rgb(0.96f, 0.82f, 0.71f),
		FaceColorRgba.Rgb(0.88f, 0.71f, 0.55f),
		FaceColorRgba.Rgb(0.74f, 0.55f, 0.40f),
		FaceColorRgba.Rgb(0.55f, 0.38f, 0.27f),
		FaceColorRgba.Rgb(0.36f, 0.24f, 0.18f),
	];
	private static readonly FaceColorRgba[] HairColors =
	[
		FaceColorRgba.Rgb(0.10f, 0.07f, 0.05f),
		FaceColorRgba.Rgb(0.30f, 0.20f, 0.12f),
		FaceColorRgba.Rgb(0.55f, 0.40f, 0.22f),
		FaceColorRgba.Rgb(0.78f, 0.62f, 0.30f),
		FaceColorRgba.Rgb(0.92f, 0.86f, 0.66f),
		FaceColorRgba.Rgb(0.78f, 0.30f, 0.20f),
		FaceColorRgba.Rgb(0.55f, 0.40f, 0.78f),
		FaceColorRgba.Rgb(0.92f, 0.92f, 0.88f),
	];
	private static readonly FaceColorRgba[] EyeColors =
	[
		FaceColorRgba.Rgb(0.18f, 0.42f, 0.65f),
		FaceColorRgba.Rgb(0.22f, 0.55f, 0.30f),
		FaceColorRgba.Rgb(0.40f, 0.28f, 0.16f),
		FaceColorRgba.Rgb(0.65f, 0.50f, 0.20f),
		FaceColorRgba.Rgb(0.55f, 0.55f, 0.55f),
	];
	private static readonly FaceColorRgba[] ClothesColors =
	[
		FaceColorRgba.Rgb(0.46f, 0.32f, 0.22f),
		FaceColorRgba.Rgb(0.20f, 0.32f, 0.55f),
		FaceColorRgba.Rgb(0.55f, 0.25f, 0.20f),
		FaceColorRgba.Rgb(0.30f, 0.45f, 0.32f),
		FaceColorRgba.Rgb(0.22f, 0.22f, 0.26f),
		FaceColorRgba.Rgb(0.85f, 0.78f, 0.62f),
	];

	/// <summary>
	/// 用于 PortraitComposer 缓存键：所有可影响合成结果的字段拼接成稳定字符串。
	/// </summary>
	public string ComputeCacheKey()
	{
		return string.Join('|',
			HeadShapeId, EyesId, EyebrowsId, NoseId, MouthId, EarsId, HairId, BeardId ?? "_",
			SkinTone.ToHex(), HairColor.ToHex(), EyeColor.ToHex(), ClothesColor.ToHex(),
			MapSpriteTemplateId);
	}
}

/// <summary>
/// 0-1 浮点 RGBA。<see cref="MiniRPG.Shared"/> 不依赖 Godot，故自定义。
/// 在 Godot 端可用扩展方法 ToGodotColor() 转 <see cref="Godot.Color"/>。
/// </summary>
public readonly struct FaceColorRgba : IEquatable<FaceColorRgba>
{
	[JsonPropertyName("r")]
	public float R { get; init; }

	[JsonPropertyName("g")]
	public float G { get; init; }

	[JsonPropertyName("b")]
	public float B { get; init; }

	[JsonPropertyName("a")]
	public float A { get; init; }

	public static FaceColorRgba Rgb(float r, float g, float b, float a = 1f) => new()
	{
		R = Math.Clamp(r, 0f, 1f),
		G = Math.Clamp(g, 0f, 1f),
		B = Math.Clamp(b, 0f, 1f),
		A = Math.Clamp(a, 0f, 1f),
	};

	public string ToHex() =>
		$"{(int)(R * 255):X2}{(int)(G * 255):X2}{(int)(B * 255):X2}{(int)(A * 255):X2}";

	public static FaceColorRgba FromHex(string hex)
	{
		if (string.IsNullOrWhiteSpace(hex))
			return DefaultSkin;

		var trimmed = hex.TrimStart('#');
		if (trimmed.Length is not (6 or 8))
			return DefaultSkin;

		try
		{
			var r = Convert.ToInt32(trimmed[..2], 16) / 255f;
			var g = Convert.ToInt32(trimmed.Substring(2, 2), 16) / 255f;
			var b = Convert.ToInt32(trimmed.Substring(4, 2), 16) / 255f;
			var a = trimmed.Length == 8 ? Convert.ToInt32(trimmed.Substring(6, 2), 16) / 255f : 1f;
			return Rgb(r, g, b, a);
		}
		catch
		{
			return DefaultSkin;
		}
	}

	public bool Equals(FaceColorRgba other) =>
		R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B) && A.Equals(other.A);

	public override bool Equals(object? obj) => obj is FaceColorRgba other && Equals(other);

	public override int GetHashCode() => HashCode.Combine(R, G, B, A);

	public static bool operator ==(FaceColorRgba left, FaceColorRgba right) => left.Equals(right);

	public static bool operator !=(FaceColorRgba left, FaceColorRgba right) => !left.Equals(right);

	public static readonly FaceColorRgba DefaultSkin = Rgb(0.96f, 0.80f, 0.69f);
	public static readonly FaceColorRgba DefaultHair = Rgb(0.30f, 0.20f, 0.12f);
	public static readonly FaceColorRgba DefaultEye = Rgb(0.18f, 0.42f, 0.65f);
	public static readonly FaceColorRgba DefaultClothes = Rgb(0.46f, 0.32f, 0.22f);
}
