using System;
using Godot;
using MiniRPG.Core.Weather;

namespace MiniRPG.Module.Render;

internal enum WeatherScreenFxMode
{
	None = 0,
	Rain = 1,
	Snow = 2,
	Fog = 3,
	Dust = 4,
	Thunderstorm = 5,
}

internal readonly record struct WeatherScreenFxParams(
	WeatherScreenFxMode Mode,
	float OverlayAlpha,
	float FogAlpha,
	float EdgeTintAlpha,
	float EdgeShadow,
	float Density,
	float Speed,
	float LightningFlash,
	float TemperatureBias,
	float Seed,
	Vector2 WindDirection,
	Color Tint)
{
	public static WeatherScreenFxParams Clear => new(
		WeatherScreenFxMode.None,
		0f,
		0f,
		0f,
		0f,
		0f,
		0f,
		0f,
		0f,
		0f,
		Vector2.Down,
		Colors.White);

	public bool HasVisibleFx =>
		OverlayAlpha > 0.002f
		|| FogAlpha > 0.002f
		|| EdgeTintAlpha > 0.002f
		|| EdgeShadow > 0.002f
		|| LightningFlash > 0.002f;
}

internal sealed class WeatherScreenFxState
{
	private const float ParamLerpRate = 5.5f;
	private const float ModeTransitionSeconds = 0.45f;

	public WeatherScreenFxParams Primary { get; private set; } = WeatherScreenFxParams.Clear;
	public WeatherScreenFxParams Secondary { get; private set; } = WeatherScreenFxParams.Clear;
	public float Blend { get; private set; }

	private bool _initialized;

	public bool HasVisibleFx =>
		Primary.HasVisibleFx
		|| Secondary.HasVisibleFx
		|| Blend > 0.001f;

	public void AdvanceTo(WeatherScreenFxParams target, double deltaSeconds)
	{
		var dt = (float)Math.Max(0d, deltaSeconds);
		if (!_initialized)
		{
			Primary = target;
			Secondary = WeatherScreenFxParams.Clear;
			Blend = 0f;
			_initialized = true;
			return;
		}

		var lerpT = dt <= 0f ? 1f : 1f - MathF.Exp(-ParamLerpRate * dt);
		if (Secondary.Mode != WeatherScreenFxMode.None || Blend > 0f)
		{
			Secondary = WeatherScreenFxResolver.Lerp(Secondary, target, lerpT);
			if (dt > 0f)
				Blend = Math.Clamp(Blend + dt / ModeTransitionSeconds, 0f, 1f);

			if (Blend >= 0.999f)
			{
				Primary = Secondary;
				Secondary = WeatherScreenFxParams.Clear;
				Blend = 0f;
			}
			return;
		}

		if (Primary.Mode != target.Mode)
		{
			Secondary = target;
			Blend = 0f;
			return;
		}

		Primary = WeatherScreenFxResolver.Lerp(Primary, target, lerpT);
	}
}

internal static class WeatherScreenFxResolver
{
	public static WeatherScreenFxMode ResolveMode(WeatherType type) => type switch
	{
		WeatherType.Rain => WeatherScreenFxMode.Rain,
		WeatherType.Storm => WeatherScreenFxMode.Rain,
		WeatherType.Thunderstorm => WeatherScreenFxMode.Thunderstorm,
		WeatherType.Snow => WeatherScreenFxMode.Snow,
		WeatherType.Fog => WeatherScreenFxMode.Fog,
		WeatherType.Sandstorm => WeatherScreenFxMode.Dust,
		_ => WeatherScreenFxMode.None,
	};

