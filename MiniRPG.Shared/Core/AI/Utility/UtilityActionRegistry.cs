using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.AI.Utility;

public static class UtilityActionRegistry
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
	};

	private static Dictionary<string, UtilityActionDef> _actions = new(StringComparer.Ordinal);
	private static List<UtilityActionDef> _actionList = [];
	private static Dictionary<string, List<UtilityActionDef>> _byTag = new(StringComparer.Ordinal);
	private static bool _loaded;

	public static IReadOnlyDictionary<string, UtilityActionDef> Actions => _actions;
	public static IReadOnlyList<UtilityActionDef> ActionList => _actionList;

	public static void EnsureLoaded()
	{
		if (_loaded) return;
		Load();
	}

	public static void Load()
	{
		var json = GameDataLocator.ReadTextOrThrow("Config/utility_actions.json");
		var root = JsonSerializer.Deserialize<UtilityActionsRoot>(json, JsonOptions);
		if (root?.Actions == null)
			throw new InvalidOperationException("utility_actions.json is empty or invalid.");

		_actions = new Dictionary<string, UtilityActionDef>(root.Actions.Count, StringComparer.Ordinal);
		_actionList = root.Actions;
		_byTag = new Dictionary<string, List<UtilityActionDef>>(StringComparer.Ordinal);

		foreach (var action in root.Actions)
		{
			_actions[action.Id] = action;
			foreach (var tag in action.Tags)
			{
				if (!_byTag.TryGetValue(tag, out var list))
				{
					list = [];
					_byTag[tag] = list;
				}
				list.Add(action);
			}
		}

		_loaded = true;
	}

	public static UtilityActionDef? Get(string id) =>
		_actions.TryGetValue(id, out var def) ? def : null;

	public static IReadOnlyList<UtilityActionDef> GetByTag(string tag) =>
		_byTag.TryGetValue(tag, out var list) ? list : [];

	public static void Reset()
	{
		_actions.Clear();
		_actionList = [];
		_byTag.Clear();
		_loaded = false;
	}

	private sealed class UtilityActionsRoot
	{
		public List<UtilityActionDef> Actions { get; set; } = [];
	}
}
