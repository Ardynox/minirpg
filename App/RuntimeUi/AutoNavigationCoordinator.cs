using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using MiniRPG.Module.Render;

namespace MiniRPG;

internal sealed class AutoNavigationCoordinator
{
	private readonly GameState _state;
	private readonly GameSessionModule _session;
	private readonly LogModule _log;
	private readonly Func<MenuModule> _getMenu;
	private readonly Func<bool> _playerDead;
	private readonly Func<bool> _mapEditorActive;
	private readonly Func<bool> _layoutEditActive;
	private readonly Func<bool> _renderReady;
	private readonly Func<bool> _isMultiplayerSession;
	private readonly Func<bool> _watchModeEnabled;
	private readonly Func<bool> _skillTargetCursorActive;
	private readonly Func<Vector3I?> _skillTargetWorldCell;
	private readonly Action<int, int> _doMove;
	private readonly Action<TimelinePlayerAction> _submitPlayerAction;
	private readonly Action _flushMap;
	private readonly Action _syncSettingsUiState;
	private readonly Func<long> _getMultiplayerActivityVersion;
	private readonly Func<int> _getMultiplayerPendingPredictionCount;

	private AutoNavigationInterruptPolicy _interruptPolicy = AutoNavigationInterruptPolicy.ConservativeStop;
	private Vector3I? _targetWorldCell;
	private AutoNavigationInFlightKind _inFlightKind;
	private int _inFlightStartX;
	private int _inFlightStartY;
	private int _inFlightStartZ;
	private int _inFlightStartTurn;
	private long _inFlightActivityVersion;
	private string? _lastStopReason;
	private bool _executingStep;
	private ActorMotionTimingTier _currentPlayerMotionTimingTier = ActorMotionTiming.ResolveManualPlayerTier();

	private Vector3I? _previewTarget;
	private List<Vector3I>? _previewPath;

	public AutoNavigationCoordinator(
		GameState state,
		GameSessionModule session,
		LogModule log,
		Func<MenuModule> getMenu,
		Func<bool> playerDead,
		Func<bool> mapEditorActive,
		Func<bool> layoutEditActive,
		Func<bool> renderReady,
		Func<bool> isMultiplayerSession,
		Func<bool> watchModeEnabled,
		Func<bool> skillTargetCursorActive,
		Func<Vector3I?> skillTargetWorldCell,
		Action<int, int> doMove,
		Action<TimelinePlayerAction> submitPlayerAction,
		Action flushMap,
		Action syncSettingsUiState,
		Func<long> getMultiplayerActivityVersion,
		Func<int> getMultiplayerPendingPredictionCount)
	{
		_state = state;
		_session = session;
		_log = log;
		_getMenu = getMenu;
		_playerDead = playerDead;
		_mapEditorActive = mapEditorActive;
		_layoutEditActive = layoutEditActive;
		_renderReady = renderReady;
		_isMultiplayerSession = isMultiplayerSession;
		_watchModeEnabled = watchModeEnabled;
		_skillTargetCursorActive = skillTargetCursorActive;
		_skillTargetWorldCell = skillTargetWorldCell;
		_doMove = doMove;
		_submitPlayerAction = submitPlayerAction;
		_flushMap = flushMap;
		_syncSettingsUiState = syncSettingsUiState;
		_getMultiplayerActivityVersion = getMultiplayerActivityVersion;
		_getMultiplayerPendingPredictionCount = getMultiplayerPendingPredictionCount;
	}

	public bool IsActive => _targetWorldCell != null;
	public bool IsPreviewActive => _previewTarget != null;
	public bool IsExecutingStep => _executingStep;
	public AutoNavigationInterruptPolicy InterruptPolicy => _interruptPolicy;
	public ActorMotionTimingTier CurrentPlayerMotionTimingTier => _currentPlayerMotionTimingTier;

	public Action<bool> SetPlayerRestModeActive { get; set; } = _ => { };

	public void LoadInterruptPolicy(AutoNavigationInterruptPolicy policy) => _interruptPolicy = policy;

	public bool StartOrRetarget(Vector3I targetCell)
	{
		if (!_session.GameStarted
			|| _getMenu().InMenu
			|| _playerDead()
			|| _mapEditorActive()
			|| _layoutEditActive()
			|| _state.World == null)
		{
			return false;
		}

		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return false;

		if (player.X == targetCell.X && player.Y == targetCell.Y && player.Z == targetCell.Z)
		{
			CancelPreview();
			Cancel(AutoNavigationStopReason.TargetReached, emitLog: false);
			return true;
		}

		if (_previewTarget is { } previewTarget
			&& previewTarget == targetCell
			&& _previewPath is { Count: > 0 })
		{
			return Confirm(targetCell);
		}

		return ShowPreview(player, targetCell);
	}

