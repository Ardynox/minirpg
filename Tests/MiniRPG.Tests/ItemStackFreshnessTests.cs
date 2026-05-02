using MiniRPG.Core.Data;
using MiniRPG.Core.Needs;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 验证 <see cref="Item.CanStackWith"/> 在 <see cref="FoodFreshnessEvaluator"/> 加载后，
/// 跨新鲜阶段的食物拒绝合堆，同阶段允许合堆。
/// </summary>
public sealed class ItemStackFreshnessTests
{
	public ItemStackFreshnessTests()
	{
		FoodFreshnessEvaluator.EnsureInitialized();
	}

	[Fact]
	public void CanStackWith_BothUntracked_True()
	{
		// 罐头（永不过期）：两个不同 turn 创建的 raw_meat 模板都视为同阶段。
		var a = MakeFood(spawnTurn: -1, freshnessTurns: 0);
		var b = MakeFood(spawnTurn: -1, freshnessTurns: 0);
		Assert.True(a.CanStackWith(b));
	}

	[Fact]
	public void CanStackWith_SameSpawnedTurn_True()
	{
		var a = MakeFood(spawnTurn: 100, freshnessTurns: 10);
		var b = MakeFood(spawnTurn: 100, freshnessTurns: 10);
		Assert.True(a.CanStackWith(b));
	}

	[Fact]
	public void CanStackWith_FarApartTurns_False()
	{
		// freshness=10：spawn=0 是 Fresh、spawn=20 也是 Fresh（age=0 from current turn 立刻评估）
		// 但 SameStage 不依赖 currentTurn，按 spawn-time bucket=5 比较：bucket(0)=0, bucket(20)=4 → 不同
		var a = MakeFood(spawnTurn: 0, freshnessTurns: 10);
		var b = MakeFood(spawnTurn: 20, freshnessTurns: 10);
		Assert.False(a.CanStackWith(b));
	}

	[Fact]
	public void CanStackWith_OneUntrackedOneTracked_False()
	{
		// 防止"罐头永不过期"和"鲜肉"被合堆，污染保鲜含义
		var fresh = MakeFood(spawnTurn: 5, freshnessTurns: 10);
		var canned = MakeFood(spawnTurn: -1, freshnessTurns: 0);
		Assert.False(fresh.CanStackWith(canned));
		Assert.False(canned.CanStackWith(fresh));
	}

	private static Item MakeFood(int spawnTurn, int freshnessTurns)
	{
		var item = new Item
		{
			Id = "raw_meat",
			MaterialId = "meat",
			Category = ItemCategories.Food,
			SpawnedAtTurn = spawnTurn,
			MaxStack = 10,
			StackCount = 1,
			MaxDurability = 1,
			Durability = 1,
		};
		if (freshnessTurns > 0)
			item.Tags[ItemTags.FreshnessTurns] = freshnessTurns;
		item.EnsureRuntimeState();
		return item;
	}
}
