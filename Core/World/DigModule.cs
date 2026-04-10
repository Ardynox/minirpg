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
			TrySpawnDigDrop(state, tx, ty, tz, terrain.StringId);
			TryCreateVerticalStairsAfterBreak(world, actor.X, actor.Y, actor.Z, tx, ty, tz);

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

	private static void TrySpawnDigDrop(GameState state, int tx, int ty, int tz, string terrainId)
	{
		if (state.World == null)
			return;

		var dropItemId = ResolveDigDropItemId(terrainId);
		if (string.IsNullOrWhiteSpace(dropItemId))
			return;

		try
		{
			var item = PresetDB.CloneItem(dropItemId);
			item.StackCount = 1;
			state.World.PlaceItem(tx, ty, tz, item);
		}
		catch (ArgumentException)
		{
			// Ignore unknown presets to keep dig path resilient.
		}
	}

	private static string ResolveDigDropItemId(string terrainId) => terrainId switch
	{
		Terrains.OreCoal => "mat_coal",
		Terrains.OreIron => "mat_iron",
		Terrains.OreCopper => "mat_copper",
		Terrains.OreGold => "mat_gold",
		Terrains.OreCrystal => "mat_crystal",
		_ => string.Empty,
	};

	private static void TryCreateVerticalStairsAfterBreak(WorldMap world, int actorX, int actorY, int actorZ, int tx, int ty, int tz)
	{
		if (tx != actorX || ty != actorY)
			return;
		if (tz == actorZ)
			return;

		if (tz > actorZ)
		{
			if (!world.HasFixture(actorX, actorY, actorZ, Entities.StairDown))
				world.SetFixture(actorX, actorY, actorZ, WorldMap.ResolveFixtureGlyph(Entities.StairDown), Entities.StairDown);
			if (!world.HasFixture(actorX, actorY, tz, Entities.StairUp))
				world.SetFixture(actorX, actorY, tz, WorldMap.ResolveFixtureGlyph(Entities.StairUp), Entities.StairUp);
		}
		else
		{
			if (!world.HasFixture(actorX, actorY, actorZ, Entities.StairUp))
				world.SetFixture(actorX, actorY, actorZ, WorldMap.ResolveFixtureGlyph(Entities.StairUp), Entities.StairUp);
			if (!world.HasFixture(actorX, actorY, tz, Entities.StairDown))
				world.SetFixture(actorX, actorY, tz, WorldMap.ResolveFixtureGlyph(Entities.StairDown), Entities.StairDown);
		}

		if (!world.HasFixture(actorX, actorY, actorZ, Entities.Ladder))
			world.SetFixture(actorX, actorY, actorZ, WorldMap.ResolveFixtureGlyph(Entities.Ladder), Entities.Ladder);
		if (!world.HasFixture(actorX, actorY, tz, Entities.Ladder))
			world.SetFixture(actorX, actorY, tz, WorldMap.ResolveFixtureGlyph(Entities.Ladder), Entities.Ladder);
	}

	private static GameEvent DigFailed(Actor actor, string reason) =>
		new("dig_failed") { InitiatorId = actor.Id, ItemName = reason };
}
