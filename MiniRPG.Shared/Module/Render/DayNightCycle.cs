using System;

namespace MiniRPG.Module.Render;

/// <summary>
/// Converts game turn into lighting parameters for the day/night cycle.
/// A full day spans <see cref="TurnsPerDay"/> turns, cycling through
/// Night → Dawn → Day → Dusk → Night phases.
/// </summary>
public static class DayNightCycle
{
	public const int TurnsPerDay = 120;

	// Phase boundaries (fraction of day)
	private const float NightEnd = 0.20f;
	private const float DawnEnd = 0.30f;
	private const float DayEnd = 0.70f;
	private const float DuskEnd = 0.80f;

	// Night lighting
	private const float NightAmbient = 0.25f;
	private const float NightSunAltitude = 0f;
	private static readonly (float R, float G, float B) NightTint = (0.20f, 0.22f, 0.40f);

	// Day lighting
	private const float DayAmbient = 1.0f;
	private const float DaySunAltitude = 1.0f;
	private static readonly (float R, float G, float B) DayTint = (1.0f, 1.0f, 0.98f);

	// Dawn/Dusk warm tint (peak color at transition midpoints)
	private static readonly (float R, float G, float B) WarmTint = (1.0f, 0.82f, 0.58f);

	public static DayNightSnapshot Compute(int turn)
	{
		var t = (turn % TurnsPerDay) / (float)TurnsPerDay;

		float ambient;
		float sunAltitude;
		float r, g, b;
		float sunAngle; // radians, 0 = east, π = west

		if (t < NightEnd)
		{
			// Deep night
			ambient = NightAmbient;
			sunAltitude = NightSunAltitude;
			(r, g, b) = NightTint;
			sunAngle = MathF.PI; // below horizon
		}
		else if (t < DawnEnd)
		{
			// Dawn: night → day
			var p = (t - NightEnd) / (DawnEnd - NightEnd);
			var smooth = SmoothStep(p);
			ambient = Lerp(NightAmbient, DayAmbient, smooth);
			sunAltitude = Lerp(0f, 0.6f, smooth);

			// Night → warm → day color
			if (p < 0.5f)
			{
				var q = p * 2f;
				r = Lerp(NightTint.R, WarmTint.R, q);
				g = Lerp(NightTint.G, WarmTint.G, q);
				b = Lerp(NightTint.B, WarmTint.B, q);
			}
			else
			{
				var q = (p - 0.5f) * 2f;
				r = Lerp(WarmTint.R, DayTint.R, q);
				g = Lerp(WarmTint.G, DayTint.G, q);
				b = Lerp(WarmTint.B, DayTint.B, q);
			}

			sunAngle = Lerp(MathF.PI, MathF.PI * 0.75f, smooth); // rising from east
		}
		else if (t < DayEnd)
		{
			// Full day
			ambient = DayAmbient;
			sunAltitude = DaySunAltitude;
			(r, g, b) = DayTint;

			// Sun traverses from east to west during the day
			var dayProgress = (t - DawnEnd) / (DayEnd - DawnEnd);
			sunAngle = Lerp(MathF.PI * 0.75f, MathF.PI * 0.25f, dayProgress);
		}
		else if (t < DuskEnd)
		{
			// Dusk: day → night
			var p = (t - DayEnd) / (DuskEnd - DayEnd);
			var smooth = SmoothStep(p);
			ambient = Lerp(DayAmbient, NightAmbient, smooth);
			sunAltitude = Lerp(0.6f, 0f, smooth);

			// Day → warm → night color
			if (p < 0.5f)
			{
				var q = p * 2f;
				r = Lerp(DayTint.R, WarmTint.R, q);
				g = Lerp(DayTint.G, WarmTint.G, q);
				b = Lerp(DayTint.B, WarmTint.B, q);
			}
			else
			{
				var q = (p - 0.5f) * 2f;
				r = Lerp(WarmTint.R, NightTint.R, q);
				g = Lerp(WarmTint.G, NightTint.G, q);
				b = Lerp(WarmTint.B, NightTint.B, q);
			}

			sunAngle = Lerp(MathF.PI * 0.25f, 0f, smooth); // setting in west
		}
		else
		{
			// Late night
			ambient = NightAmbient;
			sunAltitude = NightSunAltitude;
			(r, g, b) = NightTint;
			sunAngle = 0f; // below horizon
		}

		// Sun direction projects shadows in world XY space.
		// Isometric convention: +X is screen-right-down, +Y is screen-left-down.
		// Sun direction = where the sun IS (shadow falls opposite).
		var sunDirX = MathF.Cos(sunAngle);
		var sunDirY = MathF.Sin(sunAngle);

		return new DayNightSnapshot(
			AmbientMultiplier: ambient,
			SunTintR: r,
			SunTintG: g,
			SunTintB: b,
			SunAltitude: sunAltitude,
			SunDirectionX: sunDirX,
			SunDirectionY: sunDirY);
	}

	private static float Lerp(float a, float b, float t) => a + (b - a) * t;

	private static float SmoothStep(float t)
	{
		t = Math.Clamp(t, 0f, 1f);
		return t * t * (3f - 2f * t);
	}
}

/// <summary>Immutable snapshot of day/night lighting state for the current turn.</summary>
public readonly record struct DayNightSnapshot(
	float AmbientMultiplier,
	float SunTintR,
	float SunTintG,
	float SunTintB,
	float SunAltitude,
	float SunDirectionX,
	float SunDirectionY)
{
	public static readonly DayNightSnapshot FullDay = new(1f, 1f, 1f, 0.98f, 1f, 0.707f, 0.707f);
}
