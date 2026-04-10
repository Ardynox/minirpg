using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace MiniRPG.Core.Social;

/// <summary>
/// 角色间关系数据。
/// 关系是双向的但非对称的：A 对 B 的好感度可以和 B 对 A 不同。
/// </summary>
public sealed class RelationEntry
{
	/// <summary>关系持有者 ID。</summary>
	[JsonPropertyName("fromId")]
	public string FromId { get; set; } = "";

	/// <summary>关系对象 ID。</summary>
	[JsonPropertyName("toId")]
	public string ToId { get; set; } = "";

	/// <summary>好感度（-100 ~ 100）。</summary>
	[JsonPropertyName("opinion")]
	public int Opinion { get; set; }

	/// <summary>关系类型标签（friend, rival, lover, family 等）。</summary>
	[JsonPropertyName("tags")]
	public HashSet<string> Tags { get; set; } = [];

	/// <summary>上次社交互动的回合。</summary>
	[JsonPropertyName("lastInteractionTurn")]
	public int LastInteractionTurn { get; set; }
}

/// <summary>
/// 社交状态：存储所有角色间的关系。
/// 存储在 GameState 中，可序列化。
/// </summary>
public sealed class SocialState
{
	/// <summary>关系表：key = "fromId:toId"。</summary>
	[JsonPropertyName("relations")]
	public Dictionary<string, RelationEntry> Relations { get; set; } = new(StringComparer.Ordinal);

	/// <summary>社交互动冷却：key = "actorId"，value = 上次社交回合。</summary>
	[JsonPropertyName("socialCooldowns")]
	public Dictionary<string, int> SocialCooldowns { get; set; } = new(StringComparer.Ordinal);

	public SocialState Clone() => new()
	{
		Relations = new Dictionary<string, RelationEntry>(
			Relations.Select(kv => new KeyValuePair<string, RelationEntry>(kv.Key, new RelationEntry
			{
				FromId = kv.Value.FromId,
				ToId = kv.Value.ToId,
				Opinion = kv.Value.Opinion,
				Tags = [.. kv.Value.Tags],
				LastInteractionTurn = kv.Value.LastInteractionTurn,
			})),
			StringComparer.Ordinal),
		SocialCooldowns = new Dictionary<string, int>(SocialCooldowns, StringComparer.Ordinal),
	};
}

/// <summary>
/// 社交互动定义。
/// </summary>
public sealed class SocialInteractionDef
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";

	/// <summary>对发起者好感度的影响。</summary>
	public int InitiatorOpinionChange { get; set; }

	/// <summary>对接收者好感度的影响。</summary>
	public int RecipientOpinionChange { get; set; }

	/// <summary>触发条件：最低好感度。</summary>
	public int MinOpinion { get; set; } = -100;

	/// <summary>触发条件：最高好感度。</summary>
	public int MaxOpinion { get; set; } = 100;

	/// <summary>权重（随机选择时使用）。</summary>
	public float Weight { get; set; } = 1f;
}
