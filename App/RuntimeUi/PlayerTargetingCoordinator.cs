using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Module;
using MiniRPG.Module.Render;

namespace MiniRPG;

internal sealed class PlayerTargetingCoordinator
{
	private readonly GameState _state;
	private readonly GameSessionModule _session;
	private readonly LogModule _log;
	private readonly MenuModule _menu;
	private readonly Func<IsometricVoxelRenderer?> _getMapRender;
	private readonly Func<bool> _playerDead;

	private ThreatHudMode _lastActiveThreatMode;
	private PlayerTargetingContext _playerTargeting = PlayerTargetingContext.Empty;

	public PlayerTargetingCoordinator(
		GameState state,
		GameSessionModule session,
		LogModule log,
		MenuModule menu,
		Func<IsometricVoxelRenderer?> getMapRender,
		Func<bool> playerDead)
	{
		_state = state;
		_session = session;
		_log = log;
		_menu = menu;
		_getMapRender = getMapRender;
		_playerDead = playerDead;
	}

	public ThreatHudModule? ThreatHud { get; set; }
	public TargetSummaryHudModule? TargetSummaryHud { get; set; }
	public HealthAlertsModule? HealthAlerts { get; set; }

	public void UpdateThreatHud(double delta, RuntimeUiModeSnapshot uiMode)
	{
		if (ThreatHud == null)
			return;

		var snapshot = _session.GameStarted
			? ThreatHudModule.BuildSnapshot(_state)
			: ThreatHudSnapshot.Hidden;
		if (_lastActiveThreatMode != ThreatHudMode.Hidden
			&& snapshot.Mode == ThreatHudMode.Hidden
			&& !_playerDead()
			&& _session.GameStarted
			&& !_menu.InMenu)
		{
			_log.Add(LocalizationService.T("log.awareness.disengaged"));
		}

		_lastActiveThreatMode = snapshot.Mode;
		ThreatHud.Update(snapshot, delta, uiMode.SuppressHudAndAlerts);
	}

	public void UpdateTargetSummaryHud(RuntimeUiModeSnapshot uiMode)
	{
		if (TargetSummaryHud == null)
			return;

		var snapshot = _session.GameStarted
			? BuildTargetSummarySnapshot()
			: TargetSummarySnapshot.Hidden;
		TargetSummaryHud.Update(snapshot, uiMode.SuppressHudAndAlerts);
	}

	public void ClearPlayerTargeting()
	{
		_playerTargeting = PlayerTargetingContext.Empty;
	}

	public void SetCurrentTarget(Actor target, PlayerTargetSource source)
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null || !PlayerTargetingModule.IsTargetValid(player, target))
			return;

		_playerTargeting = PlayerTargetingModule.SetTarget(target, source);
	}

	public Vector3I ResolveSkillCursorOriginCell(Actor player)
	{
		var origin = PlayerTargetingModule.ResolveCursorOrigin(_state, player, _playerTargeting);
		return new Vector3I(origin.X, origin.Y, origin.Z);
	}

	public void ResetThreatHud()
	{
		if (ThreatHud == null)
			return;

		_lastActiveThreatMode = ThreatHudMode.Hidden;
		ThreatHud.HideImmediate();
		TargetSummaryHud?.HideImmediate();
		HealthAlerts?.HideImmediate();
	}

	private TargetSummarySnapshot BuildTargetSummarySnapshot()
	{
		var resolution = PlayerTargetingModule.Resolve(_state, _playerTargeting);
		_playerTargeting = resolution.Context;

		var selection = PlayerTargetingModule.ResolveSummaryTarget(_state, _playerTargeting, IsHudTargetVisible);
		return TargetSummaryHudModule.BuildSnapshot(_state, selection.Target, selection.MarkCurrentTarget);
	}

	private bool IsHudTargetVisible(Actor actor)
	{
		var mapRender = _getMapRender();
		return mapRender != null && mapRender.IsWorldCellVisible(actor.X, actor.Y, actor.Z);
	}
}
