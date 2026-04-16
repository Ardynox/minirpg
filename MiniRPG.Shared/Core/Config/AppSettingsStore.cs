using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniRPG.Core.Config;

public static class AppSettingsStore
{
	private const int SchemaVersion = 9;

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
			LogWarning($"[AppSettingsStore] Failed to load locale: {ex.Message}");
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

	public static bool LoadFastTurnMode()
	{
		var settings = LoadSettings();
		return settings.FastTurnMode ?? true;
	}

	public static void SaveFastTurnMode(bool enabled)
	{
		SaveSettings(settings => settings.FastTurnMode = enabled, "fast turn mode setting");
	}

	public static bool LoadEnableDebugPanel()
	{
		var settings = LoadSettings();
		return settings.EnableDebugPanel ?? true;
	}

	public static AutoNavigationInterruptPolicy LoadAutoNavigationInterruptPolicy()
	{
		var settings = LoadSettings();
		return ParseAutoNavigationInterruptPolicy(settings.AutoNavigationInterruptPolicy);
	}

	public static void SaveEnableDebugPanel(bool enabled)
	{
		SaveSettings(settings => settings.EnableDebugPanel = enabled, "debug panel setting");
	}

	public static void SaveAutoNavigationInterruptPolicy(AutoNavigationInterruptPolicy policy)
	{
		SaveSettings(
			settings => settings.AutoNavigationInterruptPolicy = SerializeAutoNavigationInterruptPolicy(policy),
			"auto-navigation interrupt policy setting");
	}

	public static float LoadMapZoomMin()
	{
		var settings = LoadSettings();
		return ClampZoomValue(settings.MapZoomMin ?? 0.6f);
	}

	public static void SaveMapZoomMin(float value)
	{
		SaveSettings(settings =>
		{
			settings.MapZoomMin = ClampZoomValue(value);
			if (settings.MapZoomMax is { } max && settings.MapZoomMin > max)
				settings.MapZoomMax = settings.MapZoomMin;
		}, "map zoom min setting");
	}

	public static float LoadMapZoomMax()
	{
		var settings = LoadSettings();
		return ClampZoomValue(settings.MapZoomMax ?? 2.4f);
	}

	public static void SaveMapZoomMax(float value)
	{
		SaveSettings(settings =>
		{
			settings.MapZoomMax = ClampZoomValue(value);
			if (settings.MapZoomMin is { } min && settings.MapZoomMax < min)
				settings.MapZoomMin = settings.MapZoomMax;
		}, "map zoom max setting");
	}

	public static float LoadUiFontScale()
	{
		var settings = LoadSettings();
		return ClampUiFontScale(settings.UiFontScale);
	}

	public static void SaveUiFontScale(float value)
	{
		SaveSettings(settings => settings.UiFontScale = ClampUiFontScale(value), "ui font scale setting");
	}

	public static string LoadUiContrastMode()
	{
		var settings = LoadSettings();
		return string.IsNullOrWhiteSpace(settings.UiContrastMode) ? "normal" : settings.UiContrastMode.Trim();
	}

	public static void SaveUiContrastMode(string mode)
	{
		SaveSettings(settings => settings.UiContrastMode = string.IsNullOrWhiteSpace(mode) ? "normal" : mode.Trim(), "ui contrast mode setting");
	}

	public static string LoadUiColorBlindMode()
	{
		var settings = LoadSettings();
		return string.IsNullOrWhiteSpace(settings.UiColorBlindMode) ? "none" : settings.UiColorBlindMode.Trim();
	}

	public static void SaveUiColorBlindMode(string mode)
	{
		SaveSettings(settings => settings.UiColorBlindMode = string.IsNullOrWhiteSpace(mode) ? "none" : mode.Trim(), "ui color blind mode setting");
	}

	public static float LoadMasterVolume()
	{
		var settings = LoadSettings();
		return ClampNormalizedVolume(settings.MasterVolume);
	}

	public static void SaveMasterVolume(float value)
	{
		SaveSettings(settings => settings.MasterVolume = ClampNormalizedVolume(value), "master volume setting");
	}

	public static float LoadMusicVolume()
	{
		var settings = LoadSettings();
		return ClampNormalizedVolume(settings.MusicVolume);
	}

	public static void SaveMusicVolume(float value)
	{
		SaveSettings(settings => settings.MusicVolume = ClampNormalizedVolume(value), "music volume setting");
	}

	public static float LoadSfxVolume()
	{
		var settings = LoadSettings();
		return ClampNormalizedVolume(settings.SfxVolume);
	}

