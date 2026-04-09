using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MiniRPG.Core.Config;
using Xunit;

namespace MiniRPG.Tests;

public sealed class RenderMappingAuditTests
{
	[Fact]
	public void RenderMappingAudit_WritesReport_AndHasNoCriticalMissingMappings()
	{
		TestSupport.EnsureGameplayDataLoaded();

		var repoRoot = FindRepoRoot();
		var result = BuildAuditResult();
		var reportPath = WriteAuditReport(repoRoot, result);

		Assert.True(File.Exists(reportPath), $"Expected audit report to be generated: {reportPath}");
		Assert.True(result.MissingFactionFallbacks.Count == 0,
			"Missing faction fallback(s): " + string.Join(", ", result.MissingFactionFallbacks));
		Assert.True(result.MissingActorMappings.Count == 0,
			"Missing actor render mapping(s): " + string.Join(", ", result.MissingActorMappings));
		Assert.True(result.MissingItemMappings.Count == 0,
			"Missing item world render mapping(s): " + string.Join(", ", result.MissingItemMappings));
	}

	private static AuditResult BuildAuditResult()
	{
		var actorEntries = ReadActorEntries();
		var itemEntries = ReadItemEntries();
		var entityRenderKeys = ReadTopLevelObjectKeys("entity_render.json");
		var itemWorldRender = ReadJsonDocument("item_world_render.json");
		var tileMapping = ReadJsonDocument("tile_mapping.json");

		var factionFallbacks = new[] { "friendly", "hostile" };
		var missingFactionFallbacks = factionFallbacks
			.Where(f => !entityRenderKeys.Contains(f))
			.ToList();

		var missingActorMappings = new List<string>();
		foreach (var actor in actorEntries)
		{
			var hasDirect = entityRenderKeys.Contains(actor.Id);
			var hasRaceFallback = !string.IsNullOrWhiteSpace(actor.RaceId) && entityRenderKeys.Contains(actor.RaceId!);
			var hasFactionFallback = !string.IsNullOrWhiteSpace(actor.Faction) && entityRenderKeys.Contains(actor.Faction!);
			if (!hasDirect && !hasRaceFallback && !hasFactionFallback)
				missingActorMappings.Add(actor.Id);
		}

		var categories = ReadNestedObjectKeys(itemWorldRender, "categories");
		var itemMappings = ReadNestedObjectKeys(itemWorldRender, "items");
		var hasDefaultItemFallback = itemWorldRender.RootElement.TryGetProperty("default", out _);

		var missingItemMappings = new List<string>();
		foreach (var item in itemEntries)
		{
			var hasDirect = itemMappings.Contains(item.Id);
			var hasCategory = !string.IsNullOrWhiteSpace(item.Category) && categories.Contains(item.Category!);
			if (!hasDirect && !hasCategory && !hasDefaultItemFallback)
				missingItemMappings.Add(item.Id);
		}

		var missingTileMappingEntityFallbacks = factionFallbacks
			.Where(f => !HasNestedObjectKey(tileMapping, "entity", f))
			.ToList();

		var missingTileMappingItemFallbacks = new[] { "drop", "container" }
			.Where(k => !HasNestedObjectKey(tileMapping, "item", k))
			.ToList();

		return new AuditResult(
			missingFactionFallbacks,
			missingActorMappings,
			missingItemMappings,
			missingTileMappingEntityFallbacks,
			missingTileMappingItemFallbacks,
			actorEntries.Count,
			itemEntries.Count);
	}

	private static string WriteAuditReport(string repoRoot, AuditResult result)
	{
		var artifactsDir = Path.Combine(repoRoot, "Artifacts");
		Directory.CreateDirectory(artifactsDir);
		var reportPath = Path.Combine(artifactsDir, "render_mapping_audit_report.md");

		var sb = new StringBuilder();
		sb.AppendLine("# Render Mapping Audit Report");
		sb.AppendLine();
		sb.AppendLine($"Generated (UTC): {DateTime.UtcNow:O}");
		sb.AppendLine();
		sb.AppendLine("## Scope");
		sb.AppendLine("- Data/entity_render.json");
		sb.AppendLine("- Data/tile_mapping.json");
		sb.AppendLine("- Data/item_world_render.json");
		sb.AppendLine("- Data/actors.json");
		sb.AppendLine("- Data/items.json");
		sb.AppendLine();
		sb.AppendLine("## Summary");
		sb.AppendLine($"- actors_checked: {result.ActorCount}");
		sb.AppendLine($"- items_checked: {result.ItemCount}");
		sb.AppendLine($"- missing_faction_fallbacks: {result.MissingFactionFallbacks.Count}");
		sb.AppendLine($"- missing_actor_mappings: {result.MissingActorMappings.Count}");
		sb.AppendLine($"- missing_item_mappings: {result.MissingItemMappings.Count}");
		sb.AppendLine($"- missing_tile_mapping_entity_fallbacks: {result.MissingTileMappingEntityFallbacks.Count}");
		sb.AppendLine($"- missing_tile_mapping_item_fallbacks: {result.MissingTileMappingItemFallbacks.Count}");
		sb.AppendLine();
		AppendSection(sb, "Missing faction fallbacks (entity_render)", result.MissingFactionFallbacks);
		AppendSection(sb, "Missing actor mappings (actorId > raceId > faction)", result.MissingActorMappings);
		AppendSection(sb, "Missing item mappings (itemId > category > default)", result.MissingItemMappings);
		AppendSection(sb, "Missing tile_mapping.entity fallbacks", result.MissingTileMappingEntityFallbacks);
		AppendSection(sb, "Missing tile_mapping.item fallbacks", result.MissingTileMappingItemFallbacks);

		File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
		return reportPath;
	}

