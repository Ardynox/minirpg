using Godot;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;

namespace MiniRPG;

public partial class Main
{
	private AutoNavigationInterruptPolicy _autoNavigationInterruptPolicy = AutoNavigationInterruptPolicy.ConservativeStop;
	private Vector3I? _autoNavigationTargetWorldCell;
	private AutoNavigationInFlightKind _autoNavigationInFlightKind;
	private int _autoNavigationInFlightStartX;
	private int _autoNavigationInFlightStartY;
	private int _autoNavigationInFlightStartZ;
	private int _autoNavigationInFlightStartTurn;
	private long _autoNavigationInFlightActivityVersion;
	private string? _autoNavigationLastStopReason;
	private bool _autoNavigationExecutingStep;

	private bool IsAutoNavigationActive => _autoNavigationTargetWorldCell != null;

	private bool StartOrRetargetAutoNavigation(Vector3I targetCell)
	{
		if (!_session.GameStarted
			|| _menu.InMenu
			|| PlayerDead
			|| MapEditorActive
			|| LayoutEditActive
			|| _state.World == null)
		{
			return false;
		}

		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return false;

		var plan = AutoNavigationPlanner.PlanNextStep(
			_state.World,
			player.X,
			player.Y,
			player.Z,
			targetCell.X,
			targetCell.Y,
			targetCell.Z);
		if (!plan.Success && !plan.ReachedTarget)
		{
			LogAutoNavigationFailure(plan.FailureReason);
			return false;
		}

		if (plan.ReachedTarget)
		{
			CancelAutoNavigation(AutoNavigationStopReason.TargetReached, emitLog: false);
			return true;
		}

		var retargeted = _autoNavigationTargetWorldCell is { } current && current != targetCell;
		_autoNavigationTargetWorldCell = targetCell;
		ClearAutoNavigationInFlight();
		_autoNavigationLastStopReason = null;
		_playerRestModeActive = false;
		if (retargeted)
		{
			_log.Add(LocalizationService.TOrFallback(
				"log.auto_navigation.retargeted",
				"Auto-navigation retargeted to ({x}, {y}, {z}).",
				("x", targetCell.X),
				("y", targetCell.Y),
				("z", targetCell.Z)));
		}
		else
		{
			_log.Add(LocalizationService.TOrFallback(
				"log.auto_navigation.started",
				"Auto-navigation started toward ({x}, {y}, {z}).",
				("x", targetCell.X),
				("y", targetCell.Y),
				("z", targetCell.Z)));
		}

		RefreshAutoNavigationPresentation();
		return true;
	}

	private void CancelAutoNavigation(AutoNavigationStopReason reason, bool emitLog)
	{
		var hadState = _autoNavigationTargetWorldCell != null || _autoNavigationInFlightKind != AutoNavigationInFlightKind.None;
		_autoNavigationTargetWorldCell = null;
		ClearAutoNavigationInFlight();
		_autoNavigationLastStopReason = reason.ToString();

		if (emitLog && hadState)
			_log.Add(ResolveAutoNavigationStopMessage(reason));

		if (hadState)
			RefreshAutoNavigationPresentation();
	}

