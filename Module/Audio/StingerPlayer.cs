using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Music;

namespace MiniRPG.Module.Audio;

public sealed partial class StingerPlayer : Node
{
	private const int PoolSize = 8;
	private readonly AudioStreamPlayer[] _pool = new AudioStreamPlayer[PoolSize];
	private int _nextIndex;
	private SamplerEngine? _sampler;
	private float _cooldownRemaining;
	private const float MinCooldown = 0.1f;

	public float MasterVolume { get; set; } = 1f;

	public override void _Ready()
	{
		for (var i = 0; i < PoolSize; i++)
		{
			_pool[i] = new AudioStreamPlayer();
			_pool[i].Bus = "SFX";
			AddChild(_pool[i]);
		}
	}

	public void SetSampler(SamplerEngine sampler) => _sampler = sampler;

	public void PlayStinger(StingerDef def)
	{
		if (_cooldownRemaining > 0f)
			return;

		if (_sampler == null)
			return;

		var delay = 0f;
		for (var i = 0; i < def.Pitches.Length; i++)
		{
			var pitch = def.Pitches[i];
			var dur = i < def.Durations.Length ? def.Durations[i] : 0.3f;
			var vel = i < def.Velocities.Length ? def.Velocities[i] : 0.7f;

			ScheduleNote(def.InstrumentId, pitch, vel * def.Volume * MasterVolume, dur, delay);
			delay += dur;
		}

		_cooldownRemaining = MinCooldown;
	}

	public void PlayFromEvent(string eventType, string? targetId, string? playerId)
	{
		var stingerType = StingerBank.MapEventToStinger(eventType, targetId, playerId);
		if (stingerType == null) return;

		var def = StingerBank.Get(stingerType.Value);
		if (def == null) return;

		PlayStinger(def);
	}

	public override void _Process(double delta)
	{
		if (_cooldownRemaining > 0f)
			_cooldownRemaining -= (float)delta;
	}

	private void ScheduleNote(string instrumentId, int pitch, float velocity, float duration, float delay)
	{
		if (velocity < 0.01f) return;

		var timer = GetTree().CreateTimer(delay);
		timer.Timeout += () => _sampler?.PlayNote(instrumentId, pitch, velocity, duration);
	}
}
