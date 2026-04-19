using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Calendar;
using MiniRPG.Core.Data;
using MiniRPG.Core.Demographics;
using MiniRPG.Core.Needs;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 守护亲属网络的查询语义 + 失亲悲恸链路：
/// - KinshipModule.GetParents/Children/Siblings/Mate/AllRelatives 反向查询正确；
/// - KinGriefHelper.BuildGriefTargets 把"victim 死了谁悲伤"列出（去重 + 关系标对）；
/// - KinshipLossCapturer.OnEvent 给亲属真正 ApplyThought lost_*；
/// - InfantCarryModule.OnNewDay 自动给 Infant 挂 mother / fallback father。
/// </summary>
public sealed class DemographicsKinshipTests
{
	private static void OverrideLifeStageHumanOnly()
	{
		LifeStageCatalog.OverrideForTesting(
			new LifeStageCatalogRoot
			{
				Races = new Dictionary<string, RaceLifeStageBounds>(System.StringComparer.OrdinalIgnoreCase)
				{
					["default"] = new RaceLifeStageBounds(),
					["human"] = new RaceLifeStageBounds(),
				},
			},
			new ConceptionCatalogRoot());
	}

	private static Actor MakeActor(string id, string? motherId = null, string? fatherId = null, string? mateId = null)
		=> new()
		{
			Id = id,
			Race = new Race { Id = "human" },
			MotherActorId = motherId,
			FatherActorId = fatherId,
			MateActorId = mateId,
		};

	[Fact]
	public void KinshipModule_GetParents_ReturnsBothWhenSet()
	{
		var state = new GameState { PlayerId = "p" };
		state.Actors["mom"] = MakeActor("mom");
		state.Actors["dad"] = MakeActor("dad");
		state.Actors["kid"] = MakeActor("kid", motherId: "mom", fatherId: "dad");

		var parents = KinshipModule.GetParents(state, state.Actors["kid"]).ToList();

		Assert.Equal(2, parents.Count);
		Assert.Contains(parents, p => p.Id == "mom");
		Assert.Contains(parents, p => p.Id == "dad");
	}

	[Fact]
	public void KinshipModule_GetParents_SkipsMissingActors()
	{
		var state = new GameState { PlayerId = "p" };
		state.Actors["kid"] = MakeActor("kid", motherId: "ghost_mom", fatherId: "ghost_dad");

		var parents = KinshipModule.GetParents(state, state.Actors["kid"]).ToList();
		Assert.Empty(parents);
	}

	[Fact]
	public void KinshipModule_GetChildren_ReversedLookup()
	{
		var state = new GameState { PlayerId = "p" };
		state.Actors["mom"] = MakeActor("mom");
		state.Actors["a"] = MakeActor("a", motherId: "mom");
		state.Actors["b"] = MakeActor("b", motherId: "mom");
		state.Actors["c"] = MakeActor("c", fatherId: "mom"); // mom 也可作 father（这里只测 id 匹配）

		var kids = KinshipModule.GetChildren(state, state.Actors["mom"]).Select(k => k.Id).ToList();
		Assert.Equal(3, kids.Count);
		Assert.Contains("a", kids);
		Assert.Contains("b", kids);
		Assert.Contains("c", kids);
	}

	[Fact]
	public void KinshipModule_GetSiblings_SharesParent()
	{
		var state = new GameState { PlayerId = "p" };
		state.Actors["mom"] = MakeActor("mom");
		state.Actors["a"] = MakeActor("a", motherId: "mom");
		state.Actors["b"] = MakeActor("b", motherId: "mom");
		state.Actors["c"] = MakeActor("c", motherId: "other");

		var siblings = KinshipModule.GetSiblings(state, state.Actors["a"]).Select(s => s.Id).ToList();
		Assert.Single(siblings);
		Assert.Equal("b", siblings[0]);
	}

	[Fact]
	public void KinshipModule_GetMate_ResolvesById()
	{
		var state = new GameState { PlayerId = "p" };
		state.Actors["alice"] = MakeActor("alice", mateId: "bob");
		state.Actors["bob"] = MakeActor("bob");

		Assert.Equal("bob", KinshipModule.GetMate(state, state.Actors["alice"])!.Id);
		Assert.Null(KinshipModule.GetMate(state, state.Actors["bob"]));
	}

