using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class PzTileCatalogAndVoxelMappingTests
{
	[Fact]
	public void PzTileCatalog_CoversEveryPngUnderRoot()
	{
		var catalog = PzTileCatalogStore.Load();
		var repoRoot = GetRepoRoot();
		var pzTilesRoot = Path.Combine(repoRoot, "Assets", "Art", "PZ_Tiles");
		var expectedPaths = Directory.EnumerateFiles(pzTilesRoot, "*.png", SearchOption.AllDirectories)
			.Select(path => ToResPath(repoRoot, path))
			.Select(PzTilePathUtility.NormalizeAssetPath)
			.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		var actualPaths = catalog.Entries
			.Select(entry => PzTilePathUtility.NormalizeAssetPath(entry.Path))
			.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		var missing = expectedPaths.Except(actualPaths, StringComparer.OrdinalIgnoreCase).ToArray();
		var unexpected = actualPaths.Except(expectedPaths, StringComparer.OrdinalIgnoreCase).ToArray();

		Assert.True(missing.Length == 0, "Missing catalog entries: " + string.Join(", ", missing.Take(20)));
		Assert.True(unexpected.Length == 0, "Unexpected catalog entries: " + string.Join(", ", unexpected.Take(20)));
		Assert.Equal(expectedPaths.Length, actualPaths.Length);
	}

	[Fact]
	public void PzTileCatalog_UsesUniqueIds_ExistingPaths_AndRootOnly()
	{
		var catalog = PzTileCatalogStore.Load();
		var duplicateIds = catalog.Entries
			.GroupBy(static entry => entry.Id, StringComparer.OrdinalIgnoreCase)
			.Where(static group => group.Count() > 1)
			.Select(static group => group.Key)
			.ToArray();
		var invalidRootPaths = catalog.Entries
			.Select(static entry => entry.Path)
			.Where(path => !PzTilePathUtility.IsUnderRoot(path))
			.ToArray();
		var missingFiles = catalog.Entries
			.Select(static entry => entry.Path)
			.Where(path => !File.Exists(PzTilePathUtility.GetProjectFilePath(path)))
			.ToArray();

		Assert.True(duplicateIds.Length == 0, "Duplicate catalog ids: " + string.Join(", ", duplicateIds));
		Assert.True(invalidRootPaths.Length == 0, "Catalog entries outside PZ_Tiles root: " + string.Join(", ", invalidRootPaths.Take(20)));
		Assert.True(missingFiles.Length == 0, "Catalog file path missing on disk: " + string.Join(", ", missingFiles.Take(20)));
	}

	[Fact]
	public void PzTileCatalog_UsageMetadata_IsWellFormed()
	{
		var catalog = PzTileCatalogStore.Load();
		var allowedPlacements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			string.Empty,
			"floor",
			"wall",
			"roof",
			"object",
			"mixed",
		};

		var invalidPlacements = catalog.Entries
			.Where(entry => !allowedPlacements.Contains(entry.Placement))
			.Select(entry => $"{entry.Id}: {entry.Placement}")
			.ToArray();
		var invalidUsageDomains = catalog.Entries
			.Where(entry => entry.UsageDomains.Any(static value => string.IsNullOrWhiteSpace(value)))
			.Select(static entry => entry.Id)
			.ToArray();
		var invalidUsageRoles = catalog.Entries
			.Where(entry => entry.UsageRoles.Any(static value => string.IsNullOrWhiteSpace(value)))
			.Select(static entry => entry.Id)
			.ToArray();

		Assert.True(invalidPlacements.Length == 0, "Invalid catalog placements: " + string.Join(", ", invalidPlacements.Take(20)));
		Assert.True(invalidUsageDomains.Length == 0, "Invalid usageDomains entries: " + string.Join(", ", invalidUsageDomains.Take(20)));
		Assert.True(invalidUsageRoles.Length == 0, "Invalid usageRoles entries: " + string.Join(", ", invalidUsageRoles.Take(20)));
	}

	[Fact]
	public void PzTilePathUtility_IsPzTilesAssetPath_OnlyAcceptsRoot()
	{
		Assert.True(PzTilePathUtility.IsPzTilesAssetPath("res://Assets/Art/PZ_Tiles/overlays/trash_01_0.png"));
		Assert.True(PzTilePathUtility.IsPzTilesAssetPath("Assets/Art/PZ_Tiles/overlays/trash_01_0.png"));
		Assert.True(PzTilePathUtility.IsPzTilesAssetPath("PZ_Tiles/overlays/trash_01_0.png"));
		Assert.False(PzTilePathUtility.IsPzTilesAssetPath("res://Assets/Art/PZ_Tiles_Copy/overlays/trash_01_0.png"));
		Assert.False(PzTilePathUtility.IsPzTilesAssetPath("res://Assets/Art/Generated/voxel_tiles/grass_top.png"));
		Assert.False(PzTilePathUtility.IsPzTilesAssetPath(null));
	}

	[Fact]
	public void VoxelTileMapping_CoversEveryRuntimeTerrainExceptVoidAndAir()
	{
		TestSupport.EnsureGameplayDataLoaded();

		var mappingDocument = VoxelTileMappingStore.Load();
		var expectedTerrainIds = TerrainRegistry.All
			.Where(static terrain => terrain != null)
			.Select(static terrain => terrain.StringId)
			.Where(static terrainId => terrainId is not Terrains.Void and not Terrains.Air)
			.OrderBy(static terrainId => terrainId, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		var mappedTerrainIds = mappingDocument.Entries
			.Select(static entry => entry.TerrainId)
			.OrderBy(static terrainId => terrainId, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		var missing = expectedTerrainIds.Except(mappedTerrainIds, StringComparer.OrdinalIgnoreCase).ToArray();
		var extra = mappedTerrainIds.Except(expectedTerrainIds, StringComparer.OrdinalIgnoreCase).ToArray();

		Assert.True(missing.Length == 0, "Missing terrain mappings: " + string.Join(", ", missing));
		Assert.True(extra.Length == 0, "Unexpected terrain mappings: " + string.Join(", ", extra));
		Assert.Equal(expectedTerrainIds.Length, mappedTerrainIds.Length);
	}

	[Fact]
	public void VoxelTileMapping_UsesCatalogBackedTopTiles_ExplicitSides_AndNoLegacyCopyPaths()
	{
		TestSupport.EnsureGameplayDataLoaded();

		var catalogPaths = PzTileCatalogStore.Load().Entries
			.Select(static entry => PzTilePathUtility.NormalizeAssetPath(entry.Path))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var mappingDocument = VoxelTileMappingStore.Load();
		var missingTopPaths = new List<string>();
		var invalidLeftSides = new List<string>();
		var invalidRightSides = new List<string>();
		var legacyPaths = new List<string>();

		foreach (var entry in mappingDocument.Entries)
		{
			if (!string.IsNullOrWhiteSpace(entry.TopTilePath))
			{
				var normalizedTop = PzTilePathUtility.NormalizeAssetPath(entry.TopTilePath);
				if (normalizedTop.Contains("/PZ_Tiles_Copy/", StringComparison.OrdinalIgnoreCase))
					legacyPaths.Add($"{entry.TerrainId}: {entry.TopTilePath}");
				if (!catalogPaths.Contains(normalizedTop))
					missingTopPaths.Add($"{entry.TerrainId}: {normalizedTop}");
			}
			else
			{
				missingTopPaths.Add($"{entry.TerrainId}: <missing topTilePath>");
			}

			if (!IsValidExplicitSide(entry.LeftSideMode, entry.LeftSideTilePath, entry.LeftSideColor, catalogPaths, out var leftIssue))
				invalidLeftSides.Add($"{entry.TerrainId}: {leftIssue}");
			if (!IsValidExplicitSide(entry.RightSideMode, entry.RightSideTilePath, entry.RightSideColor, catalogPaths, out var rightIssue))
				invalidRightSides.Add($"{entry.TerrainId}: {rightIssue}");

			foreach (var path in EnumerateTilePaths(entry))
			{
				if (path.Contains("/PZ_Tiles_Copy/", StringComparison.OrdinalIgnoreCase))
					legacyPaths.Add($"{entry.TerrainId}: {path}");
			}
		}

		Assert.True(missingTopPaths.Count == 0, "Mapping topTilePath not found in catalog: " + string.Join(" | ", missingTopPaths));
		Assert.True(invalidLeftSides.Count == 0, "Invalid left side mapping: " + string.Join(" | ", invalidLeftSides));
		Assert.True(invalidRightSides.Count == 0, "Invalid right side mapping: " + string.Join(" | ", invalidRightSides));
		Assert.True(legacyPaths.Count == 0, "Legacy PZ_Tiles_Copy path remains: " + string.Join(" | ", legacyPaths));
	}

	[Fact]
	public void TerrainAtlas_LoadCustomMappings_CoversEveryRuntimeTerrain()
	{
		TestSupport.EnsureGameplayDataLoaded();

		var atlas = new TerrainAtlas();
		var method = typeof(TerrainAtlas).GetMethod("LoadCustomMappings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		var field = typeof(TerrainAtlas).GetField("_customMappings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

		Assert.NotNull(method);
		Assert.NotNull(field);

		method!.Invoke(atlas, null);

		var customMappings = Assert.IsAssignableFrom<IDictionary<string, VoxelTileMappingEntry>>(field!.GetValue(atlas));
		var expectedTerrainIds = TerrainRegistry.All
			.Where(static terrain => terrain != null)
			.Select(static terrain => terrain.StringId)
			.Where(static terrainId => terrainId is not Terrains.Void and not Terrains.Air)
			.OrderBy(static terrainId => terrainId, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		Assert.Equal(expectedTerrainIds.Length, customMappings.Count);
		foreach (var terrainId in expectedTerrainIds)
			Assert.True(customMappings.ContainsKey(terrainId), $"TerrainAtlas missing custom mapping for {terrainId}");
	}

	[Fact]
	public void VoxelTileMapping_RoundTrips_IsoFlags_Offsets_Heights_AndVisibility()
	{
		var document = new VoxelTileMappingDocument
		{
			Entries =
			[
				new VoxelTileMappingEntry
				{
					TerrainId = "test_fixture",
					Category = "fixture",
					TopTilePath = "res://Assets/Art/PZ_Tiles/overlays/trash_01_0.png",
					TopIsIso = true,
					LeftSideMode = "texture",
					LeftSideTilePath = "res://Assets/Art/PZ_Tiles/overlays/trash_01_1.png",
					LeftIsIso = true,
					RightSideMode = "color",
					RightSideColor = "#abcdef",
					RightIsIso = false,
					TopScaleX = 1.35f,
					TopScaleY = 0.85f,
					TopOffsetX = 13f,
					TopOffsetY = -7f,
					LeftOffsetX = -12f,
					LeftOffsetY = 9f,
					LeftHeight = 144,
					RightOffsetX = 17f,
					RightOffsetY = -11f,
					RightHeight = 156,
					ShowLeftSide = false,
					ShowRightSide = true,
				},
			],
		};

		var json = VoxelTileMappingStore.Serialize(document);
		var roundTripped = JsonSerializer.Deserialize<VoxelTileMappingDocument>(
			json,
			new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true,
				ReadCommentHandling = JsonCommentHandling.Skip,
			});

		Assert.NotNull(roundTripped);
		var entry = Assert.Single(roundTripped!.Entries);
		Assert.Equal("test_fixture", entry.TerrainId);
		Assert.Equal("fixture", entry.Category);
		Assert.Equal("res://Assets/Art/PZ_Tiles/overlays/trash_01_0.png", entry.TopTilePath);
		Assert.True(entry.TopIsIso);
		Assert.Equal("texture", entry.LeftSideMode);
		Assert.Equal("res://Assets/Art/PZ_Tiles/overlays/trash_01_1.png", entry.LeftSideTilePath);
		Assert.True(entry.LeftIsIso);
		Assert.Equal("color", entry.RightSideMode);
		Assert.Equal("#abcdef", entry.RightSideColor);
		Assert.False(entry.RightIsIso);
		Assert.Equal(1.35f, entry.TopScaleX);
		Assert.Equal(0.85f, entry.TopScaleY);
		Assert.Equal(13f, entry.TopOffsetX);
		Assert.Equal(-7f, entry.TopOffsetY);
		Assert.Equal(-12f, entry.LeftOffsetX);
		Assert.Equal(9f, entry.LeftOffsetY);
		Assert.Equal(144, entry.LeftHeight);
		Assert.Equal(17f, entry.RightOffsetX);
		Assert.Equal(-11f, entry.RightOffsetY);
		Assert.Equal(156, entry.RightHeight);
		Assert.False(entry.ShowLeftSide);
		Assert.True(entry.ShowRightSide);
	}

	[Fact]
	public void PzWorldVisualRegistry_ReferencesValidCatalogEntries_AndCoversAllRuntimeTerrains()
	{
		TestSupport.EnsureGameplayDataLoaded();

		var catalog = PzTileCatalogStore.Load();
		var catalogIds = catalog.Entries
			.Select(static entry => entry.Id)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var registry = PzWorldVisualRegistryStore.Load();

		var expectedTerrainIds = TerrainRegistry.All
			.Where(static terrain => terrain != null)
			.Select(static terrain => terrain.StringId)
			.Where(static terrainId => terrainId is not Terrains.Void and not Terrains.Air)
			.OrderBy(static terrainId => terrainId, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		var actualTerrainIds = registry.Terrain
			.Select(static entry => entry.TerrainId)
			.OrderBy(static terrainId => terrainId, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		Assert.Equal(expectedTerrainIds, actualTerrainIds);

		var missingCatalogIds = new List<string>();
		foreach (var terrain in registry.Terrain)
		{
			CollectMissingCatalogId(terrain.TerrainId, terrain.TopCatalogId, catalogIds, missingCatalogIds);
			CollectMissingCatalogId(terrain.TerrainId, terrain.Left.CatalogId, catalogIds, missingCatalogIds);
			CollectMissingCatalogId(terrain.TerrainId, terrain.Right.CatalogId, catalogIds, missingCatalogIds);
		}

		foreach (var entry in registry.Fixture)
			CollectMissingCatalogIds(entry.Id, entry, catalogIds, missingCatalogIds);
		foreach (var entry in registry.ItemWorld.Items)
			CollectMissingCatalogIds(entry.Id, entry, catalogIds, missingCatalogIds);
		foreach (var entry in registry.ItemWorld.Categories)
			CollectMissingCatalogIds("category:" + entry.Id, entry, catalogIds, missingCatalogIds);
		CollectMissingCatalogIds("default", registry.ItemWorld.Default, catalogIds, missingCatalogIds);
		foreach (var entry in registry.Fx)
			CollectMissingCatalogIds(entry.Id, entry, catalogIds, missingCatalogIds);

		var invalidDecorPools = registry.DecorPools
			.Where(static entry => entry.CatalogIds.Count != entry.Weights.Count)
			.Select(static entry => entry.PoolId)
			.ToArray();
		var invalidDecorPoolIds = registry.DecorPools
			.SelectMany(pool => pool.CatalogIds
				.Where(catalogId => !catalogIds.Contains(catalogId))
				.Select(catalogId => $"{pool.PoolId}: {catalogId}"))
			.ToArray();

		Assert.True(missingCatalogIds.Count == 0, "Registry references missing catalog ids: " + string.Join(" | ", missingCatalogIds));
		Assert.True(invalidDecorPools.Length == 0, "Decor pool weights mismatch: " + string.Join(", ", invalidDecorPools));
		Assert.True(invalidDecorPoolIds.Length == 0, "Decor pool missing catalog ids: " + string.Join(", ", invalidDecorPoolIds));
	}

	[Fact]
	public void PzWorldVisualRegistry_GeneratesCompatVoxelMapping_WithoutLosingTerrainCoverage()
	{
		TestSupport.EnsureGameplayDataLoaded();

		var catalog = PzTileCatalogStore.Load();
		var registry = PzWorldVisualRegistryStore.Load();
		var generated = PzWorldVisualRegistryStore.GenerateVoxelTileMappingDocument(registry, catalog);
		var terrainIds = generated.Entries
			.Select(static entry => entry.TerrainId)
			.OrderBy(static entry => entry, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		var expectedTerrainIds = TerrainRegistry.All
			.Where(static terrain => terrain != null)
			.Select(static terrain => terrain.StringId)
			.Where(static terrainId => terrainId is not Terrains.Void and not Terrains.Air)
			.OrderBy(static terrainId => terrainId, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		Assert.Equal(expectedTerrainIds, terrainIds);
		Assert.All(generated.Entries, static entry =>
		{
			Assert.DoesNotContain("/PZ_Tiles_Copy/", entry.TopTilePath ?? string.Empty, StringComparison.OrdinalIgnoreCase);
			Assert.DoesNotContain("/PZ_Tiles_Copy/", entry.LeftSideTilePath ?? string.Empty, StringComparison.OrdinalIgnoreCase);
			Assert.DoesNotContain("/PZ_Tiles_Copy/", entry.RightSideTilePath ?? string.Empty, StringComparison.OrdinalIgnoreCase);
		});
	}

	private static bool IsValidExplicitSide(
		string? mode,
		string? tilePath,
		string? colorHex,
		HashSet<string> catalogPaths,
		out string issue)
	{
		if (string.Equals(mode, "color", StringComparison.OrdinalIgnoreCase))
		{
			if (TryParseColor(colorHex, out _))
			{
				issue = string.Empty;
				return true;
			}

			issue = $"invalid color '{colorHex}'";
			return false;
		}

		if (string.Equals(mode, "texture", StringComparison.OrdinalIgnoreCase))
		{
			if (!string.IsNullOrWhiteSpace(tilePath))
			{
				var normalized = PzTilePathUtility.NormalizeAssetPath(tilePath);
				if (catalogPaths.Contains(normalized))
				{
					issue = string.Empty;
					return true;
				}

				issue = $"missing texture '{normalized}'";
				return false;
			}

			issue = "missing side texture path";
			return false;
		}

		issue = $"side mode must be 'color' or 'texture', actual '{mode ?? "<null>"}'";
		return false;
	}

	private static IEnumerable<string> EnumerateTilePaths(VoxelTileMappingEntry entry)
	{
		if (!string.IsNullOrWhiteSpace(entry.TopTilePath))
			yield return PzTilePathUtility.NormalizeAssetPath(entry.TopTilePath);
		if (!string.IsNullOrWhiteSpace(entry.LeftSideTilePath))
			yield return PzTilePathUtility.NormalizeAssetPath(entry.LeftSideTilePath);
		if (!string.IsNullOrWhiteSpace(entry.RightSideTilePath))
			yield return PzTilePathUtility.NormalizeAssetPath(entry.RightSideTilePath);
	}

	private static bool TryParseColor(string? colorHex, out Color color)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(colorHex))
			{
				color = new Color(colorHex);
				return true;
			}
		}
		catch
		{
			// Ignore invalid colors.
		}

		color = Colors.Transparent;
		return false;
	}

	private static void CollectMissingCatalogIds(
		string ownerId,
		PzWorldVisualEntry? entry,
		HashSet<string> catalogIds,
		List<string> issues)
	{
		if (entry == null)
			return;

		CollectMissingCatalogId(ownerId, entry.CatalogId, catalogIds, issues);
		foreach (var catalogId in entry.CatalogIds)
			CollectMissingCatalogId(ownerId, catalogId, catalogIds, issues);
	}

	private static void CollectMissingCatalogId(
		string ownerId,
		string? catalogId,
		HashSet<string> catalogIds,
		List<string> issues)
	{
		if (string.IsNullOrWhiteSpace(catalogId))
			return;
		if (!catalogIds.Contains(catalogId))
			issues.Add($"{ownerId}: {catalogId}");
	}

	private static string GetRepoRoot()
	{
		var dataRoot = GameDataLocator.GetProjectDataRoot();
		Assert.False(string.IsNullOrWhiteSpace(dataRoot));
		return Directory.GetParent(dataRoot!)!.FullName;
	}

	private static string ToResPath(string repoRoot, string absolutePath)
	{
		var relativePath = Path.GetRelativePath(repoRoot, absolutePath).Replace('\\', '/');
		return $"res://{relativePath}";
	}
}
