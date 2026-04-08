using System;
using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using Xunit;

namespace MiniRPG.Tests;

public sealed class FirearmModuleTests
{
	public FirearmModuleTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	private static Actor MakeActorWithWeapon(string weaponId = "pistol", bool equipped = true)
	{
		var actor = PresetDB.SpawnActor("player", "gunner");
		// Find or create a weapon
		var weapon = actor.Inventory.FirstOrDefault(i => i.Category == ItemCategories.Weapon);
		if (weapon == null)
		{
			weapon = new Item
			{
				Id = weaponId,
				Name = "Test Pistol",
				Category = ItemCategories.Weapon,
				Equipped = equipped,
				AmmoType = "bullet",
				MagazineSize = 6,
				LoadedAmmo = 6,
				GrantedSkills = ["gun_shoot"],
			};
			actor.Inventory.Add(weapon);
		}
		else
		{
			weapon.Equipped = equipped;
		}
		return actor;
	}

	// ── FindEquippedWeaponForSkill ───────────────────────

	[Fact]
	public void FindEquippedWeaponForSkill_NoWeapon_ReturnsNull()
	{
		var actor = PresetDB.SpawnActor("player", "unarmed");
		actor.Inventory.RemoveAll(i => i.Category == ItemCategories.Weapon);
		Assert.Null(FirearmModule.FindEquippedWeaponForSkill(actor, "gun_shoot"));
	}

	[Fact]
	public void FindEquippedWeaponForSkill_MatchingSkill_ReturnsWeapon()
	{
		var actor = MakeActorWithWeapon();
		var weapon = FirearmModule.FindEquippedWeaponForSkill(actor, "gun_shoot");
		Assert.NotNull(weapon);
	}

	[Fact]
	public void FindEquippedWeaponForSkill_NoMatchingSkill_ReturnsFallback()
	{
		var actor = MakeActorWithWeapon();
		var weapon = FirearmModule.FindEquippedWeaponForSkill(actor, "nonexistent_skill");
		// Falls back to first equipped weapon
		Assert.NotNull(weapon);
	}

	[Fact]
	public void FindEquippedWeaponForSkill_UnequippedWeapon_ReturnsNull()
	{
		var actor = MakeActorWithWeapon(equipped: false);
		actor.Inventory.RemoveAll(i => i.Category == ItemCategories.Weapon && i.Equipped);
		var weapon = FirearmModule.FindEquippedWeaponForSkill(actor, "gun_shoot");
		Assert.Null(weapon);
	}

	// ── TrySpendAmmoForShot ──────────────────────────────

	[Fact]
	public void TrySpendAmmoForShot_NoWeapon_ReturnsConsumed()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState();
		player.Inventory.RemoveAll(i => i.Category == ItemCategories.Weapon);
		var skill = new InteractionDef { Id = "gun_shoot", Name = "Shoot" };
		var result = FirearmModule.TrySpendAmmoForShot(state, player, skill);
		Assert.True(result.Consumed);
	}

	[Fact]
	public void TrySpendAmmoForShot_WeaponNoAmmoRequired_ReturnsConsumed()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState();
		var weapon = new Item
		{
			Id = "sword",
			Category = ItemCategories.Weapon,
			Equipped = true,
		};
		player.Inventory.Add(weapon);
		var skill = new InteractionDef { Id = "slash", Name = "Slash" };
		var result = FirearmModule.TrySpendAmmoForShot(state, player, skill);
		Assert.True(result.Consumed);
	}

	[Fact]
	public void TrySpendAmmoForShot_EmptyMagazine_FailsWithEvent()
	{
		var (state, _, _) = SkillCastingTestHelper.CreateCombatState();
		var actor = MakeActorWithWeapon();
		var weapon = actor.Inventory.First(i => i.Category == ItemCategories.Weapon);
		weapon.LoadedAmmo = 0;
		ActorModule.Add(state, actor);

		var skill = new InteractionDef { Id = "gun_shoot", Name = "Shoot" };
		var result = FirearmModule.TrySpendAmmoForShot(state, actor, skill);
		Assert.False(result.Consumed);
		Assert.Contains(result.Events, e => e.Type == "empty_magazine");
	}

	[Fact]
	public void TrySpendAmmoForShot_HasAmmo_ConsumesAndReturnsEvent()
	{
		var (state, _, _) = SkillCastingTestHelper.CreateCombatState();
		var actor = MakeActorWithWeapon();
		var weapon = actor.Inventory.First(i => i.Category == ItemCategories.Weapon);
		weapon.LoadedAmmo = 3;
		ActorModule.Add(state, actor);

		var skill = new InteractionDef { Id = "gun_shoot", Name = "Shoot" };
		var result = FirearmModule.TrySpendAmmoForShot(state, actor, skill);
		Assert.True(result.Consumed);
		Assert.Contains(result.Events, e => e.Type == "ammo_spent");
		Assert.True(weapon.LoadedAmmo < 3);
	}
}
