using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.Revival;

/// <summary>
/// 复活代价模型。把"被复活次数"换算成需要施加的持续性惩罚 + 本次资源消耗 + 失败概率，
/// 供 <see cref="ReviveService.TryRevive"/> 调用。
/// </summary>
/// <remarks>
/// 设计意图（见 <c>Docs/产品愿景.md</c> 死亡与复活段）：
/// "多次复活可累加负面效果（性格偏移、技能折损、灵魂残缺），给'真的回不来的线'留空间。"
///
/// 数值由 <c>Data/Config/revival_costs.json</c> 驱动；仓库 fallback 给一份保守默认让单元测试在
/// 没加载 JSON 的情况下也能跑（默认 1 次复活轻微、3 次后很重、第 6 次永久死亡）。
/// </remarks>
public static class RevivalCostModel
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
	};

	private static RevivalCostsConfig _config = RevivalCostsConfig.Defaults();
	private static bool _loaded;

	/// <summary>当前生效的配置（测试或上层可读字段做断言）。</summary>
	public static RevivalCostsConfig Config => _config;

	/// <summary>从 <c>Data/Config/revival_costs.json</c> 加载一次。重复调用幂等。</summary>
	public static void EnsureLoaded()
	{
		if (_loaded) return;
		try
		{
			var json = GameDataLocator.ReadTextOrThrow("Config/revival_costs.json");
			var loaded = JsonSerializer.Deserialize<RevivalCostsConfig>(json, JsonOptions);
			if (loaded != null)
				_config = loaded;
		}
		catch
		{
			// 测试环境或非常规部署：保留 RevivalCostsConfig.Defaults() 作为兜底。
		}
		_loaded = true;
	}

	/// <summary>仅供测试：换上自定义 config + 重置 loaded 标志。</summary>
	public static void OverrideConfigForTesting(RevivalCostsConfig overrideConfig)
	{
		_config = overrideConfig;
		_loaded = true;
	}

	/// <summary>仅供测试：恢复默认 config。</summary>
	public static void ResetForTesting()
	{
		_config = RevivalCostsConfig.Defaults();
		_loaded = false;
	}

	/// <summary>取指定 method 的配置，未注册时返回 null。</summary>
	public static RevivalMethodConfig? GetMethod(string methodId)
	{
		EnsureLoaded();
		return _config.Methods.TryGetValue(methodId, out var def) ? def : null;
	}

	/// <summary>
	/// 推算被复活者本次应承受的额外 mood 负 thought 强度和持续回合。
	/// 约定：<paramref name="priorRevivalCount"/> 是**本次复活发生之前**的累计复活次数。
	/// 返回值用于上层再调 <see cref="Needs.NeedSystem.ApplyTemporaryThought"/> 挂一条 "revived_scar" thought。
	/// </summary>
	public static (float MoodOffset, int DurationTurns) ComputeRevivalScar(int priorRevivalCount)
	{
		EnsureLoaded();
		var index = Math.Max(0, priorRevivalCount);
		var scar = _config.MoodScar;

		// MoodOffset：base + perAttempt*index，clamp 到 maxOffset（绝对值上限）。
		var rawOffset = scar.BaseOffset + scar.PerAttemptOffset * index;
		var moodOffset = scar.MaxOffset < 0
			? Math.Max(scar.MaxOffset, rawOffset)
			: Math.Min(scar.MaxOffset, rawOffset);

		// Duration：base + perAttempt*index，clamp 到 maxDuration。
		var rawDuration = scar.BaseDurationTurns + scar.PerAttemptDurationTurns * index;
		var duration = Math.Min(scar.MaxDurationTurns, Math.Max(0, rawDuration));
		return (moodOffset, duration);
	}

	/// <summary>
	/// 推算被复活者本次应承受的技能/能力百分比折损。
	/// 0.0 表示不折损；0.1 表示把技能等级（或 capacity 上限）扣掉 10%。
	/// </summary>
	public static float ComputeCapacityPenalty(int priorRevivalCount)
	{
		EnsureLoaded();
		var index = Math.Max(0, priorRevivalCount);
		var p = _config.CapacityPenalty;
		return Math.Min(p.MaxFraction, p.PerAttempt * index);
	}

	/// <summary>
	/// 判断是否触发"彻底回不来"——累计过多次复活后的最终阈值。
	/// 默认：第 6 次复活（priorRevivalCount &gt;= 6）触发 "no_return"，上层应该拒绝复活并给出 FailureReason。
	/// </summary>
	public static bool IsPermanentlyLost(int priorRevivalCount)
	{
		EnsureLoaded();
		return priorRevivalCount >= _config.PermanentLossThreshold;
	}

	/// <summary>
	/// 算本次复活需要消耗的材料：基础列表 × pow(perAttemptMultiplier, priorRevivalCount)，向上取整。
	/// Key = item template id，Value = 数量。
	/// </summary>
	public static IReadOnlyDictionary<string, int> ComputeMaterialCost(string methodId, int priorRevivalCount)
	{
		var method = GetMethod(methodId);
		if (method == null)
			return new Dictionary<string, int>(StringComparer.Ordinal);

		var index = Math.Max(0, priorRevivalCount);
		var multiplier = (float)Math.Pow(method.PerAttemptMultiplier, index);
		var result = new Dictionary<string, int>(method.BaseMaterials.Count, StringComparer.Ordinal);
		foreach (var (itemId, baseQty) in method.BaseMaterials)
		{
			var scaled = (int)Math.Ceiling(baseQty * multiplier);
			result[itemId] = Math.Max(1, scaled);
		}
		return result;
	}

	/// <summary>
	/// 算本次复活的失败概率（0.0~1.0）。
	/// 公式：base + perPriorRevival*priorRevivalCount，clamp 到 maxFailureChance。
	/// 施法者技能阈值不直接影响概率（让 caller 决定是否硬阻断），但本数值会被 ReviveService.TryRevive 直接掷骰。
	/// </summary>
	public static float ComputeFailureChance(string methodId, int priorRevivalCount)
	{
		var method = GetMethod(methodId);
		if (method == null)
			return 1f;

		var index = Math.Max(0, priorRevivalCount);
		var raw = method.BaseFailureChance + method.FailureChancePerPriorRevival * index;
		return Math.Clamp(raw, 0f, method.MaxFailureChance);
	}

	/// <summary>本 method 的施法时长（回合数），用于 TimelineTurnGateway 回合化施法。</summary>
	public static int GetChannelTurns(string methodId)
	{
		var method = GetMethod(methodId);
		return method?.ChannelTurns ?? 1;
	}
}

