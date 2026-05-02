using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using MiniRPG.Core.Needs;
using MiniRPG.Core.Revival;
using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 验证 P0-11 复活闭环：ReviveService.TryRevive 在四种典型情况下的行为对齐
/// 《产品愿景》死亡-复活段四条验收。
/// </summary>
public class ReviveServiceTests
{
	[Fact]
	public void TryRevive_SufficientMaterials_RevivesActorAndConsumesMaterials()
	{
		var state = CreateState();
		var reviver = AddReviver(state, "reviver", x: 5, y: 5, withHerbs: 100, withWater: 100);
		var corpse = SpawnCorpseFor(state, "victim");

		var outcome = ReviveService.TryRevive(state, corpse, reviver, ReviveService.Methods.Magic, currentTurn: 10, rng: new Random(0));

		Assert.True(outcome.Success, $"Expected success but got failure: {outcome.FailureReason}");
		Assert.Equal("victim", outcome.RevivedActor!.Id);
		Assert.Equal(1, outcome.RevivedActor.RevivalCount);

		var revived = state.Actors["victim"];
		Assert.False(CombatModule.IsDead(revived));
		Assert.Equal(0f, revived.PainValue);
		Assert.Equal(0f, revived.BloodLossValue);
		Assert.Empty(revived.HealthConditions);
		Assert.True(revived.Limbs.All(l => l.Durability >= l.MaxDurability / 2),
			"复活后所有肢体耐久应至少恢复到 50%。");

		var herbs = reviver.Inventory.Where(i => i.Id == "herb_root").Sum(i => i.SafeStackCount);
		var water = reviver.Inventory.Where(i => i.Id == "water").Sum(i => i.SafeStackCount);
		Assert.Equal(100 - 5, herbs);
		Assert.Equal(100 - 3, water);

		Assert.Single(outcome.Events);
		var ev = outcome.Events[0];
		Assert.Equal("actor_revived", ev.Type);
		Assert.Equal(ReviveService.Methods.Magic, ev.EffectType);
		Assert.Contains(revived.Thoughts, t => t.Id == "revived_memory" && t.MoodOffset < 0f);
		Assert.Contains(reviver.Thoughts, t => t.Id == "revived_ally" && t.MoodOffset > 0f);
	}

	[Fact]
	public void TryRevive_InsufficientMaterials_FailsWithVisibleEvent_AndDoesNotRevive()
	{
		var state = CreateState();
		// 1 herb 不够（magic 默认要 5）；剩 0 water。
		var reviver = AddReviver(state, "reviver", x: 5, y: 5, withHerbs: 1, withWater: 0);
		var corpse = SpawnCorpseFor(state, "victim");

		var outcome = ReviveService.TryRevive(state, corpse, reviver, ReviveService.Methods.Magic, currentTurn: 10, rng: new Random(0));

		Assert.False(outcome.Success);
		Assert.Equal(ReviveService.FailureReasons.InsufficientMaterials, outcome.FailureReason);

		// 仍是死的（没复活）。
		Assert.True(CombatModule.IsDead(state.Actors["victim"]),
			"资源不足必须不复活——victim 应仍处于死亡状态。");
		// 计数没动。
		Assert.Equal(0, state.Actors["victim"].RevivalCount);
		// 资源没扣（关键：失败不消耗）。
		Assert.Equal(1, reviver.Inventory.Where(i => i.Id == "herb_root").Sum(i => i.SafeStackCount));

		// 失败事件可见（GameEventPresentationRouter / LogModule 据此本地化展示）。
		Assert.Single(outcome.Events);
		var ev = outcome.Events[0];
		Assert.Equal("revival_failed", ev.Type);
		Assert.Equal(ReviveService.Methods.Magic, ev.EffectType);
		Assert.Equal(ReviveService.FailureReasons.InsufficientMaterials, ev.ActionName);
	}

