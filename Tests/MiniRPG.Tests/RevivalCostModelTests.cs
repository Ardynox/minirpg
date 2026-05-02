using System.Collections.Generic;
using MiniRPG.Core.Revival;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 验证 RevivalCostModel 的纯函数公式边界——
/// 0 次 / 1 次 / N 次的 mood scar / capacity penalty / material cost / failure chance / channel turns
/// 在不同 priorRevivalCount 下的单调性、上限 clamp、负输入处理。
/// 这套测试是 P0 优先级因为：未来调整复活数值（设计师可能想改 -3 而不是 -4）时，
/// 一次 ResetForTesting + Override 即可锁住公式语义。
/// </summary>
public class RevivalCostModelTests
{
	[Fact]
	public void ComputeRevivalScar_FirstRevival_BaselineMoodAndDuration()
	{
		ApplyDefaultConfig();

		var (mood, duration) = RevivalCostModel.ComputeRevivalScar(priorRevivalCount: 0);

		Assert.Equal(-4f, mood);
		// 240 turn/day 历法下 BaseDurationTurns = 720（旧 120 turn/day 历法下为 360）。
		Assert.Equal(720, duration);
	}

	[Fact]
	public void ComputeRevivalScar_NRevivals_LinearAccumulationUntilCap()
	{
		ApplyDefaultConfig();

		Assert.Equal(-8f, RevivalCostModel.ComputeRevivalScar(1).MoodOffset);
		Assert.Equal(-12f, RevivalCostModel.ComputeRevivalScar(2).MoodOffset);
		Assert.Equal(-16f, RevivalCostModel.ComputeRevivalScar(3).MoodOffset);
		Assert.Equal(-20f, RevivalCostModel.ComputeRevivalScar(4).MoodOffset);
		// 4 次累加之后到达 -20 上限，第 5 次仍然 -20。
		Assert.Equal(-20f, RevivalCostModel.ComputeRevivalScar(5).MoodOffset);

		// 240 turn/day 历法下 Base/PerAttempt/MaxDurationTurns 分别为 720 / 720 / 3600（旧历法下 360 / 360 / 1800）。
		Assert.Equal(1440, RevivalCostModel.ComputeRevivalScar(1).DurationTurns);
		Assert.Equal(2160, RevivalCostModel.ComputeRevivalScar(2).DurationTurns);
		Assert.Equal(2880, RevivalCostModel.ComputeRevivalScar(3).DurationTurns);
		Assert.Equal(3600, RevivalCostModel.ComputeRevivalScar(4).DurationTurns);
		// duration 在 MaxDurationTurns 处饱和。
		Assert.Equal(3600, RevivalCostModel.ComputeRevivalScar(5).DurationTurns);
		Assert.Equal(3600, RevivalCostModel.ComputeRevivalScar(20).DurationTurns);
	}

	[Fact]
	public void ComputeRevivalScar_NegativePriorCount_TreatedAsZero()
	{
		ApplyDefaultConfig();

		var atZero = RevivalCostModel.ComputeRevivalScar(0);
		var atNeg = RevivalCostModel.ComputeRevivalScar(-3);

		Assert.Equal(atZero.MoodOffset, atNeg.MoodOffset);
		Assert.Equal(atZero.DurationTurns, atNeg.DurationTurns);
	}

	[Fact]
	public void ComputeCapacityPenalty_LinearUntilCap()
	{
		ApplyDefaultConfig();

		Assert.Equal(0f, RevivalCostModel.ComputeCapacityPenalty(0));
		Assert.Equal(0.05f, RevivalCostModel.ComputeCapacityPenalty(1), 5);
		Assert.Equal(0.10f, RevivalCostModel.ComputeCapacityPenalty(2), 5);
		Assert.Equal(0.40f, RevivalCostModel.ComputeCapacityPenalty(8), 5);
		// 8 次后到达 40% 上限，第 9 次仍 0.4。
		Assert.Equal(0.40f, RevivalCostModel.ComputeCapacityPenalty(9), 5);
		Assert.Equal(0.40f, RevivalCostModel.ComputeCapacityPenalty(100), 5);
	}

	[Fact]
	public void IsPermanentlyLost_HitsThresholdAtSixthRevival()
	{
		ApplyDefaultConfig();

		Assert.False(RevivalCostModel.IsPermanentlyLost(0));
		Assert.False(RevivalCostModel.IsPermanentlyLost(5));
		Assert.True(RevivalCostModel.IsPermanentlyLost(6));
		Assert.True(RevivalCostModel.IsPermanentlyLost(7));
		// 上溢一点不影响判定。
		Assert.True(RevivalCostModel.IsPermanentlyLost(int.MaxValue));
	}

