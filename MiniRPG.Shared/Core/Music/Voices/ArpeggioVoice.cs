using System;

namespace MiniRPG.Core.Music.Voices;

public static class ArpeggioVoice
{
	public static VoiceTrack? Generate(MusicScore score, MusicParams p, int seed)
	{
		if (p.Arousal > 0.7f && p.CombatPulse > 0.4f)
			return null;

		var weight = ComputeWeight(p);
		if (weight < 0.1f)
			return null;

		var track = new VoiceTrack
		{
			InstrumentId = SelectInstrument(p),
			Volume = Math.Clamp(weight, 0.2f, 0.65f),
		};

		var rng = new Random(seed);
		var beatsPerBar = score.BeatsPerBar;
		var totalBeats = score.TotalBeats;
		var pattern = SelectPattern(p, rng);

		var beat = 0f;
		while (beat < totalBeats)
		{
			var barIndex = (int)(beat / beatsPerBar);
			var chord = barIndex < score.Chords.Count ? score.Chords[barIndex] : null;
			var chordDegree = chord?.ScaleDegree ?? 0;

			var noteCount = p.Valence > 0.5f ? 4 : 3;
			var chordPitches = MusicTheory.BuildChord(score.Scale, score.RootNote, chordDegree, noteCount);

			GenerateArpeggioBar(track, rng, p, chordPitches, pattern, beat, beatsPerBar);
			beat += beatsPerBar;
		}

		return track;
	}

	private static float ComputeWeight(MusicParams p)
	{
		var w = 0.3f;
		if (p.IsIndoors) w += 0.2f;
		if (p.Arousal < 0.3f) w += 0.15f;
		if (p.Valence > 0.5f) w += 0.1f;
		if (p.IsNight) w += 0.1f;
		w -= p.CombatPulse * 0.3f;
		return w;
	}

	private static string SelectInstrument(MusicParams p)
	{
		if (p.IsIndoors) return "harp";
		if (p.IsNight) return "harp";
		return "lute";
	}

	private static ArpPattern SelectPattern(MusicParams p, Random rng)
	{
		if (p.Valence > 0.6f)
			return rng.Next(2) == 0 ? ArpPattern.Up : ArpPattern.UpDown;
		if (p.Valence < 0.3f)
			return rng.Next(2) == 0 ? ArpPattern.Down : ArpPattern.DownUp;
		return (ArpPattern)rng.Next(4);
	}

	private static void GenerateArpeggioBar(VoiceTrack track, Random rng, MusicParams p,
		int[] chordPitches, ArpPattern pattern, float barStart, int beatsPerBar)
	{
		var subdivisions = p.Arousal > 0.4f ? beatsPerBar * 2 : beatsPerBar;
		var stepDuration = (float)beatsPerBar / subdivisions;
		var sequence = BuildSequence(chordPitches, pattern);
		var velocity = 0.35f + p.Valence * 0.2f;

		for (var i = 0; i < subdivisions; i++)
		{
			if (rng.NextDouble() < 0.15)
				continue;

			var idx = i % sequence.Length;
			var pitch = sequence[idx];

			track.Notes.Add(new NoteEvent
			{
				BeatPosition = barStart + i * stepDuration,
				Pitch = pitch,
				Duration = stepDuration * 0.85f,
				Velocity = Math.Clamp(velocity + (float)(rng.NextDouble() * 0.08 - 0.04), 0.15f, 0.7f),
			});
		}
	}

	private static int[] BuildSequence(int[] pitches, ArpPattern pattern)
	{
		return pattern switch
		{
			ArpPattern.Up => pitches,
			ArpPattern.Down => Reverse(pitches),
			ArpPattern.UpDown => Concat(pitches, Reverse(pitches)),
			ArpPattern.DownUp => Concat(Reverse(pitches), pitches),
			_ => pitches,
		};
	}

	private static int[] Reverse(int[] src)
	{
		var r = new int[src.Length];
		for (var i = 0; i < src.Length; i++)
			r[i] = src[src.Length - 1 - i];
		return r;
	}

	private static int[] Concat(int[] a, int[] b)
	{
		var r = new int[a.Length + b.Length];
		a.CopyTo(r, 0);
		b.CopyTo(r, a.Length);
		return r;
	}

	private enum ArpPattern
	{
		Up,
		Down,
		UpDown,
		DownUp,
	}
}
