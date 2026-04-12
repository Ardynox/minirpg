using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Module;

namespace MiniRPG;

public partial class Main
{
	private ThreatHudMode _lastActiveThreatMode;
	private PlayerTargetingContext _playerTargeting = PlayerTargetingContext.Empty;

	private void UpdateThreatHud(double delta, RuntimeUiModeSnapshot uiMode)
	{
		if (_threatHud == null)
			return;

		var snapshot = _session.GameStarted
			? ThreatHudModule.BuildSnapshot(_state)
			: ThreatHudSnapshot.Hidden;
		if (_lastActiveThreatMode != ThreatHudMode.Hidden
			&& snapshot.Mode == ThreatHudMode.Hidden
			&& !PlayerDead
			&& _session.GameStarted
			&& !_menu.InMenu)
		{
			_log.Add(LocalizationService.T("log.awareness.disengaged"));
		}

		_lastActiveThreatMode = snapshot.Mode;
		_threatHud.Update(snapshot, delta, uiMode.SuppressHudAndAlerts);
	}

	private void UpdateTargetSummaryHud(RuntimeUiModeSnapshot uiMode)
	{
		if (_targetSummaryHud == null)
			return;

		var snapshot = _session.GameStarted
			? BuildTargetSummarySnapshot()
			: TargetSummarySnapshot.Hidden;
		_targetSummaryHud.Update(snapshot, uiMode.SuppressHudAndAlerts);
	}

	private TargetSummarySnapshot BuildTargetSummarySnapshot()
	{
		var resolution = PlayerTargetingModule.Resolve(_state, _playerTargeting);
		_playerTargeting = resolution.Context;

		var selection = PlayerTargetingModule.ResolveSummaryTarget(_state, _playerTargeting, IsHudTargetVisible);
		return TargetSummaryHudModule.BuildSnapshot(_state, selection.Target, selection.MarkCurrentTarget);
	}

	private bool IsHudTargetVisible(Actor actor) =>
		_mapRender != null && _mapRender.IsWorldCellVisible(actor.X, actor.Y, actor.Z);

	private void ClearPlayerTargeting()
	{
		_playerTargeting = PlayerTargetingContext.Empty;
	}

	private void SetCurrentTarget(Actor target, PlayerTargetSource source)
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null || !PlayerTargetingModule.IsTargetValid(player, target))
			return;

		_playerTargeting = PlayerTargetingModule.SetTarget(target, source);
	}

	private Vector3I ResolveSkillCursorOriginCell(Actor player)
	{
		var origin = PlayerTargetingModule.ResolveCursorOrigin(_state, player, _playerTargeting);
		return new Vector3I(origin.X, origin.Y, origin.Z);
	}

	private void ResetThreatHud()
	{
		if (_threatHud == null)
			return;

		_lastActiveThreatMode = ThreatHudMode.Hidden;
		_threatHud.HideImmediate();
		_targetSummaryHud.HideImmediate();
		_healthAlerts.HideImmediate();
	}
}
