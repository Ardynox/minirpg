using System.Collections.Generic;
using MiniRPG.Core.Calendar;
using MiniRPG.Core.Data;
using MiniRPG.Module.Render;

namespace MiniRPG.Core.Demographics;

/// <summary>
/// 世界人口学每 tick 入口。每 turn 调一次（由 <c>TurnModule.AdvanceWorldSystems</c> 接入），
/// 内部用 <see cref="GameState.LastDemographicsDay"/> detect day boundary，
/// 仅当跨过新一天时才触发"按天"子系统（<see cref="ConceptionBirthTick.OnNewDay"/>）。
///
/// Phase 5 / 6 会在这里继续接入 <c>LifeStageTransitionService</c> / <c>InfantCarryModule</c>
/// / <c>NaturalDeathTick</c> / <c>KinGriefHelper</c>，保持单一入口让 TurnModule 干净。
/// </summary>
public static class WorldDemographicsTick
{
	public static List<GameEvent> Tick(GameState state)
	{
		var events = new List<GameEvent>();
		var currentDay = state.Turn / DayNightCycle.TurnsPerDay;
		if (currentDay <= state.LastDemographicsDay)
			return events;

		state.LastDemographicsDay = currentDay;
		events.AddRange(LifeStageTransitionService.OnNewDay(state));
		events.AddRange(InfantCarryModule.OnNewDay(state));
		events.AddRange(ConceptionBirthTick.OnNewDay(state));
		return events;
	}
}
