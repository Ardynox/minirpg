using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace MiniRPG.Tests;

public sealed class TerrainTileMappingTests
{
	[Fact]
	public void TileMapping_CoversAllTerrainStringIds_InTerrainsConfig()
	{
		var terrainsPath = GetRepoPath("Data", "terrains.json");
		var mappingPath = GetRepoPath("Data", "tile_mapping.json");

		using var terrainsDoc = JsonDocument.Parse(
			File.ReadAllText(terrainsPath),
			new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
		using var mappingDoc = JsonDocument.Parse(File.ReadAllText(mappingPath));

		var mappedTerrains = ReadMappedTerrains(mappingDoc);
		var missing = terrainsDoc.RootElement
			.EnumerateArray()
			.Select(entry => entry.TryGetProperty("StringId", out var idProp) ? idProp.GetString() : null)
			.Where(static id => !string.IsNullOrWhiteSpace(id))
			.Cast<string>()
			.Where(id => !mappedTerrains.Contains(id))
			.OrderBy(static id => id, StringComparer.Ordinal)
			.ToArray();

		Assert.True(
			missing.Length == 0,
			"Missing terrain tile mappings: " + string.Join(", ", missing));
	}

	[Fact]
	public void OreTerrains_HaveExplicitTileMappings()
	{
		var mappingPath = GetRepoPath("Data", "tile_mapping.json");
		using var mappingDoc = JsonDocument.Parse(File.ReadAllText(mappingPath));
		var mappedTerrains = ReadMappedTerrains(mappingDoc);

		var expectedOreIds = new[] { "ore_coal", "ore_iron", "ore_copper", "ore_gold", "ore_crystal" };
		var missingOreMappings = expectedOreIds.Where(id => !mappedTerrains.Contains(id)).ToArray();

		Assert.True(
			missingOreMappings.Length == 0,
			"Missing ore terrain tile mappings: " + string.Join(", ", missingOreMappings));
	}

	private static HashSet<string> ReadMappedTerrains(JsonDocument mappingDoc)
	{
		var mappedTerrains = new HashSet<string>(StringComparer.Ordinal);
		if (mappingDoc.RootElement.TryGetProperty("terrain", out var terrainMap)
			&& terrainMap.ValueKind == JsonValueKind.Object)
		{
			foreach (var property in terrainMap.EnumerateObject())
				mappedTerrains.Add(property.Name);
		}

		return mappedTerrains;
	}

	private static string GetRepoPath(params string[] segments)
	{
		var path = AppContext.BaseDirectory;
		for (var i = 0; i < 5; i++)
			path = Path.Combine(path, "..");

		return Path.GetFullPath(Path.Combine(path, Path.Combine(segments)));
	}
}