	private void ProcessAutoNavigation(RuntimeUiModeSnapshot snapshot)
	{
		if (!IsAutoNavigationActive)
			return;

		if (PlayerDead
			|| !snapshot.SessionStarted
			|| snapshot.InMenu
			|| snapshot.BusyOperationActive
			|| snapshot.HasVisibleModalLayer
			|| snapshot.MapEditorActive
			|| snapshot.LayoutEditActive
			|| snapshot.SettingsOverlayVisible
			|| _watchModeEnabled)
		{
			CancelAutoNavigation(AutoNavigationStopReason.GameplayBlocked, emitLog: false);
			return;
		}

		if (_state.World == null)
		{
			CancelAutoNavigation(AutoNavigationStopReason.SessionReset, emitLog: false);
			return;
		}

		var player = ActorModule.GetPlayer(_state);
		if (player == null)
		{
			CancelAutoNavigation(AutoNavigationStopReason.SessionReset, emitLog: false);
			return;
		}

		if (ShouldStopAutoNavigationForNearbyHostile(player))
		{
			CancelAutoNavigation(AutoNavigationStopReason.HostileNearby, emitLog: true);
			return;
		}

		if (_autoNavigationTargetWorldCell is not { } targetCell)
			return;

		if (player.X == targetCell.X && player.Y == targetCell.Y && player.Z == targetCell.Z)
		{
			CancelAutoNavigation(AutoNavigationStopReason.TargetReached, emitLog: false);
			return;
		}

		if (IsMultiplayerSession && !ResolveAutoNavigationMultiplayerStepReady(player))
			return;

		if (!TimelineTurnManager.IsPlayerTurn(_state))
			return;

		var plan = AutoNavigationPlanner.PlanNextStep(
			_state.World,
			player.X,
			player.Y,
			player.Z,
			targetCell.X,
			targetCell.Y,
			targetCell.Z);
		if (plan.ReachedTarget)
		{
			CancelAutoNavigation(AutoNavigationStopReason.TargetReached, emitLog: false);
			return;
		}

		if (!plan.Success || plan.Step is not { } nextStep)
		{
			CancelAutoNavigation(AutoNavigationStopReason.RouteFailed, emitLog: true);
			return;
		}

		ExecuteAutoNavigationStep(nextStep);
	}

	private bool ResolveAutoNavigationMultiplayerStepReady(Actor player)
	{
		if (_autoNavigationInFlightKind == AutoNavigationInFlightKind.None)
			return true;

		if (_multiplayerRuntimeCoordinator == null)
			return false;

		if (_autoNavigationInFlightKind == AutoNavigationInFlightKind.PredictedMove
			&& _multiplayerRuntimeCoordinator.PendingPredictionCount > 0)
		{
			return false;
		}

		var actorStateAdvanced = player.X != _autoNavigationInFlightStartX
			|| player.Y != _autoNavigationInFlightStartY
			|| player.Z != _autoNavigationInFlightStartZ
			|| _state.Turn != _autoNavigationInFlightStartTurn;
		var runtimeObservedActivity = _multiplayerRuntimeCoordinator.ActivityVersion != _autoNavigationInFlightActivityVersion;
		if (!actorStateAdvanced && !runtimeObservedActivity)
			return false;

		ClearAutoNavigationInFlight();
		return true;
	}

	private void ExecuteAutoNavigationStep(AutoNavigationStep step)
	{
		SetAutoNavigationInFlight(step);
		_autoNavigationExecutingStep = true;
		try
		{
			if (step.Kind == AutoNavigationStepKind.Move)
			{
				DoMove(step.Dx, step.Dy);
			}
			else
			{
				SubmitPlayerAction(TimelinePlayerAction.Climb(step.Dz));
			}
		}
		finally
		{
			_autoNavigationExecutingStep = false;
		}

		if (!IsMultiplayerSession)
			ClearAutoNavigationInFlight();
	}

	private void SetAutoNavigationInFlight(AutoNavigationStep step)
	{
		if (!IsMultiplayerSession)
		{
			ClearAutoNavigationInFlight();
			return;
		}

		_autoNavigationInFlightKind = step.Kind == AutoNavigationStepKind.Move
			? AutoNavigationInFlightKind.PredictedMove
			: AutoNavigationInFlightKind.TimelineAction;
		_autoNavigationInFlightStartX = _state.PlayerX;
		_autoNavigationInFlightStartY = _state.PlayerY;
		_autoNavigationInFlightStartZ = _state.PlayerZ;
		_autoNavigationInFlightStartTurn = _state.Turn;
		_autoNavigationInFlightActivityVersion = _multiplayerRuntimeCoordinator?.ActivityVersion ?? 0L;
	}

	private void ClearAutoNavigationInFlight()
	{
		_autoNavigationInFlightKind = AutoNavigationInFlightKind.None;
		_autoNavigationInFlightStartX = 0;
		_autoNavigationInFlightStartY = 0;
		_autoNavigationInFlightStartZ = 0;
		_autoNavigationInFlightStartTurn = 0;
		_autoNavigationInFlightActivityVersion = 0L;
	}

	private void InterruptAutoNavigationForManualInput()
	{
		if (!IsAutoNavigationActive || _autoNavigationExecutingStep)
			return;

		CancelAutoNavigation(AutoNavigationStopReason.ManualInput, emitLog: true);
	}

