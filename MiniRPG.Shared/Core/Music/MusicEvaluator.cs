using System;
using System.Collections.Generic;
using MiniRPG.Core.Data;
using MiniRPG.Core.Needs;
using MiniRPG.Core.Weather;

namespace MiniRPG.Core.Music;

public static class MusicEvaluator
{
	private const float PulseDecayRate = 0.7f;
	private const int PulseWindowTurns = 5;

	public static MusicParams Evaluate(GameState state, Actor? player)
	{
		var p = new MusicParams();
		if (player == null)
			return p;

		EvaluateValence(player, p);
		EvaluateArousal(state, player, p);
		EvaluateStress(player, p);
		EvaluatePersonality(player, p);
		EvaluateEnvironment(state, player, p);
		EvaluateTimeOfDay(state, p);

		return p;
	}

	public static void AccumulateEventPulses(MusicParams p, IReadOnlyList<GameEvent> events, string? playerId)
	{
		foreach (var e in events)
		{
			switch (e.Type)
			{
				case "combat_attack":
				case "combat_block":
					p.CombatPulse = Math.Min(1f, p.CombatPulse + 0.3f);
					break;
				case "actor_killed":
					if (string.Equals(e.TargetId, playerId, StringComparison.Ordinal)
						|| string.Equals(e.InitiatorId, playerId, StringComparison.Ordinal))
						p.DeathPulse = Math.Min(1f, p.DeathPulse + 0.6f);
					else
						p.DeathPulse = Math.Min(1f, p.DeathPulse + 0.2f);
					break;
				case "actor_incapacitated":
					p.DeathPulse = Math.Min(1f, p.DeathPulse + 0.4f);
					break;
				case "social_interaction":
					p.SocialPulse = Math.Min(1f, p.SocialPulse + 0.3f);
					break;
				case "mental_break":
					p.MentalBreakPulse = 1f;
					break;
				case "mental_break_end":
					p.MentalBreakPulse = Math.Max(0f, p.MentalBreakPulse - 0.5f);
					break;
				case "weather_lightning_strike":
					p.CombatPulse = Math.Min(1f, p.CombatPulse + 0.15f);
					break;
			}
		}
	}

	public static void DecayPulses(MusicParams p)
	{
		p.CombatPulse *= PulseDecayRate;
		p.SocialPulse *= PulseDecayRate;
		p.DeathPulse *= PulseDecayRate;
		p.MentalBreakPulse *= PulseDecayRate;

		if (p.CombatPulse < 0.01f) p.CombatPulse = 0f;
		if (p.SocialPulse < 0.01f) p.SocialPulse = 0f;
		if (p.DeathPulse < 0.01f) p.DeathPulse = 0f;
		if (p.MentalBreakPulse < 0.01f) p.MentalBreakPulse = 0f;
	}

	private static void EvaluateValence(Actor player, MusicParams p)
	{
		p.Valence = Math.Clamp(player.MoodValue / 100f, 0f, 1f);
	}

	private static void EvaluateArousal(GameState state, Actor player, MusicParams p)
	{
		var arousal = 0f;

		switch (player.AwarenessState)
		{
			case AwarenessState.Suspicious:
				arousal += 0.2f;
				break;
			case AwarenessState.Alerted:
				arousal += 0.5f;
				break;
			case AwarenessState.Searching:
				arousal += 0.35f;
				break;
		}

		var enemyCount = 0;
		var closestEnemyDist = int.MaxValue;
		foreach (var (_, actor) in state.Actors)
		{
			if (actor.Id == player.Id) continue;
			if (!FactionRelation.IsHostile(player.Faction, actor.Faction)) continue;
			if (actor.Z != player.Z) continue;
			var dist = Math.Abs(actor.X - player.X) + Math.Abs(actor.Y - player.Y);
			if (dist > 15) continue;
			enemyCount++;
			if (dist < closestEnemyDist) closestEnemyDist = dist;
		}

		if (enemyCount > 0)
		{
			arousal += Math.Min(enemyCount / 5f, 0.3f);
			arousal += Math.Max(0f, 1f - closestEnemyDist / 10f) * 0.3f;
		}

		p.Arousal = Math.Clamp(arousal, 0f, 1f);
	}

	private static void EvaluateStress(Actor player, MusicParams p)
	{
		var stress = 0f;

		if (player.Needs.TryGetValue(NeedIds.Hunger, out var hunger))
			stress += (1f - hunger.Current / 100f) * 0.2f;

		if (player.Needs.TryGetValue(NeedIds.Rest, out var rest))
			stress += (1f - rest.Current / 100f) * 0.15f;

		stress += (1f - Math.Clamp(player.MoodValue / 100f, 0f, 1f)) * 0.25f;

		if (player.MentalBreak != null)
			stress += 0.4f;

		stress += player.PainValue / 100f * 0.15f;
		stress += player.BloodLossValue / 100f * 0.1f;

		p.Stress = Math.Clamp(stress, 0f, 1f);
	}

	private static void EvaluatePersonality(Actor player, MusicParams p)
	{
		float Get(string key) =>
			player.DialogPersonality.TryGetValue(key, out var v) ? Math.Clamp(v, 0f, 1f) : 0.5f;

		p.Bravery = Get("bravery");
		p.Altruism = Get("altruism");
		p.Diligence = Get("diligence");
		p.Curiosity = Get("curiosity");
		p.Aggression = Get("aggression");
		p.Sociability = Get("sociability");
		p.Patience = Get("patience");
		p.Greed = Get("greed");
		p.Loyalty = Get("loyalty");
		p.Caution = Get("caution");
	}

	private static void EvaluateEnvironment(GameState state, Actor player, MusicParams p)
	{
		if (state.World == null)
			return;

		var sample = WeatherRules.GetLocalWeather(state, player.X, player.Y, player.Z);
		p.Temperature = sample.TemperatureNormalized;

		p.Weather = sample.Type switch
		{
			WeatherType.Rain => WeatherMusicHint.Rain,
			WeatherType.Snow => WeatherMusicHint.Snow,
			WeatherType.Storm or WeatherType.Thunderstorm => WeatherMusicHint.Storm,
			WeatherType.Fog => WeatherMusicHint.Fog,
			WeatherType.Sandstorm => WeatherMusicHint.Storm,
			_ => WeatherMusicHint.Clear,
		};

		var surface = WeatherSurface.GetSurfaceState(state, player.X, player.Y, player.Z);
		p.IsIndoors = !surface.IsExposed;
	}

	private static void EvaluateTimeOfDay(GameState state, MusicParams p)
	{
		var turnsPerDay = 120;
		var t = (state.Turn % turnsPerDay) / (float)turnsPerDay;
		p.TimeOfDay = t;
		p.IsNight = t < 0.20f || t >= 0.80f;
		p.Season = (state.Turn / 100) % 4;
	}
}
