using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MiniRPG.Core.Data;

public class DialogEntry
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("speaker")]
	public string Speaker { get; set; } = "npc";

	[JsonPropertyName("template")]
	public string Template { get; set; } = "";

	[JsonPropertyName("condition")]
	public DialogCondition? Condition { get; set; }

	[JsonPropertyName("options")]
	public List<DialogOption> Options { get; set; } = [];

	[JsonPropertyName("effects")]
	public List<DialogEffect> Effects { get; set; } = [];

	[JsonPropertyName("nextId")]
	public string? NextId { get; set; }

	[JsonPropertyName("priority")]
	public int Priority { get; set; }

	/// <summary>
	/// 标签组：标记这条对话属于哪个话题/类别，用于 DialogPool 索引。
	/// 如 ["greet", "trade", "lore"]。不参与条件匹配，只用于分组查找。
	/// </summary>
	[JsonPropertyName("tags")]
	public List<string> Tags { get; set; } = [];
}

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

public class DialogOption
{
	[JsonPropertyName("text")]
	public string Text { get; set; } = "";

	[JsonPropertyName("nextId")]
	public string? NextId { get; set; }

	[JsonPropertyName("effects")]
	public List<DialogEffect> Effects { get; set; } = [];

	[JsonPropertyName("condition")]
	public DialogCondition? Condition { get; set; }
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

/// <summary>
/// 对话集合。不再绑定 npcId/profession，改为纯标签分组。
/// category 用于 DialogPool 索引（如 "greet", "trade", "lore", "react"）。
/// </summary>
public class DialogSet
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("category")]
	public string Category { get; set; } = "general";

	[JsonPropertyName("entries")]
	public List<DialogEntry> Entries { get; set; } = [];
}
