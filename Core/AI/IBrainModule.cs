using System;
using System.Collections.Generic;

namespace MiniRPG.Core.AI;

public enum DecisionType
{
	Idle,
	Wander,
	MoveTo,
	Attack,
	Flee,
	Interact,
	UseItem,
}

public class Decision
{
	public DecisionType Type { get; set; } = DecisionType.Idle;
	public (int X, int Y)? TargetPos { get; set; }
	public string? TargetActorId { get; set; }
	public string? ActionDefId { get; set; }
	public string? TargetLimbId { get; set; }
}

public enum SimDetail
{
	Full,
	Simplified,
	Summary,
}

public class Perception
{
	public required Actor Self { get; init; }
	public List<Actor> NearbyActors { get; init; } = [];
	public Dictionary<(int X, int Y), bool> NearbyWalkable { get; init; } = [];
	public Dictionary<(int X, int Y), string> NearbyFixtures { get; init; } = [];
	public int Turn { get; init; }
	public int Floor { get; init; }

	/// <summary>GameState 引用，供寻路等需要全局地图信息的功能使用。</summary>
	public GameState? State { get; init; }
}

/// <summary>
/// 大脑模块接口：给定感知，返回决策。
/// 纯函数，无副作用，无状态（状态存在 Actor 上）。
/// </summary>
public interface IBrainModule
{
	Decision Decide(Perception perception, Random rng);
}

/// <summary>
/// 阵营敌对关系（全局规则，所有大脑共用）。
/// hostile ↔ player   互相敌对
/// hostile ↔ friendly 互相敌对
/// friendly ↔ player  友好
/// 同阵营不攻击
/// </summary>
public static class FactionRelation
{
	public static bool IsHostile(string a, string b)
	{
		if (a == b) return false;
		return (a, b) switch
		{
			(Factions.Hostile, Factions.Player) or (Factions.Player, Factions.Hostile) => true,
			(Factions.Hostile, Factions.Friendly) or (Factions.Friendly, Factions.Hostile) => true,
			_ => false,
		};
	}
}
