using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MiniRPG.Core.Data;

namespace MiniRPG.Module.Render;

public enum ItemWorldRenderKind
{
	Tile,
	Texture,
	CatalogTexture,
}

public readonly record struct ItemWorldVisualSpec(
	ItemWorldRenderKind Kind,
	string Value,
	Vector2 Scale);

public static class GroundItemStackLayout
{
	public const int MaxVisibleItems = 4;
	public static readonly Vector2 DefaultScale = new(0.5f, 0.5f);
	public static readonly Vector2 BaseOffset = new(0, 32);
	private static readonly Vector2[] Offsets =
	[
		Vector2.Zero,
		new Vector2(-10, -8),
		new Vector2(10, -16),
		new Vector2(0, -24),
	];

	public static int GetVisibleCount(int totalCount) =>
		Math.Clamp(totalCount, 0, MaxVisibleItems);

	public static Vector2 GetOffset(int visibleIndex)
	{
		var clamped = Math.Clamp(visibleIndex, 0, Offsets.Length - 1);
		return Offsets[clamped];
	}
}

public sealed class ItemWorldRenderRegistry
{
	private static readonly Lazy<Dictionary<string, string>> CatalogTexturePaths = new(BuildCatalogTexturePaths);

	public static readonly ItemWorldRenderRegistry Empty = new(
		new Dictionary<string, ItemWorldVisualSpec>(StringComparer.Ordinal),
		new Dictionary<string, ItemWorldVisualSpec>(StringComparer.Ordinal),
		null);

	private readonly Dictionary<string, ItemWorldVisualSpec> _items;
	private readonly Dictionary<string, ItemWorldVisualSpec> _categories;
	private readonly ItemWorldVisualSpec? _defaultSpec;

	public ItemWorldRenderRegistry(
		Dictionary<string, ItemWorldVisualSpec>? items,
		Dictionary<string, ItemWorldVisualSpec>? categories,
		ItemWorldVisualSpec? defaultSpec)
	{
		_items = items ?? new Dictionary<string, ItemWorldVisualSpec>(StringComparer.Ordinal);
		_categories = categories ?? new Dictionary<string, ItemWorldVisualSpec>(StringComparer.Ordinal);
		_defaultSpec = defaultSpec;
	}

	public static ItemWorldRenderRegistry FromJson(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
			return Empty;

		var root = JsonSerializer.Deserialize<ItemWorldRenderConfig>(json);
		if (root == null)
			return Empty;

		return new ItemWorldRenderRegistry(
			BuildMap(root.Items),
			BuildMap(root.Categories),
			ToSpec(root.Default));
	}

	public bool TryResolve(string itemId, string? category, out ItemWorldVisualSpec spec)
	{
		if (!string.IsNullOrWhiteSpace(itemId) && _items.TryGetValue(itemId, out spec))
			return true;

		if (!string.IsNullOrWhiteSpace(category) && _categories.TryGetValue(category, out spec))
			return true;

		if (_defaultSpec is { } defaultSpec)
		{
			spec = defaultSpec;
			return true;
		}

		spec = default;
		return false;
	}

	private static Dictionary<string, ItemWorldVisualSpec> BuildMap(
		Dictionary<string, ItemWorldVisualEntry>? entries)
	{
		var result = new Dictionary<string, ItemWorldVisualSpec>(StringComparer.Ordinal);
		if (entries == null)
			return result;

		foreach (var (key, entry) in entries)
		{
			if (string.IsNullOrWhiteSpace(key))
				continue;

			if (ToSpec(entry) is { } spec)
				result[key] = spec;
		}

		return result;
	}

	private static ItemWorldVisualSpec? ToSpec(ItemWorldVisualEntry? entry)
	{
		if (entry == null
			|| string.IsNullOrWhiteSpace(entry.Kind)
			|| string.IsNullOrWhiteSpace(entry.Value))
			return null;

		var kind = entry.Kind.Trim().ToLowerInvariant() switch
		{
			"tile" => ItemWorldRenderKind.Tile,
			"texture" => ItemWorldRenderKind.Texture,
			"catalog_texture" => ItemWorldRenderKind.CatalogTexture,
			_ => (ItemWorldRenderKind?)null,
		};
		if (kind == null)
			return null;

		var value = kind.Value switch
		{
			ItemWorldRenderKind.CatalogTexture => ResolveCatalogTexturePath(entry.Value),
			_ => entry.Value,
		};
		if (string.IsNullOrWhiteSpace(value))
			return null;

		return new ItemWorldVisualSpec(
			kind.Value,
			value,
			ParseScale(entry.Scale));
	}

	private static string ResolveCatalogTexturePath(string catalogId)
	{
		if (string.IsNullOrWhiteSpace(catalogId))
			return string.Empty;

		return CatalogTexturePaths.Value.TryGetValue(catalogId.Trim(), out var path)
			? path
			: string.Empty;
	}

	private static Dictionary<string, string> BuildCatalogTexturePaths()
	{
		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var catalog = PzTileCatalogStore.Load();
		foreach (var entry in catalog.Entries)
		{
			if (!string.IsNullOrWhiteSpace(entry.Id))
				result[entry.Id] = PzTilePathUtility.NormalizeAssetPath(entry.Path);
		}

		return result;
	}

	private static Vector2 ParseScale(float[]? scale)
	{
		if (scale is [var x, var y])
		{
			return new Vector2(
				x > 0f ? x : GroundItemStackLayout.DefaultScale.X,
				y > 0f ? y : GroundItemStackLayout.DefaultScale.Y);
		}

		return GroundItemStackLayout.DefaultScale;
	}

	public sealed class ItemWorldRenderConfig
	{
		[JsonPropertyName("items")]
		public Dictionary<string, ItemWorldVisualEntry>? Items { get; set; }

		[JsonPropertyName("categories")]
		public Dictionary<string, ItemWorldVisualEntry>? Categories { get; set; }

		[JsonPropertyName("default")]
		public ItemWorldVisualEntry? Default { get; set; }
	}

	public sealed class ItemWorldVisualEntry
	{
		[JsonPropertyName("kind")]
		public string Kind { get; set; } = "";

		[JsonPropertyName("value")]
		public string Value { get; set; } = "";

		[JsonPropertyName("scale")]
		public float[]? Scale { get; set; }
	}
}