	[Fact]
	public void KinshipModule_GetAllRelatives_DedupesAcrossCategories()
	{
		var state = new GameState { PlayerId = "p" };
		state.Actors["mom"] = MakeActor("mom");
		state.Actors["sibling"] = MakeActor("sibling", motherId: "mom");
		state.Actors["me"] = MakeActor("me", motherId: "mom", mateId: "sibling"); // 故意 spouse=sibling 触发去重

		var relatives = KinshipModule.GetAllRelatives(state, state.Actors["me"]).Select(r => r.Id).ToList();
		// mom + sibling 各一次（sibling 既是 spouse 又是 sibling，不应重复）。
		Assert.Equal(2, relatives.Count);
		Assert.Contains("mom", relatives);
		Assert.Contains("sibling", relatives);
	}

	[Fact]
	public void KinGriefHelper_BuildGriefTargets_AssignsRelationsCorrectly()
	{
		var state = new GameState { PlayerId = "p" };
		state.Actors["mom"] = MakeActor("mom");
		state.Actors["dad"] = MakeActor("dad");
		state.Actors["mate"] = MakeActor("mate");
		state.Actors["sibling"] = MakeActor("sibling", motherId: "mom");
		state.Actors["kid"] = MakeActor("kid", motherId: "victim");
		state.Actors["victim"] = MakeActor("victim", motherId: "mom", fatherId: "dad", mateId: "mate");

		var targets = KinGriefHelper.BuildGriefTargets(state, state.Actors["victim"]);

		// mom + dad 失去了 child；mate 失去了 spouse；sibling 失去了 sibling；kid 失去了 parent。
		var byId = targets.ToDictionary(t => t.SurvivorId, t => t.Relation);
		Assert.Equal(KinGriefHelper.RelationChild, byId["mom"]);
		Assert.Equal(KinGriefHelper.RelationChild, byId["dad"]);
		Assert.Equal(KinGriefHelper.RelationSpouse, byId["mate"]);
		Assert.Equal(KinGriefHelper.RelationSibling, byId["sibling"]);
		Assert.Equal(KinGriefHelper.RelationParent, byId["kid"]);
		Assert.Equal(5, targets.Count);
	}

	[Fact]
	public void KinGriefHelper_NoRelatives_ReturnsEmpty()
	{
		var state = new GameState { PlayerId = "p" };
		state.Actors["loner"] = MakeActor("loner");

		var targets = KinGriefHelper.BuildGriefTargets(state, state.Actors["loner"]);
		Assert.Empty(targets);
	}

	[Fact]
	public void KinGriefHelper_ResolveThoughtId_MapsAllRelations()
	{
		Assert.Equal("lost_child", KinGriefHelper.ResolveThoughtId(KinGriefHelper.RelationChild));
		Assert.Equal("lost_parent", KinGriefHelper.ResolveThoughtId(KinGriefHelper.RelationParent));
		Assert.Equal("lost_spouse", KinGriefHelper.ResolveThoughtId(KinGriefHelper.RelationSpouse));
		Assert.Equal("lost_family", KinGriefHelper.ResolveThoughtId(KinGriefHelper.RelationSibling));
		// fallback：未知 relation 走 lost_family 兜底。
		Assert.Equal("lost_family", KinGriefHelper.ResolveThoughtId("unknown"));
	}

	[Fact]
	public void KinGriefHelper_AppendToDeathEvent_PopulatesKinGriefTargets()
	{
		var state = new GameState { PlayerId = "p" };
		state.Actors["mom"] = MakeActor("mom");
		state.Actors["victim"] = MakeActor("victim", motherId: "mom");
		var ev = new GameEvent("actor_killed");

		KinGriefHelper.AppendToDeathEvent(state, state.Actors["victim"], ev);

		Assert.NotNull(ev.KinGriefTargets);
		Assert.Single(ev.KinGriefTargets);
		Assert.Equal("mom", ev.KinGriefTargets[0].SurvivorId);
	}

	[Fact]
	public void KinGriefHelper_AppendToDeathEvent_NoRelatives_LeavesEventUntouched()
	{
		var state = new GameState { PlayerId = "p" };
		state.Actors["loner"] = MakeActor("loner");
		var ev = new GameEvent("actor_killed");

		KinGriefHelper.AppendToDeathEvent(state, state.Actors["loner"], ev);

		Assert.Null(ev.KinGriefTargets);
	}