	private bool ShouldStopAutoNavigationForNearbyHostile(Actor player)
	{
		if (_autoNavigationInterruptPolicy is not AutoNavigationInterruptPolicy.ConservativeStop
			and not AutoNavigationInterruptPolicy.HostileProximityStop)
		{
			return false;
		}

		return HasAdjacentHostile(player);
	}

	private bool HasAdjacentHostile(Actor player)
	{
		foreach (var (dx, dy) in GridDirections.Cardinal)
		{
			var hostile = ActorModule.GetHostileAt(_state, player.X + dx, player.Y + dy, player.Z);
			if (hostile != null)
				return true;
		}

		return false;
	}

	private void RefreshAutoNavigationPresentation()
	{
		if (_session.GameStarted && !_menu.InMenu && RenderReady)
			FlushMap();
	}

	private static bool ShouldInterruptAutoNavigationForCommand(string cmd)
	{
		if (string.IsNullOrWhiteSpace(cmd))
			return false;

		if (cmd.StartsWith(":dig_", System.StringComparison.Ordinal) && cmd != ":dig")
			return true;

		return cmd is
			"w" or "a" or "s" or "d"
			or "look"
			or ":interact" or "interact"
			or ":dig" or "dig"
			or ":dig_up" or "dig_up"
			or ":dig_down" or "dig_down"
			or ":climb_up" or "climb_up"
			or ":climb_down" or "climb_down"
			or ":rest" or "rest";
	}

	private Vector3I? ResolvePrimaryTargetCursorWorldCell() =>
		_skillTargetCursorActive
			? _skillTargetWorldCell
			: _autoNavigationTargetWorldCell;

	private void CycleAutoNavigationInterruptPolicy()
	{
		_autoNavigationInterruptPolicy = _autoNavigationInterruptPolicy switch
		{
			AutoNavigationInterruptPolicy.ConservativeStop => AutoNavigationInterruptPolicy.ManualOnly,
			AutoNavigationInterruptPolicy.ManualOnly => AutoNavigationInterruptPolicy.HostileProximityStop,
			_ => AutoNavigationInterruptPolicy.ConservativeStop,
		};
		AppSettingsStore.SaveAutoNavigationInterruptPolicy(_autoNavigationInterruptPolicy);
		SyncSettingsUiState();
	}

	private void LogAutoNavigationFailure(AutoNavigationFailureReason failureReason)
	{
		var message = failureReason switch
		{
			AutoNavigationFailureReason.InvalidTarget => LocalizationService.TOrFallback(
				"log.auto_navigation.invalid_target",
				"That destination is not reachable right now."),
			AutoNavigationFailureReason.SearchLimitExceeded => LocalizationService.TOrFallback(
				"log.auto_navigation.route_failed",
				"Auto-navigation could not find a safe route."),
			_ => LocalizationService.TOrFallback(
				"log.auto_navigation.route_failed",
				"Auto-navigation could not find a safe route."),
		};
		_log.Add(message);
	}

	private string ResolveAutoNavigationStopMessage(AutoNavigationStopReason reason) => reason switch
	{
		AutoNavigationStopReason.ManualInput => LocalizationService.TOrFallback(
			"log.auto_navigation.stopped.manual",
			"Auto-navigation stopped because you took manual control."),
		AutoNavigationStopReason.HostileNearby => LocalizationService.TOrFallback(
			"log.auto_navigation.stopped.hostile",
			"Auto-navigation stopped because a hostile is adjacent."),
		AutoNavigationStopReason.RouteFailed => LocalizationService.TOrFallback(
			"log.auto_navigation.route_failed",
			"Auto-navigation could not find a safe route."),
		_ => LocalizationService.TOrFallback(
			"log.auto_navigation.stopped",
			"Auto-navigation stopped."),
	};

	private enum AutoNavigationInFlightKind
	{
		None,
		PredictedMove,
		TimelineAction,
	}

	private enum AutoNavigationStopReason
	{
		ManualInput,
		TargetReached,
		RouteFailed,
		HostileNearby,
		GameplayBlocked,
		SessionReset,
	}
}
