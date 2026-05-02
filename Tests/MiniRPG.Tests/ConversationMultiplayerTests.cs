using System;
using MiniRPG.Core.Conversation;
using MiniRPG.Core.Data;
using MiniRPG.Core.Multiplayer;
using Xunit;

namespace MiniRPG.Tests;

public sealed class ConversationMultiplayerTests
{
	[Fact]
	public void RequestTakeover_ThenApprove_SwitchesLock()
	{
		ConversationRegistry.Clear();
		ConversationRegistry.Register(new ConversationDef
		{
			Id = "mp_conv",
			Triggers = new ConversationTriggers { Priority = 10 },
			EntryNode = "n",
			Nodes =
			[
				new ConversationNodeDef
				{
					Id = "n",
					Lines = ["Hi"],
					Branches = [new ConversationBranchDef { Kind = "choice", Text = "Ok", Next = "n2" }],
				},
				new ConversationNodeDef { Id = "n2", Lines = ["Bye"], Branches = [] },
			],
		});

		var state = new GameState();
		RoomRuntimeModule.GetOrCreatePlayer(state, "owner", "Owner");
		RoomRuntimeModule.GetOrCreatePlayer(state, "guest", "Guest");
		var player = new Actor { Id = "player", DisplayName = "P", X = 0, Y = 0, Z = 0 };
		var npc = new Actor { Id = "npcX", DisplayName = "N", X = 1, Y = 0, Z = 0 };
		ActorModule.Add(state, player);
		ActorModule.Add(state, npc);
		state.PlayerId = player.Id;

		var rng = new Random(2);
		Assert.NotEmpty(ConversationModule.TryOpen(state, player, npc, "owner", rng));
		Assert.Equal("owner", state.ActiveConversations[npc.Id].LockedPlayerSessionId);

		Assert.NotEmpty(ConversationModule.TryAddObserver(state, npc.Id, "guest", player));
		Assert.Contains("guest", state.ActiveConversations[npc.Id].ObserverPlayerSessionIds);

		Assert.NotEmpty(ConversationModule.RequestTakeover(state, npc.Id, "guest"));
		Assert.Equal("guest", state.ActiveConversations[npc.Id].PendingTakeoverFromSessionId);

		Assert.NotEmpty(ConversationModule.ApproveTakeover(state, npc.Id, "owner", approve: true));
		Assert.Equal("guest", state.ActiveConversations[npc.Id].LockedPlayerSessionId);
		Assert.DoesNotContain("guest", state.ActiveConversations[npc.Id].ObserverPlayerSessionIds);
	}
}
