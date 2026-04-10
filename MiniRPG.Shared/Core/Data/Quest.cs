using System.Collections.Generic;

namespace MiniRPG.Core.Data;

public enum QuestStatus { Active, Completed, Failed }

/// <summary>
/// 任务实例：一条可追踪的目标，带状态、描述、进度。
/// </summary>
public class Quest
{
	public string Id { get; set; } = "";
	public string Title { get; set; } = "";
	public string Description { get; set; } = "";

	/// <summary>任务来源（NPC 名字、事件名等）。</summary>
	public string Source { get; set; } = "";

	public QuestStatus Status { get; set; } = QuestStatus.Active;

	/// <summary>接取时的回合数。</summary>
	public int AcceptedTurn { get; set; }

	/// <summary>完成/失败时的回合数。0 = 尚未结束。</summary>
	public int FinishedTurn { get; set; }

	/// <summary>任务目标列表。全部完成 → 任务可完成。</summary>
	public List<QuestObjective> Objectives { get; set; } = [];

	/// <summary>任意 KV 标签，用于条件检查或奖励控制。</summary>
	public Dictionary<string, string> Tags { get; set; } = new();
}

/// <summary>
/// 任务子目标：描述一个需要达成的条件及当前进度。
/// </summary>
public class QuestObjective
{
	public string Text { get; set; } = "";
	public int Current { get; set; }
	public int Target { get; set; } = 1;
	public bool Done => Current >= Target;
}
