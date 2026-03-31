using System;
using System.Collections.Generic;

namespace MiniRPG.Core.World;

/// <summary>
/// 地形破坏模块：处理挖掘、伐木、采矿等地形破坏逻辑。
/// 通过交互系统调用——必须有 Actor 发起破坏行为。
/// 硬度系统：每次破坏减少目标格的剩余硬度，到 0 时地形变为 BreaksInto 指定的地形。
/// </summary>
public static class DigModule
{
	/// <summary>
	/// 尝试用指定技能破坏指定世界坐标的地形。
	/// 返回事件列表：dig_success / dig_progress / dig_failed。
	/// </summary>
	public static List<GameEvent> TryDig(GameState state, Actor actor, int tx, int ty, int tz,
		InteractionDef? skill = null)
	{
		var world = state.World;
		if (world == null) return [DigFailed(actor, "世界未初始化")];

		var terrain = world.GetTerrain(tx, ty, tz);
		if (!terrain.Solid)
			return [DigFailed(actor, "目标不是实心地形")];

		var hardness = world.GetHardness(tx, ty, tz);
		if (hardness == 0)
			return [DigFailed(actor, "该地形不可破坏")];

		if (skill != null && skill.TerrainMaterial.Length > 0 &&
			terrain.Material != skill.TerrainMaterial)
			return [DigFailed(actor, $"该技能无法作用于{terrain.Material}材质")];

		var digPower = CalcDigPower(actor, skill);
		if (digPower <= 0)
			return [DigFailed(actor, "没有足够的破坏能力")];

		var newHardness = (byte)(hardness > digPower ? hardness - digPower : 0);
		world.SetHardness(tx, ty, tz, newHardness);

		var skillName = skill?.Name ?? "挖掘";

		if (newHardness == 0)
		{
			var breaksInto = terrain.BreaksInto;
			var rubbleId = TerrainRegistry.GetId(breaksInto);
			if (rubbleId == 0) rubbleId = TerrainRegistry.GetId("rubble");
			world.SetTerrainId(tx, ty, tz, rubbleId);

			return [new GameEvent("dig_success")
			{
				InitiatorId = actor.Id,
				TargetX = tx, TargetY = ty,
				ActionName = skillName,
			}];
		}

		return [new GameEvent("dig_progress")
		{
			InitiatorId = actor.Id,
			TargetX = tx, TargetY = ty,
			Damage = digPower,
			ActionName = skillName,
		}];
	}

	/// <summary>
	/// 检查指定技能是否可以作用于目标地形。
	/// </summary>
	public static bool CanApply(InteractionDef skill, TerrainDef terrain, byte hardness)
	{
		if (!terrain.Solid || hardness == 0) return false;
		if (skill.TerrainMaterial.Length > 0 && terrain.Material != skill.TerrainMaterial)
			return false;
		return true;
	}

	/// <summary>
	/// 计算破坏力。公式：(1 + 技能等级) × manipulation × 5 × (1 + 肢体硬度/10)
	/// - 技能等级：对应 tag（挖掘/伐木/采矿），无则为 0，有手就有基础 1
	/// - manipulation：手的操作能力，受肢体耐久影响
	/// - 肢体硬度：提供 manipulation 的肢体的加权平均材质硬度
	/// </summary>
	private static int CalcDigPower(Actor actor, InteractionDef? skill)
	{
		var tags = actor.ComputeTags();
		var caps = actor.ComputeCapacities();
		var manipulation = caps.GetValueOrDefault("manipulation", 0f);
		if (manipulation <= 0f) return 0;

		var tagKey = skill?.Id switch
		{
			"chop" => "伐木",
			"mine" => "采矿",
			_ => "挖掘",
		};
		var skillLevel = tags.GetValueOrDefault(tagKey, 0);

		var limbHardness = actor.GetLimbHardness("manipulation");

		var power = (1 + skillLevel) * manipulation * 5f * (1f + limbHardness / 10f);
		return Math.Max(1, (int)power);
	}

	private static GameEvent DigFailed(Actor actor, string reason) =>
		new("dig_failed") { InitiatorId = actor.Id, ItemName = reason };
}
