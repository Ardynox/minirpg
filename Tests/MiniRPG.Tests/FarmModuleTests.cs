using MiniRPG.Core;
using MiniRPG.Core.Data;
using MiniRPG.Core.Farm;
using Xunit;

namespace MiniRPG.Tests;

public class FarmModuleTests
{
	private static GameState CreateState(int turn = 0)
	{
		var state = new GameState { PlayerId = "player" };
		state.Turn = turn;
		state.Actors["player"] = new Actor { Id = "player", X = 5, Y = 5 };
		return state;
	}

	private static void RegisterTestCrop()
	{
		CropRegistry.Clear();
		CropRegistry.Register(new CropDef
		{
			Id = "wheat",
			Name = "小麦",
			GrowthTurns = 10,
			HarvestItemId = "wheat_grain",
			HarvestMin = 2,
			HarvestMax = 4,
			SeedItemId = "wheat_seed",
		});
	}

	[Fact]
	public void Plant_CreatesNewCrop()
	{
		RegisterTestCrop();
		var state = CreateState();

		var result = FarmModule.Plant(state, "wheat", 3, 3, 0);
		Assert.True(result.Success);
		Assert.NotEmpty(result.Events);

		var crop = FarmModule.GetCropAt(state, 3, 3, 0);
		Assert.NotNull(crop);
		Assert.Equal("wheat", crop.CropDefId);
		Assert.Equal(0, crop.Growth);
		Assert.False(crop.Mature);
	}

	[Fact]
	public void Plant_FailsOnOccupiedCell()
	{
		RegisterTestCrop();
		var state = CreateState();

		FarmModule.Plant(state, "wheat", 3, 3, 0);
		var result = FarmModule.Plant(state, "wheat", 3, 3, 0);
		Assert.False(result.Success);
		Assert.Equal("cell_occupied", result.FailureReason);
	}

	[Fact]
	public void Plant_FailsForUnknownCrop()
	{
		CropRegistry.Clear();
		var state = CreateState();

		var result = FarmModule.Plant(state, "nonexistent", 3, 3, 0);
		Assert.False(result.Success);
		Assert.Equal("unknown_crop", result.FailureReason);
	}

	[Fact]
	public void TickGrowth_AdvancesGrowth()
	{
		RegisterTestCrop();
		var state = CreateState();
		FarmModule.Plant(state, "wheat", 3, 3, 0);

		for (var i = 0; i < 5; i++)
			FarmModule.TickGrowth(state);

		var crop = FarmModule.GetCropAt(state, 3, 3, 0);
		Assert.NotNull(crop);
		Assert.Equal(5, crop.Growth);
		Assert.False(crop.Mature);
	}

	[Fact]
	public void TickGrowth_MaturesAtFullGrowth()
	{
		RegisterTestCrop();
		var state = CreateState();
		FarmModule.Plant(state, "wheat", 3, 3, 0);

		for (var i = 0; i < 10; i++)
		{
			var events = FarmModule.TickGrowth(state);
			if (i == 9)
			{
				Assert.Contains(events, e => e.Type == "crop_mature");
			}
		}

		var crop = FarmModule.GetCropAt(state, 3, 3, 0);
		Assert.NotNull(crop);
		Assert.True(crop.Mature);
	}

	[Fact]
	public void TickGrowth_StopsAfterMature()
	{
		RegisterTestCrop();
		var state = CreateState();
		FarmModule.Plant(state, "wheat", 3, 3, 0);

		for (var i = 0; i < 15; i++)
			FarmModule.TickGrowth(state);

		var crop = FarmModule.GetCropAt(state, 3, 3, 0);
		Assert.NotNull(crop);
		Assert.Equal(10, crop.Growth); // 不超过 GrowthTurns
	}

	[Fact]
	public void Harvest_FailsWhenNotMature()
	{
		RegisterTestCrop();
		var state = CreateState();
		FarmModule.Plant(state, "wheat", 3, 3, 0);

		var result = FarmModule.Harvest(state, 3, 3, 0);
		Assert.False(result.Success);
		Assert.Equal("not_mature", result.FailureReason);
	}

