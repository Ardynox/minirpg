using System;
using System.IO;
using System.Text.RegularExpressions;
using Godot;
using GodotFileAccess = Godot.FileAccess;

namespace MiniRPG;

public partial class Main
{
	private const string LocaleMeta = "__loc_text_key";
	private const string PlaceholderMeta = "__loc_placeholder_key";
	private static readonly Regex LocKeyPattern = new(@"^[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

	private static void ConfigureGodotBridge()
	{
		LocalizationService.ConfigureTreeLocalizer(LocalizeGodotTree);
		if (CanUseGodotRuntimeFileAccess())
			GameDataLocator.ConfigureResourceTextReader(ReadGodotResourceText);
	}

	private static string? ReadGodotResourceText(string resourcePath)
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

	private static void LocalizeGodotTree(object root)
	{
		if (root is not Node node)
			return;

		ApplyLocaleNode(node);
		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
				LocalizeGodotTree(childNode);
		}
	}

	private static void ApplyLocaleNode(Node node)
	{
		switch (node)
		{
			case Button button:
				ApplyLocaleText(button, LocaleMeta, () => button.Text, value => button.Text = value);
				break;
			case Label label:
				ApplyLocaleText(label, LocaleMeta, () => label.Text, value => label.Text = value);
				break;
			case RichTextLabel richTextLabel:
				ApplyLocaleText(richTextLabel, LocaleMeta, () => richTextLabel.Text, value => richTextLabel.Text = value);
				break;
			case LineEdit lineEdit:
				ApplyLocaleText(lineEdit, PlaceholderMeta, () => lineEdit.PlaceholderText, value => lineEdit.PlaceholderText = value);
				break;
		}
	}

	private static void ApplyLocaleText(GodotObject obj, string metaKey, Func<string> getter, Action<string> setter)
	{
		string key;
		if (obj.HasMeta(metaKey))
			key = obj.GetMeta(metaKey).AsString();
		else
		{
			key = getter();
			if (!LooksLikeLocKey(key))
				return;
			obj.SetMeta(metaKey, key);
		}

		setter(LocalizationService.T(key));
	}

	private static bool LooksLikeLocKey(string value) =>
		!string.IsNullOrWhiteSpace(value)
		&& value.Contains('.')
		&& LocKeyPattern.IsMatch(value);

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
