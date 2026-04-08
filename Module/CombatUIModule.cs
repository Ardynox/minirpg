using System;
using System.Collections.Generic;
using MiniRPG.Core.Config;

namespace MiniRPG.Module;

public class CombatUIModule
{
	private readonly IGameUI _ui;

	public CombatUIModule(IGameUI ui) => _ui = ui;

	public void HandleActorKilled(GameEvent e)
	{
		_ui.AddLog(LocalizationService.T("combat.killed", ("target", e.TargetActorName)));
		var player = ActorModule.GetPlayer(_ui.State);
		if (player != null)
		{
			var goldDrop = e.Damage > 0 ? e.Damage : 5;
			player.Gold += goldDrop;
			_ui.AddLog(LocalizationService.T("combat.gold_reward", ("gold", goldDrop), ("total", player.Gold)));
		}

		GenerateLoot(e);
	}

	private void GenerateLoot(GameEvent e)
	{
		var rng = new Random(_ui.State.RngSeed + _ui.State.Turn + (e.TargetId ?? string.Empty).GetHashCode());
		if (rng.Next(100) >= 40)
			return;

		var pool = new List<string>(PresetDB.Items.Keys);
		if (pool.Count == 0)
			return;

		var itemId = pool[rng.Next(pool.Count)];
		var item = PresetDB.CloneItem(itemId);
		MapModule.PlaceItem(_ui.State, e.TargetX, e.TargetY, item);
		_ui.AddLog(LocalizationService.T("combat.loot_drop",
			("target", e.TargetActorName),
			("item", IdentificationModule.GetItemDisplayName(_ui.State, item))));
	}
}
