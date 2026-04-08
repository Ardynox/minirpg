using System;
using System.Collections.Generic;
using System.Reflection;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Event;
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
		GameLocalizer.ApplyPresetTranslations();
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
		Assert.Equal("Rifle", subCategory.Name);

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
	public void PresetDB_LoadsStorytellerDefsFromJsonStringEnums()
	{
		Storyteller.ClearDefs();
		typeof(PresetDB).GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, false);
		PresetDB.Load();

		Assert.Equal(IncidentCategory.Threat, Storyteller.GetDef("raid_goblin")?.Category);
		Assert.Equal(IncidentCategory.Neutral, Storyteller.GetDef("trader_visit")?.Category);
		Assert.Equal(IncidentCategory.Positive, Storyteller.GetDef("wanderer_join")?.Category);
	}

	[Fact]
	public void StorytellerLoader_DeserializeDefs_ParsesStringEnumCategory()
	{
		const string json = """
			[
			  {
			    "id": "test_threat",
			    "name": "Test Threat",
			    "category": "Threat"
			  }
			]
			""";

		var method = typeof(StorytellerDefLoader).GetMethod("DeserializeDefs", BindingFlags.NonPublic | BindingFlags.Static);
		var defs = Assert.IsType<List<IncidentDef>>(method!.Invoke(null, [json]));

		var def = Assert.Single(defs);
		Assert.Equal(IncidentCategory.Threat, def.Category);
	}

	[Fact]
	public void InventoryAndTradeMessages_UseShapeDescriptions_WhenStateProvided()
	{
		var state = new GameState();
		var actor = CreateActor("player", Factions.Player);
		var trader = CreateActor("merchant", Factions.Friendly);
		var hiddenItem = new Item
		{
			Id = "mystery_rifle",
			Name = "Prototype Thunderbolt",
			Category = ItemCategories.Weapon,
			SubCategory = "firearm_rifle",
			MaterialId = "steel",
			Price = 20,
		};

		actor.Inventory.Add(hiddenItem);
		trader.Gold = 100;

		var dropResult = InventoryModule.Drop(actor, 0, state);
		Assert.DoesNotContain(hiddenItem.Name, dropResult.Message, StringComparison.Ordinal);
		Assert.Contains("Rifle", dropResult.Message, StringComparison.Ordinal);

		actor.Inventory.Add(hiddenItem);
		var sellResult = TradeModule.Sell(actor, trader, 0, state);
		Assert.True(sellResult.Ok);
		Assert.DoesNotContain(hiddenItem.Name, sellResult.Message, StringComparison.Ordinal);
		Assert.Contains("Rifle", sellResult.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void UnidentifiedItemDetail_UsesShapeAndMaterialWithoutRevealingRealName()
	{
		var state = new GameState();
		var hiddenItem = new Item
		{
			Id = "mystery_rifle",
			Name = "Prototype Thunderbolt",
			Category = ItemCategories.Weapon,
			SubCategory = "firearm_rifle",
			MaterialId = "steel",
			SharpDamage = 12,
		};

		var displayName = IdentificationModule.GetItemDisplayName(state, hiddenItem);
		var detail = IdentificationModule.BuildUnknownItemDetail(state, hiddenItem);

		Assert.Equal("Rifle", displayName);
		Assert.Contains("Rifle", detail, StringComparison.Ordinal);
		Assert.Contains("Material:", detail, StringComparison.Ordinal);
		Assert.Contains("Steel", detail, StringComparison.Ordinal);
		Assert.DoesNotContain(hiddenItem.Name, detail, StringComparison.Ordinal);
		Assert.DoesNotContain("Damage:", detail, StringComparison.Ordinal);
	}

	[Fact]
	public void UnidentifiedItemDisplayName_FollowsFallbackOrder()
	{
		var state = new GameState();
		var categoryOnly = new Item
		{
			Id = "mystery_blade",
			Name = "Ancient Sword",
			Category = ItemCategories.Weapon,
		};
		var materialOnly = new Item
		{
			Id = "mystery_fragment",
			Name = "Alien Fragment",
			Category = string.Empty,
			MaterialId = "steel",
		};
		var missingMetadata = new Item
		{
			Name = "Ghost Relic",
			Category = string.Empty,
		};

		Assert.Equal("Weapon", IdentificationModule.GetItemDisplayName(state, categoryOnly));
		Assert.Equal("Steel", IdentificationModule.GetItemDisplayName(state, materialOnly));
		Assert.Equal("Unknown item", IdentificationModule.GetItemDisplayName(state, missingMetadata));
	}

	[Fact]
	public void IdentifiedItems_StillUseRealNameAndFullDetail()
	{
		var state = new GameState();
		var identifiedItem = new Item
		{
			Id = "mystery_rifle",
			Name = "Prototype Thunderbolt",
			Category = ItemCategories.Weapon,
			SubCategory = "firearm_rifle",
			MaterialId = "steel",
			SharpDamage = 12,
		};

		IdentificationModule.IdentifyItem(state, identifiedItem);
		var displayName = IdentificationModule.GetItemDisplayName(state, identifiedItem);
		var detail = IdentificationModule.BuildUnknownItemDetail(state, identifiedItem);

		Assert.Equal(identifiedItem.Name, displayName);
		Assert.Contains(identifiedItem.Name, detail, StringComparison.Ordinal);
		Assert.Contains("Damage:", detail, StringComparison.Ordinal);
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
