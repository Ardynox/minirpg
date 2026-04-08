using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

public sealed class ActorSpatialQueryTests
{
	[Fact]
	public void GetAllAt_UsesLoadedChunkActorIndex()
	{
		var state = CreateLoadedState();
		var alpha = CreateActor("alpha", Factions.Friendly, 1, 1, 0);
		var beta = CreateActor("beta", Factions.Hostile, 1, 1, 0);

		ActorModule.Add(state, alpha);
		ActorModule.Add(state, beta);

		var chunk = state.World!.Chunks.GetOrLoad(CoordUtil.WorldToChunk(1, 1, 0));
		Assert.Contains(alpha.Id, chunk.ActorIds);
		Assert.Contains(beta.Id, chunk.ActorIds);

		var occupants = ActorModule.GetAllAt(state, 1, 1, 0);

		Assert.Equal(["alpha", "beta"], occupants.Select(actor => actor.Id).ToArray());
		Assert.Same(beta, ActorModule.GetHostileAt(state, 1, 1, 0));
	}

	[Fact]
	public void GetAllAt_FallsBackToFullScan_ForUnloadedChunk()
	{
		var state = CreateLoadedState();
		var remoteX = ChunkData.Size * 8;
		var remoteY = 3;
		var remote = CreateActor("remote", Factions.Hostile, remoteX, remoteY, 0);

		ActorModule.Add(state, remote);

		Assert.False(state.World!.Chunks.IsLoaded(CoordUtil.WorldToChunk(remoteX, remoteY, 0)));

		var occupants = ActorModule.GetAllAt(state, remoteX, remoteY, 0);

		Assert.Single(occupants);
		Assert.Same(remote, occupants[0]);
	}

	[Fact]
	public void RebuildLoadedActorIndex_RehydratesSnapshotStyleActors()
	{
		var state = CreateLoadedState();
		var restored = CreateActor("restored", Factions.Hostile, 2, 2, 0);

		state.Actors[restored.Id] = restored;
		var chunk = state.World!.Chunks.GetOrLoad(CoordUtil.WorldToChunk(2, 2, 0));
		Assert.Empty(chunk.ActorIds);

		state.World.RebuildLoadedActorIndex(state.Actors);

		Assert.Contains(restored.Id, chunk.ActorIds);
		Assert.Same(restored, ActorModule.GetAt(state, 2, 2, 0));
	}

	private static GameState CreateLoadedState()
	{
		var state = new GameState
		{
			PlayerId = "player",
			World = new WorldMap(123, new StubGenerator()),
		};

		state.World.Chunks.UpdateLoadedChunks(new WorldCoord(0, 0, 0), currentTurn: 0);
		return state;
	}

	private static Actor CreateActor(string id, string faction, int x, int y, int z) =>
		new()
		{
			Id = id,
			DisplayName = id,
			Faction = faction,
			X = x,
			Y = y,
			Z = z,
			Limbs =
			[
				new Limb
				{
					Id = $"{id}_core",
					Name = "Core",
					MaxDurability = 10,
					Durability = 10,
					Material = "flesh",
					BodyPart = "torso",
					Capacities = new Dictionary<string, float>
					{
						[Caps.BloodCirculation] = 1f,
						[Caps.Moving] = 1f,
						[Caps.Consciousness] = 1f,
						[Caps.Metabolism] = 1f,
					},
				},
			],
		};

	private sealed class StubGenerator : IMapGenerator
	{
		public string Id => "stub";
		public string Name => "Stub";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