public sealed class RevivalCostsConfig
{
	[JsonPropertyName("methods")]
	public Dictionary<string, RevivalMethodConfig> Methods { get; set; } = new(StringComparer.Ordinal);

	[JsonPropertyName("moodScar")]
	public RevivalMoodScarConfig MoodScar { get; set; } = new();

	[JsonPropertyName("capacityPenalty")]
	public RevivalCapacityPenaltyConfig CapacityPenalty { get; set; } = new();

	[JsonPropertyName("permanentLossThreshold")]
	public int PermanentLossThreshold { get; set; } = 6;

	/// <summary>构造一份兜底默认值——和原硬编码常量等价，让 fallback 行为可预测。</summary>
	public static RevivalCostsConfig Defaults() => new()
	{
		Methods = new Dictionary<string, RevivalMethodConfig>(StringComparer.Ordinal)
		{
			["magic"] = new RevivalMethodConfig
			{
				ChannelTurns = 8,
				BaseMaterials = new Dictionary<string, int>(StringComparer.Ordinal)
				{
					["herb_root"] = 5,
					["water"] = 3,
				},
				PerAttemptMultiplier = 1.5f,
				BaseFailureChance = 0.10f,
				FailureChancePerPriorRevival = 0.08f,
				MaxFailureChance = 0.65f,
				CasterSkillId = "magic_revival",
				CasterSkillThreshold = 4,
			},
			["tech"] = new RevivalMethodConfig
			{
				ChannelTurns = 12,
				BaseMaterials = new Dictionary<string, int>(StringComparer.Ordinal)
				{
					["nano_paste"] = 4,
					["energy_cell"] = 2,
				},
				PerAttemptMultiplier = 1.4f,
				BaseFailureChance = 0.05f,
				FailureChancePerPriorRevival = 0.05f,
				MaxFailureChance = 0.55f,
				CasterSkillId = "tech_revival",
				CasterSkillThreshold = 3,
			},
			["divine"] = new RevivalMethodConfig
			{
				ChannelTurns = 6,
				BaseMaterials = new Dictionary<string, int>(StringComparer.Ordinal)
				{
					["blessed_water"] = 1,
				},
				PerAttemptMultiplier = 2.0f,
				BaseFailureChance = 0.45f,
				FailureChancePerPriorRevival = 0.10f,
				MaxFailureChance = 0.85f,
				CasterSkillId = "faith",
				CasterSkillThreshold = 6,
			},
		},
		MoodScar = new RevivalMoodScarConfig
		{
			BaseOffset = -4f,
			PerAttemptOffset = -4f,
			MaxOffset = -20f,
			BaseDurationTurns = 360,
			PerAttemptDurationTurns = 360,
			MaxDurationTurns = 1800,
		},
		CapacityPenalty = new RevivalCapacityPenaltyConfig
		{
			PerAttempt = 0.05f,
			MaxFraction = 0.40f,
		},
		PermanentLossThreshold = 6,
	};
}