	public static WeatherScreenFxParams Resolve(WeatherSample sample, bool isActive, int hash)
	{
		if (!isActive || !sample.HasActiveWeather)
			return WeatherScreenFxParams.Clear;

		var mode = ResolveMode(sample.Type);
		if (mode == WeatherScreenFxMode.None)
			return WeatherScreenFxParams.Clear;

		var severity = Math.Clamp(sample.Severity * 1.0f + IntensityOffset(sample.Intensity), 0.08f, 1.15f);
		var temperatureBias = Math.Clamp((sample.TemperatureNormalized - 0.5f) * 2f, -1f, 1f);
		var seed = HashRange(hash, 0.05f, 0.95f);

		return mode switch
		{
			WeatherScreenFxMode.Rain => new WeatherScreenFxParams(
				mode,
				OverlayAlpha: Math.Clamp(0.12f + severity * 0.20f, 0.12f, 0.35f),
				FogAlpha: Math.Clamp(0.05f + severity * 0.12f, 0.05f, 0.20f),
				EdgeTintAlpha: Math.Clamp(0.03f + severity * 0.05f, 0.03f, 0.10f),
				EdgeShadow: Math.Clamp(0.04f + severity * 0.08f, 0.04f, 0.15f),
				Density: Math.Clamp(0.4f + severity * 0.65f, 0.4f, 1.2f),
				Speed: Math.Clamp(0.8f + severity * 0.8f, 0.8f, 1.8f),
				LightningFlash: 0f,
				TemperatureBias: temperatureBias,
				Seed: seed,
				WindDirection: new Vector2(0.24f, 1.0f).Normalized(),
				Tint: new Color(0.75f, 0.82f, 0.90f, 1f)),
			WeatherScreenFxMode.Thunderstorm => new WeatherScreenFxParams(
				mode,
				OverlayAlpha: Math.Clamp(0.15f + severity * 0.22f, 0.15f, 0.40f),
				FogAlpha: Math.Clamp(0.08f + severity * 0.14f, 0.08f, 0.25f),
				EdgeTintAlpha: Math.Clamp(0.10f + severity * 0.16f, 0.10f, 0.30f),
				EdgeShadow: Math.Clamp(0.15f + severity * 0.25f, 0.15f, 0.45f),
				Density: Math.Clamp(0.4f + severity * 0.7f, 0.4f, 1.2f),
				Speed: Math.Clamp(1.0f + severity * 1.0f, 1.0f, 2.2f),
				LightningFlash: Math.Clamp(0.3f + severity * 0.4f, 0.3f, 0.8f),
				TemperatureBias: temperatureBias,
				Seed: seed,
				WindDirection: new Vector2(0.28f, 1.0f).Normalized(),
				Tint: new Color(0.72f, 0.78f, 0.88f, 1f)),
			WeatherScreenFxMode.Snow => new WeatherScreenFxParams(
				mode,
				OverlayAlpha: Math.Clamp(0.10f + severity * 0.16f, 0.10f, 0.30f),
				FogAlpha: Math.Clamp(0.04f + severity * 0.12f, 0.04f, 0.20f),
				EdgeTintAlpha: Math.Clamp(0.02f + severity * 0.04f, 0.02f, 0.08f),
				EdgeShadow: Math.Clamp(0.02f + severity * 0.04f, 0.02f, 0.08f),
				Density: Math.Clamp(0.3f + severity * 0.5f, 0.3f, 0.9f),
				Speed: Math.Clamp(0.15f + severity * 0.20f, 0.15f, 0.40f),
				LightningFlash: 0f,
				TemperatureBias: temperatureBias,
				Seed: seed,
				WindDirection: new Vector2(0.07f, 1.0f).Normalized(),
				Tint: new Color(0.90f, 0.93f, 0.97f, 1f)),
			WeatherScreenFxMode.Fog => new WeatherScreenFxParams(
				mode,
				OverlayAlpha: 0f,
				FogAlpha: Math.Clamp(0.20f + severity * 0.32f, 0.20f, 0.60f),
				EdgeTintAlpha: Math.Clamp(0.10f + severity * 0.16f, 0.10f, 0.30f),
				EdgeShadow: Math.Clamp(0.08f + severity * 0.14f, 0.08f, 0.25f),
				Density: Math.Clamp(0.45f + severity * 0.55f, 0.35f, 1.1f),
				Speed: Math.Clamp(0.12f + severity * 0.18f, 0.1f, 0.35f),
				LightningFlash: 0f,
				TemperatureBias: temperatureBias,
				Seed: seed,
				WindDirection: new Vector2(0.26f, 0.04f).Normalized(),
				Tint: new Color(0.78f, 0.82f, 0.86f, 1f)),
			WeatherScreenFxMode.Dust => new WeatherScreenFxParams(
				mode,
				OverlayAlpha: Math.Clamp(0.10f + severity * 0.16f, 0.10f, 0.30f),
				FogAlpha: Math.Clamp(0.15f + severity * 0.24f, 0.15f, 0.45f),
				EdgeTintAlpha: Math.Clamp(0.15f + severity * 0.20f, 0.15f, 0.40f),
				EdgeShadow: Math.Clamp(0.10f + severity * 0.16f, 0.10f, 0.30f),
				Density: Math.Clamp(0.4f + severity * 0.6f, 0.3f, 1.1f),
				Speed: Math.Clamp(0.45f + severity * 0.5f, 0.35f, 1.05f),
				LightningFlash: 0f,
				TemperatureBias: temperatureBias,
				Seed: seed,
				WindDirection: new Vector2(1.0f, 0.04f).Normalized(),
				Tint: new Color(0.86f, 0.74f, 0.56f, 1f)),
			_ => WeatherScreenFxParams.Clear,
		};
	}

