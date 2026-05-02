using System.Text.Json;
using MiniRPG.Core.Data;
using Xunit;

namespace MiniRPG.Tests;

public sealed class FaceCustomizationDataTests
{
	[Fact]
	public void CreateDefault_HasAllFieldsPopulated_AndNoBeard()
	{
		var data = FaceCustomizationData.CreateDefault();

		Assert.Equal(FaceCustomizationData.DefaultHeadShapeId, data.HeadShapeId);
		Assert.Equal(FaceCustomizationData.DefaultEyesId, data.EyesId);
		Assert.Equal(FaceCustomizationData.DefaultEyebrowsId, data.EyebrowsId);
		Assert.Equal(FaceCustomizationData.DefaultNoseId, data.NoseId);
		Assert.Equal(FaceCustomizationData.DefaultMouthId, data.MouthId);
		Assert.Equal(FaceCustomizationData.DefaultEarsId, data.EarsId);
		Assert.Equal(FaceCustomizationData.DefaultHairId, data.HairId);
		Assert.Null(data.BeardId);
		Assert.Equal(FaceColorRgba.DefaultSkin, data.SkinTone);
		Assert.Equal(FaceColorRgba.DefaultHair, data.HairColor);
		Assert.Equal(FaceColorRgba.DefaultEye, data.EyeColor);
		Assert.Equal(FaceColorRgba.DefaultClothes, data.ClothesColor);
		Assert.Equal(FaceCustomizationData.DefaultMapSpriteTemplateId, data.MapSpriteTemplateId);
	}

	[Fact]
	public void Clone_ReturnsDeepCopy_NotReference()
	{
		var original = FaceCustomizationData.CreateDefault();
		original.HeadShapeId = "long";
		original.SkinTone = FaceColorRgba.Rgb(0.5f, 0.5f, 0.5f);

		var clone = original.Clone();
		clone.HeadShapeId = "round";
		clone.SkinTone = FaceColorRgba.Rgb(0.1f, 0.1f, 0.1f);

		Assert.Equal("long", original.HeadShapeId);
		Assert.Equal(FaceColorRgba.Rgb(0.5f, 0.5f, 0.5f), original.SkinTone);
		Assert.Equal("round", clone.HeadShapeId);
		Assert.Equal(FaceColorRgba.Rgb(0.1f, 0.1f, 0.1f), clone.SkinTone);
	}

	[Fact]
	public void ComputeCacheKey_SameInput_ProducesSameKey()
	{
		var a = FaceCustomizationData.CreateDefault();
		var b = FaceCustomizationData.CreateDefault();

		Assert.Equal(a.ComputeCacheKey(), b.ComputeCacheKey());
	}

	[Fact]
	public void ComputeCacheKey_AnyFieldChange_ProducesDifferentKey()
	{
		var baseline = FaceCustomizationData.CreateDefault();
		var baseKey = baseline.ComputeCacheKey();

		var headChanged = baseline.Clone();
		headChanged.HeadShapeId = "long";
		Assert.NotEqual(baseKey, headChanged.ComputeCacheKey());

		var beardChanged = baseline.Clone();
		beardChanged.BeardId = "wizard_pointed";
		Assert.NotEqual(baseKey, beardChanged.ComputeCacheKey());

		var skinChanged = baseline.Clone();
		skinChanged.SkinTone = FaceColorRgba.Rgb(0.1f, 0.1f, 0.1f);
		Assert.NotEqual(baseKey, skinChanged.ComputeCacheKey());

		var templateChanged = baseline.Clone();
		templateChanged.MapSpriteTemplateId = "orc";
		Assert.NotEqual(baseKey, templateChanged.ComputeCacheKey());
	}

	[Fact]
	public void ComputeCacheKey_BeardNullVsCleanShavenString_ProducesDifferentKeys()
	{
		var nullBeard = FaceCustomizationData.CreateDefault();
		Assert.Null(nullBeard.BeardId);

		var cleanShavenBeard = nullBeard.Clone();
		cleanShavenBeard.BeardId = "clean_shaven";

		Assert.NotEqual(nullBeard.ComputeCacheKey(), cleanShavenBeard.ComputeCacheKey());
	}

	[Fact]
	public void Serialize_RoundTrip_PreservesAllFields()
	{
		var original = new FaceCustomizationData
		{
			HeadShapeId = "long",
			EyesId = "glowing",
			EyebrowsId = "scarred_broken",
			NoseId = "hooked",
			MouthId = "fanged_toothy",
			EarsId = "horned_tiefling",
			HairId = "mohawk",
			BeardId = "wizard_pointed",
			SkinTone = FaceColorRgba.Rgb(0.78f, 0.30f, 0.20f),
			HairColor = FaceColorRgba.Rgb(0.06f, 0.04f, 0.04f),
			EyeColor = FaceColorRgba.Rgb(0.95f, 0.85f, 0.20f),
			ClothesColor = FaceColorRgba.Rgb(0.45f, 0.10f, 0.20f),
			MapSpriteTemplateId = "orc",
		};
		var json = JsonSerializer.Serialize(original);

		var loaded = JsonSerializer.Deserialize<FaceCustomizationData>(json);

		Assert.NotNull(loaded);
		Assert.Equal(original.HeadShapeId, loaded!.HeadShapeId);
		Assert.Equal(original.EyesId, loaded.EyesId);
		Assert.Equal(original.EyebrowsId, loaded.EyebrowsId);
		Assert.Equal(original.NoseId, loaded.NoseId);
		Assert.Equal(original.MouthId, loaded.MouthId);
		Assert.Equal(original.EarsId, loaded.EarsId);
		Assert.Equal(original.HairId, loaded.HairId);
		Assert.Equal(original.BeardId, loaded.BeardId);
		Assert.Equal(original.SkinTone, loaded.SkinTone);
		Assert.Equal(original.HairColor, loaded.HairColor);
		Assert.Equal(original.EyeColor, loaded.EyeColor);
		Assert.Equal(original.ClothesColor, loaded.ClothesColor);
		Assert.Equal(original.MapSpriteTemplateId, loaded.MapSpriteTemplateId);
	}

	[Fact]
	public void FaceColorRgba_Rgb_ClampsOutOfRangeValues()
	{
		var clipped = FaceColorRgba.Rgb(2.0f, -0.5f, 0.5f);

		Assert.Equal(1.0f, clipped.R);
		Assert.Equal(0.0f, clipped.G);
		Assert.Equal(0.5f, clipped.B);
		Assert.Equal(1.0f, clipped.A);
	}

	[Fact]
	public void FaceColorRgba_HexRoundTrip_PreservesValuesWithinByteResolution()
	{
		var original = FaceColorRgba.Rgb(0.5f, 0.25f, 0.75f);
		var hex = original.ToHex();
		var roundTripped = FaceColorRgba.FromHex(hex);

		Assert.InRange(roundTripped.R, 0.49f, 0.51f);
		Assert.InRange(roundTripped.G, 0.24f, 0.26f);
		Assert.InRange(roundTripped.B, 0.74f, 0.76f);
	}

	[Fact]
	public void FaceColorRgba_FromHex_InvalidString_ReturnsDefaultSkin()
	{
		Assert.Equal(FaceColorRgba.DefaultSkin, FaceColorRgba.FromHex(""));
		Assert.Equal(FaceColorRgba.DefaultSkin, FaceColorRgba.FromHex("nothex"));
		Assert.Equal(FaceColorRgba.DefaultSkin, FaceColorRgba.FromHex("12345"));
	}
}
