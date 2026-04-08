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
		Assert.True(zh.ContainsKey("ui.world_manager.delete_world"));
		Assert.True(zh.ContainsKey("ui.world_manager.status.deleted"));
		Assert.True(zh.ContainsKey("ui.confirm_world_delete.title"));
		Assert.True(zh.ContainsKey("ui.confirm_switch.title.world_character"));
		Assert.True(zh.ContainsKey("ui.session_label.map_editor"));
		Assert.True(zh.ContainsKey("hint.game.world_entry"));
		Assert.DoesNotContain(zh, static entry => entry.Value.Contains('\uFFFD'));
		foreach (var marker in mojibakeMarkers)
		{
			Assert.DoesNotContain(
				zh,
				entry => entry.Value.Contains(marker, StringComparison.Ordinal));
		}
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
