using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using MiniRPG.Core.Map;
using MiniRPG.Core.Weather;
using Xunit;

namespace MiniRPG.Tests;

public sealed class ActionModuleTests
{
	[Fact]
	public void GetInteractTargets_UsesCurrentCellAndCardinalNeighborsOnly()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState(enemyX: 10, enemyY: 10);
		var sameCell = CreateActor("same_cell", Factions.Friendly, player.X, player.Y, player.Z);
		var north = CreateActor("north", Factions.Friendly, player.X, player.Y - 1, player.Z);
		var east = CreateActor("east", Factions.Friendly, player.X + 1, player.Y, player.Z);
		var diagonal = CreateActor("diagonal", Factions.Friendly, player.X + 1, player.Y + 1, player.Z);

		ActorModule.Add(state, sameCell);
		ActorModule.Add(state, north);
		ActorModule.Add(state, east);
		ActorModule.Add(state, diagonal);

		var targets = ActionModule.GetInteractTargets(state, player);

		Assert.Contains(sameCell, targets);
		Assert.Contains(north, targets);
		Assert.Contains(east, targets);
		Assert.DoesNotContain(diagonal, targets);
		Assert.DoesNotContain(player, targets);
	}

	[Fact]
	public void TryMove_IgnoresDifferentZTerrain_WhenDestinationSameZIsWalkable()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState(enemyX: 10, enemyY: 10);
		player.Z = 1;
		state.PlayerZ = 1;
		state.World!.SetTerrain(player.X + 1, player.Y, 0, Terrains.WallStone);
		state.World.SetTerrain(player.X + 1, player.Y, 1, Terrains.Floor);

		var events = ActionModule.TryMove(state, player, 1, 0);

		Assert.Contains(events, evt => evt.Type == "actor_moved");
		Assert.Equal(2, player.X);
		Assert.Equal(1, player.Y);
		Assert.Equal(1, player.Z);
	}

	[Fact]
	public void TryMove_EmitsFullSourceAndTargetCoordinates()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState(enemyX: 10, enemyY: 10);

		var moveEvent = Assert.Single(ActionModule.TryMove(state, player, 1, 0), evt => evt.Type == "actor_moved");

		Assert.Equal(1, moveEvent.SourceX);
		Assert.Equal(1, moveEvent.SourceY);
		Assert.Equal(0, moveEvent.SourceZ);
		Assert.Equal(2, moveEvent.TargetX);
		Assert.Equal(1, moveEvent.TargetY);
		Assert.Equal(0, moveEvent.TargetZ);
	}

	[Fact]
	public void TryCastSkill_RangedAttackSucceeds_WhenTargetInRangeAndVisible()
	{
		var (state, player, enemy) = SkillCastingTestHelper.CreateCombatState(enemyX: 4, enemyY: 1);

		var result = ActionModule.TryCastSkill(
			state,
			player,
			"bow_shoot",
			SkillTargetType.Actor,
			targetActor: enemy);

		Assert.True(result.Consumed);
		Assert.Contains(result.Events, evt => evt.Type == "combat_attack");
		Assert.DoesNotContain(result.Events, evt => evt.Type == "skill_cast_failed");
	}

	[Fact]
	public void TryCastSkill_Fails_WhenLineOfSightIsBlocked()
	{
		var (state, player, enemy) = SkillCastingTestHelper.CreateCombatState(enemyX: 4, enemyY: 1);
		state.World!.SetTerrain(3, 1, 0, Terrains.WallStone);

		var result = ActionModule.TryCastSkill(
			state,
			player,
			"bow_shoot",
			SkillTargetType.Actor,
			targetActor: enemy);

		Assert.False(result.Consumed);
		var failure = Assert.Single(result.Events);
		Assert.Equal("skill_cast_failed", failure.Type);
		Assert.Equal("no_line_of_sight", failure.FailureReason);
	}

	[Fact]
	public void TryCastSkill_Fails_WhenTargetIsOutOfRange()
	{
		var (state, player, enemy) = SkillCastingTestHelper.CreateCombatState(enemyX: 7, enemyY: 1);

		var result = ActionModule.TryCastSkill(
			state,
			player,
			"bow_shoot",
			SkillTargetType.Actor,
			targetActor: enemy);

		Assert.False(result.Consumed);
		var failure = Assert.Single(result.Events);
		Assert.Equal("skill_cast_failed", failure.Type);
		Assert.Equal("out_of_range", failure.FailureReason);
	}

	[Fact]
	public void TryCastSkill_DigFails_OnWrongMaterial()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState();
		state.World!.SetTerrain(2, 1, 0, Terrains.WallStone);
		Assert.True(state.World.GetTerrain(2, 1, 0).Solid);
		Assert.True(state.World.GetHardness(2, 1, 0) > 0);

		var result = ActionModule.TryCastSkill(
			state,
			player,
			"chop",
			SkillTargetType.Cell,
			targetActor: null,
			targetLimb: null,
			targetX: 2,
			targetY: 1,
			targetZ: 0);

		Assert.False(result.Consumed);
		var failure = Assert.Single(result.Events);
		Assert.Equal("skill_cast_failed", failure.Type);
		Assert.Equal("invalid_terrain_material", failure.FailureReason);
	}

	[Fact]
	public void TryCastSkill_BlockSelfSucceeds()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState();

		var result = ActionModule.TryCastSkill(
			state,
			player,
			"sword_parry",
			SkillTargetType.Self,
			targetActor: null,
			targetLimb: null);

		Assert.True(result.Consumed);
		Assert.Contains(result.Events, evt => evt.Type == "combat_block");
	}

	[Fact]
	public void TryCastSkill_RangedAttackFails_WhenWeatherReducesRange()
	{
		var (state, player, enemy) = SkillCastingTestHelper.CreateCombatState(enemyX: 4, enemyY: 1);
		state.Weather = new WeatherState
		{
			FrontPhase = 0f,
			DebugOverride = new WeatherDebugOverride
			{
				Type = WeatherType.Sandstorm,
				Intensity = WeatherIntensity.Heavy,
			},
		};

		var result = ActionModule.TryCastSkill(
			state,
			player,
			"bow_shoot",
			SkillTargetType.Actor,
			targetActor: enemy);

		Assert.False(result.Consumed);
		var failure = Assert.Single(result.Events);
		Assert.Equal("skill_cast_failed", failure.Type);
		Assert.Equal("out_of_range", failure.FailureReason);
	}

	[Fact]
	public void TryCastSkill_LightFireCreatesCampfire_AndConsumesWood()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState(enemyX: 6, enemyY: 6);
		player.Inventory.Add(PresetDB.CloneItem("mat_wood"));

		Assert.Contains(SkillQuery.GetCellSkills(player), skill => skill.Id == "light_fire");

		var result = ActionModule.TryCastSkill(
			state,
			player,
			"light_fire",
			SkillTargetType.Cell,
			targetActor: null,
			targetLimb: null,
			targetX: 1,
			targetY: 2,
			targetZ: 0);

		Assert.True(result.Consumed);
		Assert.DoesNotContain(player.Inventory, item => item.Id == "mat_wood");
		Assert.True(state.World!.HasFixture(1, 2, 0, Entities.Campfire));
		Assert.Contains(result.Events, evt => evt.Type == "campfire_lit");
		Assert.True(player.IsSkillOnCooldown("light_fire"));
	}

	[Fact]
	public void TryCastSkill_LightFireFails_WhenTargetOrResourcesAreInvalid()
	{
		var (noWoodState, noWoodPlayer, _) = SkillCastingTestHelper.CreateCombatState(enemyX: 6, enemyY: 6);
		var noWood = ActionModule.TryCastSkill(
			noWoodState,
			noWoodPlayer,
			"light_fire",
			SkillTargetType.Cell,
			targetActor: null,
			targetLimb: null,
			targetX: 1,
			targetY: 2,
			targetZ: 0);
		Assert.False(noWood.Consumed);

		var (waterState, waterPlayer, _) = SkillCastingTestHelper.CreateCombatState(enemyX: 6, enemyY: 6);
		waterPlayer.Inventory.Add(PresetDB.CloneItem("mat_wood"));
		waterState.World!.SetTerrain(1, 2, 0, Terrains.Water);
		var onWater = ActionModule.TryCastSkill(
			waterState,
			waterPlayer,
			"light_fire",
			SkillTargetType.Cell,
			targetActor: null,
			targetLimb: null,
			targetX: 1,
			targetY: 2,
			targetZ: 0);
		Assert.False(onWater.Consumed);

		var (occupiedState, occupiedPlayer, occupiedEnemy) = SkillCastingTestHelper.CreateCombatState(enemyX: 1, enemyY: 2);
		occupiedPlayer.Inventory.Add(PresetDB.CloneItem("mat_wood"));
		var occupied = ActionModule.TryCastSkill(
			occupiedState,
			occupiedPlayer,
			"light_fire",
			SkillTargetType.Cell,
			targetActor: null,
			targetLimb: null,
			targetX: occupiedEnemy.X,
			targetY: occupiedEnemy.Y,
			targetZ: occupiedEnemy.Z);
		Assert.False(occupied.Consumed);

		var (fixtureState, fixturePlayer, _) = SkillCastingTestHelper.CreateCombatState(enemyX: 6, enemyY: 6);
		fixturePlayer.Inventory.Add(PresetDB.CloneItem("mat_wood"));
		fixtureState.World!.SetFixture(1, 2, 0, "H", Entities.House);
		var withFixture = ActionModule.TryCastSkill(
			fixtureState,
			fixturePlayer,
			"light_fire",
			SkillTargetType.Cell,
			targetActor: null,
			targetLimb: null,
			targetX: 1,
			targetY: 2,
			targetZ: 0);
		Assert.False(withFixture.Consumed);
	}

	[Fact]
	public void TryCastSkill_ExtinguishFire_RemovesAdjacentFireAndSelfFire()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState(enemyX: 6, enemyY: 6);
		FireSystem.TryIgniteCell(state, 1, 2, 0, intensity: 5, fuel: 10);

		var adjacent = ActionModule.TryCastSkill(
			state,
			player,
			"extinguish_fire",
			SkillTargetType.Cell,
			targetActor: null,
			targetLimb: null,
			targetX: 1,
			targetY: 2,
			targetZ: 0);

		Assert.True(adjacent.Consumed);
		Assert.Equal(0, FireSystem.GetFireIntensityAt(state, 1, 2, 0));
		Assert.Contains(adjacent.Events, evt => evt.Type == "fire_extinguished" && evt.TargetX == 1 && evt.TargetY == 2);

		FireSystem.TryIgniteActor(state, player, intensity: 1);
		var self = ActionModule.TryCastSkill(
			state,
			player,
			"extinguish_fire",
			SkillTargetType.Cell,
			targetActor: null,
			targetLimb: null,
			targetX: player.X,
			targetY: player.Y,
			targetZ: player.Z);

		Assert.True(self.Consumed);
		Assert.DoesNotContain(player.HealthConditions, condition => condition.Id == HealthConditionIds.OnFire);
		Assert.Contains(self.Events, evt => evt.Type == "fire_extinguished" && evt.TargetId == player.Id);
	}

	[Fact]
	public void TryCastSkill_ReloadConsumesAmmoAndRefillsMagazine()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState(enemyX: 6, enemyY: 6);
		player.Inventory.Clear();

		var rifle = PresetDB.CloneItem("bolt_rifle");
		rifle.Equipped = true;
		rifle.LoadedAmmo = 0;
		var ammo = PresetDB.CloneItem("ammo_rifle");
		ammo.StackCount = 9;

		player.Inventory.Add(rifle);
		player.Inventory.Add(ammo);

		var result = ActionModule.TryCastSkill(
			state,
			player,
			"reload_firearm",
			SkillTargetType.Self,
			targetActor: null,
			targetLimb: null);

		Assert.True(result.Consumed);
		Assert.Equal(5, rifle.LoadedAmmo);
		Assert.Equal(4, ammo.StackCount);
		Assert.Contains(result.Events, evt => evt.Type == "reload_complete" && evt.Damage == 5);
	}

	[Fact]
	public void TryCastSkill_FirearmShotFails_WhenMagazineIsEmpty()
	{
		var (state, player, enemy) = SkillCastingTestHelper.CreateCombatState(enemyX: 4, enemyY: 1);
		player.Inventory.Clear();

		var revolver = PresetDB.CloneItem("sidearm_revolver");
		revolver.Equipped = true;
		revolver.LoadedAmmo = 0;
		player.Inventory.Add(revolver);

		var result = ActionModule.TryCastSkill(
			state,
			player,
			"revolver_shot",
			SkillTargetType.Actor,
			targetActor: enemy);

		Assert.False(result.Consumed);
		var failure = Assert.Single(result.Events);
		Assert.Equal("empty_magazine", failure.Type);
		Assert.DoesNotContain(result.Events, evt => evt.Type == "combat_attack");
	}

	[Fact]
	public void TryCastSkill_HarvestCorpseConsumesMedicalSupplyButKeepsTool()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState(enemyX: 6, enemyY: 6);
		player.Inventory.Clear();

		var medicine = PresetDB.CloneItem("industrial_medicine");
		medicine.StackCount = 4;
		var tool = PresetDB.CloneItem("surgery_kit");
		tool.Equipped = true;
		player.Inventory.Add(medicine);
		player.Inventory.Add(tool);

		var corpse = PresetDB.CloneItem("corpse_human");
		corpse.Corpse = new ItemCorpseMetadata
		{
			CorpseProfileId = "corpse_humanoid",
			SourceActorTemplateId = "player",
			SourceRaceId = "human",
			SourceActorName = "Raider",
			RemainingLimbIds = ["human_heart"],
		};
		corpse.Contents = [];
		state.World!.PlaceItem(player.X, player.Y + 1, player.Z, corpse);

		var result = ActionModule.TryCastSkill(
			state,
			player,
			"harvest_corpse",
			SkillTargetType.Item,
			targetActor: null,
			targetLimb: null,
			targetX: player.X,
			targetY: player.Y + 1,
			targetZ: player.Z,
			targetItem: corpse,
			targetLimbId: "human_heart");

		Assert.True(result.Consumed);
		Assert.Equal(2, medicine.StackCount);
		Assert.Contains(player.Inventory, item => item.InstanceId == tool.InstanceId);
		Assert.Contains(player.Inventory, item => item.Id == "organ_heart");
		Assert.Empty(corpse.Corpse.RemainingLimbIds);

		var harvestEvent = Assert.Single(result.Events, evt => evt.Type == "corpse_harvested");
		Assert.Equal("organ_heart", harvestEvent.ItemTypeId);
		Assert.Equal(corpse.Name, harvestEvent.TargetActorName);
	}

	private static Actor CreateActor(string id, string faction, int x, int y, int z) =>
		new()
		{
			Id = id,
			DisplayName = id,
			Faction = faction,
			X = x,
			Y = y,
			Z = z,
			Limbs =
			[
				new Limb
				{
					Id = $"{id}_core",
					Name = "Core",
					MaxDurability = 10,
					Durability = 10,
					Material = "flesh",
					BodyPart = "torso",
					Capacities = new Dictionary<string, float>
					{
						[Caps.BloodCirculation] = 1f,
						[Caps.Moving] = 1f,
						[Caps.Consciousness] = 1f,
						[Caps.Metabolism] = 1f,
					},
				},
			],
		};
}
