using System.Collections.Generic;
using System.Text.Json;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.Social;

/// <summary>
/// 从 JSON 加载 SocialInteractionDef 并注册到 SocialModule。
/// </summary>
public static class SocialInteractionLoader
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		PropertyNameCaseInsensitive = true,
	};

	public static void Load(string relativeDataPath = "social_interactions.json")
	{
		if (!GameDataLocator.TryReadText(relativeDataPath, out var json, out _))
			return;

		var defs = JsonSerializer.Deserialize<List<SocialInteractionDef>>(json, JsonOpts);
		if (defs == null) return;

		foreach (var def in defs)
			SocialModule.RegisterInteraction(def);
	}
}
