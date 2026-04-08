using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using Xunit;

namespace MiniRPG.Tests;

public sealed class InteractionModuleTests
{
	public InteractionModuleTests()
	{
		GameConfig.Load();
		PresetDB.Load();
	}

	[Fact]
	public void GetInteractions_ReturnsTrade_ForActorsWithShopGoods()
	{
		var player = CreateActor("player", Factions.Player);
		var merchant = CreateActor("merchant", Factions.Friendly);
		merchant.ShopSlots.Add(new ShopSlot
		{
			Stock = 2,
			Item = new Item
			{
				Id = "torch",
				Name = "Torch",
				Category = ItemCategories.Tool,
				Price = 6,
				Weight = 0.7f,
			},
		});

		var interactions = InteractionModule.GetInteractions(player, merchant, InteractionDefs.All);

		Assert.Contains(interactions, interaction => interaction.Id == "trade");
	}

	[Fact]
	public void GetInteractions_DoesNotReturnTrade_WhenTargetHasNoGoods()
	{
		var player = CreateActor("player", Factions.Player);
		var villager = CreateActor("villager", Factions.Friendly);

		var interactions = InteractionModule.GetInteractions(player, villager, InteractionDefs.All);

		Assert.DoesNotContain(interactions, interaction => interaction.Id == "trade");
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
				Id = $"{id}_head",
				Name = "Head",
				MaxDurability = 10,
				Durability = 10,
				Material = "flesh",
				BodyPart = BodyParts.Head,
				Capacities = new Dictionary<string, float>
				{
					[Caps.Consciousness] = 1f,
				},
				Tags = new Dictionary<string, int>(),
			},
		],
	};
}
