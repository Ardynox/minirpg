using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace MiniRPG.Module.Render;

internal sealed class WeatherScreenFxTuningProfile
{
	public float OverlayAlphaScale { get; set; } = 1f;
	public float FogAlphaScale { get; set; } = 1f;
	public float EdgeTintScale { get; set; } = 1f;
	public float EdgeShadowScale { get; set; } = 1f;
	public float DensityScale { get; set; } = 1f;
	public float SpeedScale { get; set; } = 1f;
	public float ParticleSizeScale { get; set; } = 1f;
	public float ParticleFrequencyScale { get; set; } = 1f;
	public float ParticleBlendScale { get; set; } = 1f;
	public float LightningFlashScale { get; set; } = 1f;
	public float TintStrengthScale { get; set; } = 1f;

	public void Reset()
	{
		OverlayAlphaScale = 1f;
		FogAlphaScale = 1f;
		EdgeTintScale = 1f;
		EdgeShadowScale = 1f;
		DensityScale = 1f;
		SpeedScale = 1f;
		ParticleSizeScale = 1f;
		ParticleFrequencyScale = 1f;
		ParticleBlendScale = 1f;
		LightningFlashScale = 1f;
		TintStrengthScale = 1f;
	}

	public WeatherScreenFxTuningProfile Clone() => new()
	{
		OverlayAlphaScale = OverlayAlphaScale,
		FogAlphaScale = FogAlphaScale,
		EdgeTintScale = EdgeTintScale,
		EdgeShadowScale = EdgeShadowScale,
		DensityScale = DensityScale,
		SpeedScale = SpeedScale,
		ParticleSizeScale = ParticleSizeScale,
		ParticleFrequencyScale = ParticleFrequencyScale,
		ParticleBlendScale = ParticleBlendScale,
		LightningFlashScale = LightningFlashScale,
		TintStrengthScale = TintStrengthScale,
	};

	public float GetValue(string id) => id switch
	{
		"overlay_alpha_scale" => OverlayAlphaScale,
		"fog_alpha_scale" => FogAlphaScale,
		"edge_tint_scale" => EdgeTintScale,
		"edge_shadow_scale" => EdgeShadowScale,
		"density_scale" => DensityScale,
		"speed_scale" => SpeedScale,
		"particle_size_scale" => ParticleSizeScale,
		"particle_frequency_scale" => ParticleFrequencyScale,
		"particle_blend_scale" => ParticleBlendScale,
		"lightning_flash_scale" => LightningFlashScale,
		"tint_strength_scale" => TintStrengthScale,
		_ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown weather tuning property."),
	};

	public void SetValue(string id, float value)
	{
		var clamped = ClampScale(value);
		switch (id)
		{
			case "overlay_alpha_scale":
				OverlayAlphaScale = clamped;
				break;
			case "fog_alpha_scale":
				FogAlphaScale = clamped;
				break;
			case "edge_tint_scale":
				EdgeTintScale = clamped;
				break;
			case "edge_shadow_scale":
				EdgeShadowScale = clamped;
				break;
			case "density_scale":
				DensityScale = clamped;
				break;
			case "speed_scale":
				SpeedScale = clamped;
				break;
			case "particle_size_scale":
				ParticleSizeScale = clamped;
				break;
			case "particle_frequency_scale":
				ParticleFrequencyScale = clamped;
				break;
			case "particle_blend_scale":
				ParticleBlendScale = clamped;
				break;
			case "lightning_flash_scale":
				LightningFlashScale = clamped;
				break;
			case "tint_strength_scale":
				TintStrengthScale = clamped;
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown weather tuning property.");
		}
	}

	public Dictionary<string, float> ToDictionary() => new(StringComparer.Ordinal)
	{
		["overlay_alpha_scale"] = OverlayAlphaScale,
		["fog_alpha_scale"] = FogAlphaScale,
		["edge_tint_scale"] = EdgeTintScale,
		["edge_shadow_scale"] = EdgeShadowScale,
		["density_scale"] = DensityScale,
		["speed_scale"] = SpeedScale,
		["particle_size_scale"] = ParticleSizeScale,
		["particle_frequency_scale"] = ParticleFrequencyScale,
		["particle_blend_scale"] = ParticleBlendScale,
		["lightning_flash_scale"] = LightningFlashScale,
		["tint_strength_scale"] = TintStrengthScale,
	};

