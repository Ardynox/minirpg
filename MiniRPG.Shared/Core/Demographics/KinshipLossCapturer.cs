using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Events;
using MiniRPG.Core.Needs;

namespace MiniRPG.Core.Demographics;

/// <summary>
/// <see cref="IGameEventConsequenceHandler"/> 实现：监听死亡事件（actor_killed /
/// natural_death / death_blood_loss / death_infection），按 <see cref="GameEvent.KinGriefTargets"/>
/// 给每个 survivor 写 kin grief thought（lost_child / lost_parent / lost_spouse /
/// lost_family，已在 Data/thoughts.json 定义）。
///
/// 列表本身由死亡触发点（CombatModule / SurgeryModule）在 victim 还在 state.Actors
/// 时通过 <see cref="KinGriefHelper.AppendToDeathEvent"/> 预填，capturer 自己不重新
/// 解析亲属关系（victim 已被 ActorModule.Remove，没法重建）。
/// </summary>
public sealed class KinshipLossCapturer : IGameEventConsequenceHandler
{
	public void OnEvent(GameState state, GameEvent ev)
	{
		if (!IsDeathEvent(ev.Type))
			return;
		if (ev.KinGriefTargets == null || ev.KinGriefTargets.Count == 0)
			return;

		foreach (var target in ev.KinGriefTargets)
		{
			if (string.IsNullOrWhiteSpace(target.SurvivorId))
				continue;
			if (!state.Actors.TryGetValue(target.SurvivorId, out var survivor))
				continue;
			if (CombatModule.IsDead(survivor))
				continue;

			var thoughtId = KinGriefHelper.ResolveThoughtId(target.Relation);
			NeedSystem.ApplyThought(survivor, thoughtId, state.Turn, NeedThoughtSources.Social, null, state);
		}
	}

	private static bool IsDeathEvent(string type) =>
		type is "actor_killed" or "natural_death" or "death_blood_loss" or "death_infection";
}
