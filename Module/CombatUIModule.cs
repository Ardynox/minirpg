using MiniRPG.Core.Config;

namespace MiniRPG.Module;

public class CombatUIModule
{
	private readonly IGameUI _ui;

	public CombatUIModule(IGameUI ui) => _ui = ui;

	public void HandleActorKilled(GameEvent e)
	{
		_ui.AddLog(LocalizationService.T("combat.killed", ("target", e.TargetActorName)));
		var result = ServerActionGateway.HandleActorKilled(_ui.State, e);
		foreach (var log in result.Logs)
			_ui.AddLog(log);
	}
}
