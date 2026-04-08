using System;
using Godot;
using MiniRPG.Core.Weather;

namespace MiniRPG.Module.Render;

internal enum WeatherFxMode
{
	None = 0,
	Rain = 1,
	Snow = 2,
	Fog = 3,
	Dust = 4,
	Lightning = 5,
}

internal readonly record struct WeatherFxVisualParams(
	WeatherFxMode Mode,
	float FootprintTiles,
	float Alpha,
	float Density,
	float Speed,
	float BandStrength,
	float CoverageAlpha,
	float Intensity,
	float Seed,
	Vector2 WindDirection,
	Color Tint);

internal static class WeatherFxVisualResolver
{
	public static WeatherFxVisualParams? ResolveWeatherFxVisual(WeatherSample sample, PlayerVisionBand band, int hash) =>
		sample.Type switch
		{
			WeatherType.Rain => BuildVisual(
				WeatherFxMode.Rain,
				sample,
				band,
				hash,
				focusedDivisor: 2,
				peripheralDivisor: 4,
				focusedFootprint: 0.88f,
				peripheralFootprint: 0.74f,
				baseAlpha: 0.22f,
				baseDensity: 0.58f,
				baseSpeed: 1.15f,
				baseCoverage: 0.72f,
				tint: new Color(0.76f, 0.86f, 0.96f, 1f),
				windDirection: new Vector2(0.28f, 1.0f)),
			WeatherType.Storm => BuildVisual(
				WeatherFxMode.Rain,
				sample,
				band,
				hash,
				focusedDivisor: 2,
				peripheralDivisor: 4,
				focusedFootprint: 0.88f,
				peripheralFootprint: 0.74f,
				baseAlpha: 0.25f,
				baseDensity: 0.74f,
				baseSpeed: 1.45f,
				baseCoverage: 0.76f,
				tint: new Color(0.72f, 0.82f, 0.92f, 1f),
				windDirection: new Vector2(0.34f, 1.0f)),
			WeatherType.Thunderstorm => BuildVisual(
				WeatherFxMode.Rain,
				sample,
				band,
				hash,
				focusedDivisor: 2,
				peripheralDivisor: 5,
				focusedFootprint: 0.88f,
				peripheralFootprint: 0.74f,
				baseAlpha: 0.28f,
				baseDensity: 0.82f,
				baseSpeed: 1.62f,
				baseCoverage: 0.8f,
				tint: new Color(0.70f, 0.81f, 0.94f, 1f),
				windDirection: new Vector2(0.38f, 1.0f)),
			WeatherType.Snow => BuildVisual(
				WeatherFxMode.Snow,
				sample,
				band,
				hash,
				focusedDivisor: 2,
				peripheralDivisor: 4,
				focusedFootprint: 0.90f,
				peripheralFootprint: 0.76f,
				baseAlpha: 0.2f,
				baseDensity: 0.46f,
				baseSpeed: 0.46f,
				baseCoverage: 0.7f,
				tint: new Color(0.95f, 0.97f, 1.0f, 1f),
				windDirection: new Vector2(0.18f, 1.0f)),
			WeatherType.Fog => BuildVisual(
				WeatherFxMode.Fog,
				sample,
				band,
				hash,
				focusedDivisor: 1,
				peripheralDivisor: 2,
				focusedFootprint: 1.10f,
				peripheralFootprint: 0.96f,
				baseAlpha: 0.12f,
				baseDensity: 0.38f,
				baseSpeed: 0.24f,
				baseCoverage: 0.56f,
				tint: new Color(0.80f, 0.84f, 0.88f, 1f),
				windDirection: new Vector2(0.32f, 0.05f)),
			WeatherType.Sandstorm => BuildVisual(
				WeatherFxMode.Dust,
				sample,
				band,
				hash,
				focusedDivisor: 2,
				peripheralDivisor: 4,
				focusedFootprint: 0.92f,
				peripheralFootprint: 0.78f,
				baseAlpha: 0.2f,
				baseDensity: 0.62f,
				baseSpeed: 0.92f,
				baseCoverage: 0.72f,
				tint: new Color(0.88f, 0.76f, 0.56f, 1f),
				windDirection: new Vector2(1.0f, 0.08f)),
			_ => null,
		};

	public static WeatherFxVisualParams? ResolveLightningFxVisual(WeatherSample sample, PlayerVisionBand band, int hash) =>
		sample.Type != WeatherType.Thunderstorm
			? null
			: BuildVisual(
				WeatherFxMode.Lightning,
				sample,
				band,
				hash,
				focusedDivisor: 7,
				peripheralDivisor: 13,
				focusedFootprint: 0.72f,
				peripheralFootprint: 0.6f,
				baseAlpha: 0.16f,
				baseDensity: 0.52f,
				baseSpeed: 0.9f,
				baseCoverage: 0.44f,
				tint: new Color(0.92f, 0.96f, 1.0f, 1f),
				windDirection: new Vector2(0f, 1f));

	private static WeatherFxVisualParams? BuildVisual(
		WeatherFxMode mode,
		WeatherSample sample,
		PlayerVisionBand band,
		int hash,
		int focusedDivisor,
		int peripheralDivisor,
		float focusedFootprint,
		float peripheralFootprint,
		float baseAlpha,
		float baseDensity,
		float baseSpeed,
		float baseCoverage,
		Color tint,
		Vector2 windDirection)
	{
		if (band is PlayerVisionBand.Memory or PlayerVisionBand.Unknown || !sample.HasActiveWeather)
			return null;

		var divisor = band == PlayerVisionBand.Focused ? focusedDivisor : peripheralDivisor;
		if (divisor > 1 && hash % divisor != 0)
			return null;

		var bandStrength = band == PlayerVisionBand.Focused ? 1f : 0.68f;
		var intensity = sample.Intensity switch
		{
			WeatherIntensity.Light => 0.86f,
			WeatherIntensity.Heavy => 1.18f,
			_ => 1f,
		};
		var variance = HashRange(hash, 0.92f, 1.08f);
		var densityScale = band == PlayerVisionBand.Focused ? 1f : 0.58f;
		var speedScale = band == PlayerVisionBand.Focused ? 1f : 0.82f;
		return new WeatherFxVisualParams(
			mode,
			band == PlayerVisionBand.Focused ? focusedFootprint : peripheralFootprint,
			Math.Clamp(baseAlpha * intensity * bandStrength, 0.04f, 0.42f),
			Math.Clamp(baseDensity * intensity * densityScale * variance, 0.08f, 1.45f),
			Math.Clamp(baseSpeed * (0.92f + (intensity - 1f) * 0.75f) * speedScale, 0.08f, 2.5f),
			bandStrength,
			Math.Clamp(baseCoverage * bandStrength, 0.06f, 0.92f),
			Math.Clamp(intensity, 0.75f, 1.3f),
			HashRange(hash, 0.01f, 0.99f),
			Normalize(windDirection),
			tint);
	}

	private static Vector2 Normalize(Vector2 value) =>
		value.LengthSquared() <= float.Epsilon
			? Vector2.Right
			: value.Normalized();

	private static float HashRange(int hash, float min, float max)
	{
		var t = (hash & 1023) / 1023f;
		return min + ((max - min) * t);
	}
}