	private static void AppendSection(StringBuilder sb, string title, IReadOnlyList<string> entries)
	{
		sb.AppendLine($"## {title}");
		if (entries.Count == 0)
		{
			sb.AppendLine("- none");
		}
		else
		{
			foreach (var entry in entries.OrderBy(x => x, StringComparer.Ordinal))
				sb.AppendLine($"- {entry}");
		}
		sb.AppendLine();
	}

	private static HashSet<string> ReadTopLevelObjectKeys(string relativeDataPath)
	{
		using var doc = ReadJsonDocument(relativeDataPath);
		if (doc.RootElement.ValueKind != JsonValueKind.Object)
			return new HashSet<string>(StringComparer.Ordinal);

		var set = new HashSet<string>(StringComparer.Ordinal);
		foreach (var property in doc.RootElement.EnumerateObject())
			set.Add(property.Name);
		return set;
	}

	private static HashSet<string> ReadNestedObjectKeys(JsonDocument document, string propertyName)
	{
		if (!document.RootElement.TryGetProperty(propertyName, out var node)
			|| node.ValueKind != JsonValueKind.Object)
		{
			return new HashSet<string>(StringComparer.Ordinal);
		}

		var set = new HashSet<string>(StringComparer.Ordinal);
		foreach (var property in node.EnumerateObject())
			set.Add(property.Name);
		return set;
	}

	private static bool HasNestedObjectKey(JsonDocument document, string objectProperty, string key)
	{
		if (!document.RootElement.TryGetProperty(objectProperty, out var node)
			|| node.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		return node.TryGetProperty(key, out _);
	}

	private static List<ActorEntry> ReadActorEntries()
	{
		using var doc = ReadJsonDocument("actors.json");
		var result = new List<ActorEntry>();
		if (doc.RootElement.ValueKind != JsonValueKind.Array)
			return result;

		foreach (var actor in doc.RootElement.EnumerateArray())
		{
			if (actor.ValueKind != JsonValueKind.Object)
				continue;

			if (!TryReadString(actor, "id", out var id) || string.IsNullOrWhiteSpace(id))
				continue;

			TryReadString(actor, "raceId", out var raceId);
			TryReadString(actor, "faction", out var faction);
			result.Add(new ActorEntry(id!, raceId, faction));
		}

		return result;
	}

	private static List<ItemEntry> ReadItemEntries()
	{
		using var doc = ReadJsonDocument("items.json");
		var result = new List<ItemEntry>();
		if (doc.RootElement.ValueKind != JsonValueKind.Array)
			return result;

		foreach (var item in doc.RootElement.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object)
				continue;

			if (!TryReadString(item, "id", out var id) || string.IsNullOrWhiteSpace(id))
				continue;

			TryReadString(item, "category", out var category);
			result.Add(new ItemEntry(id!, category));
		}

		return result;
	}

	private static bool TryReadString(JsonElement obj, string propertyName, out string? value)
	{
		value = null;
		if (!obj.TryGetProperty(propertyName, out var prop))
			return false;
		if (prop.ValueKind != JsonValueKind.String)
			return false;

		value = prop.GetString();
		return true;
	}

	private static JsonDocument ReadJsonDocument(string relativeDataPath)
	{
		if (!GameDataLocator.TryReadText(relativeDataPath, out var json, out var sourceLabel))
			throw new FileNotFoundException($"Failed to read data file: {sourceLabel}");

		return JsonDocument.Parse(json);
	}

	private static string FindRepoRoot()
	{
		var cursor = new DirectoryInfo(AppContext.BaseDirectory);
		while (cursor != null)
		{
			var dataDir = Path.Combine(cursor.FullName, "Data");
			var testsDir = Path.Combine(cursor.FullName, "Tests");
			if (Directory.Exists(dataDir) && Directory.Exists(testsDir))
				return cursor.FullName;

			cursor = cursor.Parent;
		}

		throw new DirectoryNotFoundException("Unable to locate repository root for Artifacts output.");
	}

	private sealed record ActorEntry(string Id, string? RaceId, string? Faction);
	private sealed record ItemEntry(string Id, string? Category);

	private sealed record AuditResult(
		List<string> MissingFactionFallbacks,
		List<string> MissingActorMappings,
		List<string> MissingItemMappings,
		List<string> MissingTileMappingEntityFallbacks,
		List<string> MissingTileMappingItemFallbacks,
		int ActorCount,
		int ItemCount);
}
