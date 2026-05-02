using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.Data;

/// <summary>
/// 部件分类。每个 <see cref="FaceCustomizationData"/> 字段对应一个分类。
/// </summary>
public enum FacePartCategory
{
	HeadShape = 0,
	Eyes = 1,
	Eyebrows = 2,
	Nose = 3,
	Mouth = 4,
	Ears = 5,
	Hair = 6,
	Beard = 7,
}

/// <summary>
/// 部件目录里的一条 metadata。<see cref="ImagePath"/> 可空：空表示用程序生成 fallback。
/// </summary>
public sealed class FacePartEntry
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = string.Empty;

	[JsonPropertyName("displayName")]
	public string DisplayName { get; set; } = string.Empty;

	/// <summary>例如 res://Assets/Faces/HeadShape/round.png；空表示 procedural fallback。</summary>
	[JsonPropertyName("imagePath")]
	public string? ImagePath { get; set; }

	[JsonPropertyName("anchorOffsetX")]
	public int AnchorOffsetX { get; set; }

	[JsonPropertyName("anchorOffsetY")]
	public int AnchorOffsetY { get; set; }
}

/// <summary>
/// 8 类部件的目录加载器。每个分类一份 JSON（如 <c>Data/FaceParts/head_shapes.json</c>），
/// 找不到 JSON 时 fallback 到内置默认条目，以保证系统恒可启动。
/// </summary>
public static class FacePartCatalog
{
	private static readonly object Sync = new();
	private static bool _initialized;

