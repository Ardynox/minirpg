using System;

namespace MiniRPG.Core.AI.Utility;

public enum CurveType
{
	Boolean,
	Linear,
	Exponential,
	Logistic,
	Inverse,
	Step,
}

public sealed class ResponseCurve
{
	public CurveType Type { get; set; }
	public float Slope { get; set; }
	public float Offset { get; set; }
	public float Exponent { get; set; } = 2f;
	public float Steepness { get; set; } = 10f;
	public float Midpoint { get; set; } = 0.5f;
	public float Threshold { get; set; } = 0.5f;
	public float TrueValue { get; set; } = 1f;
	public float FalseValue { get; set; }
	public float High { get; set; } = 1f;
	public float Low { get; set; }

	public float Evaluate(float x)
	{
		return Type switch
		{
			CurveType.Boolean => x > 0f ? TrueValue : FalseValue,
			CurveType.Linear => Clamp01(Slope * x + Offset),
			CurveType.Exponential => Clamp01(MathF.Pow(Clamp01(x), Exponent)),
			CurveType.Logistic => Clamp01(1f / (1f + MathF.Exp(-Steepness * (x - Midpoint)))),
			CurveType.Inverse => Clamp01(1f - x),
			CurveType.Step => x >= Threshold ? High : Low,
			_ => 0f,
		};
	}

	private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
}
