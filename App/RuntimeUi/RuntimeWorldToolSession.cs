using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using MiniRPG.Module.WorldTool;

namespace MiniRPG;

internal readonly record struct RuntimeWorldToolBrush(
	string Id,
	string Label,
	string? Glyph = null);

internal sealed class RuntimeWorldToolSession
{
	private const int TerrainColumnScanDepth = 16;

	private readonly GameState _state;
	private readonly List<RuntimeWorldToolBrush> _terrainBrushes = [];
	private readonly List<RuntimeWorldToolBrush> _facilityBrushes = [];
	private int _terrainBrushIndex;
	private int _facilityBrushIndex;
	private bool _reverseStack;
	private bool _facilityRotationSeeded;

	public RuntimeWorldToolSession(GameState state)
	{
		_state = state;
		RefreshBrushes();
	}

	public WorldToolMode CurrentToolMode { get; private set; } = WorldToolMode.Select;
	public WorldToolCategory CurrentCategory { get; private set; } = WorldToolCategory.Terrain;
	public Vector3I? HoverWorld { get; private set; }
	public FacilityRotation FacilityRotation { get; private set; } = FacilityRotation.South;

	public IReadOnlyList<RuntimeWorldToolBrush> TerrainBrushes => _terrainBrushes;
	public IReadOnlyList<RuntimeWorldToolBrush> FacilityBrushes => _facilityBrushes;
	public IReadOnlyList<RuntimeWorldToolBrush> CurrentBrushes => CurrentCategory == WorldToolCategory.Facility
		? _facilityBrushes
		: _terrainBrushes;

	public int CurrentBrushIndex => CurrentCategory == WorldToolCategory.Facility
		? _facilityBrushIndex
		: _terrainBrushIndex;

	public RuntimeWorldToolBrush CurrentBrush => CurrentCategory == WorldToolCategory.Facility
		? (_facilityBrushes.Count > 0 ? _facilityBrushes[Math.Clamp(_facilityBrushIndex, 0, _facilityBrushes.Count - 1)] : default)
		: (_terrainBrushes.Count > 0 ? _terrainBrushes[Math.Clamp(_terrainBrushIndex, 0, _terrainBrushes.Count - 1)] : default);

	public void RefreshBrushes()
	{
		RefreshTerrainBrushes();
		RefreshFacilityBrushes();
	}

	public void SetToolMode(WorldToolMode toolMode) => CurrentToolMode = toolMode;

	public void SetCategory(WorldToolCategory category)
	{
		if (category is not (WorldToolCategory.Terrain or WorldToolCategory.Facility))
			return;

		CurrentCategory = category;
		if (category == WorldToolCategory.Facility)
			EnsureFacilityRotationSeeded();
	}

	public void SelectTerrainBrush(int index)
	{
		if (_terrainBrushes.Count == 0)
			return;

		_terrainBrushIndex = Math.Clamp(index, 0, _terrainBrushes.Count - 1);
	}

	public void SelectFacilityBrush(int index)
	{
		if (_facilityBrushes.Count == 0)
			return;

		_facilityBrushIndex = Math.Clamp(index, 0, _facilityBrushes.Count - 1);
		EnsureFacilityRotationSeeded();
	}

	public void StepBrush(int delta)
	{
		if (CurrentBrushes.Count == 0 || delta == 0)
			return;

		var count = CurrentBrushes.Count;
		var next = (CurrentBrushIndex + delta) % count;
		if (next < 0)
			next += count;

		if (CurrentCategory == WorldToolCategory.Facility)
			_facilityBrushIndex = next;
		else
			_terrainBrushIndex = next;
	}

	public bool SetHover(Vector3I? hoverWorld)
	{
		if (HoverWorld == hoverWorld)
			return false;

		HoverWorld = hoverWorld;
		return true;
	}

	public bool SetReverseStack(bool reverseStack)
	{
		if (_reverseStack == reverseStack)
			return false;

		_reverseStack = reverseStack;
		return true;
	}

	public void RotateFacility(int delta)
	{
		if (delta == 0)
			return;

		EnsureFacilityRotationSeeded();
		var next = (((int)FacilityRotation + delta) % 4 + 4) % 4;
		FacilityRotation = (FacilityRotation)next;
		_facilityRotationSeeded = true;
	}

	public WorldToolPreviewState? ResolveHoverState(Vector3I? hoverWorld) =>
		ResolveHoverState(hoverWorld, _reverseStack);

