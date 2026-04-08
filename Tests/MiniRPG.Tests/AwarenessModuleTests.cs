using System.Collections.Generic;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

public sealed class AwarenessModuleTests
{
	public AwarenessModuleTests()
	{
		GameConfig.Load();
		TerrainRegistry.Load("terrains.json");
	}

	[Fact]
	public void FirstSight_EntersSuspicious()
	{
		var state = CreateState();
		var enemy = ActorModule.GetById(state, "enemy")!;

		var events = AwarenessModule.UpdateForTurn(state, enemy, PerceptionBuilder.Build(state, enemy, SimDetail.Full));

		Assert.Equal(AwarenessState.Suspicious, enemy.AwarenessState);
		Assert.Equal(state.PlayerId, enemy.AlertTargetActorId);
		Assert.Equal(4, enemy.LastKnownTargetX);
		Assert.Equal(1, enemy.LastKnownTargetY);
		Assert.Single(events);
		Assert.Equal("awareness_state_changed", events[0].Type);
		Assert.Equal("suspicious", events[0].EffectType);
	}

	[Fact]
	public void Suspicious_VisibleAgain_UpgradesToAlerted()
	{
		var state = CreateState();
		var enemy = ActorModule.GetById(state, "enemy")!;

		AwarenessModule.UpdateForTurn(state, enemy, PerceptionBuilder.Build(state, enemy, SimDetail.Full));
		state.Turn++;

		var events = AwarenessModule.UpdateForTurn(state, enemy, PerceptionBuilder.Build(state, enemy, SimDetail.Full));

		Assert.Equal(AwarenessState.Alerted, enemy.AwarenessState);
		Assert.Single(events);
		Assert.Equal("alerted", events[0].EffectType);
	}

	[Fact]
	public void Alerted_LoseSight_EntersSearching()
	{
		var state = CreateState();
		var enemy = ActorModule.GetById(state, "enemy")!;

		AwarenessModule.UpdateForTurn(state, enemy, PerceptionBuilder.Build(state, enemy, SimDetail.Full));
		state.Turn++;
		AwarenessModule.UpdateForTurn(state, enemy, PerceptionBuilder.Build(state, enemy, SimDetail.Full));

		state.World!.SetTerrain(2, 1, 0, Terrains.WallStone);
		state.Turn++;

		var events = AwarenessModule.UpdateForTurn(state, enemy, PerceptionBuilder.Build(state, enemy, SimDetail.Full));

		Assert.Equal(AwarenessState.Searching, enemy.AwarenessState);
		Assert.Equal(AwarenessModule.SearchDurationTurns, enemy.SearchTurnsRemaining);
		Assert.Single(events);
		Assert.Equal("searching", events[0].EffectType);
	}

	[Fact]
	public void Searching_Timeout_ReturnsIdle_AndClearsTargetMemory()
	{
		var state = CreateState();
		var enemy = ActorModule.GetById(state, "enemy")!;

		enemy.AwarenessState = AwarenessState.Searching;
		enemy.AlertTargetActorId = state.PlayerId;
		enemy.LastKnownTargetX = 4;
		enemy.LastKnownTargetY = 1;
		enemy.LastKnownTargetZ = 0;
		enemy.SearchTurnsRemaining = 1;
		state.World!.SetTerrain(2, 1, 0, Terrains.WallStone);

		var events = AwarenessModule.UpdateForTurn(state, enemy, PerceptionBuilder.Build(state, enemy, SimDetail.Full));

		Assert.Equal(AwarenessState.Idle, enemy.AwarenessState);
		Assert.Null(enemy.AlertTargetActorId);
		Assert.Equal(0, enemy.LastKnownTargetX);
		Assert.Equal(0, enemy.LastKnownTargetY);
		Assert.Equal(0, enemy.LastKnownTargetZ);
		Assert.Equal(0, enemy.SearchTurnsRemaining);
		Assert.Single(events);
		Assert.Equal("idle", events[0].EffectType);
	}

	[Fact]
	public void UpdateForTurn_WithCachedContext_MatchesDefaultFlow()
	{
		var state = CreateState();
		var enemy = ActorModule.GetById(state, "enemy")!;
		var perception = PerceptionBuilder.Build(state, enemy, SimDetail.Full);
		var context = AwarenessModule.CreateTurnContext(state);

		var events = AwarenessModule.UpdateForTurn(state, enemy, perception, context);

		Assert.Equal(AwarenessState.Suspicious, enemy.AwarenessState);
		Assert.Equal(state.PlayerId, enemy.AlertTargetActorId);
		Assert.Single(events);
		Assert.Equal("suspicious", events[0].EffectType);
	}

	[Fact]
	public void UpdateForTurn_WithDeadPlayerContext_ResetsActor()
	{
		var state = CreateState();
		var enemy = ActorModule.GetById(state, "enemy")!;
		var player = ActorModule.GetPlayer(state)!;
		enemy.AwarenessState = AwarenessState.Alerted;
		enemy.AlertTargetActorId = player.Id;
		player.Limbs.Clear();
		var context = AwarenessModule.CreateTurnContext(state);

		var events = AwarenessModule.UpdateForTurn(
			state,
			enemy,
			PerceptionBuilder.Build(state, enemy, SimDetail.Full),
			context);

		Assert.Empty(events);
		Assert.Equal(AwarenessState.Idle, enemy.AwarenessState);
		Assert.Null(enemy.AlertTargetActorId);
	}

	private static GameState CreateState()
	{
		var state = new GameState
		{
			PlayerId = "player",
			PlayerX = 4,
			PlayerY = 1,
			PlayerZ = 0,
			WorldSeed = 1234,
			World = new WorldMap(1234, new FloorGenerator()),
		};

		var player = CreateActor("player", Factions.Player, 4, 1, brainId: null);
		var enemy = CreateActor("enemy", Factions.Hostile, 1, 1, brainId: "simple");
		enemy.FacingX = 1;
		enemy.FacingY = 0;

		ActorModule.Add(state, player);
		ActorModule.Add(state, enemy);
		return state;
	}

	private static Actor CreateActor(string id, string faction, int x, int y, string? brainId)
	{
		return new Actor
		{
			Id = id,
			DisplayName = id,
			Faction = faction,
			BrainId = brainId,
			X = x,
			Y = y,
			Z = 0,
			FacingY = 1,
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
						[Caps.BloodCirculation] = 1f,
						[Caps.Moving] = 1f,
						[Caps.Consciousness] = 1f,
						[Caps.Metabolism] = 1f,
						[Caps.Sight] = 1f,
					},
					Tags = new Dictionary<string, int>
					{
						[CombatModule.VitalTag] = 1,
					},
				},
			],
		};
	}

	private sealed class FloorGenerator : IMapGenerator
	{
		public string Id => "floor";
		public string Name => "Floor";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
			chunk.Fill(TerrainRegistry.GetId(Terrains.Floor));
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
