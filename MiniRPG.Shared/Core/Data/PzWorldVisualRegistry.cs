using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.Data;

public sealed class PzWorldVisualRegistryDocument
{
	[JsonPropertyName("terrain")]
	public List<PzTerrainVisualEntry> Terrain { get; set; } = [];

	[JsonPropertyName("fixture")]
	public List<PzWorldVisualEntry> Fixture { get; set; } = [];

	[JsonPropertyName("itemWorld")]
	public PzWorldVisualCollectionSection ItemWorld { get; set; } = new();

	[JsonPropertyName("fx")]
	public List<PzWorldVisualEntry> Fx { get; set; } = [];

	[JsonPropertyName("decorPools")]
	public List<PzDecorPoolEntry> DecorPools { get; set; } = [];
}

public sealed class PzWorldVisualCollectionSection
{
	[JsonPropertyName("items")]
	public List<PzWorldVisualEntry> Items { get; set; } = [];

	[JsonPropertyName("categories")]
	public List<PzWorldVisualEntry> Categories { get; set; } = [];

	[JsonPropertyName("default")]
	public PzWorldVisualEntry? Default { get; set; }
}

public sealed class PzTerrainVisualEntry
{
	[JsonPropertyName("terrainId")]
	public string TerrainId { get; set; } = "";

	[JsonPropertyName("topCatalogId")]
	public string? TopCatalogId { get; set; }

	[JsonPropertyName("topPath")]
	public string? TopPath { get; set; }

	[JsonPropertyName("topIsIso")]
	public bool TopIsIso { get; set; }

	[JsonPropertyName("topScaleX")]
	public float TopScaleX { get; set; } = 1.0f;

	[JsonPropertyName("topScaleY")]
	public float TopScaleY { get; set; } = 1.0f;

	[JsonPropertyName("topOffsetX")]
	public float TopOffsetX { get; set; }

	[JsonPropertyName("topOffsetY")]
	public float TopOffsetY { get; set; }

	[JsonPropertyName("left")]
	public PzTerrainSideVisualSpec Left { get; set; } = new();

	[JsonPropertyName("right")]
	public PzTerrainSideVisualSpec Right { get; set; } = new();
}

public sealed class PzTerrainSideVisualSpec
{
	[JsonPropertyName("mode")]
	public string? Mode { get; set; }

	[JsonPropertyName("catalogId")]
	public string? CatalogId { get; set; }

	[JsonPropertyName("path")]
	public string? Path { get; set; }

	[JsonPropertyName("color")]
	public string? Color { get; set; }

	[JsonPropertyName("isIso")]
	public bool IsIso { get; set; }

	[JsonPropertyName("offsetX")]
	public float OffsetX { get; set; }

	[JsonPropertyName("offsetY")]
	public float OffsetY { get; set; }

	[JsonPropertyName("height")]
	public int Height { get; set; }

	[JsonPropertyName("visible")]
	public bool Visible { get; set; } = true;
}

public sealed class PzWorldVisualEntry
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("visualKind")]
	public string VisualKind { get; set; } = "catalog_texture";

	[JsonPropertyName("catalogId")]
	public string? CatalogId { get; set; }

	[JsonPropertyName("catalogIds")]
	public List<string> CatalogIds { get; set; } = [];

	[JsonPropertyName("path")]
	public string? Path { get; set; }

	[JsonPropertyName("paths")]
	public List<string> Paths { get; set; } = [];

	[JsonPropertyName("scale")]
	public PzVisualVector2 Scale { get; set; } = PzVisualVector2.One();

	[JsonPropertyName("offset")]
	public PzVisualVector2 Offset { get; set; } = PzVisualVector2.Zero();

	[JsonPropertyName("zBias")]
	public float ZBias { get; set; }

	[JsonPropertyName("placement")]
	public string Placement { get; set; } = "object";

	[JsonPropertyName("variantGroup")]
	public string? VariantGroup { get; set; }

	[JsonPropertyName("notes")]
	public string? Notes { get; set; }
}

