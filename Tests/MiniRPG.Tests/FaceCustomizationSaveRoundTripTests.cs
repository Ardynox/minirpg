using System;
using System.Linq;
using MiniRPG.Core.Data;
using MiniRPG.Core.Map;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 验证 FaceCustomization 在 SaveModule 里的双向 round-trip + 老存档兼容路径
/// （老存档没有 face 字段时由 PlayerAppearanceId 经 LegacyAppearanceMigration 派生）。
/// </summary>
public sealed class FaceCustomizationSaveRoundTripTests
{
	[Fact]
	public void RoundTrip_PlayerFaceCustomization_PreservesAllFields()
	{
		var state = CreateMinimalState();
		state.PlayerFaceCustomization = new FaceCustomizationData
		{
			HeadShapeId = "diamond",
			EyesId = "glowing",
			EyebrowsId = "pointed_evil",
			NoseId = "long_pointed",
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

		var save = SaveModule.BuildSnapshot(state);
		var restored = new GameState();
		SaveModule.ApplySnapshot(restored, save);

		Assert.NotNull(restored.PlayerFaceCustomization);
		Assert.Equal("diamond", restored.PlayerFaceCustomization!.HeadShapeId);
		Assert.Equal("glowing", restored.PlayerFaceCustomization.EyesId);
		Assert.Equal("wizard_pointed", restored.PlayerFaceCustomization.BeardId);
		Assert.Equal("orc", restored.PlayerFaceCustomization.MapSpriteTemplateId);
		Assert.Equal(state.PlayerFaceCustomization.SkinTone, restored.PlayerFaceCustomization.SkinTone);
		Assert.Equal(state.PlayerFaceCustomization.HairColor, restored.PlayerFaceCustomization.HairColor);
		Assert.Equal(state.PlayerFaceCustomization.EyeColor, restored.PlayerFaceCustomization.EyeColor);
		Assert.Equal(state.PlayerFaceCustomization.ClothesColor, restored.PlayerFaceCustomization.ClothesColor);
	}

	[Fact]
	public void RoundTrip_ActorFaceCustomization_PreservesAllFields()
	{
		var state = CreateMinimalState();
		var actor = state.Actors.Values.Single();
		actor.FaceCustomization = new FaceCustomizationData
		{
			HeadShapeId = "wide",
			EyesId = "narrow_sharp",
			HairId = "ponytail",
			BeardId = "full_bushy",
			SkinTone = FaceColorRgba.Rgb(0.78f, 0.62f, 0.46f),
			MapSpriteTemplateId = "dwarf",
		};

		var save = SaveModule.BuildSnapshot(state);
		var restored = new GameState();
		SaveModule.ApplySnapshot(restored, save);

		var restoredActor = restored.Actors.Values.Single();
		Assert.NotNull(restoredActor.FaceCustomization);
		Assert.Equal("wide", restoredActor.FaceCustomization!.HeadShapeId);
		Assert.Equal("ponytail", restoredActor.FaceCustomization.HairId);
		Assert.Equal("full_bushy", restoredActor.FaceCustomization.BeardId);
		Assert.Equal("dwarf", restoredActor.FaceCustomization.MapSpriteTemplateId);
	}

	[Fact]
	public void LegacyPayload_NoFaceCustomization_FallsBackToDefault()
	{
		// 老存档不再有 PlayerAppearanceId 字段也不再走 LegacyAppearanceMigration；
		// face 字段为 null 时 ApplySnapshot 后 PlayerFaceCustomization 直接 null，
		// ResolvePlayerFaceCustomization 返回新 default。
		var legacySave = new SaveFile
		{
			Version = SaveModule.CurrentVersion,
			Header = new SaveHeader
			{
				Title = "legacy",
				SavedAtUtc = DateTimeOffset.UtcNow,
				Turn = 0,
				PlayerZ = 0,
				GeneratorId = "room_corridor",
				ViewModeId = "single_layer",
			},
			Payload = new SavePayload
			{
				WorldSeed = 1,
				Turn = 0,
				PlayerX = 0,
				PlayerY = 0,
				PlayerZ = 0,
				PlayerId = "player",
				PlayerFaceCustomization = null,
				KillCount = 0,
				GeneratorId = "room_corridor",
				ViewModeId = "single_layer",
				Actors = [],
				Quests = [],
				DirtyChunks = [],
				Timeline = new TimelineSnapshot { Actors = [] },
			},
		};

		var restored = new GameState();
		SaveModule.ApplySnapshot(restored, legacySave);

		Assert.Null(restored.PlayerFaceCustomization);
		var resolved = restored.ResolvePlayerFaceCustomization();
		Assert.Equal(FaceCustomizationData.DefaultMapSpriteTemplateId, resolved.MapSpriteTemplateId);
	}

	[Fact]
	public void GameStateResolvePlayerFaceCustomization_NullReturnsDefault()
	{
		var state = new GameState
		{
			PlayerFaceCustomization = null,
		};

		var resolved = state.ResolvePlayerFaceCustomization();

		Assert.NotNull(resolved);
		Assert.Equal(FaceCustomizationData.DefaultMapSpriteTemplateId, resolved.MapSpriteTemplateId);
	}

	[Fact]
	public void GameStateResolvePlayerFaceCustomization_WhenSet_ReturnsItDirectly()
	{
		var explicitFace = new FaceCustomizationData
		{
			HeadShapeId = "triangle",
			MapSpriteTemplateId = "dwarf",
		};
		var state = new GameState();
		state.PlayerFaceCustomization = explicitFace;

		var resolved = state.ResolvePlayerFaceCustomization();

		Assert.Same(explicitFace, resolved);
	}

	[Fact]
	public void PlayerCreationOptionsResolveFace_PrefersExplicitFace()
	{
		var explicitFace = new FaceCustomizationData
		{
			HeadShapeId = "heart",
			MapSpriteTemplateId = "human_female",
		};
		var options = new PlayerCreationOptions
		{
			FaceCustomization = explicitFace,
		};

		var resolved = options.ResolveFaceCustomization();

		Assert.Equal("heart", resolved.HeadShapeId);
		Assert.Equal("human_female", resolved.MapSpriteTemplateId);
	}

	[Fact]
	public void PlayerCreationOptionsResolveFace_NullReturnsDefault()
	{
		var options = new PlayerCreationOptions();

		var resolved = options.ResolveFaceCustomization();

		Assert.Equal(FaceCustomizationData.DefaultMapSpriteTemplateId, resolved.MapSpriteTemplateId);
		Assert.Equal(FaceCustomizationData.DefaultHeadShapeId, resolved.HeadShapeId);
	}

	private static GameState CreateMinimalState()
	{
		var state = new GameState();
		state.WorldSeed = 99;
		state.Turn = 5;
		state.PlayerId = "player";

		state.Actors["player"] = new Actor
		{
			Id = "player",
			TemplateId = "player",
			DisplayName = "Hero",
			Glyph = "@",
			Faction = "player",
			X = 1,
			Y = 1,
			Z = 0,
		};
		return state;
	}
}
