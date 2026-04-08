using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Data;
using Xunit;

namespace MiniRPG.Tests;

public sealed class TagSystemTests
{
	// ── Limb ─────────────────────────────────────────────

	[Fact]
	public void Limb_GetTags_ReturnsTags()
	{
		var limb = new Limb { Tags = new() { ["vital"] = 1, ["organic"] = 1 } };
		var tags = limb.GetTags();
		Assert.Equal(2, tags.Count);
		Assert.Equal(1, tags["vital"]);
		Assert.Equal(1, tags["organic"]);
	}

	[Fact]
	public void Limb_GetTags_EmptyByDefault()
	{
		var limb = new Limb();
		Assert.Empty(limb.GetTags());
	}

	[Fact]
	public void Limb_InitEquipSlots_GeneratesFromLayers()
	{
		var limb = new Limb
		{
			Id = "left_arm",
			BodyPart = "arm",
			EquipLayers = [EquipLayer.Skin, EquipLayer.Middle, EquipLayer.Shell],
		};

		limb.InitEquipSlots();

		Assert.Equal(3, limb.EquipSlots.Count);
		Assert.All(limb.EquipSlots, s =>
		{
			Assert.Equal("left_arm", s.LimbId);
			Assert.Equal("arm", s.BodyPart);
		});
		Assert.Equal(EquipLayer.Skin, limb.EquipSlots[0].Layer);
		Assert.Equal(EquipLayer.Middle, limb.EquipSlots[1].Layer);
		Assert.Equal(EquipLayer.Shell, limb.EquipSlots[2].Layer);
	}

	[Fact]
	public void Limb_InitEquipSlots_NoopWhenAlreadyPopulated()
	{
		var limb = new Limb
		{
			Id = "arm",
			BodyPart = "arm",
			EquipLayers = [EquipLayer.Skin],
			EquipSlots = [new EquipSlot { LimbId = "arm", BodyPart = "arm", Layer = EquipLayer.Shell }],
		};

		limb.InitEquipSlots();

		// Should not overwrite existing slots
		Assert.Single(limb.EquipSlots);
		Assert.Equal(EquipLayer.Shell, limb.EquipSlots[0].Layer);
	}

	[Fact]
	public void Limb_InitEquipSlots_NoopWhenNoLayers()
	{
		var limb = new Limb { Id = "eye", BodyPart = "head", EquipLayers = [] };
		limb.InitEquipSlots();
		Assert.Empty(limb.EquipSlots);
	}

	[Fact]
	public void Limb_DefaultValues()
	{
		var limb = new Limb();
		Assert.Equal("", limb.Id);
		Assert.Equal(5, limb.MaxDurability);
		Assert.Equal(5, limb.Durability);
		Assert.Equal(0, limb.PermanentDamage);
		Assert.Equal("flesh", limb.Material);
	}

	// ── Race ─────────────────────────────────────────────

	[Fact]
	public void Race_GetTags_ReturnsTags()
	{
		var race = new Race { Tags = new() { ["humanoid"] = 1 } };
		Assert.Equal(1, race.GetTags()["humanoid"]);
	}

	[Fact]
	public void Race_DefaultProperties()
	{
		var race = new Race();
		Assert.Equal("", race.Id);
		Assert.Equal("", race.NeedProfileId);
		Assert.Equal("", race.HealthProfileId);
		Assert.Empty(race.GetTags());
	}

	// ── Profession ───────────────────────────────────────

	[Fact]
	public void Profession_GetTags_ReturnsTags()
	{
		var prof = new Profession { Tags = new() { ["melee_bonus"] = 2 } };
		Assert.Equal(2, prof.GetTags()["melee_bonus"]);
	}

	// ── Buff ─────────────────────────────────────────────

	[Fact]
	public void Buff_GetTags_ReturnsTags()
	{
		var buff = new Buff
		{
			Id = "rage",
			RemainingTurns = 3,
			Tags = new() { ["strength"] = 5 },
		};
		Assert.Equal(5, buff.GetTags()["strength"]);
		Assert.Equal(3, buff.RemainingTurns);
	}

