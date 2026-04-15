using System;
using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using MiniRPG.Module.Render;

namespace MiniRPG.Module.Render;

internal sealed class VoxelLightingCalculator
{
	private const float MinLight = 0.08f;
	private const float MaxLight = 1.35f;

	private IsometricVoxelRenderer.IsometricLightingSettings _lighting;
	private DayNightSnapshot _dayNight;
	private readonly LightMap _lightMap;

	public VoxelLightingCalculator(LightMap lightMap)
	{
		_lightMap = lightMap;
	}

	public void Configure(IsometricVoxelRenderer.IsometricLightingSettings lighting, DayNightSnapshot dayNight)
	{
		_lighting = lighting;
		_dayNight = dayNight;
	}

	public Color GetFaceTint(WorldMap? world, int wx, int wy, int wz, int playerZ, IsometricVoxelRenderer.VoxelFace face, Color visionTint)
	{
		if (visionTint.R <= 0f && visionTint.G <= 0f && visionTint.B <= 0f)
			return visionTint;

		var faceLight = face switch
		{
			IsometricVoxelRenderer.VoxelFace.Top => _lighting.TopLight,
			IsometricVoxelRenderer.VoxelFace.Left => _lighting.LeftLight,
			IsometricVoxelRenderer.VoxelFace.Right => _lighting.RightLight,
			_ => 1f,
		};

		var depth = wz - playerZ;
		var depthAttenuation = Math.Max(0.35f, 1f - depth * _lighting.DepthFalloff);
		var brightness = Math.Clamp(_lighting.Ambient * _dayNight.AmbientMultiplier * faceLight * depthAttenuation, MinLight, MaxLight);
		brightness = MathF.Pow(brightness, _lighting.Contrast);

		var ao = ComputeAmbientOcclusion(world, wx, wy, wz, face);
		var sunShadow = ComputeSunShadow(world, wx, wy, wz, face);
		var shadedBrightness = Math.Clamp(brightness * (1f - ao) * (1f - sunShadow), MinLight, MaxLight);

		var tintR = _dayNight.SunTintR;
		var tintG = _dayNight.SunTintG;
		var tintB = _dayNight.SunTintB;

		if (_lightMap.TryGetLight(wx, wy, wz, out var cellLight))
		{
			var pointBrightness = cellLight.Intensity * 0.5f;
			shadedBrightness = Math.Clamp(shadedBrightness + pointBrightness, MinLight, MaxLight);

			var blend = Math.Clamp(pointBrightness / (shadedBrightness + 0.001f) * 0.6f, 0f, 0.8f);
			tintR = tintR + (cellLight.R - tintR) * blend;
			tintG = tintG + (cellLight.G - tintG) * blend;
			tintB = tintB + (cellLight.B - tintB) * blend;
		}

		return new Color(
			Math.Clamp(visionTint.R * shadedBrightness * tintR, 0f, 1f),
			Math.Clamp(visionTint.G * shadedBrightness * tintG, 0f, 1f),
			Math.Clamp(visionTint.B * shadedBrightness * tintB, 0f, 1f),
			visionTint.A);
	}

