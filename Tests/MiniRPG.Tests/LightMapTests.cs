using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using MiniRPG.Core.World.Generators;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class LightMapTests
{
	public LightMapTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	[Fact]
	public void Rebuild_CampfireEmitter_ProducesLightAtSourceCell()
	{
		var world = new WorldMap(7, new BlankFloorGenerator());
		world.SetFixture(3, 4, 0, "*", Entities.Campfire);
		var lightMap = new LightMap();

		lightMap.Rebuild(world, cx: 3, cy: 4, cz: 0, halfW: 4, halfH: 4, zMin: 0, zMax: 0);

		Assert.True(lightMap.TryGetLight(3, 4, 0, out var light));
		Assert.True(light.Intensity > 0f);
	}

	[Fact]
	public void Rebuild_LavaEmitter_ProducesLightAtSourceCell()
	{
		var world = new WorldMap(11, new BlankFloorGenerator());
		world.SetTerrain(6, 2, 0, Terrains.Lava);
		var lightMap = new LightMap();

		lightMap.Rebuild(world, cx: 6, cy: 2, cz: 0, halfW: 4, halfH: 4, zMin: 0, zMax: 0);

		Assert.True(lightMap.TryGetLight(6, 2, 0, out var light));
		Assert.True(light.Intensity > 0f);
	}
}
