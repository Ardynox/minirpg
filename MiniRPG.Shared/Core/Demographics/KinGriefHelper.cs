using System;
using System.Collections.Generic;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Demographics;

/// <summary>
/// 在 victim 被 <c>ActorModule.Remove</c> 之前，把"该谁悲伤 + 是什么亲属关系"
/// 列表挂到死亡事件的 <see cref="GameEvent.KinGriefTargets"/>。
/// 让 <see cref="KinshipLossCapturer"/> 能在 victim 已不在 state.Actors 时
/// 仍按列表给每个 survivor 写 kin grief thought。
///
/// Relation 字段从 survivor 视角描述失去了什么：
/// - "child"  : survivor 失去了 child（victim 是 survivor 的 mother/father）
/// - "parent" : survivor 失去了 parent（victim 是 survivor 的 child）
/// - "spouse" : survivor 失去了 spouse / mate
/// - "sibling": survivor 失去了 sibling
/// </summary>
public static class KinGriefHelper
{
	public const string RelationChild = "child";
	public const string RelationParent = "parent";
	public const string RelationSpouse = "spouse";
	public const string RelationSibling = "sibling";

	public static List<KinGriefTarget> BuildGriefTargets(GameState state, Actor victim)
	{
		var list = new List<KinGriefTarget>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		void Add(string? survivorId, string relation)
		{
			if (string.IsNullOrWhiteSpace(survivorId))
				return;
			if (!seen.Add(survivorId!))
				return;
			list.Add(new KinGriefTarget { SurvivorId = survivorId!, Relation = relation });
		}

		Add(victim.MotherActorId, RelationChild);
		Add(victim.FatherActorId, RelationChild);
		Add(victim.MateActorId, RelationSpouse);

		var motherId = victim.MotherActorId;
		var fatherId = victim.FatherActorId;
		foreach (var other in state.Actors.Values)
		{
			if (string.Equals(other.Id, victim.Id, StringComparison.Ordinal))
				continue;

			if (string.Equals(other.MotherActorId, victim.Id, StringComparison.Ordinal)
				|| string.Equals(other.FatherActorId, victim.Id, StringComparison.Ordinal))
			{
				Add(other.Id, RelationParent);
				continue;
			}

			var sameMother = !string.IsNullOrWhiteSpace(motherId)
				&& string.Equals(other.MotherActorId, motherId, StringComparison.Ordinal);
			var sameFather = !string.IsNullOrWhiteSpace(fatherId)
				&& string.Equals(other.FatherActorId, fatherId, StringComparison.Ordinal);
			if (sameMother || sameFather)
				Add(other.Id, RelationSibling);
		}

		return list;
	}

	/// <summary>原地把亲属列表挂到死亡事件，给死亡触发点（CombatModule / SurgeryModule / 自然死亡）一行调用。</summary>
	public static void AppendToDeathEvent(GameState state, Actor victim, GameEvent deathEvent)
	{
		var targets = BuildGriefTargets(state, victim);
		if (targets.Count == 0)
			return;
		deathEvent.KinGriefTargets ??= new List<KinGriefTarget>();
		deathEvent.KinGriefTargets.AddRange(targets);
	}

	/// <summary>把 KinGriefTarget.Relation 翻译成 thoughts.json 里的 thought id。</summary>
	public static string ResolveThoughtId(string relation) => relation switch
	{
		RelationChild => "lost_child",
		RelationParent => "lost_parent",
		RelationSpouse => "lost_spouse",
		RelationSibling => "lost_family",
		_ => "lost_family",
	};
}