public sealed class RevivalMethodConfig
{
	[JsonPropertyName("channelTurns")]
	public int ChannelTurns { get; set; } = 6;

	[JsonPropertyName("baseMaterials")]
	public Dictionary<string, int> BaseMaterials { get; set; } = new(StringComparer.Ordinal);

	[JsonPropertyName("perAttemptMultiplier")]
	public float PerAttemptMultiplier { get; set; } = 1.5f;

	[JsonPropertyName("baseFailureChance")]
	public float BaseFailureChance { get; set; }

	[JsonPropertyName("failureChancePerPriorRevival")]
	public float FailureChancePerPriorRevival { get; set; }

	[JsonPropertyName("maxFailureChance")]
	public float MaxFailureChance { get; set; } = 1f;

	[JsonPropertyName("casterSkillId")]
	public string CasterSkillId { get; set; } = "";

	[JsonPropertyName("casterSkillThreshold")]
	public int CasterSkillThreshold { get; set; }

	[JsonPropertyName("displayNameKey")]
	public string DisplayNameKey { get; set; } = "";
}

public sealed class RevivalMoodScarConfig
{
	[JsonPropertyName("baseOffset")]
	public float BaseOffset { get; set; } = -4f;

	[JsonPropertyName("perAttemptOffset")]
	public float PerAttemptOffset { get; set; } = -4f;

	[JsonPropertyName("maxOffset")]
	public float MaxOffset { get; set; } = -20f;

	[JsonPropertyName("baseDurationTurns")]
	public int BaseDurationTurns { get; set; } = 360;

	[JsonPropertyName("perAttemptDurationTurns")]
	public int PerAttemptDurationTurns { get; set; } = 360;

	[JsonPropertyName("maxDurationTurns")]
	public int MaxDurationTurns { get; set; } = 1800;
}

public sealed class RevivalCapacityPenaltyConfig
{
	[JsonPropertyName("perAttempt")]
	public float PerAttempt { get; set; } = 0.05f;

	[JsonPropertyName("maxFraction")]
	public float MaxFraction { get; set; } = 0.4f;
}
