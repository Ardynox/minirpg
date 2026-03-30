using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniRPG.Core;

/// <summary>
/// 交互模块：检测可交互目标、过滤可用交互、执行交互产出事件。
/// </summary>
public static class InteractionModule
{
	private static readonly (int Dx, int Dy)[] Dirs = [(0, -1), (0, 1), (-1, 0), (1, 0), (0, 0)];

	/// <summary>扫描玩家相邻 + 脚下的所有非玩家 Actor。</summary>
	public static List<Actor> GetAvailableTargets(GameState state)
	{
		var targets = new List<Actor>();
		foreach (var (dx, dy) in Dirs)
		{
			var x = state.PlayerX + dx;
			var y = state.PlayerY + dy;
			foreach (var actor in ActorModule.GetAllAt(state, x, y))
			{
				if (actor.Id != state.PlayerId)
					targets.Add(actor);
			}
		}
		return targets;
	}

	/// <summary>根据双方 tag 过滤出满足条件的交互列表。</summary>
	public static List<InteractionDef> GetInteractions(Actor initiator, Actor target,
		IReadOnlyList<InteractionDef> allDefs)
	{
		var iTags = initiator.ComputeTags();
		var tTags = target.ComputeTags();
		return allDefs.Where(d =>
			CheckTags(iTags, d.Required, initiator.Faction) &&
			CheckTags(tTags, d.TargetRequired, target.Faction)
		).ToList();
	}

	/// <summary>执行交互，产出事件。具体效果由 Main.Dispatch 消费。</summary>
	public static List<GameEvent> Execute(GameState state, Actor initiator, Actor target,
		InteractionDef def)
	{
		var events = new List<GameEvent>();
		var evt = new GameEvent("interaction")
		{
			InitiatorId = initiator.Id,
			TargetId = target.Id,
			TargetActorName = target.DisplayName,
		};
		evt.InteractionDefId = def.Id;
		evt.InteractionName = def.Name;
		evt.EffectType = def.EffectType;

		switch (def.EffectType)
		{
			case "tame":
				target.Faction = "friendly";
				initiator.Experiences.Add(new Experience
				{
					Id = $"tamed_{target.Id}",
					Name = $"驯服了{target.DisplayName}",
					Tags = new() { ["驯服经验"] = 1 },
				});
				break;
			case "combat":
				break;
		}

		events.Add(evt);
		return events;
	}

	private static bool CheckTags(Dictionary<string, int> tags, Dictionary<string, int> requirements,
		string faction)
	{
		foreach (var (key, val) in requirements)
		{
			if (key.StartsWith("@faction:"))
			{
				var requiredFaction = key["@faction:".Length..];
				if (faction != requiredFaction) return false;
			}
			else if (key.StartsWith("@max:"))
			{
				var parts = key["@max:".Length..].Split(':');
				if (parts.Length != 2 || !int.TryParse(parts[1], out var maxVal)) return false;
				if (tags.GetValueOrDefault(parts[0], 0) > maxVal) return false;
			}
			else
			{
				if (tags.GetValueOrDefault(key, 0) < val) return false;
			}
		}
		return true;
	}
}