	public static void SaveSfxVolume(float value)
	{
		SaveSettings(settings => settings.SfxVolume = ClampNormalizedVolume(value), "sfx volume setting");
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

	public static MultiplayerSettings LoadMultiplayerSettings()
	{
		var settings = LoadSettings();
		return NormalizeMultiplayerSettings(new MultiplayerSettings
		{
			DisplayName = settings.MultiplayerDisplayName ?? "Player",
			LobbyBaseUrl = settings.MultiplayerLobbyBaseUrl ?? "http://127.0.0.1:5076/",
			PreferredHostMode = ParseHostMode(settings.MultiplayerPreferredHostMode),
			LocalServerExecutablePath = settings.MultiplayerLocalServerExecutablePath ?? string.Empty,
			LocalLobbyPrefix = settings.MultiplayerLocalLobbyPrefix ?? "http://127.0.0.1:5076/",
			LocalGameAddress = settings.MultiplayerLocalGameAddress ?? "127.0.0.1",
			LocalGamePort = settings.MultiplayerLocalGamePort ?? 2455,
			LastReconnectTicket = settings.LastReconnectTicket == null
				? null
				: new MultiplayerReconnectTicket
				{
					LobbyBaseUrl = settings.LastReconnectTicket.LobbyBaseUrl ?? string.Empty,
					RoomId = settings.LastReconnectTicket.RoomId ?? string.Empty,
					RoomCode = settings.LastReconnectTicket.RoomCode ?? string.Empty,
					RoomDisplayName = settings.LastReconnectTicket.RoomDisplayName ?? string.Empty,
					ServerEndpoint = settings.LastReconnectTicket.ServerEndpoint ?? string.Empty,
					PlayerSessionId = settings.LastReconnectTicket.PlayerSessionId ?? string.Empty,
					PrimaryActorId = settings.LastReconnectTicket.PrimaryActorId ?? string.Empty,
					ReconnectToken = settings.LastReconnectTicket.ReconnectToken ?? string.Empty,
					DisplayName = settings.LastReconnectTicket.DisplayName ?? string.Empty,
					ReconnectDeadlineUtc = settings.LastReconnectTicket.ReconnectDeadlineUtc,
				},
		});
	}

	public static void SaveMultiplayerSettings(MultiplayerSettings multiplayerSettings)
	{
		ArgumentNullException.ThrowIfNull(multiplayerSettings);
		var normalized = NormalizeMultiplayerSettings(multiplayerSettings);
		SaveSettings(settings =>
		{
			settings.MultiplayerDisplayName = normalized.DisplayName;
			settings.MultiplayerLobbyBaseUrl = normalized.LobbyBaseUrl;
			settings.MultiplayerPreferredHostMode = normalized.PreferredHostMode switch
			{
				MultiplayerHostMode.Remote => "remote",
				_ => "local",
			};
			settings.MultiplayerLocalServerExecutablePath = normalized.LocalServerExecutablePath;
			settings.MultiplayerLocalLobbyPrefix = normalized.LocalLobbyPrefix;
			settings.MultiplayerLocalGameAddress = normalized.LocalGameAddress;
			settings.MultiplayerLocalGamePort = normalized.LocalGamePort;
			settings.LastReconnectTicket = normalized.LastReconnectTicket == null
				? null
				: new MultiplayerReconnectTicketDto
				{
					LobbyBaseUrl = normalized.LastReconnectTicket.LobbyBaseUrl,
					RoomId = normalized.LastReconnectTicket.RoomId,
					RoomCode = normalized.LastReconnectTicket.RoomCode,
					RoomDisplayName = normalized.LastReconnectTicket.RoomDisplayName,
					ServerEndpoint = normalized.LastReconnectTicket.ServerEndpoint,
					PlayerSessionId = normalized.LastReconnectTicket.PlayerSessionId,
					PrimaryActorId = normalized.LastReconnectTicket.PrimaryActorId,
					ReconnectToken = normalized.LastReconnectTicket.ReconnectToken,
					DisplayName = normalized.LastReconnectTicket.DisplayName,
					ReconnectDeadlineUtc = normalized.LastReconnectTicket.ReconnectDeadlineUtc,
				};
		}, "multiplayer settings");
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
			LogWarning($"[AppSettingsStore] Failed to load settings: {ex.Message}");
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
			LogWarning($"[AppSettingsStore] Failed to save {label}: {ex.Message}");
		}
	}

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	private static float ClampZoomValue(float value) => Math.Clamp(value, 0.2f, 4.0f);

	private static float ClampUiFontScale(float? value)
	{
		if (!value.HasValue || float.IsNaN(value.Value) || float.IsInfinity(value.Value))
			return 1f;

		return Math.Clamp(value.Value, 0.75f, 1.5f);
	}

	private static float ClampNormalizedVolume(float? value)
	{
		if (!value.HasValue || float.IsNaN(value.Value) || float.IsInfinity(value.Value))
			return 1f;

		return Math.Clamp(value.Value, 0f, 1f);
	}

