using System;
using System.Collections.Generic;

namespace MiniRPG.Core.Music.Voices;

public static class BassVoice
{
	public static VoiceTrack Generate(MusicScore score, MusicParams p, int seed)
	{
		var track = new VoiceTrack
		{
			InstrumentId = "viol",
			Volume = ComputeVolume(p),
		};

		var rng = new Random(seed);
		var root = score.RootNote;
		var bassRoot = root - 12;
		var beatsPerBar = score.BeatsPerBar;
		var totalBeats = score.TotalBeats;
		var walking = p.Arousal > 0.5f;

		var beat = 0f;
		while (beat < totalBeats)
		{
			var barIndex = (int)(beat / beatsPerBar);
			var chord = barIndex < score.Chords.Count ? score.Chords[barIndex] : null;
			var chordDegree = chord?.ScaleDegree ?? 0;

			if (walking)
				GenerateWalkingBar(track, score, rng, p, bassRoot, chordDegree, beat, beatsPerBar);
			else
				GenerateDroneBar(track, score, rng, p, bassRoot, chordDegree, beat, beatsPerBar);

			beat += beatsPerBar;
		}

		return track;
	}

	private static float ComputeVolume(MusicParams p)
	{
		var vol = 0.5f;
		if (p.Arousal > 0.5f) vol += 0.15f;
		if (p.IsNight) vol -= 0.05f;
		return Math.Clamp(vol, 0.3f, 0.8f);
	}

	private static void GenerateDroneBar(VoiceTrack track, MusicScore score, Random rng,
		MusicParams p, int bassRoot, int chordDegree, float barStart, int beatsPerBar)
	{
		var pitch = MusicTheory.ScaleDegreeToMidi(score.Scale, bassRoot, chordDegree);
		var velocity = 0.5f + p.Arousal * 0.2f;

		track.Notes.Add(new NoteEvent
		{
			BeatPosition = barStart,
			Pitch = pitch,
			Duration = beatsPerBar * 0.95f,
			Velocity = velocity,
		});

		if (rng.NextDouble() < 0.4f)
		{
			var fifthPitch = MusicTheory.ScaleDegreeToMidi(score.Scale, bassRoot, chordDegree + 4);
			track.Notes.Add(new NoteEvent
			{
				BeatPosition = barStart,
				Pitch = fifthPitch,
				Duration = beatsPerBar * 0.95f,
				Velocity = velocity * 0.6f,
			});
		}
	}

	private static void GenerateWalkingBar(VoiceTrack track, MusicScore score, Random rng,
		MusicParams p, int bassRoot, int chordDegree, float barStart, int beatsPerBar)
	{
		var velocity = 0.5f + p.Arousal * 0.25f;
		var prevDegree = chordDegree;

		for (var i = 0; i < beatsPerBar; i++)
		{
			int degree;
			if (i == 0)
				degree = chordDegree;
			else if (i == beatsPerBar - 1 && rng.NextDouble() < 0.5)
				degree = chordDegree + (rng.Next(2) == 0 ? -1 : 1);
			else
				degree = prevDegree + rng.Next(-2, 3);

			var pitch = MusicTheory.ScaleDegreeToMidi(score.Scale, bassRoot, degree);
			pitch = Math.Clamp(pitch, bassRoot - 5, bassRoot + 12);

			track.Notes.Add(new NoteEvent
			{
				BeatPosition = barStart + i,
				Pitch = pitch,
				Duration = 0.9f,
				Velocity = i == 0 ? velocity : velocity * 0.8f,
			});

			prevDegree = degree;
		}
	}
}
