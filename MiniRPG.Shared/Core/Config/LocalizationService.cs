using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;

namespace MiniRPG.Core.Config;

public static class LocalizationService
{
	public const string DefaultLocale = "zh_CN";

	private const string LocaleMeta = "__loc_text_key";
	private const string PlaceholderMeta = "__loc_placeholder_key";
	private static readonly Regex NamedArgPattern = new(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);
	private static readonly Regex KeyPattern = new(@"^[A-Za-z0-9_.-]+$", RegexOptions.Compiled);
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		ReadCommentHandling = JsonCommentHandling.Skip,
	};

	private static readonly string[] _supportedLocales = [DefaultLocale, "en"];
	private static readonly Dictionary<string, Dictionary<string, string>> _catalogs = [];
	private static bool _initialized;

	public static event Action<string>? LocaleChanged;

	public static IReadOnlyList<string> SupportedLocales => _supportedLocales;
	public static string CurrentLocale { get; private set; } = DefaultLocale;

	public static void Initialize()
	{
		if (_initialized)
			return;

		foreach (var locale in _supportedLocales)
			_catalogs[locale] = LoadCatalog(locale);

		_initialized = true;
		CurrentLocale = DefaultLocale;
	}

	public static bool IsSupported(string locale)
	{
		foreach (var supported in _supportedLocales)
		{
			if (string.Equals(supported, locale, StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}

	public static string NormalizeLocale(string? locale)
	{
		if (string.IsNullOrWhiteSpace(locale))
			return DefaultLocale;

		foreach (var supported in _supportedLocales)
		{
			if (string.Equals(supported, locale, StringComparison.OrdinalIgnoreCase))
				return supported;
		}

		return DefaultLocale;
	}

	public static bool SetLocale(string? locale, bool notify = true)
	{
		Initialize();

		var normalized = NormalizeLocale(locale);
		if (string.Equals(CurrentLocale, normalized, StringComparison.Ordinal))
			return false;

		CurrentLocale = normalized;
		if (notify)
			LocaleChanged?.Invoke(normalized);
		return true;
	}

	public static string T(string key, params (string Name, object? Value)[] args) =>
		TOrFallback(key, key, args);

	public static string TOrFallback(string key, string fallback, params (string Name, object? Value)[] args)
	{
		Initialize();
		var raw = ResolveRaw(key, fallback);
		return FormatNamed(raw, args);
	}

	public static string TForLocale(string? locale, string key, string fallback, params (string Name, object? Value)[] args)
	{
		Initialize();
		var raw = ResolveRaw(NormalizeLocale(locale), key, fallback);
		return FormatNamed(raw, args);
	}

	public static string GetLocaleLabel(string locale) =>
		TOrFallback($"locale.{NormalizeLocale(locale)}", locale);

	public static string[] GetList(string key, string fallback, params (string Name, object? Value)[] args)
	{
		var raw = TOrFallback(key, fallback, args);
		return raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}

	public static void LocalizeTree(Node root)
	{
		ApplyNode(root);
		foreach (var child in root.GetChildren())
		{
			if (child is Node childNode)
				LocalizeTree(childNode);
		}
	}

	private static Dictionary<string, string> LoadCatalog(string locale)
	{
		if (!GameDataLocator.TryReadText($"I18n/{locale}.json", out var json, out var sourceLabel))
		{
			LogWarning($"[LocalizationService] Missing locale catalog '{locale}': {sourceLabel}");
			return new Dictionary<string, string>(StringComparer.Ordinal);
		}

		try
		{
			var catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
			return catalog != null
				? new Dictionary<string, string>(catalog, StringComparer.Ordinal)
				: new Dictionary<string, string>(StringComparer.Ordinal);
		}
		catch (Exception ex)
		{
			LogWarning($"[LocalizationService] Failed to parse locale catalog from {sourceLabel}: {ex.Message}");
			return new Dictionary<string, string>(StringComparer.Ordinal);
		}
	}

	private static string ResolveRaw(string key, string fallback)
		=> ResolveRaw(CurrentLocale, key, fallback);

	private static string ResolveRaw(string locale, string key, string fallback)
	{
		if (_catalogs.TryGetValue(locale, out var current)
			&& current.TryGetValue(key, out var localized))
			return localized;

		if (_catalogs.TryGetValue(DefaultLocale, out var defaults)
			&& defaults.TryGetValue(key, out var defaultValue))
			return defaultValue;

		return string.IsNullOrEmpty(fallback) ? key : fallback;
	}

	private static string FormatNamed(string value, IReadOnlyList<(string Name, object? Value)> args)
	{
		if (args.Count == 0 || string.IsNullOrEmpty(value))
			return value;

		var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var (name, argumentValue) in args)
		{
			if (string.IsNullOrWhiteSpace(name))
				continue;

			replacements[name] = argumentValue?.ToString() ?? string.Empty;
		}

		if (replacements.Count == 0)
			return value;

		return NamedArgPattern.Replace(value, match =>
		{
			var name = match.Groups[1].Value;
			return replacements.TryGetValue(name, out var replacement)
				? replacement
				: match.Value;
		});
	}

	private static void ApplyNode(Node node)
	{
		switch (node)
		{
			case Button button:
				ApplyText(button, LocaleMeta, () => button.Text, value => button.Text = value);
				break;
			case Label label:
				ApplyText(label, LocaleMeta, () => label.Text, value => label.Text = value);
				break;
			case RichTextLabel richTextLabel:
				ApplyText(richTextLabel, LocaleMeta, () => richTextLabel.Text, value => richTextLabel.Text = value);
				break;
			case LineEdit lineEdit:
				ApplyText(lineEdit, PlaceholderMeta, () => lineEdit.PlaceholderText, value => lineEdit.PlaceholderText = value);
				break;
		}
	}

	private static void ApplyText(GodotObject obj, string metaKey, Func<string> getter, Action<string> setter)
	{
		string key;
		if (obj.HasMeta(metaKey))
			key = obj.GetMeta(metaKey).AsString();
		else
		{
			key = getter();
			if (!LooksLikeKey(key))
				return;
			obj.SetMeta(metaKey, key);
		}

		setter(T(key));
	}

	private static bool LooksLikeKey(string value) =>
		!string.IsNullOrWhiteSpace(value)
		&& value.Contains('.')
		&& KeyPattern.IsMatch(value);

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
