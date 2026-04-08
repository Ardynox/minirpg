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

		// 冷却中
		state.Turn = 15;
		var events = SocialModule.TryInteract(state, "player", "npc1");
		Assert.Empty(events);

		// 冷却结束
		state.Turn = 25;
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
}
