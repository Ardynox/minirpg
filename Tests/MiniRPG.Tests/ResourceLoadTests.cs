using System;
using System.Collections.Generic;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using MiniRPG.Core.Trade;
using Xunit;

namespace MiniRPG.Tests;

public sealed class ResourceLoadTests
{
	public ResourceLoadTests()
	{
		LocalizationService.Initialize();
		LocalizationService.SetLocale("en", notify: false);
		GameConfig.Load();
		PresetDB.Load();
		HealthCatalog.Load();
	}

	[Fact]
	public void GameConfig_LoadsWeatherProfiles()
	{
		Assert.True(GameConfig.IsLoaded);
		Assert.True(GameConfig.Weather.RegionSizeChunks > 0);
		Assert.Contains("clear", GameConfig.Weather.Profiles.Keys, StringComparer.OrdinalIgnoreCase);
		Assert.Contains("thunderstorm", GameConfig.Weather.Profiles.Keys, StringComparer.OrdinalIgnoreCase);
	}

	[Fact]
	public void MaterialsAndPresetItems_LoadConductivityAndMaterialAssignments()
	{
		Assert.True(MaterialRegistry.Get("steel").Conductivity > MaterialRegistry.Get("cloth").Conductivity);
		Assert.True(MaterialRegistry.Get("silver").Conductivity >= 0.99f);

		var steelSword = PresetDB.CloneItem("sword_steel");
		var torch = PresetDB.CloneItem("torch");

		Assert.Equal("steel", steelSword.MaterialId);
		Assert.Equal("wood", torch.MaterialId);
	}

	[Fact]
	public void HealthCatalog_LoadsRequiredProfilesConditionsAndThoughts()
	{
		Assert.Contains("flesh_humanoid", HealthCatalog.Profiles.Keys);
		Assert.Contains("flesh_beast", HealthCatalog.Profiles.Keys);
		Assert.Contains("woody_beast", HealthCatalog.Profiles.Keys);
		Assert.Contains("undead_numb", HealthCatalog.Profiles.Keys);
		Assert.Contains("gelatinous", HealthCatalog.Profiles.Keys);

		Assert.Contains(HealthConditionIds.CutWound, HealthCatalog.Conditions.Keys);
		Assert.Contains(HealthConditionIds.BluntTrauma, HealthCatalog.Conditions.Keys);
		Assert.Contains(HealthConditionIds.ToxicWound, HealthCatalog.Conditions.Keys);
		Assert.Contains(HealthConditionIds.Infection, HealthCatalog.Conditions.Keys);
		Assert.Contains(HealthConditionIds.MissingLimb, HealthCatalog.Conditions.Keys);

		Assert.Contains("minor_pain", HealthCatalog.Thoughts.Keys);
		Assert.Contains("major_pain", HealthCatalog.Thoughts.Keys);
		Assert.Contains("extreme_pain", HealthCatalog.Thoughts.Keys);
		Assert.Contains("tended_well", HealthCatalog.Thoughts.Keys);
		Assert.Contains("tended_poorly", HealthCatalog.Thoughts.Keys);
	}

	[Fact]
	public void PresetDB_LoadsFacilityRecipesAndRoomRoles()
	{
		Assert.Contains(FacilityIds.Stove, PresetDB.Facilities.Keys);
		Assert.Contains(FacilityIds.MarketStall, PresetDB.Facilities.Keys);
		Assert.Contains("cook_simple_meal", PresetDB.Recipes.Keys);
		Assert.Contains(RoomRoleIds.Kitchen, PresetDB.RoomRoles.Keys);

		var stove = PresetDB.Facilities[FacilityIds.Stove];
		Assert.Contains(RecipeTagIds.Cooking, stove.AllowedRecipeTags);
		Assert.Contains(FacilityTags.Heat, stove.Tags);

		var recipe = PresetDB.Recipes["cook_simple_meal"];
		Assert.Equal(OutputTargetStrategy.OwnerDomainStockpile, recipe.OutputTarget);
		Assert.Contains("cook", recipe.RequiredProfessionIds);

		var kitchen = PresetDB.RoomRoles[RoomRoleIds.Kitchen];
		Assert.Contains(FacilityIds.Stove, kitchen.FacilityIds);
	}

	[Fact]
	public void PresetDB_LoadsMixedTechItemRegistries()
	{
		Assert.NotNull(ItemCategoryDef.Get(ItemCategories.Ammo));

		var subCategory = ItemSubcategoryRegistry.Get("firearm_rifle");
		Assert.NotNull(subCategory);
		Assert.Equal(ItemCategories.Weapon, subCategory!.Category);

		var ammoProfile = AmmoProfileRegistry.Get("rifle_round");
		Assert.NotNull(ammoProfile);
		Assert.Equal("ammo_rifle", ammoProfile!.ItemId);

		var corpseProfile = CorpseProfileRegistry.Get("corpse_humanoid");
		Assert.NotNull(corpseProfile);
		Assert.Equal("corpse_human", corpseProfile!.CorpseItemId);

		var installArm = SurgeryOperationRegistry.Get("install_arm");
		Assert.NotNull(installArm);
		Assert.Equal(SurgeryOperationModes.LiveInstall, installArm!.Mode);
	}

	[Fact]
	public void InventoryAndTradeMessages_HideUnknownItemNames_WhenStateProvided()
	{
		var state = new GameState();
		var actor = CreateActor("player", Factions.Player);
		var trader = CreateActor("merchant", Factions.Friendly);
		var hiddenItem = new Item
		{
			Id = "mystery_blade",
			Name = "Ancient Sword",
			Category = ItemCategories.Weapon,
			Price = 20,
		};

		actor.Inventory.Add(hiddenItem);
		trader.Gold = 100;

		var dropResult = InventoryModule.Drop(actor, 0, state);
		Assert.DoesNotContain(hiddenItem.Name, dropResult.Message, StringComparison.Ordinal);
		Assert.Contains("Unknown", dropResult.Message, StringComparison.Ordinal);

		actor.Inventory.Add(hiddenItem);
		var sellResult = TradeModule.Sell(actor, trader, 0, state);
		Assert.True(sellResult.Ok);
		Assert.DoesNotContain(hiddenItem.Name, sellResult.Message, StringComparison.Ordinal);
		Assert.Contains("Unknown", sellResult.Message, StringComparison.Ordinal);
	}

	private static Actor CreateActor(string id, string faction) => new()
	{
		Id = id,
		DisplayName = id,
		Faction = faction,
		Limbs =
		[
			new Limb
			{
				Id = $"{id}_core",
				Name = "Core",
				MaxDurability = 10,
				Durability = 10,
				Material = "flesh",
				BodyPart = BodyParts.Torso,
				Capacities = new Dictionary<string, float>
				{
					[Caps.Consciousness] = 1f,
				},
			},
		],
	};
}
