using System.Collections.Generic;

namespace MiniRPG.Core.World;

/// <summary>
/// 方块放置模块：在空气格子放置指定地形方块。
/// 与 DigModule 互补——挖掘产生空气，放置填充空气。
/// </summary>
public static class BlockPlaceModule
{
	/// <summary>
	/// 尝试在指定位置放置方块。
	/// </summary>
	public static List<GameEvent> TryPlace(GameState state, Actor actor, int tx, int ty, int tz, string terrainStringId)
	{
		var world = state.World;
		if (world == null)
			return [PlaceFailed(actor, "世界未初始化")];

		var current = world.GetTerrain(tx, ty, tz);

		// 只能在空气或非实心格子放置
		if (current.Solid)
			return [PlaceFailed(actor, "目标位置已被占据")];

		if (current.StringId != Terrains.Air && current.StringId != Terrains.Void)
			return [PlaceFailed(actor, "目标位置不是空气")];

		var terrainId = TerrainRegistry.GetId(terrainStringId);
		if (terrainId == 0 && terrainStringId != Terrains.Void)
			return [PlaceFailed(actor, $"未知方块类型: {terrainStringId}")];

		// 检查玩家是否持有对应材料（简化：暂不检查背包）
		world.SetTerrainId(tx, ty, tz, terrainId);
		world.SetHardness(tx, ty, tz, TerrainRegistry.Get(terrainId).DefaultHardness);

		return [new GameEvent("block_placed")
		{
			InitiatorId = actor.Id,
			TargetX = tx,
			TargetY = ty,
			ItemName = terrainStringId,
		}];
	}

	private static GameEvent PlaceFailed(Actor actor, string reason) =>
		new("block_place_failed") { InitiatorId = actor.Id, ItemName = reason };
}
