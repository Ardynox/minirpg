using System.Collections.Generic;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

public sealed class FacilityWorldTests
{
	private readonly Dictionary<string, Actor> _actors = new(StringComparer.Ordinal);

	public FacilityWorldTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	[Fact]
	public void AttachedFacility_UsesFootprintForDisplayCollisionAndSight()
	{
		var world = new WorldMap(1234, new FloorGenerator());
		world.AttachFacilityState(new Dictionary<string, FacilityInstance>(StringComparer.Ordinal)
		{
			["stove_1"] = new FacilityInstance
			{
				Id = "stove_1",
				FacilityDefId = FacilityIds.Stove,
				AnchorX = 10,
				AnchorY = 10,
				Z = 0,
				Rotation = FacilityRotation.East,
				Stage = FacilityStage.Active,
				OwnerDomainId = DomainIds.Player,
			},
		});

		Assert.True(world.TryGetFacilityAt(10, 10, 0, out var anchorFacility));
		Assert.Equal("stove_1", anchorFacility!.Id);
		Assert.True(world.TryGetFacilityAt(10, 11, 0, out var chimneyFacility, out var chimneyCell));
		Assert.Equal("stove_1", chimneyFacility!.Id);
		Assert.NotNull(chimneyCell);

		Assert.Equal("C", world.GetDisplayCell(10, 10, 0, _actors));
		Assert.Equal("c", world.GetDisplayCell(10, 11, 0, _actors));
		Assert.False(world.IsWalkable(10, 10, 0));
		Assert.False(world.IsWalkable(10, 11, 0));
		Assert.True(world.BlocksSight(10, 11, 0));
	}

	[Fact]
	public void CanPlaceFacility_DetectsTerrainAndFacilityBlockers()
	{
		var world = new WorldMap(5678, new FloorGenerator());
		world.AttachFacilityState(new Dictionary<string, FacilityInstance>(StringComparer.Ordinal)
		{
			["shelf_1"] = new FacilityInstance
			{
				Id = "shelf_1",
				FacilityDefId = FacilityIds.Shelf,
				AnchorX = 5,
				AnchorY = 5,
				Z = 0,
				Rotation = FacilityRotation.North,
				Stage = FacilityStage.Active,
				OwnerDomainId = DomainIds.Player,
			},
		});

		world.SetTerrain(20, 20, 0, Terrains.WallStone);
		var bedDef = PresetDB.Facilities[FacilityIds.Bed];

		Assert.False(world.CanPlaceFacility(bedDef, 5, 5, 0, FacilityRotation.North));
		Assert.Contains(new ZoneCell(5, 5, 0), world.GetFacilityBlockers(bedDef, 5, 5, 0, FacilityRotation.North));

		Assert.False(world.CanPlaceFacility(bedDef, 20, 20, 0, FacilityRotation.North));
		Assert.Contains(new ZoneCell(20, 20, 0), world.GetFacilityBlockers(bedDef, 20, 20, 0, FacilityRotation.North));
	}

	private sealed class FloorGenerator : IMapGenerator
	{
		public string Id => "floor";
		public string Name => "Floor";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
			var floorId = TerrainRegistry.GetId(Terrains.Floor);
			for (var i = 0; i < chunk.TerrainIds.Length; i++)
				chunk.TerrainIds[i] = floorId;
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
