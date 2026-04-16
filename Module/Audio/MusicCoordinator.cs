using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Core.Music;

namespace MiniRPG.Module.Audio;

public sealed partial class MusicCoordinator : Node
{
	private MusicRenderer? _renderer;
	private AudioBusSetup? _busSetup;
	private GameState? _state;
	private MusicParams _currentParams = new();
	private MusicParams _smoothedParams = new();
	private MusicScore? _currentScore;
	private bool _enabled = true;
	private int _lastEvaluatedTurn = -1;
	private float _idleTimer;
	private const float IdleRegenerateInterval = 15f;
	private const float ParamSmoothingFactor = 0.3f;

	public bool Enabled
	{
		get => _enabled;
		set
		{
			_enabled = value;
			if (!_enabled) _renderer?.Stop();
		}
	}

	public float MusicVolume
	{
		get => _renderer?.MasterVolume ?? 1f;
		set
		{
			if (_renderer != null) _renderer.MasterVolume = value;
			AudioBusSetup.SetMusicVolume(value);
		}
	}

	public override void _Ready()
	{
		_busSetup = new AudioBusSetup();
		_busSetup.EnsureInitialized();

		_renderer = new MusicRenderer();
		AddChild(_renderer);
	}

	public void Bind(GameState state)
	{
		_state = state;
		_lastEvaluatedTurn = -1;
	}

	public void Unbind()
	{
		_state = null;
		_renderer?.Stop();
		_currentParams = new MusicParams();
		_smoothedParams = new MusicParams();
		_currentScore = null;
	}

	public void OnTurnAdvanced(IReadOnlyList<GameEvent> events)
	{
		if (!_enabled || _state == null)
			return;

		var player = GetPlayerActor();
		if (player == null) return;

		var newParams = MusicEvaluator.Evaluate(_state, player);
		MusicEvaluator.AccumulateEventPulses(newParams, events, _state.PlayerId);

		newParams.CombatPulse = Math.Max(newParams.CombatPulse, _currentParams.CombatPulse);
		newParams.SocialPulse = Math.Max(newParams.SocialPulse, _currentParams.SocialPulse);
		newParams.DeathPulse = Math.Max(newParams.DeathPulse, _currentParams.DeathPulse);
		newParams.MentalBreakPulse = Math.Max(newParams.MentalBreakPulse, _currentParams.MentalBreakPulse);

		_currentParams = newParams;
		_smoothedParams = _smoothedParams.Lerp(_currentParams, ParamSmoothingFactor);

		MusicEvaluator.DecayPulses(_currentParams);

		if (ShouldRecompose())
		{
			var score = MusicComposer.Compose(_smoothedParams);
			_currentScore = score;
			_renderer?.Play(score);
			_lastEvaluatedTurn = _state.Turn;
		}

		_idleTimer = 0f;
	}

	public override void _Process(double delta)
	{
		if (!_enabled || _state == null || _currentScore == null)
			return;

		_idleTimer += (float)delta;

		if (_idleTimer >= IdleRegenerateInterval)
		{
			_idleTimer = 0f;
			RegenerateForIdle();
		}
	}

	private void RegenerateForIdle()
	{
		if (_state == null) return;

		var player = GetPlayerActor();
		if (player == null) return;

		var freshParams = MusicEvaluator.Evaluate(_state, player);
		freshParams.CombatPulse = _currentParams.CombatPulse;
		freshParams.SocialPulse = _currentParams.SocialPulse;
		freshParams.DeathPulse = _currentParams.DeathPulse;
		freshParams.MentalBreakPulse = _currentParams.MentalBreakPulse;

		MusicEvaluator.DecayPulses(freshParams);
		_currentParams = freshParams;
		_smoothedParams = _smoothedParams.Lerp(_currentParams, ParamSmoothingFactor * 0.5f);

		var score = MusicComposer.Compose(_smoothedParams);
		_currentScore = score;
		_renderer?.Play(score);
	}

	private bool ShouldRecompose()
	{
		if (_currentScore == null) return true;
		if (_lastEvaluatedTurn < 0) return true;

		if (_state != null && _state.Turn - _lastEvaluatedTurn >= 3)
			return true;

		if (MathF.Abs(_smoothedParams.Arousal - PrevArousal()) > 0.25f)
			return true;
		if (MathF.Abs(_smoothedParams.Valence - PrevValence()) > 0.2f)
			return true;
		if (_smoothedParams.CombatPulse > 0.4f && PrevCombatPulse() < 0.2f)
			return true;
		if (_smoothedParams.MentalBreakPulse > 0.4f)
			return true;

		return false;
	}

	private float PrevArousal() => _currentScore != null ? EstimateArousalFromBpm(_currentScore.Bpm) : 0f;
	private float PrevValence() => _currentScore != null ? EstimateValenceFromScale(_currentScore.Scale) : 0.5f;
	private float PrevCombatPulse() => _currentScore != null && _currentScore.Bpm > 110 ? 0.5f : 0f;

	private static float EstimateArousalFromBpm(int bpm) => Math.Clamp((bpm - 60f) / 80f, 0f, 1f);

	private static float EstimateValenceFromScale(ScaleType scale) => scale switch
	{
		ScaleType.Ionian or ScaleType.Lydian => 0.8f,
		ScaleType.Mixolydian => 0.65f,
		ScaleType.Dorian => 0.45f,
		ScaleType.Aeolian => 0.3f,
		ScaleType.Phrygian or ScaleType.Locrian => 0.15f,
		_ => 0.5f,
	};

	private Actor? GetPlayerActor()
	{
		if (_state == null) return null;
		_state.Actors.TryGetValue(_state.PlayerId, out var player);
		return player;
	}
}
