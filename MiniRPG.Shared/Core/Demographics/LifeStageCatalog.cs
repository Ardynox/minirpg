using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Calendar;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Module.Render;

namespace MiniRPG.Core.Demographics;

/// <summary>
/// race → 生命阶段年龄阈值。对应 Data/Config/lifestage_defaults.json 的一条。
/// 字段命名 snake_case 与 JSON 一致，由 <see cref="JsonPropertyName"/> 显式映射。
/// </summary>
public sealed class RaceLifeStageBounds
{
	[JsonPropertyName("infant_to_years")]
	public int InfantToYears { get; set; } = 3;

	[JsonPropertyName("child_to_years")]
	public int ChildToYears { get; set; } = 13;

	[JsonPropertyName("adolescent_to_years")]
	public int AdolescentToYears { get; set; } = 18;

	[JsonPropertyName("adult_to_years")]
	public int AdultToYears { get; set; } = 60;

	[JsonPropertyName("max_lifespan_years")]
	public int MaxLifespanYears { get; set; } = 90;
}

public sealed class LifeStageCatalogRoot
{
	[JsonPropertyName("races")]
	public Dictionary<string, RaceLifeStageBounds> Races { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 受孕 / 怀孕 / 出生 / 自然死亡的全局参数。对应 Data/Config/conception_defaults.json。
/// Phase 4 ConceptionBirthTick 消费这些字段。
/// </summary>
public sealed class ConceptionCatalogRoot
{
	[JsonPropertyName("pairing_radius")]
	public int PairingRadius { get; set; } = 2;

	[JsonPropertyName("lover_opinion_threshold")]
	public int LoverOpinionThreshold { get; set; } = 20;

	[JsonPropertyName("conception_chance_per_day")]
	public float ConceptionChancePerDay { get; set; } = 0.04f;

	[JsonPropertyName("gestation_turns")]
	public int GestationTurns { get; set; } = 2160;

	[JsonPropertyName("twin_chance")]
	public float TwinChance { get; set; } = 0.02f;

	[JsonPropertyName("miscarriage_chance_per_trimester")]
	public float MiscarriageChancePerTrimester { get; set; } = 0.01f;

	[JsonPropertyName("natural_death_base_chance_per_day")]
	public float NaturalDeathBaseChancePerDay { get; set; } = 0.002f;

	/// <summary>
	/// race 白名单：只有 race id 在列表里的 actor 才会自动受孕配对。
	/// null / 空列表 = 不限制（向后兼容，全 race 都可受孕）。
	/// 用来阻止怪物 race（goblin / spider 等）也自动繁殖出小怪物。
	/// </summary>
	[JsonPropertyName("auto_conception_race_ids")]
	public List<string>? AutoConceptionRaceIds { get; set; }
}

/// <summary>
/// 启动一次加载 lifestage_defaults.json + conception_defaults.json，提供查询接口。
/// 没找到 race 时 fallback 到 default；没找到 default 时给硬编码兜底，保证不抛。
/// 测试可走 <see cref="OverrideForTesting"/> 注入数据，避开磁盘 IO。
/// </summary>
public static class LifeStageCatalog
{
	public const int LegacyUnknownAgeYears = 25;

	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
	};

	private static LifeStageCatalogRoot? _life;
	private static ConceptionCatalogRoot? _conception;
	private static bool _loaded;

	public static void EnsureLoaded()
	{
		if (_loaded)
			return;
		Load();
	}

	public static void Load()
	{
		var lifeJson = GameDataLocator.TryReadText("Config/lifestage_defaults.json", out var lj, out _) ? lj : "{}";
		_life = JsonSerializer.Deserialize<LifeStageCatalogRoot>(lifeJson, JsonOpts) ?? new LifeStageCatalogRoot();

		var cJson = GameDataLocator.TryReadText("Config/conception_defaults.json", out var cj, out _) ? cj : "{}";
		_conception = JsonSerializer.Deserialize<ConceptionCatalogRoot>(cJson, JsonOpts) ?? new ConceptionCatalogRoot();

		_loaded = true;
	}

	public static void Reset()
	{
		_life = null;
		_conception = null;
		_loaded = false;
	}

	public static void OverrideForTesting(LifeStageCatalogRoot? life, ConceptionCatalogRoot? conception)
	{
		_life = life ?? new LifeStageCatalogRoot();
		_conception = conception ?? new ConceptionCatalogRoot();
		_loaded = true;
	}

	public static RaceLifeStageBounds GetForRace(string? raceId)
	{
		EnsureLoaded();
		if (!string.IsNullOrWhiteSpace(raceId) && _life!.Races.TryGetValue(raceId!, out var bounds))
			return bounds;
		return _life!.Races.TryGetValue("default", out var fallback) ? fallback : new RaceLifeStageBounds();
	}

	public static ConceptionCatalogRoot Conception
	{
		get
		{
			EnsureLoaded();
			return _conception!;
		}
	}

	public static int TurnsPerYear => Math.Max(1, DayNightCycle.TurnsPerSeason * CalendarService.SeasonsPerYear);

	/// <summary>从 <see cref="Actor.BirthTurn"/> 算年龄；BirthTurn &lt; 0 视为 legacy unknown，返回常量。</summary>
	public static int AgeYears(GameState state, Actor actor)
	{
		if (actor.BirthTurn < 0)
			return LegacyUnknownAgeYears;
		return Math.Max(0, (state.Turn - actor.BirthTurn) / TurnsPerYear);
	}

	public static LifeStage GetLifeStage(GameState state, Actor actor)
	{
		var age = AgeYears(state, actor);
		var bounds = GetForRace(actor.Race?.Id);
		if (age < bounds.InfantToYears)
			return LifeStage.Infant;
		if (age < bounds.ChildToYears)
			return LifeStage.Child;
		if (age < bounds.AdolescentToYears)
			return LifeStage.Adolescent;
		if (age < bounds.AdultToYears)
			return LifeStage.Adult;
		return LifeStage.Elder;
	}

	public static bool IsAdultOrOlder(LifeStage stage) => stage is LifeStage.Adult or LifeStage.Elder;
}
