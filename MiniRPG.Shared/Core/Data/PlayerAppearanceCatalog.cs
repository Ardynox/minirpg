using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Config;
namespace MiniRPG.Core.Data;

public sealed class PlayerAppearanceCatalogEntry
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = string.Empty;

	[JsonPropertyName("displayName")]
	public string DisplayName { get; set; } = string.Empty;

	[JsonPropertyName("sheetDir")]
	public string SheetDir { get; set; } = string.Empty;

	[JsonPropertyName("defaultAnim")]
	public string DefaultAnim { get; set; } = "Idle";
}

public static class PlayerAppearanceCatalog
{
	private const string CatalogPath = "player_appearances.json";

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
	};

	private static readonly List<PlayerAppearanceCatalogEntry> BuiltInEntries =
	[
		new()
		{
			Id = GameState.DefaultPlayerAppearanceId,
			DisplayName = "Player 1",
			SheetDir = "res://Assets/Art/Tilesets/FantasyKingdom/FantasyKingdomTileset_Godot/Characters/player1",
			DefaultAnim = "Idle",
		},
		new()
		{
			Id = "npc1",
			DisplayName = "NPC 1",
			SheetDir = "res://Assets/Art/Tilesets/FantasyKingdom/FantasyKingdomTileset_Godot/Characters/NPC1",
			DefaultAnim = "Idle",
		},
		new()
		{
			Id = "npc2",
			DisplayName = "NPC 2",
			SheetDir = "res://Assets/Art/Tilesets/FantasyKingdom/FantasyKingdomTileset_Godot/Characters/NPC2",
			DefaultAnim = "Idle",
		},
		new()
		{
			Id = "npc3",
			DisplayName = "NPC 3",
			SheetDir = "res://Assets/Art/Tilesets/FantasyKingdom/FantasyKingdomTileset_Godot/Characters/NPC3",
			DefaultAnim = "Idle",
		},
		new()
		{
			Id = "enemy1",
			DisplayName = "Enemy 1",
			SheetDir = "res://Assets/Art/Tilesets/FantasyKingdom/FantasyKingdomTileset_Godot/Characters/Enemy 1",
			DefaultAnim = "Idle",
		},
		new()
		{
			Id = "enemy2",
			DisplayName = "Enemy 2",
			SheetDir = "res://Assets/Art/Tilesets/FantasyKingdom/FantasyKingdomTileset_Godot/Characters/Enemy 2",
			DefaultAnim = "Idle",
		},
		new()
		{
			Id = "enemy3",
			DisplayName = "Enemy 3",
			SheetDir = "res://Assets/Art/Tilesets/FantasyKingdom/FantasyKingdomTileset_Godot/Characters/Enemy 3",
			DefaultAnim = "Idle",
		},
	];

	private static readonly Dictionary<string, PlayerAppearanceCatalogEntry> EntryMap = new(StringComparer.Ordinal);
	private static readonly List<PlayerAppearanceCatalogEntry> Entries = [];
	private static bool _initialized;

	public static IReadOnlyList<PlayerAppearanceCatalogEntry> All
	{
		get
		{
			EnsureInitialized();
			return Entries;
		}
	}

	public static void LoadProjectCatalog()
	{
		EnsureInitialized();
		if (!GameDataLocator.TryReadText(CatalogPath, out var json, out _))
			return;

		try
		{
			var parsed = JsonSerializer.Deserialize<List<PlayerAppearanceCatalogEntry>>(json, JsonOptions);
			if (parsed == null || parsed.Count == 0)
				return;

			var filtered = parsed
				.Where(static entry =>
					!string.IsNullOrWhiteSpace(entry.Id)
					&& !string.IsNullOrWhiteSpace(entry.SheetDir))
				.ToList();
			if (filtered.Count == 0)
				return;

			Entries.Clear();
			EntryMap.Clear();
			foreach (var entry in filtered)
			{
				entry.Id = entry.Id.Trim();
				entry.DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName)
					? entry.Id
					: entry.DisplayName.Trim();
				entry.SheetDir = entry.SheetDir.TrimEnd('/');
				entry.DefaultAnim = string.IsNullOrWhiteSpace(entry.DefaultAnim)
					? "Idle"
					: entry.DefaultAnim.Trim();

				if (EntryMap.ContainsKey(entry.Id))
					continue;

				Entries.Add(entry);
				EntryMap[entry.Id] = entry;
			}

			if (!EntryMap.ContainsKey(GameState.DefaultPlayerAppearanceId))
				ResetToBuiltIns();
		}
		catch
		{
			ResetToBuiltIns();
		}
	}

	public static PlayerAppearanceCatalogEntry GetOrDefault(string? id)
	{
		EnsureInitialized();
		return EntryMap.GetValueOrDefault(NormalizeId(id))
			?? EntryMap[GameState.DefaultPlayerAppearanceId];
	}

	public static string NormalizeId(string? id)
	{
		EnsureInitialized();
		if (string.IsNullOrWhiteSpace(id))
			return GameState.DefaultPlayerAppearanceId;

		var trimmed = id.Trim();
		return EntryMap.ContainsKey(trimmed)
			? trimmed
			: GameState.DefaultPlayerAppearanceId;
	}

	private static void EnsureInitialized()
	{
		if (_initialized)
			return;

		ResetToBuiltIns();
		_initialized = true;
	}

	private static void ResetToBuiltIns()
	{
		Entries.Clear();
		EntryMap.Clear();
		foreach (var entry in BuiltInEntries)
		{
			var clone = new PlayerAppearanceCatalogEntry
			{
				Id = entry.Id,
				DisplayName = entry.DisplayName,
				SheetDir = entry.SheetDir,
				DefaultAnim = entry.DefaultAnim,
			};
			Entries.Add(clone);
			EntryMap[clone.Id] = clone;
		}
	}
}
