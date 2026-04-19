using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Demographics;

/// <summary>
/// 生命阶段过渡侦测：每天 <see cref="WorldDemographicsTick"/> 调一次，
/// 给所有 actor 比对当前 <see cref="LifeStageCatalog.GetLifeStage"/> 与
/// <see cref="Actor.LastResolvedLifeStage"/>，跨越时 emit <c>lifestage_transition</c> 事件。
///
/// 不直接改 actor 行为或装备——副作用由订阅方按事件类型决定（成年解锁工作 BrainId
/// / 长大解开 carry / 老年降低需求等都放在更上层的玩法层处理）。
/// 唯一会做的运行时调整：Infant→Child 时清空 <see cref="Actor.CarriedByActorId"/>，
/// 让孩子从被抱状态自然脱离（受 <see cref="InfantCarryModule"/> 配合）。
/// </summary>
public static class LifeStageTransitionService
{
	public static List<GameEvent> OnNewDay(GameState state)
	{
		LifeStageCatalog.EnsureLoaded();
		var events = new List<GameEvent>();
		foreach (var actor in state.Actors.Values.ToList())
		{
			var current = LifeStageCatalog.GetLifeStage(state, actor);
			var previous = actor.LastResolvedLifeStage;
			if (current == previous)
				continue;

			actor.LastResolvedLifeStage = current;
			if (previous == LifeStage.Infant && current >= LifeStage.Child)
				actor.CarriedByActorId = null;

			events.Add(new GameEvent("lifestage_transition")
			{
				TargetId = actor.Id,
				TargetActorName = actor.DisplayName,
				TargetX = actor.X,
				TargetY = actor.Y,
				TargetZ = actor.Z,
				FailureReason = current.ToString(),
				ActionName = previous.ToString(),
			});
		}

		return events;
	}
}
