using System;
using MiniRPG.Core.Music.Voices;

namespace MiniRPG.Core.Music;

public static class MusicComposer
{
	private static int _seedCounter;

	public static MusicScore Compose(MusicParams p, int? externalSeed = null)
	{
		var baseSeed = externalSeed ?? System.Threading.Interlocked.Increment(ref _seedCounter)
			^ (int)(p.Valence * 10000) ^ (int)(p.Arousal * 7777);

		var score = new MusicScore
		{
			Scale = MusicTheory.SelectScale(p),
			RootNote = MusicTheory.SelectRootNote(p),
			Bpm = MusicTheory.SelectBpm(p),
			BeatsPerBar = MusicTheory.SelectBeatsPerBar(p),
			BarCount = SelectBarCount(p),
		};

		BuildChordProgression(score, p);

		score.Voices.Add(PadVoice.Generate(score, p, baseSeed + 1));
		score.Voices.Add(BassVoice.Generate(score, p, baseSeed + 2));
		score.Voices.Add(MelodyVoice.Generate(score, p, baseSeed + 3));

		var rhythm = RhythmVoice.Generate(score, p, baseSeed + 4);
		if (rhythm != null)
			score.Voices.Add(rhythm);

		var arpeggio = ArpeggioVoice.Generate(score, p, baseSeed + 5);
		if (arpeggio != null)
			score.Voices.Add(arpeggio);

		ApplyPersonalityModulations(score, p);

		return score;
	}

	private static int SelectBarCount(MusicParams p)
	{
		if (p.CombatPulse > 0.5f) return 4;
		if (p.Arousal > 0.6f) return 4;
		if (p.IsNight && p.Arousal < 0.3f) return 8;
		return p.Patience > 0.6f ? 8 : 4;
	}

	private static void BuildChordProgression(MusicScore score, MusicParams p)
	{
		var progression = ChordProgressionBank.Select(p, score.BarCount);
		var beatsPerBar = score.BeatsPerBar;

		for (var bar = 0; bar < score.BarCount; bar++)
		{
			var degrees = progression[bar];
			var degree = degrees[0];
			var pitches = MusicTheory.BuildChord(score.Scale, score.RootNote, degree);

			score.Chords.Add(new ChordEvent
			{
				BeatPosition = bar * beatsPerBar,
				Duration = beatsPerBar,
				Pitches = pitches,
				ScaleDegree = degree,
			});
		}
	}

	private static void ApplyPersonalityModulations(MusicScore score, MusicParams p)
	{
		foreach (var voice in score.Voices)
		{
			if (string.Equals(voice.InstrumentId, "percussion", StringComparison.Ordinal))
			{
				voice.Volume *= 0.8f + p.Aggression * 0.4f;
			}

			if (string.Equals(voice.InstrumentId, "harp", StringComparison.Ordinal)
				|| string.Equals(voice.InstrumentId, "lute", StringComparison.Ordinal))
			{
				if (p.Sociability > 0.6f && p.SocialPulse > 0.2f)
					voice.Volume *= 1.15f;
			}

			voice.Volume = Math.Clamp(voice.Volume, 0f, 1f);
		}

		if (p.Caution > 0.6f && p.Arousal > 0.3f)
		{
			foreach (var voice in score.Voices)
			{
				foreach (var note in voice.Notes)
				{
					if (note.Velocity > 0.6f)
						note.Velocity *= 0.85f;
				}
			}
		}
	}
}
