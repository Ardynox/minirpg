using System;
using System.Collections.Generic;

namespace MiniRPG;

/// <summary>
/// Owns the player "rest mode" loop that used to live directly on <c>Main</c>:
/// bedroll/threat guards, per-tick rest-action submission and external stop
/// signals (death, session reset, auto navigation, non-<c>:rest</c> commands).
/// </summary>
internal sealed class PlayerRestModeController
{
	private readonly GameState _state;
	private readonly LogModule _log;
	private readonly Func<bool> _isPlayerDead;
	private readonly Func<bool> _isWatchModeEnabled;
	private readonly Action<TimelinePlayerAction> _submitPlayerAction;
	private readonly Action<List<GameEvent>> _dispatch;

	private bool _active;

	public PlayerRestModeController(
		GameState state,
		LogModule log,
		Func<bool> isPlayerDead,
		Func<bool> isWatchModeEnabled,
		Action<TimelinePlayerAction> submitPlayerAction,
		Action<List<GameEvent>> dispatch)
	{
		_state = state;
		_log = log;
		_isPlayerDead = isPlayerDead;
		_isWatchModeEnabled = isWatchModeEnabled;
		_submitPlayerAction = submitPlayerAction;
		_dispatch = dispatch;
	}

	public bool IsActive => _active;

	public void Stop() => _active = false;

	/// <summary>Per-frame driver: auto-submit <see cref="TimelinePlayerAction.Rest"/> until rest is satisfied or threats appear.</summary>
	public void Tick()
	{
		if (!_active || _isPlayerDead() || _isWatchModeEnabled())
			return;

		var player = ActorModule.GetPlayer(_state);
		if (player == null)
		{
			_active = false;
			return;
		}

		ActorDerivedStateUpdater.SyncPlayerUiState(_state);
		var restValue = NeedSystem.GetNeedValueSnapshot(player, NeedIds.Rest);
		if (restValue >= 85f)
		{
			_active = false;
			return;
		}

		if (ThreatDetection.HasNearbyThreat(_state, player))
		{
			_active = false;
			return;
		}

		if (TimelineTurnManager.IsPlayerTurn(_state))
			_submitPlayerAction(TimelinePlayerAction.Rest());
	}

	/// <summary>Toggle rest mode on/off with bedroll + nearby-threat safety guards.</summary>
	public void Toggle()
	{
		if (_active)
		{
			_active = false;
			return;
		}

		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return;
		if (!NeedActionModule.HasBedroll(player))
		{
			_log.Add(LocalizationService.T("log.rest.needs_bedroll"));
			return;
		}
		if (ThreatDetection.HasNearbyThreat(_state, player))
		{
			_log.Add(LocalizationService.T("log.rest.unsafe"));
			return;
		}

		_active = true;
		_dispatch(
		[
			new GameEvent("rest_started")
			{
				InitiatorId = player.Id,
				TargetId = player.Id,
				TargetActorName = IdentificationModule.GetActorDisplayName(_state, player),
			},
		]);
	}
}
