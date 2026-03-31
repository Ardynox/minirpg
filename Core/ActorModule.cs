using System.Collections.Generic;
using System.Linq;

namespace MiniRPG.Core;

/// <summary>
/// Actor 增删查改模块：对 GameState.Actors 字典的纯函数操作集合。
/// Actor 不存在地图格子栈中——位置由 Actor.X/Y 表示，渲染时由 MapModule.GetDisplayCell 动态查询。
///
/// 职责边界：
/// - 只做 CRUD + 简单查询，不包含战斗/AI/交互逻辑。
/// - 战斗 → CombatModule，AI → AIDispatcher，交互 → InteractionModule。
/// </summary>
public static class ActorModule
{
	/// <summary>将 Actor 加入字典。已有同 Id 会被覆盖。</summary>
	public static void Add(GameState state, Actor actor)
	{
		state.Actors[actor.Id] = actor;
	}

	/// <summary>按 Id 移除 Actor。Id 不存在时静默返回。</summary>
	public static void Remove(GameState state, string id)
	{
		state.Actors.Remove(id);
	}

	/// <summary>按 Id 查找 Actor，不存在则返回 null。</summary>
	public static Actor? GetById(GameState state, string id) =>
		state.Actors.GetValueOrDefault(id);

	/// <summary>返回指定坐标上的第一个 Actor。同格有多个时结果不稳定。</summary>
	// REVIEW: Dictionary.Values 的遍历顺序不保证稳定。
	//         如果需要确定性，应排序后取第一个。
	public static Actor? GetAt(GameState state, int x, int y) =>
		state.Actors.Values.FirstOrDefault(a => a.X == x && a.Y == y);

	/// <summary>返回指定坐标上的所有 Actor（可能为空列表）。</summary>
	// REVIEW: 每次调用都遍历整个 Actors 字典并创建新 List，O(n) 且产生 GC 压力。
	//         如果地图上 Actor 数量增多，考虑维护空间索引（如坐标 → Actor 列表的字典）。
	public static List<Actor> GetAllAt(GameState state, int x, int y) =>
		state.Actors.Values.Where(a => a.X == x && a.Y == y).ToList();

	/// <summary>返回指定坐标上的第一个敌对 Actor。</summary>
	public static Actor? GetHostileAt(GameState state, int x, int y) =>
		state.Actors.Values.FirstOrDefault(a =>
			a.X == x && a.Y == y && a.Faction == Factions.Hostile);

	/// <summary>移动 Actor 到新坐标，同时同步 GameState.PlayerX/Y（如果是玩家）。</summary>
	public static void MoveActor(GameState state, string id, int nx, int ny)
	{
		if (!state.Actors.TryGetValue(id, out var actor))
			return;
		actor.X = nx;
		actor.Y = ny;

		if (id == state.PlayerId)
		{
			state.PlayerX = nx;
			state.PlayerY = ny;
		}
	}

	/// <summary>获取玩家 Actor 的快捷方法。</summary>
	public static Actor? GetPlayer(GameState state) =>
		GetById(state, state.PlayerId);

	/// <summary>在玩家四方向相邻格中查找第一个敌对 Actor。</summary>
	// REVIEW: 硬编码使用 state.PlayerX/Y 而非接受参数，
	//         与其他方法接受 (state, actor) 的模式不一致。
	//         只能用于玩家，AI 无法复用。
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

	/// <summary>清空所有 Actor（新地图生成前调用）。</summary>
	public static void ClearAll(GameState state) => state.Actors.Clear();

	/// <summary>获取所有敌对阵营的 Actor。</summary>
	public static List<Actor> GetAllHostile(GameState state) =>
		state.Actors.Values.Where(a => a.Faction == Factions.Hostile).ToList();

}