	[Fact]
	public void KinshipLossCapturer_OnEvent_AppliesThoughtToSurvivor()
	{
		// ApplyThought 依赖 NeedCatalog 已加载（解析 thought def）+ actor 走真实 Race / NeedProfile
		// 才能触发 AllowThoughts 分支；用 PresetDB.SpawnActor("player") 直接拿到完整 actor。
		PresetDB.Load();
		OverrideLifeStageHumanOnly();
		var state = new GameState { PlayerId = "p", Turn = 100 };
		var mom = PresetDB.SpawnActor("player", "mom");
		state.Actors["mom"] = mom;
		var ev = new GameEvent("actor_killed");
		ev.KinGriefTargets = [new KinGriefTarget { SurvivorId = "mom", Relation = KinGriefHelper.RelationChild }];

		new KinshipLossCapturer().OnEvent(state, ev);

		Assert.Contains(mom.Thoughts, t => t.Id == "lost_child");
	}

	[Fact]
	public void KinshipLossCapturer_OnEvent_NonDeathEvent_Ignored()
	{
		var state = new GameState { PlayerId = "p", Turn = 100 };
		state.Actors["mom"] = MakeActor("mom");
		var ev = new GameEvent("pickup_item");
		ev.KinGriefTargets = [new KinGriefTarget { SurvivorId = "mom", Relation = KinGriefHelper.RelationChild }];

		new KinshipLossCapturer().OnEvent(state, ev);

		Assert.DoesNotContain(state.Actors["mom"].Thoughts, t => t.Id == "lost_child");
	}

	[Fact]
	public void KinshipLossCapturer_OnEvent_MissingSurvivor_Skipped()
	{
		var state = new GameState { PlayerId = "p", Turn = 100 };
		var ev = new GameEvent("actor_killed");
		ev.KinGriefTargets = [new KinGriefTarget { SurvivorId = "ghost", Relation = KinGriefHelper.RelationChild }];

		// 不抛即可（survivor 已经不在 state.Actors 里——可能也死了）。
		new KinshipLossCapturer().OnEvent(state, ev);
	}

	[Fact]
	public void InfantCarryModule_OnNewDay_AssignsMotherAsCarrier()
	{
		OverrideLifeStageHumanOnly();
		var state = new GameState { PlayerId = "p", Turn = 0 };
		state.Actors["mom"] = MakeActor("mom");
		// infant：BirthTurn 刚出生 → Infant 阶段。
		var infant = MakeActor("baby", motherId: "mom");
		infant.BirthTurn = 0;
		state.Actors["baby"] = infant;

		InfantCarryModule.OnNewDay(state);

		Assert.Equal("mom", infant.CarriedByActorId);
		Assert.Equal("baby", state.Actors["mom"].CarriedInfantId);
	}

	[Fact]
	public void InfantCarryModule_OnNewDay_FallsBackToFatherWhenMotherMissing()
	{
		OverrideLifeStageHumanOnly();
		var state = new GameState { PlayerId = "p", Turn = 0 };
		state.Actors["dad"] = MakeActor("dad");
		var infant = MakeActor("baby", motherId: "ghost_mom", fatherId: "dad");
		infant.BirthTurn = 0;
		state.Actors["baby"] = infant;

		InfantCarryModule.OnNewDay(state);

		Assert.Equal("dad", infant.CarriedByActorId);
	}

	[Fact]
	public void InfantCarryModule_OnNewDay_NonInfantClearsCarriedState()
	{
		OverrideLifeStageHumanOnly();
		var tpy = LifeStageCatalog.TurnsPerYear;
		var state = new GameState { PlayerId = "p", Turn = tpy * 5 };
		var grew = MakeActor("grew_up", motherId: "mom");
		grew.BirthTurn = 0; // 年龄 5 → Child
		grew.CarriedByActorId = "mom"; // 之前被抱
		state.Actors["grew_up"] = grew;
		state.Actors["mom"] = MakeActor("mom");

		InfantCarryModule.OnNewDay(state);

		Assert.Null(grew.CarriedByActorId);
	}
}