	public WorldToolPreviewState? ResolveHoverState(Vector3I? hoverWorld, bool reverseStack)
	{
		if (_state.World == null || hoverWorld is not { } hoverCell)
			return null;

		if (CurrentCategory == WorldToolCategory.Facility)
			EnsureFacilityRotationSeeded();

		return CurrentCategory switch
		{
			WorldToolCategory.Facility when CurrentToolMode == WorldToolMode.Build => ResolveFacilityBuildPreview(hoverCell),
			WorldToolCategory.Facility => ResolveFacilityOccupiedPreview(hoverCell),
			_ when CurrentToolMode == WorldToolMode.Build => ResolveTerrainBuildPreview(hoverCell, reverseStack),
			_ => ResolveTerrainOccupiedPreview(hoverCell),
		};
	}

	public string BuildSummary(WorldToolPreviewState? previewState)
	{
		var actor = ResolveActiveActor();
		if (_state.RuntimeFreeBuild)
			return LocalizationService.TOrFallback("ui.runtime_tool.summary.free_build", "Free Build");

		if (CurrentToolMode == WorldToolMode.Demolish && previewState is { CanApply: true })
		{
			var refund = previewState.Kind switch
			{
				WorldToolPreviewKind.Facility => RuntimeBuildActionModule.GetFacilityDemolishRefund(previewState.GhostFacility),
				_ => ResolveTerrainRefund(previewState.ResolvedTargetCell),
			};
			return FormatMaterialSummary(refund, actor, affordPrefix: "Refund");
		}

		var costs = CurrentCategory == WorldToolCategory.Facility
			? RuntimeBuildActionModule.GetFacilityCosts(CurrentBrush.Id)
			: RuntimeBuildActionModule.GetTerrainCosts(CurrentBrush.Id);
		return FormatMaterialSummary(costs, actor, affordPrefix: null);
	}

	private WorldToolPreviewState ResolveTerrainBuildPreview(Vector3I hoverCell, bool reverseStack)
	{
		var world = _state.World!;
		var brush = CurrentBrush;
		var targetCell = ResolveTerrainBuildTargetCell(hoverCell, reverseStack);
		var targetTerrain = targetCell is { } resolvedTarget
			? world.GetTerrain(resolvedTarget.X, resolvedTarget.Y, resolvedTarget.Z).StringId
			: null;
		var costs = RuntimeBuildActionModule.GetTerrainCosts(brush.Id);
		var allowed = _state.RuntimeFreeBuild || costs.Count > 0;
		var hasConnectivity = targetCell is { } connectivityTarget
			&& BlockPlacementRules.HasFaceConnectedTerrain(world, connectivityTarget.X, connectivityTarget.Y, connectivityTarget.Z);
		var canApply = targetCell != null
			&& allowed
			&& targetTerrain is Terrains.Air or Terrains.Void
			&& (_state.RuntimeFreeBuild || hasConnectivity)
			&& (_state.RuntimeFreeBuild || RuntimeBuildActionModule.CanAffordMaterials(ResolveActiveActor(), costs));
		return CreatePreviewState(
			WorldToolMode.Build,
			WorldToolCategory.Terrain,
			WorldToolPreviewKind.Terrain,
			hoverCell,
			targetCell,
			brush,
			canApply,
			showGhost: canApply,
			showInfoOverlay: false,
			ghostRenderId: canApply ? brush.Id : null,
			ghostGlyph: canApply ? TerrainRegistry.Get(brush.Id)?.Glyph : null);
	}

	private WorldToolPreviewState ResolveTerrainOccupiedPreview(Vector3I hoverCell)
	{
		var brush = CurrentBrush;
		var targetCell = ResolveTerrainOccupiedTargetCell(hoverCell);
		var existingTerrainId = targetCell is { } resolvedTarget
			? _state.World!.GetTerrain(resolvedTarget.X, resolvedTarget.Y, resolvedTarget.Z).StringId
			: null;
		var demolishAllowed = existingTerrainId != null
			&& (_state.RuntimeFreeBuild || RuntimeBuildActionModule.GetTerrainCosts(existingTerrainId).Count > 0);
		var canApply = CurrentToolMode == WorldToolMode.Demolish
			? targetCell != null && demolishAllowed
			: targetCell != null;
		var showGhost = CurrentToolMode == WorldToolMode.Demolish
			&& canApply
			&& existingTerrainId is not (null or Terrains.Air or Terrains.Void);
		return CreatePreviewState(
			CurrentToolMode,
			WorldToolCategory.Terrain,
			WorldToolPreviewKind.Terrain,
			hoverCell,
			targetCell,
			brush,
			canApply,
			showGhost,
			showInfoOverlay: CurrentToolMode == WorldToolMode.Select && targetCell != null,
			ghostRenderId: showGhost ? existingTerrainId : null,
			ghostGlyph: showGhost && existingTerrainId != null ? TerrainRegistry.Get(existingTerrainId)?.Glyph : null,
			hideResolvedTargetInWorld: CurrentToolMode == WorldToolMode.Demolish && canApply);
	}

