using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using MiniRPG.Core.Data;
using MiniRPG.Core.Map;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 反射守护：保证 <see cref="GameState"/> 上每一个"非运行时"字段都在
/// <see cref="SavePayload"/> 上有对应字段。
/// 历史上 SaveModule 漏存 PartyState / SocialState / StorytellerState / Zones / Crops，
/// 玩家继续游戏后队伍 / 叙事 / 社交全清零（决策标尺第 1 条"世界连续"破口）。
/// 这套断言的目的：
///   - 任何 contributor 在 GameState 上加了新可序列化字段但忘了在 SavePayload 上加对应字段，
///     CI 立刻 fail，写出"该字段去哪儿了"的明确提示。
///   - 显式列出所有"故意不持久化"的运行时状态（白名单），避免静默偷渡。
/// 不检查类型严格相等：实际形态映射在 SaveModule.BuildSnapshot/ApplySnapshot 各自 round-trip
/// 测试中验收，本测试只盯住"覆盖度"这一面。
/// </summary>
public sealed class SavePayloadCoverageTests
{
	/// <summary>
	/// GameState 字段名 → SavePayload 上的对应字段名。
	/// 显式映射，让任何漏存或重命名一目了然。
	/// </summary>
	private static readonly Dictionary<string, string> ExpectedPayloadMapping = new(StringComparer.Ordinal)
	{
		// ── 基础玩家状态 ──
		[nameof(GameState.Turn)] = nameof(SavePayload.Turn),
		[nameof(GameState.WorldSeed)] = nameof(SavePayload.WorldSeed),
		[nameof(GameState.PlayerX)] = nameof(SavePayload.PlayerX),
		[nameof(GameState.PlayerY)] = nameof(SavePayload.PlayerY),
		[nameof(GameState.PlayerZ)] = nameof(SavePayload.PlayerZ),
		[nameof(GameState.PlayerId)] = nameof(SavePayload.PlayerId),
		[nameof(GameState.PlayerAppearanceId)] = nameof(SavePayload.PlayerAppearanceId),

		// ── 生物 / 设施 / 经济 ──
		[nameof(GameState.Actors)] = nameof(SavePayload.Actors),
		[nameof(GameState.Facilities)] = nameof(SavePayload.Facilities),
		[nameof(GameState.StockpileZones)] = nameof(SavePayload.StockpileZones),
		[nameof(GameState.EconomicDomains)] = nameof(SavePayload.EconomicDomains),
		[nameof(GameState.Room)] = nameof(SavePayload.Room),

		// ── 队伍 / 叙事 / 社交（P0-1 修复点） ──
		[nameof(GameState.Party)] = nameof(SavePayload.Party),
		[nameof(GameState.StorytellerState)] = nameof(SavePayload.Storyteller),
		[nameof(GameState.SocialState)] = nameof(SavePayload.Social),

		// ── 区域 / 农业（P0-1 修复点） ──
		[nameof(GameState.Zones)] = nameof(SavePayload.Zones),
		[nameof(GameState.Crops)] = nameof(SavePayload.Crops),

		// ── 任务 / 计数 / 时间线 / 天气 ──
		[nameof(GameState.Quests)] = nameof(SavePayload.Quests),
		[nameof(GameState.KillCount)] = nameof(SavePayload.KillCount),
		[nameof(GameState.Timeline)] = nameof(SavePayload.Timeline),
		[nameof(GameState.Weather)] = nameof(SavePayload.Weather),

		// ── 显式开关 ──
		[nameof(GameState.WatchMode)] = nameof(SavePayload.WatchMode),
		[nameof(GameState.IdentifiedActorTypes)] = nameof(SavePayload.IdentifiedActorTypes),
		[nameof(GameState.IdentifiedItemTypes)] = nameof(SavePayload.IdentifiedItemTypes),
		[nameof(GameState.GeneratorId)] = nameof(SavePayload.GeneratorId),
		[nameof(GameState.ViewModeId)] = nameof(SavePayload.ViewModeId),
	};

