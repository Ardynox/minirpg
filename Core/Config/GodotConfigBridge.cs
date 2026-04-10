using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Godot;
using GodotFileAccess = Godot.FileAccess;

namespace MiniRPG.Core.Config;

internal static class GodotConfigBridge
{
	private const string LocaleMeta = "__loc_text_key";
	private const string PlaceholderMeta = "__loc_placeholder_key";
	private static readonly Regex KeyPattern = new(@"^[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

#pragma warning disable CA2255
	[ModuleInitializer]
	internal static void Initialize()
	{
		LocalizationService.ConfigureTreeLocalizer(LocalizeTree);
		if (CanUseGodotRuntimeFileAccess())
			GameDataLocator.ConfigureResourceTextReader(ReadResourceText);
	}
#pragma warning restore CA2255

	private static string? ReadResourceText(string resourcePath)
	{
		try
		{
			using var file = GodotFileAccess.Open(resourcePath, GodotFileAccess.ModeFlags.Read);
			return file?.GetAsText();
		}
		catch
		{
			return null;
		}
	}

	private static void LocalizeTree(object root)
	{
		if (root is not Node node)
			return;

		ApplyNode(node);
		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
				LocalizeTree(childNode);
		}
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

		setter(LocalizationService.T(key));
	}

	private static bool LooksLikeKey(string value) =>
		!string.IsNullOrWhiteSpace(value)
		&& value.Contains('.')
		&& KeyPattern.IsMatch(value);

	private static bool CanUseGodotRuntimeFileAccess()
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
}
