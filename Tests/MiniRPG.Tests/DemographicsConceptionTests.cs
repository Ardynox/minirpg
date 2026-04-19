using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Calendar;
using MiniRPG.Core.Data;
using MiniRPG.Core.Demographics;
using MiniRPG.Core.Genetics;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 守护 ConceptionBirthTick + WorldDemographicsTick 的核心承诺：
/// - day boundary 才触发（state.LastDemographicsDay 跟踪）；
/// - 受孕条件全满足（Adult+ + 异性 + 同 race + radius 内 + 非敌对）+ 概率掷骰才怀孕；
/// - 怀孕 PregnancyTicksRemaining 按天减 TurnsPerDay；
/// - 倒计时归零 spawn 新生儿，孩子 BirthTurn / Mother / Father / Genome 正确写入；
/// - GeneInheritanceService 真正接通（孩子 Genome 含父母 locus）。
/// </summary>
public sealed class DemographicsConceptionTests
{
	private static GeneDef MakeGene(string id) => new()
	{
		Id = id,
		Locus = id,
		Dominance = GeneDominance.Dominant,
		AlleleA = "A",
		AlleleB = "a",
		TagsIfADominant = new Dictionary<string, int> { ["dom"] = 1 },
		TagsIfHomozygousB = new Dictionary<string, int>(),
		TagsIfCodominantMixed = new Dictionary<string, int>(),
	};

