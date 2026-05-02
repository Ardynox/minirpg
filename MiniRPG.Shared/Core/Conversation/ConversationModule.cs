using System;
using System.Collections.Generic;
using MiniRPG.Core.Data;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Core.Social;
using MiniRPG.Module;

namespace MiniRPG.Core.Conversation;

public static class ConversationModule
{
	private static string NormalizeSession(string? sessionId) =>
		string.IsNullOrWhiteSpace(sessionId)
			? RoomRuntimeModule.SinglePlayerSessionId
			: sessionId!;

	private static bool CanControlChoices(ConversationState c, string? sessionId) =>
		string.Equals(NormalizeSession(sessionId), NormalizeSession(c.LockedPlayerSessionId), StringComparison.Ordinal);

	public static List<GameEvent> TryAddObserver(
		GameState state,
		string npcActorId,
		string? playerSessionId,
		Actor observerInitiator)
	{
		var list = new List<GameEvent>();
		if (!state.ActiveConversations.TryGetValue(npcActorId, out var conv))
			return list;
		var session = NormalizeSession(playerSessionId);
		if (string.Equals(session, NormalizeSession(conv.LockedPlayerSessionId), StringComparison.Ordinal))
			return list;
		if (conv.ObserverPlayerSessionIds.Add(session))
		{
			list.Add(new GameEvent("conversation_observer_joined")
			{
				TargetId = npcActorId,
				InitiatorId = observerInitiator.Id,
				ConversationDefId = conv.ConversationDefId,
			});
		}

		return list;
	}

	public static List<GameEvent> TryOpen(
		GameState state,
		Actor initiator,
		Actor npc,
		string? playerSessionId,
		Random rng)
	{
		ConversationDefLoader.EnsureLoaded();
		var events = new List<GameEvent>();
		var session = NormalizeSession(playerSessionId);
		if (state.ActiveConversations.ContainsKey(npc.Id))
			return TryAddObserver(state, npc.Id, playerSessionId, initiator);

		var def = ConversationRegistry.SelectDefForNpc(npc, state);
		if (def == null)
			return events;

		var player = ActorModule.GetPlayer(state);
		if (player == null)
			return events;

		var conv = new ConversationState
		{
			ConversationDefId = def.Id,
			NpcActorId = npc.Id,
			InitiatorActorId = initiator.Id,
			LockedPlayerSessionId = session,
		};
		state.ActiveConversations[npc.Id] = conv;
		EnterNode(state, def, conv, def.EntryNode, player, npc, rng, events);
		events.Add(new GameEvent("conversation_opened")
		{
			TargetId = npc.Id,
			InitiatorId = player.Id,
			ConversationDefId = def.Id,
			ConversationNodeId = conv.CurrentNodeId,
		});
		return events;
	}

	public static List<GameEvent> AdvanceLine(GameState state, string npcActorId, string? playerSessionId)
	{
		var events = new List<GameEvent>();
		if (!state.ActiveConversations.TryGetValue(npcActorId, out var conv))
			return events;
		if (!CanControlChoices(conv, playerSessionId))
			return events;
		if (!ConversationRegistry.TryGet(conv.ConversationDefId, out var def) || def == null)
			return events;
		var node = ConversationRegistry.FindNode(def, conv.CurrentNodeId);
		if (node == null || conv.Stage != ConversationStage.Lines)
			return events;

		conv.LineIndex++;
		if (conv.LineIndex >= node.Lines.Count)
		{
			conv.Stage = ConversationStage.Choosing;
			conv.LineIndex = Math.Max(0, node.Lines.Count - 1);
		}

		events.Add(MakeAdvancedEvent(conv));
		return events;
	}

