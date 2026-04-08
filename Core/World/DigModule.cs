using System;
using System.Collections.Generic;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.World;

public static class DigModule
{
	public static List<GameEvent> TryDig(GameState state, Actor actor, int tx, int ty, int tz,
		InteractionDef? skill = null)
	{
		var world = state.World;
		if (world == null)
			return [DigFailed(actor, LocalizationService.T("dig.failure.world_uninitialized"))];

		var terrain = world.GetTerrain(tx, ty, tz);
		if (!terrain.Solid)
			return [DigFailed(actor, LocalizationService.T("dig.failure.not_solid"))];

		var hardness = world.GetHardness(tx, ty, tz);
		if (hardness == 0)
			return [DigFailed(actor, LocalizationService.T("dig.failure.indestructible"))];

		if (skill != null && skill.TerrainMaterial.Length > 0 && terrain.Material != skill.TerrainMaterial)
		{
			var materialName = GameLocalizer.LocalizeMaterialName(terrain.Material, terrain.Material);
			return [DigFailed(actor, LocalizationService.T("dig.failure.material_mismatch", ("material", materialName)))];
		}

		var digPower = CalcDigPower(actor, skill);
		if (digPower <= 0)
			return [DigFailed(actor, LocalizationService.T("dig.failure.no_power"))];

		var newHardness = (byte)(hardness > digPower ? hardness - digPower : 0);
		world.SetHardness(tx, ty, tz, newHardness);

		var skillName = skill?.Name ?? LocalizationService.T("enum.effect_type.dig");
		if (newHardness == 0)
		{
			var breaksInto = terrain.BreaksInto;
			var rubbleId = TerrainRegistry.GetId(breaksInto);
			if (rubbleId == 0)
				rubbleId = TerrainRegistry.GetId(Terrains.Rubble);
			world.SetTerrainId(tx, ty, tz, rubbleId);

			return [new GameEvent("dig_success")
			{
				InitiatorId = actor.Id,
				TargetX = tx,
				TargetY = ty,
				ActionName = skillName,
			}];
		}

		return [new GameEvent("dig_progress")
		{
			InitiatorId = actor.Id,
			TargetX = tx,
			TargetY = ty,
			Damage = digPower,
			ActionName = skillName,
		}];
	}

	public static bool CanApply(InteractionDef skill, TerrainDef terrain, byte hardness)
	{
		if (!terrain.Solid || hardness == 0)
			return false;
		if (skill.TerrainMaterial.Length > 0 && terrain.Material != skill.TerrainMaterial)
			return false;
		return true;
	}

	private static int CalcDigPower(Actor actor, InteractionDef? skill)
	{
		var tags = actor.ComputeTags();
		var caps = actor.ComputeCapacities();
		var manipulation = caps.GetValueOrDefault(Caps.Manipulation, 0f);
		if (manipulation <= 0f)
			return 0;

		var tagKey = skill?.Id switch
		{
			"chop" => SkillTags.Chop,
			"mine" => SkillTags.Mine,
			_ => SkillTags.Dig,
		};
		var skillLevel = tags.GetValueOrDefault(tagKey, 0);
		var limbHardness = actor.GetLimbHardness(Caps.Manipulation);
		var power = (1 + skillLevel) * manipulation * 5f * (1f + limbHardness / 10f);
		return Math.Max(1, (int)power);
	}

	private static GameEvent DigFailed(Actor actor, string reason) =>
		new("dig_failed") { InitiatorId = actor.Id, ItemName = reason };
}
