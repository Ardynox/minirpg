using System;

namespace MiniRPG.Core.World;

public readonly record struct DirectionalVisionRange(int FrontRadius, int RearRadius);

public static class VisionRangeScaler
{
	public static int ScaleRadius(
		int baseRadius,
		float sightMultiplier,
		int minimumRadius = 0,
		float externalMultiplier = 1.0f)
	{
		if (baseRadius <= 0)
			return 0;

		sightMultiplier = Math.Max(0f, sightMultiplier);
		externalMultiplier = Math.Max(0f, externalMultiplier);
		if (sightMultiplier <= 0f || externalMultiplier <= 0f)
			return 0;

		var radius = (int)(baseRadius * sightMultiplier * externalMultiplier);
		return Math.Max(Math.Max(0, minimumRadius), radius);
	}

	public static DirectionalVisionRange ScaleDirectional(
		int baseFrontRadius,
		float sightMultiplier,
		float rearVisionRatio,
		int minimumFrontRadius = 0,
		int minimumRearRadius = 0,
		float externalMultiplier = 1.0f)
	{
		var frontRadius = ScaleRadius(
			baseFrontRadius,
			sightMultiplier,
			minimumFrontRadius,
			externalMultiplier);
		if (frontRadius <= 0)
			return new DirectionalVisionRange(0, 0);

		var rearRadius = (int)(frontRadius * Math.Max(0f, rearVisionRatio));
		rearRadius = Math.Max(Math.Max(0, minimumRearRadius), rearRadius);
		return new DirectionalVisionRange(frontRadius, rearRadius);
	}
}