	public void CancelPreview()
	{
		if (_previewTarget == null && _previewPath == null)
			return;

		_previewTarget = null;
		_previewPath = null;
		RefreshPresentation();
	}

	public void Cancel(AutoNavigationStopReason reason, bool emitLog)
	{
		var hadState = _targetWorldCell != null || _inFlightKind != AutoNavigationInFlightKind.None;
		_targetWorldCell = null;
		ClearInFlight();
		ResetCurrentPlayerMotionTimingTier();
		_lastStopReason = reason.ToString();

		if (emitLog && hadState)
			_log.Add(ResolveStopMessage(reason));

		if (hadState)
			RefreshPresentation();
	}

	public void Process(RuntimeUiModeSnapshot snapshot)
	{
		if (!IsActive)
			return;

		if (_playerDead()
			|| !snapshot.SessionStarted
			|| snapshot.InMenu
			|| snapshot.BusyOperationActive
			|| snapshot.HasVisibleModalLayer
			|| snapshot.MapEditorActive
			|| snapshot.LayoutEditActive
			|| snapshot.SettingsOverlayVisible
			|| _watchModeEnabled())
		{
			Cancel(AutoNavigationStopReason.GameplayBlocked, emitLog: false);
			return;
		}

		if (_state.World == null)
		{
			Cancel(AutoNavigationStopReason.SessionReset, emitLog: false);
			return;
		}

		var player = ActorModule.GetPlayer(_state);
		if (player == null)
		{
			Cancel(AutoNavigationStopReason.SessionReset, emitLog: false);
			return;
		}

		if (ShouldStopForNearbyHostile(player))
		{
			Cancel(AutoNavigationStopReason.HostileNearby, emitLog: true);
			return;
		}

		if (_targetWorldCell is not { } targetCell)
			return;

		if (player.X == targetCell.X && player.Y == targetCell.Y && player.Z == targetCell.Z)
		{
			Cancel(AutoNavigationStopReason.TargetReached, emitLog: false);
			return;
		}

		if (_isMultiplayerSession() && !ResolveMultiplayerStepReady(player))
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
			Cancel(AutoNavigationStopReason.TargetReached, emitLog: false);
			return;
		}

		if (!plan.Success || plan.Step is not { } nextStep)
		{
			Cancel(AutoNavigationStopReason.RouteFailed, emitLog: true);
			return;
		}

