using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace MiniRPG.Tools;

public sealed class PlaceholderWorkspaceManifest
{
	[JsonPropertyName("version")]
	public int Version { get; set; } = PlaceholderWorkspaceStore.CurrentVersion;

	[JsonPropertyName("workspaceName")]
	public string WorkspaceName { get; set; } = "art_placeholders";

	[JsonPropertyName("entries")]
	public List<PlaceholderWorkspaceEntry> Entries { get; set; } = [];

	public PlaceholderWorkspaceManifest DeepClone() => new()
	{
		Version = Version,
		WorkspaceName = WorkspaceName,
		Entries = Entries.Select(static entry => entry.DeepClone()).ToList(),
	};
}

public sealed class PlaceholderWorkspaceEntry
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = string.Empty;

	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("description")]
	public string Description { get; set; } = string.Empty;

	[JsonPropertyName("category")]
	public string Category { get; set; } = PlaceholderWorkspaceCatalog.NpcMapCategory;

	[JsonPropertyName("status")]
	public string Status { get; set; } = PlaceholderWorkspaceCatalog.TodoStatus;

	[JsonPropertyName("canvasPresetId")]
	public string CanvasPresetId { get; set; } = PlaceholderWorkspaceCatalog.Tile256PresetId;

	[JsonPropertyName("width")]
	public int Width { get; set; } = 256;

	[JsonPropertyName("height")]
	public int Height { get; set; } = 256;

	[JsonPropertyName("imagePath")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ImagePath { get; set; }

	[JsonPropertyName("frames")]
	public List<PlaceholderWorkspaceFrame> Frames { get; set; } = [];

	[JsonPropertyName("tags")]
	public List<string> Tags { get; set; } = [];

	public PlaceholderWorkspaceEntry DeepClone() => new()
	{
		Id = Id,
		Name = Name,
		Description = Description,
		Category = Category,
		Status = Status,
		CanvasPresetId = CanvasPresetId,
		Width = Width,
		Height = Height,
		ImagePath = ImagePath,
		Frames = Frames.Select(static frame => frame.DeepClone()).ToList(),
		Tags = [.. Tags],
	};
}

public sealed class PlaceholderWorkspaceFrame
{
	[JsonPropertyName("imagePath")]
	public string ImagePath { get; set; } = string.Empty;

	public PlaceholderWorkspaceFrame DeepClone() => new()
	{
		ImagePath = ImagePath,
	};
}

public sealed record PlaceholderCanvasPreset(string Id, string Label, int Width, int Height, bool IsCustom = false);

public static class PlaceholderWorkspaceCatalog
{
	private sealed record StarterText(string Name, string Description);

	public const string DefaultWorkspaceResPath = "res://Assets/Art/Placeholders";
	public const string ManifestFileName = "manifest.json";

	public const string NpcMapCategory = "npc_map";
	public const string MonsterMapCategory = "monster_map";
	public const string ItemIconsCategory = "item_icons";
	public const string PortraitsCategory = "portraits";
	public const string EffectsCategory = "effects";
	public const string BrandingCategory = "branding";

	public const string TodoStatus = "todo";
	public const string WipStatus = "wip";
	public const string DoneStatus = "done";

	public const string Icon64PresetId = "icon_64";
	public const string Icon128PresetId = "icon_128";
	public const string Tile256PresetId = "tile_256";
	public const string Portrait256PresetId = "portrait_256";
	public const string Portrait512PresetId = "portrait_512";
	public const string Effect256PresetId = "effect_256";
	public const string Logo1600x600PresetId = "logo_1600x600";
	public const string Bg1920x1080PresetId = "bg_1920x1080";
	public const string CustomPresetId = "custom";

