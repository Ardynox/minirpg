using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Module.Session;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 验证"焦点死亡 → 自动切人 → 全队死才终局"的核心规则。
/// 这是《产品愿景》死亡与复活节的硬约束；任何动 ActiveActorDeathHandler / PartyModule
/// 切焦点逻辑的改动都应该让这套用例继续通过。
/// </summary>
public class ActiveActorDeathHandlerIntegrationTests
{
	private static GameState CreateLivingPartyState()
	{
		TestSupport.EnsureGameplayDataLoaded();

		var state = new GameState { PlayerId = "leader" };
		state.Actors["leader"] = MakeLivingActor("leader");
		state.Actors["ally"] = MakeLivingActor("ally");

		PartyModule.Initialize(state);
		state.Party.MemberIds.Add("ally");
		state.Party.ActiveId = "leader";
		return state;
	}

	// 必须给 limb 配齐"vital tag + blood_circulation/consciousness capacity"，
	// 否则 CombatModule.CheckVitalStatus 会因为 PresetDB.Capacities 里 blood_circulation 的
	// vitalEffect=death_instant + zeroThreshold=0 而把空 capacity 的 actor 判成 death_instant，
	// AnyMemberAlive 永远返回 false → 测试期望的"切焦点"路径走不到。
	private static Actor MakeLivingActor(string id) => new()
	{
		Id = id,
		DisplayName = id,
		Faction = Factions.Player,
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
				Tags = { [CombatModule.VitalTag] = 1 },
			},
		},
	};

	// 通过清空 limbs 让 CombatModule.IsDead 第二个分支命中（!Limbs.Any(IsVitalLimb)），
	// 从而让 PartyModule.AnyMemberAlive / TryPromoteNextLivingMember 把这个角色识别为"死亡"。
	private static void Kill(Actor actor)
	{
		actor.Limbs.Clear();
		actor.InvalidateCapacityCache();
	}

	[Fact]
	public void Handle_ActiveLeaderDies_PromotesToNextLivingMember()
	{
		var state = CreateLivingPartyState();
		Kill(state.Actors["leader"]);

		var events = new List<GameEvent>();
		var outcome = ActiveActorDeathHandler.Handle(state, "leader", events);

		Assert.Equal(ActiveActorDeathOutcome.PromotedNextMember, outcome);
		Assert.Equal("ally", PartyModule.GetActiveId(state));

		var lost = events.Single(e => e.Type == "party_member_lost");
		Assert.Equal("was_active", lost.EffectType);
		var switched = events.Single(e => e.Type == "active_actor_switched");
		Assert.Equal("ally", switched.ItemName);
		Assert.DoesNotContain(events, e => e.Type == "party_wiped");
	}

	[Fact]
	public void Handle_NonActiveMemberDies_DoesNotSwitchFocusOrEndGame()
	{
		var state = CreateLivingPartyState();
		Kill(state.Actors["ally"]);

		var events = new List<GameEvent>();
		var outcome = ActiveActorDeathHandler.Handle(state, "ally", events);

		Assert.Equal(ActiveActorDeathOutcome.NotActive, outcome);
		Assert.Equal("leader", PartyModule.GetActiveId(state));

		var lost = events.Single(e => e.Type == "party_member_lost");
		Assert.Equal("non_active", lost.EffectType);
		Assert.DoesNotContain(events, e => e.Type == "active_actor_switched");
		Assert.DoesNotContain(events, e => e.Type == "party_wiped");
	}

	[Fact]
	public void Handle_AllPartyDeadAfterActiveDies_EmitsPartyWiped()
	{
		var state = CreateLivingPartyState();
		Kill(state.Actors["leader"]);
		Kill(state.Actors["ally"]);

		var events = new List<GameEvent>();
		var outcome = ActiveActorDeathHandler.Handle(state, "leader", events);

		Assert.Equal(ActiveActorDeathOutcome.AllMembersDead, outcome);
		Assert.Contains(events, e => e.Type == "party_member_lost");
		Assert.Contains(events, e => e.Type == "party_wiped");
		Assert.DoesNotContain(events, e => e.Type == "active_actor_switched");
	}

	[Fact]
	public void Handle_ActiveDiesAfterFocusSwitched_StillFindsLastMember()
	{
		// 之前已经 Tab 切到 ally；ally 死了又有第三个活人 medic，焦点应继续切到 medic。
		var state = CreateLivingPartyState();
		state.Actors["medic"] = MakeLivingActor("medic");
		state.Party.MemberIds.Add("medic");
		state.Party.ActiveId = "ally";
		Kill(state.Actors["ally"]);

		var events = new List<GameEvent>();
		var outcome = ActiveActorDeathHandler.Handle(state, "ally", events);

		Assert.Equal(ActiveActorDeathOutcome.PromotedNextMember, outcome);
		Assert.Equal("medic", PartyModule.GetActiveId(state));
		Assert.DoesNotContain(events, e => e.Type == "party_wiped");
	}

	[Fact]
	public void GetActive_FallsBackToPlayerId_WhenPartyUninitialized()
	{
		// 老存档加载首帧 / 多人 snapshot 未对齐时 Party 还没初始化（MemberIds 空 + ActiveId 空）。
		// ActiveActorAccess 应该退回到 PlayerId 对应的 actor，不能让 caller 拿到 null 沿调用栈散播。
		var state = new GameState { PlayerId = "lonely_hero" };
		state.Actors["lonely_hero"] = MakeLivingActor("lonely_hero");

		Assert.Empty(state.Party.MemberIds);
		Assert.Equal(string.Empty, state.Party.ActiveId);

		var actor = ActiveActorAccess.GetActive(state);
		Assert.NotNull(actor);
		Assert.Equal("lonely_hero", actor!.Id);
		Assert.Equal("lonely_hero", ActiveActorAccess.GetActiveId(state));
	}

	[Fact]
	public void GetActive_ReturnsActiveActor_WhenPartyInitialized()
	{
		var state = CreateLivingPartyState();
		state.Party.ActiveId = "ally";

		var actor = ActiveActorAccess.GetActive(state);
		Assert.NotNull(actor);
		Assert.Equal("ally", actor!.Id);
		Assert.Equal("ally", ActiveActorAccess.GetActiveId(state));
	}

	[Fact]
	public void GetActive_FallsBackToPlayerId_WhenActiveIdPointsToMissingActor()
	{
		// 极端 corner case：Party.ActiveId 残留指向已移除的 actor。
		// TryGetActiveActor 严格不命中 → ActiveActorAccess 走 PlayerId fallback。
		var state = new GameState { PlayerId = "leader" };
		state.Actors["leader"] = MakeLivingActor("leader");
		PartyModule.Initialize(state);
		state.Party.ActiveId = "ghost"; // ghost 不在 Actors 字典里

		var actor = ActiveActorAccess.GetActive(state);
		Assert.NotNull(actor);
		Assert.Equal("leader", actor!.Id);
	}
}