	[Fact]
	public void ComputeMaterialCost_ScalesByPerAttemptMultiplier_RoundsUp()
	{
		// 自定义 method：base herbs=4，倍率 1.5。
		// priorCount=0 → ceil(4*1) = 4
		// priorCount=1 → ceil(4*1.5) = 6
		// priorCount=2 → ceil(4*2.25) = 9
		// priorCount=3 → ceil(4*3.375) = 14
		RevivalCostModel.OverrideConfigForTesting(new RevivalCostsConfig
		{
			Methods = new(System.StringComparer.Ordinal)
			{
				["test"] = new RevivalMethodConfig
				{
					BaseMaterials = new(System.StringComparer.Ordinal) { ["herb"] = 4 },
					PerAttemptMultiplier = 1.5f,
				},
			},
		});

		Assert.Equal(4, RevivalCostModel.ComputeMaterialCost("test", 0)["herb"]);
		Assert.Equal(6, RevivalCostModel.ComputeMaterialCost("test", 1)["herb"]);
		Assert.Equal(9, RevivalCostModel.ComputeMaterialCost("test", 2)["herb"]);
		Assert.Equal(14, RevivalCostModel.ComputeMaterialCost("test", 3)["herb"]);
	}

	[Fact]
	public void ComputeMaterialCost_UnknownMethod_EmptyDict()
	{
		ApplyDefaultConfig();

		var cost = RevivalCostModel.ComputeMaterialCost("does_not_exist", priorRevivalCount: 0);

		Assert.Empty(cost);
	}

	[Fact]
	public void ComputeMaterialCost_NeverDropsBelowOnePerItem()
	{
		// base 1 + 倍率 1.5 ^ 0 = 1；ceil 也不会变成 0。
		RevivalCostModel.OverrideConfigForTesting(new RevivalCostsConfig
		{
			Methods = new(System.StringComparer.Ordinal)
			{
				["tiny"] = new RevivalMethodConfig
				{
					BaseMaterials = new(System.StringComparer.Ordinal) { ["dust"] = 1 },
					PerAttemptMultiplier = 1.5f,
				},
			},
		});

		Assert.Equal(1, RevivalCostModel.ComputeMaterialCost("tiny", 0)["dust"]);
		Assert.Equal(2, RevivalCostModel.ComputeMaterialCost("tiny", 1)["dust"]);
	}

	[Fact]
	public void ComputeFailureChance_ScalesByPriorCount_ClampsToMax()
	{
		RevivalCostModel.OverrideConfigForTesting(new RevivalCostsConfig
		{
			Methods = new(System.StringComparer.Ordinal)
			{
				["risky"] = new RevivalMethodConfig
				{
					BaseFailureChance = 0.10f,
					FailureChancePerPriorRevival = 0.15f,
					MaxFailureChance = 0.50f,
				},
			},
		});

		Assert.Equal(0.10f, RevivalCostModel.ComputeFailureChance("risky", 0), 4);
		Assert.Equal(0.25f, RevivalCostModel.ComputeFailureChance("risky", 1), 4);
		Assert.Equal(0.40f, RevivalCostModel.ComputeFailureChance("risky", 2), 4);
		// 第 3 次：0.10 + 0.15*3 = 0.55，被 max 0.50 截断。
		Assert.Equal(0.50f, RevivalCostModel.ComputeFailureChance("risky", 3), 4);
		Assert.Equal(0.50f, RevivalCostModel.ComputeFailureChance("risky", 100), 4);
	}

	[Fact]
	public void ComputeFailureChance_UnknownMethod_ReturnsOne()
	{
		ApplyDefaultConfig();

		Assert.Equal(1f, RevivalCostModel.ComputeFailureChance("ghost", 0), 4);
	}

	[Fact]
	public void GetChannelTurns_FromConfig_OrFallbackOne()
	{
		RevivalCostModel.OverrideConfigForTesting(new RevivalCostsConfig
		{
			Methods = new(System.StringComparer.Ordinal)
			{
				["slow"] = new RevivalMethodConfig { ChannelTurns = 12 },
			},
		});

		Assert.Equal(12, RevivalCostModel.GetChannelTurns("slow"));
		// 未注册 method 用 fallback 1（让 TimelineTurnManager.TryExecuteRevive 至少 1 回合）。
		Assert.Equal(1, RevivalCostModel.GetChannelTurns("missing"));
	}

	/// <summary>
	/// 让代码示例的 mood scar / penalty / threshold 默认值跟 Defaults() 对齐。
	/// 直接重置 + Override 默认配置，避免 Data/Config/revival_costs.json 的实测值
	/// 改动让本测试一起改。
	/// </summary>
	private static void ApplyDefaultConfig()
	{
		RevivalCostModel.OverrideConfigForTesting(RevivalCostsConfig.Defaults());
	}
}
