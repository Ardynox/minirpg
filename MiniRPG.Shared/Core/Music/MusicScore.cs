using System;
using System.Collections.Generic;

namespace MiniRPG.Core.Music;

public sealed class NoteEvent
{
	public float BeatPosition { get; set; }
	public int Pitch { get; set; }
	public float Duration { get; set; }
	public float Velocity { get; set; } = 0.7f;

	public float EndBeat => BeatPosition + Duration;
}

public sealed class VoiceTrack
{
	public string InstrumentId { get; set; } = "";
	public List<NoteEvent> Notes { get; } = [];
	public float Volume { get; set; } = 1f;
	public float Pan { get; set; }
}

public sealed class ChordEvent
{
	public float BeatPosition { get; set; }
	public float Duration { get; set; }
	public int[] Pitches { get; set; } = [];
	public int ScaleDegree { get; set; }
}

public sealed class MusicScore
{
	public int Bpm { get; set; } = 80;
	public int BeatsPerBar { get; set; } = 4;
	public int BarCount { get; set; } = 4;
	public ScaleType Scale { get; set; } = ScaleType.Aeolian;
	public int RootNote { get; set; } = 48;
	public List<ChordEvent> Chords { get; } = [];
	public List<VoiceTrack> Voices { get; } = [];

	public float TotalBeats => BeatsPerBar * BarCount;
	public float SecondsPerBeat => 60f / Bpm;
	public float TotalSeconds => TotalBeats * SecondsPerBeat;
}