	public static WeatherScreenFxParams ApplyTuning(
		WeatherScreenFxParams value,
		WeatherScreenFxTuningSet? tuning)
	{
		if (value.Mode == WeatherScreenFxMode.None || tuning == null)
			return value;

		var profile = tuning.GetProfile(value.Mode);
		return new WeatherScreenFxParams(
			value.Mode,
			value.OverlayAlpha * WeatherScreenFxTuningProfile.ClampScale(profile.OverlayAlphaScale),
			value.FogAlpha * WeatherScreenFxTuningProfile.ClampScale(profile.FogAlphaScale),
			value.EdgeTintAlpha * WeatherScreenFxTuningProfile.ClampScale(profile.EdgeTintScale),
			value.EdgeShadow * WeatherScreenFxTuningProfile.ClampScale(profile.EdgeShadowScale),
			value.Density * WeatherScreenFxTuningProfile.ClampScale(profile.DensityScale),
			value.Speed * WeatherScreenFxTuningProfile.ClampScale(profile.SpeedScale),
			value.LightningFlash * WeatherScreenFxTuningProfile.ClampScale(profile.LightningFlashScale),
			value.TemperatureBias,
			value.Seed,
			value.WindDirection,
			value.Tint);
	}

	public static WeatherScreenFxParams Lerp(WeatherScreenFxParams from, WeatherScreenFxParams to, float t)
	{
		var clamped = Math.Clamp(t, 0f, 1f);
		return new WeatherScreenFxParams(
			clamped >= 0.5f ? to.Mode : from.Mode,
			Lerp(from.OverlayAlpha, to.OverlayAlpha, clamped),
			Lerp(from.FogAlpha, to.FogAlpha, clamped),
			Lerp(from.EdgeTintAlpha, to.EdgeTintAlpha, clamped),
			Lerp(from.EdgeShadow, to.EdgeShadow, clamped),
			Lerp(from.Density, to.Density, clamped),
			Lerp(from.Speed, to.Speed, clamped),
			Lerp(from.LightningFlash, to.LightningFlash, clamped),
			Lerp(from.TemperatureBias, to.TemperatureBias, clamped),
			Lerp(from.Seed, to.Seed, clamped),
			from.WindDirection.Lerp(to.WindDirection, clamped),
			Lerp(from.Tint, to.Tint, clamped));
	}

	private static float IntensityOffset(WeatherIntensity intensity) => intensity switch
	{
		WeatherIntensity.Light => 0.18f,
		WeatherIntensity.Heavy => 0.36f,
		_ => 0.26f,
	};

	private static float HashRange(int hash, float min, float max)
	{
		var normalized = (hash & 4095) / 4095f;
		return Lerp(min, max, normalized);
	}

	private static float Lerp(float from, float to, float t) =>
		from + ((to - from) * t);

	private static Color Lerp(Color from, Color to, float t) => new(
		Lerp(from.R, to.R, t),
		Lerp(from.G, to.G, t),
		Lerp(from.B, to.B, t),
		Lerp(from.A, to.A, t));
}
