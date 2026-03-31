using System.Collections.Generic;
using System.Text;
using MiniRPG.Core;

namespace MiniRPG.Module;

/// <summary>
/// L 键查看：构建环境信息文本。纯查询，无副作用。
/// </summary>
public static class LookModule
{
	public static string BuildLookText(GameState state)
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
			sb.Append($"  {name}: {CellLabel(state, tx, ty)}");
		}
		return sb.ToString();
	}

	private static string CellLabel(GameState state, int x, int y)
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
