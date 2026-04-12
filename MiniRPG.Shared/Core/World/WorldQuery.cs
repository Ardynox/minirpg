using System.Collections.Generic;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.World;

/// <summary>
/// 世界查询服务：组合地形/实体/设施/Actor 数据的只读查询。
/// </summary>
public class WorldQuery
{
	private readonly TerrainAccess _terrain;
	private readonly EntityAccess _entities;
	private readonly ActorIndex _actors;
	private readonly FacilityIndex _facilities;

	public WorldQuery(TerrainAccess terrain, EntityAccess entities, ActorIndex actors, FacilityIndex facilities)
	{
		_terrain = terrain;
		_entities = entities;
		_actors = actors;
		_facilities = facilities;
	}

	public bool IsSolid(int x, int y, int z) =>
		_terrain.GetTerrain(x, y, z).Solid;

	/// <summary>
	/// 共享视线遮挡真相：地形沿用 Solid，fixture 可覆写是否挡视线。
	/// </summary>
	public bool BlocksSight(int x, int y, int z)
	{
		if (_terrain.GetTerrain(x, y, z).Solid)
			return true;

		if (_facilities.BlocksSight(x, y, z))
			return true;

		foreach (var entity in _entities.GetEntities(x, y, z))
		{
			if (entity.Type != CellEntityType.Fixture) continue;
			if (EntityAccess.FixtureBlocksSight(entity))
				return true;
		}

		return false;
	}

	public bool IsWalkable(int x, int y, int z) =>
		!IsSolid(x, y, z) && _facilities.IsPassable(x, y, z);

	/// <summary>
	/// 该格是否暴露在天空下：自身非实心，且向上（z-1 方向）无实心方块遮挡。
	/// </summary>
	public bool IsWeatherExposed(int x, int y, int z)
	{
		if (BlocksSight(x, y, z))
			return false;

		const int maxScanLayers = 16;
		for (var checkZ = z - 1; checkZ >= z - maxScanLayers; checkZ--)
		{
			var id = _terrain.GetTerrainId(x, y, checkZ);
			if (id == 0) return true; // void = world boundary = sky
			if (TerrainRegistry.Get(id).Solid)
				return false;
		}

		return true;
	}

	public bool CanTraverseVertical(int x, int y, int z, bool goDown)
	{
		var targetZ = goDown ? z + 1 : z - 1;
		return _entities.HasVerticalAnchor(x, y, z, goDown) && IsWalkable(x, y, targetZ);
	}

	/// <summary>
	/// 渲染用：获取格子的显示字符。
	/// 优先显示 Actor（player > hostile > 其他），否则显示栈顶实体 Glyph，否则显示地形 Glyph。
	/// </summary>
	public string GetDisplayCell(int x, int y, int z, Dictionary<string, Actor> actors)
	{
		var actor = GetDisplayActor(x, y, z, actors);
		if (actor != null) return actor.Glyph;

		var entities = _entities.GetEntities(x, y, z);
		if (entities.Count > 0)
		{
			var top = entities[^1];
			if (_facilities.OccupiesCell(x, y, z)
				&& top.Type is CellEntityType.Item or CellEntityType.Container)
			{
				return _facilities.GetGlyph(x, y, z);
			}

			return top.Glyph;
		}

		var facilityGlyph = _facilities.GetGlyph(x, y, z);
		if (facilityGlyph.Length > 0)
			return facilityGlyph;

		return _terrain.GetTerrain(x, y, z).Glyph;
	}

	/// <summary>该格是否有 Actor 或非地形实体。</summary>
	public bool HasActorOrEntity(int x, int y, int z, Dictionary<string, Actor> actors)
	{
		return _actors.GetActorsAt(x, y, z, actors).Count > 0
			|| _entities.GetEntities(x, y, z).Count > 0
			|| _facilities.OccupiesCell(x, y, z);
	}

	private Actor? GetDisplayActor(int x, int y, int z, Dictionary<string, Actor> actors)
	{
		var occupants = _actors.GetActorsAt(x, y, z, actors);
		Actor? best = null;
		foreach (var a in occupants)
		{
			if (best == null) { best = a; continue; }
			if (a.Faction == Factions.Player) { best = a; break; }
			if (a.Faction == Factions.Hostile && best.Faction != Factions.Hostile) best = a;
		}
		return best;
	}
}
