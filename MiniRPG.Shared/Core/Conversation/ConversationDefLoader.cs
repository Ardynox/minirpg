using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.Conversation;

public static class ConversationDefLoader
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
	};

	private static bool _loaded;

	/// <summary>Idempotent load of JSON conversation defs (startup + tests).</summary>
	public static void EnsureLoaded()
	{
		if (_loaded)
			return;
		_loaded = true;
		ConversationRegistry.Clear();
		var dataRoot = GameDataLocator.GetProjectDataRoot();
		if (string.IsNullOrWhiteSpace(dataRoot))
			return;
		var dir = Path.Combine(dataRoot, "Conversations");
		if (!Directory.Exists(dir))
			return;
		foreach (var path in Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
		{
			try
			{
				var json = File.ReadAllText(path, Encoding.UTF8);
				var def = JsonSerializer.Deserialize<ConversationDef>(json, JsonOptions);
				if (def != null)
					ConversationRegistry.Register(def);
			}
			catch
			{
				// 忽略损坏文件，避免单个 JSON 拖垮整个对话注册
			}
		}
	}

	/// <summary>测试钩子：清除内部加载状态，强制下次 EnsureLoaded 重读磁盘。</summary>
	public static void ResetForTesting()
	{
		_loaded = false;
		ConversationRegistry.Clear();
	}
}
