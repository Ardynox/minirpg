using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace MiniRPG.Core.Data;

/// <summary>
/// 队伍状态：管理玩家控制的多个角色。
/// 存储在 GameState 中，可序列化。
/// </summary>
public sealed class PartyState
{
	/// <summary>队伍成员 ID 列表（有序，第一个是队长）。</summary>
	[JsonPropertyName("memberIds")]
	public List<string> MemberIds { get; set; } = [];

	/// <summary>当前激活（接受输入）的角色 ID。</summary>
	[JsonPropertyName("activeId")]
	public string ActiveId { get; set; } = "";

	/// <summary>最大队伍人数。</summary>
	[JsonPropertyName("maxSize")]
	public int MaxSize { get; set; } = 6;

	public PartyState Clone() => new()
	{
		MemberIds = [.. MemberIds],
		ActiveId = ActiveId,
		MaxSize = MaxSize,
	};
}

/// <summary>
/// 队伍管理模块：招募、解散、切换激活角色。
/// 纯函数，无副作用——所有状态存在 GameState.Party 上。
/// 
/// 设计原则：
/// - PartyModule.IsPartyMember(state, id) 替代原来的 id == state.PlayerId
/// - PartyModule.GetActiveId(state) 替代原来的 state.PlayerId（用于输入路由）
/// - PartyModule.GetLeaderId(state) 替代原来的 state.PlayerId（用于摄像机跟随）
/// - 非激活的队伍成员由 AI 自动跟随激活角色
/// </summary>
public static class PartyModule
{
	/// <summary>
	/// 初始化队伍：将当前玩家设为唯一成员和激活角色。
	/// 在新游戏创建时调用。
	/// </summary>
	public static void Initialize(GameState state)
	{
		state.Party.MemberIds.Clear();
		state.Party.MemberIds.Add(state.PlayerId);
		state.Party.ActiveId = state.PlayerId;
	}

	/// <summary>
	/// 确保队伍状态有效（兼容旧存档）。
	/// </summary>
	public static void EnsureValid(GameState state)
	{
		if (state.Party.MemberIds.Count == 0)
		{
			state.Party.MemberIds.Add(state.PlayerId);
			state.Party.ActiveId = state.PlayerId;
		}

		// 移除已死亡/不存在的成员
		state.Party.MemberIds.RemoveAll(id => !state.Actors.ContainsKey(id));

		// 确保 ActiveId 有效
		if (state.Party.MemberIds.Count == 0)
		{
			state.Party.ActiveId = "";
			return;
		}

		if (!state.Party.MemberIds.Contains(state.Party.ActiveId))
			state.Party.ActiveId = state.Party.MemberIds[0];
	}

	/// <summary>当前激活角色 ID（接受玩家输入的角色）。</summary>
	public static string GetActiveId(GameState state) =>
		string.IsNullOrEmpty(state.Party.ActiveId) ? state.PlayerId : state.Party.ActiveId;

	/// <summary>队长 ID（第一个成员，用于摄像机跟随等）。</summary>
	public static string GetLeaderId(GameState state) =>
		state.Party.MemberIds.Count > 0 ? state.Party.MemberIds[0] : state.PlayerId;

	/// <summary>获取激活角色实体。</summary>
	public static Actor? GetActiveActor(GameState state) =>
		ActorModule.GetById(state, GetActiveId(state));

