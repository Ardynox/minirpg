using System;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;

namespace MiniRPG;

/// <summary>
/// 把"角色死亡"翻译成玩家可见的复盘——拿 <see cref="DeathReport"/>、写一段日志、把 panel 推上去。
/// </summary>
/// <remarks>
/// <para>设计意图（见 <c>Docs/产品愿景.md</c> "结果可追溯"）：
/// 玩家死后不应该只看到一行 <c>You died.</c>，而应能回看"刚才那 30 回合发生了什么"。
/// 本 presenter 是 <see cref="DeathReportRecorder"/> 与 UI panel 之间的薄薄一层，
/// 它本身不碰 Godot 节点：panel 显示通过 <see cref="ShowReportPanel"/> 委托派给 Module/Panel 实现。</para>
///
/// <para>三种 reason 分支（来自 <c>App/RuntimeUi/GameEventPresentationRouter</c>）：
/// <list type="bullet">
/// <item><c>"killed"</c>：焦点角色被打死、队伍仍可能有活人；写报告日志 + 弹简版 panel。</item>
/// <item><c>"incapacitated"</c>：焦点角色 vital capacity 归零；同 killed 处理（Recorder 也记 actor_incapacitated）。</item>
/// <item><c>"party_wiped"</c>：全队覆灭终局；写报告日志 + 弹完整 panel + 终局按钮。</item>
/// </list></para>
///
/// <para>不接管原 <c>Main.HandlePlayerDeath</c> 的"清 UI 状态 / 取消寻路"——那块 90% 是 Main 私有
/// 方法的薄委托，抽出来反而要拉一长串回调；只接管"写报告日志 + 弹 panel"这个内聚部分。
/// 这样做的副作用是：<c>Docs/重构路线图.md</c> 批次 1 第 5 项还没"完全"被本 presenter 吃下去——
/// 留给后续 PlayerDeathFlowController 推进。</para>
/// </remarks>
public sealed class PlayerDeathPresenter
{
	private const double FocalSweepDurationSec = 0.8;
	private const double PartyMemberLostHeadingDurationSec = 2.4;
	private const double PartyWipedHeadingDurationSec = 3.2;
	private const double DeathOverlayDurationSec = 1.5;
	private const double PartyWipedOverlayDurationSec = 2.4;

	private readonly Func<string, int, DeathReport?> _getReport;
	private readonly Action<DeathReport, bool> _showReportPanel;
	private readonly Action<string> _addLog;
	private readonly Action<string, double>? _showHeadingToast;
	private readonly Action<double>? _beginDeathOverlay;
	private readonly Action<int, int, int, double>? _beginCameraSweep;

	/// <param name="getReport">委托：(actorId, currentTurn) → DeathReport？通常是 recorder.GetOrBuildReport。</param>
	/// <param name="showReportPanel">委托：(report, isPartyWipe) → 弹/更新死亡报告 panel。<c>isPartyWipe</c>=true 时显示终局完整版。</param>
	/// <param name="addLog">委托：把一行报告文字写入主日志（Main._log.Add）。</param>
	/// <param name="showHeadingToast">可选：(text, durationSec) → 弹一条"大字头条" toast。给"X 倒下了" / "全队覆灭"演出用。</param>
	/// <param name="beginDeathOverlay">可选：触发屏幕减饱和叠加层（参数为持续秒数）。死亡仪式感第 1 条 UX。</param>
	/// <param name="beginCameraSweep">可选：(fromX, fromY, fromZ, durationSec) → 触发镜头从旧焦点缓动回新焦点。</param>
	public PlayerDeathPresenter(
		Func<string, int, DeathReport?> getReport,
		Action<DeathReport, bool> showReportPanel,
		Action<string> addLog,
		Action<string, double>? showHeadingToast = null,
		Action<double>? beginDeathOverlay = null,
		Action<int, int, int, double>? beginCameraSweep = null)
	{
		_getReport = getReport ?? throw new ArgumentNullException(nameof(getReport));
		_showReportPanel = showReportPanel ?? throw new ArgumentNullException(nameof(showReportPanel));
		_addLog = addLog ?? throw new ArgumentNullException(nameof(addLog));
		_showHeadingToast = showHeadingToast;
		_beginDeathOverlay = beginDeathOverlay;
		_beginCameraSweep = beginCameraSweep;
	}

	/// <summary>
	/// 主入口：从 recorder 拿 report，写日志、弹 panel。
	/// </summary>
	/// <param name="reason"><c>"killed"</c> / <c>"incapacitated"</c> / <c>"party_wiped"</c>。</param>
	/// <param name="actorId">死掉的 actor id；party_wiped 时传焦点 actor 的 id 即可。</param>
	/// <param name="currentTurn">当前回合（用于补缺 report 时）。</param>
	public void Present(string reason, string actorId, int currentTurn)
	{
		if (string.IsNullOrWhiteSpace(reason))
			return;

		var report = _getReport(actorId, currentTurn);
		var isPartyWipe = string.Equals(reason, "party_wiped", StringComparison.Ordinal);

		WriteLogSummary(reason, report);

		if (report != null)
			_showReportPanel(report, isPartyWipe);
	}

