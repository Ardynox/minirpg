using System;
using System.Collections.Generic;

namespace MiniRPG.Core;

/// <summary>
/// 生物模板注册表：每种生物定义一次工厂函数，
/// 运行时通过 Spawn 创建实例。新增类型只需加一行 Register。
/// </summary>
public static class ActorTemplates
{
	private static readonly Dictionary<string, Func<string, Actor>> Registry = new();

	static ActorTemplates()
	{
		Register("player", id => new Actor
		{
			Id = id, Glyph = "P", DisplayName = "你", Faction = "friendly",
			Gold = 50,
			Race = new Race
			{
				Id = "human", Name = "人类",
				Tags = new() { ["力量"] = 5, ["防御"] = 2 },
			},
			Limbs =
			[
				new Limb { Id = "torso",     Name = "躯干", MaxDurability = 15, Durability = 15, Tags = new() { ["要害"] = 1, ["防御"] = 2 } },
				new Limb { Id = "right_arm", Name = "右臂", MaxDurability = 8,  Durability = 8,  Tags = new() { ["近战"] = 3, ["力量"] = 2 } },
				new Limb { Id = "left_arm",  Name = "左臂", MaxDurability = 8,  Durability = 8,  Tags = new() { ["近战"] = 1, ["格挡"] = 2 } },
				new Limb { Id = "legs",      Name = "双腿", MaxDurability = 10, Durability = 10, Tags = new() { ["移动"] = 1, ["速度"] = 2 } },
				new Limb { Id = "eyes",      Name = "双眼", MaxDurability = 3,  Durability = 3,  Tags = new() { ["视觉"] = 3 } },
			],
		});

		Register("goblin", id => new Actor
		{
			Id = id, Glyph = "G", DisplayName = "哥布林", Faction = "hostile",
			Gold = 5,
			Race = new Race
			{
				Id = "goblin", Name = "哥布林",
				Tags = new() { ["力量"] = 2, ["速度"] = 3 },
			},
			Limbs =
			[
				new Limb { Id = "body",  Name = "身体", MaxDurability = 6,  Durability = 6,  Tags = new() { ["要害"] = 1, ["防御"] = 1 } },
				new Limb { Id = "claws", Name = "利爪", MaxDurability = 4,  Durability = 4,  Tags = new() { ["近战"] = 2, ["力量"] = 1 } },
				new Limb { Id = "legs",  Name = "短腿", MaxDurability = 5,  Durability = 5,  Tags = new() { ["移动"] = 1, ["速度"] = 1 } },
				new Limb { Id = "eyes",  Name = "夜眼", MaxDurability = 2,  Durability = 2,  Tags = new() { ["视觉"] = 2, ["夜视"] = 1 } },
			],
		});

		Register("slime", id => new Actor
		{
			Id = id, Glyph = "S", DisplayName = "史莱姆", Faction = "hostile",
			Gold = 3,
			Race = new Race
			{
				Id = "slime", Name = "史莱姆",
				Tags = new() { ["防御"] = 3, ["毒性"] = 2 },
			},
			Limbs =
			[
				new Limb { Id = "body", Name = "弹性体", MaxDurability = 8, Durability = 8, Tags = new() { ["近战"] = 1, ["防御"] = 2 } },
				new Limb { Id = "core", Name = "核心",   MaxDurability = 5, Durability = 5, Tags = new() { ["要害"] = 1, ["移动"] = 1, ["毒性"] = 1 } },
			],
		});

		Register("skeleton", id => new Actor
		{
			Id = id, Glyph = "K", DisplayName = "骷髅", Faction = "hostile",
			Gold = 8,
			Race = new Race
			{
				Id = "undead", Name = "亡灵",
				Tags = new() { ["力量"] = 4, ["防御"] = 1, ["亡灵"] = 1 },
			},
			Limbs =
			[
				new Limb { Id = "skull",      Name = "头骨",   MaxDurability = 4, Durability = 4, Tags = new() { ["要害"] = 1, ["视觉"] = 1, ["亡灵"] = 1 } },
				new Limb { Id = "bone_arm_r", Name = "骨臂(右)", MaxDurability = 5, Durability = 5, Tags = new() { ["近战"] = 3, ["力量"] = 2 } },
				new Limb { Id = "bone_arm_l", Name = "骨臂(左)", MaxDurability = 5, Durability = 5, Tags = new() { ["近战"] = 1, ["格挡"] = 1 } },
				new Limb { Id = "bone_legs",  Name = "骨腿",   MaxDurability = 5, Durability = 5, Tags = new() { ["移动"] = 1, ["速度"] = 1 } },
			],
		});

		RegisterNPCs();
	}