	/// <summary>
	/// 已知"故意不持久化"的字段名单。新增进来的字段必须配一行注释说明理由，
	/// 否则下一次扫描就要把它挪进 ExpectedPayloadMapping。
	/// </summary>
	private static readonly HashSet<string> KnownNonPersistedFields = new(StringComparer.Ordinal)
	{
		// JobBoardState：每次会话由 FacilityConstructionModule.RebuildConstructionTickets 从 Facilities 重建。
		nameof(GameState.JobBoardState),
		// RngSeed：WorldSeed 的别名属性，已经通过 WorldSeed 持久化。
		nameof(GameState.RngSeed),
		// ActiveConversations：BG3 对话进程的 in-progress 字段，ConversationModule + Snapshot 还在路上。
		// 临时白名单是为了让 build 不被这一字段一直阻塞别的任务；接通持久化后请把本行删掉，
		// 改进 ExpectedPayloadMapping 即可。
		nameof(GameState.ActiveConversations),
	};

	[Fact]
	public void EveryNonRuntimeGameStateField_HasMatchingSavePayloadField()
	{
		var gameStateProps = GetSerializableInstanceProperties(typeof(GameState));
		Assert.NotEmpty(gameStateProps);

		var unmapped = new List<string>();
		var missingOnPayload = new List<string>();
		foreach (var prop in gameStateProps)
		{
			if (KnownNonPersistedFields.Contains(prop.Name))
				continue;

			if (!ExpectedPayloadMapping.TryGetValue(prop.Name, out var payloadFieldName))
			{
				unmapped.Add(prop.Name);
				continue;
			}

			var payloadProp = typeof(SavePayload).GetProperty(payloadFieldName, BindingFlags.Public | BindingFlags.Instance);
			if (payloadProp == null)
				missingOnPayload.Add($"{prop.Name} -> SavePayload.{payloadFieldName}");
		}

		Assert.True(
			unmapped.Count == 0,
			"GameState 新增了字段但没有进 ExpectedPayloadMapping 也没在 KnownNonPersistedFields 白名单里。"
			+ " 要么把它接到 SavePayload，要么写明为何属于运行时不持久化字段：\n  - "
			+ string.Join("\n  - ", unmapped));

		Assert.True(
			missingOnPayload.Count == 0,
			"ExpectedPayloadMapping 声称这些 GameState 字段会被持久化，但 SavePayload 上找不到对应属性："
			+ "\n  - "
			+ string.Join("\n  - ", missingOnPayload));
	}

	[Fact]
	public void KnownNonPersistedFields_AreActuallyOnGameState()
	{
		var stale = KnownNonPersistedFields
			.Where(name => typeof(GameState).GetProperty(name, BindingFlags.Public | BindingFlags.Instance) == null)
			.ToList();

		Assert.True(
			stale.Count == 0,
			"KnownNonPersistedFields 列表过期：以下字段在 GameState 上已不存在，请同步删除：\n  - "
			+ string.Join("\n  - ", stale));
	}

	[Fact]
	public void NewlyAddedSubsystemSnapshots_AppearOnSavePayload()
	{
		// 显式 sanity check：P0-1 修复后，新增的 5 个 snapshot 字段必须在 SavePayload 上能被发现。
		// 万一未来有人误删了某个 snapshot 字段，这条断言比 round-trip 测试更早 fail。
		string[] expected =
		[
			nameof(SavePayload.Party),
			nameof(SavePayload.Social),
			nameof(SavePayload.Storyteller),
			nameof(SavePayload.Zones),
			nameof(SavePayload.Crops),
		];

		foreach (var name in expected)
		{
			var prop = typeof(SavePayload).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
			Assert.NotNull(prop);
		}
	}

	private static List<PropertyInfo> GetSerializableInstanceProperties(Type type)
	{
		var props = new List<PropertyInfo>();
		foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (prop.GetCustomAttribute<JsonIgnoreAttribute>() != null)
				continue;
			if (prop.GetMethod == null || prop.SetMethod == null)
				continue;
			props.Add(prop);
		}
		return props;
	}
}