	/// <summary>
	/// 仪式感第 1 条（UX 层）：焦点角色倒下 / 焦点切换 / 全队覆灭时，发起一段
	/// "屏幕减饱和 + 大字 heading + 镜头缓动"的演出。
	/// </summary>
	/// <param name="kind">演出类型，决定 heading 文字、overlay 时长、是否触发镜头缓动。</param>
	/// <param name="actorDisplayName">倒下角色的显示名（用于 heading 占位）。</param>
	/// <param name="fromCell">焦点切换时旧焦点的世界坐标，用于触发镜头缓动；其它类型可传 null。</param>
	public void PresentSequence(
		DeathSequenceKind kind,
		string? actorDisplayName,
		(int X, int Y, int Z)? fromCell)
	{
		switch (kind)
		{
			case DeathSequenceKind.PartyMemberLost:
				// 焦点角色倒下：先减饱和 + heading；镜头缓动等紧随其后的 active_actor_switched 触发。
				_beginDeathOverlay?.Invoke(DeathOverlayDurationSec);
				_showHeadingToast?.Invoke(
					LocalizationService.TOrFallback(
						"death.headline.actor_fell",
						"{actor} has fallen",
						("actor", actorDisplayName ?? "?")),
					PartyMemberLostHeadingDurationSec);
				break;
			case DeathSequenceKind.FocusSwept:
				// 紧随 PartyMemberLost 之后的焦点切换：只缓动镜头，不再叠 heading / overlay。
				if (fromCell is { } from)
					_beginCameraSweep?.Invoke(from.X, from.Y, from.Z, FocalSweepDurationSec);
				break;
			case DeathSequenceKind.PartyWiped:
				_beginDeathOverlay?.Invoke(PartyWipedOverlayDurationSec);
				_showHeadingToast?.Invoke(
					LocalizationService.TOrFallback("death.headline.party_wiped", "Your party has fallen"),
					PartyWipedHeadingDurationSec);
				break;
			case DeathSequenceKind.NonActiveMemberLost:
				_showHeadingToast?.Invoke(
					LocalizationService.TOrFallback(
						"death.headline.actor_fell",
						"{actor} has fallen",
						("actor", actorDisplayName ?? "?")),
					PartyMemberLostHeadingDurationSec);
				break;
		}
	}

	private void WriteLogSummary(string reason, DeathReport? report)
	{
		var heading = reason switch
		{
			"incapacitated" => LocalizationService.T("death.incapacitated"),
			"party_wiped" => LocalizationService.T("death.party_wiped"),
			_ => LocalizationService.T("death.killed"),
		};
		_addLog(heading);

		if (report == null)
			return;

		_addLog(LocalizationService.TOrFallback(
			"death.report.cause",
			"Cause: {cause}",
			("cause", LocalizeCause(report.DeathCause, report.KillerName))));
		_addLog(LocalizationService.TOrFallback(
			"death.report.recent_count",
			"Last {count} significant events recorded.",
			("count", report.RecentEvents.Count)));
	}

	private static string LocalizeCause(string causeId, string? killer)
	{
		// killer 走单独一条 i18n key，避免 fallback 把 "killed_by" 翻成 literal 字串
		if (string.Equals(causeId, "killed_by", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(killer))
		{
			return LocalizationService.TOrFallback(
				"death.cause.killed_by",
				"Killed by {killer}",
				("killer", killer));
		}

		var key = causeId switch
		{
			"killed" => "death.cause.killed",
			"incapacitated" => "death.cause.incapacitated",
			"blood_loss" => "death.cause.blood_loss",
			"infection" => "death.cause.infection",
			"food_poisoning" => "death.cause.food_poisoning",
			_ => "death.cause.unknown",
		};
		var fallback = causeId switch
		{
			"killed" => "Killed in combat",
			"incapacitated" => "Incapacitated",
			"blood_loss" => "Bled out",
			"infection" => "Died of infection",
			"food_poisoning" => "Died of food poisoning",
			_ => "Unknown",
		};
		return LocalizationService.TOrFallback(key, fallback);
	}
}

/// <summary>
/// 仪式感第 1 条（UX 层）演出分类。
/// </summary>
public enum DeathSequenceKind
{
	/// <summary>焦点成员倒下：弹"X 倒下了"大字 heading + 减饱和叠加层（不缓镜头）。</summary>
	PartyMemberLost,
	/// <summary>焦点切换：仅触发镜头从 fromCell 缓动到新 active actor 位置，不再叠 heading / overlay。</summary>
	FocusSwept,
	/// <summary>全队覆灭：更长的 overlay + "全队覆灭" heading（不缓镜头）。</summary>
	PartyWiped,
	/// <summary>非焦点队员倒下——只弹 heading toast 提示玩家，不动画面。</summary>
	NonActiveMemberLost,
}
