using System;
using System.Collections.Generic;

namespace MiniRPG.Core.Music;

public enum ScaleType
{
	Ionian,
	Dorian,
	Phrygian,
	Lydian,
	Mixolydian,
	Aeolian,
	Locrian,
	HarmonicMinor,
	WholeTone,
	Pentatonic,
	MinorPentatonic,
}

public static class MusicTheory
{
	private static readonly Dictionary<ScaleType, int[]> ScaleIntervals = new()
	{
		[ScaleType.Ionian] = [0, 2, 4, 5, 7, 9, 11],
		[ScaleType.Dorian] = [0, 2, 3, 5, 7, 9, 10],
		[ScaleType.Phrygian] = [0, 1, 3, 5, 7, 8, 10],
		[ScaleType.Lydian] = [0, 2, 4, 6, 7, 9, 11],
		[ScaleType.Mixolydian] = [0, 2, 4, 5, 7, 9, 10],
		[ScaleType.Aeolian] = [0, 2, 3, 5, 7, 8, 10],
		[ScaleType.Locrian] = [0, 1, 3, 5, 6, 8, 10],
		[ScaleType.HarmonicMinor] = [0, 2, 3, 5, 7, 8, 11],
		[ScaleType.WholeTone] = [0, 2, 4, 6, 8, 10],
		[ScaleType.Pentatonic] = [0, 2, 4, 7, 9],
		[ScaleType.MinorPentatonic] = [0, 3, 5, 7, 10],
	};

	public static int[] GetScaleNotes(ScaleType scale) =>
		ScaleIntervals.TryGetValue(scale, out var intervals) ? intervals : ScaleIntervals[ScaleType.Aeolian];

	public static int ScaleDegreeToMidi(ScaleType scale, int root, int degree)
	{
		var intervals = GetScaleNotes(scale);
		var len = intervals.Length;
		var octaveOffset = degree >= 0 ? degree / len : (degree - len + 1) / len;
		var idx = ((degree % len) + len) % len;
		return root + octaveOffset * 12 + intervals[idx];
	}

	public static int NearestScaleTone(ScaleType scale, int root, int midiNote)
	{
		var intervals = GetScaleNotes(scale);
		var pc = ((midiNote - root) % 12 + 12) % 12;
		var octave = (midiNote - root) / 12;
		if (midiNote < root) octave--;

		var bestDist = int.MaxValue;
		var bestInterval = 0;
		foreach (var interval in intervals)
		{
			var dist = Math.Abs(pc - interval);
			if (dist > 6) dist = 12 - dist;
			if (dist < bestDist)
			{
				bestDist = dist;
				bestInterval = interval;
			}
		}
		return root + octave * 12 + bestInterval;
	}

	public static int[] BuildChord(ScaleType scale, int root, int degree, int noteCount = 3)
	{
		var intervals = GetScaleNotes(scale);
		var len = intervals.Length;
		var chord = new int[noteCount];
		for (var i = 0; i < noteCount; i++)
		{
			var d = degree + i * 2;
			chord[i] = ScaleDegreeToMidi(scale, root, d);
		}
		return chord;
	}

	public static int[] BuildChordInversion(ScaleType scale, int root, int degree, int inversion, int noteCount = 3)
	{
		var chord = BuildChord(scale, root, degree, noteCount);
		for (var i = 0; i < inversion && i < noteCount; i++)
			chord[i] += 12;
		Array.Sort(chord);
		return chord;
	}

	public static ScaleType SelectScale(MusicParams p)
	{
		if (p.MentalBreakPulse > 0.5f)
			return ScaleType.WholeTone;

		if (p.Stress > 0.7f)
			return ScaleType.HarmonicMinor;

		if (p.Valence > 0.75f)
		{
			if (p.Arousal > 0.5f) return ScaleType.Mixolydian;
			return ScaleType.Ionian;
		}

		if (p.Valence > 0.55f)
		{
			if (p.Arousal > 0.5f) return ScaleType.Mixolydian;
			return ScaleType.Lydian;
		}

		if (p.Valence > 0.35f)
			return ScaleType.Dorian;

		if (p.Valence > 0.2f)
			return ScaleType.Aeolian;

		return ScaleType.Phrygian;
	}

	public static int SelectRootNote(MusicParams p)
	{
		var baseRoot = p.IsNight ? 48 : 55;
		if (p.Valence < 0.3f) baseRoot -= 5;
		if (p.Arousal > 0.6f) baseRoot += 2;
		return Math.Clamp(baseRoot, 36, 65);
	}

	public static int SelectBpm(MusicParams p)
	{
		var baseBpm = Lerp(60f, 140f, p.Arousal);
		baseBpm += (p.Patience - 0.5f) * -20f;
		baseBpm += p.CombatPulse * 20f;
		if (p.MentalBreakPulse > 0.5f) baseBpm += 15f;
		if (p.IsNight) baseBpm -= 10f;
		return Math.Clamp((int)baseBpm, 50, 160);
	}

	public static int SelectBeatsPerBar(MusicParams p)
	{
		if (p.Arousal > 0.7f && p.Stress > 0.5f) return 7;
		if (p.Arousal < 0.3f && p.Valence > 0.5f) return 3;
		return 4;
	}

	private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
