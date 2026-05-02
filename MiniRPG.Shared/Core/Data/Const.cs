namespace MiniRPG.Core.Data;

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
	public const string Campfire = "campfire";
	public const string Fire = "fire";
	public const string Ladder = "ladder";
	public const string Item = "item";
	public const string BloodFilth = "blood_filth";
}

/// <summary>地形 StringId 常量，与 terrains.json 保持一致。</summary>
public static class Terrains
{
	public const string Void = "void";
	public const string Air = "air";
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
	public const string Dirt = "dirt";
	public const string Stone = "stone";
	public const string GrassBlock = "grass_block";
	public const string OreCoal = "ore_coal";
	public const string OreIron = "ore_iron";
	public const string OreCopper = "ore_copper";
	public const string OreGold = "ore_gold";
	public const string OreCrystal = "ore_crystal";
}

/// <summary>能力 ID 常量，与 capacities.json 保持一致。</summary>
public static class Caps
{
	public const string Consciousness = "consciousness";
	public const string BloodCirculation = "blood_circulation";
	public const string Moving = "moving";
	public const string Sight = "sight";
	public const string Manipulation = "manipulation";
	public const string Metabolism = "metabolism";
	public const string Talking = "talking";
	public const string Eating = "eating";
	public const string Breathing = "breathing";
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
	public const string Lightning = "lightning";
	public const string Fire = "fire";
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
	public const string Ammo = "ammo";
	public const string Misc = "misc";
}

/// <summary>常用物品 tag 常量。</summary>
public static class ItemTags
{
	public const string Healing = "\u6cbb\u7597";
	public const string Nutrition = "\u9971\u8179";
	public const string Hydration = "\u89e3\u6e34";
	public const string Mood = "\u5fc3\u60c5";
	public const string RestQuality = "\u4f11\u606f\u8d28\u91cf";
	public const string Warmth = "\u4fdd\u6696";
	public const string LegacyWarmth = "\u6dc7\u6fc7\u6ba9";
	/// <summary>
	/// 食物保质期 tag：从 spawn turn 起计算，超过这个回合数进入 Stale，2× 进入 Spoiled，3× 进入 Rotten。
	/// 不配 / &lt;=0 视为永不过期（罐头 / 干粮 / 蜂蜜等）。
	/// </summary>
	public const string FreshnessTurns = "\u4fdd\u8d28\u56de\u5408";
}
