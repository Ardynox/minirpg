using System;
using System.Collections.Generic;

namespace MiniRPG.Core.Music.Voices;

public static class MelodyVoice
{
	private const int DefaultOctave = 60;

	public static VoiceTrack Generate(MusicScore score, MusicParams p, int seed)
	{
		var track = new VoiceTrack
		{
			InstrumentId = SelectInstrument(p),
			Volume = ComputeVolume(p),
		};

		var rng = new Random(seed);
		var scale = score.Scale;
		var root = score.RootNote;
		var totalBeats = score.TotalBeats;
		var beatsPerBar = score.BeatsPerBar;

		var density = ComputeDensity(p);
		var prevDegree = 0;
		var beat = 0f;

		while (beat < totalBeats)
		{
			var barIndex = (int)(beat / beatsPerBar);
			var chord = barIndex < score.Chords.Count ? score.Chords[barIndex] : null;
			var chordDegree = chord?.ScaleDegree ?? 0;

			var degree = NextDegree(rng, prevDegree, chordDegree, p);
			var pitch = MusicTheory.ScaleDegreeToMidi(scale, root, degree);
			pitch = Math.Clamp(pitch, root - 5, root + 19);

			var dur = NextDuration(rng, density, beatsPerBar, beat);
			var velocity = ComputeNoteVelocity(rng, p, beat, beatsPerBar);

			if (rng.NextDouble() < RestProbability(p))
			{
				beat += dur;
				continue;
			}

			track.Notes.Add(new NoteEvent
			{
				BeatPosition = beat,
				Pitch = pitch,
				Duration = dur * 0.9f,
				Velocity = velocity,
			});

			prevDegree = degree;
			beat += dur;
		}

		return track;
	}

	private static string SelectInstrument(MusicParams p)
	{
		if (p.CombatPulse > 0.5f) return "recorder";
		if (p.IsNight) return "lute";
		if (p.IsIndoors) return "harp";
		return "lute";
	}

	private static float ComputeVolume(MusicParams p)
	{
		var vol = 0.7f;
		if (p.Arousal > 0.6f) vol += 0.15f;
		if (p.IsNight) vol -= 0.1f;
		return Math.Clamp(vol, 0.3f, 1f);
	}

	private static float ComputeDensity(MusicParams p)
	{
		var d = 0.4f + p.Arousal * 0.4f;
		d += p.Curiosity * 0.1f;
		if (p.MentalBreakPulse > 0.5f) d += 0.2f;
		return Math.Clamp(d, 0.2f, 1f);
	}

	private static int NextDegree(Random rng, int prev, int chordDegree, MusicParams p)
	{
		var direction = p.Valence > 0.5f ? 1 : -1;
		var maxStep = p.Curiosity > 0.6f ? 4 : 2;

		if (rng.NextDouble() < 0.3f)
			return chordDegree + (rng.Next(3) * 2);

		var step = rng.Next(1, maxStep + 1) * direction;
		if (rng.NextDouble() < 0.3f) step = -step;

		if (p.Curiosity > 0.7f && rng.NextDouble() < 0.15f)
			step = (rng.Next(2) == 0 ? 1 : -1) * rng.Next(3, 6);

		var next = prev + step;
		return Math.Clamp(next, -7, 14);
	}

	private static float NextDuration(Random rng, float density, int beatsPerBar, float currentBeat)
	{
		float[] options = density > 0.7f
			? [0.25f, 0.5f, 0.5f, 1f]
			: density > 0.4f
				? [0.5f, 1f, 1f, 2f]
				: [1f, 2f, 2f, 4f];

		var dur = options[rng.Next(options.Length)];
		var remaining = beatsPerBar - (currentBeat % beatsPerBar);
		return Math.Min(dur, remaining);
	}

	private static float ComputeNoteVelocity(Random rng, MusicParams p, float beat, int beatsPerBar)
	{
		var base_ = 0.5f + p.Arousal * 0.3f;
		var beatInBar = beat % beatsPerBar;
		if (beatInBar < 0.01f) base_ += 0.15f;

		if (p.Aggression > 0.6f)
			base_ += (float)(rng.NextDouble() * 0.2f);

		return Math.Clamp(base_ + (float)(rng.NextDouble() * 0.1f - 0.05f), 0.2f, 1f);
	}

	private static double RestProbability(MusicParams p)
	{
		var rest = 0.15;
		if (p.Arousal < 0.3) rest += 0.15;
		if (p.Patience > 0.6) rest += 0.1;
		return Math.Clamp(rest, 0.05, 0.5);
	}
}