	/// <summary>
	/// 严格版"取焦点角色"：仅在 <see cref="PartyState.ActiveId"/> 非空且对应 actor 实体存在时返回 true。
	/// 区别于 <see cref="GetActiveActor"/>：本方法不隐式回退到 <see cref="GameState.PlayerId"/>，
	/// 让调用方（典型如 <c>ActiveActorAccess</c>）决定回退策略。
	/// </summary>
	/// <remarks>
	/// 设计意图（见 <c>Docs/产品愿景.md</c> 玩家控制模型）：
	/// 玩家控制的"焦点角色"是 Party 的事实，但单机老存档 / 多人首帧未对齐时 Party 可能未初始化。
	/// 这种过渡态下调用方应回退到 <see cref="GameState.PlayerId"/>，避免 null 沿调用栈扩散。
	/// </remarks>
	public static bool TryGetActiveActor(
		GameState state,
		[System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Actor? actor)
	{
		var activeId = state.Party.ActiveId;
		if (!string.IsNullOrEmpty(activeId)
			&& state.Actors.TryGetValue(activeId, out var found))
		{
			actor = found;
			return true;
		}

		actor = null;
		return false;
	}

	/// <summary>判断是否是队伍成员。兼容旧存档：Party 为空时视 PlayerId 为唯一成员。</summary>
	public static bool IsPartyMember(GameState state, string actorId)
	{
		if (state.Party.MemberIds.Count == 0)
			return string.Equals(actorId, state.PlayerId, StringComparison.Ordinal);

		return state.Party.MemberIds.Contains(actorId, StringComparer.Ordinal);
	}

	/// <summary>队伍成员数量。</summary>
	public static int Count(GameState state) => state.Party.MemberIds.Count;

	/// <summary>获取所有队伍成员实体。</summary>
	public static List<Actor> GetMembers(GameState state) =>
		state.Party.MemberIds
			.Select(id => ActorModule.GetById(state, id))
			.Where(static actor => actor != null)
			.ToList()!;

	/// <summary>
	/// 招募角色加入队伍。
	/// </summary>
	public static PartyActionResult TryRecruit(GameState state, string actorId)
	{
		if (state.Party.MemberIds.Count >= state.Party.MaxSize)
			return new PartyActionResult { FailureReason = "party_full" };

		if (!state.Actors.ContainsKey(actorId))
			return new PartyActionResult { FailureReason = "actor_not_found" };

		if (IsPartyMember(state, actorId))
			return new PartyActionResult { FailureReason = "already_member" };

		var actor = ActorModule.GetById(state, actorId);
		if (actor == null)
			return new PartyActionResult { FailureReason = "actor_not_found" };

		// 加入队伍
		state.Party.MemberIds.Add(actorId);

		// 设为友好阵营
		actor.Faction = Factions.Player;

		// 清除 AI 大脑（由 FollowerBrain 接管）
		actor.BrainId = "party_follower";

		return new PartyActionResult { Success = true };
	}

	/// <summary>
	/// 解散角色离开队伍。不能解散队长。
	/// </summary>
	public static PartyActionResult TryDismiss(GameState state, string actorId)
	{
		if (!IsPartyMember(state, actorId))
			return new PartyActionResult { FailureReason = "not_member" };

		// 不能解散队长
		if (state.Party.MemberIds.Count > 0
			&& string.Equals(state.Party.MemberIds[0], actorId, StringComparison.Ordinal))
		{
			return new PartyActionResult { FailureReason = "cannot_dismiss_leader" };
		}

		state.Party.MemberIds.Remove(actorId);

		// 如果解散的是激活角色，切换到队长
		if (string.Equals(state.Party.ActiveId, actorId, StringComparison.Ordinal))
			state.Party.ActiveId = GetLeaderId(state);

		// 恢复为友好 NPC
		var actor = ActorModule.GetById(state, actorId);
		if (actor != null)
		{
			actor.Faction = Factions.Friendly;
			actor.BrainId = "simple";
		}

		return new PartyActionResult { Success = true };
	}

	/// <summary>
	/// 切换激活角色（Tab 键循环）。
	/// </summary>
	public static string CycleActive(GameState state)
	{
		EnsureValid(state);
		if (state.Party.MemberIds.Count <= 1)
			return GetActiveId(state);

		var currentIndex = state.Party.MemberIds.IndexOf(state.Party.ActiveId);
		var nextIndex = (currentIndex + 1) % state.Party.MemberIds.Count;
		state.Party.ActiveId = state.Party.MemberIds[nextIndex];
		return state.Party.ActiveId;
	}

	/// <summary>
	/// 直接选择激活角色（点击头像）。
	/// </summary>
	public static bool TrySetActive(GameState state, string actorId)
	{
		if (!IsPartyMember(state, actorId))
			return false;

		state.Party.ActiveId = actorId;
		return true;
	}

	/// <summary>
	/// 返回队伍中是否还有至少一个活着的成员。
	/// "活着"的定义：actor 存在于 <see cref="GameState.Actors"/> 且 <see cref="Combat.CombatModule.IsDead"/> 返回 false。
	/// </summary>
	public static bool AnyMemberAlive(GameState state)
	{
		foreach (var id in state.Party.MemberIds)
		{
			var actor = ActorModule.GetById(state, id);
			if (actor != null && !Combat.CombatModule.IsDead(actor))
				return true;
		}
		return false;
	}

	/// <summary>
	/// 当前激活角色死亡后，把激活角色切换到队伍里下一个还活着的成员。
	/// 返回 true 表示成功切换（仍有活人），false 表示全队已死。
	/// </summary>
	/// <remarks>
	/// 设计意图（见 <c>Docs/产品愿景.md</c> 死亡与复活）：
	/// 玩家控制多个角色但同时只操作一个；某个成员死亡时视角自动切到下一个活着的队员，
	/// 只有全队死亡才进入"回主菜单/永久死"流程。
	/// </remarks>
	public static bool TryPromoteNextLivingMember(GameState state)
	{
		EnsureValid(state);
		if (state.Party.MemberIds.Count == 0)
			return false;

		var startIndex = Math.Max(0, state.Party.MemberIds.IndexOf(state.Party.ActiveId));
		var count = state.Party.MemberIds.Count;
		for (var step = 1; step <= count; step++)
		{
			var idx = (startIndex + step) % count;
			var id = state.Party.MemberIds[idx];
			var actor = ActorModule.GetById(state, id);
			if (actor != null && !Combat.CombatModule.IsDead(actor))
			{
				state.Party.ActiveId = id;
				return true;
			}
		}

		state.Party.ActiveId = string.Empty;
		return false;
	}
}

public sealed class PartyActionResult
{
	public bool Success { get; init; }
	public string FailureReason { get; init; } = "";
}
