using MiniRPG.Core.Weather;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class WeatherScreenFxResolverTests
{
	[Theory]
	[InlineData(WeatherType.Clear, (int)WeatherScreenFxMode.None)]
	[InlineData(WeatherType.Rain, (int)WeatherScreenFxMode.Rain)]
	[InlineData(WeatherType.Storm, (int)WeatherScreenFxMode.Rain)]
	[InlineData(WeatherType.Thunderstorm, (int)WeatherScreenFxMode.Thunderstorm)]
	[InlineData(WeatherType.Snow, (int)WeatherScreenFxMode.Snow)]
	[InlineData(WeatherType.Fog, (int)WeatherScreenFxMode.Fog)]
	[InlineData(WeatherType.Sandstorm, (int)WeatherScreenFxMode.Dust)]
	public void ResolveMode_MapsWeatherTypeToExpectedScreenMode(WeatherType type, int expectedMode)
	{
		Assert.Equal((WeatherScreenFxMode)expectedMode, WeatherScreenFxResolver.ResolveMode(type));
	}

	[Fact]
	public void Resolve_InactiveExposureReturnsClearFx()
	{
		var sample = new WeatherSample(WeatherType.Rain, WeatherIntensity.Heavy, Severity: 0.9f);

		var fx = WeatherScreenFxResolver.Resolve(sample, isActive: false, hash: 123);

		Assert.Equal(WeatherScreenFxMode.None, fx.Mode);
		Assert.False(fx.HasVisibleFx);
		Assert.Equal(0f, fx.OverlayAlpha);
	}

	[Fact]
	public void Resolve_ThunderstormProducesMoreAmbientShadowThanRain()
	{
		var rain = WeatherScreenFxResolver.Resolve(
			new WeatherSample(WeatherType.Rain, WeatherIntensity.Normal, Severity: 0.75f, TemperatureNormalized: 0.3f),
			isActive: true,
			hash: 11);
		var thunder = WeatherScreenFxResolver.Resolve(
			new WeatherSample(WeatherType.Thunderstorm, WeatherIntensity.Normal, Severity: 0.75f, TemperatureNormalized: 0.3f),
			isActive: true,
			hash: 11);

		Assert.Equal(WeatherScreenFxMode.Thunderstorm, thunder.Mode);
		Assert.True(thunder.EdgeShadow > rain.EdgeShadow);
		Assert.True(thunder.LightningFlash > 0f);
		Assert.InRange(thunder.OverlayAlpha, 0.018f, 0.04f);
		Assert.InRange(thunder.FogAlpha, 0.004f, 0.015f);
		Assert.InRange(thunder.Density, 0.2f, 0.46f);
	}

	[Fact]
	public void Resolve_RainUsesSubtleParticleOnlyProfile()
	{
		var rain = WeatherScreenFxResolver.Resolve(
			new WeatherSample(WeatherType.Rain, WeatherIntensity.Heavy, Severity: 0.9f, TemperatureNormalized: 0.3f),
			isActive: true,
			hash: 31);

		Assert.Equal(WeatherScreenFxMode.Rain, rain.Mode);
		Assert.InRange(rain.OverlayAlpha, 0.015f, 0.035f);
		Assert.InRange(rain.FogAlpha, 0.001f, 0.008f);
		Assert.Equal(0f, rain.EdgeTintAlpha);
		Assert.Equal(0f, rain.EdgeShadow);
		Assert.InRange(rain.Density, 0.18f, 0.42f);
	}

	[Fact]
	public void Resolve_SnowUsesSparseNeutralProfile()
	{
		var snow = WeatherScreenFxResolver.Resolve(
			new WeatherSample(WeatherType.Snow, WeatherIntensity.Heavy, Severity: 0.85f, TemperatureNormalized: 0.1f),
			isActive: true,
			hash: 47);

		Assert.Equal(WeatherScreenFxMode.Snow, snow.Mode);
		Assert.InRange(snow.OverlayAlpha, 0.01f, 0.028f);
		Assert.InRange(snow.FogAlpha, 0f, 0.006f);
		Assert.Equal(0f, snow.EdgeTintAlpha);
		Assert.Equal(0f, snow.EdgeShadow);
		Assert.InRange(snow.Density, 0.08f, 0.22f);
		Assert.InRange(snow.Speed, 0.12f, 0.28f);
	}

	[Fact]
	public void WeatherScreenFxState_CrossFadesModeChangesInsteadOfHardCutting()
	{
		var state = new WeatherScreenFxState();
		var rain = WeatherScreenFxResolver.Resolve(
			new WeatherSample(WeatherType.Rain, WeatherIntensity.Normal, Severity: 0.7f),
			isActive: true,
			hash: 5);
		var fog = WeatherScreenFxResolver.Resolve(
			new WeatherSample(WeatherType.Fog, WeatherIntensity.Heavy, Severity: 0.8f),
			isActive: true,
			hash: 9);

		state.AdvanceTo(rain, 0d);
		Assert.Equal(WeatherScreenFxMode.Rain, state.Primary.Mode);
		Assert.Equal(WeatherScreenFxMode.None, state.Secondary.Mode);

		state.AdvanceTo(fog, 0d);
		Assert.Equal(WeatherScreenFxMode.Rain, state.Primary.Mode);
		Assert.Equal(WeatherScreenFxMode.Fog, state.Secondary.Mode);
		Assert.Equal(0f, state.Blend);

		state.AdvanceTo(fog, 0.2d);
		Assert.True(state.Blend > 0f);
		Assert.True(state.Blend < 1f);
		Assert.Equal(WeatherScreenFxMode.Rain, state.Primary.Mode);
		Assert.Equal(WeatherScreenFxMode.Fog, state.Secondary.Mode);
	}

	[Fact]
	public void ApplyTuning_DefaultProfileIsNoOp()
	{
		var fx = WeatherScreenFxResolver.Resolve(
			new WeatherSample(WeatherType.Rain, WeatherIntensity.Normal, Severity: 0.7f),
			isActive: true,
			hash: 17);

		var tuned = WeatherScreenFxResolver.ApplyTuning(fx, new WeatherScreenFxTuningSet());

		Assert.Equal(fx, tuned);
	}

	[Fact]
	public void ApplyTuning_ScalesBaseResolverOutput()
	{
		var fx = WeatherScreenFxResolver.Resolve(
			new WeatherSample(WeatherType.Thunderstorm, WeatherIntensity.Heavy, Severity: 0.8f),
			isActive: true,
			hash: 29);
		var tuning = new WeatherScreenFxTuningSet();
		var profile = tuning.GetProfile(WeatherScreenFxMode.Thunderstorm);
		profile.OverlayAlphaScale = 0.5f;
		profile.FogAlphaScale = 1.5f;
		profile.DensityScale = 0.75f;
		profile.SpeedScale = 1.25f;
		profile.LightningFlashScale = 0.4f;

		var tuned = WeatherScreenFxResolver.ApplyTuning(fx, tuning);

		Assert.Equal(fx.Mode, tuned.Mode);
		Assert.Equal(fx.OverlayAlpha * 0.5f, tuned.OverlayAlpha, 4);
		Assert.Equal(fx.FogAlpha * 1.5f, tuned.FogAlpha, 4);
		Assert.Equal(fx.Density * 0.75f, tuned.Density, 4);
		Assert.Equal(fx.Speed * 1.25f, tuned.Speed, 4);
		Assert.Equal(fx.LightningFlash * 0.4f, tuned.LightningFlash, 4);
	}

	[Fact]
	public void ResolveBlendedProfile_LerpsShaderTuningUniforms()
	{
		var tuning = new WeatherScreenFxTuningSet();
		tuning.GetProfile(WeatherScreenFxMode.Rain).ParticleSizeScale = 0.6f;
		tuning.GetProfile(WeatherScreenFxMode.Thunderstorm).ParticleSizeScale = 1.6f;
		tuning.GetProfile(WeatherScreenFxMode.Rain).TintStrengthScale = 0.8f;
		tuning.GetProfile(WeatherScreenFxMode.Thunderstorm).TintStrengthScale = 1.4f;

		var blended = tuning.ResolveBlendedProfile(WeatherScreenFxMode.Rain, WeatherScreenFxMode.Thunderstorm, 0.25f);

		Assert.Equal(0.85f, blended.ParticleSizeScale, 3);
		Assert.Equal(0.95f, blended.TintStrengthScale, 3);
	}
}
