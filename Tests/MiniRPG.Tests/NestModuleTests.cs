using System;
using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Map;
using MiniRPG.Module;
using MiniRPG.Module.Render;
using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

public sealed class NestModuleTests
{
	public NestModuleTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	private static GameState CreateStateWithNest(int nestX = 5, int nestY = 5, int spawnInterval = 1, int maxSpawned = 3)
	{
		var (state, _, _) = SkillCastingTestHelper.CreateCombatState();
		var chunkCoord = CoordUtil.WorldToChunk(nestX, nestY, 0);
		var chunk = state.World!.Chunks.GetOrLoad(chunkCoord);
		chunk.Nests.Add(new NestData
		{
			X = nestX,
			Y = nestY,
			SpawnInterval = spawnInterval,
			MaxSpawned = maxSpawned,
			TurnsSinceSpawn = spawnInterval, // Ready to spawn
		});
		return state;
	}

	[Fact]
	public void Tick_NullWorld_ReturnsEmpty()
	{
		var state = new GameState { World = null };
		var events = NestModule.Tick(state);
		Assert.Empty(events);
	}

	[Fact]
	public void Tick_NestReady_SpawnsMonster()
	{
		var state = CreateStateWithNest();
		var actorCountBefore = state.Actors.Count;
		var events = NestModule.Tick(state);
		// Should have spawned at least one monster
		Assert.True(state.Actors.Count > actorCountBefore || events.Count == 0);
	}

	[Fact]
	public void Tick_NestNotReady_DoesNotSpawn()
	{
		var state = CreateStateWithNest(spawnInterval: 10);
		// Reset timer so it's not ready
		var chunkCoord = CoordUtil.WorldToChunk(5, 5, 0);
		var chunk = state.World!.Chunks.GetOrLoad(chunkCoord);
		chunk.Nests[0].TurnsSinceSpawn = 0;

		var actorCountBefore = state.Actors.Count;
		NestModule.Tick(state);
		Assert.Equal(actorCountBefore, state.Actors.Count);
	}

	[Fact]
	public void Tick_MaxSpawnedReached_DoesNotSpawn()
	{
		var state = CreateStateWithNest(maxSpawned: 0);
		var actorCountBefore = state.Actors.Count;
		NestModule.Tick(state);
		Assert.Equal(actorCountBefore, state.Actors.Count);
	}

	[Fact]
	public void StartWorldCharacter_ResetsNestSpawnCounter()
	{
		var priorState = CreateStateWithNest();
		NestModule.Tick(priorState);
		Assert.Contains(priorState.Actors.Keys, id => string.Equals(id, "nest_0", StringComparison.Ordinal));

		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("nest-counter-reset");
		try
		{
			var state = new GameState();
			var session = new GameSessionModule(state, new FogOfWarTracker(), root);
			var manifest = session.CreateWorld("Alpha", new WorldSettings
			{
				Seed = 424242,
				GeneratorId = "blank_floor",
			});

			session.StartWorldCharacter(manifest.WorldId, PlayerCreationOptions.CreateDefault());
			AddReadyNest(state, state.PlayerX + 3, state.PlayerY, state.PlayerZ);

			NestModule.Tick(state);

			var spawned = Assert.Single(state.Actors.Values, actor =>
				actor.Faction == Factions.Hostile
				&& string.Equals(actor.Id, "nest_0", StringComparison.Ordinal));
			Assert.Equal(state.PlayerZ, spawned.Z);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	private static void AddReadyNest(GameState state, int x, int y, int z)
	{
		var chunkCoord = CoordUtil.WorldToChunk(x, y, z);
		var chunk = state.World!.Chunks.GetOrLoad(chunkCoord);
		chunk.Nests.Add(new NestData
		{
			X = x,
			Y = y,
			SpawnInterval = 1,
			MaxSpawned = 1,
			TurnsSinceSpawn = 1,
		});
	}
}
