using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Event;

/// <summary>
/// 故事讲述者状态：追踪事件调度的运行时数据。
/// 存储在 GameState 中，可序列化。
/// </summary>
public sealed class StorytellerState
{
	/// <summary>上次触发各类事件的回合。</summary>
	[JsonPropertyName("lastIncidentTurn")]
	public Dictionary<string, int> LastIncidentTurn { get; set; } = [];

	/// <summary>待执行的延迟事件队列。</summary>
	[JsonPropertyName("pendingIncidents")]
	public List<PendingIncident> PendingIncidents { get; set; } = [];

	/// <summary>已发生事件的历史记录（最近 N 条）。</summary>
	[JsonPropertyName("history")]
	public List<IncidentRecord> History { get; set; } = [];

	/// <summary>当前威胁等级（0-100，影响事件选择）。</summary>
	[JsonPropertyName("threatLevel")]
	public float ThreatLevel { get; set; }

	/// <summary>上次调度检查的回合。</summary>
	[JsonPropertyName("lastCheckTurn")]
	public int LastCheckTurn { get; set; }

	public StorytellerState Clone() => new()
	{
		LastIncidentTurn = new Dictionary<string, int>(LastIncidentTurn),
		PendingIncidents = [.. PendingIncidents.ConvertAll(static p => p.Clone())],
		History = [.. History],
		ThreatLevel = ThreatLevel,
		LastCheckTurn = LastCheckTurn,
	};
}

public sealed class PendingIncident
{
	[JsonPropertyName("incidentDefId")]
	public string IncidentDefId { get; set; } = "";

	[JsonPropertyName("triggerTurn")]
	public int TriggerTurn { get; set; }

	[JsonPropertyName("params")]
	public Dictionary<string, string> Params { get; set; } = [];

	public PendingIncident Clone() => new()
	{
		IncidentDefId = IncidentDefId,
		TriggerTurn = TriggerTurn,
		Params = new Dictionary<string, string>(Params),
	};
}

public sealed class IncidentRecord
{
	[JsonPropertyName("defId")]
	public string DefId { get; set; } = "";

	[JsonPropertyName("turn")]
	public int Turn { get; set; }

	[JsonPropertyName("category")]
	public IncidentCategory Category { get; set; }
}
