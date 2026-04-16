using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Map;
using MiniRPG.Core.Needs;
using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 验证 B8 Thirst 链路：decay → stage thought → 背包喝水 → 能力乘数。
/// 与 NeedSystemTests 风格对称，不共用 helper，避免改动该文件。
/// </summary>
public sealed class ThirstSystemTests
{
	public ThirstSystemTests()
	{
		GameConfig.Load();
		PresetDB.Load();
		TerrainRegistry.Load("terrains.json");
	}

	[Fact]
	public void Thirst_EnabledForHumanProfile_AndDecaysOverTime()
	{
		var actor = CreateActor("hero", raceId: "human");
		NeedSystem.EnsureInitialized(actor, 0);
		Assert.Contains(NeedIds.Thirst, actor.Needs.Keys);

		NeedSystem.Sync(actor, 200);
		Assert.True(NeedSystem.GetNeedValue(actor, NeedIds.Thirst) < 100f);
	}

	[Fact]
	public void Dehydrated_StageThoughtIsAttachedAndHurtsMood()
	{
		var actor = CreateActor("hero", raceId: "human");
		NeedSystem.EnsureInitialized(actor, 0);
		NeedSystem.SetNeedValue(actor, NeedIds.Thirst, 10f);
		NeedSystem.Sync(actor, currentTurn: 1, events: new List<GameEvent>(), state: null);

		Assert.Contains(actor.Thoughts, t => t.Source == NeedThoughtSources.ThirstStage && t.Id == "dehydrated");
		Assert.True(actor.MoodValue < NeedSystem.DefaultMoodValue);
	}

	[Fact]
	public void Dehydrated_ReducesMovingAndConsciousnessCapacity()
	{
		var actor = CreateActor("hero", raceId: "human");
		NeedSystem.EnsureInitialized(actor, 0);
		NeedSystem.SetNeedValue(actor, NeedIds.Thirst, 10f);
		NeedSystem.Sync(actor, currentTurn: 1);

		var moveMultiplier = NeedSystem.GetCapacityMultiplier(actor, Caps.Moving);
		var consciousnessMultiplier = NeedSystem.GetCapacityMultiplier(actor, Caps.Consciousness);

		Assert.True(moveMultiplier < 1f, "dehydrated 应降低 Moving 能力");
		Assert.True(consciousnessMultiplier < 1f, "dehydrated 应降低 Consciousness 能力");
	}

	[Fact]
	public void ConsumeDrink_RestoresThirst_AndAppliesDrankWaterThought()
	{
		var state = CreateState();
		var actor = CreateActor("hero", raceId: "human");
		actor.Inventory.Add(PresetDB.CloneItem("water_flask"));
		ActorModule.Add(state, actor);
		NeedSystem.SetNeedValue(actor, NeedIds.Thirst, 20f);

		var result = NeedActionModule.TryConsumeDrink(state, actor, 0);

		Assert.True(result.Consumed);
		Assert.True(NeedSystem.GetNeedValue(actor, NeedIds.Thirst) > 20f);
		Assert.Empty(actor.Inventory);
		Assert.Contains(actor.Thoughts, t => t.Id == "drank_water" && t.Source == NeedThoughtSources.Drink);
		Assert.Contains(result.Events, evt => evt.Type == "drink_consumed");
	}

	[Fact]
	public void EatAction_FallsBackToDrink_WhenItemHasHydrationButNoNutrition()
	{
		var state = CreateState();
		var player = CreateActor("player", raceId: "human", faction: Factions.Player);
		state.PlayerId = player.Id;
		state.PlayerX = player.X;
		state.PlayerY = player.Y;
		state.PlayerZ = player.Z;
		player.Inventory.Add(PresetDB.CloneItem("water_flask"));
		ActorModule.Add(state, player);
		TimelineTurnManager.Reset(state);
		NeedSystem.SetNeedValue(player, NeedIds.Thirst, 20f);
		var beforeThirst = NeedSystem.GetNeedValue(player, NeedIds.Thirst);

		var action = TimelinePlayerAction.EatInventory(0);
		var result = TimelineTurnManager.SubmitPlayerAction(state, action);

		Assert.True(result.ActionConsumed);
		Assert.True(NeedSystem.GetNeedValue(player, NeedIds.Thirst) > beforeThirst);
		Assert.Empty(player.Inventory);
	}

	private static GameState CreateState()
	{
		return new GameState
		{
			PlayerId = "player",
			PlayerX = 1,
			PlayerY = 1,
			PlayerZ = 0,
			WorldSeed = 1234,
			World = new WorldMap(1234, new FloorGenerator()),
		};
	}

	private static Actor CreateActor(
		string id,
		string raceId,
		string faction = Factions.Friendly,
		int x = 1,
		int y = 1)
	{
		var actor = new Actor
		{
			Id = id,
			DisplayName = id,
			Faction = faction,
			BrainId = "simple",
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
