using System;
using System.Collections.Generic;

namespace MiniRPG.Module;

internal static class LootService
{
	public static ServerActionResult HandleActorKilled(GameState state, GameEvent e)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(e);

		var logs = new List<string>();
		var rewardActor = ResolveRewardActor(state, e);
		if (rewardActor != null)
		{
			var goldDrop = e.Damage > 0 ? e.Damage : 5;
			rewardActor.Gold += goldDrop;
			logs.Add(LocalizationService.T("combat.gold_reward", ("gold", goldDrop), ("total", rewardActor.Gold)));
		}

		GenerateLoot(state, e, logs);
		return ServerActionResult.Accept(logs: logs);
	}

	private static void GenerateLoot(GameState state, GameEvent e, List<string> logs)
	{
		var rng = new Random(state.RngSeed + state.Turn + (e.TargetId ?? string.Empty).GetHashCode());
		if (rng.Next(100) >= 40)
			return;

		var pool = new List<string>(PresetDB.Items.Keys);
		if (pool.Count == 0)
			return;

		var itemId = pool[rng.Next(pool.Count)];
		var item = PresetDB.CloneItem(itemId, state.Turn);
		state.World!.PlaceItem(e.TargetX, e.TargetY, e.TargetZ, item);
		logs.Add(LocalizationService.T(
			"combat.loot_drop",
			("target", e.TargetActorName),
			("item", IdentificationModule.GetItemDisplayName(state, item))));
	}

	private static Actor? ResolveRewardActor(GameState state, GameEvent e)
	{
		if (!string.IsNullOrWhiteSpace(e.InitiatorId))
		{
			var initiator = ActorModule.GetById(state, e.InitiatorId);
			if (initiator != null)
				return initiator;
		}

		return ActorModule.GetPlayer(state);
	}
}
