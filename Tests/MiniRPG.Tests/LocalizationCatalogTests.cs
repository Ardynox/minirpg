using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MiniRPG.Core.Config;
using Xunit;

namespace MiniRPG.Tests;

public sealed class LocalizationCatalogTests
{
	private static readonly HashSet<string> IgnoredDirNames =
	[
		".git",
		".godot",
		".claude",
		".codex",
		"bin",
		"obj",
		"Artifacts",
		"data_MiniRPG_windows_x86_64",
	];

	private static readonly HashSet<string> ScannedExtensions =
	[
		".cs",
		".json",
		".py",
		".tscn",
	];

	[Fact]
	public void LocalizationService_LoadsExpectedTranslations()
	{
		LocalizationService.Initialize();

		Assert.Equal("Simplified Chinese", LocalizationService.TForLocale("en", "locale.zh_CN", string.Empty));
		Assert.Equal("Continue", LocalizationService.TForLocale("en", "ui.main_menu.continue", string.Empty));
		Assert.Equal("TIMELINE", LocalizationService.TForLocale("en", "ui.turn_panel.title", string.Empty));

		Assert.False(string.IsNullOrWhiteSpace(LocalizationService.TForLocale("zh_CN", "locale.zh_CN", string.Empty)));
		Assert.False(string.IsNullOrWhiteSpace(LocalizationService.TForLocale("zh_CN", "ui.main_menu.continue", string.Empty)));
		Assert.False(string.IsNullOrWhiteSpace(LocalizationService.TForLocale("zh_CN", "ui.turn_panel.title", string.Empty)));
	}

	[Fact]
	public void ResourceDetection_UsesExecutableDirectory_ForExportedBuildLayout()
	{
		var method = typeof(GameDataLocator).GetMethod(
			"CanUseGodotResourceFileAccess",
			BindingFlags.NonPublic | BindingFlags.Static,
			null,
			[typeof(string)],
			null);
		Assert.NotNull(method);

		var root = Path.Combine(Path.GetTempPath(), $"minirpg-loc-{Guid.NewGuid():N}");
		Directory.CreateDirectory(root);

		try
		{
			var processPath = Path.Combine(root, "MiniRPG.exe");

			File.WriteAllText(Path.Combine(root, "MiniRPG.pck"), string.Empty);
			Assert.True((bool)method!.Invoke(null, [processPath])!);

			File.Delete(Path.Combine(root, "MiniRPG.pck"));
			Directory.CreateDirectory(Path.Combine(root, "data_MiniRPG_windows_x86_64"));
			Assert.True((bool)method.Invoke(null, [processPath])!);
		}
		finally
		{
			try
			{
				if (Directory.Exists(root))
					Directory.Delete(root, recursive: true);
			}
			catch
			{
				// Ignore cleanup failures for temp test fixtures.
			}
		}
	}

