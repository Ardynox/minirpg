using System.Collections.Generic;
using System.Text.Json;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.Event;

/// <summary>
/// 从 JSON 加载 IncidentDef 并注册到 Storyteller。
/// </summary>
public static class StorytellerDefLoader
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		PropertyNameCaseInsensitive = true,
	};

	public static void Load(string relativeDataPath = "storyteller_incidents.json")
	{
		if (!GameDataLocator.TryReadText(relativeDataPath, out var json, out _))
			return;

		var defs = JsonSerializer.Deserialize<List<IncidentDef>>(json, JsonOpts);
		if (defs == null) return;

		foreach (var def in defs)
			Storyteller.RegisterDef(def);
	}
}
