using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using MiniRPG.Core.Config;

namespace MiniRPG.Module.Editor;

internal static class BrushPreviewResolver
{
	private const string RegistryPath = "entity_render.json";
	private const string EnvironmentSpriteRoot = "res://Assets/Art/Tilesets/FantasyKingdom/FantasyKingdomTileset_Godot/Environment/Sprites";
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
	};
	private static Dictionary<string, PreviewRenderEntry>? _registry;

	public static BrushPreview? ResolveFixturePreview(string fixtureId)
	{
		return ResolveEntityPreview(fixtureId);
	}

	public static BrushPreview? ResolveFacilityPreview(string facilityId)
	{
		if (string.IsNullOrWhiteSpace(facilityId))
			return null;

		return ResolveEntityPreview($"facility_{facilityId}")
			?? ResolveEntityPreview(facilityId);
	}

	public static BrushPreview? ResolveEntityPreview(string entityId)
	{
		if (string.IsNullOrWhiteSpace(entityId))
			return null;

		if (!TryGetEntry(entityId, out var entry))
			return null;

		if (string.Equals(entry.Type, "texture", StringComparison.OrdinalIgnoreCase) &&
			!string.IsNullOrWhiteSpace(entry.TexturePath))
		{
			return new BrushPreview(
				entry.TexturePath,
				ResolveTexturePreviewRegion(entry));
		}

		if (string.Equals(entry.Type, "tile", StringComparison.OrdinalIgnoreCase) &&
			!string.IsNullOrWhiteSpace(entry.TileName))
		{
			return new BrushPreview($"{EnvironmentSpriteRoot}/{entry.TileName}.png");
		}

		return null;
	}

	private static Rect2I? ResolveTexturePreviewRegion(PreviewRenderEntry entry)
	{
		if (entry.FrameWidth <= 0 || entry.FrameHeight <= 0)
			return null;

		return new Rect2I(0, 0, entry.FrameWidth, entry.FrameHeight);
	}

	private static bool TryGetEntry(string fixtureId, out PreviewRenderEntry entry)
	{
		EnsureRegistryLoaded();
		if (_registry is { } registry && registry.TryGetValue(fixtureId, out var resolvedEntry))
		{
			entry = resolvedEntry;
			return true;
		}

		entry = new PreviewRenderEntry();
		return false;
	}

	private static void EnsureRegistryLoaded()
	{
		if (_registry != null)
			return;

		_registry = new Dictionary<string, PreviewRenderEntry>(StringComparer.Ordinal);
		if (!GameDataLocator.TryReadText(RegistryPath, out var json, out _))
			return;

		var entries = JsonSerializer.Deserialize<Dictionary<string, PreviewRenderEntry>>(json, JsonOptions);
		if (entries == null)
			return;

		foreach (var (id, entry) in entries)
			_registry[id] = entry;
	}

	private sealed class PreviewRenderEntry
	{
		public string Type { get; set; } = string.Empty;
		public string? TexturePath { get; set; }
		public int FrameWidth { get; set; }
		public int FrameHeight { get; set; }
		public string? TileName { get; set; }
	}
}
