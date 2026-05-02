using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;
using MiniRPG.Core.Demographics;
using MiniRPG.Core.Needs;
using MiniRPG.Core.Social;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 守护愿景"死亡仪式感第 3 条"的 capturer：
/// - 目击者挂 <c>witnessed_death</c> thought；
/// - 同 Party 队友（非亲属）挂 <c>ally_died</c>；
/// - 亲属 survivor 在 <see cref="GameEvent.KinGriefTargets"/> 里的 → 跳过避免与 KinshipLoss 重挂；
/// - 凶手 / 受害者本人 / 范围外 / 跨 Z 层 / 非死亡事件 → 不挂。
/// </summary>
public sealed class BystanderGriefCapturerTests
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

	private static Actor SpawnHuman(GameState state, string id, int x, int y, int z = 0)
	{
		// ApplyThought 需要真实 NeedProfile（AllowThoughts=true）；走 PresetDB 出的 actor 才稳。
		var a = PresetDB.SpawnActor("player", id);
		a.X = x; a.Y = y; a.Z = z;
		state.Actors[id] = a;
		return a;
	}

	private static GameEvent MakeActorKilled(string victimId, int x, int y, int z, string? initiatorId = null)
	{
		return new GameEvent("actor_killed")
		{
			TargetId = victimId,
			TargetX = x,
			TargetY = y,
			TargetZ = z,
			InitiatorId = initiatorId,
		};
	}

	[Fact]
	public void OnEvent_ActorKilled_AppliesWitnessedDeathToNearbyBystander()
	{
		PresetDB.Load();
		OverrideLifeStageHumanOnly();
		var state = new GameState { PlayerId = "p", Turn = 100 };
		SpawnHuman(state, "victim", 5, 5);
		var close = SpawnHuman(state, "bystander_close", 6, 6);
		var far = SpawnHuman(state, "bystander_far", 99, 99);
		var otherZ = SpawnHuman(state, "bystander_other_z", 5, 5, z: 1);

		new BystanderGriefCapturer().OnEvent(state, MakeActorKilled("victim", 5, 5, 0));

		Assert.Contains(close.Thoughts, t => t.Id == BystanderGriefCapturer.ThoughtIdWitness);
		Assert.DoesNotContain(far.Thoughts, t => t.Id == BystanderGriefCapturer.ThoughtIdWitness);
		Assert.DoesNotContain(otherZ.Thoughts, t => t.Id == BystanderGriefCapturer.ThoughtIdWitness);
	}

	[Fact]
	public void OnEvent_ActorKilled_PartyMemberDeath_AppliesAllyDiedToLivingTeammate()
	{
		PresetDB.Load();
		OverrideLifeStageHumanOnly();
		var state = new GameState { PlayerId = "leader", Turn = 50 };
		SpawnHuman(state, "leader", 5, 5);
		SpawnHuman(state, "victim", 5, 5);
		var teammate = SpawnHuman(state, "teammate", 6, 5);
		// 同 Party 三人（leader / victim / teammate）。
		state.Party.MemberIds.AddRange(new[] { "leader", "victim", "teammate" });
		state.Party.ActiveId = "leader";

		new BystanderGriefCapturer().OnEvent(state, MakeActorKilled("victim", 5, 5, 0));

		Assert.Contains(teammate.Thoughts, t => t.Id == BystanderGriefCapturer.ThoughtIdAllyDied);
		Assert.DoesNotContain(teammate.Thoughts, t => t.Id == BystanderGriefCapturer.ThoughtIdWitness);
	}

	[Fact]
	public void OnEvent_ActorKilled_KillerItself_DoesNotTakeWitnessThought()
	{
		PresetDB.Load();
		OverrideLifeStageHumanOnly();
		var state = new GameState { PlayerId = "p", Turn = 10 };
		SpawnHuman(state, "victim", 5, 5);
		var killer = SpawnHuman(state, "killer", 6, 5);

		new BystanderGriefCapturer().OnEvent(state, MakeActorKilled("victim", 5, 5, 0, initiatorId: "killer"));

		Assert.DoesNotContain(killer.Thoughts, t => t.Id == BystanderGriefCapturer.ThoughtIdWitness);
	}

	[Fact]
	public void OnEvent_ActorKilled_KinSurvivorInList_IsSkippedToAvoidDoubleGrief()
	{
		PresetDB.Load();
		OverrideLifeStageHumanOnly();
		var state = new GameState { PlayerId = "p", Turn = 10 };
		SpawnHuman(state, "victim", 5, 5);
		var mom = SpawnHuman(state, "mom", 6, 5);
		// mom 同时在亲属列表 → KinshipLossCapturer 会给 lost_child；本 capturer 跳过。
		var ev = MakeActorKilled("victim", 5, 5, 0);
		ev.KinGriefTargets = new List<KinGriefTarget>
		{
			new() { SurvivorId = "mom", Relation = KinGriefHelper.RelationChild },
		};

		new BystanderGriefCapturer().OnEvent(state, ev);

		Assert.DoesNotContain(mom.Thoughts, t => t.Id == BystanderGriefCapturer.ThoughtIdWitness);
		Assert.DoesNotContain(mom.Thoughts, t => t.Id == BystanderGriefCapturer.ThoughtIdAllyDied);
	}

	[Fact]
	public void OnEvent_NonDeathEvent_IsIgnored()
	{
		PresetDB.Load();
		OverrideLifeStageHumanOnly();
		var state = new GameState { PlayerId = "p", Turn = 10 };
		var a = SpawnHuman(state, "a", 5, 5);
		var ev = new GameEvent("pickup_item") { TargetId = "whatever", TargetX = 5, TargetY = 5, TargetZ = 0 };

		new BystanderGriefCapturer().OnEvent(state, ev);

		Assert.DoesNotContain(a.Thoughts, t => t.Id == BystanderGriefCapturer.ThoughtIdWitness);
	}

	[Fact]
	public void OnEvent_HealthDeathCauses_AlsoTrigger()
	{
		PresetDB.Load();
		OverrideLifeStageHumanOnly();
		var state = new GameState { PlayerId = "p", Turn = 10 };
		SpawnHuman(state, "victim", 5, 5);
		var close = SpawnHuman(state, "close", 6, 5);

		foreach (var kind in new[] { "death_blood_loss", "death_infection", "natural_death" })
		{
			// 清旧 thought 再单独跑，避免一次命中之后后续分支的"同 source 同 id"被覆盖判等。
			close.Thoughts.RemoveAll(t => t.Id == BystanderGriefCapturer.ThoughtIdWitness);
			var ev = new GameEvent(kind)
			{
				TargetId = "victim",
				TargetX = 5,
				TargetY = 5,
				TargetZ = 0,
			};

			new BystanderGriefCapturer().OnEvent(state, ev);

			Assert.Contains(close.Thoughts, t => t.Id == BystanderGriefCapturer.ThoughtIdWitness);
		}
	}

	[Fact]
	public void OnEvent_DeadActor_DoesNotTakeWitnessThought()
	{
		PresetDB.Load();
		OverrideLifeStageHumanOnly();
		var state = new GameState { PlayerId = "p", Turn = 10 };
		SpawnHuman(state, "victim", 5, 5);
		var alsoDead = SpawnHuman(state, "also_dead", 6, 5);
		// CombatModule.IsDead = !actor.Limbs.Any(IsVitalLimb)；把所有肢体清空即可触发。
		alsoDead.Limbs.Clear();

		new BystanderGriefCapturer().OnEvent(state, MakeActorKilled("victim", 5, 5, 0));

		Assert.DoesNotContain(alsoDead.Thoughts, t => t.Id == BystanderGriefCapturer.ThoughtIdWitness);
	}
}
