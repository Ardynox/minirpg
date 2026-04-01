using System;
using System.Collections.Generic;
using MiniRPG.Core.World;
using MiniRPG.Module;

namespace MiniRPG.Core;

/// <summary>
/// Debug/作弊模块：命令解析 + 执行。
/// HandleCommand 解析 "/" 前缀文本命令，直接调用内部方法执行。
/// </summary>
public static class DebugModule
{
	public struct Result
	{
		public List<string> Logs;
		public bool NeedsFlush;
	}

	private const string GodBuffId = "debug_godmode";

	// ── 命令路由 ──────────────────────────────────────────

	public static Result HandleCommand(string cmd, GameState state, GameSessionModule session)
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
				var count = SpawnChest(state, state.PlayerX, state.PlayerY);
				logs.Add($"[debug] 宝箱已生成，含 {count} 件装备。按 F 打开");
				flush = true;
				break;

			case "/gold":
				var gold = int.TryParse(arg, out var g) ? g : 1000;
				player.Gold += gold;
				logs.Add($"[debug] +{gold}G (总计: {player.Gold}G)");
				break;

			case "/heal":
				HealAll(player);
				logs.Add("[debug] 所有肢体已恢复满耐久");
				break;

			case "/spawn":
				if (string.IsNullOrEmpty(arg))
				{
					var ids = GetMonsterTemplateIds();
					logs.Add($"[debug] 可用模板: {string.Join(", ", ids)}");
					break;
				}
				var sx = state.PlayerX + player.FacingX;
				var sy = state.PlayerY + player.FacingY;
				var spawned = SpawnEnemy(state, arg, sx, sy);
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
				var on = ToggleGodMode(player);
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

	// ── 具体功能 ──────────────────────────────────────────

	public static int SpawnChest(GameState state, int x, int y)
	{
		if (state.World == null) return 0;

		var chest = PresetDB.CloneItem("chest_wooden");
		chest.Name = "调试宝箱";

		var count = 0;
		foreach (var preset in PresetDB.Items.Values)
		{
			if (preset.Id == "chest_wooden") continue;
			var item = PresetDB.CloneItem(preset.Id);
			if (item.IsEquippable || item.Category is ItemCategories.Weapon or ItemCategories.Armor or ItemCategories.Tool)
			{
				chest.Contents!.Add(item);
				count++;
			}
		}

		state.World.PlaceItem(x, y, state.PlayerZ, chest);
		return count;
	}

	public static void HealAll(Actor actor)
	{
		foreach (var limb in actor.Limbs)
			limb.Durability = limb.MaxDurability;
	}

	public static Actor? SpawnEnemy(GameState state, string templateId, int x, int y)
	{
		if (!PresetDB.Actors.ContainsKey(templateId)) return null;

		var id = $"debug_{templateId}_{state.Turn}_{x}_{y}";
		var actor = ActorTemplates.Spawn(templateId, id);
		actor.X = x;
		actor.Y = y;
		actor.Z = state.PlayerZ;
		ActorModule.Add(state, actor);
		return actor;
	}

	public static bool ToggleGodMode(Actor actor)
	{
		var existing = actor.Buffs.Find(b => b.Id == GodBuffId);
		if (existing != null)
		{
			actor.Buffs.Remove(existing);
			return false;
		}

		actor.Buffs.Add(new Buff
		{
			Id = GodBuffId,
			Name = "无敌",
			RemainingTurns = -1,
			Tags = new Dictionary<string, int>(),
		});
		return true;
	}

	public static List<string> GetMonsterTemplateIds()
	{
		var ids = new List<string>();
		foreach (var (id, preset) in PresetDB.Actors)
			if (preset.Faction == Factions.Hostile)
				ids.Add(id);
		return ids;
	}
}
