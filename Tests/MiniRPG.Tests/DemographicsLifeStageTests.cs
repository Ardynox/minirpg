using System.Collections.Generic;
using MiniRPG.Core.Calendar;
using MiniRPG.Core.Data;
using MiniRPG.Core.Demographics;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 守护 LifeStageCatalog / LifeStageTransitionService / DemographicsService 的语义：
/// - 年龄从 BirthTurn 反算（legacy 兜底常量）；
/// - GetLifeStage 按 race-specific 阈值映射；
/// - LifeStageTransitionService 跨阶段才 emit 事件，并清 Infant carry 状态；
/// - DemographicsService 薄壳门面对外接口稳定。
/// </summary>
public sealed class DemographicsLifeStageTests
{
	private static void OverrideLifeStageHumanOnly()
	{
		LifeStageCatalog.OverrideForTesting(
			new LifeStageCatalogRoot
			{
				Races = new Dictionary<string, RaceLifeStageBounds>(System.StringComparer.OrdinalIgnoreCase)
				{
					["default"] = new RaceLifeStageBounds
					{
						InfantToYears = 3,
						ChildToYears = 13,
						AdolescentToYears = 18,
						AdultToYears = 60,
						MaxLifespanYears = 90,
					},
					["human"] = new RaceLifeStageBounds
					{
						InfantToYears = 3,
						ChildToYears = 13,
						AdolescentToYears = 18,
						AdultToYears = 60,
						MaxLifespanYears = 90,
					},
				},
			},
			new ConceptionCatalogRoot { GestationTurns = 2160 });
	}

	[Fact]
	public void AgeYears_BirthTurnNegative_ReturnsLegacyConstant()
	{
		OverrideLifeStageHumanOnly();
		var actor = new Actor { Id = "legacy", BirthTurn = -1 };
		var state = new GameState { PlayerId = "p", Turn = 100000 };

		Assert.Equal(LifeStageCatalog.LegacyUnknownAgeYears, LifeStageCatalog.AgeYears(state, actor));
	}

	[Fact]
	public void AgeYears_BirthTurnSet_AgeAdvancesByTurnsPerYear()
	{
		OverrideLifeStageHumanOnly();
		var tpy = LifeStageCatalog.TurnsPerYear;
		var actor = new Actor { Id = "kid", BirthTurn = 0 };

		Assert.Equal(0, LifeStageCatalog.AgeYears(new GameState { PlayerId = "p", Turn = 0 }, actor));
		Assert.Equal(0, LifeStageCatalog.AgeYears(new GameState { PlayerId = "p", Turn = tpy - 1 }, actor));
		Assert.Equal(1, LifeStageCatalog.AgeYears(new GameState { PlayerId = "p", Turn = tpy }, actor));
		Assert.Equal(7, LifeStageCatalog.AgeYears(new GameState { PlayerId = "p", Turn = tpy * 7 + 5 }, actor));
	}

	[Fact]
	public void GetLifeStage_AcrossThresholds_StagesUpInOrder()
	{
		OverrideLifeStageHumanOnly();
		var tpy = LifeStageCatalog.TurnsPerYear;
		var actor = new Actor
		{
			Id = "x",
			BirthTurn = 0,
			Race = new Race { Id = "human" },
		};

		Assert.Equal(LifeStage.Infant, LifeStageCatalog.GetLifeStage(new GameState { PlayerId = "p", Turn = tpy * 0 }, actor));
		Assert.Equal(LifeStage.Infant, LifeStageCatalog.GetLifeStage(new GameState { PlayerId = "p", Turn = tpy * 2 }, actor));
		Assert.Equal(LifeStage.Child, LifeStageCatalog.GetLifeStage(new GameState { PlayerId = "p", Turn = tpy * 3 }, actor));
		Assert.Equal(LifeStage.Adolescent, LifeStageCatalog.GetLifeStage(new GameState { PlayerId = "p", Turn = tpy * 13 }, actor));
		Assert.Equal(LifeStage.Adult, LifeStageCatalog.GetLifeStage(new GameState { PlayerId = "p", Turn = tpy * 18 }, actor));
		Assert.Equal(LifeStage.Adult, LifeStageCatalog.GetLifeStage(new GameState { PlayerId = "p", Turn = tpy * 50 }, actor));
		Assert.Equal(LifeStage.Elder, LifeStageCatalog.GetLifeStage(new GameState { PlayerId = "p", Turn = tpy * 60 }, actor));
	}

