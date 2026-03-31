using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.World;

namespace MiniRPG.Core;

/// <summary>
/// 交互模块：检测可交互目标、过滤可用交互、执行交互产出事件。
/// 交互范围包括：Actor↔Actor 交互、Actor→Cell 交互（挖掘等）、物品拾取、物品丢弃。
/// </summary>
public static class InteractionModule
{
	private static readonly (int Dx, int Dy)[] Dirs = [(0, -1), (0, 1), (-1, 0), (1, 0), (0, 0)];

	// ══════════════════════════════════════════════════════
	//  目标扫描
	// ══════════════════════════════════════════════════════

	/// <summary>扫描指定 Actor 相邻 + 脚下的所有其他 Actor。</summary>
	public static List<Actor> GetAvailableTargets(GameState state, Actor initiator)
	{
		var targets = new List<Actor>();
		foreach (var (dx, dy) in Dirs)
		{
			var x = initiator.X + dx;
			var y = initiator.Y + dy;
			foreach (var actor in ActorModule.GetAllAt(state, x, y))
			{
				if (actor.Id != initiator.Id)
					targets.Add(actor);
			}
		}
		return targets;
	}

	// ══════════════════════════════════════════════════════
	//  Actor↔Actor 交互
	// ══════════════════════════════════════════════════════

	/// <summary>根据双方 tag/能力过滤出满足条件的交互列表。</summary>
	public static List<InteractionDef> GetInteractions(Actor initiator, Actor target,
		IReadOnlyList<InteractionDef> allDefs)
	{
		var iTags = initiator.ComputeTags();
		var iCaps = initiator.ComputeCapacities();
		var tTags = target.ComputeTags();
		return allDefs.Where(d =>
			CheckTags(iTags, d.Required, initiator.Faction) &&
			CheckCapacities(iCaps, d.CapacityRequired) &&
			CheckTags(tTags, d.TargetRequired, target.Faction)
		).ToList();
	}

	/// <summary>执行 Actor↔Actor 交互，产出事件。具体效果由 Main.Dispatch 消费。</summary>
	public static List<GameEvent> Execute(GameState state, Actor initiator, Actor target,
		InteractionDef def)
	{
		var events = new List<GameEvent>();
		var evt = new GameEvent("interaction")
		{
			InitiatorId = initiator.Id,
			TargetId = target.Id,
			TargetActorName = target.DisplayName,
		};
		evt.InteractionDefId = def.Id;
		evt.InteractionName = def.Name;
		evt.EffectType = def.EffectType;

		switch (def.EffectType)
		{
			case "tame":
				target.Faction = Factions.Friendly;
				initiator.Experiences.Add(new Experience
				{
					Id = $"tamed_{target.Id}",
					Name = $"驯服了{target.DisplayName}",
					Tags = new() { ["驯服经验"] = 1 },
				});
				break;
			case "combat":
				break;
		}

		events.Add(evt);
		return events;
	}

	// ══════════════════════════════════════════════════════
	//  物品丢弃（背包 → 地面）
	// ══════════════════════════════════════════════════════

	/// <summary>
	/// 将 Actor 背包中指定下标的物品丢弃到脚下格子栈。
	/// 已装备的物品不允许直接丢弃，需先卸下。
	/// </summary>
	public static List<GameEvent> DropItem(GameState state, Actor actor, int inventoryIndex)
	{
		if (inventoryIndex < 0 || inventoryIndex >= actor.Inventory.Count)
			return [];

		var item = actor.Inventory[inventoryIndex];
		if (item.Equipped)
			return [new GameEvent("drop_failed") { InitiatorId = actor.Id, ItemName = item.Name }];

		actor.Inventory.RemoveAt(inventoryIndex);
		MapModule.PlaceItem(state, actor.X, actor.Y, item);

		return [new GameEvent("item_dropped")
		{
			InitiatorId = actor.Id,
			ItemName = item.Name,
			TargetX = actor.X,
			TargetY = actor.Y,
		}];
	}

	// ══════════════════════════════════════════════════════
	//  物品拾取（地面 → 背包）
	// ══════════════════════════════════════════════════════