	private static readonly IReadOnlyDictionary<string, StarterText> StarterTextById = new Dictionary<string, StarterText>(StringComparer.OrdinalIgnoreCase)
	{
		["npc_merchant"] = new("商人地图占位图", "商人的地图像素占位图。"),
		["npc_elder"] = new("长者地图占位图", "长者的地图像素占位图。"),
		["npc_villager"] = new("村民地图占位图", "村民的地图像素占位图。"),
		["npc_blacksmith_npc"] = new("铁匠地图占位图", "铁匠的地图像素占位图。"),
		["npc_herbalist_npc"] = new("草药师地图占位图", "草药师的地图像素占位图。"),
		["npc_cook_npc"] = new("厨师地图占位图", "厨师的地图像素占位图。"),
		["npc_guard_npc"] = new("守卫地图占位图", "守卫的地图像素占位图。"),
		["npc_tailor_npc"] = new("裁缝地图占位图", "裁缝的地图像素占位图。"),
		["monster_goblin"] = new("哥布林地图占位图", "哥布林的地图像素占位图。"),
		["monster_slime"] = new("史莱姆地图占位图", "史莱姆的地图像素占位图。"),
		["monster_skeleton"] = new("骷髅地图占位图", "骷髅的地图像素占位图。"),
		["monster_spider"] = new("巨蜘蛛地图占位图", "巨蜘蛛的地图像素占位图。"),
		["monster_scorpion"] = new("巨蝎地图占位图", "巨蝎的地图像素占位图。"),
		["monster_wolf"] = new("狼地图占位图", "狼的地图像素占位图。"),
		["monster_bear"] = new("熊地图占位图", "熊的地图像素占位图。"),
		["monster_rat"] = new("老鼠地图占位图", "老鼠的地图像素占位图。"),
		["monster_orc_warrior"] = new("兽人战士地图占位图", "兽人战士的地图像素占位图。"),
		["monster_treant"] = new("树人地图占位图", "树人的地图像素占位图。"),
		["portrait_merchant"] = new("商人头像占位图", "商人的对话头像占位图。"),
		["portrait_elder"] = new("长者头像占位图", "长者的对话头像占位图。"),
		["portrait_villager"] = new("村民头像占位图", "村民的对话头像占位图。"),
		["portrait_blacksmith_npc"] = new("铁匠头像占位图", "铁匠的对话头像占位图。"),
		["portrait_herbalist_npc"] = new("草药师头像占位图", "草药师的对话头像占位图。"),
		["portrait_cook_npc"] = new("厨师头像占位图", "厨师的对话头像占位图。"),
		["portrait_guard_npc"] = new("守卫头像占位图", "守卫的对话头像占位图。"),
		["portrait_tailor_npc"] = new("裁缝头像占位图", "裁缝的对话头像占位图。"),
		["portrait_elf_trader"] = new("精灵商人头像占位图", "精灵商人的对话头像占位图。"),
		["portrait_orc_merchant"] = new("兽人商人头像占位图", "兽人商人的对话头像占位图。"),
		["item_potion_hp"] = new("生命药水图标占位图", "生命药水的道具图标占位图。"),
		["item_potion_str"] = new("力量药水图标占位图", "力量药水的道具图标占位图。"),
		["item_herbal_medicine"] = new("草药药剂图标占位图", "草药药剂的道具图标占位图。"),
		["item_antidote"] = new("解毒剂图标占位图", "解毒剂的道具图标占位图。"),
		["item_bandage"] = new("绷带图标占位图", "绷带的道具图标占位图。"),
		["item_torch"] = new("火把图标占位图", "火把的道具图标占位图。"),
		["item_lantern"] = new("提灯图标占位图", "提灯的道具图标占位图。"),
		["item_sword_iron"] = new("铁剑图标占位图", "铁剑的道具图标占位图。"),
		["item_sword_steel"] = new("钢剑图标占位图", "钢剑的道具图标占位图。"),
		["item_shield_iron"] = new("铁盾图标占位图", "铁盾的道具图标占位图。"),
		["item_bow_short"] = new("短弓图标占位图", "短弓的道具图标占位图。"),
		["item_pickaxe_steel"] = new("钢镐图标占位图", "钢镐的道具图标占位图。"),
		["item_wood_axe"] = new("木斧图标占位图", "木斧的道具图标占位图。"),
		["item_war_hammer"] = new("战锤图标占位图", "战锤的道具图标占位图。"),
		["item_meal_simple"] = new("简餐图标占位图", "简餐的道具图标占位图。"),
		["item_raw_meat"] = new("生肉图标占位图", "生肉的道具图标占位图。"),
		["item_mat_iron"] = new("铁矿材料图标占位图", "铁矿材料的道具图标占位图。"),
		["item_mat_herb"] = new("草药材料图标占位图", "草药材料的道具图标占位图。"),
		["fx_slash_arc"] = new("挥砍弧光占位图", "挥砍弧光特效占位图。"),
		["fx_hit_blunt"] = new("钝击命中占位图", "钝击命中特效占位图。"),
		["fx_arrow_projectile"] = new("箭矢飞行占位图", "箭矢飞行特效占位图。"),
		["fx_poison_spit"] = new("毒液喷射占位图", "毒液喷射特效占位图。"),
		["fx_buff_flash"] = new("增益闪光占位图", "增益闪光特效占位图。"),
		["fx_pickup_glint"] = new("拾取闪光占位图", "拾取闪光特效占位图。"),
		["logo_main"] = new("主 Logo 占位图", "游戏主 Logo 的品牌占位图。"),
		["menu_background_1920x1080"] = new("主菜单背景占位图", "主菜单 1920x1080 背景占位图。"),
	};

