using System.Collections.Generic;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using Xunit;

namespace MiniRPG.Tests;

public sealed class SurgeryModuleTests
{
	public SurgeryModuleTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	// ── IsCorpseItem ─────────────────────────────────────

	[Fact]
	public void IsCorpseItem_NullItem_ReturnsFalse()
	{
		Assert.False(SurgeryModule.IsCorpseItem(null));
	}

	[Fact]
	public void IsCorpseItem_NoCorpse_ReturnsFalse()
	{
		var item = new Item { Id = "sword" };
		Assert.False(SurgeryModule.IsCorpseItem(item));
	}

	[Fact]
	public void IsCorpseItem_WithCorpse_ReturnsTrue()
	{
		var item = new Item
		{
			Id = "corpse_rat",
			Corpse = new ItemCorpseMetadata { SourceActorTemplateId = "rat" },
		};
		Assert.True(SurgeryModule.IsCorpseItem(item));
	}

	// ── GetCorpseHarvestableLimbIds ──────────────────────

	[Fact]
	public void GetCorpseHarvestableLimbIds_NoCorpse_ReturnsEmpty()
	{
		var item = new Item { Id = "sword" };
		Assert.Empty(SurgeryModule.GetCorpseHarvestableLimbIds(item));
	}

	[Fact]
	public void GetCorpseHarvestableLimbIds_ButcheredCorpse_ReturnsEmpty()
	{
		var item = new Item
		{
			Id = "corpse",
			Corpse = new ItemCorpseMetadata { SourceActorTemplateId = "rat", Butchered = true },
		};
		Assert.Empty(SurgeryModule.GetCorpseHarvestableLimbIds(item));
	}

	[Fact]
	public void GetCorpseHarvestableLimbIds_ValidCorpse_ReturnsSorted()
	{
		var item = new Item
		{
			Id = "corpse",
			Corpse = new ItemCorpseMetadata
			{
				SourceActorTemplateId = "rat",
				RemainingLimbIds = ["left_arm", "right_arm", "head", "torso"],
			},
		};
		var ids = SurgeryModule.GetCorpseHarvestableLimbIds(item);
		for (int i = 1; i < ids.Count; i++)
			Assert.True(string.Compare(ids[i - 1], ids[i], System.StringComparison.Ordinal) <= 0);
	}

	[Fact]
	public void GetCorpseHarvestableLimbIds_EmptyLimbs_ReturnsEmpty()
	{
		var item = new Item
		{
			Id = "corpse",
			Corpse = new ItemCorpseMetadata { SourceActorTemplateId = "rat", RemainingLimbIds = [] },
		};
		Assert.Empty(SurgeryModule.GetCorpseHarvestableLimbIds(item));
	}

	// ── FindBestInstallItem ──────────────────────────────

	[Fact]
	public void FindBestInstallItem_NoItems_ReturnsNull()
	{
		var surgeon = PresetDB.SpawnActor("player", "surgeon");
		surgeon.Inventory.Clear();
		Assert.Null(SurgeryModule.FindBestInstallItem(surgeon, "left_arm"));
	}

	// ── CreateSeveredLimbItem ────────────────────────────

	[Fact]
	public void CreateSeveredLimbItem_UnknownLimb_ReturnsNull()
	{
		Assert.Null(SurgeryModule.CreateSeveredLimbItem("nonexistent_limb_xyz"));
	}

	// ── GetLiveOperationLimbIds ──────────────────────────

	[Fact]
	public void GetLiveOperationLimbIds_NoMatchingOps_ReturnsEmpty()
	{
		var surgeon = PresetDB.SpawnActor("player", "surgeon");
		surgeon.Inventory.Clear();
		var target = PresetDB.SpawnActor("player", "target");
		target.Limbs.Clear();
		Assert.Empty(SurgeryModule.GetLiveOperationLimbIds(surgeon, target));
	}

	// ── TrySpawnCorpseOnDeath ────────────────────────────

	[Fact]
	public void TrySpawnCorpseOnDeath_CreatesCorpseItem()
	{
		var (state, _, enemy) = SkillCastingTestHelper.CreateCombatState();
		var events = new List<GameEvent>();
		SurgeryModule.TrySpawnCorpseOnDeath(state, enemy, events, "death_instant");
		Assert.Contains(events, e => e.Type == "corpse_spawned");
	}
}
