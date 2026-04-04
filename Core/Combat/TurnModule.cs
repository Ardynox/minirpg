using System;
using System.Collections.Generic;
using MiniRPG.Core.AI;

namespace MiniRPG.Core.Combat;

/// <summary>
/// 回合推进模块：驱动所有非玩家系统，以及看海模式下的玩家 AI。
/// 纯函数，输入 GameState，输出 GameEvent 列表。
/// </summary>
public static class TurnModule
{
	/// <summary>
	/// 推进一个回合：Turn++，巢穴刷怪，AI 行动。
	/// </summary>
	public static List<GameEvent> Tick(GameState state)
	{
		state.Turn++;
		var viewRange = Math.Max(0, GameConfig.AIVision.ActivationViewRange);

		var events = new List<GameEvent>();
		events.AddRange(NestModule.Tick(state));
		events.AddRange(AIDispatcher.TickAll(
			state, state.PlayerX, state.PlayerY, viewRange));

		return events;
	}

	/// <summary>
	/// 看海模式的完整回合：玩家 AI 行动 + 世界推进。
	/// 返回所有事件（包含玩家的行动事件）。
	/// </summary>
	public static List<GameEvent> TickWatchMode(GameState state)
	{
		var events = new List<GameEvent>();

		var player = ActorModule.GetPlayer(state);
		if (player != null)
		{
			player.BrainId ??= "simple";
			var playerEvents = AIDispatcher.DecideAndExecuteAny(state, player);
			events.AddRange(playerEvents);
		}

		events.AddRange(Tick(state));
		return events;
	}
}