public sealed class PzDecorPoolEntry
{
	[JsonPropertyName("poolId")]
	public string PoolId { get; set; } = "";

	[JsonPropertyName("placement")]
	public string Placement { get; set; } = "floor";

	[JsonPropertyName("catalogIds")]
	public List<string> CatalogIds { get; set; } = [];

	[JsonPropertyName("weights")]
	public List<float> Weights { get; set; } = [];

	[JsonPropertyName("density")]
	public float Density { get; set; } = 1.0f;

	[JsonPropertyName("tags")]
	public List<string> Tags { get; set; } = [];
}

public sealed class PzVisualVector2
{
	[JsonPropertyName("x")]
	public float X { get; set; }

	[JsonPropertyName("y")]
	public float Y { get; set; }

	public static PzVisualVector2 Zero() => new() { X = 0f, Y = 0f };

	public static PzVisualVector2 One() => new() { X = 1f, Y = 1f };
}

public sealed class PzItemWorldRenderCompatDocument
{
	[JsonPropertyName("default")]
	public PzItemWorldRenderCompatEntry? Default { get; set; }

	[JsonPropertyName("categories")]
	public Dictionary<string, PzItemWorldRenderCompatEntry> Categories { get; set; } = new(StringComparer.Ordinal);

	[JsonPropertyName("items")]
	public Dictionary<string, PzItemWorldRenderCompatEntry> Items { get; set; } = new(StringComparer.Ordinal);
}

public sealed class PzItemWorldRenderCompatEntry
{
	[JsonPropertyName("kind")]
	public string Kind { get; set; } = "texture";

	[JsonPropertyName("value")]
	public string Value { get; set; } = string.Empty;

	[JsonPropertyName("scale")]
	public float[] Scale { get; set; } = [1f, 1f];
}

