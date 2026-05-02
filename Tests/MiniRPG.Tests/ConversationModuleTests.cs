using System;
using MiniRPG.Core.Conversation;
using MiniRPG.Core.Data;
using MiniRPG.Core.Multiplayer;
using Xunit;

namespace MiniRPG.Tests;

public sealed class ConversationModuleTests
{
	[Fact]
	public void TryOpen_CreatesState_AndChooseClosesTerminalBranch()
	{
		ConversationRegistry.Clear();
		ConversationRegistry.Register(new ConversationDef
		{
			Id = "test_conv",
			Triggers = new ConversationTriggers { Priority = 99 },
			EntryNode = "a",
			Nodes =
			[
				new ConversationNodeDef
				{
					Id = "a",
					Speaker = "npc",
					Lines = ["Hi"],
					Branches =
					[
						new ConversationBranchDef { Kind = "choice", Text = "Bye", Effects = [] },
					],
				},
			],
		});

		var state = new GameState();
		var player = new Actor { Id = "player", DisplayName = "P", X = 0, Y = 0, Z = 0 };
		var npc = new Actor { Id = "npc1", DisplayName = "N", X = 1, Y = 0, Z = 0 };
		ActorModule.Add(state, player);
		ActorModule.Add(state, npc);
		state.PlayerId = player.Id;

		var rng = new Random(1);
		var evs = ConversationModule.TryOpen(state, player, npc, RoomRuntimeModule.SinglePlayerSessionId, rng);
		Assert.Contains(evs, e => e.Type == "conversation_opened");
		Assert.True(state.ActiveConversations.ContainsKey(npc.Id));

		var adv = ConversationModule.AdvanceLine(state, npc.Id, RoomRuntimeModule.SinglePlayerSessionId);
		Assert.NotEmpty(adv);

		var choose = ConversationModule.Choose(state, npc.Id, RoomRuntimeModule.SinglePlayerSessionId, 0, rng);
		Assert.Contains(choose, e => e.Type == "conversation_closed" || e.Type == "conversation_chosen");
	}
}
