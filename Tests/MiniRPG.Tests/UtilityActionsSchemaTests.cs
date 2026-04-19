using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.AI.Utility;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 反序列化反射守护：让 <c>Data/Config/utility_actions.json</c> 和
/// <c>Data/Config/personality_defaults.json</c> 任何 schema 漂移立刻被 CI 抓住。
///
/// 守护点：
/// - utility_actions.json：ID 唯一；executor / curve / input 字段完整且合法；
///   每个 input 在 InputResolver 已注册；每个 executor 在 ExecutorRegistry 已注册。
/// - personality_defaults.json：每个 axis.id 有名字、min &lt;= default &lt;= max。
/// </summary>
public class UtilityActionsSchemaTests
{
	[Fact]
	public void UtilityActions_LoadsWithoutThrow()
	{
		TestSupport.EnsureGameplayDataLoaded();
		Assert.NotEmpty(UtilityActionRegistry.ActionList);
	}

	[Fact]
	public void UtilityActions_AllIdsUnique()
	{
		TestSupport.EnsureGameplayDataLoaded();
		var ids = UtilityActionRegistry.ActionList.Select(a => a.Id).ToList();
		var duplicates = ids.GroupBy(id => id, StringComparer.Ordinal)
			.Where(g => g.Count() > 1)
			.Select(g => g.Key)
			.ToList();
		Assert.True(duplicates.Count == 0,
			$"utility_actions.json 中存在重复 ID：{string.Join(", ", duplicates)}");
	}

	[Fact]
	public void UtilityActions_AllRequiredFieldsNonEmpty()
	{
		TestSupport.EnsureGameplayDataLoaded();
		foreach (var action in UtilityActionRegistry.ActionList)
		{
			Assert.False(string.IsNullOrWhiteSpace(action.Id),
				"Every action must have a non-empty Id.");
			Assert.False(string.IsNullOrWhiteSpace(action.Executor),
				$"Action '{action.Id}' must have a non-empty Executor.");
			Assert.True(action.Considerations != null,
				$"Action '{action.Id}' has null Considerations list (should be empty list at worst).");
			if (action.RequiresTarget)
			{
				Assert.False(string.IsNullOrWhiteSpace(action.TargetType),
					$"Action '{action.Id}' requires target but TargetType is empty.");
			}
		}
	}

	[Fact]
	public void UtilityActions_AllConsiderations_HaveValidCurves()
	{
		TestSupport.EnsureGameplayDataLoaded();
		foreach (var action in UtilityActionRegistry.ActionList)
		{
			for (var i = 0; i < action.Considerations.Count; i++)
			{
				var c = action.Considerations[i];
				Assert.False(string.IsNullOrWhiteSpace(c.Input),
					$"Action '{action.Id}' consideration[{i}] has empty Input id.");
				Assert.NotNull(c.Curve);

				// 各 curve 类型的具体字段合法性。
				switch (c.Curve.Type)
				{
					case CurveType.Boolean:
						Assert.True(c.Curve.TrueValue >= 0f && c.Curve.TrueValue <= 1f,
							$"Action '{action.Id}' c[{i}] Boolean.TrueValue out of [0,1].");
						Assert.True(c.Curve.FalseValue >= 0f && c.Curve.FalseValue <= 1f,
							$"Action '{action.Id}' c[{i}] Boolean.FalseValue out of [0,1].");
						break;
					case CurveType.Step:
						Assert.True(c.Curve.High >= 0f && c.Curve.High <= 1f,
							$"Action '{action.Id}' c[{i}] Step.High out of [0,1].");
						Assert.True(c.Curve.Low >= 0f && c.Curve.Low <= 1f,
							$"Action '{action.Id}' c[{i}] Step.Low out of [0,1].");
						break;
					case CurveType.Logistic:
						Assert.True(c.Curve.Steepness > 0f,
							$"Action '{action.Id}' c[{i}] Logistic.Steepness must be positive.");
						Assert.True(c.Curve.Midpoint >= 0f && c.Curve.Midpoint <= 1f,
							$"Action '{action.Id}' c[{i}] Logistic.Midpoint out of [0,1].");
						break;
					case CurveType.Exponential:
						Assert.True(c.Curve.Exponent > 0f,
							$"Action '{action.Id}' c[{i}] Exponential.Exponent must be positive.");
						break;
				}
			}
		}
	}