	[Fact]
	public void TryRevive_RepeatedRevivals_ScarMoodOffsetAccumulatesByPriorRevivalCount()
	{
		var state = CreateState();
		// 准备超量材料（让 perAttemptMultiplier 多次复活也够）。
		var reviver = AddReviver(state, "reviver", x: 5, y: 5, withHerbs: 1000, withWater: 1000);

		// Turn 1: 第一次复活（priorRevivalCount=0）。
		var corpse1 = SpawnCorpseFor(state, "victim");
		var firstOutcome = ReviveService.TryRevive(state, corpse1, reviver, ReviveService.Methods.Magic, currentTurn: 10, rng: new Random(0));
		Assert.True(firstOutcome.Success);
		var firstScar = state.Actors["victim"].Thoughts.First(t => t.Id == "revived_memory").MoodOffset;
		var firstExpected = RevivalCostModel.ComputeRevivalScar(0).MoodOffset;
		Assert.Equal(firstExpected, firstScar);

		// 重新"杀"victim（清 limbs）然后第二次复活（priorRevivalCount=1）。
		KillVictim(state.Actors["victim"]);
		// 重新生成同 SourceActorId 的 corpse 让 ReviveService.FindSourceActor 命中。
		var corpse2 = SpawnCorpseFor(state, "victim");
		var secondOutcome = ReviveService.TryRevive(state, corpse2, reviver, ReviveService.Methods.Magic, currentTurn: 100, rng: new Random(1));
		Assert.True(secondOutcome.Success);
		Assert.Equal(2, state.Actors["victim"].RevivalCount);

		var secondScar = state.Actors["victim"].Thoughts
			.Where(t => t.Id == "revived_memory").Last().MoodOffset;
		var secondExpected = RevivalCostModel.ComputeRevivalScar(1).MoodOffset;
		Assert.Equal(secondExpected, secondScar);
		Assert.True(secondExpected < firstExpected,
			$"第 2 次复活的 mood scar 应该比第 1 次更负；first={firstExpected}, second={secondExpected}");

		// 材料消耗也按 perAttemptMultiplier 复利上升。
		var firstHerbCost = RevivalCostModel.ComputeMaterialCost(ReviveService.Methods.Magic, 0)["herb_root"];
		var secondHerbCost = RevivalCostModel.ComputeMaterialCost(ReviveService.Methods.Magic, 1)["herb_root"];
		Assert.True(secondHerbCost > firstHerbCost,
			$"第 2 次复活的 herb_root 用量应大于第 1 次；first={firstHerbCost}, second={secondHerbCost}");
	}

	[Fact]
	public void TryRevive_Misfire_ConsumesHalfMaterials_AndReturnsFailureEvent()
	{
		// 掷骰失败分支：BaseFailureChance=1 让 ComputeFailureChance 强制返回 1（clamped by MaxFailureChance=1）；
		// NextDouble() 返回值永远 < 1，保证走 "spell_misfire" 路径。
		var state = CreateState(overrideFailureChance: 1.0f, overrideMaxFailureChance: 1.0f);
		var reviver = AddReviver(state, "reviver", x: 5, y: 5, withHerbs: 100, withWater: 100);
		var corpse = SpawnCorpseFor(state, "victim");

		var outcome = ReviveService.TryRevive(state, corpse, reviver, ReviveService.Methods.Magic, currentTurn: 10, rng: new Random(0));

		Assert.False(outcome.Success);
		Assert.Equal(ReviveService.FailureReasons.SpellMisfire, outcome.FailureReason);

		// 失败路径不改 victim 状态：仍死、RevivalCount 不变。
		Assert.True(CombatModule.IsDead(state.Actors["victim"]));
		Assert.Equal(0, state.Actors["victim"].RevivalCount);

		// 仪式感：失败也付代价——材料按 ceil(fullCost * 0.5f) 扣掉，和成功差一半。
		// magic 默认 herb_root=5 / water=3，misfire 应扣 ceil(2.5)=3 / ceil(1.5)=2。
		var herbsLeft = reviver.Inventory.Where(i => i.Id == "herb_root").Sum(i => i.SafeStackCount);
		var waterLeft = reviver.Inventory.Where(i => i.Id == "water").Sum(i => i.SafeStackCount);
		Assert.Equal(100 - 3, herbsLeft);
		Assert.Equal(100 - 2, waterLeft);

		// 事件可见：revival_failed + reason=spell_misfire。
		Assert.Single(outcome.Events);
		var ev = outcome.Events[0];
		Assert.Equal("revival_failed", ev.Type);
		Assert.Equal(ReviveService.Methods.Magic, ev.EffectType);
		Assert.Equal(ReviveService.FailureReasons.SpellMisfire, ev.ActionName);
	}

