using System;
using System.Collections.Generic;

namespace MiniRPG.Core.Music;

public sealed class MusicParams
{
	public float Valence { get; set; } = 0.5f;
	public float Arousal { get; set; }
	public float Stress { get; set; }

	public float Bravery { get; set; } = 0.5f;
	public float Altruism { get; set; } = 0.5f;
	public float Diligence { get; set; } = 0.5f;
	public float Curiosity { get; set; } = 0.5f;
	public float Aggression { get; set; } = 0.5f;
	public float Sociability { get; set; } = 0.5f;
	public float Patience { get; set; } = 0.5f;
	public float Greed { get; set; } = 0.5f;
	public float Loyalty { get; set; } = 0.5f;
	public float Caution { get; set; } = 0.5f;

	public float Temperature { get; set; } = 0.5f;
	public WeatherMusicHint Weather { get; set; } = WeatherMusicHint.Clear;
	public bool IsIndoors { get; set; }
	public float TimeOfDay { get; set; } = 0.5f;
	public bool IsNight { get; set; }
	public int Season { get; set; }

	public float CombatPulse { get; set; }
	public float SocialPulse { get; set; }
	public float DeathPulse { get; set; }
	public float MentalBreakPulse { get; set; }

	public MusicParams Lerp(MusicParams target, float t)
	{
		t = Math.Clamp(t, 0f, 1f);
		return new MusicParams
		{
			Valence = L(Valence, target.Valence, t),
			Arousal = L(Arousal, target.Arousal, t),
			Stress = L(Stress, target.Stress, t),
			Bravery = L(Bravery, target.Bravery, t),
			Altruism = L(Altruism, target.Altruism, t),
			Diligence = L(Diligence, target.Diligence, t),
			Curiosity = L(Curiosity, target.Curiosity, t),
			Aggression = L(Aggression, target.Aggression, t),
			Sociability = L(Sociability, target.Sociability, t),
			Patience = L(Patience, target.Patience, t),
			Greed = L(Greed, target.Greed, t),
			Loyalty = L(Loyalty, target.Loyalty, t),
			Caution = L(Caution, target.Caution, t),
			Temperature = L(Temperature, target.Temperature, t),
			Weather = target.Weather,
			IsIndoors = target.IsIndoors,
			TimeOfDay = L(TimeOfDay, target.TimeOfDay, t),
			IsNight = target.IsNight,
			Season = target.Season,
			CombatPulse = L(CombatPulse, target.CombatPulse, t),
			SocialPulse = L(SocialPulse, target.SocialPulse, t),
			DeathPulse = L(DeathPulse, target.DeathPulse, t),
			MentalBreakPulse = L(MentalBreakPulse, target.MentalBreakPulse, t),
		};
	}

	private static float L(float a, float b, float t) => a + (b - a) * t;
}

public enum WeatherMusicHint
{
	Clear,
	Rain,
	Snow,
	Storm,
	Fog,
}