	private static void OverrideAll(int gestationTurns = 50, float conceptionChance = 1.0f, int pairingRadius = 4)
	{
		// 出生流程会调 PresetDB.SpawnActor(templateId, ...)，需要预设字典已加载。
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
				PairingRadius = pairingRadius,
				ConceptionChancePerDay = conceptionChance,
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

	private static Actor MakeAdultHuman(string id, Sex sex, int x = 0, int y = 0)
	{
		var tpy = LifeStageCatalog.TurnsPerYear;
		return new Actor
		{
			Id = id,
			Race = new Race { Id = "human" },
			Sex = sex,
			BirthTurn = -tpy * 25, // 25 岁 = Adult
			X = x,
			Y = y,
			Z = 0,
			Faction = Factions.Friendly,
			TemplateId = "player", // 走 Data/actors.json 里真实存在的 template；Phase 4 ConceptionBirthTick.SpawnChild 复用母亲的 templateId。
			Genome = new GenePool { Loci = { ["alpha"] = new LocusPair { A = "A", B = "A" } } },
		};
	}

	[Fact]
	public void WorldDemographicsTick_SameDay_NoOp()
	{
		OverrideAll();
		var state = new GameState { PlayerId = "p", Turn = 5, LastDemographicsDay = 0 };
		// LastDemographicsDay = 0，currentDay = 5/120 = 0 → 不该触发。
		var events = WorldDemographicsTick.Tick(state);
		Assert.Empty(events);
	}

	[Fact]
	public void WorldDemographicsTick_NextDay_TriggersOnce()
	{
		OverrideAll();
		var state = new GameState
		{
			PlayerId = "p",
			Turn = DayNightCycle.TurnsPerDay,
			LastDemographicsDay = 0,
		};
		// currentDay = 1，应触发一次。
		WorldDemographicsTick.Tick(state);
		Assert.Equal(1, state.LastDemographicsDay);

		// 同一天再调一次不再触发（无新事件）。
		var second = WorldDemographicsTick.Tick(state);
		Assert.Empty(second);
	}

	[Fact]
	public void ConceptionBirthTick_AdvancePregnancy_DecrementsByTurnsPerDay()
	{
		OverrideAll();
		var state = new GameState { PlayerId = "p", Turn = DayNightCycle.TurnsPerDay };
		var mom = MakeAdultHuman("mom", Sex.Female);
		mom.PregnancyTicksRemaining = DayNightCycle.TurnsPerDay * 5; // 5 天
		state.Actors["mom"] = mom;

		ConceptionBirthTick.OnNewDay(state);

		Assert.Equal(DayNightCycle.TurnsPerDay * 4, mom.PregnancyTicksRemaining);
	}

	[Fact]
	public void ConceptionBirthTick_PregnancyExpires_TriggersBirth()
	{
		OverrideAll();
		var state = new GameState { PlayerId = "p", Turn = 1000 };
		var mom = MakeAdultHuman("mom", Sex.Female);
		mom.PregnancyTicksRemaining = 30; // 不足 1 天
		state.Actors["mom"] = mom;

		var events = ConceptionBirthTick.OnNewDay(state);

		Assert.Null(mom.PregnancyTicksRemaining);
		Assert.Contains(events, e => e.Type == "birth" && e.InitiatorId == "mom");

		// 找到 newborn 验证字段。
		var newborn = state.Actors.Values.FirstOrDefault(a => a.MotherActorId == "mom");
		Assert.NotNull(newborn);
		Assert.Equal(1000, newborn!.BirthTurn);
		Assert.NotNull(newborn.Genome);
		Assert.Contains(newborn.Genome.Loci.Keys, k => k == "alpha");
		Assert.True(newborn.Sex is Sex.Female or Sex.Male);
	}

	[Fact]
	public void ConceptionBirthTick_TwoEligibleAdults_FemaleConceives()
	{
		OverrideAll(gestationTurns: 100, conceptionChance: 1.0f);
		var state = new GameState { PlayerId = "p", Turn = 0 };
		state.Actors["mom"] = MakeAdultHuman("mom", Sex.Female, x: 0, y: 0);
		state.Actors["dad"] = MakeAdultHuman("dad", Sex.Male, x: 1, y: 0);

		var events = ConceptionBirthTick.OnNewDay(state);

		Assert.NotNull(state.Actors["mom"].PregnancyTicksRemaining);
		Assert.Equal("dad", state.Actors["mom"].MateActorId);
		Assert.Contains(events, e => e.Type == "conception" && e.InitiatorId == "mom" && e.TargetId == "dad");
	}

	[Fact]
	public void ConceptionBirthTick_NoMaleNearby_NoConception()
	{
		OverrideAll(gestationTurns: 100, conceptionChance: 1.0f);
		var state = new GameState { PlayerId = "p", Turn = 0 };
		state.Actors["mom"] = MakeAdultHuman("mom", Sex.Female);

		ConceptionBirthTick.OnNewDay(state);
		Assert.Null(state.Actors["mom"].PregnancyTicksRemaining);
	}

	[Fact]
	public void ConceptionBirthTick_MaleTooFar_NoConception()
	{
		OverrideAll(gestationTurns: 100, conceptionChance: 1.0f, pairingRadius: 2);
		var state = new GameState { PlayerId = "p", Turn = 0 };
		state.Actors["mom"] = MakeAdultHuman("mom", Sex.Female, x: 0, y: 0);
		state.Actors["dad"] = MakeAdultHuman("dad", Sex.Male, x: 10, y: 10);

		ConceptionBirthTick.OnNewDay(state);
		Assert.Null(state.Actors["mom"].PregnancyTicksRemaining);
	}

	[Fact]
	public void ConceptionBirthTick_HostileFemale_DoesNotConceive()
	{
		OverrideAll(gestationTurns: 100, conceptionChance: 1.0f);
		var state = new GameState { PlayerId = "p", Turn = 0 };
		var mom = MakeAdultHuman("mom", Sex.Female, x: 0, y: 0);
		mom.Faction = Factions.Hostile;
		state.Actors["mom"] = mom;
		state.Actors["dad"] = MakeAdultHuman("dad", Sex.Male, x: 1, y: 0);

		ConceptionBirthTick.OnNewDay(state);
		Assert.Null(state.Actors["mom"].PregnancyTicksRemaining);
	}

	[Fact]
	public void ConceptionBirthTick_DifferentRaces_DoesNotConceive()
	{
		OverrideAll(gestationTurns: 100, conceptionChance: 1.0f);
		GeneCatalog.OverrideForTesting(
			[MakeGene("alpha")],
			[
				new XenotypeDef { Id = "h", RaceId = "human", FixedGenes = ["alpha"], RandomGenes = [] },
				new XenotypeDef { Id = "g", RaceId = "goblin", FixedGenes = ["alpha"], RandomGenes = [] },
			]);
		var state = new GameState { PlayerId = "p", Turn = 0 };
		var mom = MakeAdultHuman("mom", Sex.Female);
		state.Actors["mom"] = mom;
		var dad = MakeAdultHuman("dad", Sex.Male, x: 1);
		dad.Race = new Race { Id = "goblin" };
		state.Actors["dad"] = dad;

		ConceptionBirthTick.OnNewDay(state);
		Assert.Null(state.Actors["mom"].PregnancyTicksRemaining);
	}

	[Fact]
	public void ConceptionBirthTick_AlreadyPregnant_DoesNotConceiveAgain()
	{
		OverrideAll(gestationTurns: 1000, conceptionChance: 1.0f);
		var state = new GameState { PlayerId = "p", Turn = 0 };
		var mom = MakeAdultHuman("mom", Sex.Female, x: 0);
		// 留 5 天 + 1 turn 的 buffer，避免本次 tick 触发出生让 mom 重新可受孕。
		mom.PregnancyTicksRemaining = DayNightCycle.TurnsPerDay * 5 + 1;
		mom.MateActorId = "dad_old";
		state.Actors["mom"] = mom;
		state.Actors["dad"] = MakeAdultHuman("dad", Sex.Male, x: 1);

		ConceptionBirthTick.OnNewDay(state);
		Assert.Equal("dad_old", mom.MateActorId);
		Assert.NotNull(mom.PregnancyTicksRemaining);
	}

	[Fact]
	public void ConceptionBirthTick_ZeroChance_NoConception()
	{
		OverrideAll(gestationTurns: 100, conceptionChance: 0f);
		var state = new GameState { PlayerId = "p", Turn = 0 };
		state.Actors["mom"] = MakeAdultHuman("mom", Sex.Female);
		state.Actors["dad"] = MakeAdultHuman("dad", Sex.Male, x: 1);

		ConceptionBirthTick.OnNewDay(state);
		Assert.Null(state.Actors["mom"].PregnancyTicksRemaining);
	}

	[Fact]
	public void ConceptionBirthTick_InfantFemale_DoesNotConceive()
	{
		OverrideAll(gestationTurns: 100, conceptionChance: 1.0f);
		var state = new GameState { PlayerId = "p", Turn = 0 };
		var infant = MakeAdultHuman("inf_mom", Sex.Female);
		infant.BirthTurn = 0; // 0 岁 → Infant
		state.Actors["inf_mom"] = infant;
		state.Actors["dad"] = MakeAdultHuman("dad", Sex.Male, x: 1);

		ConceptionBirthTick.OnNewDay(state);
		Assert.Null(state.Actors["inf_mom"].PregnancyTicksRemaining);
	}
}
