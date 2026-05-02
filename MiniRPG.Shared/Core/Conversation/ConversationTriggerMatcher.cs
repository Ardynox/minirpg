using System;
using System.Collections.Generic;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Conversation;

public static class ConversationTriggerMatcher
{
	public static bool Matches(ConversationDef def, Actor npc, GameState state, Dictionary<string, int> tags)
	{
		var t = def.Triggers;
		if (t == null)
			return true;
		if (t.RequireFirstMeet && npc.DialogTalkCount > 0)
			return false;
		if (t.ActorTags == null || t.ActorTags.Count == 0)
			return true;
		foreach (var tag in t.ActorTags)
		{
			if (string.IsNullOrWhiteSpace(tag))
				continue;
			if (!tags.TryGetValue(tag, out var v) || v <= 0)
				return false;
		}

		return true;
	}
}
