using System;
using System.Collections.Generic;
using MiniRPG.Core;

namespace MiniRPG.Module;

/// <summary>
/// 处理 / 前缀的 debug 文本命令。纯逻辑，不持有 UI 引用。
/// </summary>
public static class DebugCommandHandler
{
	public struct Result
	{
		public List<string> Logs;
		public bool NeedsFlush;
	}

	public static Result Handle(string cmd, GameState state, GameSessionModule session)
	{
		var logs = new List<string>();
		var flush = false;

		var player = ActorModule.GetPlayer(state);
		if (player == null) { logs.Add("[debug] 无玩家"); return new Result { Logs = logs }; }

		var parts = cmd.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
		var verb = parts[0].ToLowerInvariant();
		var arg = parts.Length > 1 ? parts[1].Trim() : "";

		switch (verb)
		{
			case "/chest":
				var count = DebugModule.SpawnChest(state, state.PlayerX, state.PlayerY);
				logs.Add($"[debug] 宝箱已生成，含 {count} 件装备。按 F 打开");
				flush = true;
				break;

			case "/gold":
				var gold = int.TryParse(arg, out var g) ? g : 1000;
				DebugModule.GiveGold(player, gold);
				logs.Add($"[debug] +{gold}G (总计: {player.Gold}G)");
				break;

			case "/heal":
				DebugModule.HealAll(player);
				logs.Add("[debug] 所有肢体已恢复满耐久");
				break;

			case "/spawn":
				if (string.IsNullOrEmpty(arg))
				{
					var ids = DebugModule.GetMonsterTemplateIds();
					logs.Add($"[debug] 可用模板: {string.Join(", ", ids)}");
					break;
				}
				var sx = state.PlayerX + player.FacingX;
				var sy = state.PlayerY + player.FacingY;
				var spawned = DebugModule.SpawnEnemy(state, arg, sx, sy);
				if (spawned != null)
				{
					logs.Add($"[debug] 已生成 {spawned.DisplayName} 在 ({sx},{sy})");
					flush = true;
				}
				else
				{
					logs.Add($"[debug] 未知模板: {arg}");
				}
				break;

			case "/god":
				var on = DebugModule.ToggleGodMode(player);
				logs.Add($"[debug] 无敌模式: {(on ? "开启" : "关闭")}");
				break;

			case "/down":
				session.ChangeFloor(goDown: true);
				logs.Add($"[debug] 已传送到第 {state.PlayerZ} 层");
				flush = true;
				break;

			default:
				logs.Add("[debug] 可用命令: /chest /gold /heal /spawn /god /down");
				break;
		}

		return new Result { Logs = logs, NeedsFlush = flush };
	}
}
