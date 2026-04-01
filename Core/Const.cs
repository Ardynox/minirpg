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
	public const string Swamp = "swamp";
	public const string Snow = "snow";
	public const string Ice = "ice";
	public const string Marsh = "marsh";
	public const string Gravel = "gravel";
	public const string WallIron = "wall_iron";
	public const string Fungus = "fungus";
	public const string CrystalVein = "crystal_vein";
}

/// <summary>能力 ID 常量，与 capacities.json 保持一致。</summary>
public static class Caps
{
	public const string Sight = "sight";
	public const string Manipulation = "manipulation";
	public const string Hearing = "hearing";
}

/// <summary>技能 tag 常量。</summary>
public static class SkillTags
{
	public const string Dig = "挖掘";
	public const string Chop = "伐木";
	public const string Mine = "采矿";
}

/// <summary>身体部位常量。肢体和装备共用。</summary>
public static class BodyParts
{
	public const string Head = "head";
	public const string Torso = "torso";
	public const string Arm = "arm";
	public const string Hand = "hand";
	public const string Leg = "leg";
	public const string Foot = "foot";
}

/// <summary>
/// 装备层级（环世界四层）。数值越大越外层。
/// Skin=贴身层, Middle=中间层, Shell=外壳层, Overhead=最外层（头盔/腰带/手持武器）。
/// </summary>
public enum EquipLayer { Skin = 0, Middle = 1, Shell = 2, Overhead = 3 }

/// <summary>伤害类型常量。</summary>
public static class DamageTypes
{
	public const string Sharp = "sharp";
	public const string Blunt = "blunt";
	public const string Poison = "poison";
}

/// <summary>物品大类常量。</summary>
public static class ItemCategories
{
	public const string Weapon = "weapon";
	public const string Armor = "armor";
	public const string Clothing = "clothing";
	public const string Consumable = "consumable";
	public const string Tool = "tool";
	public const string Material = "material";
	public const string Food = "food";
	public const string Misc = "misc";
}
