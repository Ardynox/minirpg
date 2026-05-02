using MiniRPG.Core;
using MiniRPG.Core.Data;
using MiniRPG.Core.Social;
using Xunit;

namespace MiniRPG.Tests;

public class SocialModuleTests
{
	private static GameState CreateState()
	{
		var state = new GameState { PlayerId = "player" };
		state.Actors["player"] = new Actor { Id = "player", DisplayName = "Player" };
		state.Actors["npc1"] = new Actor { Id = "npc1", DisplayName = "NPC1" };
		state.Actors["npc2"] = new Actor { Id = "npc2", DisplayName = "NPC2" };
		return state;
	}

	[Fact]
	public void GetOpinion_DefaultsToZero()
	{
		var state = CreateState();
		Assert.Equal(0, SocialModule.GetOpinion(state, "player", "npc1"));
	}

	[Fact]
	public void AdjustOpinion_ChangesValue()
	{
		var state = CreateState();
		SocialModule.AdjustOpinion(state, "player", "npc1", 30);
		Assert.Equal(30, SocialModule.GetOpinion(state, "player", "npc1"));
	}

	[Fact]
	public void AdjustOpinion_ClampsToRange()
	{
		var state = CreateState();
		SocialModule.AdjustOpinion(state, "player", "npc1", 200);
		Assert.Equal(100, SocialModule.GetOpinion(state, "player", "npc1"));

		SocialModule.AdjustOpinion(state, "player", "npc1", -300);
		Assert.Equal(-100, SocialModule.GetOpinion(state, "player", "npc1"));
	}

	[Fact]
	public void Opinion_IsAsymmetric()
	{
		var state = CreateState();
		SocialModule.AdjustOpinion(state, "player", "npc1", 50);
		SocialModule.AdjustOpinion(state, "npc1", "player", -20);

		Assert.Equal(50, SocialModule.GetOpinion(state, "player", "npc1"));
		Assert.Equal(-20, SocialModule.GetOpinion(state, "npc1", "player"));
	}

	[Fact]
	public void AddTag_And_HasTag()
	{
		var state = CreateState();
		SocialModule.AddTag(state, "player", "npc1", "friend");
		Assert.True(SocialModule.HasTag(state, "player", "npc1", "friend"));
		Assert.False(SocialModule.HasTag(state, "npc1", "player", "friend"));
	}

	[Fact]
	public void RemoveTag_Works()
	{
		var state = CreateState();
		SocialModule.AddTag(state, "player", "npc1", "rival");
		SocialModule.RemoveTag(state, "player", "npc1", "rival");
		Assert.False(SocialModule.HasTag(state, "player", "npc1", "rival"));
	}

	[Fact]
	public void IsFriend_And_IsRival()
	{
		var state = CreateState();
		SocialModule.SetOpinion(state, "player", "npc1", 60);
		SocialModule.SetOpinion(state, "player", "npc2", -70);

		Assert.True(SocialModule.IsFriend(state, "player", "npc1"));
		Assert.False(SocialModule.IsRival(state, "player", "npc1"));
		Assert.True(SocialModule.IsRival(state, "player", "npc2"));
		Assert.False(SocialModule.IsFriend(state, "player", "npc2"));
	}

	[Fact]
	public void TryInteract_WithRegisteredInteraction_GeneratesEvent()
	{
		SocialModule.ClearInteractions();
		SocialModule.RegisterInteraction(new SocialInteractionDef
		{
			Id = "chat",
			Name = "闲聊",
			InitiatorOpinionChange = 5,
			RecipientOpinionChange = 3,
			Weight = 1f,
		});

		var state = CreateState();
		var events = SocialModule.TryInteract(state, "player", "npc1");

		Assert.NotEmpty(events);
		Assert.Equal("social_interaction", events[0].Type);
		Assert.Equal("chat", events[0].InteractionDefId);
		Assert.Equal(5, SocialModule.GetOpinion(state, "player", "npc1"));
		Assert.Equal(3, SocialModule.GetOpinion(state, "npc1", "player"));
	}

