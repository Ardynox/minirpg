using System;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;

namespace MiniRPG.Module;

internal readonly record struct AutoTestVisionProbe(
	int EffectiveBaseVisionRadius,
	DirectionalVisionRange Vision,
	int PartialVisibleForwardOffset,
	int PartialHiddenForwardOffset);

internal static class AutoTestVisionProbeHelper
{
	public static AutoTestVisionProbe Create(
		int baseVisionRadius,
		float sightMultiplier,
		float rearVisionRatio,
		int minimumVisionRadius,
		int minimumRearVisionRadius,
		float ambientLight,
		bool weatherExposed,
		WeatherSample weather)
	{
		var effectiveBaseVisionRadius = baseVisionRadius;
		if (weatherExposed)
		{
			effectiveBaseVisionRadius = Math.Max(
				1,
				(int)MathF.Round(baseVisionRadius * WeatherRules.GetVisionMultiplier(weather)));
		}

		var vision = VisionRangeScaler.ScaleDirectional(
			effectiveBaseVisionRadius,
			Math.Max(0f, sightMultiplier),
			rearVisionRatio,
			minimumVisionRadius,
			minimumRearVisionRadius,
			ambientLight);
		var partialVisibleForwardOffset = Math.Max(1, vision.FrontRadius);
		return new AutoTestVisionProbe(
			effectiveBaseVisionRadius,
			vision,
			partialVisibleForwardOffset,
			partialVisibleForwardOffset + 1);
	}
}
