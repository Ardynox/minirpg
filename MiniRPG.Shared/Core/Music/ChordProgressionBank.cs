using System;
using System.Collections.Generic;

namespace MiniRPG.Core.Music;

public static class ChordProgressionBank
{
	public static int[][] Select(MusicParams p, int barCount)
	{
		var progressions = SelectPool(p);
		var idx = HashSelect(p, progressions.Length);
		var template = progressions[idx];

		var result = new int[barCount][];
		for (var i = 0; i < barCount; i++)
			result[i] = [template[i % template.Length]];
		return result;
	}

	private static int[][] SelectPool(MusicParams p)
	{
		if (p.MentalBreakPulse > 0.5f)
			return CrisisProgressions;

		if (p.CombatPulse > 0.5f || p.Arousal > 0.7f)
			return CombatProgressions;

		if (p.Stress > 0.6f)
			return TenseProgressions;

		if (p.Valence > 0.65f)
			return HopefulProgressions;

		if (p.Valence < 0.3f)
			return MelancholyProgressions;

		return PeacefulProgressions;
	}

	private static int HashSelect(MusicParams p, int count)
	{
		var hash = (int)(p.Valence * 1000) ^ (int)(p.Arousal * 777) ^ (int)(p.Stress * 333);
		return ((hash % count) + count) % count;
	}

	private static readonly int[][] PeacefulProgressions =
	[
		[0, 6, 5, 6],
		[0, 3, 4, 0],
		[0, 2, 5, 0],
		[0, 4, 5, 3],
	];

	private static readonly int[][] HopefulProgressions =
	[
		[0, 4, 5, 3],
		[0, 3, 6, 4],
		[0, 5, 3, 4],
		[0, 2, 4, 0],
	];

	private static readonly int[][] MelancholyProgressions =
	[
		[0, 5, 3, 4],
		[0, 3, 5, 6],
		[0, 6, 5, 4],
		[0, 4, 6, 5],
	];

	private static readonly int[][] TenseProgressions =
	[
		[0, 1, 4, 0],
		[0, 5, 1, 4],
		[0, 3, 6, 4],
		[0, 6, 1, 0],
	];

	private static readonly int[][] CombatProgressions =
	[
		[0, 3, 4, 0],
		[0, 5, 4, 0],
		[0, 3, 6, 4],
		[0, 4, 3, 4],
	];

	private static readonly int[][] CrisisProgressions =
	[
		[0, 1, 2, 3],
		[0, 3, 1, 4],
		[0, 2, 4, 1],
		[0, 5, 2, 6],
	];
}
