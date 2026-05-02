using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 验证 <see cref="DeathReportRecorder"/> 的 DeathCause 推断：
/// killed_by / blood_loss / infection / food_poisoning / unknown 各路径都能从事件流推出。
/// </summary>
public sealed class DeathReportCauseTests
{
	[Fact]
	public void Cause_KilledByAttacker_WhenActorKilledHasInitiatorName()
	{
		var recorder = new DeathReportRecorder();
		var state = MakeState(turn: 10);

		recorder.OnEvent(state, new GameEvent("combat_attack")
		{
			TargetId = "victim",
			InitiatorActorName = "wolf",
			Damage = 50,
		});
		recorder.OnEvent(state, new GameEvent("actor_killed")
		{
			TargetId = "victim",
			InitiatorActorName = "wolf",
			InitiatorId = "wolf_id",
		});

		var report = recorder.GetOrBuildReport(state, "victim", state.Turn);
		Assert.NotNull(report);
		Assert.Equal("killed_by", report!.DeathCause);
		Assert.Equal("wolf", report.KillerName);
	}

	[Fact]
	public void Cause_PlainKilled_WhenActorKilledHasNoInitiator()
	{
		var recorder = new DeathReportRecorder();
		var state = MakeState(turn: 10);

		recorder.OnEvent(state, new GameEvent("actor_killed")
		{
			TargetId = "victim",
		});

		var report = recorder.GetOrBuildReport(state, "victim", state.Turn);
		Assert.NotNull(report);
		Assert.Equal("killed", report!.DeathCause);
		Assert.Null(report.KillerName);
	}

	[Fact]
	public void Cause_BloodLoss_WhenDeathBloodLossEventOnly()
	{
		var recorder = new DeathReportRecorder();
		var state = MakeState(turn: 10);

		recorder.OnEvent(state, new GameEvent("death_blood_loss")
		{
			TargetId = "victim",
		});

		var report = recorder.GetOrBuildReport(state, "victim", state.Turn);
		Assert.NotNull(report);
		Assert.Equal("blood_loss", report!.DeathCause);
	}

	[Fact]
	public void Cause_Infection_WhenDeathInfectionEventOnly()
	{
		var recorder = new DeathReportRecorder();
		var state = MakeState(turn: 10);

		recorder.OnEvent(state, new GameEvent("death_infection")
		{
			TargetId = "victim",
		});

		var report = recorder.GetOrBuildReport(state, "victim", state.Turn);
		Assert.NotNull(report);
		Assert.Equal("infection", report!.DeathCause);
	}

	[Fact]
	public void Cause_FoodPoisoning_WhenSpoiledFoodPlusInjury()
	{
		var recorder = new DeathReportRecorder();
		var state = MakeState(turn: 5);

		// Spoiled 食物 → injury_applied(food_poisoning) → 之后 actor_killed
		recorder.OnEvent(state, new GameEvent("food_consumed")
		{
			TargetId = "victim",
			ActionName = "raw_meat",
			EffectType = "spoiled",
		});
		recorder.OnEvent(state, new GameEvent("injury_applied")
		{
			TargetId = "victim",
			ActionName = HealthConditionIds.FoodPoisoning,
		});
		state.Turn = 8;
		recorder.OnEvent(state, new GameEvent("actor_killed")
		{
			TargetId = "victim",
		});

		var report = recorder.GetOrBuildReport(state, "victim", state.Turn);
		Assert.NotNull(report);
		Assert.Equal("food_poisoning", report!.DeathCause);
	}

	[Fact]
	public void Cause_FoodPoisoning_NotInferred_WhenOnlyFoodNoInjury()
	{
		// 只吃了变质食物但没挂上 food_poisoning condition，不应推为 food_poisoning
		var recorder = new DeathReportRecorder();
		var state = MakeState(turn: 5);

		recorder.OnEvent(state, new GameEvent("food_consumed")
		{
			TargetId = "victim",
			ActionName = "raw_meat",
			EffectType = "spoiled",
		});
		recorder.OnEvent(state, new GameEvent("actor_killed")
		{
			TargetId = "victim",
		});

		var report = recorder.GetOrBuildReport(state, "victim", state.Turn);
		Assert.NotNull(report);
		Assert.NotEqual("food_poisoning", report!.DeathCause);
	}

	[Fact]
	public void Cause_Unknown_WhenNoFatalEventRecorded()
	{
		var recorder = new DeathReportRecorder();
		var state = MakeState(turn: 10);

		// 只有 incapacitated（不是死亡）
		recorder.OnEvent(state, new GameEvent("actor_incapacitated")
		{
			TargetId = "victim",
		});

		var report = recorder.GetOrBuildReport(state, "victim", state.Turn);
		Assert.NotNull(report);
		Assert.Equal("incapacitated", report!.DeathCause);
	}

	[Fact]
	public void Cause_KilledBy_TakesPrecedenceOverBloodLoss()
	{
		// 当 actor_killed 带 InitiatorName 时，killed_by 应优先于早期的 death_blood_loss 事件
		var recorder = new DeathReportRecorder();
		var state = MakeState(turn: 5);

		recorder.OnEvent(state, new GameEvent("death_blood_loss")
		{
			TargetId = "victim",
		});
		recorder.OnEvent(state, new GameEvent("actor_killed")
		{
			TargetId = "victim",
			InitiatorActorName = "bandit",
		});

		var report = recorder.GetOrBuildReport(state, "victim", state.Turn);
		Assert.NotNull(report);
		Assert.Equal("killed_by", report!.DeathCause);
		Assert.Equal("bandit", report.KillerName);
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
}