	[Fact]
	public void TryRevive_PermanentlyLostActor_RejectsWithFailureReason()
	{
		var state = CreateState();
		var reviver = AddReviver(state, "reviver", x: 5, y: 5, withHerbs: 1000, withWater: 1000);
		var corpse = SpawnCorpseFor(state, "victim");
		// 直接把 victim.RevivalCount 拉到永久死亡阈值。
		state.Actors["victim"].RevivalCount = 6;

		var outcome = ReviveService.TryRevive(state, corpse, reviver, ReviveService.Methods.Magic, currentTurn: 10, rng: new Random(0));

		Assert.False(outcome.Success);
		Assert.Equal(ReviveService.FailureReasons.PermanentlyLost, outcome.FailureReason);

		// 失败事件可见（4-th 验收：失败必须给玩家看到具体失败文本，禁止沉默 ok=false）。
		Assert.Single(outcome.Events);
		var ev = outcome.Events[0];
		Assert.Equal("revival_failed", ev.Type);
		Assert.Equal(ReviveService.FailureReasons.PermanentlyLost, ev.ActionName);

		// 资源没扣。
		Assert.Equal(1000, reviver.Inventory.Where(i => i.Id == "herb_root").Sum(i => i.SafeStackCount));
		// RevivalCount 不变。
		Assert.Equal(6, state.Actors["victim"].RevivalCount);
		// 仍处于"已死"状态——不会被复活。
		Assert.True(CombatModule.IsDead(state.Actors["victim"]));
	}

	/// <summary>
	/// 愿景第 4 条"失败后果可见 + 可能的产出"：PermanentlyLost 时应在 reviver 脚下物化
	/// 一块 soul_fragment + 发一条 revival_soul_fragment_dropped 事件，失败不再沉默。
	/// </summary>
	[Fact]
	public void TryRevive_PermanentlyLost_WithWorld_DropsSoulFragmentAtReviverFeet()
	{
		var state = CreateStateWithWorld();
		var reviver = AddReviver(state, "reviver", x: 5, y: 5, withHerbs: 1000, withWater: 1000);
		var corpse = SpawnCorpseFor(state, "victim");
		state.Actors["victim"].RevivalCount = 6;

		var outcome = ReviveService.TryRevive(state, corpse, reviver, ReviveService.Methods.Magic, currentTurn: 10, rng: new Random(0));

		Assert.False(outcome.Success);
		Assert.Equal(ReviveService.FailureReasons.PermanentlyLost, outcome.FailureReason);
		// 两条事件：revival_failed + revival_soul_fragment_dropped。
		Assert.Equal(2, outcome.Events.Count);
		Assert.Equal("revival_failed", outcome.Events[0].Type);
		var drop = outcome.Events[1];
		Assert.Equal("revival_soul_fragment_dropped", drop.Type);
		Assert.Equal(ReviveService.FailureReasons.PermanentlyLost, drop.ActionName);
		Assert.Equal(reviver.X, drop.TargetX);
		Assert.Equal(reviver.Y, drop.TargetY);
		Assert.Equal(reviver.Z, drop.TargetZ);

		// 物品真的落到了世界上。
		var ground = state.World!.GetEntitiesByType(reviver.X, reviver.Y, reviver.Z, CellEntityType.Item);
		Assert.Contains(ground, entity => WorldMap.ResolveGroundItemTemplateId(entity) == ReviveService.SoulFragmentItemId);
	}

