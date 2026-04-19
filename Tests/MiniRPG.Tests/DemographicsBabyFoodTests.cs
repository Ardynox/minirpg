using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.AI;
using MiniRPG.Core.AI.Utility;
using MiniRPG.Core.Calendar;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Demographics;
using MiniRPG.Core.Genetics;
using MiniRPG.Core.Needs;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 守护"婴儿吃奶"链路：
/// - newborn 生成时切到 human_infant need profile，自动有 baby_food need；
/// - 长大跨阶段 Infant→Child 自动切回默认 profile，baby_food 消失；
/// - TendInfantExecutor 真消耗 mother inventory 一份食物 → 提升 baby.baby_food；
/// - 没食物 / 没婴儿 / 婴儿失踪等边界都不抛。
/// </summary>
public sealed class DemographicsBabyFoodTests
{
	private static GeneDef MakeGene(string id) => new()
	{
		Id = id,
		Locus = id,
		Dominance = GeneDominance.Dominant,
		AlleleA = "A",
		AlleleB = "a",
		TagsIfADominant = new Dictionary<string, int>(),
		TagsIfHomozygousB = new Dictionary<string, int>(),
		TagsIfCodominantMixed = new Dictionary<string, int>(),
	};

	private static void OverrideAll(int gestationTurns = 50)
	{
		PresetDB.Load();
		LifeStageCatalog.OverrideForTesting(
			new LifeStageCatalogRoot
			{
				Races = new Dictionary<string, RaceLifeStageBounds>(System.StringComparer.OrdinalIgnoreCase)
				{
					["default"] = new RaceLifeStageBounds(),
					["human"] = new RaceLifeStageBounds(),
				},
			},
			new ConceptionCatalogRoot
			{
				PairingRadius = 4,
				ConceptionChancePerDay = 1.0f,
				GestationTurns = gestationTurns,
			});
		GeneCatalog.OverrideForTesting(
			[MakeGene("alpha")],
			[
				new XenotypeDef
				{
					Id = "baseliner",
					RaceId = "human",
					FixedGenes = ["alpha"],
					RandomGenes = [],
				},
			]);
	}

	private static Actor MakeAdultHuman(string id, Sex sex, int x = 0)
	{
		var tpy = LifeStageCatalog.TurnsPerYear;
		return new Actor
		{
			Id = id,
			Race = new Race { Id = "human" },
			Sex = sex,
			BirthTurn = -tpy * 25,
			X = x,
			Y = 0,
			Z = 0,
			Faction = Factions.Friendly,
			TemplateId = "player",
			Genome = new GenePool { Loci = { ["alpha"] = new LocusPair { A = "A", B = "A" } } },
		};
	}

	[Fact]
	public void Newborn_GetsHumanInfantNeedProfile_AndBabyFoodNeed()
	{
		OverrideAll();
		var state = new GameState { PlayerId = "p", Turn = 1000 };
		var mom = MakeAdultHuman("mom", Sex.Female);
		mom.PregnancyTicksRemaining = 30;
		state.Actors["mom"] = mom;

		ConceptionBirthTick.OnNewDay(state);

		var newborn = state.Actors.Values.First(a => a.MotherActorId == "mom");
		Assert.Equal("human_infant", newborn.Race?.NeedProfileId);
		Assert.True(newborn.Needs.ContainsKey(NeedIds.BabyFood),
			$"Expected newborn to have baby_food need, got: {string.Join(",", newborn.Needs.Keys)}");
		Assert.False(newborn.Needs.ContainsKey(NeedIds.Hunger), "Infant 不该有 hunger（用 baby_food 替代）");
		Assert.Equal(LifeStage.Infant, newborn.LastResolvedLifeStage);
	}