	public static List<GameEvent> Choose(
		GameState state,
		string npcActorId,
		string? playerSessionId,
		int branchIndex,
		Random rng)
	{
		var events = new List<GameEvent>();
		if (!state.ActiveConversations.TryGetValue(npcActorId, out var conv))
			return events;
		if (!CanControlChoices(conv, playerSessionId))
			return events;
		if (!ConversationRegistry.TryGet(conv.ConversationDefId, out var def) || def == null)
			return events;
		var player = ActorModule.GetPlayer(state);
		var npc = ActorModule.GetById(state, npcActorId);
		if (player == null || npc == null)
			return events;
		var node = ConversationRegistry.FindNode(def, conv.CurrentNodeId);
		if (node == null || conv.Stage != ConversationStage.Choosing)
			return events;

		var visible = GetVisibleBranchIndices(state, def, conv, node, player, npc);
		if (branchIndex < 0 || branchIndex >= node.Branches.Count || !visible.Contains(branchIndex))
			return events;

		var branch = node.Branches[branchIndex];
		events.Add(new GameEvent("conversation_chosen")
		{
			TargetId = npcActorId,
			InitiatorId = player.Id,
			ConversationDefId = conv.ConversationDefId,
			ConversationNodeId = conv.CurrentNodeId,
			ConversationBranchIndex = branchIndex,
		});

		if (string.Equals(branch.Kind, "check", StringComparison.OrdinalIgnoreCase) && branch.Check != null)
		{
			var result = ConversationCheckResolver.Resolve(state, player, branch.Check, rng);
			conv.History.Add($"{branch.Text} → {(result.Passed ? "pass" : "fail")} (d20:{result.Roll}+{result.Modifier}={result.Total} vs DC{branch.Check.Dc})");
			var nextId = result.Passed ? branch.OnPass : branch.OnFail;
			if (string.IsNullOrWhiteSpace(nextId))
				CloseInternal(state, npcActorId, playerSessionId, events);
			else
				EnterNode(state, def, conv, nextId!, player, npc, rng, events);
		}
		else
		{
			ApplyEffects(state, player, npc, branch.Effects, events);
			ApplyApproval(state, player, npc, branch, events);
			if (string.IsNullOrWhiteSpace(branch.Next))
				CloseInternal(state, npcActorId, playerSessionId, events);
			else
				EnterNode(state, def, conv, branch.Next!, player, npc, rng, events);
		}

		return events;
	}

	public static List<GameEvent> Leave(GameState state, string npcActorId, string? playerSessionId)
	{
		var events = new List<GameEvent>();
		CloseInternal(state, npcActorId, playerSessionId, events);
		return events;
	}

	public static List<GameEvent> RequestTakeover(GameState state, string npcActorId, string? requesterSessionId)
	{
		var events = new List<GameEvent>();
		if (!state.ActiveConversations.TryGetValue(npcActorId, out var conv))
			return events;
		var req = NormalizeSession(requesterSessionId);
		if (CanControlChoices(conv, requesterSessionId))
			return events;
		if (!conv.ObserverPlayerSessionIds.Contains(req))
			return events;
		conv.PendingTakeoverFromSessionId = req;
		events.Add(new GameEvent("conversation_takeover_requested")
		{
			TargetId = npcActorId,
			InitiatorId = req,
			ConversationDefId = conv.ConversationDefId,
		});
		return events;
	}

	public static List<GameEvent> ApproveTakeover(
		GameState state,
		string npcActorId,
		string? ownerSessionId,
		bool approve)
	{
		var events = new List<GameEvent>();
		if (!state.ActiveConversations.TryGetValue(npcActorId, out var conv))
			return events;
		if (!CanControlChoices(conv, ownerSessionId))
			return events;
		var pending = conv.PendingTakeoverFromSessionId;
		if (string.IsNullOrWhiteSpace(pending))
			return events;
		if (!approve)
		{
			conv.PendingTakeoverFromSessionId = "";
			events.Add(new GameEvent("conversation_takeover_denied") { TargetId = npcActorId, InitiatorId = pending });
			return events;
		}

		conv.LockedPlayerSessionId = pending;
		conv.ObserverPlayerSessionIds.Remove(pending);
		conv.PendingTakeoverFromSessionId = "";
		events.Add(new GameEvent("conversation_takeover_approved") { TargetId = npcActorId, InitiatorId = pending });
		return events;
	}

	public static HashSet<int> GetVisibleBranchIndices(
		GameState state,
		ConversationDef def,
		ConversationState conv,
		ConversationNodeDef node,
		Actor player,
		Actor npc)
	{
		_ = def;
		_ = conv;
		var ctx = DialogContext.Build(state, player, npc);
		var set = new HashSet<int>();
		for (var i = 0; i < node.Branches.Count; i++)
		{
			var b = node.Branches[i];
			if (b.RequiresPartyMember && state.Party.MemberIds.Count <= 1)
				continue;
			if (b.Visibility != null && !DialogConditionMatcher.Matches(ctx, b.Visibility))
				continue;
			set.Add(i);
		}

		return set;
	}