	public static void Register(string templateId, Func<string, Actor> factory) =>
		Registry[templateId] = factory;

	public static Actor Spawn(string templateId, string instanceId)
	{
		if (!Registry.TryGetValue(templateId, out var factory))
			throw new ArgumentException($"Unknown actor template: {templateId}");
		return factory(instanceId);
	}

	static void RegisterNPCs()
	{
		Register("merchant", id => new Actor
		{
			Id = id, Glyph = "T", DisplayName = "流浪商人", Faction = "friendly",
			Gold = 200,
			Race = new Race
			{
				Id = "human", Name = "人类",
				Tags = new() { ["生命"] = 15, ["力量"] = 2, ["防御"] = 1 },
			},
			Profession = new Profession
			{
				Id = "merchant", Name = "商人",
				Tags = new() { ["交易"] = 3, ["视觉"] = 2 },
			},
			Limbs =
			[
				new Limb { Id = "torso", Name = "躯干", MaxDurability = 12, Durability = 12, Tags = new() { ["要害"] = 1 } },
				new Limb { Id = "arms",  Name = "双臂", MaxDurability = 6,  Durability = 6,  Tags = new() { ["近战"] = 1 } },
				new Limb { Id = "legs",  Name = "双腿", MaxDurability = 8,  Durability = 8,  Tags = new() { ["移动"] = 1, ["速度"] = 1 } },
				new Limb { Id = "eyes",  Name = "双眼", MaxDurability = 3,  Durability = 3,  Tags = new() { ["视觉"] = 2 } },
			],
			ShopSlots =
			[
				new ShopSlot
				{
					Stock = 3,
					Item = new Item { Id = "potion_hp", Name = "生命药水", Price = 10, Tags = new() { ["治疗"] = 5 } },
				},
				new ShopSlot
				{
					Stock = 2,
					Item = new Item { Id = "potion_str", Name = "力量药水", Price = 15, Tags = new() { ["力量"] = 3 } },
				},
				new ShopSlot
				{
					Stock = 1,
					Item = new Item { Id = "shield_iron", Name = "铁盾", Price = 30, Tags = new() { ["防御"] = 4, ["格挡"] = 2 } },
				},
				new ShopSlot
				{
					Stock = 1,
					Item = new Item { Id = "sword_steel", Name = "钢剑", Price = 40, Tags = new() { ["近战"] = 5, ["力量"] = 3 } },
				},
				new ShopSlot
				{
					Stock = 5,
					Item = new Item { Id = "torch", Name = "火把", Price = 5, Tags = new() { ["视觉"] = 2 } },
				},
			],
		});

		Register("elder", id => new Actor
		{
			Id = id, Glyph = "E", DisplayName = "村长", Faction = "friendly",
			Race = new Race
			{
				Id = "human", Name = "人类",
				Tags = new() { ["力量"] = 1, ["防御"] = 1 },
			},
			Profession = new Profession
			{
				Id = "elder", Name = "村长",
				Tags = new() { ["智慧"] = 3, ["视觉"] = 1 },
			},
			Limbs =
			[
				new Limb { Id = "torso", Name = "躯干", MaxDurability = 10, Durability = 10, Tags = new() { ["要害"] = 1 } },
				new Limb { Id = "arms",  Name = "双臂", MaxDurability = 5,  Durability = 5,  Tags = new() { ["近战"] = 1 } },
				new Limb { Id = "legs",  Name = "双腿", MaxDurability = 6,  Durability = 6,  Tags = new() { ["移动"] = 1 } },
				new Limb { Id = "eyes",  Name = "双眼", MaxDurability = 3,  Durability = 3,  Tags = new() { ["视觉"] = 2 } },
			],
		});

		Register("villager", id => new Actor
		{
			Id = id, Glyph = "V", DisplayName = "村民", Faction = "friendly",
			Race = new Race
			{
				Id = "human", Name = "人类",
				Tags = new() { ["力量"] = 2 },
			},
			Limbs =
			[
				new Limb { Id = "torso", Name = "躯干", MaxDurability = 8, Durability = 8, Tags = new() { ["要害"] = 1 } },
				new Limb { Id = "arms",  Name = "双臂", MaxDurability = 5, Durability = 5, Tags = new() { ["近战"] = 1 } },
				new Limb { Id = "legs",  Name = "双腿", MaxDurability = 6, Durability = 6, Tags = new() { ["移动"] = 1 } },
				new Limb { Id = "eyes",  Name = "双眼", MaxDurability = 3, Durability = 3, Tags = new() { ["视觉"] = 2 } },
			],
		});
	}

	public static IReadOnlyCollection<string> MonsterIds { get; } =
		new[] { "goblin", "slime", "skeleton" };
}
