using System.Collections.Generic;
using System.Text.Json;
using Godot;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.Dialog;

/// <summary>
/// 扁平对话池。所有 DialogEntry 存在一个全局列表中，
/// 由 DialogRuleEngine 根据 DialogCondition 标签做筛选。
/// 不再按 npcId/profession 分组——分组逻辑全部下沉到标签条件。
/// </summary>
public static class DialogPool
{
	private static List<DialogSet> _sets = [];
	private static List<DialogEntry> _all = [];
	private static readonly Dictionary<string, DialogEntry> _byId = new();
	private static readonly Dictionary<string, List<DialogEntry>> _byCategory = new();
	private static bool _loaded;

	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		PropertyNameCaseInsensitive = true,
	};

	public static void Load(string path = "dialogs.json")
	{
		_all = [];
		_byId.Clear();
		_byCategory.Clear();

		if (!GameDataLocator.TryReadText(path, out var json, out var sourceLabel))
		{
			GD.PrintErr($"DialogPool: cannot open {path} (tried: {sourceLabel})");
			_loaded = true;
			return;
		}

		_sets = JsonSerializer.Deserialize<List<DialogSet>>(json, JsonOpts) ?? [];
		ApplyLocalizationOverrides();
		IndexAll();
		_loaded = true;
	}

	private static void ApplyLocalizationOverrides()
	{
		foreach (var set in _sets)
		{
			foreach (var entry in set.Entries)
			{
				if (!string.IsNullOrWhiteSpace(entry.Id))
				{
					entry.Template = LocalizationService.TOrFallback(
						$"dialog.entry.{entry.Id}.template",
						entry.Template);
				}

				for (var i = 0; i < entry.Options.Count; i++)
				{
					var option = entry.Options[i];
					if (string.IsNullOrWhiteSpace(entry.Id))
						continue;

					option.Text = LocalizationService.TOrFallback(
						$"dialog.entry.{entry.Id}.option.{i}",
						option.Text);
				}
			}
		}
	}

	private static void IndexAll()
	{
		foreach (var set in _sets)
		{
			if (!_byCategory.ContainsKey(set.Category))
				_byCategory[set.Category] = [];

			foreach (var entry in set.Entries)
			{
				_all.Add(entry);
				if (!string.IsNullOrEmpty(entry.Id))
					_byId[entry.Id] = entry;
				_byCategory[set.Category].Add(entry);
			}
		}
	}

	/// <summary>获取所有入口级对话候选（category = "greet" 的全部条目）。</summary>
	public static List<DialogEntry> GetGreetCandidates()
	{
		if (!_loaded) Load();
		return _byCategory.TryGetValue("greet", out var list) ? list : [];
	}

	/// <summary>获取所有对话条目（不分类）。</summary>
	public static List<DialogEntry> GetAll()
	{
		if (!_loaded) Load();
		return _all;
	}

	/// <summary>按 category 获取候选。</summary>
	public static List<DialogEntry> GetByCategory(string category)
	{
		if (!_loaded) Load();
		return _byCategory.TryGetValue(category, out var list) ? list : [];
	}

	/// <summary>按 ID 查找单个对话节点（用于选项跳转）。</summary>
	public static DialogEntry? FindById(string entryId)
	{
		if (!_loaded) Load();
		return _byId.GetValueOrDefault(entryId);
	}
}