	private WorldToolPreviewState ResolveFacilityBuildPreview(Vector3I hoverCell)
	{
		var brush = CurrentBrush;
		var def = FacilityRegistry.Get(brush.Id);
		var costs = RuntimeBuildActionModule.GetFacilityCosts(brush.Id);
		var canApply = def != null
			&& _state.World != null
			&& _state.World.GetFacilityBlockers(def, hoverCell.X, hoverCell.Y, hoverCell.Z, FacilityRotation).Count == 0
			&& (_state.RuntimeFreeBuild || RuntimeBuildActionModule.CanAffordMaterials(ResolveActiveActor(), costs));
		var previewFacility = BuildPreviewFacility(brush.Id, hoverCell);
		return CreatePreviewState(
			WorldToolMode.Build,
			WorldToolCategory.Facility,
			WorldToolPreviewKind.Facility,
			hoverCell,
			hoverCell,
			brush,
			canApply,
			showGhost: canApply,
			showInfoOverlay: false,
			ghostFacility: canApply ? previewFacility : null);
	}

	private WorldToolPreviewState ResolveFacilityOccupiedPreview(Vector3I hoverCell)
	{
		var brush = CurrentBrush;
		_state.World!.TryGetFacilityAt(hoverCell.X, hoverCell.Y, hoverCell.Z, out var facility);
		var canApply = facility != null;
		var showGhost = CurrentToolMode == WorldToolMode.Demolish && canApply;
		return CreatePreviewState(
			CurrentToolMode,
			WorldToolCategory.Facility,
			WorldToolPreviewKind.Facility,
			hoverCell,
			canApply ? hoverCell : null,
			brush,
			canApply,
			showGhost,
			showInfoOverlay: CurrentToolMode == WorldToolMode.Select && canApply,
			resolvedEntityId: facility?.Id,
			hideResolvedTargetInWorld: CurrentToolMode == WorldToolMode.Demolish && canApply,
			ghostFacility: showGhost ? facility : null);
	}

	private Vector3I? ResolveTerrainBuildTargetCell(Vector3I hoverCell, bool reverseStack)
	{
		var pickedTerrain = _state.World!.GetTerrain(hoverCell.X, hoverCell.Y, hoverCell.Z).StringId;
		if (pickedTerrain is Terrains.Air or Terrains.Void)
			return hoverCell;

		var zStep = reverseStack ? 1 : -1;
		var placeZ = hoverCell.Z + zStep;
		for (var scanned = 0; scanned < TerrainColumnScanDepth; scanned++, placeZ += zStep)
		{
			var terrain = _state.World.GetTerrain(hoverCell.X, hoverCell.Y, placeZ).StringId;
			if (terrain is Terrains.Air or Terrains.Void)
				return new Vector3I(hoverCell.X, hoverCell.Y, placeZ);
		}

		return null;
	}

	private Vector3I? ResolveTerrainOccupiedTargetCell(Vector3I hoverCell)
	{
		var pickedTerrain = _state.World!.GetTerrain(hoverCell.X, hoverCell.Y, hoverCell.Z).StringId;
		return pickedTerrain is Terrains.Air or Terrains.Void ? null : hoverCell;
	}

	private FacilityInstance BuildPreviewFacility(string facilityDefId, Vector3I hoverCell) => new()
	{
		Id = $"__preview_{facilityDefId}",
		FacilityDefId = facilityDefId,
		AnchorX = hoverCell.X,
		AnchorY = hoverCell.Y,
		Z = hoverCell.Z,
		Rotation = FacilityRotation,
		Stage = FacilityStage.Blueprint,
		OwnerDomainId = DomainIds.Player,
		MaxHitPoints = 1,
		HitPoints = 1,
	};

	private List<ItemAmount> ResolveTerrainRefund(Vector3I? targetCell)
	{
		if (_state.World == null || targetCell is not { } cell)
			return [];

		return RuntimeBuildActionModule.GetTerrainCosts(_state.World.GetTerrain(cell.X, cell.Y, cell.Z).StringId);
	}

	private WorldToolPreviewState CreatePreviewState(
		WorldToolMode toolMode,
		WorldToolCategory category,
		WorldToolPreviewKind kind,
		Vector3I rawHoverCell,
		Vector3I? resolvedTargetCell,
		RuntimeWorldToolBrush brush,
		bool canApply,
		bool showGhost,
		bool showInfoOverlay,
		string? ghostRenderId = null,
		string? ghostGlyph = null,
		string? resolvedEntityId = null,
		bool hideResolvedTargetInWorld = false,
		FacilityInstance? ghostFacility = null) =>
		new(
			toolMode,
			category,
			kind,
			rawHoverCell,
			resolvedTargetCell,
			brush.Id,
			brush.Glyph,
			canApply,
			showGhost,
			showInfoOverlay,
			showGhost ? ghostRenderId : null,
			showGhost ? ghostGlyph ?? brush.Glyph : null,
			resolvedEntityId,
			hideResolvedTargetInWorld,
			ghostFacility);