	private static void EnterNode(
		GameState state,
		ConversationDef def,
		ConversationState conv,
		string nodeId,
		Actor player,
		Actor npc,
		Random rng,
		List<GameEvent> events)
	{
		var node = ConversationRegistry.FindNode(def, nodeId);
		if (node == null)
		{
			CloseInternal(state, conv.NpcActorId, conv.LockedPlayerSessionId, events);
			return;
		}

		conv.CurrentNodeId = node.Id;
		conv.LineIndex = 0;
		ApplyEffects(state, player, npc, node.OnEnter, events);
		if (node.Lines.Count == 0)
			conv.Stage = ConversationStage.Choosing;
		else
			conv.Stage = ConversationStage.Lines;

		events.Add(MakeAdvancedEvent(conv));
	}

	private static GameEvent MakeAdvancedEvent(ConversationState conv) =>
		new("conversation_advanced")
		{
			TargetId = conv.NpcActorId,
			ConversationDefId = conv.ConversationDefId,
			ConversationNodeId = conv.CurrentNodeId,
			ConversationPayload = conv.Stage.ToString(),
		};

	private static void CloseInternal(GameState state, string npcActorId, string? playerSessionId, List<GameEvent> events)
	{
		if (!state.ActiveConversations.TryGetValue(npcActorId, out var conv))
			return;
		state.ActiveConversations.Remove(npcActorId);
		var owner = NormalizeSession(conv.LockedPlayerSessionId);
		var key = ReservationService.BuildPrefixedReservationKey("dialog", npcActorId);
		RoomRuntimeModule.ReleaseInteraction(state, key, owner);
		if (ConversationRegistry.TryGet(conv.ConversationDefId, out var d)
			&& d?.Triggers?.RequireFirstMeet == true)
		{
			var p = ActorModule.GetPlayer(state);
			if (p != null && !string.IsNullOrWhiteSpace(d.Id) && !p.DialogMemory.Contains($"met_{d.Id}"))
				p.DialogMemory.Add($"met_{d.Id}");
		}

		events.Add(new GameEvent("conversation_closed") { TargetId = npcActorId });
	}

	private static void ApplyEffects(
		GameState state,
		Actor player,
		Actor npc,
		IReadOnlyList<ConversationEffectDef>? effects,
		List<GameEvent> events)
	{
		if (effects == null)
			return;
		foreach (var e in effects)
		{
			switch (e.Type.ToLowerInvariant())
			{
				case "affinity":
					npc.DialogAffinity += e.Value;
					break;
				case "mood":
					npc.DialogMood += e.Value;
					break;
				case "memory":
					if (!string.IsNullOrWhiteSpace(e.Key) && !player.DialogMemory.Contains(e.Key!))
						player.DialogMemory.Add(e.Key!);
					break;
				case "gold":
					player.Gold += (int)e.Value;
					break;
				case "relationship":
				{
					var obs = ResolveToken(state, player, npc, e.Observer ?? "player");
					var sub = ResolveToken(state, player, npc, e.Subject ?? "npc");
					events.Add(new GameEvent("relationship_adjust")
					{
						InitiatorId = obs,
						TargetId = sub,
						RelationshipTrustDelta = e.Value,
					});
					break;
				}
				case "rumor":
					if (!Enum.TryParse<RumorKind>(e.RumorKind, true, out var kind))
						kind = RumorKind.CasualtyReported;
					events.Add(new GameEvent("conversation_rumor")
					{
						InitiatorId = npc.Id,
						SkillId = kind.ToString(),
						TargetX = npc.X,
						TargetY = npc.Y,
						TargetZ = npc.Z,
					});
					break;
				case "recruit":
					_ = PartyModule.TryRecruit(state, npc.Id);
					break;
			}
		}
	}

	private static void ApplyApproval(
		GameState state,
		Actor player,
		Actor npc,
		ConversationBranchDef branch,
		List<GameEvent> events)
	{
		if (branch.ApprovalEffects == null)
			return;
		foreach (var (key, delta) in branch.ApprovalEffects)
		{
			var subject = ResolveToken(state, player, npc, key == "npc" ? "npc" : key);
			if (string.IsNullOrWhiteSpace(subject))
				continue;
			events.Add(new GameEvent("relationship_adjust")
			{
				InitiatorId = player.Id,
				TargetId = subject,
				RelationshipTrustDelta = delta,
			});
		}
	}

	private static string ResolveToken(GameState state, Actor player, Actor npc, string token)
	{
		if (string.Equals(token, "player", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(token, "player:active", StringComparison.OrdinalIgnoreCase))
			return player.Id;
		if (string.Equals(token, "npc", StringComparison.OrdinalIgnoreCase))
			return npc.Id;
		if (state.Actors.ContainsKey(token))
			return token;
		return "";
	}
}