	[Fact]
	public void CatalogFiles_AreUtf8Clean_AndMatchEnglishKeys()
	{
		var en = LoadCatalog("en");
		var zh = LoadCatalog("zh_CN");
		var mojibakeMarkers = LoadMojibakeMarkers();

		Assert.Equal(
			en.Keys.OrderBy(static key => key, StringComparer.Ordinal),
			zh.Keys.OrderBy(static key => key, StringComparer.Ordinal));

		Assert.All(zh, static entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value)));
		Assert.Equal("简体中文", zh["locale.zh_CN"]);
		Assert.Equal("继续游戏", zh["ui.main_menu.continue"]);
		Assert.Equal("官方测试场景排在前面，下面是普通用户存档。", zh["ui.save_browser.subtitle.default"]);
		Assert.Equal("世界管理", zh["ui.world_manager.title"]);
		Assert.Equal("天气: {weather} ({intensity}) {temp}C", zh["ui.status.header.weather"]);
		Assert.Equal("Worlds", en["ui.main_menu.worlds"]);
		Assert.True(zh.ContainsKey("ui.main_menu.worlds"));
		Assert.True(zh.ContainsKey("ui.main_menu.continue.with_target"));
		Assert.True(zh.ContainsKey("ui.world_manager.title.main_menu"));
		Assert.True(zh.ContainsKey("ui.world_settings.title"));
		Assert.True(zh.ContainsKey("ui.world_settings.randomize_seed"));
		Assert.True(zh.ContainsKey("ui.world_manager.danger.title"));
		Assert.True(zh.ContainsKey("ui.world_manager.delete_save_data"));
		Assert.True(zh.ContainsKey("ui.world_manager.clean_assets"));
		Assert.True(zh.ContainsKey("ui.world_manager.status.delete_save_data_success"));
		Assert.True(zh.ContainsKey("ui.world_manager.status.clean_assets_no_assets"));
		Assert.True(zh.ContainsKey("ui.world_manager.status.migration_warning"));
		Assert.True(zh.ContainsKey("ui.confirm_world_delete_save_data.title"));
		Assert.True(zh.ContainsKey("ui.confirm_world_clean_assets.title"));
		Assert.True(zh.ContainsKey("ui.confirm_switch.title.world_character"));
		Assert.True(zh.ContainsKey("ui.session_label.map_editor"));
		Assert.True(zh.ContainsKey("hint.game.world_entry"));
		Assert.Equal("取消", zh["ui.common.cancel"]);
		Assert.Equal("多人房间", zh["ui.multiplayer.room_panel.title"]);
		Assert.True(zh.ContainsKey("ui.multiplayer.room_panel.host.room_name"));
		Assert.True(zh.ContainsKey("ui.multiplayer.connect.room_failed"));
		Assert.True(zh.ContainsKey("ui.world_hover.label.walkable"));
		Assert.DoesNotContain(zh, static entry => entry.Value.Contains('\uFFFD'));
		foreach (var marker in mojibakeMarkers)
		{
			Assert.DoesNotContain(
				zh,
				entry => entry.Value.Contains(marker, StringComparison.Ordinal));
		}
	}

	[Fact]
	public void CatalogFiles_ContainLocalizationFixesForMapEditorMultiplayerAndLighting()
	{
		var en = LoadCatalog("en");
		var zh = LoadCatalog("zh_CN");
		string[] requiredKeys =
		[
			"ui.multiplayer.disabled.map_editor",
			"ui.multiplayer.disabled.save",
			"ui.multiplayer.disabled.load",
			"ui.map_editor.category.environment",
			"ui.map_editor.time_of_day",
			"ui.map_editor.weather",
			"ui.map_editor.lighting_profile",
			"ui.map_editor.turn_controller",
			"ui.map_editor.hint.v2",
			"ui.map_editor.undo",
			"ui.map_editor.redo",
			"ui.pause_menu.multiplayer_room",
			"render.lighting.unavailable",
			"render.lighting.profile_changed",
			"render.lighting.profile.default",
			"render.lighting.profile.cinematic",
			"render.lighting.profile.soft",
			"log.multiplayer.delegate_actor_unauthorized",
			"log.multiplayer.assign_primary_actor",
			"log.multiplayer.assign_primary_actor_failed",
			"log.multiplayer.kick_player",
			"log.multiplayer.kick_player_failed",
			"ui.weather_lab.preview.line2",
		];

		foreach (var key in requiredKeys)
		{
			Assert.True(en.ContainsKey(key), $"Missing en key: {key}");
			Assert.True(zh.ContainsKey(key), $"Missing zh_CN key: {key}");
			Assert.False(string.IsNullOrWhiteSpace(en[key]));
			Assert.False(string.IsNullOrWhiteSpace(zh[key]));
		}

		var expectedZhValues = new Dictionary<string, string>
		{
			["ui.multiplayer.disabled.map_editor"] = "多人会话中暂不支持地图编辑器。",
			["ui.multiplayer.disabled.save"] = "多人会话中已禁用本地存档。",
			["ui.multiplayer.disabled.load"] = "多人会话中已禁用本地读档。",
			["ui.map_editor.category.environment"] = "环境",
			["ui.map_editor.time_of_day"] = "时间段",
			["ui.map_editor.weather"] = "天气",
			["ui.map_editor.lighting_profile"] = "光照预设",
			["ui.map_editor.turn_controller"] = "回合控制",
			["ui.map_editor.hint.v2"] = "左键：执行当前工具  滚轮：切换笔刷\n工具栏：选择/建造/拆除  建造时按住 Ctrl：反向堆叠  Tab：分类  Ctrl+Z/Y：撤销/重做",
			["ui.map_editor.undo"] = "撤销",
			["ui.map_editor.redo"] = "重做",
			["ui.pause_menu.multiplayer_room"] = "多人房间",
			["render.lighting.unavailable"] = "当前光照预设不可用。",
			["render.lighting.profile_changed"] = "光照预设已切换：{profile}",
			["render.lighting.profile.default"] = "默认",
			["render.lighting.profile.cinematic"] = "电影感",
			["render.lighting.profile.soft"] = "柔和",
			["log.multiplayer.delegate_actor_unauthorized"] = "只有主拥有者才能移交 {actor}。",
			["log.multiplayer.assign_primary_actor"] = "已将 {actor} 分配给 {player}。",
			["log.multiplayer.assign_primary_actor_failed"] = "分配 {actor} 失败。",
			["log.multiplayer.kick_player"] = "已移除玩家 {player}。",
			["log.multiplayer.kick_player_failed"] = "移除玩家 {player} 失败。",
			["ui.save_browser.summary.standard"] = "回合 {turn} | Z{floor} | {timestamp}",
			["ui.status.tab.buff"] = "增益",
			["ui.turn_panel.tag.you"] = "你",
			["ui.turn_panel.tag.last"] = "上一位",
			["render.view_mode.tilemap"] = "瓦片地图 2D",
			["ui.save_name_dialog.placeholder"] = "输入存档名",
			["ui.load_recovery.message"] = "存档中的玩家 ID {playerId} 不存在。请选择一个玩家阵营角色，重新绑定后再继续加载。",
			["ui.multiplayer.status.backend_missing"] = "多人会话后端不可用。",
			["ui.weather_lab.preview.line2"] = "控制：{control} | 回合：{turn}",
			["ui.loading.save.finalize"] = "正在刷新游戏界面...",
		};

		foreach (var entry in expectedZhValues)
			Assert.Equal(entry.Value, zh[entry.Key]);
	}

	[Fact]
	public void ControlledTextFiles_DoNotContainKnownMojibakeMarkers()
	{
		var markers = LoadMojibakeMarkers();
		var hits = new List<string>();
		foreach (var path in EnumerateControlledTextFiles())
		{
			var text = File.ReadAllText(path, Encoding.UTF8);
			var marker = markers.FirstOrDefault(candidate => text.Contains(candidate, StringComparison.Ordinal));
			if (marker == null)
				continue;

			hits.Add($"{Path.GetRelativePath(ResolveRepoRoot(), path)} [{marker}]");
		}

		Assert.True(
			hits.Count == 0,
			"Found known mojibake markers:" + Environment.NewLine + string.Join(Environment.NewLine, hits));
	}

	private static IReadOnlyDictionary<string, string> LoadCatalog(string locale)
	{
		var path = Path.Combine(ResolveRepoRoot(), "Data", "I18n", $"{locale}.json");
		var json = File.ReadAllText(path, Encoding.UTF8);
		return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
			?? throw new Xunit.Sdk.XunitException($"Failed to deserialize localization catalog: {path}");
	}

	private static IReadOnlyList<string> LoadMojibakeMarkers()
	{
		var path = Path.Combine(ResolveRepoRoot(), "Tools", "mojibake_markers.txt");
		return File.ReadAllLines(path, Encoding.UTF8)
			.Select(static line => line.Trim())
			.Where(static line => line.Length > 0 && !line.StartsWith('#'))
			.ToArray();
	}

	private static IEnumerable<string> EnumerateControlledTextFiles()
	{
		var root = ResolveRepoRoot();
		return Directory
			.EnumerateFiles(root, "*", SearchOption.AllDirectories)
			.Where(static path => ShouldScanFile(path));
	}

	private static bool ShouldScanFile(string path)
	{
		var file = new FileInfo(path);
		if (!ScannedExtensions.Contains(file.Extension))
			return false;

		return !file.Directory!.EnumerateDirectoriesUpward().Any(static dir => IgnoredDirNames.Contains(dir.Name));
	}

	private static string ResolveRepoRoot()
	{
		var current = new DirectoryInfo(AppContext.BaseDirectory);
		while (current != null)
		{
			if (File.Exists(Path.Combine(current.FullName, "project.godot")))
				return current.FullName;

			current = current.Parent;
		}

		throw new Xunit.Sdk.XunitException("Failed to locate repository root from test base directory.");
	}
}

file static class DirectoryInfoExtensions
{
	public static IEnumerable<DirectoryInfo> EnumerateDirectoriesUpward(this DirectoryInfo directory)
	{
		for (DirectoryInfo? current = directory; current != null; current = current.Parent)
			yield return current;
	}
}
