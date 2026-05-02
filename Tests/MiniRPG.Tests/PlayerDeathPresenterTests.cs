using System.Collections.Generic;
using MiniRPG.Core.Calendar;
using MiniRPG.Core.Combat;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 验证 <see cref="PlayerDeathPresenter"/>：
/// 拿到 report 后调 panel.ShowReport 一次、写若干日志行；reason="party_wiped" 时 isPartyWipe 标记正确传递。
/// 不依赖 Godot 节点；用纯 stub 委托验证调用次数和参数。
/// </summary>
public sealed class PlayerDeathPresenterTests
{
	[Fact]
	public void Present_KilledReason_ShowsPanelOnce_WithIsPartyWipeFalse()
	{
		var report = MakeReport("victim", "killed_by", killerName: "wolf", deathTurn: 50);
		var captured = new List<(DeathReport report, bool isPartyWipe)>();
		var logs = new List<string>();
		var presenter = new PlayerDeathPresenter(
			getReport: (id, turn) => report,
			showReportPanel: (r, p) => captured.Add((r, p)),
			addLog: logs.Add);

		presenter.Present("killed", "victim", currentTurn: 50);

		Assert.Single(captured);
		Assert.Same(report, captured[0].report);
		Assert.False(captured[0].isPartyWipe);
		Assert.NotEmpty(logs);
	}

	[Fact]
	public void Present_PartyWipedReason_PassesIsPartyWipeTrue()
	{
		var report = MakeReport("leader", "killed", killerName: null, deathTurn: 100);
		var captured = new List<(DeathReport report, bool isPartyWipe)>();
		var presenter = new PlayerDeathPresenter(
			getReport: (id, turn) => report,
			showReportPanel: (r, p) => captured.Add((r, p)),
			addLog: _ => { });

		presenter.Present("party_wiped", "leader", currentTurn: 100);

		Assert.Single(captured);
		Assert.True(captured[0].isPartyWipe);
	}

	[Fact]
	public void Present_NullReport_StillWritesHeadingButSkipsPanel()
	{
		var captured = new List<(DeathReport report, bool isPartyWipe)>();
		var logs = new List<string>();
		var presenter = new PlayerDeathPresenter(
			getReport: (id, turn) => null,
			showReportPanel: (r, p) => captured.Add((r, p)),
			addLog: logs.Add);

		presenter.Present("killed", "victim", currentTurn: 1);

		Assert.Empty(captured);
		Assert.NotEmpty(logs); // 至少 heading 被写入
	}

	[Fact]
	public void Present_BlankReason_NoOp()
	{
		var captured = new List<(DeathReport report, bool isPartyWipe)>();
		var logs = new List<string>();
		var presenter = new PlayerDeathPresenter(
			getReport: (id, turn) => MakeReport("victim", "killed", null, 1),
			showReportPanel: (r, p) => captured.Add((r, p)),
			addLog: logs.Add);

		presenter.Present("", "victim", currentTurn: 1);

		Assert.Empty(captured);
		Assert.Empty(logs);
	}

	[Fact]
	public void Present_KilledByCause_LogsKillerName()
	{
		var report = MakeReport("victim", "killed_by", killerName: "Bandit Alpha", deathTurn: 5);
		var logs = new List<string>();
		var presenter = new PlayerDeathPresenter(
			getReport: (id, turn) => report,
			showReportPanel: (r, p) => { },
			addLog: logs.Add);

		presenter.Present("killed", "victim", currentTurn: 5);

		Assert.Contains(logs, line => line.Contains("Bandit Alpha"));
	}

	private static DeathReport MakeReport(string actorId, string cause, string? killerName, int deathTurn)
	{
		return new DeathReport
		{
			ActorId = actorId,
			ActorName = actorId,
			DeathTurn = deathTurn,
			DeathCalendarView = CalendarService.View(deathTurn),
			DeathCause = cause,
			KillerName = killerName,
			RecentEvents =
			[
				new RecentEventEntry
				{
					Turn = deathTurn - 1,
					EventType = "combat_attack",
					InitiatorActorName = killerName,
					Damage = 30,
					LimbName = "torso",
				},
				new RecentEventEntry
				{
					Turn = deathTurn,
					EventType = "actor_killed",
					InitiatorActorName = killerName,
				},
			],
		};
	}
}
