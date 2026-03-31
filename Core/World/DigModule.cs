using System.Collections.Generic;

namespace MiniRPG.Core.World;

/// <summary>
/// 挖掘模块：处理墙体破坏逻辑。
/// 通过交互系统调用——必须有 Actor 发起挖掘行为。
/// 硬度系统：每次挖掘减少目标格的剩余硬度，到 0 时地形变为 rubble。
/// </summary>
public static class DigModule
{
	/// <summary>
	/// 尝试挖掘指定世界坐标的墙体。
	/// 返回事件列表：dig_success(破坏完成) / dig_progress(硬度减少) / dig_failed(不可挖掘)。
	/// </summary>
	public static List<GameEvent> TryDig(GameState state, Actor actor, int tx, int ty, int tz)
	{
		var world = state.World;
		if (world == null) return [DigFailed(actor, "世界未初始化")];

		var terrain = world.GetTerrain(tx, ty, tz);
		if (!terrain.Solid)
			return [DigFailed(actor, "目标不是实心地形")];

		var hardness = world.GetHardness(tx, ty, tz);
		if (hardness == 0)
			return [DigFailed(actor, "该地形不可挖掘")];

		var digPower = CalcDigPower(actor);
		if (digPower <= 0)
			return [DigFailed(actor, "没有足够的挖掘能力")];

		var newHardness = (byte)(hardness > digPower ? hardness - digPower : 0);
		world.SetHardness(tx, ty, tz, newHardness);

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
			}];
		}

		return [new GameEvent("dig_progress")
		{
			InitiatorId = actor.Id,
			TargetX = tx, TargetY = ty,
			Damage = digPower,
		}];
	}

	/// <summary>
	/// 从 Actor 的 tag 和能力计算挖掘力。
	/// "挖掘" tag 值 + manipulation 能力加成。
	/// </summary>
	private static int CalcDigPower(Actor actor)
	{
		var tags = actor.ComputeTags();
		var basePower = tags.GetValueOrDefault("挖掘", 0);

		var caps = actor.ComputeCapacities();
		var manipulation = caps.GetValueOrDefault("manipulation", 0f);
		var bonus = (int)(manipulation * 5);

		return basePower + bonus;
	}

	private static GameEvent DigFailed(Actor actor, string reason) =>
		new("dig_failed") { InitiatorId = actor.Id, ItemName = reason };
}
