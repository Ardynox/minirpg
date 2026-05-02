using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MiniRPG.Core.Data;

// 旧 Dialog 系统已被 MiniRPG.Shared/Core/Conversation/* 替代；
// 仅保留 DialogCondition / DialogEffect 两个条件表达式模型，
// 继续被 ConversationModule + DialogConditionMatcher 复用。

/// <summary>
/// 通用标签条件。所有维度统一为 tag 键值对：
///   - requiresTags: 上下文必须包含这些 tag（存在性检查，如 "prof:merchant", "memory:met_elder"）
///   - forbidsTags:  上下文不能包含这些 tag
///   - minTags:      tag 的数值必须 >= 指定值（如 {"affinity": 30, "mood": -0.3}）
///   - maxTags:      tag 的数值必须 <= 指定值
/// 所有条件取 AND。null 表示不检查。
/// </summary>
public class DialogCondition
{
	[JsonPropertyName("requiresTags")]
	public List<string>? RequiresTags { get; set; }

	[JsonPropertyName("forbidsTags")]
	public List<string>? ForbidsTags { get; set; }

	[JsonPropertyName("minTags")]
	public Dictionary<string, float>? MinTags { get; set; }

	[JsonPropertyName("maxTags")]
	public Dictionary<string, float>? MaxTags { get; set; }
}

/// <summary>
/// 副作用。Type:
///   "affinity" / "mood" -- 修改 NPC 对话状态数值
///   "memory"            -- 添加记忆标签 (Key = 标签名)
///   "gold"              -- 修改玩家金币
///   "tag"               -- 通用：给 NPC 加一个运行时 tag (Key = tag名, Value = 值)
/// </summary>
public class DialogEffect
{
	[JsonPropertyName("type")]
	public string Type { get; set; } = "";

	[JsonPropertyName("key")]
	public string? Key { get; set; }

	[JsonPropertyName("value")]
	public float Value { get; set; }
}
