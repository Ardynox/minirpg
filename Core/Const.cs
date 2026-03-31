namespace MiniRPG.Core;

/// <summary>阵营常量。</summary>
public static class Factions
{
	public const string Player = "player";
	public const string Hostile = "hostile";
	public const string Friendly = "friendly";
}

/// <summary>CellEntity.EntityId 常量（设施类）+ 旧兼容地形 ID。</summary>
public static class Entities
{
	public const string Wall = "wall";
	public const string Floor = "floor";
	public const string Rubble = "rubble";

	public const string StairDown = "stair_down";
	public const string StairUp = "stair_up";
	public const string Nest = "nest";
	public const string Door = "door";
	public const string House = "house";
	public const string Item = "item";
}