	[Fact]
	public void LifeStageTransition_InfantToChild_RestoresDefaultProfile()
	{
		OverrideAll();
		var tpy = LifeStageCatalog.TurnsPerYear;
		var state = new GameState { PlayerId = "p", Turn = tpy * 5 };
		var grew = new Actor
		{
			Id = "grew",
			Race = new Race { Id = "human", NeedProfileId = "human_infant" },
			BirthTurn = 0, // 5 岁 → Child
			LastResolvedLifeStage = LifeStage.Infant,
		};
		state.Actors["grew"] = grew;

		LifeStageTransitionService.OnNewDay(state);

		Assert.Equal("", grew.Race!.NeedProfileId);
		Assert.True(grew.Needs.ContainsKey(NeedIds.Hunger), "成长后应回到默认 profile 含 hunger");
		Assert.False(grew.Needs.ContainsKey(NeedIds.BabyFood), "baby_food 应被 EnsureInitialized 清掉");
	}

	[Fact]
	public void TendInfantExecutor_NoCarriedInfant_NoOp()
	{
		var state = new GameState { PlayerId = "p", Turn = 0 };
		var actor = new Actor { Id = "a" };
		state.Actors["a"] = actor;

		var result = new TendInfantExecutor().Execute(state, actor, BuildPerception(actor), default);
		Assert.False(result.Consumed);
	}

	[Fact]
	public void TendInfantExecutor_InfantMissing_NoOp()
	{
		var state = new GameState { PlayerId = "p", Turn = 0 };
		var mom = new Actor { Id = "mom", CarriedInfantId = "ghost_baby" };
		state.Actors["mom"] = mom;

		var result = new TendInfantExecutor().Execute(state, mom, BuildPerception(mom), default);
		Assert.False(result.Consumed);
	}

	[Fact]
	public void TendInfantExecutor_HasFood_ConsumesAndFeedsBaby()
	{
		OverrideAll();
		var state = new GameState { PlayerId = "p", Turn = 100 };
		var mom = PresetDB.SpawnActor("player", "mom");
		mom.CarriedInfantId = "baby";
		var berry = new Item
		{
			Id = "berries",
			InstanceId = "berries_1",
			Name = "Berries",
			Category = ItemCategories.Food,
			Tags = new Dictionary<string, int> { [ItemTags.Nutrition] = 2 },
		};
		mom.Inventory.Add(berry);

		var baby = new Actor
		{
			Id = "baby",
			Race = new Race { Id = "human", NeedProfileId = "human_infant" },
			BirthTurn = state.Turn,
		};
		NeedSystem.EnsureInitialized(baby, state.Turn);
		NeedSystem.SetNeedValue(baby, NeedIds.BabyFood, 20f);
		state.Actors["mom"] = mom;
		state.Actors["baby"] = baby;

		var result = new TendInfantExecutor().Execute(state, mom, BuildPerception(mom), default);

		Assert.True(result.Consumed);
		Assert.Empty(mom.Inventory); // 食物被消耗
		var babyFood = NeedSystem.GetNeedValue(baby, NeedIds.BabyFood);
		Assert.True(babyFood > 20f, $"baby_food 应 > 20，实际 {babyFood}");
		Assert.Contains(result.Events, e => e.Type == "infant_nursed" && e.TargetId == "baby");
	}

	[Fact]
	public void TendInfantExecutor_NoFood_StillConsumesTurnButDoesNotEmitNursed()
	{
		OverrideAll();
		var state = new GameState { PlayerId = "p", Turn = 100 };
		var mom = PresetDB.SpawnActor("player", "mom");
		mom.CarriedInfantId = "baby";

		var baby = new Actor
		{
			Id = "baby",
			Race = new Race { Id = "human", NeedProfileId = "human_infant" },
			BirthTurn = state.Turn,
		};
		NeedSystem.EnsureInitialized(baby, state.Turn);
		var initialBabyFood = NeedSystem.GetNeedValue(baby, NeedIds.BabyFood);
		state.Actors["mom"] = mom;
		state.Actors["baby"] = baby;

		var result = new TendInfantExecutor().Execute(state, mom, BuildPerception(mom), default);

		Assert.True(result.Consumed);
		Assert.DoesNotContain(result.Events, e => e.Type == "infant_nursed");
		Assert.Equal(initialBabyFood, NeedSystem.GetNeedValue(baby, NeedIds.BabyFood));
	}

	private static Perception BuildPerception(Actor self) => new()
	{
		Self = self,
		Turn = 0,
	};
}
