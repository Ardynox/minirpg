using System.Collections.Generic;
using MiniRPG.Core.Calendar;

namespace MiniRPG.Core.Combat;

/// <summary>
/// 一次"角色死亡复盘"的只读快照——回答"谁死了、什么时候、怎么死的、最近 30 回合发生了什么"。
/// </summary>
/// <remarks>
/// 设计意图（见 <c>Docs/产品愿景.md</c> "结果可追溯"）：
/// 玩家死后应能回看"刚才那 30 回合发生了什么"，而不是看到一段沉默的"You died."。
/// 由 <see cref="DeathReportRecorder"/> 在 <c>actor_killed</c> 事件触发时构建，并喂给
/// <c>App/RuntimeUi/PlayerDeathPresenter</c> + <c>Module/Panel/PlayerDeathReportPanel</c> 展示。
/// </remarks>
public sealed class DeathReport
{
	public string ActorId { get; init; } = "";
	public string ActorName { get; init; } = "";
	public int DeathTurn { get; init; }
	public CalendarView DeathCalendarView { get; init; }

	/// <summary>机器可读死因 id：<c>killed_by</c>、<c>incapacitated</c>、<c>death_blood_loss</c>、<c>death_infection</c>、<c>food_poisoning</c>、<c>fire</c>、<c>unknown</c>。</summary>
	public string DeathCause { get; init; } = "unknown";

	/// <summary>致命攻击者的展示名（如有），用于 "killed_by {killer}" UI 翻译。</summary>
	public string? KillerName { get; init; }

	/// <summary>最近 N 回合（默认 30）内发生在该 actor 上的关键事件，按时间顺序。</summary>
	public IReadOnlyList<RecentEventEntry> RecentEvents { get; init; } = [];
}

/// <summary>
/// <see cref="DeathReport"/> 中保留的一条历史事件的只读视图。
/// 字段是 <see cref="MiniRPG.Core.Data.GameEvent"/> 的简化投影——只留 UI 需要的列。
/// </summary>
public sealed class RecentEventEntry
{
	public int Turn { get; init; }
	public string EventType { get; init; } = "";
	public string? InitiatorActorName { get; init; }
	public string? LimbName { get; init; }
	public string? ActionName { get; init; }
	public int Damage { get; init; }
	/// <summary>对 food_consumed / food_rejected：保存 freshness stage（"fresh"/"stale"/"spoiled"/"rotten"）。</summary>
	public string? EffectType { get; init; }
}
