using System.Collections.Generic;
using System.Linq;

namespace MiniRPG.Core;

/// <summary>
/// Actor 的增删查改：纯函数操作 GameState.Actors + 同步 Objects 层。
/// 多个 Actor 可以重叠在同一格，Objects 层显示优先级最高的 Glyph。
/// </summary>
public static class ActorModule
{
	public static void Add(GameState state, Actor actor)
	{
		state.Actors[actor.Id] = actor;
		RefreshObjectsCell(state, actor.X, actor.Y);
	}

	public static void Remove(GameState state, string id)
	{
		if (!state.Actors.TryGetValue(id, out var actor))
			return;
		state.Actors.Remove(id);
		RefreshObjectsCell(state, actor.X, actor.Y);
	}

	public static Actor? GetById(GameState state, string id) =>
		state.Actors.GetValueOrDefault(id);

	public static Actor? GetAt(GameState state, int x, int y) =>
		state.Actors.Values.FirstOrDefault(a => a.X == x && a.Y == y);

	/// <summary>返回指定坐标上的所有 Actor。</summary>
	public static List<Actor> GetAllAt(GameState state, int x, int y) =>
		state.Actors.Values.Where(a => a.X == x && a.Y == y).ToList();

	public static Actor? GetHostileAt(GameState state, int x, int y) =>
		state.Actors.Values.FirstOrDefault(a =>
			a.X == x && a.Y == y && a.Faction == "hostile");

	public static void MoveActor(GameState state, string id, int nx, int ny)
	{
		if (!state.Actors.TryGetValue(id, out var actor))
			return;
		var ox = actor.X;
		var oy = actor.Y;
		actor.X = nx;
		actor.Y = ny;
		RefreshObjectsCell(state, ox, oy);
		RefreshObjectsCell(state, nx, ny);

		if (id == state.PlayerId)
		{
			state.PlayerX = nx;
			state.PlayerY = ny;
		}
	}

	/// <summary>
	/// 重算一格的 Objects 层显示。优先级：player > hostile > 其他。
	/// </summary>
	public static void RefreshObjectsCell(GameState state, int x, int y)
	{
		var actors = GetAllAt(state, x, y);
		if (actors.Count == 0)
		{
			MapModule.SetObject(state, x, y, "");
			return;
		}

		var best = actors[0];
		foreach (var a in actors)
		{
			if (a.Id == state.PlayerId) { best = a; break; }
			if (a.Faction == "hostile" && best.Faction != "hostile") best = a;
		}
		MapModule.SetObject(state, x, y, best.Glyph);
	}

	public static Actor? GetPlayer(GameState state) =>
		GetById(state, state.PlayerId);

	public static Actor? FindAdjacentHostile(GameState state)
	{
		var dirs = new[] { (0, -1), (0, 1), (-1, 0), (1, 0) };
		foreach (var (dx, dy) in dirs)
		{
			var hostile = GetHostileAt(state, state.PlayerX + dx, state.PlayerY + dy);
			if (hostile != null) return hostile;
		}
		return null;
	}

	public static void ClearAll(GameState state) => state.Actors.Clear();

	public static List<Actor> GetAllHostile(GameState state) =>
		state.Actors.Values.Where(a => a.Faction == "hostile").ToList();

	public static PlayerStatus? GetPlayerStatus(GameState state)
	{
		var player = GetPlayer(state);
		if (player == null) return null;
		var tags = player.ComputeTags();
		return new PlayerStatus
		{
			Tags = tags,
			Hp = tags.GetValueOrDefault("生命", 0),
			Atk = tags.GetValueOrDefault("力量", 0),
			Def = tags.GetValueOrDefault("防御", 0),
			AvailableActions = ActionQuery.GetAvailable(player, ActionDefs.All),
		};
	}
}

public class PlayerStatus
{
	public Dictionary<string, int> Tags { get; set; } = new();
	public int Hp { get; set; }
	public int Atk { get; set; }
	public int Def { get; set; }
	public List<ActionDef> AvailableActions { get; set; } = [];
}
