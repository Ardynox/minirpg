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

/// <summary>地形 StringId 常量，与 terrains.json 保持一致。</summary>
public static class Terrains
{
	public const string Void = "void";
	public const string Floor = "floor";
	public const string WallSoil = "wall_soil";
	public const string WallStone = "wall_stone";
	public const string WallGranite = "wall_granite";
	public const string WallObsidian = "wall_obsidian";
	public const string Rubble = "rubble";
	public const string Grass = "grass";
	public const string Water = "water";
	public const string Tree = "tree";
	public const string Lava = "lava";
	public const string Sand = "sand";
	public const string Mountain = "mountain";
}

/// <summary>能力 ID 常量，与 capacities.json 保持一致。</summary>
public static class Caps
{
	public const string Sight = "sight";
	public const string Manipulation = "manipulation";
}

/// <summary>技能 tag 常量。</summary>
public static class SkillTags
{
	public const string Dig = "挖掘";
	public const string Chop = "伐木";
	public const string Mine = "采矿";
}