	private static AutoNavigationInterruptPolicy ParseAutoNavigationInterruptPolicy(string? value) => value switch
	{
		"manual_only" => AutoNavigationInterruptPolicy.ManualOnly,
		"hostile_proximity_stop" => AutoNavigationInterruptPolicy.HostileProximityStop,
		_ => AutoNavigationInterruptPolicy.ConservativeStop,
	};

	private static string SerializeAutoNavigationInterruptPolicy(AutoNavigationInterruptPolicy policy) => policy switch
	{
		AutoNavigationInterruptPolicy.ManualOnly => "manual_only",
		AutoNavigationInterruptPolicy.HostileProximityStop => "hostile_proximity_stop",
		_ => "conservative_stop",
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

		[JsonPropertyName("autoNavigationInterruptPolicy")]
		public string? AutoNavigationInterruptPolicy { get; set; }

		[JsonPropertyName("fastTurnMode")]
		public bool? FastTurnMode { get; set; }

		[JsonPropertyName("mapZoomMin")]
		public float? MapZoomMin { get; set; }

		[JsonPropertyName("mapZoomMax")]
		public float? MapZoomMax { get; set; }

		[JsonPropertyName("masterVolume")]
		public float? MasterVolume { get; set; }

		[JsonPropertyName("uiFontScale")]
		public float? UiFontScale { get; set; }

		[JsonPropertyName("uiContrastMode")]
		public string? UiContrastMode { get; set; }

		[JsonPropertyName("uiColorBlindMode")]
		public string? UiColorBlindMode { get; set; }

		[JsonPropertyName("musicVolume")]
		public float? MusicVolume { get; set; }

		[JsonPropertyName("sfxVolume")]
		public float? SfxVolume { get; set; }

		[JsonPropertyName("lastContinueKind")]
		public string? LastContinueKind { get; set; }

		[JsonPropertyName("lastWorldId")]
		public string? LastWorldId { get; set; }

		[JsonPropertyName("lastCharacterId")]
		public string? LastCharacterId { get; set; }

		[JsonPropertyName("lastLegacySavePath")]
		public string? LastLegacySavePath { get; set; }

		[JsonPropertyName("multiplayerDisplayName")]
		public string? MultiplayerDisplayName { get; set; }

		[JsonPropertyName("multiplayerLobbyBaseUrl")]
		public string? MultiplayerLobbyBaseUrl { get; set; }

		[JsonPropertyName("multiplayerPreferredHostMode")]
		public string? MultiplayerPreferredHostMode { get; set; }

		[JsonPropertyName("multiplayerLocalServerExecutablePath")]
		public string? MultiplayerLocalServerExecutablePath { get; set; }

		[JsonPropertyName("multiplayerLocalLobbyPrefix")]
		public string? MultiplayerLocalLobbyPrefix { get; set; }

		[JsonPropertyName("multiplayerLocalGameAddress")]
		public string? MultiplayerLocalGameAddress { get; set; }

		[JsonPropertyName("multiplayerLocalGamePort")]
		public int? MultiplayerLocalGamePort { get; set; }

		[JsonPropertyName("lastReconnectTicket")]
		public MultiplayerReconnectTicketDto? LastReconnectTicket { get; set; }
	}

	private sealed class MultiplayerReconnectTicketDto
	{
		[JsonPropertyName("lobbyBaseUrl")]
		public string? LobbyBaseUrl { get; set; }

		[JsonPropertyName("roomId")]
		public string? RoomId { get; set; }

		[JsonPropertyName("roomCode")]
		public string? RoomCode { get; set; }

		[JsonPropertyName("roomDisplayName")]
		public string? RoomDisplayName { get; set; }

		[JsonPropertyName("serverEndpoint")]
		public string? ServerEndpoint { get; set; }

		[JsonPropertyName("playerSessionId")]
		public string? PlayerSessionId { get; set; }

		[JsonPropertyName("primaryActorId")]
		public string? PrimaryActorId { get; set; }

		[JsonPropertyName("reconnectToken")]
		public string? ReconnectToken { get; set; }

		[JsonPropertyName("displayName")]
		public string? DisplayName { get; set; }

		[JsonPropertyName("reconnectDeadlineUtc")]
		public DateTimeOffset? ReconnectDeadlineUtc { get; set; }
	}

