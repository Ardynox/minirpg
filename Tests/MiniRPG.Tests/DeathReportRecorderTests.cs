using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 验证 <see cref="DeathReportRecorder"/> 的事件捕获和"最近 30 回合"窗口截断。
/// 不依赖完整 GameState；用最小 stub 验证 OnEvent / GetOrBuildReport / Reset。
/// </summary>
public sealed class DeathReportRecorderTests
{
	[Fact]
	public void OnEvent_RecordsCombatAttackOnTarget()
	{
		var recorder = new DeathReportRecorder();
		var state = MakeState(turn: 10);

		recorder.OnEvent(state, MakeAttack(targetId: "victim", initiatorName: "wolf", damage: 50, limb: "torso"));

		Assert.Equal(1, recorder.TrackedActorCount);
		var report = recorder.GetOrBuildReport(state, "victim", state.Turn);
		Assert.NotNull(report);
		Assert.Single(report!.RecentEvents);
		Assert.Equal("combat_attack", report.RecentEvents[0].EventType);
		Assert.Equal(50, report.RecentEvents[0].Damage);
		Assert.Equal("wolf", report.RecentEvents[0].InitiatorActorName);
		Assert.Equal("torso", report.RecentEvents[0].LimbName);
	}

	[Fact]
	public void OnEvent_BuildsReportOnActorKilled_IncludingPriorAttacks()
	{
		var recorder = new DeathReportRecorder();
		var state = MakeState(turn: 10);

		recorder.OnEvent(state, MakeAttack("victim", "wolf", 50, "torso"));
		state.Turn = 15;
		recorder.OnEvent(state, MakeAttack("victim", "wolf", 50, "torso"));
		recorder.OnEvent(state, MakeKilled("victim", "wolf"));

		var report = recorder.GetOrBuildReport(state, "victim", state.Turn);
		Assert.NotNull(report);
		Assert.Equal(15, report!.DeathTurn);
		Assert.Equal(3, report.RecentEvents.Count);
		Assert.Equal("combat_attack", report.RecentEvents[0].EventType);
		Assert.Equal("combat_attack", report.RecentEvents[1].EventType);
		Assert.Equal("actor_killed", report.RecentEvents[2].EventType);
	}

	[Fact]
	public void GetOrBuildReport_DropsEventsOutsideRetentionWindow()
	{
		var recorder = new DeathReportRecorder();
		var state = MakeState(turn: 10);
		recorder.OnEvent(state, MakeAttack("victim", "wolf", 5, "torso"));

		// 跨过保留窗口（30 回合）
		state.Turn = 10 + DeathReportRecorder.MaxRetentionTurns + 5;
		recorder.OnEvent(state, MakeKilled("victim", "wolf"));

		var report = recorder.GetOrBuildReport(state, "victim", state.Turn);
		Assert.NotNull(report);
		// 早期那条 attack 已被时间窗口过滤掉
		Assert.Single(report!.RecentEvents);
		Assert.Equal("actor_killed", report.RecentEvents[0].EventType);
	}

	[Fact]
	public void OnEvent_RingBufferTruncatesAtMaxEventsPerActor()
	{
		var recorder = new DeathReportRecorder();
		var state = MakeState(turn: 0);

		// 灌满 RingBuffer + 多 5 条
		for (var i = 0; i < DeathReportRecorder.MaxEventsPerActor + 5; i++)
		{
			state.Turn = i;
			recorder.OnEvent(state, MakeAttack("victim", "wolf", 1, "torso"));
		}

		var report = recorder.GetOrBuildReport(state, "victim", state.Turn);
		Assert.NotNull(report);
		// 最近 30 回合的窗口会进一步压缩，但 RingBuffer 容量上限本身已经截断
		Assert.True(report!.RecentEvents.Count <= DeathReportRecorder.MaxEventsPerActor);
	}

	[Fact]
	public void Reset_ClearsAllTrackedActors()
	{
		var recorder = new DeathReportRecorder();
		var state = MakeState(turn: 10);
		recorder.OnEvent(state, MakeAttack("victim", "wolf", 5, "torso"));
		Assert.Equal(1, recorder.TrackedActorCount);

		recorder.Reset();

		Assert.Equal(0, recorder.TrackedActorCount);
		Assert.Null(recorder.GetOrBuildReport(state, "victim", state.Turn));
	}

	[Fact]
	public void OnEvent_IgnoresEventsWithBlankTargetId()
	{
		var recorder = new DeathReportRecorder();
		var state = MakeState(turn: 10);
		var blank = new GameEvent("combat_attack")
		{
			TargetId = "",
			InitiatorActorName = "wolf",
			Damage = 5,
		};
		recorder.OnEvent(state, blank);
		Assert.Equal(0, recorder.TrackedActorCount);
	}

	private static GameState MakeState(int turn)
	{
		return new GameState
		{
			PlayerId = "player",
			WorldSeed = 1,
			Turn = turn,
		};
	}

	private static GameEvent MakeAttack(string targetId, string initiatorName, int damage, string limb)
	{
		return new GameEvent("combat_attack")
		{
			TargetId = targetId,
			InitiatorActorName = initiatorName,
			Damage = damage,
			LimbName = limb,
			ActionName = "bite",
		};
	}

	private static GameEvent MakeKilled(string targetId, string? initiatorName)
	{
		var ev = new GameEvent("actor_killed")
		{
			TargetId = targetId,
			InitiatorActorName = initiatorName,
		};
		if (!string.IsNullOrWhiteSpace(initiatorName))
			ev.InitiatorId = initiatorName + "_id";
		return ev;
	}
}
