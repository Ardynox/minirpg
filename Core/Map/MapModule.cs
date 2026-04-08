using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.World;

namespace MiniRPG.Core.Map;

/// <summary>
/// 地图模块：作为 WorldMap 的代理层，提供与旧 API 兼容的接口。
/// 所有二维签名方法默认使用 state.PlayerZ 作为 Z 坐标。
/// 新代码应优先使用三维签名或直接访问 state.World。
/// </summary>
public static class MapModule
{
	// ══════════════════════════════════════════════════════
	//  地形读写
	// ══════════════════════════════════════════════════════

	public static void SetTerrain(GameState s, int x, int y, string glyph) =>
		SetTerrain(s, x, y, s.PlayerZ, glyph);

	public static void SetTerrain(GameState s, int x, int y, int z, string glyph)
	{
		var stringId = glyph switch
		{
			"#" => Terrains.WallStone,
			"." => Terrains.Floor,
			"~" => Terrains.Water,
			"^" => Terrains.Mountain,
			"T" => Terrains.Tree,
			_ => Terrains.Floor,
		};
		s.World!.SetTerrain(x, y, z, stringId);
	}

	public static string GetTerrain(GameState s, int x, int y) =>
		s.World!.GetTerrain(x, y, s.PlayerZ).Glyph;

	public static string GetTerrain(GameState s, int x, int y, int z) =>
		s.World!.GetTerrain(x, y, z).Glyph;

	// ══════════════════════════════════════════════════════
	//  Fixture 操作
	// ══════════════════════════════════════════════════════

	public static void SetFixture(GameState s, int x, int y, string glyph) =>
		SetFixture(s, x, y, s.PlayerZ, glyph);

	public static void SetFixture(GameState s, int x, int y, int z, string glyph)
	{
		if (string.IsNullOrEmpty(glyph))
		{
			s.World!.SetFixture(x, y, z, string.Empty, string.Empty);
			return;
		}
		var id = GlyphToFixtureId(glyph);
		s.World!.SetFixture(x, y, z, glyph, id);
	}

	public static string GetFixture(GameState s, int x, int y)
	{
		var f = s.World!.GetFirstEntity(x, y, s.PlayerZ, CellEntityType.Fixture);
		return f?.Glyph ?? "";
	}

	public static string GetFixtureId(GameState s, int x, int y) =>
		s.World!.GetFixtureId(x, y, s.PlayerZ);

	public static string GetFixtureId(GameState s, int x, int y, int z) =>
		s.World!.GetFixtureId(x, y, z);

	public static bool HasFixture(GameState s, int x, int y, string fixtureId) =>
		s.World!.HasFixture(x, y, s.PlayerZ, fixtureId);

	public static bool HasFixture(GameState s, int x, int y, int z, string fixtureId) =>
		s.World!.HasFixture(x, y, z, fixtureId);

	// ══════════════════════════════════════════════════════
	//  实体栈操作
	// ══════════════════════════════════════════════════════

	public static List<CellEntity> GetStack(GameState s, int x, int y) =>
		GetStack(s, x, y, s.PlayerZ);

	public static List<CellEntity> GetStack(GameState s, int x, int y, int z) =>
		s.World!.GetEntities(x, y, z);

	public static List<CellEntity> GetByType(GameState s, int x, int y, CellEntityType type) =>
		s.World!.GetEntitiesByType(x, y, s.PlayerZ, type);

	public static CellEntity? GetFirst(GameState s, int x, int y, CellEntityType type) =>
		s.World!.GetFirstEntity(x, y, s.PlayerZ, type);

	public static void Push(GameState s, int x, int y, CellEntity entity) =>
		s.World!.PushEntity(x, y, s.PlayerZ, entity);

	public static bool Remove(GameState s, int x, int y, CellEntityType type, string entityId) =>
		s.World!.RemoveEntity(x, y, s.PlayerZ, type, entityId);

	public static int RemoveByType(GameState s, int x, int y, CellEntityType type) =>
		s.World!.RemoveEntitiesByType(x, y, s.PlayerZ, type);

	// ══════════════════════════════════════════════════════
	//  Item 操作
	// ══════════════════════════════════════════════════════

	public static void PlaceItem(GameState s, int x, int y, Item item) =>
		s.World!.PlaceItem(x, y, s.PlayerZ, item);

	public static void PlaceItem(GameState s, int x, int y, int z, Item item) =>
		s.World!.PlaceItem(x, y, z, item);

	public static List<CellEntity> GetGroundItems(GameState s, int x, int y) =>
		GetByType(s, x, y, CellEntityType.Item);

	public static Item? PickupItem(GameState s, int x, int y, string entityId) =>
		s.World!.PickupItem(x, y, s.PlayerZ, entityId);

	public static Item? PickupItem(GameState s, int x, int y, int z, string entityId) =>
		s.World!.PickupItem(x, y, z, entityId);

	public static List<Item> PeekGroundItems(GameState s, int x, int y) =>
		s.World == null ? [] : s.World.PeekGroundItems(x, y, s.PlayerZ);

	public static List<Item> PeekGroundItems(GameState s, int x, int y, int z) =>
		s.World == null ? [] : s.World.PeekGroundItems(x, y, z);

