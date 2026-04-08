using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using GodotFileAccess = Godot.FileAccess;

namespace MiniRPG.Core.Config;

public static class AppSettingsStore
{
	private const string SettingsPath = "user://app_settings.json";
	private const int SchemaVersion = 4;

	public static string LoadLocale()
	{
		if (!TryReadSettingsJson(out var json))
			return LocalizationService.DefaultLocale;

		try
		{
			var settings = JsonSerializer.Deserialize<AppSettingsDto>(json);
			return LocalizationService.NormalizeLocale(settings?.Locale);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[AppSettingsStore] Failed to load locale: {ex.Message}");
			return LocalizationService.DefaultLocale;
		}
	}

	public static void SaveLocale(string locale)
	{
		SaveSettings(settings => settings.Locale = LocalizationService.NormalizeLocale(locale), "locale");
	}

	public static bool LoadEnableKeyboardTargeting()
	{
		var settings = LoadSettings();
		return settings.EnableKeyboardTargeting;
	}

	public static void SaveEnableKeyboardTargeting(bool enabled)
	{
		SaveSettings(settings => settings.EnableKeyboardTargeting = enabled, "keyboard targeting setting");
	}

	public static bool LoadEnableDebugPanel()
	{
		var settings = LoadSettings();
		return settings.EnableDebugPanel ?? true;
	}

	public static void SaveEnableDebugPanel(bool enabled)
	{
		SaveSettings(settings => settings.EnableDebugPanel = enabled, "debug panel setting");
	}

	public static ContinueState LoadContinueState()
	{
		var settings = LoadSettings();
		return new ContinueState
		{
			LastContinueKind = settings.LastContinueKind,
			LastWorldId = settings.LastWorldId,
			LastCharacterId = settings.LastCharacterId,
			LastLegacySavePath = settings.LastLegacySavePath,
		};
	}

	public static void SaveContinueState(ContinueState state)
	{
		SaveSettings(settings =>
		{
			settings.LastContinueKind = state.LastContinueKind;
			settings.LastWorldId = state.LastWorldId;
			settings.LastCharacterId = state.LastCharacterId;
			settings.LastLegacySavePath = state.LastLegacySavePath;
		}, "continue state");
	}

	private static AppSettingsDto LoadSettings()
	{
		if (!TryReadSettingsJson(out var json))
			return CreateDefaultSettings();

		try
		{
			return JsonSerializer.Deserialize<AppSettingsDto>(json) ?? CreateDefaultSettings();
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[AppSettingsStore] Failed to load settings: {ex.Message}");
			return CreateDefaultSettings();
		}
	}

	private static void SaveSettings(Action<AppSettingsDto> mutate, string label)
	{
		var settings = LoadSettings();
		settings.Version = SchemaVersion;
		mutate(settings);

		try
		{
			var json = JsonSerializer.Serialize(settings, JsonOptions);
			WriteSettingsJson(json);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[AppSettingsStore] Failed to save {label}: {ex.Message}");
		}
	}

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	private sealed class AppSettingsDto
	{
		[JsonPropertyName("version")]
		public int Version { get; set; }

		[JsonPropertyName("locale")]
		public string? Locale { get; set; }

		[JsonPropertyName("enableKeyboardTargeting")]
		public bool EnableKeyboardTargeting { get; set; }

		[JsonPropertyName("enableDebugPanel")]
		public bool? EnableDebugPanel { get; set; }

		[JsonPropertyName("lastContinueKind")]
		public string? LastContinueKind { get; set; }

		[JsonPropertyName("lastWorldId")]
		public string? LastWorldId { get; set; }

		[JsonPropertyName("lastCharacterId")]
		public string? LastCharacterId { get; set; }

		[JsonPropertyName("lastLegacySavePath")]
		public string? LastLegacySavePath { get; set; }
	}

	private static AppSettingsDto CreateDefaultSettings() => new()
	{
		Version = SchemaVersion,
		Locale = LocalizationService.DefaultLocale,
		EnableKeyboardTargeting = false,
		EnableDebugPanel = true,
		LastContinueKind = null,
		LastWorldId = null,
		LastCharacterId = null,
		LastLegacySavePath = null,
	};

	private static bool TryReadSettingsJson(out string json)
	{
		if (CanUseGodotFileAccess())
		{
			if (!GodotFileAccess.FileExists(SettingsPath))
			{
				json = string.Empty;
				return false;
			}

			using var file = GodotFileAccess.Open(SettingsPath, GodotFileAccess.ModeFlags.Read);
			if (file == null)
			{
				json = string.Empty;
				return false;
			}

			json = file.GetAsText();
			return true;
		}

		var path = GetFallbackSettingsPath();
		if (!File.Exists(path))
		{
			json = string.Empty;
			return false;
		}

		json = File.ReadAllText(path);
		return true;
	}

	private static void WriteSettingsJson(string json)
	{
		if (CanUseGodotFileAccess())
		{
			using var file = GodotFileAccess.Open(SettingsPath, GodotFileAccess.ModeFlags.Write);
			file?.StoreString(json);
			return;
		}

		var path = GetFallbackSettingsPath();
		var directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrWhiteSpace(directory))
			Directory.CreateDirectory(directory);
		File.WriteAllText(path, json);
	}

	private static bool CanUseGodotFileAccess()
	{
		try
		{
			var processPath = System.Environment.ProcessPath;
			if (string.IsNullOrWhiteSpace(processPath))
				return false;

			var processName = Path.GetFileNameWithoutExtension(processPath);
			if (string.IsNullOrWhiteSpace(processName))
				return false;

			var processDirectory = Path.GetDirectoryName(processPath);
			if (string.IsNullOrWhiteSpace(processDirectory))
				return false;

			if (processName.Contains("godot", StringComparison.OrdinalIgnoreCase))
				return true;

			if (File.Exists(Path.Combine(processDirectory, $"{processName}.pck")))
				return true;

			return Directory.GetDirectories(processDirectory, $"data_{processName}_*").Length > 0;
		}
		catch
		{
			return false;
		}
	}

	private static string GetFallbackSettingsPath() =>
		Path.Combine(ResolveUserDataDir(), "app_settings.json");

	private static string ResolveUserDataDir()
	{
		const string appName = "MiniRPG";

		if (OperatingSystem.IsWindows())
		{
			var root = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
			return Path.Combine(root, "Godot", "app_userdata", appName);
		}

		if (OperatingSystem.IsMacOS())
		{
			var root = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
			return Path.Combine(root, "Godot", "app_userdata", appName);
		}

		var xdgData = System.Environment.GetEnvironmentVariable("XDG_DATA_HOME");
		var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
		var rootDir = string.IsNullOrWhiteSpace(xdgData)
			? Path.Combine(home, ".local", "share")
			: xdgData;
		return Path.Combine(rootDir, "godot", "app_userdata", appName);
	}
}

public sealed class ContinueState
{
	public string? LastContinueKind { get; init; }
	public string? LastWorldId { get; init; }
	public string? LastCharacterId { get; init; }
	public string? LastLegacySavePath { get; init; }
}
