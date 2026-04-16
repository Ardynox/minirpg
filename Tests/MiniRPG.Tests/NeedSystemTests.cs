using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Map;
using MiniRPG.Core.Needs;
using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

public sealed class NeedSystemTests
{
	public NeedSystemTests()
	{
		GameConfig.Load();
		PresetDB.Load();
		TerrainRegistry.Load("terrains.json");
	}

	[Fact]
	public void Sync_DecaysNeeds_AndExpiresTemporaryThoughts()
	{
		var actor = CreateActor("hero", raceId: "human");
		NeedSystem.EnsureInitialized(actor, 0);
		NeedSystem.ApplyTemporaryThought(actor, "test_temp", -4f, durationTurns: 3, currentTurn: 0, source: "test");

		NeedSystem.Sync(actor, 10);

		Assert.True(NeedSystem.GetNeedValue(actor, NeedIds.Hunger) < 100f);
		Assert.True(NeedSystem.GetNeedValue(actor, NeedIds.Rest) < 100f);
		Assert.DoesNotContain(actor.Thoughts, thought => thought.Source == "test");
	}

	[Fact]
	public void ConsumeFood_RestoresHunger_AndAppliesMealThought()
	{
		var state = CreateState();
		var actor = CreateActor("hero", raceId: "human");
		actor.Inventory.Add(PresetDB.CloneItem("meal_fine"));
		ActorModule.Add(state, actor);
		NeedSystem.SetNeedValue(actor, NeedIds.Hunger, 20f);

		var result = NeedActionModule.TryConsumeFood(state, actor, 0);

		Assert.True(result.Consumed);
		Assert.True(NeedSystem.GetNeedValue(actor, NeedIds.Hunger) > 20f);
		Assert.Empty(actor.Inventory);
		Assert.Contains(actor.Thoughts, thought => thought.Id == "ate_fine");
		Assert.Contains(result.Events, evt => evt.Type == "food_consumed");
	}

	[Fact]
	public void RestAction_RequiresBedroll_AndRecoversRest_WhenAvailable()
	{
		var state = CreateState();
		var player = CreateActor("player", raceId: "human", faction: Factions.Player);
		state.PlayerId = player.Id;
		state.PlayerX = player.X;
		state.PlayerY = player.Y;
		state.PlayerZ = player.Z;
		ActorModule.Add(state, player);
		TimelineTurnManager.Reset(state);
		NeedSystem.SetNeedValue(player, NeedIds.Rest, 20f);

		var fail = TimelineTurnManager.SubmitPlayerAction(state, TimelinePlayerAction.Rest());

		Assert.False(fail.ActionConsumed);
		Assert.True(TimelineTurnManager.IsPlayerTurn(state));

		player.Inventory.Add(PresetDB.CloneItem("bedroll"));
		var beforeRest = NeedSystem.GetNeedValue(player, NeedIds.Rest);

		var success = TimelineTurnManager.SubmitPlayerAction(state, TimelinePlayerAction.Rest());

		Assert.True(success.ActionConsumed);
		Assert.True(NeedSystem.GetNeedValue(player, NeedIds.Rest) > beforeRest);
	}

	[Fact]
	public void Profiles_ApplyExpectedNeedAvailability()
	{
		var undead = CreateActor("undead_actor", raceId: "undead");
		NeedSystem.EnsureInitialized(undead, 0);
		Assert.Empty(undead.Needs);
		Assert.Equal(0f, undead.MoodValue);

		var slime = CreateActor("slime_actor", raceId: "slime");
		NeedSystem.EnsureInitialized(slime, 0);
		Assert.Contains(NeedIds.Hunger, slime.Needs.Keys);
		Assert.DoesNotContain(NeedIds.Rest, slime.Needs.Keys);
		Assert.Equal(0f, slime.MoodValue);
	}

	[Fact]
	public void IdleNpc_EatsBeforeBrain_ButAlertedHostileIgnoresNeedBehavior()
	{
		var state = CreateState();
		var player = CreateActor("player", raceId: "human", faction: Factions.Player, brainId: null, x: 1, y: 1);
		state.PlayerId = player.Id;
		state.PlayerX = player.X;
		state.PlayerY = player.Y;
		state.PlayerZ = player.Z;
		ActorModule.Add(state, player);

		var npc = CreateActor("npc", raceId: "human", faction: Factions.Friendly, brainId: "simple", x: 4, y: 1);
		npc.Inventory.Add(PresetDB.CloneItem("meal_simple"));
		NeedSystem.SetNeedValue(npc, NeedIds.Hunger, 10f);
		ActorModule.Add(state, npc);

		var eat = AIDispatcher.DecideAndExecuteAnyResult(state, npc, tickBuffs: false);

		Assert.True(eat.Consumed);
		Assert.Contains(eat.Events, evt => evt.Type == "food_consumed");

		npc.Faction = Factions.Hostile;
		npc.Inventory.Add(PresetDB.CloneItem("meal_simple"));
		NeedSystem.SetNeedValue(npc, NeedIds.Hunger, 10f);
		npc.AwarenessState = AwarenessState.Alerted;
		npc.AlertTargetActorId = player.Id;
		npc.LastKnownTargetX = player.X;
		npc.LastKnownTargetY = player.Y;

		var ignore = AIDispatcher.DecideAndExecuteAnyResult(state, npc, tickBuffs: false);

		Assert.DoesNotContain(ignore.Events, evt => evt.Type == "food_consumed");
	}

	[Fact]
	public void HasNearbyThreat_UsesRadiusBasedCellLookup()
	{
		var state = CreateState();
		var actor = CreateActor("npc", raceId: "human", faction: Factions.Friendly, x: 5, y: 5);
		var nearHostile = CreateActor("near_hostile", raceId: "human", faction: Factions.Hostile, x: 7, y: 5);
		var farHostile = CreateActor("far_hostile", raceId: "human", faction: Factions.Hostile, x: 15, y: 15);
		ActorModule.Add(state, actor);
		ActorModule.Add(state, nearHostile);
		ActorModule.Add(state, farHostile);

		Assert.True(ThreatDetection.HasNearbyThreat(state, actor, radius: 2));
		Assert.False(ThreatDetection.HasNearbyThreat(state, actor, radius: 1));
	}

	private static GameState CreateState()
	{
		var state = new GameState
		{
			PlayerId = "player",
			PlayerX = 1,
			PlayerY = 1,
			PlayerZ = 0,
			WorldSeed = 1234,
			World = new WorldMap(1234, new FloorGenerator()),
		};
		return state;
	}

	private static Actor CreateActor(
		string id,
		string raceId,
		string faction = Factions.Friendly,
		string? brainId = "simple",
		int x = 1,
		int y = 1)
	{
		var actor = new Actor
		{
			Id = id,
			DisplayName = id,
			Faction = faction,
			BrainId = brainId,
			X = x,
			Y = y,
			Z = 0,
			Race = new Race
			{
				Id = raceId,
				Name = raceId,
				NeedProfileId = NeedCatalog.ResolveProfileId(raceId, null),
			},
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
						[Caps.Sight] = 1f,
						[Caps.Manipulation] = 1f,
					},
					Tags = new Dictionary<string, int>
					{
						[CombatModule.VitalTag] = 1,
					},
				},
			],
		};
		NeedSystem.EnsureInitialized(actor, 0);
		return actor;
	}

	private sealed class FloorGenerator : IMapGenerator
	{
		public string Id => "floor_stub";
		public string Name => "Floor Stub";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