	private static readonly Dictionary<FacePartCategory, List<FacePartEntry>> Entries = new();
	private static readonly Dictionary<FacePartCategory, Dictionary<string, FacePartEntry>> EntryMap = new();

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
	};

	public static IReadOnlyList<FacePartEntry> GetEntries(FacePartCategory category)
	{
		EnsureInitialized();
		return Entries.TryGetValue(category, out var list) ? list : Array.Empty<FacePartEntry>();
	}

	public static FacePartEntry? Find(FacePartCategory category, string? id)
	{
		EnsureInitialized();
		if (string.IsNullOrWhiteSpace(id))
			return null;

		return EntryMap.TryGetValue(category, out var map)
			&& map.TryGetValue(id.Trim(), out var entry)
			? entry
			: null;
	}

	/// <summary>
	/// 按 id 查找；找不到时返回该分类的第一条。Beard 等可空分类调用方应自行先判 null。
	/// </summary>
	public static FacePartEntry GetOrFirst(FacePartCategory category, string? id)
	{
		var found = Find(category, id);
		if (found != null)
			return found;

		var entries = GetEntries(category);
		return entries.Count > 0 ? entries[0] : new FacePartEntry { Id = id ?? string.Empty };
	}

	/// <summary>
	/// 重新从磁盘加载全部 8 类部件。若某个 JSON 缺失 / 损坏则该分类 fallback 到内置默认。
	/// </summary>
	public static void LoadProjectCatalog()
	{
		lock (Sync)
		{
			ResetToBuiltIns();
			foreach (FacePartCategory category in Enum.GetValues<FacePartCategory>())
			{
				TryLoadCategoryFromJson(category);
			}
			_initialized = true;
		}
	}

	public static void ResetForTesting()
	{
		lock (Sync)
		{
			ResetToBuiltIns();
			_initialized = true;
		}
	}

	private static void EnsureInitialized()
	{
		if (_initialized)
			return;

		lock (Sync)
		{
			if (_initialized)
				return;

			ResetToBuiltIns();
			_initialized = true;
		}
	}

	private static void TryLoadCategoryFromJson(FacePartCategory category)
	{
		var relativePath = $"FaceParts/{GetJsonFileName(category)}";
		if (!GameDataLocator.TryReadText(relativePath, out var json, out _))
			return;

		try
		{
			var parsed = JsonSerializer.Deserialize<List<FacePartEntry>>(json, JsonOptions);
			if (parsed == null || parsed.Count == 0)
				return;

			var filtered = parsed
				.Where(static entry => !string.IsNullOrWhiteSpace(entry.Id))
				.ToList();
			if (filtered.Count == 0)
				return;

			var list = new List<FacePartEntry>(filtered.Count);
			var map = new Dictionary<string, FacePartEntry>(StringComparer.Ordinal);
			foreach (var entry in filtered)
			{
				entry.Id = entry.Id.Trim();
				if (string.IsNullOrWhiteSpace(entry.DisplayName))
					entry.DisplayName = entry.Id;
				if (map.ContainsKey(entry.Id))
					continue;

				list.Add(entry);
				map[entry.Id] = entry;
			}

			if (list.Count == 0)
				return;

			Entries[category] = list;
			EntryMap[category] = map;
		}
		catch
		{
			// 损坏的 JSON 已被丢弃，继续用默认条目。
		}
	}

	private static string GetJsonFileName(FacePartCategory category) => category switch
	{
		FacePartCategory.HeadShape => "head_shapes.json",
		FacePartCategory.Eyes => "eyes.json",
		FacePartCategory.Eyebrows => "eyebrows.json",
		FacePartCategory.Nose => "noses.json",
		FacePartCategory.Mouth => "mouths.json",
		FacePartCategory.Ears => "ears.json",
		FacePartCategory.Hair => "hair.json",
		FacePartCategory.Beard => "beards.json",
		_ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
	};

	private static void ResetToBuiltIns()
	{
		Entries.Clear();
		EntryMap.Clear();
		foreach (FacePartCategory category in Enum.GetValues<FacePartCategory>())
		{
			var defaults = BuildDefaultEntriesFor(category);
			var map = new Dictionary<string, FacePartEntry>(StringComparer.Ordinal);
			foreach (var entry in defaults)
				map[entry.Id] = entry;
			Entries[category] = defaults;
			EntryMap[category] = map;
		}
	}

	/// <summary>
	/// 内置默认条目：每个分类至少 8 个 id 占位，<see cref="FacePartEntry.ImagePath"/> 留空，
	/// 由 PortraitComposer 在 Godot 端调用 ProceduralFacePartRenderer 程序生成。
	/// 批 2 用 image_gen 生成真实素材后，对应 JSON 会在 LoadProjectCatalog 阶段覆盖这些条目。
	/// </summary>
	private static List<FacePartEntry> BuildDefaultEntriesFor(FacePartCategory category) => category switch
	{
		FacePartCategory.HeadShape => MakeEntries(
			("round", "圆"),
			("oval", "椭圆"),
			("square", "方"),
			("long", "长"),
			("heart", "心形"),
			("diamond", "菱形"),
			("triangle", "三角"),
			("wide", "宽")),
		FacePartCategory.Eyes => MakeEntries(
			("almond", "杏眼"),
			("big_round", "大圆眼"),
			("narrow_sharp", "锐窄眼"),
			("droopy", "下垂眼"),
			("upturned", "上扬猫眼"),
			("sleepy", "半睁睡眼"),
			("scarred", "疤痕眼"),
			("glowing", "魔法发光眼")),
		FacePartCategory.Eyebrows => MakeEntries(
			("thin_arched", "细弯"),
			("thick_straight", "粗直"),
			("bushy_wild", "浓密狂野"),
			("short_stubby", "短粗"),
			("scarred_broken", "疤痕断眉"),
			("pointed_evil", "尖锐邪气"),
			("soft_feminine", "柔和女性"),
			("raised_expressive", "上挑")),
		FacePartCategory.Nose => MakeEntries(
			("small_button", "小巧"),
			("straight_roman", "罗马直"),
			("hooked", "鹰钩"),
			("wide_flat", "宽扁"),
			("long_pointed", "长尖"),
			("crooked_broken", "歪鼻"),
			("upturned", "上翘"),
			("broad_bulbous", "宽球")),
		FacePartCategory.Mouth => MakeEntries(
			("small_neutral", "小中性"),
			("wide_grin", "咧嘴笑"),
			("smirk", "坏笑"),
			("frown", "皱眉"),
			("crooked_sneer", "歪冷笑"),
			("full_lips", "厚唇"),
			("thin_tight", "薄唇紧抿"),
			("fanged_toothy", "獠牙")),
		FacePartCategory.Ears => MakeEntries(
			("human_round", "人类圆耳"),
			("pointed_elf", "短精灵"),
			("long_elf", "长精灵"),
			("gnome_large", "侏儒大耳"),
			("beast_furred", "兽人毛耳"),
			("orc_tusked", "兽人獠侧"),
			("torn_half_orc", "半兽撕裂"),
			("horned_tiefling", "提夫林角")),
		FacePartCategory.Hair => MakeEntries(
			("short_messy", "短乱发"),
			("long_flowing", "长直发"),
			("ponytail", "马尾"),
			("braided", "单辫"),
			("bald", "光头"),
			("mohawk", "莫西干"),
			("curly_afro", "卷发"),
			("hooded", "兜帽")),
		FacePartCategory.Beard => MakeEntries(
			("clean_shaven", "净面"),
			("light_stubble", "浅胡渣"),
			("short_trimmed", "短整修"),
			("full_bushy", "浓密大胡"),
			("dwarf_braided", "矮人编须"),
			("goatee", "山羊胡"),
			("handlebar", "八字翘"),
			("wizard_pointed", "巫师尖")),
		_ => new List<FacePartEntry>(),
	};

	private static List<FacePartEntry> MakeEntries(params (string id, string displayName)[] items)
	{
		var list = new List<FacePartEntry>(items.Length);
		foreach (var (id, displayName) in items)
		{
			list.Add(new FacePartEntry
			{
				Id = id,
				DisplayName = displayName,
			});
		}
		return list;
	}
}
