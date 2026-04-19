using System;
using System.Collections.Generic;
using MiniRPG.Core.AI;
using MiniRPG.Core.AI.Utility;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 验证 <see cref="UtilityBrain.Evaluate"/> 在固定状态 + 固定 RNG 种子下产出
/// 确定性结果——这是"AI 决策稳定 / 调参可复现 / 测试可断言"的前置条件。
///
/// 也守护几个早期边界 case：
/// - 空 action 列表 → 返回 None（不崩 / 不抛 NPE）。
/// - 所有 score 为 0 → 返回 None（让上层把"没人愿意做的事"识别出来，外层会兜底 idle）。
/// </summary>
public class UtilityBrainDeterminismTests
{
	[Fact]
	public void Evaluate_FixedStateAndSeed_ProducesSameAction()
	{
		TestSupport.EnsureGameplayDataLoaded();
		var brain = new UtilityBrain();
		var (state, actor, perception) = MakeMinimalScenario();
		IgniteActor(actor);
		var actions = new List<UtilityActionDef>
		{
			MakeBoolAction("alpha", input: "self_on_fire", bonusScore: 10f),
			MakeBoolAction("beta", input: "self_on_fire", bonusScore: 5f),
		};

		var first = brain.Evaluate(state, actor, perception, behaviorContext: null,
			SimDetail.Simplified, new Random(42), actions);
		var second = brain.Evaluate(state, actor, perception, behaviorContext: null,
			SimDetail.Simplified, new Random(42), actions);

		Assert.NotNull(first.Action);
		Assert.Equal(first.Action!.Id, second.Action!.Id);
		Assert.Equal(first.Score, second.Score);
	}

	[Fact]
	public void Evaluate_DifferentSeeds_StaysWithinRandomJitterBudget()
	{
		// UtilityBrain.ComputeScore 给最终分加 +/-5% 抖动；同一动作不同种子下分数差≤10% bonus。
		TestSupport.EnsureGameplayDataLoaded();
		var brain = new UtilityBrain();
		var (state, actor, perception) = MakeMinimalScenario();
		IgniteActor(actor);
		var actions = new List<UtilityActionDef> { MakeBoolAction("only", "self_on_fire", bonusScore: 10f) };

		var resultA = brain.Evaluate(state, actor, perception, null,
			SimDetail.Simplified, new Random(1), actions);
		var resultB = brain.Evaluate(state, actor, perception, null,
			SimDetail.Simplified, new Random(99), actions);

		Assert.Equal("only", resultA.Action?.Id);
		Assert.Equal("only", resultB.Action?.Id);
		// 两次得分差不应超过 bonus 的 10%（jitter +/-5% × 2）。
		var diff = Math.Abs(resultA.Score - resultB.Score);
		Assert.True(diff <= 10f * 0.10f + 0.001f,
			$"Same action+state with different seeds should drift only within ±5% jitter; diff={diff}");
	}

	[Fact]
	public void Evaluate_EmptyActionList_ReturnsNoneNotThrows()
	{
		TestSupport.EnsureGameplayDataLoaded();
		var brain = new UtilityBrain();
		var (state, actor, perception) = MakeMinimalScenario();

		var result = brain.Evaluate(state, actor, perception, null,
			SimDetail.Simplified, new Random(0), new List<UtilityActionDef>());

		Assert.Same(UtilityEvalResult.None, result);
		Assert.Null(result.Action);
	}

	[Fact]
	public void Evaluate_AllScoresZero_ReturnsNone_AllowsCallerToFallbackToIdle()
	{
		// 全部 action 都因为 input 0 → curve 0 → 总分 0；UtilityBrain 不应选中任何 action。
		// 上层 AIDispatcher 看到 None 应自己兜底走 idle / wander。
		TestSupport.EnsureGameplayDataLoaded();
		var brain = new UtilityBrain();
		var (state, actor, perception) = MakeMinimalScenario();
		var actions = new List<UtilityActionDef>
		{
			MakeBoolAction("never", input: "self_on_fire", bonusScore: 10f),
			MakeBoolAction("also_never", input: "self_on_fire", bonusScore: 7f),
		};

		var result = brain.Evaluate(state, actor, perception, null,
			SimDetail.Simplified, new Random(0), actions);

		Assert.Same(UtilityEvalResult.None, result);
		Assert.Null(result.Action);
	}