	/// <summary>
	/// SpellMisfire 也应掉一块 soul_fragment——愿景要求"失败必须有产出"，
	/// 让玩家在炸法后除了扣材料外还有一点"带出来的灵魂残片"可回收的手感。
	/// </summary>
	[Fact]
	public void TryRevive_SpellMisfire_WithWorld_DropsSoulFragmentAndConsumesHalfMaterials()
	{
		var state = CreateStateWithWorld(overrideFailureChance: 1.0f, overrideMaxFailureChance: 1.0f);
		var reviver = AddReviver(state, "reviver", x: 5, y: 5, withHerbs: 100, withWater: 100);
		var corpse = SpawnCorpseFor(state, "victim");

		var outcome = ReviveService.TryRevive(state, corpse, reviver, ReviveService.Methods.Magic, currentTurn: 10, rng: new Random(0));

		Assert.False(outcome.Success);
		Assert.Equal(ReviveService.FailureReasons.SpellMisfire, outcome.FailureReason);
		Assert.Equal(2, outcome.Events.Count);
		Assert.Equal("revival_failed", outcome.Events[0].Type);
		Assert.Equal("revival_soul_fragment_dropped", outcome.Events[1].Type);

		// 材料按 ceil(fullCost * 0.5) 扣：herb_root 5→扣 3 剩 97；water 3→扣 2 剩 98。
		Assert.Equal(100 - 3, reviver.Inventory.Where(i => i.Id == "herb_root").Sum(i => i.SafeStackCount));
		Assert.Equal(100 - 2, reviver.Inventory.Where(i => i.Id == "water").Sum(i => i.SafeStackCount));

		var ground = state.World!.GetEntitiesByType(reviver.X, reviver.Y, reviver.Z, CellEntityType.Item);
		Assert.Contains(ground, entity => WorldMap.ResolveGroundItemTemplateId(entity) == ReviveService.SoulFragmentItemId);
	}

	/// <summary>
	/// InsufficientMaterials / NotACorpse 这类"前置检查"失败不该掉残魂——残魂代表"仪式残响",
	/// 只有真正开始施法（进入 PermanentlyLost 守门或 misfire 掷骰）才有；不然玩家会 farming。
	/// </summary>
	[Fact]
	public void TryRevive_InsufficientMaterials_DoesNotDropSoulFragment()
	{
		var state = CreateStateWithWorld();
		var reviver = AddReviver(state, "reviver", x: 5, y: 5, withHerbs: 1, withWater: 0);
		var corpse = SpawnCorpseFor(state, "victim");

		var outcome = ReviveService.TryRevive(state, corpse, reviver, ReviveService.Methods.Magic, currentTurn: 10, rng: new Random(0));

		Assert.False(outcome.Success);
		Assert.Equal(ReviveService.FailureReasons.InsufficientMaterials, outcome.FailureReason);
		Assert.Single(outcome.Events);
		Assert.DoesNotContain(outcome.Events, e => e.Type == "revival_soul_fragment_dropped");

		var ground = state.World!.GetEntitiesByType(reviver.X, reviver.Y, reviver.Z, CellEntityType.Item);
		Assert.DoesNotContain(ground, entity => WorldMap.ResolveGroundItemTemplateId(entity) == ReviveService.SoulFragmentItemId);
	}

	private static GameState CreateStateWithWorld(float overrideFailureChance = 0.0f, float overrideMaxFailureChance = 0.5f)
	{
		var state = CreateState(overrideFailureChance, overrideMaxFailureChance);
		state.World = new WorldMap(4242, new TestFlatFloorGenerator());
		return state;
	}

	private sealed class TestFlatFloorGenerator : IMapGenerator
	{
		public string Id => "flat_floor";
		public string Name => "Flat Floor";

		public void GenerateChunk(ChunkData chunk, int worldSeed) =>
			chunk.Fill(TerrainRegistry.GetId(Terrains.Floor));

		public void PopulateChunk(ChunkData chunk, int worldSeed) { }
	}

