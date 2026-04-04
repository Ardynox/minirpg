using System.Collections.Generic;
using System.Text;
using MiniRPG.Core.AI;
namespace MiniRPG.Module;

/// <summary>
/// L 键查看：构建环境信息文本。纯查询，无副作用。
/// </summary>
public static class LookModule
{
	public static string BuildLookText(GameState state, FogOfWarTracker fogTracker)
	{
		var sb = new StringBuilder();
		var player = ActorModule.GetPlayer(state);
		sb.Append($"📍 Z{state.PlayerZ} ({state.PlayerX}, {state.PlayerY})  回合: {state.Turn}");
		if (player != null) sb.Append($"  💰{player.Gold}G");

		if (player != null && player.Limbs.Count > 0)
		{
			sb.Append("\n  肢体: ");
			var parts = new List<string>();
			foreach (var l in player.Limbs)
			{
				var vital = l.Tags.ContainsKey("要害") ? "*" : "";
				parts.Add($"{l.Name}{vital}({l.Durability}/{l.MaxDurability})");
			}
			sb.Append(string.Join(" ", parts));
		}

		var standingOn = MapModule.GetFixtureId(state, state.PlayerX, state.PlayerY);
		if (!string.IsNullOrEmpty(standingOn))
			sb.Append($"  脚下: {FixtureLabel(standingOn)}");

		var groundItems = MapModule.PeekGroundItems(state, state.PlayerX, state.PlayerY);
		if (groundItems.Count > 0)
		{
			var names = groundItems.ConvertAll(i => i.Name);
			sb.Append($"\n  📦 地上: {string.Join(", ", names)}  (F 键拾取)");
		}

		var coActors = ActorModule.GetAllAt(state, state.PlayerX, state.PlayerY);
		foreach (var a in coActors)
		{
			if (a.Id == state.PlayerId) continue;
			sb.Append($"  同格: {a.DisplayName}");
		}

		var dirs = new (string Name, int Dx, int Dy)[]
		{
			("上", 0, -1), ("下", 0, 1), ("左", -1, 0), ("右", 1, 0),
		};
		foreach (var (name, dx, dy) in dirs)
		{
			var tx = state.PlayerX + dx;
			var ty = state.PlayerY + dy;
			sb.Append($"  {name}: {CellLabel(state, fogTracker, tx, ty)}");
		}
		return sb.ToString();
	}

	private static string CellLabel(GameState state, FogOfWarTracker fogTracker, int x, int y)
	{
		var band = fogTracker.GetVisionBand(x, y, state.PlayerZ);
		return band switch
		{
			PlayerVisionBand.Focused => FocusedCellLabel(state, x, y),
			PlayerVisionBand.Peripheral => PeripheralCellLabel(state, x, y),
			PlayerVisionBand.Memory => MemoryCellLabel(state, x, y),
			_ => "未知",
		};
	}

	private static string FocusedCellLabel(GameState state, int x, int y)
	{
		if (MapModule.IsWall(state, x, y)) return "墙 🚧";
		var actors = ActorModule.GetAllAt(state, x, y);
		if (actors.Count > 0)
		{
			var names = actors.ConvertAll(a => a.DisplayName);
			return string.Join("+", names);
		}
		var items = MapModule.GetGroundItems(state, x, y);
		if (items.Count > 0) return $"📦{items.Count}个物品";
		var f = MapModule.GetFixtureId(state, x, y);
		if (!string.IsNullOrEmpty(f)) return FixtureLabel(f);
		return "空地";
	}

	private static string PeripheralCellLabel(GameState state, int x, int y)
	{
		if (state.World!.BlocksSight(x, y, state.PlayerZ)) return "障碍";

		var actors = ActorModule.GetAllAt(state, x, y);
		if (actors.Count > 0)
		{
			foreach (var actor in actors)
			{
				if (actor.Id == state.PlayerId) continue;
				if (FactionRelation.IsHostile(Factions.Player, actor.Faction))
					return "敌对身影";
			}

			return "活动身影";
		}

		if (MapModule.GetGroundItems(state, x, y).Count > 0) return "有东西";
		if (!string.IsNullOrEmpty(MapModule.GetFixtureId(state, x, y))) return "有东西";
		return "空地";
	}

	private static string MemoryCellLabel(GameState state, int x, int y) =>
		state.World!.GetTerrain(x, y, state.PlayerZ).Solid ? "记忆中的墙" : "记忆中的空地";

	public static string FixtureLabel(string id) => id switch
	{
		Entities.StairDown => "下行楼梯 ⬇️",
		Entities.StairUp => "上行楼梯 ⬆️",
		Entities.Nest => "巢穴 🕳️",
		Entities.House => "房屋 🏠",
		Entities.Item => "道具 📦",
		Entities.Door => "门 🚪",
		_ => id,
	};
}
