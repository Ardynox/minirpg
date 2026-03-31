using System.Collections.Generic;

namespace MiniRPG.Core;

/// <summary>
/// 物品：可交易、可持有、可装备。
/// 装备后作为 ITagSource 参与 Actor 的 Tag 聚合。
/// </summary>
public class Item : ITagSource
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public int Price { get; set; }
	public bool Equipped { get; set; }

	/// <summary>大类：weapon/armor/clothing/consumable/tool/material/food/misc。</summary>
	public string Category { get; set; } = ItemCategories.Misc;

	/// <summary>重量（kg）。0 = 从 ItemCategoryDef.DefaultWeight 取。</summary>
	public float Weight { get; set; }

	/// <summary>装备位置（身体部位 ID）。空 = 不可装备。</summary>
	public string BodyPart { get; set; } = "";

	/// <summary>装备到哪一层。</summary>
	public EquipLayer Layer { get; set; }

	/// <summary>护甲覆盖的身体部位列表（如板甲覆盖 torso+arm）。</summary>
	public List<string> CoveredParts { get; set; } = [];

	/// <summary>锐伤抗性（护甲）。</summary>
	public float SharpArmor { get; set; }
	/// <summary>钝伤抗性（护甲）。</summary>
	public float BluntArmor { get; set; }

	/// <summary>锐伤基础值（武器）。</summary>
	public float SharpDamage { get; set; }
	/// <summary>钝伤基础值（武器）。</summary>
	public float BluntDamage { get; set; }

	/// <summary>装备后解锁的交互 ID 列表（如剑解锁 sword_slash, sword_pommel）。</summary>
	public List<string> GrantedSkills { get; set; } = [];

	/// <summary>容器内容物（仅容器物品有值）。</summary>
	public List<Item>? Contents { get; set; }

	public Dictionary<string, int> Tags { get; set; } = new();

	public Dictionary<string, int> GetTags() => Tags;

	/// <summary>是否为容器物品。</summary>
	public bool IsContainer => Contents != null;

	/// <summary>有效重量：自身重量 + 内容物总重（容器）。</summary>
	public float EffectiveWeight
	{
		get
		{
			var self = Weight > 0 ? Weight : ItemCategoryDef.GetDefaultWeight(Category);
			if (Contents != null)
				foreach (var c in Contents)
					self += c.EffectiveWeight;
			return self;
		}
	}

	/// <summary>是否可装备（有装备位置定义）。</summary>
	public bool IsEquippable => !string.IsNullOrEmpty(BodyPart);
}

/// <summary>
/// 商店货架槽位：一个物品 + 库存数量。
/// </summary>
public class ShopSlot
{
	public Item Item { get; set; } = new();
	public int Stock { get; set; } = 1;
}

/// <summary>
/// 物品大类定义：提供默认重量等属性。
/// </summary>
public class ItemCategoryDef
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public float DefaultWeight { get; set; } = 1.0f;

	private static readonly Dictionary<string, ItemCategoryDef> _registry = new();

	public static void Register(ItemCategoryDef def) => _registry[def.Id] = def;
	public static ItemCategoryDef? Get(string id) => _registry.GetValueOrDefault(id);
	public static float GetDefaultWeight(string category) => _registry.TryGetValue(category, out var def) ? def.DefaultWeight : 1.0f;
	public static IReadOnlyDictionary<string, ItemCategoryDef> All => _registry;

	public static void Clear() => _registry.Clear();
}