	public static Item? FindGroundItem(GameState s, int x, int y, int z, string instanceId) =>
		string.IsNullOrWhiteSpace(instanceId)
			? null
			: PeekGroundItems(s, x, y, z).Find(item => string.Equals(item.InstanceId, instanceId, System.StringComparison.Ordinal));

	public static bool UpdateGroundItem(GameState s, int x, int y, Item item) =>
		s.World != null && s.World.UpdateGroundItem(x, y, s.PlayerZ, item);

	public static bool UpdateGroundItem(GameState s, int x, int y, int z, Item item) =>
		s.World != null && s.World.UpdateGroundItem(x, y, z, item);

	// ══════════════════════════════════════════════════════
	//  查询
	// ══════════════════════════════════════════════════════

	/// <summary>无限世界中坐标永远有效——InBounds 始终返回 true。</summary>
	public static bool InBounds(GameState s, int x, int y) => true;

	public static bool IsWall(GameState s, int x, int y) =>
		!s.World!.IsWalkable(x, y, s.PlayerZ);

	public static bool IsWall(GameState s, int x, int y, int z) =>
		!s.World!.IsWalkable(x, y, z);

	public static bool IsWalkable(GameState s, int x, int y) =>
		s.World!.IsWalkable(x, y, s.PlayerZ);

	public static bool IsWalkable(GameState s, int x, int y, int z) =>
		s.World!.IsWalkable(x, y, z);

	public static bool IsHostile(GameState s, int x, int y) =>
		ActorModule.GetHostileAt(s, x, y) != null;

	public static bool IsDoor(GameState s, int x, int y) =>
		HasFixture(s, x, y, Entities.Door);

	public static bool IsDownStair(GameState s, int x, int y) =>
		HasFixture(s, x, y, Entities.StairDown);

	public static bool IsUpStair(GameState s, int x, int y) =>
		HasFixture(s, x, y, Entities.StairUp);

	// ══════════════════════════════════════════════════════
	//  渲染
	// ══════════════════════════════════════════════════════

	public static string GetDisplayCell(GameState s, int x, int y) =>
		s.World!.GetDisplayCell(x, y, s.PlayerZ, s.Actors);

	public static string GetDisplayCell(GameState s, int x, int y, int z) =>
		s.World!.GetDisplayCell(x, y, z, s.Actors);

	// ══════════════════════════════════════════════════════
	//  Z 层导航（替代旧的 GoDownFloor / GoUpFloor）
	// ══════════════════════════════════════════════════════

	/// <summary>下楼：PlayerZ++。无限世界不需要楼层缓存切换。</summary>
	public static void GoDown(GameState s)
	{
		s.PlayerZ++;
		var player = ActorModule.GetPlayer(s);
		if (player != null) player.Z = s.PlayerZ;
	}

	/// <summary>上楼：PlayerZ--。</summary>
	public static bool GoUp(GameState s)
	{
		s.PlayerZ--;
		var player = ActorModule.GetPlayer(s);
		if (player != null) player.Z = s.PlayerZ;
		return true;
	}

	/// <summary>遍历当前 Z 层查找指定 Fixture 并传送玩家。</summary>
	public static void PlacePlayerAtFixture(GameState s, string fixtureId)
	{
		var cx = CoordUtil.WorldToChunk(s.PlayerX, s.PlayerY, s.PlayerZ);
		var r = s.World!.Chunks.LoadRadiusXY;

		for (var radius = 0; radius <= r; radius++)
		{
			for (var cy = cx.Cy - radius; cy <= cx.Cy + radius; cy++)
			for (var ccx = cx.Cx - radius; ccx <= cx.Cx + radius; ccx++)
			{
				if (Math.Abs(ccx - cx.Cx) != radius && Math.Abs(cy - cx.Cy) != radius)
					continue;

				var coord = new ChunkCoord(ccx, cy, s.PlayerZ);
				var chunk = s.World.Chunks.GetOrLoad(coord);
				for (var ly = 0; ly < ChunkData.Size; ly++)
				for (var lx = 0; lx < ChunkData.Size; lx++)
				{
					var entities = chunk.GetEntities(lx, ly);
					if (entities.Any(e => e.Type == CellEntityType.Fixture && e.EntityId == fixtureId))
					{
						var w = CoordUtil.LocalToWorld(coord, lx, ly);
						ActorModule.MoveActor(s, s.PlayerId, w.X, w.Y);
						return;
					}
				}
			}
		}
	}

	// ══════════════════════════════════════════════════════
	//  加载字符串地图（测试用）
	// ══════════════════════════════════════════════════════

	/// <summary>
	/// 从字符串行加载地图到世界的指定 Z 层。
	/// 用于测试和硬编码关卡。将字符写入以 (0,0,z) 为原点的区域。
	/// </summary>
	// ══════════════════════════════════════════════════════
	//  内部工具
	// ══════════════════════════════════════════════════════

	private static string GlyphToFixtureId(string glyph) => glyph switch
	{
		">" => Entities.StairDown,
		"<" => Entities.StairUp,
		"N" => Entities.Nest,
		"D" => Entities.Door,
		"H" => Entities.House,
		"I" => Entities.Item,
		_ => glyph,
	};
}
