using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Demographics;

/// <summary>
/// 亲属关系纯查询模块。基于 <see cref="Actor.MotherActorId"/> / <see cref="Actor.FatherActorId"/>
/// / <see cref="Actor.MateActorId"/> 反向查询，不维护额外字段（孩子列表反查得到，
/// 避免双向同步）。
///
/// 没有 RegisterBirth / RegisterSpouse 这种"主动写入"接口——
/// <see cref="ConceptionBirthTick"/> 直接写孩子的 MotherActorId / FatherActorId，
/// MateActorId 也由 conception 流程直接设置。本模块只读不写。
/// </summary>
public static class KinshipModule
{
	public static IEnumerable<Actor> GetParents(GameState state, Actor actor)
	{
		if (!string.IsNullOrWhiteSpace(actor.MotherActorId)
			&& state.Actors.TryGetValue(actor.MotherActorId!, out var m))
			yield return m;
		if (!string.IsNullOrWhiteSpace(actor.FatherActorId)
			&& state.Actors.TryGetValue(actor.FatherActorId!, out var f))
			yield return f;
	}

	public static IEnumerable<Actor> GetChildren(GameState state, Actor parent) =>
		state.Actors.Values.Where(child =>
			string.Equals(child.MotherActorId, parent.Id, System.StringComparison.Ordinal)
			|| string.Equals(child.FatherActorId, parent.Id, System.StringComparison.Ordinal));

	public static IEnumerable<Actor> GetSiblings(GameState state, Actor actor)
	{
		var motherId = actor.MotherActorId;
		var fatherId = actor.FatherActorId;
		if (string.IsNullOrWhiteSpace(motherId) && string.IsNullOrWhiteSpace(fatherId))
			yield break;

		foreach (var other in state.Actors.Values)
		{
			if (string.Equals(other.Id, actor.Id, System.StringComparison.Ordinal))
				continue;
			var sameMother = !string.IsNullOrWhiteSpace(motherId)
				&& string.Equals(other.MotherActorId, motherId, System.StringComparison.Ordinal);
			var sameFather = !string.IsNullOrWhiteSpace(fatherId)
				&& string.Equals(other.FatherActorId, fatherId, System.StringComparison.Ordinal);
			if (sameMother || sameFather)
				yield return other;
		}
	}

	public static Actor? GetMate(GameState state, Actor actor) =>
		string.IsNullOrWhiteSpace(actor.MateActorId)
			? null
			: state.Actors.TryGetValue(actor.MateActorId!, out var mate) ? mate : null;

	/// <summary>所有亲属（父母 + 孩子 + 兄弟姐妹 + 配偶）去重列表。被失亲悲恸 / UI 联动用。</summary>
	public static IReadOnlyList<Actor> GetAllRelatives(GameState state, Actor actor)
	{
		var seen = new HashSet<string>(System.StringComparer.Ordinal) { actor.Id };
		var list = new List<Actor>();
		void Add(Actor? other)
		{
			if (other != null && seen.Add(other.Id))
				list.Add(other);
		}

		foreach (var p in GetParents(state, actor))
			Add(p);
		foreach (var c in GetChildren(state, actor))
			Add(c);
		foreach (var s in GetSiblings(state, actor))
			Add(s);
		Add(GetMate(state, actor));
		return list;
	}
}