	[Fact]
	public void TryInteract_RespectsCooldow()
	{
		SocialModule.ClearInteractions();
		SocialModule.RegisterInteraction(new SocialInteractionDef
		{
			Id = "chat",
			Name = "闲聊",
			Weight = 1f,
		});

		var state = CreateState();
		state.Turn = 10;
		SocialModule.TryInteract(state, "player", "npc1");

		// 冷却中（按 240 turn/day 校准后 SocialCooldownTurns = 20，旧历法下为 10）
		state.Turn = 25;
		var events = SocialModule.TryInteract(state, "player", "npc1");
		Assert.Empty(events);

		// 冷却结束
		state.Turn = 40;
		events = SocialModule.TryInteract(state, "player", "npc1");
		Assert.NotEmpty(events);
	}

	[Fact]
	public void GetRelationsOf_ReturnsCorrectEntries()
	{
		var state = CreateState();
		SocialModule.AdjustOpinion(state, "player", "npc1", 10);
		SocialModule.AdjustOpinion(state, "player", "npc2", 20);
		SocialModule.AdjustOpinion(state, "npc1", "player", 5);

		var relations = SocialModule.GetRelationsOf(state, "player");
		Assert.Equal(2, relations.Count);
	}

	[Fact]
	public void SocialState_Clone_IsIndependent()
	{
		var original = new SocialState();
		original.Relations["a:b"] = new RelationEntry { FromId = "a", ToId = "b", Opinion = 50 };
		original.SocialCooldowns["a"] = 10;

		var clone = original.Clone();
		clone.Relations["a:b"].Opinion = 0;
		clone.SocialCooldowns["a"] = 999;

		Assert.Equal(50, original.Relations["a:b"].Opinion);
		Assert.Equal(10, original.SocialCooldowns["a"]);
	}

	// ── TryGiveItem ────────────────────────────────────────────────
	//
	// Covers the new gift_given pipeline: TryGiveItem actually moves the
	// item between the two actors' inventories AND emits a single
	// gift_given GameEvent that downstream RelationshipModule /
	// ActorMemoryModule can consume. Failure modes return an empty list
	// so ServerActionGateway can map them to a localized rejection.

	private static GameState CreateGiftState(out Actor giver, out Actor recipient, out Item gift)
	{
		var state = new GameState { PlayerId = "player" };
		giver = new Actor { Id = "giver", DisplayName = "Giver", Faction = Factions.Player };
		recipient = new Actor { Id = "recipient", DisplayName = "Recipient", Faction = Factions.Friendly };
		state.Actors["giver"] = giver;
		state.Actors["recipient"] = recipient;
		gift = new Item { Id = "berry", InstanceId = "berry-1", Name = "Berry" };
		// Mark the item type identified so PopulateItemIdentity surfaces
		// the real Name in the emitted event (mirrors the realistic
		// scenario where the giver knows what they are handing over).
		state.IdentifiedItemTypes.Add(gift.Id);
		giver.Inventory.Add(gift);
		return state;
	}

	[Fact]
	public void TryGiveItem_HappyPath_TransfersItemAndEmitsEvent()
	{
		var state = CreateGiftState(out var giver, out var recipient, out var gift);

		var events = SocialModule.TryGiveItem(state, giver.Id, recipient.Id, gift.InstanceId);

		Assert.Empty(giver.Inventory);
		Assert.Single(recipient.Inventory);
		Assert.Equal(gift.InstanceId, recipient.Inventory[0].InstanceId);

		Assert.Single(events);
		var ev = events[0];
		Assert.Equal("gift_given", ev.Type);
		Assert.Equal(giver.Id, ev.InitiatorId);
		Assert.Equal(recipient.Id, ev.TargetId);
		Assert.Equal(Factions.Player, ev.InitiatorFaction);
		Assert.Equal(Factions.Friendly, ev.TargetFaction);
		Assert.Equal(gift.Id, ev.ItemTypeId);
		Assert.Equal(gift.Name, ev.ItemName);
	}

