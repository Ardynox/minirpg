namespace MiniRPG.Core.Data;

public sealed class PlayerCreationOptions
{
	public const int MaxDisplayNameLength = 20;

	public string DisplayName { get; init; } = string.Empty;
	public string RaceId { get; init; } = string.Empty;
	public string? ProfessionId { get; init; }

	/// <summary>
	/// 捏脸数据。可空：null 时 <see cref="ResolveFaceCustomization"/> 返回
	/// <see cref="FaceCustomizationData.CreateDefault"/>。
	/// </summary>
	public FaceCustomizationData? FaceCustomization { get; init; }

	/// <summary>
	/// 玩家在创建角色界面里挑的初始携带物品。null 表示"用职业默认包"（由
	/// <c>StarterKitResolver.ResolveDefault</c> 决定），空列表表示"玩家明确选了不带任何东西"。
	/// 物品会在 <c>MapGenModule.SpawnPlayer</c> 阶段一次性消费写入 <see cref="Actor.Inventory"/>，
	/// 不进入 <see cref="MiniRPG.Core.Map.SaveSnapshot"/>。
	/// </summary>
	public IReadOnlyList<PlayerStartingItem>? StartingItems { get; init; }

	/// <summary>
	/// 是否绕过白名单校验：仅本地单机调试用（联机服务端永远视为 false，按职业默认包发）。
	/// </summary>
	public bool StartingItemsDebugOverride { get; init; }

	public static PlayerCreationOptions CreateDefault()
	{
		var defaultName = "Player";
		var defaultRaceId = "human";
		string? defaultProfessionId = null;

		if (PresetDB.Actors.TryGetValue(Factions.Player, out var playerPreset))
		{
			defaultName = string.IsNullOrWhiteSpace(playerPreset.DisplayName)
				? defaultName
				: playerPreset.DisplayName;
			defaultRaceId = string.IsNullOrWhiteSpace(playerPreset.RaceId)
				? defaultRaceId
				: playerPreset.RaceId;
			defaultProfessionId = playerPreset.ProfessionId;
		}

		return new PlayerCreationOptions
		{
			DisplayName = defaultName,
			RaceId = defaultRaceId,
			ProfessionId = defaultProfessionId,
		};
	}

	public string ResolveDisplayName(string fallback)
	{
		var normalized = NormalizeDisplayName(DisplayName);
		if (!string.IsNullOrWhiteSpace(normalized))
			return normalized;

		var normalizedFallback = NormalizeDisplayName(fallback);
		return string.IsNullOrWhiteSpace(normalizedFallback)
			? "Player"
			: normalizedFallback;
	}

	public string ResolveRaceId(string fallback)
	{
		if (!string.IsNullOrWhiteSpace(RaceId)
			&& PresetDB.Races.ContainsKey(RaceId))
		{
			return RaceId;
		}

		return !string.IsNullOrWhiteSpace(fallback) && PresetDB.Races.ContainsKey(fallback)
			? fallback
			: CreateDefault().RaceId;
	}

	public string? ResolveProfessionId(string? fallback)
	{
		if (!string.IsNullOrWhiteSpace(ProfessionId)
			&& PresetDB.Professions.ContainsKey(ProfessionId))
		{
			return ProfessionId;
		}

		if (!string.IsNullOrWhiteSpace(fallback)
			&& PresetDB.Professions.ContainsKey(fallback))
		{
			return fallback;
		}

		return null;
	}

	/// <summary>
	/// 解析出非空 <see cref="FaceCustomizationData"/>：优先用本对象的 <see cref="FaceCustomization"/>，
	/// 缺失时返回新 default。
	/// </summary>
	public FaceCustomizationData ResolveFaceCustomization() =>
		FaceCustomization != null
			? FaceCustomization.Clone()
			: FaceCustomizationData.CreateDefault();

	public static string NormalizeDisplayName(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return string.Empty;

		var trimmed = value.Trim();
		if (trimmed.Length <= MaxDisplayNameLength)
			return trimmed;

		return trimmed[..MaxDisplayNameLength];
	}
}

/// <summary>
/// 玩家在创建角色界面里挑的一件初始携带物品。<see cref="ItemId"/> 必须命中
/// <see cref="PresetDB.Items"/>；<see cref="Count"/> 至少 1。
/// </summary>
public sealed class PlayerStartingItem
{
	public string ItemId { get; init; } = string.Empty;
	public int Count { get; init; } = 1;

	public PlayerStartingItem() { }

	public PlayerStartingItem(string itemId, int count)
	{
		ItemId = itemId ?? string.Empty;
		Count = count;
	}

	public PlayerStartingItem WithCount(int count) => new(ItemId, count);
}
