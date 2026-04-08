using MiniRPG.Core.Data;
using MiniRPG.Core.Map;
using Xunit;

namespace MiniRPG.Tests;

public sealed class ItemSnapshotMapperTests
{
	public ItemSnapshotMapperTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	private static Item MakeItem(string id = "test_sword", string name = "Test Sword")
	{
		var item = new Item
		{
			Id = id,
			Name = name,
			Price = 50,
			Category = ItemCategories.Weapon,
			SubCategory = "melee",
			Weight = 3.0f,
			MaxStack = 1,
			StackCount = 1,
			AmmoType = "",
			MagazineSize = 0,
			MaxDurability = 100,
			Durability = 80,
			BodyPart = "arm",
			Layer = EquipLayer.Shell,
			SharpDamage = 5,
			BluntDamage = 2,
			GrantedSkills = ["slash", "parry"],
			Tags = new() { ["sharp"] = 1, ["metal"] = 1 },
		};
		item.EnsureRuntimeState();
		return item;
	}

	// ── BuildSnapshot → CreateItem round-trip ────────────

	[Fact]
	public void RoundTrip_PreservesBasicProperties()
	{
		var original = MakeItem();
		var snapshot = ItemSnapshotMapper.BuildSnapshot(original);
		var restored = ItemSnapshotMapper.CreateItem(snapshot);

		Assert.Equal(original.Id, restored.Id);
		Assert.Equal(original.Name, restored.Name);
		Assert.Equal(original.Price, restored.Price);
		Assert.Equal(original.Category, restored.Category);
		Assert.Equal(original.SubCategory, restored.SubCategory);
	}

	[Fact]
	public void RoundTrip_PreservesCombatStats()
	{
		var original = MakeItem();
		var snapshot = ItemSnapshotMapper.BuildSnapshot(original);
		var restored = ItemSnapshotMapper.CreateItem(snapshot);

		Assert.Equal(original.SharpDamage, restored.SharpDamage);
		Assert.Equal(original.BluntDamage, restored.BluntDamage);
		Assert.Equal(original.MaxDurability, restored.MaxDurability);
		Assert.Equal(original.Durability, restored.Durability);
	}

	[Fact]
	public void RoundTrip_PreservesEquipInfo()
	{
		var original = MakeItem();
		var snapshot = ItemSnapshotMapper.BuildSnapshot(original);
		var restored = ItemSnapshotMapper.CreateItem(snapshot);

		Assert.Equal(original.BodyPart, restored.BodyPart);
		Assert.Equal(original.Layer, restored.Layer);
		Assert.Equal(original.Equipped, restored.Equipped);
	}

	[Fact]
	public void RoundTrip_PreservesGrantedSkills()
	{
		var original = MakeItem();
		var snapshot = ItemSnapshotMapper.BuildSnapshot(original);
		var restored = ItemSnapshotMapper.CreateItem(snapshot);

		Assert.Equal(original.GrantedSkills.Count, restored.GrantedSkills.Count);
		Assert.Contains("slash", restored.GrantedSkills);
		Assert.Contains("parry", restored.GrantedSkills);
	}

	[Fact]
	public void RoundTrip_PreservesTags()
	{
		var original = MakeItem();
		var snapshot = ItemSnapshotMapper.BuildSnapshot(original);
		var restored = ItemSnapshotMapper.CreateItem(snapshot);

		Assert.Equal(original.Tags.Count, restored.Tags.Count);
		Assert.Equal(1, restored.Tags["sharp"]);
		Assert.Equal(1, restored.Tags["metal"]);
	}

	[Fact]
	public void RoundTrip_PreservesAmmoInfo()
	{
		var item = new Item
		{
			Id = "pistol",
			Name = "Pistol",
			AmmoType = "bullet",
			MagazineSize = 6,
			LoadedAmmo = 4,
		};
		item.EnsureRuntimeState();

		var snapshot = ItemSnapshotMapper.BuildSnapshot(item);
		var restored = ItemSnapshotMapper.CreateItem(snapshot);

		Assert.Equal("bullet", restored.AmmoType);
		Assert.Equal(6, restored.MagazineSize);
		Assert.Equal(4, restored.LoadedAmmo);
	}

	[Fact]
	public void RoundTrip_PreservesStackInfo()
	{
		var item = new Item
		{
			Id = "arrow",
			Name = "Arrow",
			MaxStack = 50,
			StackCount = 25,
		};
		item.EnsureRuntimeState();

		var snapshot = ItemSnapshotMapper.BuildSnapshot(item);
		var restored = ItemSnapshotMapper.CreateItem(snapshot);

		Assert.Equal(50, restored.MaxStack);
		Assert.Equal(25, restored.StackCount);
	}

	[Fact]
	public void RoundTrip_PreservesInsulationStats()
	{
		var item = new Item
		{
			Id = "coat",
			Name = "Coat",
			ColdInsulation = 5.0f,
			HeatInsulation = 2.0f,
			Waterproofing = 0.8f,
		};
		item.EnsureRuntimeState();

		var snapshot = ItemSnapshotMapper.BuildSnapshot(item);
		var restored = ItemSnapshotMapper.CreateItem(snapshot);

		Assert.Equal(5.0f, restored.ColdInsulation);
		Assert.Equal(2.0f, restored.HeatInsulation);
		Assert.Equal(0.8f, restored.Waterproofing);
	}

	[Fact]
	public void RoundTrip_PreservesInstanceId()
	{
		var original = MakeItem();
		var snapshot = ItemSnapshotMapper.BuildSnapshot(original);
		var restored = ItemSnapshotMapper.CreateItem(snapshot);

		Assert.Equal(original.InstanceId, restored.InstanceId);
	}

	// ── BuildSnapshot ────────────────────────────────────

	[Fact]
	public void BuildSnapshot_CopiesContents()
	{
		var container = MakeItem("bag", "Bag");
		var inner = new Item { Id = "gem", Name = "Gem" };
		inner.EnsureRuntimeState();
		container.Contents = [inner];

		var snapshot = ItemSnapshotMapper.BuildSnapshot(container);
		Assert.NotNull(snapshot.Contents);
		Assert.Single(snapshot.Contents!);
		Assert.Equal("gem", snapshot.Contents![0].Id);
	}

	[Fact]
	public void BuildSnapshot_NullContents_StaysNull()
	{
		var item = MakeItem();
		item.Contents = null;
		var snapshot = ItemSnapshotMapper.BuildSnapshot(item);
		Assert.Null(snapshot.Contents);
	}

	// ── Serialize / Deserialize ──────────────────────────

	[Fact]
	public void Serialize_Deserialize_RoundTrip()
	{
		var original = MakeItem();
		var json = ItemSnapshotMapper.Serialize(original);
		var deserialized = ItemSnapshotMapper.Deserialize(json);

		Assert.NotNull(deserialized);
		Assert.Equal(original.Id, deserialized!.Id);
		Assert.Equal(original.Name, deserialized.Name);
		Assert.Equal(original.Price, deserialized.Price);
	}

	[Fact]
	public void Deserialize_InvalidJson_ReturnsNull()
	{
		var result = ItemSnapshotMapper.Deserialize("not valid json");
		Assert.Null(result);
	}

	[Fact]
	public void Deserialize_EmptyString_ReturnsNull()
	{
		var result = ItemSnapshotMapper.Deserialize("");
		Assert.Null(result);
	}
}