		ExecuteStep(nextStep);
	}

	public void InterruptForManualInput()
	{
		if (_executingStep)
			return;

		if (IsPreviewActive)
			CancelPreview();

		if (IsActive)
			Cancel(AutoNavigationStopReason.ManualInput, emitLog: true);
	}

	public Vector3I? ResolvePrimaryTargetCursorWorldCell()
	{
		if (_skillTargetCursorActive())
			return _skillTargetWorldCell();
		if (_targetWorldCell != null)
			return _targetWorldCell;
		return _previewTarget;
	}

	public IReadOnlyList<Vector3I>? ResolvePathHighlightCells() => _previewPath;

	public void CycleInterruptPolicy()
	{
		_interruptPolicy = _interruptPolicy switch
		{
			AutoNavigationInterruptPolicy.ConservativeStop => AutoNavigationInterruptPolicy.ManualOnly,
			AutoNavigationInterruptPolicy.ManualOnly => AutoNavigationInterruptPolicy.HostileProximityStop,
			_ => AutoNavigationInterruptPolicy.ConservativeStop,
		};
		AppSettingsStore.SaveAutoNavigationInterruptPolicy(_interruptPolicy);
		_syncSettingsUiState();
	}

	public static bool ShouldInterruptForCommand(string cmd)
	{
		if (string.IsNullOrWhiteSpace(cmd))
			return false;

		if (cmd.StartsWith(":dig_", StringComparison.Ordinal) && cmd != ":dig")
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

	private bool ShowPreview(Actor player, Vector3I targetCell)
	{
		var fullPath = AutoNavigationPlanner.PlanFullPath(
			_state.World!,
			player.X,
			player.Y,
			player.Z,
			targetCell.X,
			targetCell.Y,
			targetCell.Z);

		if (!fullPath.Success || fullPath.Path.Count == 0)
		{
			CancelPreview();
			LogFailure(fullPath.FailureReason);
			return false;
		}

		if (IsActive)
			Cancel(AutoNavigationStopReason.ManualInput, emitLog: false);

		_previewTarget = targetCell;
		_previewPath = new List<Vector3I>(fullPath.Path.Count);
		for (var i = 0; i < fullPath.Path.Count; i++)
		{
			var node = fullPath.Path[i];
			_previewPath.Add(new Vector3I(node.X, node.Y, node.Z));
		}

		_log.Add(LocalizationService.T(
			"log.auto_navigation.preview",
			("steps", fullPath.Path.Count),
			("x", targetCell.X),
			("y", targetCell.Y),
			("z", targetCell.Z)));

		RefreshPresentation();
		return true;
	}

	private bool Confirm(Vector3I targetCell)
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return false;

		var plan = AutoNavigationPlanner.PlanNextStep(
			_state.World!,
			player.X,
			player.Y,
			player.Z,
			targetCell.X,
			targetCell.Y,
			targetCell.Z);
		if (!plan.Success && !plan.ReachedTarget)
		{
			CancelPreview();
			LogFailure(plan.FailureReason);
			return false;
		}

		if (plan.ReachedTarget)
		{
			CancelPreview();
			Cancel(AutoNavigationStopReason.TargetReached, emitLog: false);
			return true;
		}

		var confirmedPathLength = _previewPath?.Count ?? 0;
		_previewTarget = null;
		_previewPath = null;

		_targetWorldCell = targetCell;
		_currentPlayerMotionTimingTier = ResolveAutoNavigationMotionTimingTier(confirmedPathLength);
		ClearInFlight();
		_lastStopReason = null;
		SetPlayerRestModeActive(false);
		_log.Add(LocalizationService.TOrFallback(
			"log.auto_navigation.started",
			"Auto-navigation started toward ({x}, {y}, {z}).",
			("x", targetCell.X),
			("y", targetCell.Y),
			("z", targetCell.Z)));

		RefreshPresentation();
		return true;
	}

	private bool ResolveMultiplayerStepReady(Actor player)
	{
		if (_inFlightKind == AutoNavigationInFlightKind.None)
			return true;

		if (_inFlightKind == AutoNavigationInFlightKind.PredictedMove
			&& _getMultiplayerPendingPredictionCount() > 0)
		{
			return false;
		}

		var actorStateAdvanced = player.X != _inFlightStartX
			|| player.Y != _inFlightStartY
			|| player.Z != _inFlightStartZ
			|| _state.Turn != _inFlightStartTurn;
		var runtimeObservedActivity = _getMultiplayerActivityVersion() != _inFlightActivityVersion;
		if (!actorStateAdvanced && !runtimeObservedActivity)
			return false;

		ClearInFlight();
		return true;
	}

	private void ExecuteStep(AutoNavigationStep step)
	{
		SetInFlight(step);
		_executingStep = true;
		try
		{
			if (step.Kind == AutoNavigationStepKind.Move)
				_doMove(step.Dx, step.Dy);
			else
				_submitPlayerAction(TimelinePlayerAction.Climb(step.Dz));
		}
		finally
		{
			_executingStep = false;
		}

		if (!_isMultiplayerSession())
			ClearInFlight();
	}

	private void SetInFlight(AutoNavigationStep step)
	{
		if (!_isMultiplayerSession())
		{
			ClearInFlight();
			return;
		}

		_inFlightKind = step.Kind == AutoNavigationStepKind.Move
			? AutoNavigationInFlightKind.PredictedMove
			: AutoNavigationInFlightKind.TimelineAction;
		_inFlightStartX = _state.PlayerX;
		_inFlightStartY = _state.PlayerY;
		_inFlightStartZ = _state.PlayerZ;
		_inFlightStartTurn = _state.Turn;
		_inFlightActivityVersion = _getMultiplayerActivityVersion();
	}

	private void ClearInFlight()
	{
		_inFlightKind = AutoNavigationInFlightKind.None;
		_inFlightStartX = 0;
		_inFlightStartY = 0;
		_inFlightStartZ = 0;
		_inFlightStartTurn = 0;
		_inFlightActivityVersion = 0L;
	}

	private void ResetCurrentPlayerMotionTimingTier()
	{
		_currentPlayerMotionTimingTier = ActorMotionTiming.ResolveManualPlayerTier();
	}

	internal static ActorMotionTimingTier ResolveAutoNavigationMotionTimingTier(int pathLength) =>
		ActorMotionTiming.ResolveAutoNavigationTier(pathLength);

	private bool ShouldStopForNearbyHostile(Actor player)
	{
		if (_interruptPolicy is not AutoNavigationInterruptPolicy.ConservativeStop
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

	private void RefreshPresentation()
	{
		if (_session.GameStarted && !_getMenu().InMenu && _renderReady())
			_flushMap();
	}

	private void LogFailure(AutoNavigationFailureReason failureReason)
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

	private static string ResolveStopMessage(AutoNavigationStopReason reason) => reason switch
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

	internal enum AutoNavigationInFlightKind
	{
		None,
		PredictedMove,
		TimelineAction,
	}
}

internal enum AutoNavigationStopReason
{
	ManualInput,
	TargetReached,
	RouteFailed,
	HostileNearby,
	GameplayBlocked,
	SessionReset,
}
