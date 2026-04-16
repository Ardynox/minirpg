using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.Data;

public sealed class PzTileCatalogDocument
{
	[JsonPropertyName("entries")]
	public List<PzTileCatalogEntry> Entries { get; set; } = [];
}

public sealed class PzTileCatalogEntry
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("path")]
	public string Path { get; set; } = "";

	[JsonPropertyName("group")]
	public string Group { get; set; } = "";

	[JsonPropertyName("originalFileName")]
	public string OriginalFileName { get; set; } = "";

	[JsonPropertyName("displayNameZh")]
	public string DisplayNameZh { get; set; } = "";

	[JsonPropertyName("displayNameEn")]
	public string DisplayNameEn { get; set; } = "";

	[JsonPropertyName("tags")]
	public List<string> Tags { get; set; } = [];

	[JsonPropertyName("kind")]
	public string Kind { get; set; } = "sprite";

	[JsonPropertyName("confidence")]
	public string Confidence { get; set; } = "medium";

	[JsonPropertyName("mappingEligible")]
	public bool MappingEligible { get; set; }
}

public static class PzTileCatalogStore
{
	public const string DataPath = "pz_tile_catalog.json";

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

	public static PzTileCatalogDocument Load(string relativeDataPath = DataPath)
	{
		var json = GameDataLocator.ReadTextOrThrow(relativeDataPath);
		var doc = JsonSerializer.Deserialize<PzTileCatalogDocument>(json, JsonReadOptions) ?? new PzTileCatalogDocument();
		NormalizeInPlace(doc);
		return doc;
	}

	public static string Serialize(PzTileCatalogDocument document)
	{
		NormalizeInPlace(document);
		return JsonSerializer.Serialize(document, JsonWriteOptions);
	}

	public static string GetProjectFilePath(string relativeDataPath = DataPath) =>
		GameDataLocator.GetProjectDataPathOrThrow(relativeDataPath);

	private static void NormalizeInPlace(PzTileCatalogDocument document)
	{
		if (document.Entries.Count == 0)
			return;

		foreach (var entry in document.Entries)
		{
			entry.Id = entry.Id.Trim();
			entry.Path = PzTilePathUtility.NormalizeAssetPath(entry.Path);
			entry.Group = entry.Group.Trim();
			entry.OriginalFileName = entry.OriginalFileName.Trim();
			entry.DisplayNameZh = entry.DisplayNameZh.Trim();
			entry.DisplayNameEn = entry.DisplayNameEn.Trim();
			entry.Kind = string.IsNullOrWhiteSpace(entry.Kind) ? "sprite" : entry.Kind.Trim();
			entry.Confidence = string.IsNullOrWhiteSpace(entry.Confidence) ? "medium" : entry.Confidence.Trim();
			entry.Tags = entry.Tags
				.Where(static tag => !string.IsNullOrWhiteSpace(tag))
				.Select(static tag => tag.Trim())
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		document.Entries = document.Entries
			.OrderBy(static entry => entry.Group, StringComparer.OrdinalIgnoreCase)
			.ThenBy(static entry => entry.DisplayNameZh, StringComparer.OrdinalIgnoreCase)
			.ThenBy(static entry => entry.OriginalFileName, StringComparer.OrdinalIgnoreCase)
			.ThenBy(static entry => entry.Id, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}
}

public static class PzTilePathUtility
{
	public const string CopyRoot = "res://Assets/Art/PZ_Tiles_Copy";
	public const string LegacyRoot = "res://Assets/Art/PZ_Tiles";

	public static string NormalizeAssetPath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return string.Empty;

		var normalized = path.Trim().Replace('\\', '/');
		if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
			normalized = "res://" + normalized;
		else if (normalized.StartsWith("PZ_Tiles_Copy/", StringComparison.OrdinalIgnoreCase))
			normalized = $"{CopyRoot}/{normalized["PZ_Tiles_Copy/".Length..]}";
		else if (normalized.StartsWith("PZ_Tiles/", StringComparison.OrdinalIgnoreCase))
			normalized = $"{LegacyRoot}/{normalized["PZ_Tiles/".Length..]}";

		if (normalized.StartsWith(LegacyRoot, StringComparison.OrdinalIgnoreCase))
			return $"{CopyRoot}{normalized[LegacyRoot.Length..]}";

		if (normalized.StartsWith(CopyRoot, StringComparison.OrdinalIgnoreCase))
			return $"{CopyRoot}{normalized[CopyRoot.Length..]}";

		return normalized;
	}

	public static bool IsUnderCopyRoot(string? path) =>
		!string.IsNullOrWhiteSpace(path)
		&& NormalizeAssetPath(path).StartsWith(CopyRoot + "/", StringComparison.OrdinalIgnoreCase);

	public static string? GetCopyRelativePath(string? path)
	{
		var normalized = NormalizeAssetPath(path);
		return normalized.StartsWith(CopyRoot + "/", StringComparison.OrdinalIgnoreCase)
			? normalized[(CopyRoot.Length + 1)..]
			: null;
	}

	public static string GetDisplayDirectory(string? path)
	{
		var relative = GetCopyRelativePath(path);
		if (string.IsNullOrWhiteSpace(relative))
			return string.Empty;

		var index = relative.LastIndexOf('/');
		return index < 0 ? string.Empty : relative[..index];
	}

	public static string GetProjectFilePath(string resPath)
	{
		var normalized = NormalizeAssetPath(resPath);
		if (normalized.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
		{
			var projectRoot = GameDataLocator.GetProjectDataRoot();
			if (!string.IsNullOrWhiteSpace(projectRoot))
			{
				var repoRoot = Directory.GetParent(projectRoot)!.FullName;
				return Path.Combine(repoRoot, normalized["res://".Length..].Replace('/', Path.DirectorySeparatorChar));
			}
		}

		return normalized;
	}
}