public sealed class LegacyTileMappingDocument
{
	[JsonPropertyName("terrain")]
	public Dictionary<string, object> Terrain { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("entity")]
	public Dictionary<string, object> Entity { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("fixture")]
	public Dictionary<string, object> Fixture { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("item")]
	public Dictionary<string, object> Item { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class PzWorldVisualRegistryStore
{
	public const string DataPath = "pz_world_visual_registry.json";

	private static readonly JsonSerializerOptions JsonReadOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
	};

	private static readonly JsonSerializerOptions JsonWriteOptions = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	public static PzWorldVisualRegistryDocument Load(string relativeDataPath = DataPath)
	{
		var json = GameDataLocator.ReadTextOrThrow(relativeDataPath);
		var document = JsonSerializer.Deserialize<PzWorldVisualRegistryDocument>(json, JsonReadOptions) ?? new PzWorldVisualRegistryDocument();
		NormalizeInPlace(document);
		return document;
	}

	public static string Serialize(PzWorldVisualRegistryDocument document)
	{
		NormalizeInPlace(document);
		return JsonSerializer.Serialize(document, JsonWriteOptions);
	}

	public static string GetProjectFilePath(string relativeDataPath = DataPath) =>
		GameDataLocator.GetProjectDataPathOrThrow(relativeDataPath);

	public static VoxelTileMappingDocument GenerateVoxelTileMappingDocument(
		PzWorldVisualRegistryDocument document,
		PzTileCatalogDocument catalog)
	{
		NormalizeInPlace(document);
		var catalogById = BuildCatalogById(catalog);
		var result = new VoxelTileMappingDocument();
		foreach (var terrain in document.Terrain)
		{
			result.Entries.Add(new VoxelTileMappingEntry
			{
				TerrainId = terrain.TerrainId,
				Category = "terrain",
				TopTilePath = ResolveCatalogBackedPath(terrain.TopCatalogId, terrain.TopPath, catalogById),
				TopIsIso = terrain.TopIsIso,
				LeftSideMode = NormalizeNullable(terrain.Left.Mode),
				LeftSideTilePath = ResolveCatalogBackedPath(terrain.Left.CatalogId, terrain.Left.Path, catalogById),
				LeftSideColor = NormalizeNullable(terrain.Left.Color),
				LeftIsIso = terrain.Left.IsIso,
				RightSideMode = NormalizeNullable(terrain.Right.Mode),
				RightSideTilePath = ResolveCatalogBackedPath(terrain.Right.CatalogId, terrain.Right.Path, catalogById),
				RightSideColor = NormalizeNullable(terrain.Right.Color),
				RightIsIso = terrain.Right.IsIso,
				TopScaleX = terrain.TopScaleX,
				TopScaleY = terrain.TopScaleY,
				TopOffsetX = terrain.TopOffsetX,
				TopOffsetY = terrain.TopOffsetY,
				LeftOffsetX = terrain.Left.OffsetX,
				LeftOffsetY = terrain.Left.OffsetY,
				LeftHeight = terrain.Left.Height,
				RightOffsetX = terrain.Right.OffsetX,
				RightOffsetY = terrain.Right.OffsetY,
				RightHeight = terrain.Right.Height,
				ShowLeftSide = terrain.Left.Visible,
				ShowRightSide = terrain.Right.Visible,
			});
		}

		return result;
	}

	public static PzItemWorldRenderCompatDocument GenerateItemWorldRenderConfig(
		PzWorldVisualRegistryDocument document,
		PzTileCatalogDocument catalog)
	{
		NormalizeInPlace(document);
		var catalogById = BuildCatalogById(catalog);
		var config = new PzItemWorldRenderCompatDocument
		{
			Default = ToItemWorldEntry(document.ItemWorld.Default, catalogById),
			Categories = new Dictionary<string, PzItemWorldRenderCompatEntry>(StringComparer.Ordinal),
			Items = new Dictionary<string, PzItemWorldRenderCompatEntry>(StringComparer.Ordinal),
		};

		foreach (var category in document.ItemWorld.Categories)
		{
			if (ToItemWorldEntry(category, catalogById) is { } entry)
				config.Categories[category.Id] = entry;
		}

		foreach (var item in document.ItemWorld.Items)
		{
			if (ToItemWorldEntry(item, catalogById) is { } entry)
				config.Items[item.Id] = entry;
		}

		return config;
	}

	public static LegacyTileMappingDocument GenerateLegacyTileMappingDocument(
		PzWorldVisualRegistryDocument document,
		PzTileCatalogDocument catalog)
	{
		NormalizeInPlace(document);
		var catalogById = BuildCatalogById(catalog);
		var result = new LegacyTileMappingDocument();
		result.Terrain["void"] = "BLACK TILE";
		result.Terrain["air"] = "BLACK TILE";

		foreach (var terrain in document.Terrain)
		{
			var resolvedTopPath = ResolveCatalogBackedPath(terrain.TopCatalogId, terrain.TopPath, catalogById);
			if (!string.IsNullOrWhiteSpace(resolvedTopPath))
				result.Terrain[terrain.TerrainId] = resolvedTopPath;
		}

		foreach (var fixture in document.Fixture)
		{
			var resolved = ResolvePrimaryVisualValue(fixture, catalogById);
			if (!string.IsNullOrWhiteSpace(resolved))
				result.Fixture[fixture.Id] = resolved;
		}

		result.Entity["player"] = "Misc B1_N";
		result.Entity["hostile"] = "Misc B2_N";
		result.Entity["friendly"] = "Misc B3_N";
		result.Entity["fire"] = document.Fx
			.FirstOrDefault(entry => string.Equals(entry.Id, "fire_medium", StringComparison.OrdinalIgnoreCase))
			?.Id ?? "fire_medium";

		var defaultItemVisual = ResolvePrimaryVisualValue(document.ItemWorld.Default, catalogById);
		result.Item["drop"] = string.IsNullOrWhiteSpace(defaultItemVisual) ? "Misc B5_N" : defaultItemVisual;

		var containerVisual = document.ItemWorld.Items
			.FirstOrDefault(entry => string.Equals(entry.Id, "chest_wooden", StringComparison.OrdinalIgnoreCase));
		result.Item["container"] = ResolvePrimaryVisualValue(containerVisual, catalogById) ?? "Chest A1_N";

		return result;
	}

	public static string SerializeLegacyTileMapping(LegacyTileMappingDocument document) =>
		JsonSerializer.Serialize(document, JsonWriteOptions);

	public static string ResolveCatalogBackedPath(
		string? catalogId,
		string? path,
		IReadOnlyDictionary<string, PzTileCatalogEntry> catalogById)
	{
		if (!string.IsNullOrWhiteSpace(catalogId)
			&& catalogById.TryGetValue(catalogId.Trim(), out var catalogEntry))
		{
			return PzTilePathUtility.NormalizeAssetPath(catalogEntry.Path);
		}

		if (string.IsNullOrWhiteSpace(path))
			return string.Empty;

		return PzTilePathUtility.NormalizeAssetPath(path);
	}

	public static Dictionary<string, PzTileCatalogEntry> BuildCatalogById(PzTileCatalogDocument catalog)
	{
		var result = new Dictionary<string, PzTileCatalogEntry>(StringComparer.OrdinalIgnoreCase);
		foreach (var entry in catalog.Entries)
		{
			if (!string.IsNullOrWhiteSpace(entry.Id))
				result[entry.Id] = entry;
		}

		return result;
	}

	private static PzItemWorldRenderCompatEntry? ToItemWorldEntry(
		PzWorldVisualEntry? entry,
		IReadOnlyDictionary<string, PzTileCatalogEntry> catalogById)
	{
		if (entry == null || string.IsNullOrWhiteSpace(entry.Id))
			return null;

		var visualKind = NormalizeNullable(entry.VisualKind)?.ToLowerInvariant();
		var resolvedScale = entry.Scale ?? PzVisualVector2.One();
		switch (visualKind)
		{
			case "catalog_texture":
				if (!string.IsNullOrWhiteSpace(entry.CatalogId))
				{
					return new PzItemWorldRenderCompatEntry
					{
						Kind = "catalog_texture",
						Value = entry.CatalogId!,
						Scale = [resolvedScale.X, resolvedScale.Y],
					};
				}

				goto case "texture";
			case "texture":
				var path = ResolveCatalogBackedPath(entry.CatalogId, entry.Path, catalogById);
				if (string.IsNullOrWhiteSpace(path))
					return null;

				return new PzItemWorldRenderCompatEntry
				{
					Kind = "texture",
					Value = path,
					Scale = [resolvedScale.X, resolvedScale.Y],
				};
			default:
				return null;
		}
	}

	private static string? ResolvePrimaryVisualValue(
		PzWorldVisualEntry? entry,
		IReadOnlyDictionary<string, PzTileCatalogEntry> catalogById)
	{
		if (entry == null)
			return null;

		var direct = ResolveCatalogBackedPath(entry.CatalogId, entry.Path, catalogById);
		if (!string.IsNullOrWhiteSpace(direct))
			return direct;

		if (entry.CatalogIds.Count > 0)
		{
			foreach (var catalogId in entry.CatalogIds)
			{
				var resolved = ResolveCatalogBackedPath(catalogId, null, catalogById);
				if (!string.IsNullOrWhiteSpace(resolved))
					return resolved;
			}
		}

		foreach (var path in entry.Paths)
		{
			var resolved = PzTilePathUtility.NormalizeAssetPath(path);
			if (!string.IsNullOrWhiteSpace(resolved))
				return resolved;
		}

		return null;
	}

	private static void NormalizeInPlace(PzWorldVisualRegistryDocument document)
	{
		foreach (var terrain in document.Terrain)
			NormalizeTerrain(terrain);
		document.Terrain = document.Terrain
			.Where(static entry => !string.IsNullOrWhiteSpace(entry.TerrainId))
			.OrderBy(static entry => entry.TerrainId, StringComparer.OrdinalIgnoreCase)
			.ToList();

		document.Fixture = NormalizeWorldVisualList(document.Fixture);
		document.ItemWorld.Items = NormalizeWorldVisualList(document.ItemWorld.Items);
		document.ItemWorld.Categories = NormalizeWorldVisualList(document.ItemWorld.Categories);
		if (document.ItemWorld.Default != null)
			NormalizeWorldVisual(document.ItemWorld.Default);
		document.Fx = NormalizeWorldVisualList(document.Fx);

		foreach (var pool in document.DecorPools)
			NormalizeDecorPool(pool);
		document.DecorPools = document.DecorPools
			.Where(static entry => !string.IsNullOrWhiteSpace(entry.PoolId))
			.OrderBy(static entry => entry.PoolId, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static void NormalizeTerrain(PzTerrainVisualEntry entry)
	{
		entry.TerrainId = entry.TerrainId.Trim();
		entry.TopCatalogId = NormalizeNullable(entry.TopCatalogId);
		entry.TopPath = NormalizeNullableAssetPath(entry.TopPath);
		NormalizeTerrainSide(entry.Left);
		NormalizeTerrainSide(entry.Right);
	}

	private static void NormalizeTerrainSide(PzTerrainSideVisualSpec side)
	{
		side.Mode = NormalizeNullable(side.Mode);
		side.CatalogId = NormalizeNullable(side.CatalogId);
		side.Path = NormalizeNullableAssetPath(side.Path);
		side.Color = NormalizeNullable(side.Color);
	}

	private static List<PzWorldVisualEntry> NormalizeWorldVisualList(List<PzWorldVisualEntry> entries)
	{
		foreach (var entry in entries)
			NormalizeWorldVisual(entry);

		return entries
			.Where(static entry => !string.IsNullOrWhiteSpace(entry.Id))
			.OrderBy(static entry => entry.Id, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static void NormalizeWorldVisual(PzWorldVisualEntry entry)
	{
		entry.Id = entry.Id.Trim();
		entry.VisualKind = string.IsNullOrWhiteSpace(entry.VisualKind) ? "catalog_texture" : entry.VisualKind.Trim();
		entry.CatalogId = NormalizeNullable(entry.CatalogId);
		entry.Path = NormalizeNullableAssetPath(entry.Path);
		entry.CatalogIds = entry.CatalogIds
			.Where(static value => !string.IsNullOrWhiteSpace(value))
			.Select(static value => value.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		entry.Paths = entry.Paths
			.Where(static value => !string.IsNullOrWhiteSpace(value))
			.Select(PzTilePathUtility.NormalizeAssetPath)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		entry.Scale ??= PzVisualVector2.One();
		entry.Offset ??= PzVisualVector2.Zero();
		entry.Placement = string.IsNullOrWhiteSpace(entry.Placement) ? "object" : entry.Placement.Trim();
		entry.VariantGroup = NormalizeNullable(entry.VariantGroup);
		entry.Notes = NormalizeNullable(entry.Notes);
	}

	private static void NormalizeDecorPool(PzDecorPoolEntry entry)
	{
		entry.PoolId = entry.PoolId.Trim();
		entry.Placement = string.IsNullOrWhiteSpace(entry.Placement) ? "floor" : entry.Placement.Trim();
		entry.CatalogIds = entry.CatalogIds
			.Where(static value => !string.IsNullOrWhiteSpace(value))
			.Select(static value => value.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		entry.Weights = entry.Weights
			.Where(static value => value > 0f)
			.ToList();
		entry.Tags = entry.Tags
			.Where(static value => !string.IsNullOrWhiteSpace(value))
			.Select(static value => value.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (entry.Weights.Count != entry.CatalogIds.Count)
		{
			entry.Weights = Enumerable.Repeat(1.0f, entry.CatalogIds.Count).ToList();
		}

		entry.Density = entry.Density > 0f ? entry.Density : 1.0f;
	}

	private static string? NormalizeNullable(string? value) =>
		string.IsNullOrWhiteSpace(value) ? null : value.Trim();

	private static string? NormalizeNullableAssetPath(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return null;

		var normalized = PzTilePathUtility.NormalizeAssetPath(value);
		return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
	}
}