	private static AppSettingsDto CreateDefaultSettings() => new()
	{
		Version = SchemaVersion,
		Locale = LocalizationService.DefaultLocale,
		EnableKeyboardTargeting = false,
		EnableDebugPanel = true,
		AutoNavigationInterruptPolicy = "conservative_stop",
		FastTurnMode = true,
		MapZoomMin = 0.6f,
		MapZoomMax = 2.4f,
		MasterVolume = 1f,
		MusicVolume = 1f,
		SfxVolume = 1f,
		UiFontScale = 1f,
		UiContrastMode = "normal",
		UiColorBlindMode = "none",
		LastContinueKind = null,
		LastWorldId = null,
		LastCharacterId = null,
		LastLegacySavePath = null,
		MultiplayerDisplayName = "Player",
		MultiplayerLobbyBaseUrl = "http://127.0.0.1:5076/",
		MultiplayerPreferredHostMode = "local",
		MultiplayerLocalServerExecutablePath = string.Empty,
		MultiplayerLocalLobbyPrefix = "http://127.0.0.1:5076/",
		MultiplayerLocalGameAddress = "127.0.0.1",
		MultiplayerLocalGamePort = 2455,
		LastReconnectTicket = null,
	};

	private static MultiplayerSettings NormalizeMultiplayerSettings(MultiplayerSettings settings) => new()
	{
		DisplayName = NormalizeDisplayName(settings.DisplayName),
		LobbyBaseUrl = NormalizeHttpPrefix(settings.LobbyBaseUrl, "http://127.0.0.1:5076/"),
		PreferredHostMode = settings.PreferredHostMode,
		LocalServerExecutablePath = settings.LocalServerExecutablePath?.Trim() ?? string.Empty,
		LocalLobbyPrefix = NormalizeHttpPrefix(settings.LocalLobbyPrefix, "http://127.0.0.1:5076/"),
		LocalGameAddress = string.IsNullOrWhiteSpace(settings.LocalGameAddress) ? "127.0.0.1" : settings.LocalGameAddress.Trim(),
		LocalGamePort = Math.Clamp(settings.LocalGamePort, 1, ushort.MaxValue),
		LastReconnectTicket = NormalizeReconnectTicket(settings.LastReconnectTicket),
	};

	private static MultiplayerReconnectTicket? NormalizeReconnectTicket(MultiplayerReconnectTicket? ticket)
	{
		if (ticket == null || string.IsNullOrWhiteSpace(ticket.RoomId) || string.IsNullOrWhiteSpace(ticket.ReconnectToken))
			return null;

		return new MultiplayerReconnectTicket
		{
			LobbyBaseUrl = NormalizeHttpPrefix(ticket.LobbyBaseUrl, "http://127.0.0.1:5076/"),
			RoomId = ticket.RoomId.Trim(),
			RoomCode = ticket.RoomCode?.Trim() ?? string.Empty,
			RoomDisplayName = ticket.RoomDisplayName?.Trim() ?? string.Empty,
			ServerEndpoint = ticket.ServerEndpoint?.Trim() ?? string.Empty,
			PlayerSessionId = ticket.PlayerSessionId?.Trim() ?? string.Empty,
			PrimaryActorId = ticket.PrimaryActorId?.Trim() ?? string.Empty,
			ReconnectToken = ticket.ReconnectToken.Trim(),
			DisplayName = NormalizeDisplayName(ticket.DisplayName),
			ReconnectDeadlineUtc = ticket.ReconnectDeadlineUtc,
		};
	}

	private static MultiplayerHostMode ParseHostMode(string? value) =>
		string.Equals(value, "remote", StringComparison.OrdinalIgnoreCase)
			? MultiplayerHostMode.Remote
			: MultiplayerHostMode.Local;

	private static string NormalizeDisplayName(string? value)
	{
		var trimmed = value?.Trim();
		return string.IsNullOrWhiteSpace(trimmed)
			? "Player"
			: PlayerCreationOptions.NormalizeDisplayName(trimmed);
	}

	private static string NormalizeHttpPrefix(string? value, string fallback)
	{
		var trimmed = value?.Trim();
		if (string.IsNullOrWhiteSpace(trimmed))
			return fallback;

		return trimmed.EndsWith("/", StringComparison.Ordinal)
			? trimmed
			: $"{trimmed}/";
	}

	private static bool TryReadSettingsJson(out string json)
	{
		var path = GetSettingsPath();
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
		var path = GetSettingsPath();
		var directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrWhiteSpace(directory))
			Directory.CreateDirectory(directory);
		File.WriteAllText(path, json);
	}

	private static string GetSettingsPath() =>
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

	private static void LogWarning(string message)
	{
		try
		{
			Console.Error.WriteLine(message);
		}
		catch
		{
			// Ignore logging failures.
		}
	}
}

public sealed class ContinueState
{
	public string? LastContinueKind { get; init; }
	public string? LastWorldId { get; init; }
	public string? LastCharacterId { get; init; }
	public string? LastLegacySavePath { get; init; }
}