	[Fact]
	public void Buff_DefaultRemainingTurns_IsNegativeOne()
	{
		var buff = new Buff();
		Assert.Equal(-1, buff.RemainingTurns);
	}

	// ── Experience ───────────────────────────────────────

	[Fact]
	public void Experience_GetTags_ReturnsTags()
	{
		var exp = new Experience { Tags = new() { ["veteran"] = 1 } };
		Assert.Equal(1, exp.GetTags()["veteran"]);
	}

	// ── JSON serialization round-trip ────────────────────

	[Fact]
	public void Limb_JsonRoundTrip()
	{
		var limb = new Limb
		{
			Id = "arm",
			Name = "Left Arm",
			MaxDurability = 10,
			Durability = 8,
			Material = "bone",
			BodyPart = "arm",
			Tags = new() { ["organic"] = 1 },
			Capacities = new() { ["manipulation"] = 0.5f },
		};

		var json = JsonSerializer.Serialize(limb);
		var deserialized = JsonSerializer.Deserialize<Limb>(json);

		Assert.NotNull(deserialized);
		Assert.Equal("arm", deserialized!.Id);
		Assert.Equal("Left Arm", deserialized.Name);
		Assert.Equal(10, deserialized.MaxDurability);
		Assert.Equal(8, deserialized.Durability);
		Assert.Equal("bone", deserialized.Material);
		Assert.Equal(1, deserialized.GetTags()["organic"]);
	}

	[Fact]
	public void Race_JsonRoundTrip()
	{
		var race = new Race
		{
			Id = "human",
			Name = "Human",
			NeedProfileId = "standard",
			HealthProfileId = "humanoid",
			Tags = new() { ["humanoid"] = 1 },
		};

		var json = JsonSerializer.Serialize(race);
		var deserialized = JsonSerializer.Deserialize<Race>(json);

		Assert.NotNull(deserialized);
		Assert.Equal("human", deserialized!.Id);
		Assert.Equal("standard", deserialized.NeedProfileId);
		Assert.Equal(1, deserialized.GetTags()["humanoid"]);
	}

	[Fact]
	public void Buff_JsonRoundTrip()
	{
		var buff = new Buff
		{
			Id = "shield",
			RemainingTurns = 5,
			Tags = new() { ["defense"] = 3 },
		};

		var json = JsonSerializer.Serialize(buff);
		var deserialized = JsonSerializer.Deserialize<Buff>(json);

		Assert.NotNull(deserialized);
		Assert.Equal(5, deserialized!.RemainingTurns);
		Assert.Equal(3, deserialized.GetTags()["defense"]);
	}

	[Fact]
	public void Profession_JsonRoundTrip()
	{
		var prof = new Profession
		{
			Id = "warrior",
			Name = "Warrior",
			Tags = new() { ["melee"] = 2 },
		};

		var json = JsonSerializer.Serialize(prof);
		var deserialized = JsonSerializer.Deserialize<Profession>(json);

		Assert.NotNull(deserialized);
		Assert.Equal("warrior", deserialized!.Id);
		Assert.Equal(2, deserialized.GetTags()["melee"]);
	}

	[Fact]
	public void Experience_JsonRoundTrip()
	{
		var exp = new Experience
		{
			Id = "first_kill",
			Tags = new() { ["killer"] = 1 },
		};

		var json = JsonSerializer.Serialize(exp);
		var deserialized = JsonSerializer.Deserialize<Experience>(json);

		Assert.NotNull(deserialized);
		Assert.Equal("first_kill", deserialized!.Id);
		Assert.Equal(1, deserialized.GetTags()["killer"]);
	}

	// ── EquipSlot ────────────────────────────────────────

	[Fact]
	public void EquipSlot_DefaultItemId_IsNull()
	{
		var slot = new EquipSlot();
		Assert.Null(slot.ItemId);
	}
}
