using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Revival;
using MiniRPG.Module.Session;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 单测 ActiveActorDeathHandler 三个 outcome 路径 + 与 ReviveService 的"复活回 active
/// 候选"互锁。这些 case 比 ActiveActorDeathHandlerIntegrationTests 更聚焦：
/// 不走 Main.Timeline 整套生产路径，只 ping handler 本身 + PartyModule 状态变化。
/// </summary>
public class ActiveActorDeathHandlerUnitTests
{
	[Fact]
	public void Handle_CurrentActiveDies_PromotesNextLivingMember()
	{
		var state = CreateLivingParty(("leader", true), ("ally", true), ("medic", true));
		state.Party.ActiveId = "leader";
		Kill(state.Actors["leader"]);
		var events = new List<GameEvent>();

		var outcome = ActiveActorDeathHandler.Handle(state, "leader", events);

		Assert.Equal(ActiveActorDeathOutcome.PromotedNextMember, outcome);
		// 切到 leader 之后第一个活着的 → ally。
		Assert.Equal("ally", PartyModule.GetActiveId(state));
		Assert.Single(events, e => e.Type == "party_member_lost");
		Assert.Single(events, e => e.Type == "active_actor_switched");
		Assert.DoesNotContain(events, e => e.Type == "party_wiped");
	}

	[Fact]
	public void Handle_AllPartyDead_AfterCurrentDies_ReturnsAllMembersDead()
	{
		var state = CreateLivingParty(("leader", true), ("ally", false));
		state.Party.ActiveId = "leader";
		Kill(state.Actors["leader"]);
		var events = new List<GameEvent>();

		var outcome = ActiveActorDeathHandler.Handle(state, "leader", events);

		Assert.Equal(ActiveActorDeathOutcome.AllMembersDead, outcome);
		Assert.Contains(events, e => e.Type == "party_wiped");
		Assert.DoesNotContain(events, e => e.Type == "active_actor_switched");
	}

	[Fact]
	public void Handle_NonActiveDies_DoesNotChangeFocus_NoWipeEvent()
	{
		var state = CreateLivingParty(("leader", true), ("ally", true));
		state.Party.ActiveId = "leader";
		Kill(state.Actors["ally"]);
		var events = new List<GameEvent>();

		var outcome = ActiveActorDeathHandler.Handle(state, "ally", events);

		Assert.Equal(ActiveActorDeathOutcome.NotActive, outcome);
		Assert.Equal("leader", PartyModule.GetActiveId(state));
		Assert.DoesNotContain(events, e => e.Type == "active_actor_switched");
		Assert.DoesNotContain(events, e => e.Type == "party_wiped");
		var lost = events.Single(e => e.Type == "party_member_lost");
		Assert.Equal("non_active", lost.EffectType);
	}

	[Fact]
	public void Handle_RevivedActor_BecomesActiveCandidateAgain()
	{
		// 全队挂掉 → AllMembersDead；后用 ReviveService 把 ally 拉回来 → AnyMemberAlive=true，
		// TryPromoteNextLivingMember 应能切焦点到 ally；handler 不应再判 AllMembersDead。
		var state = CreateLivingParty(("leader", true), ("ally", true));
		state.Party.ActiveId = "leader";
		Kill(state.Actors["leader"]);
		Kill(state.Actors["ally"]);

		var firstEvents = new List<GameEvent>();
		var firstOutcome = ActiveActorDeathHandler.Handle(state, "leader", firstEvents);
		Assert.Equal(ActiveActorDeathOutcome.AllMembersDead, firstOutcome);
		Assert.False(PartyModule.AnyMemberAlive(state));

		// 把 ally 拉回 50% durability 的活体（直接改 durability，模拟 ReviveService.ResetActorForRevival）。
		ReviveAlly(state, "ally");

		// 现在再让 leader 死亡（已经死了；这里测试"another death wave"）→ 应该切到 ally。
		var secondEvents = new List<GameEvent>();
		state.Party.ActiveId = "leader"; // 假装 active 还指向 leader（边界 case）
		var secondOutcome = ActiveActorDeathHandler.Handle(state, "leader", secondEvents);
		Assert.Equal(ActiveActorDeathOutcome.PromotedNextMember, secondOutcome);
		Assert.Equal("ally", PartyModule.GetActiveId(state));
		Assert.True(PartyModule.AnyMemberAlive(state),
			"复活后队伍至少有 1 活人，AnyMemberAlive 必须为 true。");
	}

	[Fact]
	public void Handle_DeadActorAlreadyRemovedFromActors_StillEmitsLostEventGracefully()
	{
		// 边界：Handle 接收一个已经从 state.Actors 删掉的 id（典型路径：actor_killed 之后
		// SaveModule / PartyModule 清理已经把 entry 移除）。Handler 不应崩；应该走 NotActive
		// 路径（active != deadId，因为 deadId 不是 active），并发出一条 party_member_lost。
		var state = CreateLivingParty(("leader", true), ("ally", true));
		state.Party.ActiveId = "leader";
		state.Actors.Remove("ally");

		var events = new List<GameEvent>();
		var outcome = ActiveActorDeathHandler.Handle(state, "ally", events);

		Assert.Equal(ActiveActorDeathOutcome.NotActive, outcome);
		Assert.Single(events, e => e.Type == "party_member_lost");
	}

	private static GameState CreateLivingParty(params (string Id, bool Alive)[] members)
	{
		var state = new GameState { PlayerId = members[0].Id };
		foreach (var (id, alive) in members)
		{
			var actor = MakeActor(id);
			if (!alive)
				Kill(actor);
			state.Actors[id] = actor;
		}

		PartyModule.Initialize(state);
		state.Party.MemberIds.Clear();
		foreach (var (id, _) in members)
			state.Party.MemberIds.Add(id);
		state.Party.ActiveId = members[0].Id;
		return state;
	}

	private static Actor MakeActor(string id) => new()
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
				MaxDurability = 10,
				Durability = 10,
				Tags = { [CombatModule.VitalTag] = 1 },
			},
		},
	};

	private static void Kill(Actor actor)
	{
		// 与生产代码 CombatModule.IsDead 第二个分支对齐：清空 limbs 即视为死亡。
		actor.Limbs.Clear();
		actor.InvalidateCapacityCache();
	}

	/// <summary>测试用：把 actor 拉回来——重建 vital limb，让 IsDead 重新返 false。</summary>
	private static void ReviveAlly(GameState state, string id)
	{
		var actor = state.Actors[id];
		actor.Limbs.Add(new Limb
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
			Durability = 5,
			Tags = { [CombatModule.VitalTag] = 1 },
		});
		actor.InvalidateCapacityCache();
		actor.RevivalCount += 1;
	}
}
