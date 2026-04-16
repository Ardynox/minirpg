using System;

namespace MiniRPG.Core.Music.Voices;

public static class RhythmVoice
{
	private const int KickPitch = 36;
	private const int SnarePitch = 38;
	private const int HiHatPitch = 42;
	private const int TimpaniPitch = 41;
	private const int TaborPitch = 39;

	public static VoiceTrack? Generate(MusicScore score, MusicParams p, int seed)
	{
		if (p.Arousal < 0.25f && p.CombatPulse < 0.2f)
			return null;

		var track = new VoiceTrack
		{
			InstrumentId = "percussion",
			Volume = ComputeVolume(p),
		};

		var rng = new Random(seed);
		var beatsPerBar = score.BeatsPerBar;
		var totalBeats = score.TotalBeats;

		if (p.CombatPulse > 0.5f || p.Arousal > 0.7f)
			GenerateIntensePattern(track, rng, p, beatsPerBar, totalBeats);
		else if (p.Arousal > 0.4f)
			GenerateMediumPattern(track, rng, p, beatsPerBar, totalBeats);
		else
			GenerateLightPattern(track, rng, p, beatsPerBar, totalBeats);

		return track;
	}

	private static float ComputeVolume(MusicParams p)
	{
		var vol = 0.3f + p.Arousal * 0.4f;
		vol += p.Aggression * 0.1f;
		return Math.Clamp(vol, 0.2f, 0.8f);
	}

	private static void GenerateLightPattern(VoiceTrack track, Random rng, MusicParams p,
		int beatsPerBar, float totalBeats)
	{
		var beat = 0f;
		while (beat < totalBeats)
		{
			var velocity = 0.35f + (float)(rng.NextDouble() * 0.1f);

			track.Notes.Add(new NoteEvent
			{
				BeatPosition = beat,
				Pitch = TaborPitch,
				Duration = 0.2f,
				Velocity = velocity,
			});

			if (rng.NextDouble() < 0.4f)
			{
				track.Notes.Add(new NoteEvent
				{
					BeatPosition = beat + beatsPerBar * 0.5f,
					Pitch = TaborPitch,
					Duration = 0.15f,
					Velocity = velocity * 0.6f,
				});
			}

			beat += beatsPerBar;
		}
	}

	private static void GenerateMediumPattern(VoiceTrack track, Random rng, MusicParams p,
		int beatsPerBar, float totalBeats)
	{
		var beat = 0f;
		while (beat < totalBeats)
		{
			var velocity = 0.5f + (float)(rng.NextDouble() * 0.15f);

			track.Notes.Add(new NoteEvent
			{
				BeatPosition = beat,
				Pitch = KickPitch,
				Duration = 0.3f,
				Velocity = velocity,
			});

			if (beatsPerBar >= 4)
			{
				track.Notes.Add(new NoteEvent
				{
					BeatPosition = beat + 2,
					Pitch = SnarePitch,
					Duration = 0.2f,
					Velocity = velocity * 0.8f,
				});
			}

			for (var i = 0; i < beatsPerBar; i++)
			{
				if (rng.NextDouble() < 0.6f)
				{
					track.Notes.Add(new NoteEvent
					{
						BeatPosition = beat + i,
						Pitch = HiHatPitch,
						Duration = 0.1f,
						Velocity = velocity * 0.4f,
					});
				}
			}

			beat += beatsPerBar;
		}
	}

	private static void GenerateIntensePattern(VoiceTrack track, Random rng, MusicParams p,
		int beatsPerBar, float totalBeats)
	{
		var beat = 0f;
		var useTimpani = p.CombatPulse > 0.6f;

		while (beat < totalBeats)
		{
			var velocity = 0.65f + p.Aggression * 0.2f + (float)(rng.NextDouble() * 0.1f);
			velocity = Math.Min(velocity, 1f);

			track.Notes.Add(new NoteEvent
			{
				BeatPosition = beat,
				Pitch = useTimpani ? TimpaniPitch : KickPitch,
				Duration = 0.3f,
				Velocity = velocity,
			});

			if (beatsPerBar >= 3)
			{
				track.Notes.Add(new NoteEvent
				{
					BeatPosition = beat + (beatsPerBar >= 4 ? 2 : 1.5f),
					Pitch = SnarePitch,
					Duration = 0.2f,
					Velocity = velocity * 0.85f,
				});
			}

			for (var sub = 0f; sub < beatsPerBar; sub += 0.5f)
			{
				if (rng.NextDouble() < 0.7f)
				{
					track.Notes.Add(new NoteEvent
					{
						BeatPosition = beat + sub,
						Pitch = HiHatPitch,
						Duration = 0.08f,
						Velocity = velocity * 0.45f,
					});
				}
			}

			if (useTimpani && rng.NextDouble() < 0.3f)
			{
				var fill = beat + beatsPerBar - 1;
				track.Notes.Add(new NoteEvent
				{
					BeatPosition = fill,
					Pitch = TimpaniPitch,
					Duration = 0.4f,
					Velocity = velocity * 0.9f,
				});
			}

			beat += beatsPerBar;
		}
	}
}
