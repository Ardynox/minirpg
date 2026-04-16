using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Music;

namespace MiniRPG.Module.Audio;

public sealed partial class MusicRenderer : Node
{
	private SynthEngine? _synth;
	private SamplerEngine? _sampler;
	private MusicScore? _currentScore;
	private MusicScore? _nextScore;
	private float _playbackTime;
	private bool _playing;
	private float _crossfadeDuration = 2f;
	private float _crossfadeTime;
	private bool _crossfading;

	private readonly List<ScheduledNote> _scheduledNotes = [];
	private int _scheduleIndex;

	public float MasterVolume
	{
		get => _masterVolume;
		set
		{
			_masterVolume = Math.Clamp(value, 0f, 1f);
			if (_synth != null) _synth.MasterVolume = _masterVolume;
			if (_sampler != null) _sampler.MasterVolume = _masterVolume;
		}
	}
	private float _masterVolume = 1f;

	public override void _Ready()
	{
		_synth = new SynthEngine();
		AddChild(_synth);

		_sampler = new SamplerEngine();
		AddChild(_sampler);
	}

	public void Play(MusicScore score)
	{
		if (_playing && _currentScore != null)
		{
			_nextScore = score;
			_crossfading = true;
			_crossfadeTime = 0f;
		}
		else
		{
			StartScore(score);
		}
	}

	public void Stop()
	{
		_playing = false;
		_currentScore = null;
		_nextScore = null;
		_scheduledNotes.Clear();
		_synth?.FadeOut();
		_sampler?.StopAll();
	}

	public override void _Process(double delta)
	{
		if (!_playing || _currentScore == null)
			return;

		var dt = (float)delta;

		if (_crossfading)
		{
			_crossfadeTime += dt;
			if (_crossfadeTime >= _crossfadeDuration && _nextScore != null)
			{
				StartScore(_nextScore);
				_nextScore = null;
				_crossfading = false;
				return;
			}
		}

		_playbackTime += dt;

		while (_scheduleIndex < _scheduledNotes.Count)
		{
			var note = _scheduledNotes[_scheduleIndex];
			if (note.TimeSeconds > _playbackTime)
				break;

			var fadeMultiplier = 1f;
			if (_crossfading)
				fadeMultiplier = Math.Max(0f, 1f - _crossfadeTime / _crossfadeDuration);

			PlayScheduledNote(note, fadeMultiplier);
			_scheduleIndex++;
		}

		if (_playbackTime >= _currentScore.TotalSeconds)
		{
			if (_nextScore != null)
			{
				StartScore(_nextScore);
				_nextScore = null;
				_crossfading = false;
			}
			else
			{
				_playbackTime -= _currentScore.TotalSeconds;
				_scheduleIndex = 0;
			}
		}
	}

	private void StartScore(MusicScore score)
	{
		_currentScore = score;
		_playbackTime = 0f;
		_scheduleIndex = 0;
		_playing = true;
		_scheduledNotes.Clear();

		var secPerBeat = score.SecondsPerBeat;

		foreach (var voice in score.Voices)
		{
			var isSynth = IsSynthInstrument(voice.InstrumentId);

			foreach (var note in voice.Notes)
			{
				_scheduledNotes.Add(new ScheduledNote
				{
					TimeSeconds = note.BeatPosition * secPerBeat,
					Pitch = note.Pitch,
					DurationSeconds = note.Duration * secPerBeat,
					Velocity = note.Velocity * voice.Volume,
					InstrumentId = voice.InstrumentId,
					IsSynth = isSynth,
				});
			}
		}

		_scheduledNotes.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));

		SetupSynthPad(score);
	}

	private void SetupSynthPad(MusicScore score)
	{
		if (_synth == null) return;

		foreach (var voice in score.Voices)
		{
			if (!IsSynthInstrument(voice.InstrumentId)) continue;

			_synth.SetPatch(SynthPatch.FromInstrumentId(voice.InstrumentId));

			if (voice.Notes.Count > 0)
			{
				var freq = MidiToFrequency(voice.Notes[0].Pitch);
				_synth.SetNote(freq, voice.Volume * voice.Notes[0].Velocity);
			}
			break;
		}
	}

	private void PlayScheduledNote(ScheduledNote note, float fadeMultiplier)
	{
		var velocity = note.Velocity * fadeMultiplier;
		if (velocity < 0.01f) return;

		if (note.IsSynth)
		{
			_synth?.SetNote(MidiToFrequency(note.Pitch), velocity);
		}
		else
		{
			_sampler?.PlayNote(note.InstrumentId, note.Pitch, velocity, note.DurationSeconds);
		}
	}

	private static bool IsSynthInstrument(string id) =>
		id.StartsWith("pad_", StringComparison.Ordinal) || string.Equals(id, "drone", StringComparison.Ordinal);

	private static float MidiToFrequency(int midiNote) =>
		440f * MathF.Pow(2f, (midiNote - 69) / 12f);

	private sealed class ScheduledNote
	{
		public float TimeSeconds;
		public int Pitch;
		public float DurationSeconds;
		public float Velocity;
		public string InstrumentId = "";
		public bool IsSynth;
	}
}
