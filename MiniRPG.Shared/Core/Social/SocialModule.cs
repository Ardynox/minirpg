using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Social;

/// <summary>
/// 社交模块：管理角色间关系、社交互动、好感度变化。
/// 
/// 设计原则：
/// - 关系是非对称的（A→B 和 B→A 独立）
/// - 好感度范围 -100 ~ 100
/// - 社交互动有冷却（每个角色每 N 回合只能社交一次）
/// - 好感度影响 Thought（心情系统）
/// - 好感度影响 AI 行为（高好感 → 帮助，低好感 → 回避）
/// </summary>
public static class SocialModule
{
	/// <summary>社交冷却回合数。</summary>
	private const int SocialCooldownTurns = 10;

	/// <summary>好感度上下限。</summary>
	private const int MinOpinion = -100;
	private const int MaxOpinion = 100;

	/// <summary>社交互动定义注册表。</summary>
	private static readonly Dictionary<string, SocialInteractionDef> _interactions = new(StringComparer.Ordinal);

	// ── 注册 ──

	public static void RegisterInteraction(SocialInteractionDef def) => _interactions[def.Id] = def;
	public static void ClearInteractions() => _interactions.Clear();
	public static IReadOnlyDictionary<string, SocialInteractionDef> AllInteractions => _interactions;

	// ── 好感度查询 ──

	/// <summary>获取 from 对 to 的好感度。</summary>
	public static int GetOpinion(GameState state, string fromId, string toId)
	{
		var key = MakeKey(fromId, toId);
		return state.SocialState.Relations.TryGetValue(key, out var entry) ? entry.Opinion : 0;
	}

	/// <summary>获取关系条目（可能为 null）。</summary>
	public static RelationEntry? GetRelation(GameState state, string fromId, string toId)
	{
		var key = MakeKey(fromId, toId);
		return state.SocialState.Relations.TryGetValue(key, out var entry) ? entry : null;
	}

	/// <summary>获取指定角色的所有关系。</summary>
	public static List<RelationEntry> GetRelationsOf(GameState state, string actorId) =>
		state.SocialState.Relations.Values
			.Where(r => string.Equals(r.FromId, actorId, StringComparison.Ordinal))
			.ToList();

	/// <summary>获取对指定角色有好感/恶感的所有角色。</summary>
	public static List<RelationEntry> GetRelationsToward(GameState state, string actorId) =>
		state.SocialState.Relations.Values
			.Where(r => string.Equals(r.ToId, actorId, StringComparison.Ordinal))
			.ToList();

	// ── 好感度修改 ──

	/// <summary>直接修改好感度（加减）。</summary>
	public static void AdjustOpinion(GameState state, string fromId, string toId, int delta)
	{
		var entry = GetOrCreateRelation(state, fromId, toId);
		entry.Opinion = Math.Clamp(entry.Opinion + delta, MinOpinion, MaxOpinion);
	}

	/// <summary>直接设置好感度。</summary>
	public static void SetOpinion(GameState state, string fromId, string toId, int value)
	{
		var entry = GetOrCreateRelation(state, fromId, toId);
		entry.Opinion = Math.Clamp(value, MinOpinion, MaxOpinion);
	}

	/// <summary>添加关系标签。</summary>
	public static void AddTag(GameState state, string fromId, string toId, string tag)
	{
		var entry = GetOrCreateRelation(state, fromId, toId);
		entry.Tags.Add(tag);
	}

	/// <summary>移除关系标签。</summary>
	public static void RemoveTag(GameState state, string fromId, string toId, string tag)
	{
		var entry = GetOrCreateRelation(state, fromId, toId);
		entry.Tags.Remove(tag);
	}

	/// <summary>检查是否有关系标签。</summary>
	public static bool HasTag(GameState state, string fromId, string toId, string tag)
	{
		var relation = GetRelation(state, fromId, toId);
		return relation?.Tags.Contains(tag) ?? false;
	}

	// ── 社交互动 ──

	/// <summary>
	/// 尝试执行社交互动。
	/// 返回产生的 GameEvent（用于日志/UI 显示）。
	/// </summary>
	public static List<GameEvent> TryInteract(GameState state, string initiatorId, string recipientId)
	{
		var events = new List<GameEvent>();

		// 冷却检查
		if (!CanSocialize(state, initiatorId))
			return events;

		// 选择互动类型
		var interaction = SelectInteraction(state, initiatorId, recipientId);
		if (interaction == null)
			return events;

		// 执行互动
		AdjustOpinion(state, initiatorId, recipientId, interaction.InitiatorOpinionChange);
		AdjustOpinion(state, recipientId, initiatorId, interaction.RecipientOpinionChange);

		// 更新冷却和时间戳
		state.SocialState.SocialCooldowns[initiatorId] = state.Turn;
		var relation = GetOrCreateRelation(state, initiatorId, recipientId);
		relation.LastInteractionTurn = state.Turn;

		// 生成事件
		var initiator = ActorModule.GetById(state, initiatorId);
		var recipient = ActorModule.GetById(state, recipientId);
		events.Add(new GameEvent("social_interaction")
		{
			InitiatorId = initiatorId,
			TargetId = recipientId,
			InitiatorActorName = initiator?.DisplayName,
			TargetActorName = recipient?.DisplayName,
			InteractionDefId = interaction.Id,
			InteractionName = interaction.Name,
		});

		return events;
	}

	/// <summary>检查角色是否可以进行社交（冷却检查）。</summary>
	public static bool CanSocialize(GameState state, string actorId)
	{
		if (!state.SocialState.SocialCooldowns.TryGetValue(actorId, out var lastTurn))
			return true;

		return state.Turn - lastTurn >= SocialCooldownTurns;
	}

	// ── 关系判定辅助 ──

	/// <summary>是否是朋友（好感度 >= 50）。</summary>
	public static bool IsFriend(GameState state, string fromId, string toId) =>
		GetOpinion(state, fromId, toId) >= 50;

	/// <summary>是否是敌人（好感度 <= -50）。</summary>
	public static bool IsRival(GameState state, string fromId, string toId) =>
		GetOpinion(state, fromId, toId) <= -50;

	// ── 内部方法 ──

	private static RelationEntry GetOrCreateRelation(GameState state, string fromId, string toId)
	{
		var key = MakeKey(fromId, toId);
		if (!state.SocialState.Relations.TryGetValue(key, out var entry))
		{
			entry = new RelationEntry { FromId = fromId, ToId = toId };
			state.SocialState.Relations[key] = entry;
		}

		return entry;
	}

	private static SocialInteractionDef? SelectInteraction(GameState state, string initiatorId, string recipientId)
	{
		var currentOpinion = GetOpinion(state, initiatorId, recipientId);
		var rng = new Random(state.RngSeed + state.Turn + initiatorId.GetHashCode() + recipientId.GetHashCode());

		var candidates = _interactions.Values
			.Where(def => currentOpinion >= def.MinOpinion && currentOpinion <= def.MaxOpinion)
			.ToList();

		if (candidates.Count == 0)
			return null;

		var totalWeight = candidates.Sum(static c => c.Weight);
		var roll = (float)(rng.NextDouble() * totalWeight);
		var cumulative = 0f;
		foreach (var def in candidates)
		{
			cumulative += def.Weight;
			if (roll <= cumulative)
				return def;
		}

		return candidates[^1];
	}

	private static string MakeKey(string fromId, string toId) => $"{fromId}:{toId}";
}
