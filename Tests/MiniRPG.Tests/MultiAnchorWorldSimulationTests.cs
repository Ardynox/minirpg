using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class MultiAnchorWorldSimulationTests
{
	public MultiAnchorWorldSimulationTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	[Fact]
	public void ChunkManager_MultiAnchor_DeduplicatesAndLoadsBothCentersWithoutCrossZEviction()
	{
		var manager = new ChunkManager(4242, new FlatFloorGenerator())
		{
			LoadRadiusXY = 1,
			LoadRadiusZ = 1,
			CriticalLoadRadiusXY = 0,
			BackgroundLoadBudgetPerFrame = 256,
			MaxCachedChunks = 1024,
		};

		var anchors = new[]
		{
			new WorldCoord(0, 0, 0),
			new WorldCoord(0, 0, 0),
			new WorldCoord(128, 0, 0),
			new WorldCoord(128, 0, 1),
		};

		manager.UpdateLoadedChunks(anchors, currentTurn: 10);
		manager.ProcessPendingLoads(currentTurn: 10, budgetOverride: 999);

		Assert.Contains(manager.LoadedChunks.Keys, c => c.Cx == 0 && c.Cy == 0 && c.Cz == 0);
		Assert.Contains(manager.LoadedChunks.Keys, c => c.Cx == 4 && c.Cy == 0 && c.Cz == 0);
		Assert.Contains(manager.LoadedChunks.Keys, c => c.Cx == 4 && c.Cy == 0 && c.Cz == 1);
		Assert.True(manager.LoadedChunks.Count > 0);
	}

	[Fact]
	public void AIDispatcher_Classify_UsesNearestAnchorAndIsolatesByZ()
	{
		var state = CreateState();
		var hostile = state.Actors["hostile"];

		var detailNear = AIDispatcher.Classify(
			state,
			hostile,
			[new WorldCoord(12, 10, 0), new WorldCoord(80, 80, 0)],
			range: 6);
		Assert.Equal(SimDetail.Full, detailNear);

		var detailWrongZ = AIDispatcher.Classify(
			state,
			hostile,
			[new WorldCoord(12, 10, 1)],
			range: 6);
		Assert.Equal(SimDetail.Full, detailWrongZ);

		var detailFarZ = AIDispatcher.Classify(
			state,
			hostile,
			[new WorldCoord(12, 10, 10)],
			range: 6);
		Assert.Equal(SimDetail.Summary, detailFarZ);

		var detailNoAnchors = AIDispatcher.Classify(state, hostile, Array.Empty<WorldCoord>(), range: 6);
		Assert.Equal(SimDetail.Summary, detailNoAnchors);
	}

	[Fact]
	public void FogOfWarTracker_TeamVisionUnionsActorsAndKeepsSeenPerZSeparated()
	{
		var state = CreateState();
		RoomRuntimeModule.GetOrCreatePlayer(state, "owner", "Owner");
		RoomRuntimeModule.GetOrCreatePlayer(state, "guest", "Guest");
		RoomRuntimeModule.AssignPrimaryActor(state, "owner", "hero");
		RoomRuntimeModule.AssignPrimaryActor(state, "guest", "scout");

		state.PlayerId = "hero";
		state.PlayerX = state.Actors["hero"].X;
		state.PlayerY = state.Actors["hero"].Y;
		state.PlayerZ = 0;

		state.World!.GetTerrain(10, 10, 0);
		state.World.GetTerrain(20, 10, 0);
		state.World.GetTerrain(10, 10, 1);

		var tracker = new FogOfWarTracker { BaseVisionRadius = 8 };
		tracker.Update(state);

		Assert.NotEqual(PlayerVisionBand.Unknown, tracker.GetVisionBand(10, 10, 0));
		Assert.NotEqual(PlayerVisionBand.Unknown, tracker.GetVisionBand(20, 10, 0));
		Assert.NotEqual(PlayerVisionBand.Unknown, tracker.GetVisionBand(10, 10, 1));
	}

	private static GameState CreateState()
	{
		var state = new GameState
		{
			WorldSeed = 424242,
			PlayerId = "hero",
			PlayerX = 10,
			PlayerY = 10,
			PlayerZ = 0,
			GeneratorId = "test",
			ViewModeId = "single_layer",
			Actors = new Dictionary<string, Actor>(StringComparer.Ordinal),
			World = new WorldMap(424242, new FlatFloorGenerator()),
			Weather = WeatherState.CreateDefault(424242),
		};

		ActorModule.Add(state, CreateActor("hero", Factions.Player, 10, 10, 0));
		ActorModule.Add(state, CreateActor("scout", Factions.Player, 20, 10, 0));
		ActorModule.Add(state, CreateActor("hostile", Factions.Hostile, 14, 10, 0, "simple"));
		ActorModule.Add(state, CreateActor("deep_hostile", Factions.Hostile, 10, 10, 1, "simple"));
		return state;
	}

	private static Actor CreateActor(string id, string faction, int x, int y, int z, string? brainId = null) => new()
	{
		Id = id,
		TemplateId = "player",
		BrainId = brainId,
		X = x,
		Y = y,
		Z = z,
		Glyph = "@",
		DisplayName = id,
		Faction = faction,
		FacingX = 1,
		FacingY = 0,
		Limbs =
		[
			new Limb
			{
				Id = $"{id}_core",
				Name = "Core",
				MaxDurability = 10,
				Durability = 10,
				Material = "flesh",
				BodyPart = BodyParts.Torso,
				Capacities = new Dictionary<string, float>
				{
					[Caps.Consciousness] = 1f,
					[Caps.Sight] = 1f,
					[Caps.Moving] = 1f,
				},
			},
		],
	};

	private sealed class FlatFloorGenerator : IMapGenerator
	{
		public string Id => "flat_floor";
		public string Name => "Flat Floor";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
			chunk.Fill(TerrainRegistry.GetId(Terrains.Floor));
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
