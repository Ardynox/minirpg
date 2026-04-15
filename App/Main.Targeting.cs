using Godot;
using MiniRPG.Core.Data;

namespace MiniRPG;

public partial class Main
{
	private void UpdateThreatHud(double delta, RuntimeUiModeSnapshot uiMode)
		=> _playerTargetingCoordinator.UpdateThreatHud(delta, uiMode);

	private void UpdateTargetSummaryHud(RuntimeUiModeSnapshot uiMode)
		=> _playerTargetingCoordinator.UpdateTargetSummaryHud(uiMode);

	private void ClearPlayerTargeting()
		=> _playerTargetingCoordinator.ClearPlayerTargeting();

	private void SetCurrentTarget(Actor target, PlayerTargetSource source)
		=> _playerTargetingCoordinator.SetCurrentTarget(target, source);

	private Vector3I ResolveSkillCursorOriginCell(Actor player)
		=> _playerTargetingCoordinator.ResolveSkillCursorOriginCell(player);

	private void ResetThreatHud()
		=> _playerTargetingCoordinator.ResetThreatHud();
}
