using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Audio;

public sealed partial class SamplerEngine : Node
{
	private const int PoolSize = 16;
	private const string SamplesBasePath = "res://Assets/Audio/Samples/";

	private readonly Dictionary<string, AudioStream?[]> _sampleCache = new(StringComparer.Ordinal);
	private readonly AudioStreamPlayer[] _pool = new AudioStreamPlayer[PoolSize];
	private int _nextPoolIndex;

	public float MasterVolume { get; set; } = 1f;

	private static readonly string[] InstrumentIds =
		["lute", "harp", "recorder", "viol", "percussion"];

	private static readonly Dictionary<string, (int MinNote, int MaxNote)> InstrumentRanges = new()
	{
		["lute"] = (48, 79),
		["harp"] = (48, 84),
		["recorder"] = (60, 84),
		["viol"] = (36, 60),
		["percussion"] = (36, 48),
	};

	public override void _Ready()
	{
		for (var i = 0; i < PoolSize; i++)
		{
			_pool[i] = new AudioStreamPlayer();
			_pool[i].Bus = "Music";
			AddChild(_pool[i]);
		}

		PreloadSamples();
	}

	public void PlayNote(string instrumentId, int midiNote, float velocity, float durationSec)
	{
		var stream = GetSample(instrumentId, midiNote);
		if (stream == null)
			return;

		var player = AcquirePlayer();
		player.Stream = stream;
		player.VolumeDb = VelocityToDb(velocity * MasterVolume);
		player.PitchScale = ComputePitchScale(instrumentId, midiNote);
		player.Play();
	}

	public void StopAll()
	{
		foreach (var player in _pool)
			player.Stop();
	}

	private AudioStreamPlayer AcquirePlayer()
	{
		for (var i = 0; i < PoolSize; i++)
		{
			var idx = (_nextPoolIndex + i) % PoolSize;
			if (!_pool[idx].Playing)
			{
				_nextPoolIndex = (idx + 1) % PoolSize;
				return _pool[idx];
			}
		}

		var stolen = _pool[_nextPoolIndex];
		stolen.Stop();
		_nextPoolIndex = (_nextPoolIndex + 1) % PoolSize;
		return stolen;
	}

	private AudioStream? GetSample(string instrumentId, int midiNote)
	{
		if (!_sampleCache.TryGetValue(instrumentId, out var notes))
			return null;

		var snapped = SnapToSampleNote(instrumentId, midiNote);
		var idx = snapped - 36;
		if (idx >= 0 && idx < notes.Length && notes[idx] != null)
			return notes[idx];

		return null;
	}

	private static float ComputePitchScale(string instrumentId, int midiNote)
	{
		var snapped = SnapToSampleNote(instrumentId, midiNote);
		var semitoneDiff = midiNote - snapped;
		return MathF.Pow(2f, semitoneDiff / 12f);
	}

	private static int SnapToSampleNote(string instrumentId, int midiNote)
	{
		if (!InstrumentRanges.TryGetValue(instrumentId, out var range))
			return midiNote;

		var clamped = Math.Clamp(midiNote, range.MinNote, range.MaxNote);
		return (clamped / 6) * 6;
	}

	private void PreloadSamples()
	{
		foreach (var instId in InstrumentIds)
		{
			if (!InstrumentRanges.TryGetValue(instId, out var range))
				continue;

			var noteCount = range.MaxNote - 36 + 1;
			var notes = new AudioStream?[noteCount];

			for (var midi = range.MinNote; midi <= range.MaxNote; midi += 6)
			{
				var path = $"{SamplesBasePath}{instId}/{instId}_{midi}.wav";
				if (ResourceLoader.Exists(path))
				{
					var idx = midi - 36;
					if (idx >= 0 && idx < notes.Length)
						notes[idx] = ResourceLoader.Load<AudioStream>(path);
				}
			}

			_sampleCache[instId] = notes;
		}
	}

	private static float VelocityToDb(float velocity)
	{
		velocity = Math.Clamp(velocity, 0.01f, 1f);
		return 20f * MathF.Log10(velocity);
	}
}