	public static WeatherScreenFxTuningProfile Lerp(
		WeatherScreenFxTuningProfile from,
		WeatherScreenFxTuningProfile to,
		float t) => new()
	{
		OverlayAlphaScale = LerpFloat(from.OverlayAlphaScale, to.OverlayAlphaScale, t),
		FogAlphaScale = LerpFloat(from.FogAlphaScale, to.FogAlphaScale, t),
		EdgeTintScale = LerpFloat(from.EdgeTintScale, to.EdgeTintScale, t),
		EdgeShadowScale = LerpFloat(from.EdgeShadowScale, to.EdgeShadowScale, t),
		DensityScale = LerpFloat(from.DensityScale, to.DensityScale, t),
		SpeedScale = LerpFloat(from.SpeedScale, to.SpeedScale, t),
		ParticleSizeScale = LerpFloat(from.ParticleSizeScale, to.ParticleSizeScale, t),
		ParticleFrequencyScale = LerpFloat(from.ParticleFrequencyScale, to.ParticleFrequencyScale, t),
		ParticleBlendScale = LerpFloat(from.ParticleBlendScale, to.ParticleBlendScale, t),
		LightningFlashScale = LerpFloat(from.LightningFlashScale, to.LightningFlashScale, t),
		TintStrengthScale = LerpFloat(from.TintStrengthScale, to.TintStrengthScale, t),
	};

	private static float LerpFloat(float from, float to, float t) =>
		from + ((to - from) * Math.Clamp(t, 0f, 1f));

	internal static float ClampScale(float value)
	{
		if (float.IsNaN(value) || float.IsInfinity(value))
			return 1f;
		return Math.Clamp(value, 0f, 2f);
	}
}

internal sealed class WeatherScreenFxTuningSet
{
	private readonly Dictionary<WeatherScreenFxMode, WeatherScreenFxTuningProfile> _profiles = new();

	public static IReadOnlyList<WeatherScreenFxMode> EditableModes { get; } =
	[
		WeatherScreenFxMode.Rain,
		WeatherScreenFxMode.Snow,
		WeatherScreenFxMode.Fog,
		WeatherScreenFxMode.Dust,
		WeatherScreenFxMode.Thunderstorm,
	];

	public WeatherScreenFxTuningSet()
	{
		foreach (var mode in EditableModes)
			_profiles[mode] = new WeatherScreenFxTuningProfile();
	}

	public WeatherScreenFxTuningProfile GetProfile(WeatherScreenFxMode mode)
	{
		if (!_profiles.TryGetValue(mode, out var profile))
		{
			profile = new WeatherScreenFxTuningProfile();
			_profiles[mode] = profile;
		}

		return profile;
	}

	public WeatherScreenFxTuningProfile ResolveBlendedProfile(
		WeatherScreenFxMode primary,
		WeatherScreenFxMode secondary,
		float blend)
	{
		var from = primary == WeatherScreenFxMode.None
			? new WeatherScreenFxTuningProfile()
			: GetProfile(primary);
		var to = secondary == WeatherScreenFxMode.None
			? from
			: GetProfile(secondary);
		return WeatherScreenFxTuningProfile.Lerp(from, to, blend);
	}

	public void ResetMode(WeatherScreenFxMode mode) =>
		GetProfile(mode).Reset();

	public void ResetAll()
	{
		foreach (var profile in _profiles.Values)
			profile.Reset();
	}

	public string ToJson()
	{
		var export = EditableModes.ToDictionary(
			ToId,
			mode => GetProfile(mode).ToDictionary(),
			StringComparer.Ordinal);
		return JsonSerializer.Serialize(export, new JsonSerializerOptions
		{
			WriteIndented = true,
		});
	}

	public static string ToId(WeatherScreenFxMode mode) => mode switch
	{
		WeatherScreenFxMode.Rain => "rain",
		WeatherScreenFxMode.Snow => "snow",
		WeatherScreenFxMode.Fog => "fog",
		WeatherScreenFxMode.Dust => "dust",
		WeatherScreenFxMode.Thunderstorm => "thunderstorm",
		_ => "none",
	};

	public static bool TryParseMode(string? id, out WeatherScreenFxMode mode)
	{
		mode = id?.Trim().ToLowerInvariant() switch
		{
			"rain" => WeatherScreenFxMode.Rain,
			"snow" => WeatherScreenFxMode.Snow,
			"fog" => WeatherScreenFxMode.Fog,
			"dust" => WeatherScreenFxMode.Dust,
			"sandstorm" => WeatherScreenFxMode.Dust,
			"thunderstorm" => WeatherScreenFxMode.Thunderstorm,
			_ => WeatherScreenFxMode.None,
		};
		return mode != WeatherScreenFxMode.None;
	}
}
