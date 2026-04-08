using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MiniRPG.Core.Config;

namespace MiniRPG.Module.Editor;

public sealed class ResourceCatalogDocument
{
	[JsonPropertyName("version")]
	public int Version { get; set; } = ResourceCatalogStore.CurrentVersion;

	[JsonPropertyName("entries")]
	public List<ResourceCatalogEntry> Entries { get; set; } = [];

	public ResourceCatalogDocument DeepClone() => new()
	{
		Version = Version,
		Entries = Entries.Select(static entry => entry.DeepClone()).ToList(),
	};
}

public sealed class ResourceCatalogEntry
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = string.Empty;

	[JsonPropertyName("kind")]
	public string Kind { get; set; } = ResourceCatalogKinds.Tile;

	[JsonPropertyName("displayName")]
	public string DisplayName { get; set; } = string.Empty;

	[JsonPropertyName("description")]
	public string Description { get; set; } = string.Empty;

	[JsonPropertyName("tags")]
	public List<string> Tags { get; set; } = [];

	[JsonPropertyName("category")]
	public string Category { get; set; } = string.Empty;

	[JsonPropertyName("sourceMode")]
	public string SourceMode { get; set; } = ResourceCatalogSourceModes.Single;

	[JsonPropertyName("sourceImagePath")]
	public string? SourceImagePath { get; set; }

	[JsonPropertyName("sourceFolderPath")]
	public string? SourceFolderPath { get; set; }

	[JsonPropertyName("region")]
	public ResourceCatalogRegion? Region { get; set; }

	[JsonPropertyName("frames")]
	public List<ResourceCatalogFrame> Frames { get; set; } = [];

	[JsonPropertyName("fps")]
	public float? Fps { get; set; }

	public ResourceCatalogEntry DeepClone() => new()
	{
		Id = Id,
		Kind = Kind,
		DisplayName = DisplayName,
		Description = Description,
		Tags = [.. Tags],
		Category = Category,
		SourceMode = SourceMode,
		SourceImagePath = SourceImagePath,
		SourceFolderPath = SourceFolderPath,
		Region = Region?.DeepClone(),
		Frames = Frames.Select(static frame => frame.DeepClone()).ToList(),
		Fps = Fps,
	};
}

public sealed class ResourceCatalogFrame
{
	[JsonPropertyName("imagePath")]
	public string ImagePath { get; set; } = string.Empty;

	[JsonPropertyName("region")]
	public ResourceCatalogRegion? Region { get; set; }

	[JsonPropertyName("order")]
	public int Order { get; set; }

	public ResourceCatalogFrame DeepClone() => new()
	{
		ImagePath = ImagePath,
		Region = Region?.DeepClone(),
		Order = Order,
	};
}

public sealed class ResourceCatalogRegion
{
	[JsonPropertyName("x")]
	public int X { get; set; }

	[JsonPropertyName("y")]
	public int Y { get; set; }

	[JsonPropertyName("width")]
	public int Width { get; set; }

	[JsonPropertyName("height")]
	public int Height { get; set; }

	public ResourceCatalogRegion DeepClone() => new()
	{
		X = X,
		Y = Y,
		Width = Width,
		Height = Height,
	};
}

public static class ResourceCatalogKinds
{
	public const string Tile = "tile";
	public const string Animation = "animation";
}

public static class ResourceCatalogSourceModes
{
	public const string Single = "single";
	public const string Sheet = "sheet";
	public const string Frames = "frames";
}

public static class ResourceCatalogStore
{
	public const string CatalogPath = "resource_catalog.json";
	public const int CurrentVersion = 1;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	public static ResourceCatalogDocument Load()
	{
		string path;
		try
		{
			path = GameDataLocator.GetProjectDataPathOrThrow(CatalogPath);
		}
		catch
		{
			return CreateEmpty();
		}

		if (!File.Exists(path))
			return CreateEmpty();

		try
		{
			var json = File.ReadAllText(path);
			var document = JsonSerializer.Deserialize<ResourceCatalogDocument>(json, JsonOptions) ?? CreateEmpty();
			document.Version = CurrentVersion;
			document.Entries ??= [];
			NormalizeDocument(document);
			return document;
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[ResourceCatalogStore] Failed to load catalog: {ex.Message}");
			return CreateEmpty();
		}
	}

	public static void Save(ResourceCatalogDocument document)
	{
		var clone = document.DeepClone();
		clone.Version = CurrentVersion;
		NormalizeDocument(clone);

		try
		{
			var path = GameDataLocator.GetProjectDataPathOrThrow(CatalogPath);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			var json = JsonSerializer.Serialize(clone, JsonOptions);
			File.WriteAllText(path, json);
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Failed to save resource catalog: {ex.Message}", ex);
		}
	}

	public static ResourceCatalogDocument CreateEmpty() => new()
	{
		Version = CurrentVersion,
		Entries = [],
	};

	private static void NormalizeDocument(ResourceCatalogDocument document)
	{
		document.Entries ??= [];

		foreach (var entry in document.Entries)
		{
			entry.Tags = NormalizeTags(entry.Tags);
			entry.Frames ??= [];
			entry.Kind = NormalizeKind(entry.Kind);
			entry.SourceMode = NormalizeSourceMode(entry.SourceMode, entry.Kind);

			if (entry.Kind == ResourceCatalogKinds.Tile)
			{
				entry.Fps = null;
				entry.SourceFolderPath = string.IsNullOrWhiteSpace(entry.SourceFolderPath) ? null : entry.SourceFolderPath;
				entry.Frames = [];
			}
			else
			{
				entry.SourceImagePath = string.IsNullOrWhiteSpace(entry.SourceImagePath) ? null : entry.SourceImagePath;
				entry.Frames = entry.Frames
					.OrderBy(frame => frame.Order)
					.Select((frame, index) =>
					{
						frame.Order = index;
						return frame;
					})
					.ToList();
				entry.Fps = entry.Fps is > 0 ? entry.Fps : 10f;
			}
		}

		document.Entries = document.Entries
			.OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static string NormalizeKind(string? kind) =>
		string.Equals(kind, ResourceCatalogKinds.Animation, StringComparison.OrdinalIgnoreCase)
			? ResourceCatalogKinds.Animation
			: ResourceCatalogKinds.Tile;

	private static string NormalizeSourceMode(string? sourceMode, string kind)
	{
		if (kind == ResourceCatalogKinds.Animation)
			return ResourceCatalogSourceModes.Frames;

		if (string.Equals(sourceMode, ResourceCatalogSourceModes.Sheet, StringComparison.OrdinalIgnoreCase))
			return ResourceCatalogSourceModes.Sheet;

		return ResourceCatalogSourceModes.Single;
	}

	private static List<string> NormalizeTags(IEnumerable<string>? tags)
	{
		if (tags == null)
			return [];

		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var result = new List<string>();
		foreach (var tag in tags)
		{
			var trimmed = tag?.Trim();
			if (string.IsNullOrEmpty(trimmed) || !seen.Add(trimmed))
				continue;

			result.Add(trimmed);
		}

		return result;
	}
}
