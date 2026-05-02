using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using MiniRPG.Core.Map;
using MiniRPG.Core.Needs;
using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 验证 <see cref="NeedActionModule.TryConsumeFood"/> 在不同新鲜阶段的分支：
/// Fresh 正常吃；Stale 减营养 + ate_stale；Spoiled 大减营养 + ate_spoiled + food_poisoning condition；
/// Rotten 拒绝消费 + 发 food_rejected 事件。
/// </summary>
public sealed class FoodConsumeFreshnessTests
{
	public FoodConsumeFreshnessTests()
	{
		GameConfig.Load();
		PresetDB.Load();
		TerrainRegistry.Load("terrains.json");
		FoodFreshnessEvaluator.EnsureInitialized();
	}

	[Fact]
	public void TryConsumeFood_Fresh_RestoresHungerAndConsumes()
	{
		var (state, actor) = MakeActorWithFood(spawnTurn: 100, freshnessTurns: 10, currentTurn: 102);
		NeedSystem.SetNeedValue(actor, NeedIds.Hunger, 20f);

		var result = NeedActionModule.TryConsumeFood(state, actor, 0);

		Assert.True(result.Consumed);
		Assert.Empty(actor.Inventory);
		Assert.True(NeedSystem.GetNeedValue(actor, NeedIds.Hunger) > 20f);
		var foodEvent = result.Events.SingleOrDefault(e => e.Type == "food_consumed");
		Assert.NotNull(foodEvent);
		Assert.Equal("fresh", foodEvent!.EffectType);
		Assert.DoesNotContain(actor.HealthConditions, c => c.Id == HealthConditionIds.FoodPoisoning);
	}

	[Fact]
	public void TryConsumeFood_Stale_AttachesAteStaleThought_AndStillConsumes()
	{
		var (state, actor) = MakeActorWithFood(spawnTurn: 100, freshnessTurns: 10, currentTurn: 106);
		NeedSystem.SetNeedValue(actor, NeedIds.Hunger, 20f);

		var result = NeedActionModule.TryConsumeFood(state, actor, 0);

		Assert.True(result.Consumed);
		var foodEvent = result.Events.Single(e => e.Type == "food_consumed");
		Assert.Equal("stale", foodEvent.EffectType);
		Assert.Contains(actor.Thoughts, t => t.Id == "ate_stale");
		Assert.DoesNotContain(actor.HealthConditions, c => c.Id == HealthConditionIds.FoodPoisoning);
	}

	[Fact]
	public void TryConsumeFood_Spoiled_AttachesFoodPoisoning_AndAteSpoiledThought()
	{
		var (state, actor) = MakeActorWithFood(spawnTurn: 100, freshnessTurns: 10, currentTurn: 115);
		NeedSystem.SetNeedValue(actor, NeedIds.Hunger, 20f);

		var result = NeedActionModule.TryConsumeFood(state, actor, 0);

		Assert.True(result.Consumed);
		var foodEvent = result.Events.Single(e => e.Type == "food_consumed");
		Assert.Equal("spoiled", foodEvent.EffectType);
		Assert.Contains(actor.Thoughts, t => t.Id == "ate_spoiled");
		Assert.Contains(actor.HealthConditions, c => c.Id == HealthConditionIds.FoodPoisoning);
	}

	[Fact]
	public void TryConsumeFood_Rotten_RejectsAndEmitsFoodRejected()
	{
		var (state, actor) = MakeActorWithFood(spawnTurn: 100, freshnessTurns: 10, currentTurn: 130);
		// 先让 actor 的需求 LastSyncTurn 对齐到 state.Turn，否则 TryConsumeFood 入口第一次 Sync
		// 会把 hunger 按 130 turn 的时间差衰减（EnsureInitialized 的默认起点 turn=0）。
		// Fresh/Stale/Spoiled 分支靠随后的 ConsumeFood 补回营养所以感知不到；Rotten 分支不消费
		// 无法补回，会让"beforeHunger == afterHunger"的契约破功。
		NeedSystem.Sync(actor, state.Turn, new List<GameEvent>(), state);
		NeedSystem.SetNeedValue(actor, NeedIds.Hunger, 20f);
		var beforeHunger = NeedSystem.GetNeedValue(actor, NeedIds.Hunger);

		var result = NeedActionModule.TryConsumeFood(state, actor, 0);

		Assert.False(result.Consumed);
		Assert.Single(actor.Inventory); // 食物没被消耗，仍在背包
		Assert.Equal(beforeHunger, NeedSystem.GetNeedValue(actor, NeedIds.Hunger));
		Assert.Contains(result.Events, e => e.Type == "food_rejected" && e.EffectType == "rotten");
		Assert.DoesNotContain(actor.HealthConditions, c => c.Id == HealthConditionIds.FoodPoisoning);
	}

	[Fact]
	public void TryConsumeFood_Fresh_ButNoFreshnessTag_TreatedAsFresh()
	{
		// 没配 freshness_turns 的食物（罐头/干粮）：永远 Fresh，无降级
		var state = CreateState();
		var actor = CreateActor("hero", raceId: "human");
		ActorModule.Add(state, actor);
		var canned = new Item
		{
			Id = "canned_meal",
			MaterialId = "preserved",
			Category = ItemCategories.Food,
			SpawnedAtTurn = 0, // 100 turn 前生成
			MaxStack = 1,
			StackCount = 1,
		};
		canned.Tags[ItemTags.Nutrition] = 4;
		canned.EnsureRuntimeState();
		actor.Inventory.Add(canned);
		state.Turn = 9999;
		NeedSystem.SetNeedValue(actor, NeedIds.Hunger, 20f);

		var result = NeedActionModule.TryConsumeFood(state, actor, 0);

		Assert.True(result.Consumed);
		var foodEvent = result.Events.Single(e => e.Type == "food_consumed");
		Assert.Equal("fresh", foodEvent.EffectType);
	}

	private static (GameState state, Actor actor) MakeActorWithFood(int spawnTurn, int freshnessTurns, int currentTurn)
	{
		var state = CreateState();
		state.Turn = currentTurn;
		var actor = CreateActor("hero", raceId: "human");
		ActorModule.Add(state, actor);
		var food = new Item
		{
			Id = "raw_meat",
			MaterialId = "meat",
			Category = ItemCategories.Food,
			SpawnedAtTurn = spawnTurn,
			MaxStack = 10,
			StackCount = 1,
		};
		food.Tags[ItemTags.Nutrition] = 3;
		food.Tags[ItemTags.FreshnessTurns] = freshnessTurns;
		food.EnsureRuntimeState();
		actor.Inventory.Add(food);
		return (state, actor);
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

	private static Actor CreateActor(string id, string raceId, string faction = Factions.Friendly)
	{
		var actor = new Actor
		{
			Id = id,
			DisplayName = id,
			Faction = faction,
			BrainId = "simple",
			X = 1,
			Y = 1,
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
		public void GenerateChunk(ChunkData chunk, int worldSeed) { }
		public void PopulateChunk(ChunkData chunk, int worldSeed) { }
	}
}