	private static GameState CreateState(float overrideFailureChance = 0.0f, float overrideMaxFailureChance = 0.5f)
	{
		TestSupport.EnsureGameplayDataLoaded();
		// 用最小配置覆盖 JSON：magic 只要 herb_root×5 + water×3，其它 method 留 1 件 dummy。
		// 让单测不依赖 Data/Config/revival_costs.json 是否在测试环境正确解析。
		RevivalCostModel.OverrideConfigForTesting(new RevivalCostsConfig
		{
			Methods = new Dictionary<string, RevivalMethodConfig>(StringComparer.Ordinal)
			{
				["magic"] = new RevivalMethodConfig
				{
					ChannelTurns = 8,
					BaseMaterials = new Dictionary<string, int>(StringComparer.Ordinal)
					{
						["herb_root"] = 5,
						["water"] = 3,
					},
					PerAttemptMultiplier = 1.5f,
					BaseFailureChance = overrideFailureChance,
					FailureChancePerPriorRevival = 0.0f,
					MaxFailureChance = overrideMaxFailureChance,
				},
			},
			MoodScar = new RevivalMoodScarConfig
			{
				BaseOffset = -4f,
				PerAttemptOffset = -4f,
				MaxOffset = -20f,
				BaseDurationTurns = 360,
				PerAttemptDurationTurns = 360,
				MaxDurationTurns = 1800,
			},
			CapacityPenalty = new RevivalCapacityPenaltyConfig { PerAttempt = 0.05f, MaxFraction = 0.4f },
			PermanentLossThreshold = 6,
		});
		return new GameState { PlayerId = "leader" };
	}

	private static Actor AddReviver(GameState state, string id, int x, int y, int withHerbs, int withWater)
	{
		var actor = new Actor
		{
			Id = id,
			DisplayName = id,
			Faction = Factions.Player,
			X = x,
			Y = y,
			Z = 0,
			Limbs =
			{
				new Limb
				{
					Id = $"{id}_torso",
					Name = "torso",
					BodyPart = BodyParts.Torso,
					Capacities =
					{
						["blood_circulation"] = 1.0f,
						["consciousness"] = 1.0f,
					},
					MaxDurability = 10,
					Durability = 10,
					Tags = { [CombatModule.VitalTag] = 1 },
				},
			},
		};
		if (withHerbs > 0)
			actor.Inventory.Add(MakeStack("herb_root", withHerbs));
		if (withWater > 0)
			actor.Inventory.Add(MakeStack("water", withWater));
		state.Actors[id] = actor;
		return actor;
	}

	private static Item MakeStack(string templateId, int count) => new()
	{
		Id = templateId,
		InstanceId = $"item_{templateId}_{Guid.NewGuid():N}",
		Name = templateId,
		MaxStack = 999,
		StackCount = count,
	};

	private static Item SpawnCorpseFor(GameState state, string sourceActorId)
	{
		// 准备 victim：必须保留 limbs（vital + capacity 字段），否则 ResetActorForRevival
		// 没东西可恢复 / IsDead 仍判 true。"已死"由 durability=0 表示，不清 limbs 列表。
		if (!state.Actors.ContainsKey(sourceActorId))
		{
			var deadActor = new Actor
			{
				Id = sourceActorId,
				DisplayName = sourceActorId,
				Faction = Factions.Friendly,
				Limbs =
				{
					new Limb
					{
						Id = $"{sourceActorId}_torso",
						Name = "torso",
						BodyPart = BodyParts.Torso,
						Capacities =
						{
							["blood_circulation"] = 1.0f,
							["consciousness"] = 1.0f,
						},
						MaxDurability = 10,
						Durability = 0,
						Tags = { [CombatModule.VitalTag] = 1 },
					},
				},
			};
			deadActor.InvalidateCapacityCache();
			state.Actors[sourceActorId] = deadActor;
		}
		else
		{
			KillVictim(state.Actors[sourceActorId]);
		}

		return new Item
		{
			Id = $"corpse_{sourceActorId}",
			InstanceId = $"corpse_{sourceActorId}_{Guid.NewGuid():N}",
			Name = "corpse",
			Corpse = new ItemCorpseMetadata
			{
				CorpseProfileId = "default",
				SourceActorId = sourceActorId,
				SourceActorName = sourceActorId,
			},
		};
	}

	private static void KillVictim(Actor actor)
	{
		// 把所有 limb durability 打到 0（保留 limb 列表 + tag/cap 字段供复活后恢复）。
		foreach (var limb in actor.Limbs)
			limb.Durability = 0;
		actor.HealthConditions.Clear();
		actor.PainValue = 0f;
		actor.BloodLossValue = 0f;
		actor.InvalidateCapacityCache();
	}
}
