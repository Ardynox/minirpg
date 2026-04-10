using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Combat;

public static class FirearmModule
{
	public static Item? FindEquippedWeaponForSkill(Actor actor, string? skillId)
	{
		var weapons = actor.Inventory
			.Where(item => item.Equipped && string.Equals(item.Category, ItemCategories.Weapon, StringComparison.Ordinal))
			.ToList();
		if (weapons.Count == 0)
			return null;

		if (!string.IsNullOrWhiteSpace(skillId))
		{
			var matched = weapons.FirstOrDefault(item => item.GrantedSkills.Any(id => string.Equals(id, skillId, StringComparison.Ordinal)));
			if (matched != null)
				return matched;
		}

		return weapons[0];
	}

	public static ActionExecutionResult TrySpendAmmoForShot(GameState state, Actor actor, InteractionDef skill)
	{
		var result = new ActionExecutionResult { Consumed = true };
		var weapon = FindEquippedWeaponForSkill(actor, skill.Id);
		if (weapon == null || !weapon.RequiresAmmo)
			return result;

		var ammoPerShot = AmmoProfileRegistry.Get(weapon.AmmoType)?.AmmoPerShot ?? 1;
		if (weapon.SafeLoadedAmmo < ammoPerShot)
		{
			result.Consumed = false;
			result.Events.Add(BuildAmmoEvent("empty_magazine", actor, weapon, skill, amount: 0));
			return result;
		}

		weapon.LoadedAmmo = Math.Max(0, weapon.LoadedAmmo - ammoPerShot);
		result.Events.Add(BuildAmmoEvent("ammo_spent", actor, weapon, skill, ammoPerShot));
		return result;
	}

	public static ActionExecutionResult TryReload(GameState state, Actor actor, InteractionDef skill)
	{
		var result = new ActionExecutionResult();
		var weapon = SelectReloadWeapon(actor, skill.Id);
		if (weapon == null || !weapon.RequiresAmmo)
		{
			result.Events.Add(BuildAmmoEvent("reload_failed", actor, weapon, skill, amount: 0));
			return result;
		}

		var needed = weapon.MagazineSize - weapon.SafeLoadedAmmo;
		if (needed <= 0)
		{
			result.Events.Add(BuildAmmoEvent("reload_failed", actor, weapon, skill, amount: 0));
			return result;
		}

		var available = InventoryModule.CountMatching(actor, item => IsCompatibleAmmo(item, weapon));
		if (available <= 0)
		{
			result.Events.Add(BuildAmmoEvent("reload_failed", actor, weapon, skill, amount: 0));
			return result;
		}

		var loaded = InventoryModule.ConsumeMatching(actor, item => IsCompatibleAmmo(item, weapon), Math.Min(needed, available));
		if (loaded <= 0)
		{
			result.Events.Add(BuildAmmoEvent("reload_failed", actor, weapon, skill, amount: 0));
			return result;
		}

		weapon.LoadedAmmo = Math.Min(weapon.MagazineSize, weapon.LoadedAmmo + loaded);
		result.Consumed = true;
		result.Events.Add(BuildAmmoEvent("reload_complete", actor, weapon, skill, loaded));
		return result;
	}

	private static Item? SelectReloadWeapon(Actor actor, string? skillId)
	{
		var primary = FindEquippedWeaponForSkill(actor, skillId);
		if (primary is { RequiresAmmo: true } primaryFirearm && primaryFirearm.SafeLoadedAmmo < primaryFirearm.MagazineSize)
			return primaryFirearm;

		return actor.Inventory.FirstOrDefault(item =>
			item.Equipped
			&& string.Equals(item.Category, ItemCategories.Weapon, StringComparison.Ordinal)
			&& item.RequiresAmmo
			&& item.SafeLoadedAmmo < item.MagazineSize);
	}

	private static bool IsCompatibleAmmo(Item ammoItem, Item weapon)
	{
		if (!string.Equals(ammoItem.Category, ItemCategories.Ammo, StringComparison.Ordinal))
			return false;

		if (!ammoItem.IsStackable)
			return false;

		if (!string.IsNullOrWhiteSpace(ammoItem.AmmoType))
			return string.Equals(ammoItem.AmmoType, weapon.AmmoType, StringComparison.Ordinal);

		var profile = AmmoProfileRegistry.Get(weapon.AmmoType);
		return profile != null && string.Equals(profile.ItemId, ammoItem.Id, StringComparison.Ordinal);
	}

	private static GameEvent BuildAmmoEvent(string type, Actor actor, Item? weapon, InteractionDef skill, int amount)
	{
		var evt = new GameEvent(type)
		{
			ActionName = skill.Name,
			SkillId = skill.Id,
			ItemName = weapon?.Name,
			ItemTypeId = weapon?.Id,
			ItemCategory = weapon?.Category,
			Damage = amount,
			TargetX = actor.X,
			TargetY = actor.Y,
			TargetZ = actor.Z,
		};
		IdentificationModule.PopulateInitiatorIdentity(evt, state: null, actor);
		if (weapon != null)
			IdentificationModule.PopulateItemIdentity(evt, state: null, weapon);
		return evt;
	}
}