	[Fact]
	public void Evaluate_BonusScoreOnlyAction_NoConsiderations_ReturnsThatBonus()
	{
		// 没有 considerations 的 action：ComputeScore 直接返回 BonusScore（不打 jitter）。
		TestSupport.EnsureGameplayDataLoaded();
		var brain = new UtilityBrain();
		var (state, actor, perception) = MakeMinimalScenario();
		var actions = new List<UtilityActionDef>
		{
			new UtilityActionDef { Id = "idle_fallback", Executor = "idle", BonusScore = 0.5f },
		};

		var result = brain.Evaluate(state, actor, perception, null,
			SimDetail.Simplified, new Random(0), actions);

		Assert.NotNull(result.Action);
		Assert.Equal("idle_fallback", result.Action!.Id);
		Assert.Equal(0.5f, result.Score, 4);
	}

	[Fact]
	public void Evaluate_HighScoreActionWins_DeterministicOrdering()
	{
		// 多个 active action 时，高分必胜；让"alpha 分数远高 beta"在固定 seed 下稳定选 alpha。
		TestSupport.EnsureGameplayDataLoaded();
		var brain = new UtilityBrain();
		var (state, actor, perception) = MakeMinimalScenario();
		IgniteActor(actor);
		var actions = new List<UtilityActionDef>
		{
			MakeBoolAction("alpha", input: "self_on_fire", bonusScore: 10f),
			MakeBoolAction("beta", input: "self_on_fire", bonusScore: 5f),
		};

		var result = brain.Evaluate(state, actor, perception, null,
			SimDetail.Simplified, new Random(42), actions);

		Assert.NotNull(result.Action);
		Assert.Equal("alpha", result.Action!.Id);
		Assert.True(result.Score > 5f, $"alpha should outscore beta; got {result.Score}");
	}

	private static void IgniteActor(Actor actor) =>
		actor.HealthConditions.Add(new MiniRPG.Core.Health.HealthConditionState
		{
			Id = MiniRPG.Core.Health.HealthConditionIds.OnFire,
			Severity = 50f,
		});

	private static (GameState State, Actor Actor, Perception Perception) MakeMinimalScenario()
	{
		var state = new GameState { PlayerId = "self" };
		var actor = new Actor
		{
			Id = "self",
			DisplayName = "self",
			Faction = Factions.Player,
			Limbs =
			{
				new Limb
				{
					Id = "self_torso",
					Name = "torso",
					BodyPart = BodyParts.Torso,
					Capacities =
					{
						["blood_circulation"] = 1.0f,
						["consciousness"] = 1.0f,
					},
					MaxDurability = 10,
					Durability = 10,
					Tags = { [CombatModule.VitalTag] = 1 },
				},
			},
		};
		state.Actors[actor.Id] = actor;
		var perception = new Perception { Self = actor };
		return (state, actor, perception);
	}

	/// <summary>构造一个"input 是布尔型 + curve 是 boolean type"的简单 action。</summary>
	private static UtilityActionDef MakeBoolAction(string id, string input, float bonusScore)
	{
		return new UtilityActionDef
		{
			Id = id,
			Executor = id,
			BonusScore = bonusScore,
			Considerations = new List<ConsiderationDef>
			{
				new ConsiderationDef
				{
					Input = input,
					Curve = new ResponseCurve
					{
						Type = CurveType.Boolean,
						TrueValue = 1f,
						FalseValue = 0f,
					},
				},
			},
		};
	}
}
