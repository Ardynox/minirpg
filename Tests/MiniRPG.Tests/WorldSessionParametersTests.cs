using System;
using MiniRPG.Core.Map;
using MiniRPG.Module.Session;
using Xunit;

namespace MiniRPG.Tests;

public sealed class WorldSessionParametersTests
{
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("blank_floor")]
	public void NormalizeGeneratorId_FallsBackToDefault_ForMissingOrLegacyIds(string? input)
	{
		Assert.Equal(
			WorldSessionParameters.DefaultGeneratorId,
			WorldSessionParameters.NormalizeGeneratorId(input));
	}

	[Fact]
	public void NormalizeGeneratorId_TrimsAndKeepsCustomId()
	{
		Assert.Equal("custom_gen", WorldSessionParameters.NormalizeGeneratorId("  custom_gen  "));
	}

	[Fact]
	public void Normalize_SeedZero_FallsBackToEnvironmentTickCount()
	{
		var original = WorldSettings.CreateDefault();
		original.Seed = 0;

		var normalized = WorldSessionParameters.Normalize(original);

		Assert.NotEqual(0, normalized.Seed);
	}

	[Fact]
	public void Normalize_ClampsDensitySliders_IntoAllowedRange()
	{
		var settings = WorldSettings.CreateDefault();
		settings.MonsterDensityPercent = 9999;
		settings.NpcDensityPercent = -100;
		settings.LootAbundancePercent = 1000;
		settings.NestIntensityPercent = -10;
		settings.WeatherVolatilityPercent = 600;

		var normalized = WorldSessionParameters.Normalize(settings);

		Assert.Equal(500, normalized.MonsterDensityPercent);
		Assert.Equal(0, normalized.NpcDensityPercent);
		Assert.Equal(500, normalized.LootAbundancePercent);
		Assert.Equal(0, normalized.NestIntensityPercent);
		Assert.Equal(500, normalized.WeatherVolatilityPercent);
	}

	[Fact]
	public void Normalize_FillsMissingBiomeDefaults()
	{
		var settings = WorldSettings.CreateDefault();
		settings.ClimateId = "";
		settings.StartSeasonId = "   ";
		settings.CivilizationLevelId = null!;

		var normalized = WorldSessionParameters.Normalize(settings);

		Assert.Equal("temperate", normalized.ClimateId);
		Assert.Equal("spring", normalized.StartSeasonId);
		Assert.Equal("frontier", normalized.CivilizationLevelId);
	}

	[Fact]
	public void Normalize_DoesNotMutateInputInstance()
	{
		var settings = WorldSettings.CreateDefault();
		settings.MonsterDensityPercent = 9999;

		_ = WorldSessionParameters.Normalize(settings);

		Assert.Equal(9999, settings.MonsterDensityPercent);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("  ")]
	public void NormalizePresetScenarioId_RejectsBlankInput(string? input)
	{
		Assert.Throws<ArgumentException>(() =>
			WorldSessionParameters.NormalizePresetScenarioId(input!));
	}

	[Fact]
	public void NormalizePresetScenarioId_TrimsAndReturnsInput()
	{
		Assert.Equal("demo_scenario", WorldSessionParameters.NormalizePresetScenarioId("  demo_scenario  "));
	}
}