	[Theory]
	[InlineData("", "recipient", "berry-1")]
	[InlineData("giver", "", "berry-1")]
	[InlineData("giver", "recipient", "")]
	[InlineData(null, "recipient", "berry-1")]
	[InlineData("giver", null, "berry-1")]
	[InlineData("giver", "recipient", null)]
	public void TryGiveItem_BlankIds_ReturnsEmpty(string? from, string? to, string? itemId)
	{
		var state = CreateGiftState(out var giver, out _, out _);

		var events = SocialModule.TryGiveItem(state, from!, to!, itemId!);

		Assert.Empty(events);
		Assert.Single(giver.Inventory);
	}

	[Fact]
	public void TryGiveItem_SelfGive_ReturnsEmpty()
	{
		var state = CreateGiftState(out var giver, out _, out var gift);

		var events = SocialModule.TryGiveItem(state, giver.Id, giver.Id, gift.InstanceId);

		Assert.Empty(events);
		Assert.Single(giver.Inventory);
	}

	[Fact]
	public void TryGiveItem_MissingActor_ReturnsEmpty()
	{
		var state = CreateGiftState(out var giver, out _, out var gift);

		var events = SocialModule.TryGiveItem(state, giver.Id, "ghost", gift.InstanceId);

		Assert.Empty(events);
		Assert.Single(giver.Inventory);
	}

	[Fact]
	public void TryGiveItem_ItemNotInInventory_ReturnsEmpty()
	{
		var state = CreateGiftState(out var giver, out var recipient, out _);

		var events = SocialModule.TryGiveItem(state, giver.Id, recipient.Id, "no-such-instance");

		Assert.Empty(events);
		Assert.Single(giver.Inventory);
		Assert.Empty(recipient.Inventory);
	}

	[Fact]
	public void TryGiveItem_EquippedItem_ReturnsEmpty()
	{
		// Mirror ChestPut policy: equipped items must be unequipped first.
		// Doing it implicitly here would silently free body slots.
		var state = CreateGiftState(out var giver, out var recipient, out var gift);
		gift.Equipped = true;

		var events = SocialModule.TryGiveItem(state, giver.Id, recipient.Id, gift.InstanceId);

		Assert.Empty(events);
		Assert.Single(giver.Inventory);
		Assert.Empty(recipient.Inventory);
		Assert.True(giver.Inventory[0].Equipped);
	}

	[Fact]
	public void TryGiveItem_RoutesThroughInventoryModule_StacksOnRecipient()
	{
		// Sanity check that the transfer goes through InventoryModule.Add
		// (not a hand-rolled list mutation): when a stackable instance of
		// the same item already lives on the recipient and there is room,
		// the giver-side stack collapses into the recipient's stack. The
		// exact merge accounting is owned by InventoryModule; we only
		// assert that the recipient's existing stack absorbed the gift.
		var state = CreateGiftState(out var giver, out var recipient, out var gift);
		gift.MaxStack = 10;
		gift.StackCount = 3;
		gift.EnsureRuntimeState();
		var existing = new Item
		{
			Id = "berry",
			InstanceId = "berry-existing",
			Name = "Berry",
			MaxStack = 10,
			StackCount = 5,
		};
		// Match the runtime-normalised durability so CanStackWith succeeds
		// in InventoryModule.TryMergeIntoExisting; without this the existing
		// stack and the incoming gift differ on MaxDurability after the
		// recipient call to EnsureRuntimeState and the merge silently fails.
		existing.EnsureRuntimeState();
		recipient.Inventory.Add(existing);

		var events = SocialModule.TryGiveItem(state, giver.Id, recipient.Id, gift.InstanceId);

		Assert.Single(events);
		Assert.Empty(giver.Inventory);
		var stack = recipient.Inventory.Find(i => i.InstanceId == "berry-existing");
		Assert.NotNull(stack);
		Assert.Equal(8, stack!.StackCount);
	}
}
