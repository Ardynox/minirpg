using System;
using Godot;

namespace MiniRPG.Module.Audio;

public sealed partial class SynthEngine : Node
{
	private const int SampleRate = 44100;
	private const int BufferLengthMs = 100;

	private AudioStreamGeneratorPlayback? _playback;
	private AudioStreamPlayer? _player;
	private double _phase;
	private SynthPatch _patch = SynthPatch.WarmPad;
	private float _targetFrequency = 220f;
	private float _currentFrequency = 220f;
	private float _targetVolume;
	private float _currentVolume;
	private float _lfoPhase;
	private float _lpState;

	public float MasterVolume { get; set; } = 1f;

	public override void _Ready()
	{
		_player = new AudioStreamPlayer();
		AddChild(_player);

		var stream = new AudioStreamGenerator
		{
			MixRate = SampleRate,
			BufferLength = BufferLengthMs / 1000f,
		};
		_player.Stream = stream;
		_player.Bus = "Music";
		_player.Play();

		_playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
	}

	public void SetPatch(SynthPatch patch) => _patch = patch;

	public void SetNote(float frequency, float volume)
	{
		_targetFrequency = frequency;
		_targetVolume = Math.Clamp(volume, 0f, 1f);
	}

	public void FadeOut(float duration = 1f)
	{
		_targetVolume = 0f;
	}

	public override void _Process(double delta)
	{
		if (_playback == null) return;

		var framesAvailable = _playback.GetFramesAvailable();
		if (framesAvailable <= 0) return;

		_currentFrequency = Lerp(_currentFrequency, _targetFrequency, 0.002f);
		var volumeSmooth = Math.Min(0.01f, (float)delta * 2f);
		_currentVolume = Lerp(_currentVolume, _targetVolume * MasterVolume, volumeSmooth);

		var increment = _currentFrequency / SampleRate;
		var lfoRate = _patch.LfoRate / SampleRate;

		for (var i = 0; i < framesAvailable; i++)
		{
			_lfoPhase += lfoRate;
			if (_lfoPhase >= 1.0f) _lfoPhase -= 1.0f;
			var lfo = MathF.Sin(_lfoPhase * MathF.PI * 2f) * _patch.LfoDepth;

			var sample = GenerateSample(_phase, _patch, lfo, ref _lpState);
			sample *= _currentVolume;

			_playback.PushFrame(new Vector2(sample, sample));

			_phase += increment + increment * lfo * 0.01;
			if (_phase >= 1.0) _phase -= 1.0;
		}
	}

	private static float GenerateSample(double phase, SynthPatch patch, float lfo, ref float lpState)
	{
		var p = (float)(phase * Math.PI * 2);
		var sample = 0f;

		sample += MathF.Sin(p) * patch.SineLevel;
		sample += (MathF.Sin(p) > 0 ? 1f : -1f) * patch.SquareLevel * 0.5f;
		sample += ((float)(phase * 2 % 2) - 1f) * patch.SawLevel * 0.4f;
		sample += MathF.Sin(p * 2) * patch.Overtone2;
		sample += MathF.Sin(p * 3) * patch.Overtone3;
		sample += MathF.Sin(p * 5) * patch.Overtone5 * 0.5f;

		if (patch.FilterCutoff < 1f)
		{
			var cutoff = Math.Clamp(patch.FilterCutoff + lfo * 0.1f, 0.01f, 1f);
			lpState += cutoff * (sample - lpState);
			sample = lpState;
		}

		return Math.Clamp(sample, -1f, 1f);
	}

	private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}

public readonly record struct SynthPatch(
	float SineLevel,
	float SquareLevel,
	float SawLevel,
	float Overtone2,
	float Overtone3,
	float Overtone5,
	float FilterCutoff,
	float LfoRate,
	float LfoDepth)
{
	public static readonly SynthPatch WarmPad = new(
		SineLevel: 0.6f, SquareLevel: 0f, SawLevel: 0.15f,
		Overtone2: 0.2f, Overtone3: 0.08f, Overtone5: 0f,
		FilterCutoff: 0.3f, LfoRate: 0.8f, LfoDepth: 0.15f);

	public static readonly SynthPatch BrightPad = new(
		SineLevel: 0.4f, SquareLevel: 0.1f, SawLevel: 0.2f,
		Overtone2: 0.25f, Overtone3: 0.15f, Overtone5: 0.05f,
		FilterCutoff: 0.6f, LfoRate: 1.2f, LfoDepth: 0.1f);

	public static readonly SynthPatch DarkPad = new(
		SineLevel: 0.7f, SquareLevel: 0f, SawLevel: 0.1f,
		Overtone2: 0.15f, Overtone3: 0.05f, Overtone5: 0.02f,
		FilterCutoff: 0.15f, LfoRate: 0.4f, LfoDepth: 0.25f);

	public static readonly SynthPatch Drone = new(
		SineLevel: 0.8f, SquareLevel: 0f, SawLevel: 0f,
		Overtone2: 0.1f, Overtone3: 0.05f, Overtone5: 0.02f,
		FilterCutoff: 0.2f, LfoRate: 0.3f, LfoDepth: 0.3f);

	public static SynthPatch FromInstrumentId(string id) => id switch
	{
		"pad_warm" => WarmPad,
		"pad_bright" => BrightPad,
		"pad_dark" => DarkPad,
		"drone" => Drone,
		_ => WarmPad,
	};
}
