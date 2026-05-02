using Godot;
using MiniRPG.Core.Data;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// MapSpriteRuntimeFactory + EquipmentSpriteOverlay 的纯数据 / 投影逻辑测试。
///
/// 注意：MapSpriteRuntimeFactory.GetOrBuild 会调 Godot.Image / ImageTexture.CreateFromImage，
/// 在 xunit 进程里跑这些 native API 会触发 AccessViolationException（fatal，try/catch 抓不住），
/// 因此**不在这里测 GetOrBuild 本身**。相关行为（缓存命中 / 不同输入产生不同 texture）需要在
/// Godot editor 内开局后手测验收，或留到将来跑 Godot headless 时再加 integration test。
/// </summary>
public sealed class MapSpriteRuntimeFactoryTests
{
	[Fact]
	public void EquipmentAppearanceData_Empty_HasAllNoneKinds()
	{
		var empty = EquipmentAppearanceData.Empty;
		Assert.Equal(WeaponKind.None, empty.Weapon.Kind);
		Assert.Equal(CloakKind.None, empty.Cloak.Kind);
		Assert.Equal(HelmetKind.None, empty.Helmet.Kind);
	}

	[Fact]
	public void EquipmentAppearanceData_ComputeCacheKey_DifferentForDifferentEquipment()
	{
		var noEquip = EquipmentAppearanceData.Empty;
		var withSword = noEquip with
		{
			Weapon = WeaponAppearance.Of(WeaponKind.Sword, Colors.White),
		};
		var withCloak = noEquip with
		{
			Cloak = CloakAppearance.Of(CloakKind.LongCloak, Colors.Brown),
		};

		Assert.NotEqual(noEquip.ComputeCacheKey(), withSword.ComputeCacheKey());
		Assert.NotEqual(noEquip.ComputeCacheKey(), withCloak.ComputeCacheKey());
		Assert.NotEqual(withSword.ComputeCacheKey(), withCloak.ComputeCacheKey());
	}

	[Fact]
	public void EquipmentAppearanceData_ComputeCacheKey_SameForSameEquipment()
	{
		var a = new EquipmentAppearanceData(
			WeaponAppearance.Of(WeaponKind.Sword, new Color(0.5f, 0.5f, 0.6f)),
			CloakAppearance.None,
			HelmetAppearance.None);
		var b = new EquipmentAppearanceData(
			WeaponAppearance.Of(WeaponKind.Sword, new Color(0.5f, 0.5f, 0.6f)),
			CloakAppearance.None,
			HelmetAppearance.None);

		Assert.Equal(a.ComputeCacheKey(), b.ComputeCacheKey());
	}

	[Fact]
	public void EquipmentSpriteOverlay_PicksUpEquippedSwordFromInventory()
	{
		var actor = new Actor();
		actor.Inventory.Add(new Item
		{
			Id = "iron_sword",
			Name = "Iron Sword",
			Category = ItemCategories.Weapon,
			Equipped = true,
		});

		var equipment = EquipmentSpriteOverlay.Project(actor);

		Assert.Equal(WeaponKind.Sword, equipment.Weapon.Kind);
		Assert.Equal(CloakKind.None, equipment.Cloak.Kind);
		Assert.Equal(HelmetKind.None, equipment.Helmet.Kind);
	}

	[Fact]
	public void EquipmentSpriteOverlay_RecognizesAxeFromName()
	{
		var actor = new Actor();
		actor.Inventory.Add(new Item
		{
			Id = "battle_axe",
			Name = "Battle Axe",
			Category = ItemCategories.Weapon,
			Equipped = true,
		});

		var equipment = EquipmentSpriteOverlay.Project(actor);

		Assert.Equal(WeaponKind.Axe, equipment.Weapon.Kind);
	}

	[Fact]
	public void EquipmentSpriteOverlay_RecognizesCloakAndHelmetByName()
	{
		var actor = new Actor();
		actor.Inventory.Add(new Item
		{
			Id = "linen_hood",
			Name = "Linen Hood",
			Category = ItemCategories.Clothing,
			Equipped = true,
		});
		actor.Inventory.Add(new Item
		{
			Id = "wool_cloak",
			Name = "Wool Cloak",
			Category = ItemCategories.Clothing,
			Equipped = true,
		});

		var equipment = EquipmentSpriteOverlay.Project(actor);

		Assert.Equal(HelmetKind.Cap, equipment.Helmet.Kind);
		Assert.NotEqual(CloakKind.None, equipment.Cloak.Kind);
	}

	[Fact]
	public void EquipmentSpriteOverlay_IgnoresUnequippedItems()
	{
		var actor = new Actor();
		actor.Inventory.Add(new Item
		{
			Id = "iron_sword",
			Name = "Iron Sword",
			Category = ItemCategories.Weapon,
			Equipped = false,
		});

		var equipment = EquipmentSpriteOverlay.Project(actor);

		Assert.Equal(WeaponKind.None, equipment.Weapon.Kind);
	}

	[Fact]
	public void EquipmentSpriteOverlay_OnlyTakesFirstOfEachCategory()
	{
		var actor = new Actor();
		actor.Inventory.Add(new Item
		{
			Id = "iron_sword",
			Name = "Iron Sword",
			Category = ItemCategories.Weapon,
			Equipped = true,
		});
		actor.Inventory.Add(new Item
		{
			Id = "wood_axe",
			Name = "Wood Axe",
			Category = ItemCategories.Weapon,
			Equipped = true,
		});

		var equipment = EquipmentSpriteOverlay.Project(actor);

		// 第一件 weapon = Sword, 不会被后面的 Axe 覆盖
		Assert.Equal(WeaponKind.Sword, equipment.Weapon.Kind);
	}
}
