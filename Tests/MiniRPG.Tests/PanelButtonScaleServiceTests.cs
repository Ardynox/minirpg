using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MiniRPG.Module.Panel;
using Xunit;

namespace MiniRPG.Tests;

public sealed class PanelButtonScaleServiceTests
{
	[Fact]
	public void TraverseTree_VisitsNestedChildrenRecursively()
	{
		var root = new TreeNode("root",
		[
			new TreeNode("left", [new TreeNode("left.child")]),
			new TreeNode("right"),
		]);
		var visited = new List<string>();

		InvokeTraverseTree(root, node => node.Children, node => visited.Add(node.Id));

		Assert.Equal(["left", "left.child", "right"], visited);
	}

	[Fact]
	public void TryAddBaseline_DoesNotOverwriteExistingValue()
	{
		var tracked = new Dictionary<string, int>();

		Assert.True(InvokeTryAddBaseline(tracked, "primary", 12));
		Assert.False(InvokeTryAddBaseline(tracked, "primary", 99));
		Assert.Equal(12, tracked["primary"]);
	}

	[Fact]
	public void ScaleMetrics_AppliesClampedScaleToMinimumSizeAndFontSize()
	{
		var clamped = InvokeScaleMetrics(new Vector2(10f, 20f), 12, 0.25f);
		Assert.Equal(new Vector2(5f, 10f), clamped.MinimumSize);
		Assert.Equal(6, clamped.FontSize);

		var scaled = InvokeScaleMetrics(new Vector2(8f, 14f), 10, 1.5f);
		Assert.Equal(new Vector2(12f, 21f), scaled.MinimumSize);
		Assert.Equal(15, scaled.FontSize);
	}

	[Fact]
	public void CollectInvalidKeys_ReturnsOnlyInvalidTrackedEntries()
	{
		var tracked = new Dictionary<string, int>
		{
			["keep"] = 1,
			["drop"] = 2,
			["also-keep"] = 3,
		};

		var invalid = InvokeCollectInvalidKeys(tracked, key => key != "drop");

		Assert.Equal(["drop"], invalid);
	}

	private static void InvokeTraverseTree<TNode>(TNode root, Func<TNode, IEnumerable<TNode>> getChildren, Action<TNode> visitor)
	{
		var method = typeof(PanelButtonScaleService).GetMethod("TraverseTree", BindingFlags.Static | BindingFlags.NonPublic);
		Assert.NotNull(method);
		method!.MakeGenericMethod(typeof(TNode)).Invoke(null, [root!, getChildren, visitor]);
	}

	private static bool InvokeTryAddBaseline<TKey, TValue>(Dictionary<TKey, TValue> tracked, TKey key, TValue value)
		where TKey : notnull
	{
		var method = typeof(PanelButtonScaleService).GetMethod("TryAddBaseline", BindingFlags.Static | BindingFlags.NonPublic);
		Assert.NotNull(method);
		return (bool)method!.MakeGenericMethod(typeof(TKey), typeof(TValue)).Invoke(null, [tracked, key!, value!])!;
	}

	private static IReadOnlyList<TKey> InvokeCollectInvalidKeys<TKey, TValue>(Dictionary<TKey, TValue> tracked, Func<TKey, bool> isValid)
		where TKey : notnull
	{
		var method = typeof(PanelButtonScaleService).GetMethod("CollectInvalidKeys", BindingFlags.Static | BindingFlags.NonPublic);
		Assert.NotNull(method);
		var result = method!.MakeGenericMethod(typeof(TKey), typeof(TValue)).Invoke(null, [tracked, isValid]);
		return Assert.IsAssignableFrom<IEnumerable<TKey>>(result).ToList();
	}

	private static (Vector2 MinimumSize, int FontSize) InvokeScaleMetrics(Vector2 minimumSize, int fontSize, float scale)
	{
		var method = typeof(PanelButtonScaleService).GetMethod("ScaleMetrics", BindingFlags.Static | BindingFlags.NonPublic);
		Assert.NotNull(method);
		var metrics = method!.Invoke(null, [minimumSize, fontSize, scale]);
		Assert.NotNull(metrics);
		var type = metrics!.GetType();
		return (
			(Vector2)type.GetProperty("MinimumSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(metrics)!,
			(int)type.GetProperty("FontSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(metrics)!);
	}

	private sealed class TreeNode(string id, IEnumerable<TreeNode>? children = null)
	{
		public string Id { get; } = id;
		public List<TreeNode> Children { get; } = children?.ToList() ?? [];
	}
}