	[Fact]
	public void GetLifeStage_UnknownRace_FallsBackToDefault()
	{
		OverrideLifeStageHumanOnly();
		var tpy = LifeStageCatalog.TurnsPerYear;
		var actor = new Actor { Id = "alien", BirthTurn = 0, Race = new Race { Id = "alien" } };

		// race 找不到 → fallback default → 同样 60 岁后是 Elder。
		Assert.Equal(LifeStage.Elder, LifeStageCatalog.GetLifeStage(new GameState { PlayerId = "p", Turn = tpy * 60 }, actor));
	}

	[Fact]
	public void IsAdultOrOlder_OnlyTrueForAdultOrElder()
	{
		Assert.False(LifeStageCatalog.IsAdultOrOlder(LifeStage.Infant));
		Assert.False(LifeStageCatalog.IsAdultOrOlder(LifeStage.Child));
		Assert.False(LifeStageCatalog.IsAdultOrOlder(LifeStage.Adolescent));
		Assert.True(LifeStageCatalog.IsAdultOrOlder(LifeStage.Adult));
		Assert.True(LifeStageCatalog.IsAdultOrOlder(LifeStage.Elder));
	}

	[Fact]
	public void LifeStageTransitionService_StableStage_NoEvent()
	{
		OverrideLifeStageHumanOnly();
		var actor = new Actor
		{
			Id = "stable_adult",
			BirthTurn = 0,
			Race = new Race { Id = "human" },
			LastResolvedLifeStage = LifeStage.Adult,
		};
		var state = new GameState { PlayerId = "p", Turn = LifeStageCatalog.TurnsPerYear * 30 };
		state.Actors["stable_adult"] = actor;

		var events = LifeStageTransitionService.OnNewDay(state);
		Assert.Empty(events);
	}

	[Fact]
	public void LifeStageTransitionService_CrossingThreshold_EmitsTransitionEvent()
	{
		OverrideLifeStageHumanOnly();
		var actor = new Actor
		{
			Id = "newborn",
			BirthTurn = 0,
			Race = new Race { Id = "human" },
			LastResolvedLifeStage = LifeStage.Infant,
		};
		var state = new GameState { PlayerId = "p", Turn = LifeStageCatalog.TurnsPerYear * 5 };
		state.Actors["newborn"] = actor;

		var events = LifeStageTransitionService.OnNewDay(state);

		Assert.Single(events);
		Assert.Equal("lifestage_transition", events[0].Type);
		Assert.Equal("newborn", events[0].TargetId);
		Assert.Equal(LifeStage.Child.ToString(), events[0].FailureReason);
		Assert.Equal(LifeStage.Infant.ToString(), events[0].ActionName);
		Assert.Equal(LifeStage.Child, actor.LastResolvedLifeStage);
	}

	[Fact]
	public void LifeStageTransitionService_InfantToChild_ClearsCarriedBy()
	{
		OverrideLifeStageHumanOnly();
		var actor = new Actor
		{
			Id = "baby_grew_up",
			BirthTurn = 0,
			Race = new Race { Id = "human" },
			LastResolvedLifeStage = LifeStage.Infant,
			CarriedByActorId = "mother_id",
		};
		var state = new GameState { PlayerId = "p", Turn = LifeStageCatalog.TurnsPerYear * 4 };
		state.Actors["baby_grew_up"] = actor;

		LifeStageTransitionService.OnNewDay(state);

		Assert.Null(actor.CarriedByActorId);
	}

	[Fact]
	public void DemographicsService_IsPregnant_ReadsActorField()
	{
		Assert.False(DemographicsService.IsPregnant(new Actor()));
		Assert.False(DemographicsService.IsPregnant(new Actor { PregnancyTicksRemaining = null }));
		Assert.False(DemographicsService.IsPregnant(new Actor { PregnancyTicksRemaining = 0 }));
		Assert.True(DemographicsService.IsPregnant(new Actor { PregnancyTicksRemaining = 5 }));
	}

	[Fact]
	public void DemographicsService_IsCarriedAndHasCarriedInfant_ReadsFields()
	{
		Assert.False(DemographicsService.IsCarried(new Actor()));
		Assert.True(DemographicsService.IsCarried(new Actor { CarriedByActorId = "mother" }));
		Assert.False(DemographicsService.HasCarriedInfant(new Actor()));
		Assert.True(DemographicsService.HasCarriedInfant(new Actor { CarriedInfantId = "baby" }));
	}
}
