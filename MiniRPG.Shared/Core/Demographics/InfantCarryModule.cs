using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Demographics;

/// <summary>
/// 婴儿被抱状态最小同步：每天 <see cref="WorldDemographicsTick"/> 调一次，
/// 给所有 Infant 阶段角色挂上 <see cref="Actor.CarriedByActorId"/>，对应母亲的
/// <see cref="Actor.CarriedInfantId"/>。母亲不在场（已死亡 / 不在 state 里）时降级到父亲。
/// 父母都不在 → CarriedByActorId 留 null（孤儿，等领养逻辑后续补）。
///
/// Phase 5 不做：carry 时父母行动受限、抱孩子的能量消耗、被抱孩子的需求衰减。
/// 只做"标识谁在抱谁"，让 UI / 后续 AI 模块能直接用 <see cref="Actor.CarriedByActorId"/>
/// / <see cref="Actor.CarriedInfantId"/> 字段。
/// </summary>
public static class InfantCarryModule
{
	public static List<GameEvent> OnNewDay(GameState state)
	{
		LifeStageCatalog.EnsureLoaded();
		var events = new List<GameEvent>();
		var carriers = new HashSet<string>(StringComparer.Ordinal);

		foreach (var actor in state.Actors.Values.ToList())
		{
			var stage = LifeStageCatalog.GetLifeStage(state, actor);
			if (stage != LifeStage.Infant)
			{
				if (!string.IsNullOrWhiteSpace(actor.CarriedByActorId))
					actor.CarriedByActorId = null;
				continue;
			}

			var carrier = ResolveCarrier(state, actor);
			actor.CarriedByActorId = carrier?.Id;
			if (carrier != null)
				carriers.Add(carrier.Id);
		}

		foreach (var actor in state.Actors.Values)
		{
			if (carriers.Contains(actor.Id))
			{
				if (actor.CarriedInfantId == null
					|| !state.Actors.TryGetValue(actor.CarriedInfantId, out var infant)
					|| !string.Equals(infant.CarriedByActorId, actor.Id, StringComparison.Ordinal))
				{
					actor.CarriedInfantId = state.Actors.Values
						.FirstOrDefault(a => string.Equals(a.CarriedByActorId, actor.Id, StringComparison.Ordinal))
						?.Id;
				}
			}
			else if (!string.IsNullOrWhiteSpace(actor.CarriedInfantId))
			{
				actor.CarriedInfantId = null;
			}
		}

		return events;
	}

	private static Actor? ResolveCarrier(GameState state, Actor infant)
	{
		if (!string.IsNullOrWhiteSpace(infant.MotherActorId)
			&& state.Actors.TryGetValue(infant.MotherActorId!, out var mother))
			return mother;
		if (!string.IsNullOrWhiteSpace(infant.FatherActorId)
			&& state.Actors.TryGetValue(infant.FatherActorId!, out var father))
			return father;
		return null;
	}
}