	[Fact]
	public void Harvest_RemovesCropAndProducesItems()
	{
		RegisterTestCrop();
		PresetDB.Items["wheat_grain"] = new ItemPreset { Id = "wheat_grain", Name = "小麦粒", Category = "food" };

		var state = CreateState();
		var harvester = state.Actors["player"];
		FarmModule.Plant(state, "wheat", 3, 3, 0);

		// 快速成熟
		for (var i = 0; i < 10; i++)
			FarmModule.TickGrowth(state);

		var result = FarmModule.Harvest(state, 3, 3, 0, harvester);
		Assert.True(result.Success);
		Assert.Contains(result.Events, e => e.Type == "crop_harvested");

		// 作物已移除
		Assert.Null(FarmModule.GetCropAt(state, 3, 3, 0));

		// 收获物品在背包中
		Assert.True(harvester.Inventory.Count >= 2);
	}

	[Fact]
	public void Harvest_FailsWhenNoCrop()
	{
		var state = CreateState();
		var result = FarmModule.Harvest(state, 3, 3, 0);
		Assert.False(result.Success);
		Assert.Equal("no_crop", result.FailureReason);
	}

	[Fact]
	public void Remove_DeletesCrop()
	{
		RegisterTestCrop();
		var state = CreateState();
		FarmModule.Plant(state, "wheat", 3, 3, 0);

		Assert.True(FarmModule.Remove(state, 3, 3, 0));
		Assert.Null(FarmModule.GetCropAt(state, 3, 3, 0));
	}

	[Fact]
	public void GetMatureCrops_ReturnsOnlyMature()
	{
		RegisterTestCrop();
		var state = CreateState();
		FarmModule.Plant(state, "wheat", 1, 1, 0);
		FarmModule.Plant(state, "wheat", 2, 2, 0);

		// 只让第一个成熟
		var crop1 = FarmModule.GetCropAt(state, 1, 1, 0)!;
		crop1.Growth = 10;
		crop1.Mature = true;

		var mature = FarmModule.GetMatureCrops(state);
		Assert.Single(mature);
		Assert.Equal(1, mature[0].X);
	}

	[Fact]
	public void GetGrowthPercent_CalculatesCorrectly()
	{
		RegisterTestCrop();
		var state = CreateState();
		FarmModule.Plant(state, "wheat", 3, 3, 0);

		var crop = FarmModule.GetCropAt(state, 3, 3, 0)!;
		Assert.Equal(0f, FarmModule.GetGrowthPercent(crop));

		crop.Growth = 5;
		Assert.Equal(0.5f, FarmModule.GetGrowthPercent(crop));

		crop.Growth = 10;
		Assert.Equal(1f, FarmModule.GetGrowthPercent(crop));
	}

	[Fact]
	public void SeasonalCrop_WithersOutOfSeason()
	{
		CropRegistry.Clear();
		CropRegistry.Register(new CropDef
		{
			Id = "summer_berry",
			Name = "夏季浆果",
			GrowthTurns = 10,
			HarvestItemId = "berry",
			Seasons = ["summer"],
		});

		// Turn 0 = spring，不是 summer
		var state = CreateState(0);
		FarmModule.Plant(state, "summer_berry", 3, 3, 0);

		var events = FarmModule.TickGrowth(state);
		Assert.Contains(events, e => e.Type == "crop_withered");

		var crop = FarmModule.GetCropAt(state, 3, 3, 0)!;
		Assert.True(crop.Withered);
	}

	[Fact]
	public void AutoReplant_ReplantAfterHarvest()
	{
		CropRegistry.Clear();
		CropRegistry.Register(new CropDef
		{
			Id = "auto_wheat",
			Name = "自动小麦",
			GrowthTurns = 5,
			HarvestItemId = "wheat_grain",
			HarvestMin = 1,
			HarvestMax = 1,
			AutoReplant = true,
		});
		PresetDB.Items["wheat_grain"] = new ItemPreset { Id = "wheat_grain", Name = "小麦粒", Category = "food" };

		var state = CreateState();
		FarmModule.Plant(state, "auto_wheat", 3, 3, 0);

		// 成熟
		for (var i = 0; i < 5; i++)
			FarmModule.TickGrowth(state);

		var result = FarmModule.Harvest(state, 3, 3, 0);
		Assert.True(result.Success);

		// 自动重新种植
		var newCrop = FarmModule.GetCropAt(state, 3, 3, 0);
		Assert.NotNull(newCrop);
		Assert.Equal(0, newCrop.Growth);
		Assert.False(newCrop.Mature);
	}
}
