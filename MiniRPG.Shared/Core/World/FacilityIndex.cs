using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.World;

/// <summary>
/// 设施占用索引服务：管理已建造设施在世界中的空间占用和查询。
/// </summary>
public class FacilityIndex
{
	private readonly record struct FacilityOccupancy(
		string FacilityId,
		FacilityFootprintCell Cell,
		bool BlocksSight,
		bool Passable);

	private IReadOnlyDictionary<string, FacilityInstance>? _facilities;
	private readonly Dictionary<ZoneCell, FacilityOccupancy> _index = new();

	public void AttachFacilityState(IReadOnlyDictionary<string, FacilityInstance>? facilities)
	{
		_facilities = facilities;
		RebuildIndex();
	}

	public void RebuildIndex()
	{
		_index.Clear();
		if (_facilities == null || _facilities.Count == 0)
			return;

		foreach (var facility in _facilities.Values.OrderBy(static f => f.Id, System.StringComparer.Ordinal))
		{
			var def = FacilityRegistry.Get(facility.FacilityDefId);
			if (def == null)
				continue;

			foreach (var (worldX, worldY, _, footprintCell) in EnumerateFootprint(def, facility.AnchorX, facility.AnchorY, facility.Z, facility.Rotation))
			{
				_index[new ZoneCell(worldX, worldY, facility.Z)] = new FacilityOccupancy(
					facility.Id,
					footprintCell,
					BlocksSight: ShouldBlockSight(facility, footprintCell),
					Passable: IsPassable(facility, footprintCell));
			}
		}
	}

	public bool TryGetFacilityAt(int x, int y, int z, out FacilityInstance? facility)
	{
		if (TryGetOccupancy(x, y, z, out var occupancy)
			&& _facilities != null
			&& _facilities.TryGetValue(occupancy.FacilityId, out var resolved))
		{
			facility = resolved;
			return true;
		}

		facility = null;
		return false;
	}

	public bool TryGetFacilityAt(int x, int y, int z, out FacilityInstance? facility, out FacilityFootprintCell? footprintCell)
	{
		if (TryGetOccupancy(x, y, z, out var occupancy)
			&& _facilities != null
			&& _facilities.TryGetValue(occupancy.FacilityId, out var resolved))
		{
			facility = resolved;
			footprintCell = occupancy.Cell.Clone();
			return true;
		}

		facility = null;
		footprintCell = null;
		return false;
	}

	public bool OccupiesCell(int x, int y, int z) =>
		_index.ContainsKey(new ZoneCell(x, y, z));

	public bool BlocksSight(int x, int y, int z) =>
		TryGetOccupancy(x, y, z, out var o) && o.BlocksSight;

	public bool IsPassable(int x, int y, int z) =>
		!TryGetOccupancy(x, y, z, out var o) || o.Passable;

	public string GetGlyph(int x, int y, int z) =>
		TryGetOccupancy(x, y, z, out var o) ? o.Cell.Glyph : "";

	public List<ZoneCell> GetFootprintCells(FacilityInstance facility)
	{
		var def = FacilityRegistry.Get(facility.FacilityDefId);
		return def == null
			? []
			: GetFootprintCells(def, facility.AnchorX, facility.AnchorY, facility.Z, facility.Rotation);
	}

	public List<ZoneCell> GetFootprintCells(FacilityDef def, int anchorX, int anchorY, int z, FacilityRotation rotation) =>
		EnumerateFootprint(def, anchorX, anchorY, z, rotation)
			.Select(static cell => new ZoneCell(cell.WorldX, cell.WorldY, cell.WorldZ))
			.ToList();

	public List<ZoneCell> GetBlockers(FacilityDef def, int anchorX, int anchorY, int z, FacilityRotation rotation,
		TerrainAccess terrain, EntityAccess entities)
	{
		var blockers = new List<ZoneCell>();
		foreach (var (worldX, worldY, worldZ, _) in EnumerateFootprint(def, anchorX, anchorY, z, rotation))
		{
			if (terrain.GetTerrain(worldX, worldY, worldZ).Solid)
			{
				blockers.Add(new ZoneCell(worldX, worldY, worldZ));
				continue;
			}

			if (TryGetFacilityAt(worldX, worldY, worldZ, out _))
			{
				blockers.Add(new ZoneCell(worldX, worldY, worldZ));
				continue;
			}

			if (entities.GetEntities(worldX, worldY, worldZ).Any(static entity =>
				entity.Type is CellEntityType.Fixture or CellEntityType.Container or CellEntityType.Hazard or CellEntityType.Corpse))
			{
				blockers.Add(new ZoneCell(worldX, worldY, worldZ));
			}
		}

		return blockers;
	}

	public bool CanPlace(FacilityDef def, int anchorX, int anchorY, int z, FacilityRotation rotation,
		TerrainAccess terrain, EntityAccess entities) =>
		GetBlockers(def, anchorX, anchorY, z, rotation, terrain, entities).Count == 0;

	// ── 内部工具 ──

	private bool TryGetOccupancy(int x, int y, int z, out FacilityOccupancy occupancy) =>
		_index.TryGetValue(new ZoneCell(x, y, z), out occupancy);

	private static bool ShouldBlockSight(FacilityInstance facility, FacilityFootprintCell cell) =>
		facility.Stage is FacilityStage.Construct or FacilityStage.Active or FacilityStage.Broken
		&& cell.BlocksSight;

	private static bool IsPassable(FacilityInstance facility, FacilityFootprintCell cell) =>
		facility.Stage is FacilityStage.Blueprint or FacilityStage.DeliverMaterials
			? true
			: cell.Passable;

	internal static IEnumerable<(int WorldX, int WorldY, int WorldZ, FacilityFootprintCell FootprintCell)> EnumerateFootprint(
		FacilityDef def,
		int anchorX,
		int anchorY,
		int z,
		FacilityRotation rotation)
	{
		var effectiveRotation = def.CanRotate ? rotation : FacilityRotation.North;
		var anchorCell = def.GetAnchorCell();
		foreach (var cell in def.Footprint)
		{
			var (rotatedX, rotatedY) = RotateOffset(cell.X - anchorCell.X, cell.Y - anchorCell.Y, effectiveRotation);
			yield return (
				anchorX + rotatedX,
				anchorY + rotatedY,
				z,
				new FacilityFootprintCell
				{
					X = rotatedX,
					Y = rotatedY,
					Passable = cell.Passable,
					BlocksSight = cell.BlocksSight,
					Glyph = string.IsNullOrWhiteSpace(cell.Glyph) ? def.Glyph : cell.Glyph,
				});
		}
	}

	private static (int X, int Y) RotateOffset(int x, int y, FacilityRotation rotation) => rotation switch
	{
		FacilityRotation.East => (-y, x),
		FacilityRotation.South => (-x, -y),
		FacilityRotation.West => (y, -x),
		_ => (x, y),
	};
}
