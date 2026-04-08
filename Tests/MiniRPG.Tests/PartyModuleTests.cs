using MiniRPG.Core;
using MiniRPG.Core.AI;
using MiniRPG.Core.Data;
using Xunit;

namespace MiniRPG.Tests;

public class PartyModuleTests
{
	private static GameState CreateState()
	{
		var state = new GameState { PlayerId = "player" };
		state.Actors["player"] = new Actor { Id = "player", Faction = Factions.Player };
		PartyModule.Initialize(state);
		return state;
	}

	[Fact]
	public void Initialize_SetsPlayerAsOnlyMember()
	{
		var state = CreateState();
		Assert.Single(state.Party.MemberIds);
		Assert.Equal("player", state.Party.ActiveId);
	}

	[Fact]
	public void IsPartyMember_TrueForPlayer()
	{
		var state = CreateState();
		Assert.True(PartyModule.IsPartyMember(state, "player"));
	}

	[Fact]
	public void IsPartyMember_FalseForNonMember()
	{
		var state = CreateState();
		Assert.False(PartyModule.IsPartyMember(state, "npc1"));
	}

	[Fact]
	public void IsPartyMember_FallbackToPlayerId_WhenPartyEmpty()
	{
		var state = new GameState { PlayerId = "player" };
		state.Actors["player"] = new Actor { Id = "player" };
		// Party 未初始化，MemberIds 为空
		Assert.True(PartyModule.IsPartyMember(state, "player"));
		Assert.False(PartyModule.IsPartyMember(state, "other"));
	}

	[Fact]
	public void TryRecruit_AddsToParty()
	{
		var state = CreateState();
		state.Actors["npc1"] = new Actor { Id = "npc1", Faction = Factions.Friendly, BrainId = "simple" };

		var result = PartyModule.TryRecruit(state, "npc1");
		Assert.True(result.Success);
		Assert.Equal(2, state.Party.MemberIds.Count);
		Assert.True(PartyModule.IsPartyMember(state, "npc1"));
		Assert.Equal(FollowerBrain.BrainId, state.Actors["npc1"].BrainId);
	}

	[Fact]
	public void TryRecruit_FailsWhenFull()
	{
		var state = CreateState();
		state.Party.MaxSize = 1;
		state.Actors["npc1"] = new Actor { Id = "npc1" };

		var result = PartyModule.TryRecruit(state, "npc1");
		Assert.False(result.Success);
		Assert.Equal("party_full", result.FailureReason);
	}

	[Fact]
	public void TryRecruit_FailsForAlreadyMember()
	{
		var state = CreateState();
		var result = PartyModule.TryRecruit(state, "player");
		Assert.False(result.Success);
		Assert.Equal("already_member", result.FailureReason);
	}

	[Fact]
	public void TryDismiss_RemovesFromParty()
	{
		var state = CreateState();
		state.Actors["npc1"] = new Actor { Id = "npc1", Faction = Factions.Player, BrainId = FollowerBrain.BrainId };
		state.Party.MemberIds.Add("npc1");

		var result = PartyModule.TryDismiss(state, "npc1");
		Assert.True(result.Success);
		Assert.Single(state.Party.MemberIds);
		Assert.False(PartyModule.IsPartyMember(state, "npc1"));
		Assert.Equal(Factions.Friendly, state.Actors["npc1"].Faction);
	}

	[Fact]
	public void TryDismiss_CannotDismissLeader()
	{
		var state = CreateState();
		var result = PartyModule.TryDismiss(state, "player");
		Assert.False(result.Success);
		Assert.Equal("cannot_dismiss_leader", result.FailureReason);
	}

	[Fact]
	public void CycleActive_RotatesThroughMembers()
	{
		var state = CreateState();
		state.Actors["npc1"] = new Actor { Id = "npc1" };
		state.Party.MemberIds.Add("npc1");

		Assert.Equal("player", PartyModule.GetActiveId(state));

		var next = PartyModule.CycleActive(state);
		Assert.Equal("npc1", next);

		next = PartyModule.CycleActive(state);
		Assert.Equal("player", next);
	}

	[Fact]
	public void TrySetActive_SetsSpecificMember()
	{
		var state = CreateState();
		state.Actors["npc1"] = new Actor { Id = "npc1" };
		state.Party.MemberIds.Add("npc1");

		Assert.True(PartyModule.TrySetActive(state, "npc1"));
		Assert.Equal("npc1", PartyModule.GetActiveId(state));
	}

	[Fact]
	public void TrySetActive_FailsForNonMember()
	{
		var state = CreateState();
		Assert.False(PartyModule.TrySetActive(state, "stranger"));
	}

	[Fact]
	public void GetMembers_ReturnsAllLivingMembers()
	{
		var state = CreateState();
		state.Actors["npc1"] = new Actor { Id = "npc1" };
		state.Party.MemberIds.Add("npc1");

		var members = PartyModule.GetMembers(state);
		Assert.Equal(2, members.Count);
	}

	[Fact]
	public void EnsureValid_RemovesDeadMembers()
	{
		var state = CreateState();
		state.Party.MemberIds.Add("dead_npc");
		// dead_npc 不在 Actors 中

		PartyModule.EnsureValid(state);
		Assert.Single(state.Party.MemberIds);
		Assert.Equal("player", state.Party.ActiveId);
	}

	[Fact]
	public void Dismiss_ActiveMember_SwitchesToLeader()
	{
		var state = CreateState();
		state.Actors["npc1"] = new Actor { Id = "npc1", Faction = Factions.Player, BrainId = FollowerBrain.BrainId };
		state.Party.MemberIds.Add("npc1");
		state.Party.ActiveId = "npc1";

		PartyModule.TryDismiss(state, "npc1");
		Assert.Equal("player", PartyModule.GetActiveId(state));
	}
}
