using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.Map;

public sealed class PresetScenarioDef
{
	[JsonPropertyName("id")]
	public required string Id { get; set; }

	[JsonPropertyName("displayNameKey")]
	public required string DisplayNameKey { get; set; }

	[JsonPropertyName("descriptionKey")]
	public required string DescriptionKey { get; set; }

	[JsonPropertyName("sortOrder")]
	public int SortOrder { get; set; }

	[JsonPropertyName("templatePath")]
	public required string TemplatePath { get; set; }
}

internal sealed class PresetScenarioManifest
{
	[JsonPropertyName("scenarios")]
	public List<PresetScenarioDef> Scenarios { get; set; } = [];
}

public static class PresetScenarioCatalog
{
	public const string ManifestPath = "TestMaps/manifest.json";
	public const string TemplateDirectoryPath = "TestMaps";

	public static readonly DateTimeOffset ExportTimestamp =
		new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
	};

	private static IReadOnlyList<PresetScenarioDef>? _scenarios;
	private static Dictionary<string, PresetScenarioDef>? _byId;

	public static IReadOnlyList<PresetScenarioDef> List()
	{
		EnsureLoaded();
		return _scenarios!;
	}

	public static bool TryGet(string id, out PresetScenarioDef scenario)
	{
		EnsureLoaded();
		return _byId!.TryGetValue(id, out scenario!);
	}

	public static PresetScenarioDef GetRequired(string id)
	{
		if (TryGet(id, out var scenario))
			return scenario;

		throw new InvalidOperationException($"Unknown preset scenario: {id}");
	}

	public static string ReadText(string sourcePath)
	{
		if (Path.IsPathRooted(sourcePath))
			return File.ReadAllText(sourcePath);

		return GameDataLocator.ReadTextOrThrow(sourcePath);
	}

	public static string ResolveExportPath(string scenarioId)
	{
		if (TryGet(scenarioId, out var scenario))
			return ResolveFileSystemPath(scenario.TemplatePath);

		return GameDataLocator.GetProjectDataPathOrThrow($"{TemplateDirectoryPath}/{scenarioId}.json");
	}

	public static string ResolveFileSystemPath(string sourcePath)
	{
		if (Path.IsPathRooted(sourcePath))
			return Path.GetFullPath(sourcePath);

		return GameDataLocator.GetProjectDataPathOrThrow(sourcePath);
	}

	public static DateTime GetModifiedAt(PresetScenarioDef scenario)
	{
		var path = ResolveFileSystemPath(scenario.TemplatePath);
		return File.Exists(path)
			? File.GetLastWriteTime(path)
			: DateTime.MinValue;
	}

	private static void EnsureLoaded()
	{
		if (_scenarios != null)
			return;

		var manifestJson = ReadText(ManifestPath);
		var manifest = JsonSerializer.Deserialize<PresetScenarioManifest>(manifestJson, JsonOptions)
			?? throw new InvalidOperationException($"Failed to deserialize preset scenario manifest: {ManifestPath}");

		var scenarios = manifest.Scenarios
			.OrderBy(static item => item.SortOrder)
			.ThenBy(static item => item.Id, StringComparer.Ordinal)
			.ToList();

		var byId = new Dictionary<string, PresetScenarioDef>(StringComparer.Ordinal);
		foreach (var scenario in scenarios)
		{
			ValidateScenario(scenario);
			if (!byId.TryAdd(scenario.Id, scenario))
				throw new InvalidOperationException($"Duplicate preset scenario id: {scenario.Id}");
		}

		_scenarios = scenarios;
		_byId = byId;
	}

	private static void ValidateScenario(PresetScenarioDef scenario)
	{
		if (string.IsNullOrWhiteSpace(scenario.Id))
			throw new InvalidOperationException("Preset scenario id cannot be empty.");
		if (string.IsNullOrWhiteSpace(scenario.DisplayNameKey))
			throw new InvalidOperationException($"Preset scenario '{scenario.Id}' missing displayNameKey.");
		if (string.IsNullOrWhiteSpace(scenario.DescriptionKey))
			throw new InvalidOperationException($"Preset scenario '{scenario.Id}' missing descriptionKey.");
		if (string.IsNullOrWhiteSpace(scenario.TemplatePath))
			throw new InvalidOperationException($"Preset scenario '{scenario.Id}' missing templatePath.");
	}
}