	public static readonly IReadOnlyList<string> Categories =
	[
		NpcMapCategory,
		MonsterMapCategory,
		ItemIconsCategory,
		PortraitsCategory,
		EffectsCategory,
		BrandingCategory,
	];

	public static readonly IReadOnlyList<string> Statuses =
	[
		TodoStatus,
		WipStatus,
		DoneStatus,
	];

	public static readonly IReadOnlyDictionary<string, string> CategoryLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		[NpcMapCategory] = "NPC 地图图",
		[MonsterMapCategory] = "怪物地图图",
		[ItemIconsCategory] = "道具图标",
		[PortraitsCategory] = "对话头像",
		[EffectsCategory] = "特效",
		[BrandingCategory] = "品牌图",
	};

	public static readonly IReadOnlyDictionary<string, string> StatusLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		[TodoStatus] = "待处理",
		[WipStatus] = "进行中",
		[DoneStatus] = "已完成",
	};

	public static readonly IReadOnlyList<PlaceholderCanvasPreset> Presets =
	[
		new(Icon64PresetId, "64 x 64 图标", 64, 64),
		new(Icon128PresetId, "128 x 128 图标", 128, 128),
		new(Tile256PresetId, "256 x 256 地图图", 256, 256),
		new(Portrait256PresetId, "256 x 256 头像", 256, 256),
		new(Portrait512PresetId, "512 x 512 头像", 512, 512),
		new(Effect256PresetId, "256 x 256 特效", 256, 256),
		new(Logo1600x600PresetId, "1600 x 600 Logo", 1600, 600),
		new(Bg1920x1080PresetId, "1920 x 1080 背景", 1920, 1080),
		new(CustomPresetId, "自定义", 0, 0, true),
	];

	public static PlaceholderWorkspaceManifest CreateStarterManifest() => new()
	{
		Version = PlaceholderWorkspaceStore.CurrentVersion,
		WorkspaceName = "art_placeholders",
		Entries = BuildStarterEntries(),
	};

	public static PlaceholderCanvasPreset ResolvePreset(string? presetId, int width, int height)
	{
		if (!string.IsNullOrWhiteSpace(presetId))
		{
			var byId = Presets.FirstOrDefault(preset => string.Equals(preset.Id, presetId, StringComparison.OrdinalIgnoreCase));
			if (byId != null)
				return byId;
		}

		var match = Presets.FirstOrDefault(preset => !preset.IsCustom && preset.Width == width && preset.Height == height);
		return match ?? Presets[^1];
	}

	public static string GetCategoryLabel(string category) =>
		CategoryLabels.TryGetValue(category, out var label) ? label : category;

	public static string GetStatusLabel(string status) =>
		StatusLabels.TryGetValue(status, out var label) ? label : status;

	public static string DefaultPresetForCategory(string category) => category switch
	{
		NpcMapCategory => Tile256PresetId,
		MonsterMapCategory => Tile256PresetId,
		ItemIconsCategory => Icon128PresetId,
		PortraitsCategory => Portrait512PresetId,
		EffectsCategory => Effect256PresetId,
		BrandingCategory => Logo1600x600PresetId,
		_ => Tile256PresetId,
	};

	public static string BuildImagePath(string category, string id)
	{
		var safeId = SanitizeId(id);
		if (string.IsNullOrWhiteSpace(safeId))
			safeId = "placeholder";
		return $"{category.Trim().Trim('/')}/{safeId}.png";
	}

	public static string BuildFrameImagePath(string category, string id, int frameIndex)
	{
		var safeId = SanitizeId(id);
		if (string.IsNullOrWhiteSpace(safeId))
			safeId = "placeholder";
		return $"{category.Trim().Trim('/')}/{safeId}/frame_{Math.Max(frameIndex, 0):00}.png";
	}

	public static List<PlaceholderWorkspaceFrame> BuildDefaultFrames(string category, string id) =>
	[
		new()
		{
			ImagePath = BuildImagePath(category, id),
		},
	];

	public static bool UsesFrameDirectory(PlaceholderWorkspaceEntry entry)
	{
		if (entry.Frames.Count > 1)
			return true;

		foreach (var frame in entry.Frames)
		{
			var fileName = Path.GetFileNameWithoutExtension(frame.ImagePath.Replace('\\', '/'));
			if (!string.IsNullOrWhiteSpace(fileName)
				&& fileName.StartsWith("frame_", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	public static void RewriteFramePaths(PlaceholderWorkspaceEntry entry)
	{
		if (entry.Frames.Count == 0)
			entry.Frames = BuildDefaultFrames(entry.Category, entry.Id);

		var safeId = SanitizeId(entry.Id);
		if (string.IsNullOrWhiteSpace(safeId))
		{
			foreach (var frame in entry.Frames)
				frame.ImagePath = string.Empty;
			entry.ImagePath = null;
			return;
		}

		var useDirectory = UsesFrameDirectory(entry);
		for (var i = 0; i < entry.Frames.Count; i++)
		{
			entry.Frames[i].ImagePath = useDirectory
				? BuildFrameImagePath(entry.Category, safeId, i)
				: BuildImagePath(entry.Category, safeId);
		}

		entry.ImagePath = null;
	}

	public static string SanitizeId(string raw)
	{
		var chars = raw
			.Trim()
			.ToLowerInvariant()
			.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
			.ToArray();
		var normalized = new string(chars);
		while (normalized.Contains("__", StringComparison.Ordinal))
			normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
		return normalized.Trim('_');
	}

	public static string EnsureUniqueId(string raw, IEnumerable<PlaceholderWorkspaceEntry> existingEntries, PlaceholderWorkspaceEntry? except = null)
	{
		var baseId = SanitizeId(raw);
		if (string.IsNullOrWhiteSpace(baseId))
			baseId = "placeholder";

		var candidate = baseId;
		var index = 2;
		while (existingEntries.Any(entry => !ReferenceEquals(entry, except) && string.Equals(entry.Id, candidate, StringComparison.OrdinalIgnoreCase)))
		{
			candidate = $"{baseId}_{index}";
			index++;
		}

		return candidate;
	}

	public static List<string> NormalizeTags(IEnumerable<string>? tags)
	{
		if (tags == null)
			return [];

		var result = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var tag in tags)
		{
			var trimmed = tag?.Trim();
			if (string.IsNullOrWhiteSpace(trimmed) || !seen.Add(trimmed))
				continue;
			result.Add(trimmed);
		}

		return result;
	}

	public static bool ShouldUseLocalizedStarterText(string? name, string? description)
	{
		if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
			return true;

		return name.Contains("placeholder", StringComparison.OrdinalIgnoreCase)
			|| description.Contains("placeholder", StringComparison.OrdinalIgnoreCase);
	}

	public static bool TryGetLocalizedStarterText(string id, out string name, out string description)
	{
		if (StarterTextById.TryGetValue(id, out var starterText))
		{
			name = starterText.Name;
			description = starterText.Description;
			return true;
		}

		name = string.Empty;
		description = string.Empty;
		return false;
	}

	public static string HumanizeId(string id)
	{
		var words = id.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(static word => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..]);
		return string.Join(' ', words);
	}

	private static List<PlaceholderWorkspaceEntry> BuildStarterEntries() =>
	[
		CreateStarterEntry(NpcMapCategory, "npc_merchant", Tile256PresetId, "friendly", "human", "merchant"),
		CreateStarterEntry(NpcMapCategory, "npc_elder", Tile256PresetId, "friendly", "human", "elder"),
		CreateStarterEntry(NpcMapCategory, "npc_villager", Tile256PresetId, "friendly", "human", "villager"),
		CreateStarterEntry(NpcMapCategory, "npc_blacksmith_npc", Tile256PresetId, "friendly", "human", "blacksmith"),
		CreateStarterEntry(NpcMapCategory, "npc_herbalist_npc", Tile256PresetId, "friendly", "human", "herbalist"),
		CreateStarterEntry(NpcMapCategory, "npc_cook_npc", Tile256PresetId, "friendly", "human", "cook"),
		CreateStarterEntry(NpcMapCategory, "npc_guard_npc", Tile256PresetId, "friendly", "human", "guard"),
		CreateStarterEntry(NpcMapCategory, "npc_tailor_npc", Tile256PresetId, "friendly", "human", "tailor"),
		CreateStarterEntry(MonsterMapCategory, "monster_goblin", Tile256PresetId, "hostile", "goblin"),
		CreateStarterEntry(MonsterMapCategory, "monster_slime", Tile256PresetId, "hostile", "slime"),
		CreateStarterEntry(MonsterMapCategory, "monster_skeleton", Tile256PresetId, "hostile", "undead"),
		CreateStarterEntry(MonsterMapCategory, "monster_spider", Tile256PresetId, "hostile", "spider"),
		CreateStarterEntry(MonsterMapCategory, "monster_scorpion", Tile256PresetId, "hostile", "scorpion"),
		CreateStarterEntry(MonsterMapCategory, "monster_wolf", Tile256PresetId, "hostile", "wolf"),
		CreateStarterEntry(MonsterMapCategory, "monster_bear", Tile256PresetId, "hostile", "bear"),
		CreateStarterEntry(MonsterMapCategory, "monster_rat", Tile256PresetId, "hostile", "rat"),
		CreateStarterEntry(MonsterMapCategory, "monster_orc_warrior", Tile256PresetId, "hostile", "orc", "warrior"),
		CreateStarterEntry(MonsterMapCategory, "monster_treant", Tile256PresetId, "hostile", "treant"),
		CreateStarterEntry(PortraitsCategory, "portrait_merchant", Portrait512PresetId, "friendly", "portrait", "merchant"),
		CreateStarterEntry(PortraitsCategory, "portrait_elder", Portrait512PresetId, "friendly", "portrait", "elder"),
		CreateStarterEntry(PortraitsCategory, "portrait_villager", Portrait512PresetId, "friendly", "portrait", "villager"),
		CreateStarterEntry(PortraitsCategory, "portrait_blacksmith_npc", Portrait512PresetId, "friendly", "portrait", "blacksmith"),
		CreateStarterEntry(PortraitsCategory, "portrait_herbalist_npc", Portrait512PresetId, "friendly", "portrait", "herbalist"),
		CreateStarterEntry(PortraitsCategory, "portrait_cook_npc", Portrait512PresetId, "friendly", "portrait", "cook"),
		CreateStarterEntry(PortraitsCategory, "portrait_guard_npc", Portrait512PresetId, "friendly", "portrait", "guard"),
		CreateStarterEntry(PortraitsCategory, "portrait_tailor_npc", Portrait512PresetId, "friendly", "portrait", "tailor"),
		CreateStarterEntry(PortraitsCategory, "portrait_elf_trader", Portrait512PresetId, "friendly", "portrait", "elf"),
		CreateStarterEntry(PortraitsCategory, "portrait_orc_merchant", Portrait512PresetId, "friendly", "portrait", "orc"),
		CreateStarterEntry(ItemIconsCategory, "item_potion_hp", Icon128PresetId, "item", "icon", "potion"),
		CreateStarterEntry(ItemIconsCategory, "item_potion_str", Icon128PresetId, "item", "icon", "potion"),
		CreateStarterEntry(ItemIconsCategory, "item_herbal_medicine", Icon128PresetId, "item", "icon", "medicine"),
		CreateStarterEntry(ItemIconsCategory, "item_antidote", Icon128PresetId, "item", "icon", "medicine"),
		CreateStarterEntry(ItemIconsCategory, "item_bandage", Icon128PresetId, "item", "icon", "medicine"),
		CreateStarterEntry(ItemIconsCategory, "item_torch", Icon128PresetId, "item", "icon", "tool"),
		CreateStarterEntry(ItemIconsCategory, "item_lantern", Icon128PresetId, "item", "icon", "tool"),
		CreateStarterEntry(ItemIconsCategory, "item_sword_iron", Icon128PresetId, "item", "icon", "weapon", "sword"),
		CreateStarterEntry(ItemIconsCategory, "item_sword_steel", Icon128PresetId, "item", "icon", "weapon", "sword"),
		CreateStarterEntry(ItemIconsCategory, "item_shield_iron", Icon128PresetId, "item", "icon", "armor", "shield"),
		CreateStarterEntry(ItemIconsCategory, "item_bow_short", Icon128PresetId, "item", "icon", "weapon", "bow"),
		CreateStarterEntry(ItemIconsCategory, "item_pickaxe_steel", Icon128PresetId, "item", "icon", "tool", "pickaxe"),
		CreateStarterEntry(ItemIconsCategory, "item_wood_axe", Icon128PresetId, "item", "icon", "tool", "axe"),
		CreateStarterEntry(ItemIconsCategory, "item_war_hammer", Icon128PresetId, "item", "icon", "weapon", "hammer"),
		CreateStarterEntry(ItemIconsCategory, "item_meal_simple", Icon128PresetId, "item", "icon", "food"),
		CreateStarterEntry(ItemIconsCategory, "item_raw_meat", Icon128PresetId, "item", "icon", "food"),
		CreateStarterEntry(ItemIconsCategory, "item_mat_iron", Icon128PresetId, "item", "icon", "material"),
		CreateStarterEntry(ItemIconsCategory, "item_mat_herb", Icon128PresetId, "item", "icon", "material"),
		CreateStarterEntry(EffectsCategory, "fx_slash_arc", Effect256PresetId, "fx", "slash"),
		CreateStarterEntry(EffectsCategory, "fx_hit_blunt", Effect256PresetId, "fx", "hit"),
		CreateStarterEntry(EffectsCategory, "fx_arrow_projectile", Effect256PresetId, "fx", "projectile"),
		CreateStarterEntry(EffectsCategory, "fx_poison_spit", Effect256PresetId, "fx", "projectile", "poison"),
		CreateStarterEntry(EffectsCategory, "fx_buff_flash", Effect256PresetId, "fx", "buff"),
		CreateStarterEntry(EffectsCategory, "fx_pickup_glint", Effect256PresetId, "fx", "pickup"),
		CreateStarterEntry(BrandingCategory, "logo_main", Logo1600x600PresetId, "branding", "logo"),
		CreateStarterEntry(BrandingCategory, "menu_background_1920x1080", Bg1920x1080PresetId, "branding", "background"),
	];

	private static PlaceholderWorkspaceEntry CreateStarterEntry(string category, string id, string presetId, params string[] tags)
	{
		var preset = ResolvePreset(presetId, 256, 256);
		TryGetLocalizedStarterText(id, out var name, out var description);
		if (string.IsNullOrWhiteSpace(name))
			name = $"{HumanizeId(id)} 占位图";
		if (string.IsNullOrWhiteSpace(description))
			description = $"{name}。";

		return new PlaceholderWorkspaceEntry
		{
			Id = id,
			Name = name,
			Description = description,
			Category = category,
			Status = TodoStatus,
			CanvasPresetId = preset.Id,
			Width = preset.IsCustom ? 256 : preset.Width,
			Height = preset.IsCustom ? 256 : preset.Height,
			Frames = BuildDefaultFrames(category, id),
			Tags = NormalizeTags(tags),
		};
	}
}

public static class PlaceholderWorkspaceStore
{
	public const int CurrentVersion = 2;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	public static void EnsureWorkspace(string workspaceResPath, bool createStarterIfMissing = true)
	{
		var globalPath = GlobalizeResPath(workspaceResPath);
		Directory.CreateDirectory(globalPath);
		foreach (var category in PlaceholderWorkspaceCatalog.Categories)
			Directory.CreateDirectory(Path.Combine(globalPath, category));

		var manifestPath = Path.Combine(globalPath, PlaceholderWorkspaceCatalog.ManifestFileName);
		if (createStarterIfMissing && !File.Exists(manifestPath))
			SaveManifest(workspaceResPath, PlaceholderWorkspaceCatalog.CreateStarterManifest());
	}

	public static PlaceholderWorkspaceManifest LoadManifest(string workspaceResPath)
	{
		EnsureWorkspace(workspaceResPath, createStarterIfMissing: true);
		var manifestPath = Path.Combine(GlobalizeResPath(workspaceResPath), PlaceholderWorkspaceCatalog.ManifestFileName);
		var json = File.ReadAllText(manifestPath);
		var manifest = JsonSerializer.Deserialize<PlaceholderWorkspaceManifest>(json, JsonOptions)
			?? PlaceholderWorkspaceCatalog.CreateStarterManifest();
		NormalizeManifest(manifest);
		return manifest;
	}

	public static void SaveManifest(string workspaceResPath, PlaceholderWorkspaceManifest manifest)
	{
		EnsureWorkspace(workspaceResPath, createStarterIfMissing: false);
		var clone = manifest.DeepClone();
		NormalizeManifest(clone);
		var json = JsonSerializer.Serialize(clone, JsonOptions);
		var manifestPath = Path.Combine(GlobalizeResPath(workspaceResPath), PlaceholderWorkspaceCatalog.ManifestFileName);
		File.WriteAllText(manifestPath, json);
	}

	public static string CombineResPath(string baseResPath, string relativePath)
	{
		var normalizedBase = baseResPath.Replace('\\', '/').TrimEnd('/');
		var normalizedRelative = relativePath.Replace('\\', '/').TrimStart('/');
		return $"{normalizedBase}/{normalizedRelative}";
	}

	public static string GlobalizeResPath(string resPath) => ProjectSettings.GlobalizePath(resPath.Replace('\\', '/'));

	public static bool TryAbsoluteToResPath(string absolutePath, out string resPath)
	{
		var projectRoot = Path.GetFullPath(ProjectSettings.GlobalizePath("res://"))
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var fullPath = Path.GetFullPath(absolutePath)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		if (!fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
		{
			resPath = string.Empty;
			return false;
		}

		var relative = Path.GetRelativePath(projectRoot, fullPath)
			.Replace(Path.DirectorySeparatorChar, '/')
			.Replace(Path.AltDirectorySeparatorChar, '/');
		resPath = string.IsNullOrEmpty(relative) || relative == "."
			? "res://"
			: $"res://{relative}";
		return true;
	}

	private static void NormalizeManifest(PlaceholderWorkspaceManifest manifest)
	{
		manifest.Version = CurrentVersion;
		manifest.WorkspaceName = string.IsNullOrWhiteSpace(manifest.WorkspaceName)
			? "art_placeholders"
			: manifest.WorkspaceName.Trim();

		var normalized = new List<PlaceholderWorkspaceEntry>();
		foreach (var rawEntry in manifest.Entries ?? [])
		{
			var entry = rawEntry.DeepClone();
			entry.Id = PlaceholderWorkspaceCatalog.EnsureUniqueId(entry.Id, normalized);
			if (!PlaceholderWorkspaceCatalog.Categories.Contains(entry.Category, StringComparer.OrdinalIgnoreCase))
			{
				GD.PushWarning($"[PlaceholderWorkspace] Skip entry with invalid category: {entry.Id}");
				continue;
			}

			entry.Category = PlaceholderWorkspaceCatalog.Categories
				.First(category => string.Equals(category, entry.Category, StringComparison.OrdinalIgnoreCase));
			entry.Status = PlaceholderWorkspaceCatalog.Statuses.Contains(entry.Status, StringComparer.OrdinalIgnoreCase)
				? PlaceholderWorkspaceCatalog.Statuses.First(status => string.Equals(status, entry.Status, StringComparison.OrdinalIgnoreCase))
				: PlaceholderWorkspaceCatalog.TodoStatus;

			var preset = PlaceholderWorkspaceCatalog.ResolvePreset(entry.CanvasPresetId, entry.Width, entry.Height);
			if (entry.Width <= 0 || entry.Height <= 0)
			{
				entry.Width = preset.IsCustom ? 256 : preset.Width;
				entry.Height = preset.IsCustom ? 256 : preset.Height;
			}

			entry.CanvasPresetId = preset.IsCustom
				? PlaceholderWorkspaceCatalog.ResolvePreset(null, entry.Width, entry.Height).Id
				: preset.Id;
			if (!preset.IsCustom && (entry.Width != preset.Width || entry.Height != preset.Height))
				entry.CanvasPresetId = PlaceholderWorkspaceCatalog.CustomPresetId;

			if (PlaceholderWorkspaceCatalog.ShouldUseLocalizedStarterText(entry.Name, entry.Description)
				&& PlaceholderWorkspaceCatalog.TryGetLocalizedStarterText(entry.Id, out var localizedName, out var localizedDescription))
			{
				entry.Name = localizedName;
				entry.Description = localizedDescription;
			}

			entry.Name = string.IsNullOrWhiteSpace(entry.Name)
				? PlaceholderWorkspaceCatalog.HumanizeId(entry.Id)
				: entry.Name.Trim();
			entry.Description = string.IsNullOrWhiteSpace(entry.Description)
				? $"{entry.Name} 占位图。"
				: entry.Description.Trim();
			entry.Tags = PlaceholderWorkspaceCatalog.NormalizeTags(entry.Tags);
			entry.Frames = NormalizeFrames(entry);
			PlaceholderWorkspaceCatalog.RewriteFramePaths(entry);
			normalized.Add(entry);
		}

		manifest.Entries = normalized;
	}

	private static List<PlaceholderWorkspaceFrame> NormalizeFrames(PlaceholderWorkspaceEntry entry)
	{
		var frames = entry.Frames?
			.Where(static frame => frame != null)
			.Select(static frame => new PlaceholderWorkspaceFrame
			{
				ImagePath = frame.ImagePath?.Trim() ?? string.Empty,
			})
			.Where(static frame => !string.IsNullOrWhiteSpace(frame.ImagePath))
			.ToList()
			?? [];

		if (frames.Count == 0 && !string.IsNullOrWhiteSpace(entry.ImagePath))
		{
			frames.Add(new PlaceholderWorkspaceFrame
			{
				ImagePath = entry.ImagePath.Trim(),
			});
		}

		if (frames.Count == 0)
			frames = PlaceholderWorkspaceCatalog.BuildDefaultFrames(entry.Category, entry.Id);

		return frames;
	}
}
