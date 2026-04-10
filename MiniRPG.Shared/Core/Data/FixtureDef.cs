using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.Data;

public sealed class FixtureDef
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	[JsonPropertyName("material")]
	public string Material { get; set; } = "wood";

	[JsonPropertyName("maxDurability")]
	public int MaxDurability { get; set; } = 20;

	[JsonPropertyName("flammable")]
	public bool Flammable { get; set; }

	[JsonPropertyName("burnsInto")]
	public string? BurnsInto { get; set; }

	[JsonPropertyName("removedOnDestroy")]
	public bool RemovedOnDestroy { get; set; } = true;

	[JsonPropertyName("blocksSight")]
	public bool BlocksSight { get; set; }

	[JsonPropertyName("controlledFireSource")]
	public bool ControlledFireSource { get; set; }
}

public static class FixtureRegistry
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
	};

	private static readonly Dictionary<string, FixtureDef> _defs = new(StringComparer.Ordinal);

	public static IReadOnlyDictionary<string, FixtureDef> All => _defs;

	public static void Register(FixtureDef def)
	{
		if (!string.IsNullOrWhiteSpace(def.Id))
			_defs[def.Id] = def;
	}

	public static FixtureDef? Get(string id) =>
		_defs.GetValueOrDefault(id);

	public static void Load(string relativeDataPath = "fixtures.json")
	{
		_defs.Clear();
		var json = GameDataLocator.ReadTextOrThrow(relativeDataPath);
		var defs = JsonSerializer.Deserialize<List<FixtureDef>>(json, JsonOptions) ?? [];
		foreach (var def in defs)
			Register(def);
	}
}