	[Fact]
	public void UtilityActions_AllReferencedInputs_AreRegistered()
	{
		TestSupport.EnsureGameplayDataLoaded();
		var unknownInputs = new List<string>();
		foreach (var action in UtilityActionRegistry.ActionList)
		{
			foreach (var c in action.Considerations)
			{
				// InputResolver.Resolve 对未注册 input 静默返回 0；
				// 通过反射读 _resolvers 集合更精准。这里用一个 well-known sentinel：
				// 未注册的 inputId 会让"任意 ctx 下 always 返 0"成为唯一观察特征。
				// 直接使用 InputResolver.Register 把"已注册集合"暴露不容易，做反射更稳。
				if (!IsInputRegistered(c.Input))
					unknownInputs.Add($"{action.Id}.{c.Input}");
			}
		}
		Assert.True(unknownInputs.Count == 0,
			"utility_actions.json 引用了未在 InputResolver 注册的 input：\n  - "
			+ string.Join("\n  - ", unknownInputs));
	}

	[Fact]
	public void UtilityActions_AllReferencedExecutors_AreRegistered()
	{
		TestSupport.EnsureGameplayDataLoaded();
		var unknownExecutors = new List<string>();
		foreach (var action in UtilityActionRegistry.ActionList)
		{
			if (ExecutorRegistry.Get(action.Executor) == null)
				unknownExecutors.Add($"{action.Id} → {action.Executor}");
		}
		Assert.True(unknownExecutors.Count == 0,
			"utility_actions.json 引用了未在 ExecutorRegistry 注册的 executor：\n  - "
			+ string.Join("\n  - ", unknownExecutors));
	}

	[Fact]
	public void PersonalityDefaults_HasAxes_AndAllAxisIdsUnique()
	{
		TestSupport.EnsureGameplayDataLoaded();
		PersonalityModule.EnsureLoaded();
		var axisIds = GetPersonalityAxisIds();
		Assert.NotEmpty(axisIds);
		var duplicates = axisIds.GroupBy(id => id, StringComparer.Ordinal)
			.Where(g => g.Count() > 1)
			.Select(g => g.Key)
			.ToList();
		Assert.True(duplicates.Count == 0,
			$"personality_defaults.json axes 重复：{string.Join(", ", duplicates)}");
	}

	[Fact]
	public void PersonalityDefaults_DefaultValues_WithinMinMax()
	{
		TestSupport.EnsureGameplayDataLoaded();
		PersonalityModule.EnsureLoaded();
		var axes = GetPersonalityAxes();
		foreach (var axis in axes)
		{
			Assert.False(string.IsNullOrWhiteSpace(axis.Id), "Axis must have non-empty id.");
			Assert.True(axis.Min <= axis.Default && axis.Default <= axis.Max,
				$"Axis '{axis.Id}': default={axis.Default} must lie within [{axis.Min},{axis.Max}].");
		}
	}

	private static bool IsInputRegistered(string inputId)
	{
		// InputResolver._resolvers 是 private static；用反射拿到 keys 集合。
		var field = typeof(InputResolver).GetField(
			"_resolvers",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
		Assert.NotNull(field);
		var dict = field!.GetValue(null) as System.Collections.IDictionary;
		Assert.NotNull(dict);
		return dict!.Contains(inputId);
	}

	private static List<string> GetPersonalityAxisIds() =>
		GetPersonalityAxes().Select(a => a.Id).ToList();

	private static List<PersonalityAxisDef> GetPersonalityAxes()
	{
		// PersonalityModule._config 是 private static；用反射拿到。
		var field = typeof(PersonalityModule).GetField(
			"_config",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
		Assert.NotNull(field);
		var cfg = field!.GetValue(null) as PersonalityConfig;
		Assert.NotNull(cfg);
		return cfg!.Axes;
	}
}
