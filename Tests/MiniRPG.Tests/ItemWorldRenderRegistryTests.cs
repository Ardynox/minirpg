using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class ItemWorldRenderRegistryTests
{
	[Fact]
	public void FromJson_ResolvesItemBeforeCategoryBeforeDefault()
	{
		const string json = """
		{
		  "default": { "kind": "tile", "value": "Misc B5_N" },
		  "categories": {
		    "consumable": { "kind": "tile", "value": "Misc B6_N" }
		  },
		  "items": {
		    "potion_hp": {
		      "kind": "texture",
		      "value": "res://Assets/Items/potion_hp.png",
		      "scale": [0.25, 0.5]
		    }
		  }
		}
		""";

		var registry = ItemWorldRenderRegistry.FromJson(json);

		Assert.True(registry.TryResolve("potion_hp", "consumable", out var itemSpec));
		Assert.Equal(ItemWorldRenderKind.Texture, itemSpec.Kind);
		Assert.Equal("res://Assets/Items/potion_hp.png", itemSpec.Value);
		Assert.Equal(new Vector2(0.25f, 0.5f), itemSpec.Scale);

		Assert.True(registry.TryResolve("unknown_item", "consumable", out var categorySpec));
		Assert.Equal(ItemWorldRenderKind.Tile, categorySpec.Kind);
		Assert.Equal("Misc B6_N", categorySpec.Value);
		Assert.Equal(GroundItemStackLayout.DefaultScale, categorySpec.Scale);

		Assert.True(registry.TryResolve("unknown_item", "unknown_category", out var defaultSpec));
		Assert.Equal(ItemWorldRenderKind.Tile, defaultSpec.Kind);
		Assert.Equal("Misc B5_N", defaultSpec.Value);
	}

	[Fact]
	public void GroundItemStackLayout_ClampsVisibleCountAndOffsets()
	{
		Assert.Equal(0, GroundItemStackLayout.GetVisibleCount(0));
		Assert.Equal(1, GroundItemStackLayout.GetVisibleCount(1));
		Assert.Equal(4, GroundItemStackLayout.GetVisibleCount(7));

		Assert.Equal(Vector2.Zero, GroundItemStackLayout.GetOffset(0));
		Assert.Equal(new Vector2(-10, -8), GroundItemStackLayout.GetOffset(1));
		Assert.Equal(new Vector2(10, -16), GroundItemStackLayout.GetOffset(2));
		Assert.Equal(new Vector2(0, -24), GroundItemStackLayout.GetOffset(3));
		Assert.Equal(new Vector2(0, -24), GroundItemStackLayout.GetOffset(99));
	}

	[Fact]
	public void FromJson_ResolvesCatalogTextureIntoTexturePath()
	{
		var catalogId = PzTileCatalogStore.Load().Entries
			.First(static entry => string.Equals(entry.Group, "杂项家电", StringComparison.OrdinalIgnoreCase))
			.Id;
		var json = $$"""
		{
		  "items": {
		    "generator": {
		      "kind": "catalog_texture",
		      "value": "{{catalogId}}",
		      "scale": [0.5, 0.5]
		    }
		  }
		}
		""";

		var registry = ItemWorldRenderRegistry.FromJson(json);

		Assert.True(registry.TryResolve("generator", "misc", out var spec));
		Assert.Equal(ItemWorldRenderKind.CatalogTexture, spec.Kind);
		Assert.StartsWith("res://Assets/Art/PZ_Tiles/", spec.Value, StringComparison.OrdinalIgnoreCase);
		Assert.EndsWith(".png", spec.Value, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(new Vector2(0.5f, 0.5f), spec.Scale);
	}

	[Fact]
	public void ItemWorldRenderMapping_CoversEveryPresetItemId()
	{
		var mappingPath = GetRepoPath("Data", "item_world_render.json");
		var itemsPath = GetRepoPath("Data", "items.json");

		var mappingRoot = JsonSerializer.Deserialize<ItemWorldRenderRegistry.ItemWorldRenderConfig>(
			File.ReadAllText(mappingPath));
		Assert.NotNull(mappingRoot);
		Assert.NotNull(mappingRoot!.Items);

		using var itemsDoc = JsonDocument.Parse(File.ReadAllText(itemsPath));
		var presetIds = itemsDoc.RootElement
			.EnumerateArray()
			.Select(item => item.GetProperty("id").GetString())
			.Where(static id => !string.IsNullOrWhiteSpace(id))
			.Cast<string>()
			.ToList();

		var configuredIds = new HashSet<string>(mappingRoot.Items!.Keys, StringComparer.Ordinal);
		var missingIds = presetIds
			.Where(id => !configuredIds.Contains(id))
			.OrderBy(static id => id, StringComparer.Ordinal)
			.ToArray();

		Assert.True(
			missingIds.Length == 0,
			"Missing item world render mapping ids: " + string.Join(", ", missingIds));
	}

	[Fact]
	public void ItemWorldRenderMapping_UsesSupportedKinds()
	{
		var mappingPath = GetRepoPath("Data", "item_world_render.json");
		var mappingRoot = JsonSerializer.Deserialize<ItemWorldRenderRegistry.ItemWorldRenderConfig>(
			File.ReadAllText(mappingPath));

		Assert.NotNull(mappingRoot);
		Assert.NotNull(mappingRoot!.Items);

		var unsupported = mappingRoot.Items!
			.Where(static pair => pair.Value.Kind is not ("tile" or "texture" or "catalog_texture"))
			.Select(static pair => $"{pair.Key}: {pair.Value.Kind}")
			.ToArray();

		Assert.True(unsupported.Length == 0, "Unsupported item world kinds: " + string.Join(", ", unsupported));
	}

	private static string GetRepoPath(params string[] segments)
	{
		var path = AppContext.BaseDirectory;
		for (var i = 0; i < 5; i++)
			path = Path.Combine(path, "..");

		return Path.GetFullPath(Path.Combine(path, Path.Combine(segments)));
	}
}
