using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Conversation;

public enum ConversationStage
{
	Lines,
	Choosing,
	Closed,
}

public sealed class ConversationState
{
	public string ConversationDefId { get; set; } = "";
	public string NpcActorId { get; set; } = "";
	public string? InitiatorActorId { get; set; }
	public string? LockedPlayerSessionId { get; set; }
	public HashSet<string> ObserverPlayerSessionIds { get; set; } = new(StringComparer.Ordinal);
	public string PendingTakeoverFromSessionId { get; set; } = "";
	public ConversationStage Stage { get; set; } = ConversationStage.Lines;
	public string CurrentNodeId { get; set; } = "";
	public int LineIndex { get; set; }
	public List<string> History { get; set; } = [];
	public HashSet<string> Flags { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ConversationStateSnapshot
{
	[JsonPropertyName("conversationDefId")]
	public string ConversationDefId { get; set; } = "";

	[JsonPropertyName("npcActorId")]
	public string NpcActorId { get; set; } = "";

	[JsonPropertyName("initiatorActorId")]
	public string? InitiatorActorId { get; set; }

	[JsonPropertyName("lockedPlayerSessionId")]
	public string? LockedPlayerSessionId { get; set; }

	[JsonPropertyName("observers")]
	public List<string> ObserverPlayerSessionIds { get; set; } = [];

	[JsonPropertyName("pendingTakeoverFromSessionId")]
	public string PendingTakeoverFromSessionId { get; set; } = "";

	[JsonPropertyName("stage")]
	public ConversationStage Stage { get; set; }

	[JsonPropertyName("currentNodeId")]
	public string CurrentNodeId { get; set; } = "";

	[JsonPropertyName("lineIndex")]
	public int LineIndex { get; set; }

	[JsonPropertyName("history")]
	public List<string> History { get; set; } = [];

	[JsonPropertyName("flags")]
	public List<string> Flags { get; set; } = [];

	public static ConversationStateSnapshot FromRuntime(ConversationState s) =>
		new()
		{
			ConversationDefId = s.ConversationDefId,
			NpcActorId = s.NpcActorId,
			InitiatorActorId = s.InitiatorActorId,
			LockedPlayerSessionId = s.LockedPlayerSessionId,
			ObserverPlayerSessionIds = [.. s.ObserverPlayerSessionIds],
			PendingTakeoverFromSessionId = s.PendingTakeoverFromSessionId,
			Stage = s.Stage,
			CurrentNodeId = s.CurrentNodeId,
			LineIndex = s.LineIndex,
			History = [.. s.History],
			Flags = [.. s.Flags],
		};

	public ConversationState ToRuntime() =>
		new()
		{
			ConversationDefId = ConversationDefId,
			NpcActorId = NpcActorId,
			InitiatorActorId = InitiatorActorId,
			LockedPlayerSessionId = LockedPlayerSessionId,
			ObserverPlayerSessionIds = new HashSet<string>(ObserverPlayerSessionIds ?? [], StringComparer.Ordinal),
			PendingTakeoverFromSessionId = PendingTakeoverFromSessionId ?? "",
			Stage = Stage,
			CurrentNodeId = CurrentNodeId,
			LineIndex = LineIndex,
			History = History != null ? [.. History] : [],
			Flags = new HashSet<string>(Flags ?? [], StringComparer.Ordinal),
		};
}

public sealed class ConversationTriggers
{
	public List<string>? ActorTags { get; set; }
	public int Priority { get; set; }
	public bool RequireFirstMeet { get; set; }
}

public sealed class SkillCheckDef
{
	public string Ability { get; set; } = "cha";
	public string Skill { get; set; } = "";
	public int Dc { get; set; } = 10;
}

public sealed class ConversationEffectDef
{
	public string Type { get; set; } = "";
	public string? Key { get; set; }
	public float Value { get; set; }
	public string? Observer { get; set; }
	public string? Subject { get; set; }
	public string? RumorKind { get; set; }
}

public sealed class ConversationBranchDef
{
	public string Kind { get; set; } = "choice";
	public string Speaker { get; set; } = "player:active";
	public string Text { get; set; } = "";
	public string? Next { get; set; }
	public SkillCheckDef? Check { get; set; }
	public string? OnPass { get; set; }
	public string? OnFail { get; set; }
	public DialogCondition? Visibility { get; set; }
	public bool RequiresPartyMember { get; set; }
	public Dictionary<string, float>? ApprovalEffects { get; set; }
	public List<ConversationEffectDef>? Effects { get; set; }
}

public sealed class ConversationNodeDef
{
	public string Id { get; set; } = "";
	public string Speaker { get; set; } = "npc";
	public List<string> Lines { get; set; } = [];
	public List<ConversationEffectDef>? OnEnter { get; set; }
	public List<ConversationBranchDef> Branches { get; set; } = [];
}

public sealed class ConversationDef
{
	public string Id { get; set; } = "";
	public ConversationTriggers? Triggers { get; set; }
	public string EntryNode { get; set; } = "";
	public List<ConversationNodeDef> Nodes { get; set; } = [];
}

public static class ConversationRegistry
{
	private static readonly Dictionary<string, ConversationDef> ById = new(StringComparer.Ordinal);

	public static void Clear() => ById.Clear();

	public static void Register(ConversationDef def)
	{
		if (!string.IsNullOrWhiteSpace(def.Id))
			ById[def.Id] = def;
	}

	public static bool TryGet(string id, out ConversationDef? def) => ById.TryGetValue(id, out def);

	public static ConversationDef? SelectDefForNpc(Actor npc, GameState state)
	{
		var tags = npc.ComputeTags();
		ConversationDef? best = null;
		var bestP = int.MinValue;
		foreach (var def in ById.Values)
		{
			if (!ConversationTriggerMatcher.Matches(def, npc, state, tags))
				continue;
			var p = def.Triggers?.Priority ?? 0;
			if (p > bestP)
			{
				bestP = p;
				best = def;
			}
		}

		return best ?? (ById.TryGetValue("default_npc", out var fb) ? fb : null);
	}

	public static ConversationNodeDef? FindNode(ConversationDef def, string nodeId)
	{
		foreach (var n in def.Nodes)
		{
			if (string.Equals(n.Id, nodeId, StringComparison.Ordinal))
				return n;
		}

		return null;
	}
}
