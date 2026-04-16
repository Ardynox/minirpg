using System;

namespace MiniRPG.Core.Music.Voices;

public static class PadVoice
{
	public static VoiceTrack Generate(MusicScore score, MusicParams p, int seed)
	{
		var track = new VoiceTrack
		{
			InstrumentId = SelectPadType(p),
			Volume = ComputeVolume(p),
		};

		var rng = new Random(seed);
		var beatsPerBar = score.BeatsPerBar;
		var totalBeats = score.TotalBeats;
		var beat = 0f;

		while (beat < totalBeats)
		{
			var barIndex = (int)(beat / beatsPerBar);
			var chord = barIndex < score.Chords.Count ? score.Chords[barIndex] : null;
			var chordDegree = chord?.ScaleDegree ?? 0;

			var noteCount = p.Stress > 0.5f ? 4 : 3;
			var inversion = rng.Next(2);
			var pitches = MusicTheory.BuildChordInversion(score.Scale, score.RootNote, chordDegree, inversion, noteCount);

			var velocity = ComputeChordVelocity(p);
			foreach (var pitch in pitches)
			{
				track.Notes.Add(new NoteEvent
				{
					BeatPosition = beat,
					Pitch = pitch,
					Duration = beatsPerBar * 0.98f,
					Velocity = velocity,
				});
			}

			beat += beatsPerBar;
		}

		return track;
	}

	private static string SelectPadType(MusicParams p)
	{
		if (p.MentalBreakPulse > 0.5f) return "pad_dark";
		if (p.IsNight) return "pad_warm";
		if (p.Weather == WeatherMusicHint.Rain || p.Weather == WeatherMusicHint.Fog) return "pad_warm";
		if (p.Valence > 0.6f) return "pad_bright";
		return "pad_warm";
	}

	private static float ComputeVolume(MusicParams p)
	{
		var vol = 0.35f;
		if (p.Arousal < 0.3f) vol += 0.1f;
		if (p.IsNight) vol += 0.1f;
		if (p.Weather == WeatherMusicHint.Rain) vol += 0.05f;
		return Math.Clamp(vol, 0.2f, 0.6f);
	}

	private static float ComputeChordVelocity(MusicParams p)
	{
		var v = 0.4f;
		if (p.Stress > 0.5f) v += 0.15f;
		if (p.IsNight) v -= 0.05f;
		return Math.Clamp(v, 0.2f, 0.7f);
	}
}
