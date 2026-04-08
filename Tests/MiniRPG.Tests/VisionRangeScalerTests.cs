using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

public sealed class VisionRangeScalerTests
{
	// ── ScaleRadius ──────────────────────────────────────

	[Fact]
	public void ScaleRadius_BaseMultiplied()
	{
		var result = VisionRangeScaler.ScaleRadius(10, 1.0f);
		Assert.Equal(10, result);
	}

	[Fact]
	public void ScaleRadius_HalfMultiplier()
	{
		var result = VisionRangeScaler.ScaleRadius(10, 0.5f);
		Assert.Equal(5, result);
	}

	[Fact]
	public void ScaleRadius_ZeroBase_ReturnsZero()
	{
		Assert.Equal(0, VisionRangeScaler.ScaleRadius(0, 1.0f));
	}

	[Fact]
	public void ScaleRadius_NegativeBase_ReturnsZero()
	{
		Assert.Equal(0, VisionRangeScaler.ScaleRadius(-5, 1.0f));
	}

	[Fact]
	public void ScaleRadius_ZeroMultiplier_ReturnsZero()
	{
		Assert.Equal(0, VisionRangeScaler.ScaleRadius(10, 0f));
	}

	[Fact]
	public void ScaleRadius_NegativeMultiplier_ReturnsZero()
	{
		Assert.Equal(0, VisionRangeScaler.ScaleRadius(10, -1.0f));
	}

	[Fact]
	public void ScaleRadius_MinimumRadius_Enforced()
	{
		// Scaled value would be 1, but minimum is 3
		var result = VisionRangeScaler.ScaleRadius(10, 0.1f, minimumRadius: 3);
		Assert.Equal(3, result);
	}

	[Fact]
	public void ScaleRadius_MinimumRadius_NotUsedWhenScaledIsHigher()
	{
		var result = VisionRangeScaler.ScaleRadius(10, 1.0f, minimumRadius: 3);
		Assert.Equal(10, result);
	}

	[Fact]
	public void ScaleRadius_ExternalMultiplier_Applied()
	{
		var result = VisionRangeScaler.ScaleRadius(10, 1.0f, externalMultiplier: 2.0f);
		Assert.Equal(20, result);
	}

	[Fact]
	public void ScaleRadius_ExternalMultiplier_Zero_ReturnsZero()
	{
		Assert.Equal(0, VisionRangeScaler.ScaleRadius(10, 1.0f, externalMultiplier: 0f));
	}

	[Fact]
	public void ScaleRadius_ExternalMultiplier_Negative_ReturnsZero()
	{
		Assert.Equal(0, VisionRangeScaler.ScaleRadius(10, 1.0f, externalMultiplier: -1.0f));
	}

	[Fact]
	public void ScaleRadius_BothMultipliers_Combined()
	{
		// 10 * 0.5 * 2.0 = 10
		var result = VisionRangeScaler.ScaleRadius(10, 0.5f, externalMultiplier: 2.0f);
		Assert.Equal(10, result);
	}

	// ── ScaleDirectional ─────────────────────────────────

	[Fact]
	public void ScaleDirectional_BasicCase()
	{
		var result = VisionRangeScaler.ScaleDirectional(10, 1.0f, 0.5f);
		Assert.Equal(10, result.FrontRadius);
		Assert.Equal(5, result.RearRadius);
	}

	[Fact]
	public void ScaleDirectional_ZeroFront_BothZero()
	{
		var result = VisionRangeScaler.ScaleDirectional(0, 1.0f, 0.5f);
		Assert.Equal(0, result.FrontRadius);
		Assert.Equal(0, result.RearRadius);
	}

	[Fact]
	public void ScaleDirectional_ZeroMultiplier_BothZero()
	{
		var result = VisionRangeScaler.ScaleDirectional(10, 0f, 0.5f);
		Assert.Equal(0, result.FrontRadius);
		Assert.Equal(0, result.RearRadius);
	}

	[Fact]
	public void ScaleDirectional_RearRatio_One_EqualRadii()
	{
		var result = VisionRangeScaler.ScaleDirectional(10, 1.0f, 1.0f);
		Assert.Equal(10, result.FrontRadius);
		Assert.Equal(10, result.RearRadius);
	}

	[Fact]
	public void ScaleDirectional_RearRatio_Zero_RearIsZero()
	{
		var result = VisionRangeScaler.ScaleDirectional(10, 1.0f, 0f);
		Assert.Equal(10, result.FrontRadius);
		Assert.Equal(0, result.RearRadius);
	}

	[Fact]
	public void ScaleDirectional_NegativeRearRatio_ClampedToZero()
	{
		var result = VisionRangeScaler.ScaleDirectional(10, 1.0f, -0.5f);
		Assert.Equal(10, result.FrontRadius);
		Assert.Equal(0, result.RearRadius);
	}

	[Fact]
	public void ScaleDirectional_MinimumFrontRadius_Enforced()
	{
		var result = VisionRangeScaler.ScaleDirectional(10, 0.1f, 0.5f, minimumFrontRadius: 5);
		Assert.True(result.FrontRadius >= 5);
	}

	[Fact]
	public void ScaleDirectional_MinimumRearRadius_Enforced()
	{
		var result = VisionRangeScaler.ScaleDirectional(10, 1.0f, 0.1f, minimumRearRadius: 3);
		Assert.True(result.RearRadius >= 3);
	}

	[Fact]
	public void ScaleDirectional_ExternalMultiplier_Applied()
	{
		var result = VisionRangeScaler.ScaleDirectional(10, 1.0f, 0.5f, externalMultiplier: 2.0f);
		Assert.Equal(20, result.FrontRadius);
		Assert.Equal(10, result.RearRadius);
	}

	// ── DirectionalVisionRange record ────────────────────

	[Fact]
	public void DirectionalVisionRange_ValueEquality()
	{
		var a = new DirectionalVisionRange(10, 5);
		var b = new DirectionalVisionRange(10, 5);
		Assert.Equal(a, b);
	}

	[Fact]
	public void DirectionalVisionRange_Inequality()
	{
		var a = new DirectionalVisionRange(10, 5);
		var b = new DirectionalVisionRange(10, 3);
		Assert.NotEqual(a, b);
	}
}