	/// <summary>
	/// 从脚下格子栈拾取指定 EntityId 的物品放入 Actor 背包。
	/// </summary>
	public static List<GameEvent> PickupItem(GameState state, Actor actor, string itemEntityId)
	{
		var picked = MapModule.PickupItem(state, actor.X, actor.Y, itemEntityId);
		if (picked == null)
			return [new GameEvent("pickup_failed") { InitiatorId = actor.Id }];

		InventoryModule.Add(actor, picked);

		return [new GameEvent("item_picked_up")
		{
			InitiatorId = actor.Id,
			ItemName = picked.Name,
			TargetX = actor.X,
			TargetY = actor.Y,
		}];
	}

	// ══════════════════════════════════════════════════════
	//  Actor→Cell 交互（挖掘等）
	// ══════════════════════════════════════════════════════

	/// <summary>扫描 Actor 相邻四方向中指定技能可作用的地形格。</summary>
	public static List<(int X, int Y, int Z, string TerrainName)> GetBreakableNeighbors(
		GameState state, Actor actor, InteractionDef skill)
	{
		var results = new List<(int, int, int, string)>();
		if (state.World == null) return results;

		var adjacentDirs = new (int Dx, int Dy)[] { (0, -1), (0, 1), (-1, 0), (1, 0) };
		foreach (var (dx, dy) in adjacentDirs)
		{
			var tx = actor.X + dx;
			var ty = actor.Y + dy;
			var tz = actor.Z;
			var terrain = state.World.GetTerrain(tx, ty, tz);
			var hardness = state.World.GetHardness(tx, ty, tz);
			if (DigModule.CanApply(skill, terrain, hardness))
				results.Add((tx, ty, tz, terrain.StringId));
		}
		return results;
	}

	/// <summary>扫描 Actor 相邻四方向的所有可破坏地形格（不限材质）。</summary>
	public static List<(int X, int Y, int Z, string TerrainName)> GetDiggableNeighbors(
		GameState state, Actor actor)
	{
		var results = new List<(int, int, int, string)>();
		if (state.World == null) return results;

		var adjacentDirs = new (int Dx, int Dy)[] { (0, -1), (0, 1), (-1, 0), (1, 0) };
		foreach (var (dx, dy) in adjacentDirs)
		{
			var tx = actor.X + dx;
			var ty = actor.Y + dy;
			var tz = actor.Z;
			var terrain = state.World.GetTerrain(tx, ty, tz);
			if (terrain.Solid && state.World.GetHardness(tx, ty, tz) > 0)
				results.Add((tx, ty, tz, terrain.StringId));
		}
		return results;
	}

	/// <summary>执行 Actor→Cell 的地形破坏交互（挖掘/伐木/采矿等）。</summary>
	public static List<GameEvent> ExecuteDig(GameState state, Actor actor, int tx, int ty, int tz,
		InteractionDef? skill = null) =>
		DigModule.TryDig(state, actor, tx, ty, tz, skill);

	// ══════════════════════════════════════════════════════
	//  内部工具
	// ══════════════════════════════════════════════════════

	private static bool CheckCapacities(Dictionary<string, float> caps,
		Dictionary<string, float> required)
	{
		foreach (var (key, val) in required)
			if (caps.GetValueOrDefault(key, 0f) < val) return false;
		return true;
	}

	private static bool CheckTags(Dictionary<string, int> tags, Dictionary<string, int> requirements,
		string faction)
	{
		foreach (var (key, val) in requirements)
		{
			if (key.StartsWith("@faction:"))
			{
				var requiredFaction = key["@faction:".Length..];
				if (faction != requiredFaction) return false;
			}
			else if (key.StartsWith("@max:"))
			{
				var parts = key["@max:".Length..].Split(':');
				if (parts.Length != 2 || !int.TryParse(parts[1], out var maxVal)) return false;
				if (tags.GetValueOrDefault(parts[0], 0) > maxVal) return false;
			}
			else
			{
				if (tags.GetValueOrDefault(key, 0) < val) return false;
			}
		}
		return true;
	}
}
