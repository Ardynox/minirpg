using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Config;
using MiniRPG.Core.Trade;
using MiniRPG.Core.World;

namespace MiniRPG.Core.Data;

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
			CheckTargetRequirements(target, tTags, d)
		).ToList();
	}

	/// <summary>执行 Actor↔Actor 交互，产出事件。具体效果由 Main.Dispatch 消费。</summary>
	public static List<GameEvent> Execute(GameState state, Actor initiator, Actor target,
		InteractionDef def)
	{
		var events = new List<GameEvent>();
		var evt = new GameEvent("interaction");
		IdentificationModule.PopulateInitiatorIdentity(evt, state, initiator);
		IdentificationModule.PopulateTargetIdentity(evt, state, target);
		evt.InteractionDefId = def.Id;
		evt.InteractionName = def.Name;
		evt.EffectType = def.EffectType;

		switch (def.EffectType)
		{
			case "tame":
				target.Faction = Factions.Friendly;
				var targetName = IdentificationModule.GetActorDisplayName(state, target);
				initiator.Experiences.Add(new Experience
				{
					Id = $"tamed_{target.Id}",
					Name = LocalizationService.T("runtime.experience.tamed_actor.name", ("actor", targetName)),
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
		{
			var failedEvent = new GameEvent("drop_failed");
			IdentificationModule.PopulateInitiatorIdentity(failedEvent, state, actor);
			IdentificationModule.PopulateItemIdentity(failedEvent, state, item);
			return [failedEvent];
		}

		actor.Inventory.RemoveAt(inventoryIndex);
		state.World!.PlaceItem(actor.X, actor.Y, state.PlayerZ, item);

		var droppedEvent = new GameEvent("item_dropped")
		{
			TargetX = actor.X,
			TargetY = actor.Y,
		};
		IdentificationModule.PopulateInitiatorIdentity(droppedEvent, state, actor);
		IdentificationModule.PopulateItemIdentity(droppedEvent, state, item);
		return [droppedEvent];
	}

	// ══════════════════════════════════════════════════════
	//  物品拾取（地面 → 背包）
	// ══════════════════════════════════════════════════════

	/// <summary>
	/// 从脚下格子栈拾取指定 EntityId 的物品放入 Actor 背包。
	/// </summary>
	public static List<GameEvent> PickupItem(GameState state, Actor actor, string itemEntityId)
	{
		var picked = state.World!.PickupItem(actor.X, actor.Y, state.PlayerZ, itemEntityId);
		if (picked == null)
		{
			var failedEvent = new GameEvent("pickup_failed");
			IdentificationModule.PopulateInitiatorIdentity(failedEvent, state, actor);
			return [failedEvent];
		}

		InventoryModule.Add(actor, picked);

		var pickedUpEvent = new GameEvent("item_picked_up")
		{
			TargetX = actor.X,
			TargetY = actor.Y,
		};
		IdentificationModule.PopulateInitiatorIdentity(pickedUpEvent, state, actor);
		IdentificationModule.PopulateItemIdentity(pickedUpEvent, state, picked);
		return [pickedUpEvent];
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

	private static bool CheckTargetRequirements(
		Actor target,
		Dictionary<string, int> targetTags,
		InteractionDef def)
	{
		if (CheckTags(targetTags, def.TargetRequired, target.Faction))
			return true;

		// Trade should stay available for actors that actually have sellable goods,
		// even when imported preset snapshots are missing the legacy trade tag.
		return string.Equals(def.EffectType, "trade", StringComparison.Ordinal)
			&& TradeModule.ListGoods(target).Count > 0;
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
