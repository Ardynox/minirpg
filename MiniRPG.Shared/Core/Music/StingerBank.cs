using System;
using System.Collections.Generic;
using MiniRPG.Core.Music;

namespace MiniRPG.Core.Music;

public enum StingerType
{
	CombatHit,
	CombatKill,
	PlayerDeath,
	MentalBreak,
	LightningStrike,
	ItemPickup,
	LevelUp,
	SocialPositive,
	SocialNegative,
	Discovery,
}

public sealed class StingerDef
{
	public StingerType Type { get; init; }
	public int[] Pitches { get; init; } = [];
	public float[] Durations { get; init; } = [];
	public float[] Velocities { get; init; } = [];
	public string InstrumentId { get; init; } = "harp";
	public float Volume { get; init; } = 0.8f;
}

public static class StingerBank
{
	private static readonly Dictionary<StingerType, StingerDef[]> Stingers = new()
	{
		[StingerType.CombatHit] =
		[
			new StingerDef
			{
				Type = StingerType.CombatHit,
				Pitches = [60, 55],
				Durations = [0.15f, 0.3f],
				Velocities = [0.9f, 0.6f],
				InstrumentId = "percussion",
				Volume = 0.7f,
			},
		],
		[StingerType.CombatKill] =
		[
			new StingerDef
			{
				Type = StingerType.CombatKill,
				Pitches = [48, 55, 60],
				Durations = [0.2f, 0.2f, 0.5f],
				Velocities = [0.8f, 0.9f, 0.7f],
				InstrumentId = "viol",
				Volume = 0.75f,
			},
		],
		[StingerType.PlayerDeath] =
		[
			new StingerDef
			{
				Type = StingerType.PlayerDeath,
				Pitches = [60, 58, 55, 48],
				Durations = [0.4f, 0.4f, 0.5f, 1.0f],
				Velocities = [0.7f, 0.6f, 0.5f, 0.8f],
				InstrumentId = "viol",
				Volume = 0.9f,
			},
		],
		[StingerType.MentalBreak] =
		[
			new StingerDef
			{
				Type = StingerType.MentalBreak,
				Pitches = [61, 63, 66, 68],
				Durations = [0.2f, 0.2f, 0.2f, 0.6f],
				Velocities = [0.6f, 0.7f, 0.8f, 0.9f],
				InstrumentId = "recorder",
				Volume = 0.8f,
			},
		],
		[StingerType.LightningStrike] =
		[
			new StingerDef
			{
				Type = StingerType.LightningStrike,
				Pitches = [72, 60],
				Durations = [0.1f, 0.5f],
				Velocities = [1.0f, 0.5f],
				InstrumentId = "percussion",
				Volume = 0.85f,
			},
		],
		[StingerType.ItemPickup] =
		[
			new StingerDef
			{
				Type = StingerType.ItemPickup,
				Pitches = [67, 72],
				Durations = [0.12f, 0.25f],
				Velocities = [0.5f, 0.6f],
				InstrumentId = "harp",
				Volume = 0.5f,
			},
		],
		[StingerType.SocialPositive] =
		[
			new StingerDef
			{
				Type = StingerType.SocialPositive,
				Pitches = [60, 64, 67],
				Durations = [0.2f, 0.2f, 0.4f],
				Velocities = [0.5f, 0.55f, 0.6f],
				InstrumentId = "harp",
				Volume = 0.55f,
			},
		],
		[StingerType.SocialNegative] =
		[
			new StingerDef
			{
				Type = StingerType.SocialNegative,
				Pitches = [60, 56, 53],
				Durations = [0.2f, 0.25f, 0.4f],
				Velocities = [0.5f, 0.55f, 0.5f],
				InstrumentId = "viol",
				Volume = 0.55f,
			},
		],
		[StingerType.Discovery] =
		[
			new StingerDef
			{
				Type = StingerType.Discovery,
				Pitches = [60, 64, 67, 72],
				Durations = [0.15f, 0.15f, 0.15f, 0.5f],
				Velocities = [0.4f, 0.5f, 0.6f, 0.7f],
				InstrumentId = "harp",
				Volume = 0.6f,
			},
		],
	};

	public static StingerDef? Get(StingerType type, int variant = 0)
	{
		if (!Stingers.TryGetValue(type, out var defs) || defs.Length == 0)
			return null;
		return defs[Math.Clamp(variant, 0, defs.Length - 1)];
	}

	public static StingerType? MapEventToStinger(string eventType, string? targetId, string? playerId)
	{
		var isPlayer = string.Equals(targetId, playerId, StringComparison.Ordinal);

		return eventType switch
		{
			"combat_attack" => StingerType.CombatHit,
			"combat_block" => StingerType.CombatHit,
			"actor_killed" when isPlayer => StingerType.PlayerDeath,
			"actor_killed" => StingerType.CombatKill,
			"death_blood_loss" when isPlayer => StingerType.PlayerDeath,
			"death_infection" when isPlayer => StingerType.PlayerDeath,
			"actor_incapacitated" when isPlayer => StingerType.PlayerDeath,
			"mental_break" => StingerType.MentalBreak,
			"weather_lightning_strike" => StingerType.LightningStrike,
			"item_picked_up" => StingerType.ItemPickup,
			"social_interaction" => StingerType.SocialPositive,
			_ => null,
		};
	}
}
