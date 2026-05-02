using System;
using System.Collections.Generic;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Events;
using MiniRPG.Core.Needs;

namespace MiniRPG.Core.Social;

/// <summary>
/// <see cref="IGameEventConsequenceHandler"/> 实现：监听死亡事件，在同 Z 层 Chebyshev
/// &lt;= <see cref="WitnessRadius"/> 的非亲属、非受害者、非凶手旁观者身上挂一条 mood-offset
/// thought，让"附近有人死"在玩家 HUD 可见。
/// </summary>
/// <remarks>
/// <para>设计意图（见 <c>Docs/产品愿景.md</c> 死亡与复活 / 仪式感要求第 3 条）：
/// 愿景明文要求"其他队员 / 附近 NPC 在死亡和复活的那一刻要各获得一条 thought"。
/// 亲属已走 <see cref="MiniRPG.Core.Demographics.KinshipLossCapturer"/>（lost_child /
/// lost_parent / lost_spouse / lost_family），这里补**非亲属**的情绪冲击：</para>
/// <list type="bullet">
/// <item>同 Party 队友 → <c>ally_died</c>（较重 / 较长）。</item>
/// <item>普通目击者 → <c>witnessed_death</c>（较轻 / 较短）。</item>
/// </list>
/// <para>与 <see cref="ActorMemoryModule"/> 的分工：ActorMemory 写 <c>CasualtyWitnessed</c>
/// / <c>KilledBy</c> 是 AI 层 per-actor 认知（供 InputResolver 查询）；本 capturer 写 thought
/// 是 mood HUD 层。两套互不干扰；受害者本人和凶手本人都不会拿 thought。</para>
/// <para>和 <see cref="ActorMemoryModule.HandleActorKilled"/> 共享"Chebyshev &lt;=
/// <c>WitnessRadius</c> 同 Z 层"这条目击判定——两处常量保持相等（= 8）。</para>
/// </remarks>
public sealed class BystanderGriefCapturer : IGameEventConsequenceHandler
{
	/// <summary>与 <see cref="ActorMemoryModule.CasualtyWitnessRadius"/> 对齐：同 Z 层 Chebyshev 阈值。</summary>
	public const int WitnessRadius = 8;

	/// <summary><c>Data/thoughts.json</c> 中普通目击者拿到的 thought id。</summary>
	public const string ThoughtIdWitness = "witnessed_death";

	/// <summary><c>Data/thoughts.json</c> 中同 Party 队友拿到的 thought id（更重）。</summary>
	public const string ThoughtIdAllyDied = "ally_died";

	public void OnEvent(GameState state, GameEvent ev)
	{
		if (state == null) return;
		if (ev == null) return;
		if (!IsDeathEvent(ev.Type)) return;
		if (string.IsNullOrWhiteSpace(ev.TargetId)) return;

		var victimId = ev.TargetId!;
		var killerId = string.IsNullOrWhiteSpace(ev.InitiatorId) ? null : ev.InitiatorId;
		var deathX = ev.TargetX;
		var deathY = ev.TargetY;
		var deathZ = ev.TargetZ;

		// 亲属走 KinshipLossCapturer 拿更重的 lost_* thought；跳过它们避免 mood 冲击叠两层。
		HashSet<string>? kinSurvivors = null;
		if (ev.KinGriefTargets != null && ev.KinGriefTargets.Count > 0)
		{
			kinSurvivors = new HashSet<string>(StringComparer.Ordinal);
			for (var i = 0; i < ev.KinGriefTargets.Count; i++)
			{
				var t = ev.KinGriefTargets[i];
				if (!string.IsNullOrWhiteSpace(t.SurvivorId))
					kinSurvivors.Add(t.SurvivorId);
			}
		}

		var victimIsPartyMember = PartyModule.IsPartyMember(state, victimId);

		foreach (var actor in state.Actors.Values)
		{
			if (actor == null) continue;
			if (string.Equals(actor.Id, victimId, StringComparison.Ordinal)) continue;
			if (killerId != null && string.Equals(actor.Id, killerId, StringComparison.Ordinal)) continue;
			if (kinSurvivors != null && kinSurvivors.Contains(actor.Id)) continue;
			if (CombatModule.IsDead(actor)) continue;
			if (actor.Z != deathZ) continue;
			if (Math.Abs(actor.X - deathX) > WitnessRadius) continue;
			if (Math.Abs(actor.Y - deathY) > WitnessRadius) continue;

			var thoughtId = ResolveThoughtId(state, actor.Id, victimIsPartyMember);
			NeedSystem.ApplyThought(
				actor,
				thoughtId,
				state.Turn,
				NeedThoughtSources.Social,
				events: null,
				state: state);
		}
	}

	/// <summary>
	/// 决定目击者拿哪条 thought：victim 和 bystander 都是同一个 Party 的成员 → <see cref="ThoughtIdAllyDied"/>；
	/// 否则 → <see cref="ThoughtIdWitness"/>。
	/// </summary>
	private static string ResolveThoughtId(GameState state, string bystanderId, bool victimIsPartyMember)
	{
		if (victimIsPartyMember && PartyModule.IsPartyMember(state, bystanderId))
			return ThoughtIdAllyDied;
		return ThoughtIdWitness;
	}

	private static bool IsDeathEvent(string type) =>
		type is "actor_killed" or "natural_death" or "death_blood_loss" or "death_infection";
}