	private Actor? ResolveActiveActor() => PartyModule.GetActiveActor(_state) ?? ActorModule.GetPlayer(_state);

	private void EnsureFacilityRotationSeeded()
	{
		if (_facilityRotationSeeded)
			return;

		var actor = ResolveActiveActor();
		FacilityRotation = ResolveFacingRotation(actor?.FacingX ?? 0, actor?.FacingY ?? 1);
		_facilityRotationSeeded = true;
	}

	private void RefreshTerrainBrushes()
	{
		var selectedId = _terrainBrushes.Count > 0 && _terrainBrushIndex >= 0 && _terrainBrushIndex < _terrainBrushes.Count
			? _terrainBrushes[_terrainBrushIndex].Id
			: null;
		_terrainBrushes.Clear();

		foreach (var terrain in TerrainRegistry.All)
		{
			if (terrain == null || terrain.StringId is Terrains.Air or Terrains.Void)
				continue;
			if (!_state.RuntimeFreeBuild && !TerrainBuildRuleRegistry.IsAllowed(terrain.StringId))
				continue;

			_terrainBrushes.Add(new RuntimeWorldToolBrush(
				terrain.StringId,
				GameLocalizer.LocalizeTerrainName(terrain.StringId),
				terrain.Glyph));
		}

		if (_terrainBrushes.Count == 0)
			_terrainBrushes.Add(new RuntimeWorldToolBrush(Terrains.Floor, GameLocalizer.LocalizeTerrainName(Terrains.Floor), "."));

		_terrainBrushIndex = ResolveBrushIndex(_terrainBrushes, selectedId);
	}

	private void RefreshFacilityBrushes()
	{
		var selectedId = _facilityBrushes.Count > 0 && _facilityBrushIndex >= 0 && _facilityBrushIndex < _facilityBrushes.Count
			? _facilityBrushes[_facilityBrushIndex].Id
			: null;
		_facilityBrushes.Clear();

		foreach (var (_, def) in FacilityRegistry.All.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
		{
			_facilityBrushes.Add(new RuntimeWorldToolBrush(
				def.Id,
				GameLocalizer.HumanizeId(def.Id),
				def.Glyph));
		}

		_facilityBrushIndex = ResolveBrushIndex(_facilityBrushes, selectedId);
	}

	private static int ResolveBrushIndex(IReadOnlyList<RuntimeWorldToolBrush> brushes, string? selectedId)
	{
		if (brushes.Count == 0)
			return -1;

		if (!string.IsNullOrWhiteSpace(selectedId))
		{
			for (var i = 0; i < brushes.Count; i++)
			{
				if (string.Equals(brushes[i].Id, selectedId, StringComparison.Ordinal))
					return i;
			}
		}

		return 0;
	}

	private static FacilityRotation ResolveFacingRotation(int dx, int dy)
	{
		if (dx == 0 && dy == -1)
			return FacilityRotation.North;
		if (dx == 1 && dy == 0)
			return FacilityRotation.East;
		if (dx == -1 && dy == 0)
			return FacilityRotation.West;
		return FacilityRotation.South;
	}

	private string FormatMaterialSummary(IEnumerable<ItemAmount> materials, Actor? actor, string? affordPrefix)
	{
		var normalized = materials?.Where(static item => item != null && item.Count > 0).ToList() ?? [];
		if (normalized.Count == 0)
			return affordPrefix == null
				? LocalizationService.TOrFallback("ui.runtime_tool.summary.no_cost", "No material cost")
				: LocalizationService.TOrFallback("ui.runtime_tool.summary.no_refund", "No refund");

		var parts = normalized.Select(item =>
		{
			var label = GameLocalizer.LocalizeItemName(item.ItemId, item.ItemId);
			var available = actor == null
				? 0
				: InventoryModule.CountMatching(actor, candidate =>
					!candidate.Equipped &&
					string.Equals(candidate.Id, item.ItemId, StringComparison.Ordinal));
			var colorPrefix = affordPrefix == null && available < item.Count ? "!" : string.Empty;
			return $"{colorPrefix}{label} x{item.Count}";
		});
		var body = string.Join("  ", parts);
		if (affordPrefix != null)
			return $"{affordPrefix}: {body}";

		var canAfford = RuntimeBuildActionModule.CanAffordMaterials(actor, normalized);
		var suffix = canAfford
			? LocalizationService.TOrFallback("ui.runtime_tool.summary.ready", "Ready")
			: LocalizationService.TOrFallback("ui.runtime_tool.summary.missing", "Missing materials");
		return $"{body}  |  {suffix}";
	}
}
