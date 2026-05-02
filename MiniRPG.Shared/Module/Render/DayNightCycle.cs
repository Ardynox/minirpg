using System;

namespace MiniRPG.Module.Render;

/// <summary>
/// Converts game turn into lighting parameters for the day/night cycle.
/// A full day spans <see cref="TurnsPerDay"/> turns, cycling through
/// Night → Dawn → Day → Dusk → Night phases.
/// Season is derived from turn (<see cref="TurnsPerSeason"/> turns per season, matching FarmModule).
///
/// 度量衡口径（详见 <c>Docs/开发约定.md</c>「度量衡口径」节）：
///   1 turn   = 6 游戏内分钟 → 240 turn = 24 小时 = 1 day
///   1 day    = 240 turn
///   1 season = 200 turn ≈ 20 游戏内小时（**有意比一天短**：让季节在同一天内也可能过渡，
///              不强行对齐——见 <see cref="MiniRPG.Core.Calendar.CalendarService"/> 注释）
/// 任何「每 N turn 发生一次」的速率/时长，都应与该口径一致：
///   速率（PerTurn）= 期望「每天发生量」 ÷ TurnsPerDay
///   时长（DurationTurns / CooldownTurns）= 期望「现实分钟」 ÷ 6
/// </summary>
public static class DayNightCycle
{
	/// <summary>一个游戏日的回合数。1 turn = 6 游戏内分钟 → 240 turn = 24 h。</summary>
	public const int TurnsPerDay = 240;

	/// <summary>
	/// 一个季节的回合数。≈ 20 游戏内小时（200 × 6 min），有意比一天短。
	/// 历史值 100（在 120 turn/day 历法下）；改成 240 turn/day 后等比例 ×2 → 200，
	/// 以保持季节切换的「现实分钟感」不变。
	/// </summary>
	public const int TurnsPerSeason = 200;

	// Phase boundaries (fraction of day)
	private const float NightEnd = 0.20f;
	private const float DawnEnd = 0.30f;
	private const float DayEnd = 0.70f;
	private const float DuskEnd = 0.80f;

	// Night lighting
	private const float NightAmbient = 0.25f;
	private const float NightSunAltitude = 0f;
	private static readonly (float R, float G, float B) NightTint = (0.20f, 0.22f, 0.40f);

	// Day lighting (base values — modulated by season)
	private const float DayAmbient = 1.0f;
	private static readonly (float R, float G, float B) DayTint = (1.0f, 1.0f, 0.98f);

	// Dawn/Dusk warm tint (peak color at transition midpoints)
	private static readonly (float R, float G, float B) WarmTint = (1.0f, 0.82f, 0.58f);

	// ── Season parameters ──
	//                              peakAlt  arcStart       arcEnd        dawnAlt
	private static readonly SeasonParams Spring = new(0.85f, MathF.PI * 0.78f, MathF.PI * 0.22f, 0.50f);
	private static readonly SeasonParams Summer = new(1.00f, MathF.PI * 0.80f, MathF.PI * 0.20f, 0.60f);
	private static readonly SeasonParams Autumn = new(0.85f, MathF.PI * 0.78f, MathF.PI * 0.22f, 0.50f);
	private static readonly SeasonParams Winter = new(0.55f, MathF.PI * 0.70f, MathF.PI * 0.30f, 0.35f);

	private static readonly SeasonParams[] Seasons = [Spring, Summer, Autumn, Winter];

	public static DayNightSnapshot Compute(int turn)
	{
		var t = (turn % TurnsPerDay) / (float)TurnsPerDay;
		var sp = GetInterpolatedSeason(turn);

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
			sunAltitude = Lerp(0f, sp.DawnAltitude, smooth);

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

			sunAngle = Lerp(MathF.PI, sp.ArcStart, smooth); // rising from east
		}
		else if (t < DayEnd)
		{
			// Full day
			ambient = DayAmbient;
			sunAltitude = sp.PeakAltitude;
			(r, g, b) = DayTint;

			// Sun traverses from arc start to arc end during the day
			var dayProgress = (t - DawnEnd) / (DayEnd - DawnEnd);
			sunAngle = Lerp(sp.ArcStart, sp.ArcEnd, dayProgress);
		}
		else if (t < DuskEnd)
		{
			// Dusk: day → night
			var p = (t - DayEnd) / (DuskEnd - DayEnd);
			var smooth = SmoothStep(p);
			ambient = Lerp(DayAmbient, NightAmbient, smooth);
			sunAltitude = Lerp(sp.DawnAltitude, 0f, smooth);

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

			sunAngle = Lerp(sp.ArcEnd, 0f, smooth); // setting in west
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

	/// <summary>
	/// Returns season parameters smoothly interpolated between adjacent seasons
	/// to avoid abrupt transitions at season boundaries.
	/// </summary>
	private static SeasonParams GetInterpolatedSeason(int turn)
	{
		var seasonIndex = (turn / TurnsPerSeason) % 4;
		var progress = (turn % TurnsPerSeason) / (float)TurnsPerSeason;

		var current = Seasons[seasonIndex];
		var next = Seasons[(seasonIndex + 1) % 4];

		// Blend in the last 30% of each season towards the next
		if (progress < 0.7f)
			return current;

		var blendT = (progress - 0.7f) / 0.3f;
		var smooth = SmoothStep(blendT);
		return new SeasonParams(
			Lerp(current.PeakAltitude, next.PeakAltitude, smooth),
			Lerp(current.ArcStart, next.ArcStart, smooth),
			Lerp(current.ArcEnd, next.ArcEnd, smooth),
			Lerp(current.DawnAltitude, next.DawnAltitude, smooth));
	}

	private static float Lerp(float a, float b, float t) => a + (b - a) * t;

	private static float SmoothStep(float t)
	{
		t = Math.Clamp(t, 0f, 1f);
		return t * t * (3f - 2f * t);
	}

	private readonly record struct SeasonParams(
		float PeakAltitude,
		float ArcStart,
		float ArcEnd,
		float DawnAltitude);
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
