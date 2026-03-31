using System.Collections.Generic;
using MiniRPG.Core.World;

namespace MiniRPG.Core;

/// <summary>
/// Debug/作弊模块：纯静态函数，供文本命令调用。
/// </summary>
public static class DebugModule
{
	private const string GodBuffId = "debug_godmode";

	/// <summary>在指定位置放置宝箱（容器物品）+ 所有可装备/武器/工具物品。</summary>
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

	/// <summary>给 Actor 加金币。</summary>
	public static void GiveGold(Actor actor, int amount)
	{
		actor.Gold += amount;
	}

	/// <summary>恢复所有肢体耐久到满。</summary>
	public static void HealAll(Actor actor)
	{
		foreach (var limb in actor.Limbs)
			limb.Durability = limb.MaxDurability;
	}

	/// <summary>在指定位置生成怪物。返回生成的 Actor 或 null。</summary>
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

	/// <summary>切换无敌模式（永久高防御 Buff）。返回 true = 开启, false = 关闭。</summary>
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

	/// <summary>获取所有怪物模板 ID 列表。</summary>
	public static List<string> GetMonsterTemplateIds()
	{
		var ids = new List<string>();
		foreach (var (id, preset) in PresetDB.Actors)
			if (preset.Faction == Factions.Hostile)
				ids.Add(id);
		return ids;
	}
}
