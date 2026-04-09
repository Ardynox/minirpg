using System.Collections.Generic;
using System.Linq;

namespace MiniRPG.Core.Data;

/// <summary>
/// Actor 增删查改模块：对 GameState.Actors 字典的纯函数操作集合。
/// 三维坐标 (X, Y, Z)。二维签名方法默认匹配当前 PlayerZ。
/// </summary>
public static class ActorModule
{
	public static void Add(GameState state, Actor actor)
	{
		if (!actor.HasHomePosition)
			actor.SetHomePosition(actor.X, actor.Y, actor.Z);
		state.Actors[actor.Id] = actor;
		state.World?.RegisterActor(actor);
	}

	public static void Remove(GameState state, string id)
	{
		if (state.Actors.TryGetValue(id, out var actor))
			state.World?.UnregisterActor(actor);
		state.Actors.Remove(id);
	}

	public static Actor? GetById(GameState state, string id) =>
		state.Actors.GetValueOrDefault(id);

	/// <summary>返回指定 2D 坐标 + 当前 PlayerZ 上的第一个 Actor。</summary>
	public static Actor? GetAt(GameState state, int x, int y) =>
		GetAt(state, x, y, state.PlayerZ);

	public static Actor? GetAt(GameState state, int x, int y, int z) =>
		state.World?.GetActorAt(x, y, z, state.Actors) ?? ScanGetAt(state, x, y, z);

	/// <summary>返回指定 2D 坐标 + 当前 PlayerZ 上的所有 Actor。</summary>
	public static List<Actor> GetAllAt(GameState state, int x, int y) =>
		GetAllAt(state, x, y, state.PlayerZ);

	public static List<Actor> GetAllAt(GameState state, int x, int y, int z) =>
		state.World?.GetActorsAt(x, y, z, state.Actors) ?? ScanGetAllAt(state, x, y, z);

	public static Actor? GetHostileAt(GameState state, int x, int y) =>
		GetHostileAt(state, x, y, state.PlayerZ);

	public static Actor? GetHostileAt(GameState state, int x, int y, int z) =>
		state.World?.GetHostileActorAt(x, y, z, state.Actors) ?? ScanGetHostileAt(state, x, y, z);

	/// <summary>移动 Actor 到新坐标，同时同步 chunk 注册和玩家坐标。</summary>
	public static void MoveActor(GameState state, string id, int nx, int ny)
	{
		if (!state.Actors.TryGetValue(id, out var actor)) return;
		var oldX = actor.X;
		var oldY = actor.Y;
		var oldZ = actor.Z;
		actor.X = nx;
		actor.Y = ny;

		state.World?.UpdateActorChunk(actor, oldX, oldY, oldZ);

		if (id == state.PlayerId)
		{
			state.PlayerX = nx;
			state.PlayerY = ny;
		}
	}

	public static void MoveActor(GameState state, string id, int nx, int ny, int nz)
	{
		if (!state.Actors.TryGetValue(id, out var actor)) return;
		var oldX = actor.X;
		var oldY = actor.Y;
		var oldZ = actor.Z;
		actor.X = nx;
		actor.Y = ny;
		actor.Z = nz;

		state.World?.UpdateActorChunk(actor, oldX, oldY, oldZ);

		if (id == state.PlayerId)
		{
			state.PlayerX = nx;
			state.PlayerY = ny;
			state.PlayerZ = nz;
		}
	}

	public static Actor? GetPlayer(GameState state) =>
		GetById(state, state.PlayerId);

	public static Actor? FindAdjacentHostile(GameState state)
	{
		foreach (var (dx, dy) in GridDirections.Cardinal)
		{
			var hostile = GetHostileAt(state, state.PlayerX + dx, state.PlayerY + dy);
			if (hostile != null) return hostile;
		}
		return null;
	}

	public static void ClearAll(GameState state)
	{
		if (state.World != null)
		{
			foreach (var actor in state.Actors.Values)
				state.World.UnregisterActor(actor);
		}

		state.Actors.Clear();
	}

	public static List<Actor> GetAllHostile(GameState state) =>
		state.Actors.Values.Where(a => a.Faction == Factions.Hostile).ToList();

	public static void InitializeMissingHomePositions(GameState state)
	{
		foreach (var actor in state.Actors.Values)
		{
			if (!actor.HasHomePosition)
				actor.SetHomePosition(actor.X, actor.Y, actor.Z);
		}
	}

	private static Actor? ScanGetAt(GameState state, int x, int y, int z) =>
		state.Actors.Values.FirstOrDefault(actor => actor.X == x && actor.Y == y && actor.Z == z);

	private static List<Actor> ScanGetAllAt(GameState state, int x, int y, int z) =>
		state.Actors.Values
			.Where(actor => actor.X == x && actor.Y == y && actor.Z == z)
			.OrderBy(actor => actor.Id)
			.ToList();

	private static Actor? ScanGetHostileAt(GameState state, int x, int y, int z) =>
		state.Actors.Values.FirstOrDefault(actor =>
			actor.X == x && actor.Y == y && actor.Z == z && actor.Faction == Factions.Hostile);
}
