using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.Data;

public sealed class VoxelTileMappingDocument
{
	[JsonPropertyName("entries")]
	public List<VoxelTileMappingEntry> Entries { get; set; } = [];
}

public sealed class VoxelTileMappingEntry
{
	[JsonPropertyName("terrainId")]
	public string TerrainId { get; set; } = "";

	[JsonPropertyName("category")]
	public string? Category { get; set; }

	[JsonPropertyName("topTilePath")]
	public string? TopTilePath { get; set; }

	[JsonPropertyName("topIsIso")]
	public bool TopIsIso { get; set; }

	[JsonPropertyName("leftSideMode")]
	public string? LeftSideMode { get; set; }

	[JsonPropertyName("leftSideTilePath")]
	public string? LeftSideTilePath { get; set; }

	[JsonPropertyName("leftSideColor")]
	public string? LeftSideColor { get; set; }

	[JsonPropertyName("leftIsIso")]
	public bool LeftIsIso { get; set; }

	[JsonPropertyName("rightSideMode")]
	public string? RightSideMode { get; set; }

	[JsonPropertyName("rightSideTilePath")]
	public string? RightSideTilePath { get; set; }

	[JsonPropertyName("rightSideColor")]
	public string? RightSideColor { get; set; }

	[JsonPropertyName("rightIsIso")]
	public bool RightIsIso { get; set; }

	[JsonPropertyName("topScaleX")]
	public float TopScaleX { get; set; } = 1.0f;

	[JsonPropertyName("topScaleY")]
	public float TopScaleY { get; set; } = 1.0f;

	[JsonPropertyName("topOffsetX")]
	public float TopOffsetX { get; set; }

	[JsonPropertyName("topOffsetY")]
	public float TopOffsetY { get; set; }

	[JsonPropertyName("leftOffsetX")]
	public float LeftOffsetX { get; set; }

	[JsonPropertyName("leftOffsetY")]
	public float LeftOffsetY { get; set; }

	[JsonPropertyName("leftHeight")]
	public int LeftHeight { get; set; }

	[JsonPropertyName("rightOffsetX")]
	public float RightOffsetX { get; set; }

	[JsonPropertyName("rightOffsetY")]
	public float RightOffsetY { get; set; }

	[JsonPropertyName("rightHeight")]
	public int RightHeight { get; set; }

	[JsonPropertyName("showLeftSide")]
	public bool ShowLeftSide { get; set; } = true;

	[JsonPropertyName("showRightSide")]
	public bool ShowRightSide { get; set; } = true;
}

public static class VoxelTileMappingStore
{
	public const string DataPath = "voxel_tile_mapping.json";

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

	public static VoxelTileMappingDocument Load(string relativeDataPath = DataPath)
	{
		var json = GameDataLocator.ReadTextOrThrow(relativeDataPath);
		var document = JsonSerializer.Deserialize<VoxelTileMappingDocument>(json, JsonReadOptions) ?? new VoxelTileMappingDocument();
		NormalizeInPlace(document);
		return document;
	}

	public static string Serialize(VoxelTileMappingDocument document)
	{
		NormalizeInPlace(document);
		return JsonSerializer.Serialize(document, JsonWriteOptions);
	}

	public static string GetProjectFilePath(string relativeDataPath = DataPath) =>
		GameDataLocator.GetProjectDataPathOrThrow(relativeDataPath);

	private static void NormalizeInPlace(VoxelTileMappingDocument document)
	{
		if (document.Entries.Count == 0)
			return;

		foreach (var entry in document.Entries)
		{
			entry.TerrainId = entry.TerrainId.Trim();
			entry.Category = NormalizeNullable(entry.Category);
			entry.TopTilePath = NormalizeNullableAssetPath(entry.TopTilePath);
			entry.LeftSideMode = NormalizeNullable(entry.LeftSideMode);
			entry.LeftSideTilePath = NormalizeNullableAssetPath(entry.LeftSideTilePath);
			entry.LeftSideColor = NormalizeNullable(entry.LeftSideColor);
			entry.RightSideMode = NormalizeNullable(entry.RightSideMode);
			entry.RightSideTilePath = NormalizeNullableAssetPath(entry.RightSideTilePath);
			entry.RightSideColor = NormalizeNullable(entry.RightSideColor);
		}

		document.Entries = document.Entries
			.Where(static entry => !string.IsNullOrWhiteSpace(entry.TerrainId))
			.OrderBy(static entry => entry.TerrainId, StringComparer.OrdinalIgnoreCase)
			.ToList();
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