	private float ComputeAmbientOcclusion(WorldMap? worldMap, int wx, int wy, int wz, IsometricVoxelRenderer.VoxelFace face)
	{
		if (_lighting.ShadowStrength <= 0f || _lighting.OcclusionStep <= 0f)
			return 0f;
		if (worldMap == null)
			return 0f;

		var world = worldMap;
		var ao = 0f;

		switch (face)
		{
			case IsometricVoxelRenderer.VoxelFace.Top:
			{
				const float neighborWeight = 0.06f;
				if (world.GetTerrain(wx - 1, wy, wz).IsOpaque) ao += neighborWeight;
				if (world.GetTerrain(wx + 1, wy, wz).IsOpaque) ao += neighborWeight;
				if (world.GetTerrain(wx, wy - 1, wz).IsOpaque) ao += neighborWeight;
				if (world.GetTerrain(wx, wy + 1, wz).IsOpaque) ao += neighborWeight;
				if (world.GetTerrain(wx - 1, wy - 1, wz).IsOpaque) ao += neighborWeight * 0.5f;
				if (world.GetTerrain(wx + 1, wy - 1, wz).IsOpaque) ao += neighborWeight * 0.5f;
				if (world.GetTerrain(wx - 1, wy + 1, wz).IsOpaque) ao += neighborWeight * 0.5f;
				if (world.GetTerrain(wx + 1, wy + 1, wz).IsOpaque) ao += neighborWeight * 0.5f;

				const float aboveWeight = 0.04f;
				if (world.GetTerrain(wx + 1, wy + 1, wz - 1).IsOpaque) ao += aboveWeight;
				if (world.GetTerrain(wx - 1, wy + 1, wz - 1).IsOpaque) ao += aboveWeight;
				if (world.GetTerrain(wx + 1, wy - 1, wz - 1).IsOpaque) ao += aboveWeight;
				if (world.GetTerrain(wx - 1, wy - 1, wz - 1).IsOpaque) ao += aboveWeight;

				const float cornerBonus = 0.08f;
				var n = world.GetTerrain(wx, wy - 1, wz).IsOpaque;
				var s = world.GetTerrain(wx, wy + 1, wz).IsOpaque;
				var w = world.GetTerrain(wx - 1, wy, wz).IsOpaque;
				var e = world.GetTerrain(wx + 1, wy, wz).IsOpaque;
				if (n && w) ao += cornerBonus;
				if (n && e) ao += cornerBonus;
				if (s && w) ao += cornerBonus;
				if (s && e) ao += cornerBonus;
				break;
			}
			case IsometricVoxelRenderer.VoxelFace.Left:
			{
				const float sideWeight = 0.10f;
				const float aboveSideWeight = 0.06f;
				if (world.GetTerrain(wx, wy + 1, wz).IsOpaque) ao += sideWeight;
				if (world.GetTerrain(wx, wy + 1, wz - 1).IsOpaque) ao += aboveSideWeight;
				if (world.GetTerrain(wx - 1, wy + 1, wz).IsOpaque) ao += sideWeight * 0.5f;
				if (world.GetTerrain(wx, wy, wz - 1).IsOpaque) ao += aboveSideWeight;
				break;
			}
			case IsometricVoxelRenderer.VoxelFace.Right:
			{
				const float sideWeight = 0.10f;
				const float aboveSideWeight = 0.06f;
				if (world.GetTerrain(wx + 1, wy, wz).IsOpaque) ao += sideWeight;
				if (world.GetTerrain(wx + 1, wy, wz - 1).IsOpaque) ao += aboveSideWeight;
				if (world.GetTerrain(wx + 1, wy - 1, wz).IsOpaque) ao += sideWeight * 0.5f;
				if (world.GetTerrain(wx, wy, wz - 1).IsOpaque) ao += aboveSideWeight;
				break;
			}
		}

		return Math.Clamp(ao * _lighting.ShadowStrength, 0f, 0.65f);
	}

	private float ComputeSunShadow(WorldMap? worldMap, int wx, int wy, int wz, IsometricVoxelRenderer.VoxelFace face)
	{
		if (_dayNight.SunAltitude <= 0.01f)
			return 0f;
		if (_lighting.ShadowStrength <= 0f)
			return 0f;
		if (worldMap == null)
			return 0f;

		var world = worldMap;
		var maxSteps = (int)Math.Clamp(3f / Math.Max(0.3f, _dayNight.SunAltitude), 2, 8);
		var dirX = _dayNight.SunDirectionX;
		var dirY = _dayNight.SunDirectionY;

		var shadow = 0f;
		for (var step = 1; step <= maxSteps; step++)
		{
			var sampleX = wx + (int)MathF.Round(dirX * step);
			var sampleY = wy + (int)MathF.Round(dirY * step);
			var sampleZ = wz - step;

			if (world.GetTerrain(sampleX, sampleY, sampleZ).IsOpaque)
			{
				var contribution = _lighting.OcclusionStep * (1f - (step - 1) / (float)(maxSteps + 1));
				shadow += contribution;
			}
		}

		var faceScale = face switch
		{
			IsometricVoxelRenderer.VoxelFace.Top => 0.78f,
			IsometricVoxelRenderer.VoxelFace.Left => 1.10f,
			IsometricVoxelRenderer.VoxelFace.Right => 0.92f,
			_ => 1f,
		};

		return Math.Clamp(shadow * _lighting.ShadowStrength * faceScale, 0f, 0.72f);
	}
}
