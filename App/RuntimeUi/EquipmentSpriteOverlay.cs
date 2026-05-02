using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Data;

namespace MiniRPG;

/// <summary>
/// 把 <see cref="Actor.Inventory"/> 里 Equipped == true 的物品投影成 <see cref="EquipmentAppearanceData"/>，
/// 让 <see cref="MapSpriteRuntimeFactory"/> 能在地图 sprite 上叠加 weapon / cloak / helmet 三件大件挂件。
///
/// 投影规则（务实简化）：
///   - 第一件 <c>Category == "weapon"</c> 的装备 → WeaponAppearance（按物品 name 关键字识别 sword/axe/bow/staff）
///   - 第一件 <c>BodyPart contains "back" 或 "shoulder"</c>，或 name 含 "cloak/cape" 的装备 → CloakAppearance
///   - 第一件 <c>BodyPart contains "head"</c>，或 Layer == Overhead，或 name 含 "helmet/cap/hood/crown" 的 → HelmetAppearance
///
/// 颜色：暂统一用 <c>Item.MaterialId</c> 推断的金属色 / 布色，找不到就用 default tint。
/// 这是个 dumb projector，不做精细查找——目标是"穿铠甲后地图上看得出来"，不是装备外观系统的产品级实现。
/// </summary>
public static class EquipmentSpriteOverlay
{
	private static readonly Color DefaultMetal = new(0.62f, 0.62f, 0.66f);
	private static readonly Color DefaultLeather = new(0.40f, 0.27f, 0.16f);
	private static readonly Color DefaultCloth = new(0.46f, 0.32f, 0.22f);
	private static readonly Color DefaultGold = new(0.85f, 0.70f, 0.25f);

	public static EquipmentAppearanceData Project(Actor actor)
	{
		var weapon = WeaponAppearance.None;
		var cloak = CloakAppearance.None;
		var helmet = HelmetAppearance.None;

		foreach (var item in actor.Inventory)
		{
			if (!item.Equipped)
				continue;

			if (weapon.Kind == WeaponKind.None && IsWeapon(item))
			{
				weapon = WeaponAppearance.Of(InferWeaponKind(item), InferWeaponTint(item));
				continue;
			}

			if (cloak.Kind == CloakKind.None && IsCloak(item))
			{
				cloak = CloakAppearance.Of(InferCloakKind(item), InferClothTint(item));
				continue;
			}

			if (helmet.Kind == HelmetKind.None && IsHelmet(item))
			{
				helmet = HelmetAppearance.Of(InferHelmetKind(item), InferMetalTint(item));
				continue;
			}
		}

		return new EquipmentAppearanceData(weapon, cloak, helmet);
	}

	private static bool IsWeapon(Item item) =>
		string.Equals(item.Category, ItemCategories.Weapon, System.StringComparison.OrdinalIgnoreCase);

	private static bool IsCloak(Item item) =>
		ContainsAny(item.Name, "cloak", "cape", "mantle") ||
		ContainsAny(item.BodyPart, "back", "shoulder");

	private static bool IsHelmet(Item item) =>
		item.Layer == EquipLayer.Overhead ||
		ContainsAny(item.BodyPart, "head") ||
		ContainsAny(item.Name, "helmet", "helm", "cap", "hood", "crown");

	private static bool ContainsAny(string source, params string[] tokens)
	{
		if (string.IsNullOrEmpty(source))
			return false;
		foreach (var t in tokens)
			if (source.Contains(t, System.StringComparison.OrdinalIgnoreCase))
				return true;
		return false;
	}

	private static WeaponKind InferWeaponKind(Item item)
	{
		var n = item.Name ?? string.Empty;
		if (ContainsAny(n, "axe")) return WeaponKind.Axe;
		if (ContainsAny(n, "bow")) return WeaponKind.Bow;
		if (ContainsAny(n, "staff", "wand")) return WeaponKind.Staff;
		if (ContainsAny(n, "mace", "hammer", "club")) return WeaponKind.Mace;
		return WeaponKind.Sword;
	}

	private static CloakKind InferCloakKind(Item item)
	{
		var n = item.Name ?? string.Empty;
		if (ContainsAny(n, "hooded", "ranger")) return CloakKind.HoodedCloak;
		if (ContainsAny(n, "long", "royal", "noble", "ceremonial")) return CloakKind.LongCloak;
		return CloakKind.ShortCape;
	}

	private static HelmetKind InferHelmetKind(Item item)
	{
		var n = item.Name ?? string.Empty;
		if (ContainsAny(n, "crown")) return HelmetKind.Crown;
		if (ContainsAny(n, "cap", "hood")) return HelmetKind.Cap;
		return HelmetKind.FullHelm;
	}

	private static Color InferWeaponTint(Item item) =>
		ContainsAny(item.MaterialId, "wood", "bone") ? DefaultLeather :
		ContainsAny(item.MaterialId, "gold") ? DefaultGold :
		DefaultMetal;

	private static Color InferClothTint(Item item) =>
		ContainsAny(item.MaterialId, "leather") ? DefaultLeather :
		ContainsAny(item.MaterialId, "silk", "linen") ? DefaultCloth :
		ContainsAny(item.MaterialId, "fur", "wool") ? new Color(0.55f, 0.40f, 0.30f) :
		DefaultCloth;

	private static Color InferMetalTint(Item item) =>
		ContainsAny(item.MaterialId, "gold") ? DefaultGold :
		ContainsAny(item.MaterialId, "leather", "wood") ? DefaultLeather :
		DefaultMetal;
}
