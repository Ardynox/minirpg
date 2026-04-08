using System;
using System.IO;
using Godot;
using GodotFileAccess = Godot.FileAccess;

namespace MiniRPG.Core.Config;

/// <summary>
/// 统一定位运行时 Data 目录。
/// 优先读取导出目录旁的松散 Data，再回退到包内 res://Data，
/// 最后在非 Godot 测试/工具进程中回退到仓库里的 Data。
/// </summary>
public static class GameDataLocator
{
	private const string DataFolderName = "Data";

	public static string? GetExternalDataRoot()
	{
		var candidate = GetExpectedExternalDataRoot(System.Environment.ProcessPath);
		if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
			return null;

		return candidate;
	}

	public static string? GetProjectDataRoot()
	{
		var root = ResolveProjectRoot(AppContext.BaseDirectory);
		if (root == null)
			return null;

		var dataRoot = Path.Combine(root, DataFolderName);
		return Directory.Exists(dataRoot) ? dataRoot : null;
	}

	public static string GetProjectDataPathOrThrow(string relativeDataPath)
	{
		var dataRoot = GetProjectDataRoot();
		if (string.IsNullOrWhiteSpace(dataRoot))
			throw new InvalidOperationException("Failed to locate project Data directory.");

		return Path.Combine(dataRoot, ToSystemRelativePath(relativeDataPath));
	}

	public static bool TryReadText(string relativeDataPath, out string text, out string sourceLabel) =>
		TryReadText(
			relativeDataPath,
			GetExternalDataRoot(),
			GetProjectDataRoot(),
			CanUseGodotResourceFileAccess(),
			out text,
			out sourceLabel);

	public static string ReadTextOrThrow(string relativeDataPath)
	{
		if (TryReadText(relativeDataPath, out var text, out var sourceLabel))
			return text;

		throw new FileNotFoundException(
			$"Data file not found for '{NormalizeRelativeDataPath(relativeDataPath)}'. Tried: {sourceLabel}");
	}

	private static bool TryReadText(
		string relativeDataPath,
		string? externalDataRoot,
		string? projectDataRoot,
		bool tryResourceFallback,
		out string text,
		out string sourceLabel)
	{
		var normalized = NormalizeRelativeDataPath(relativeDataPath);
		var attemptedSources = new[]
		{
			BuildExternalPathLabel(externalDataRoot, normalized),
			GetResourcePath(normalized),
			BuildProjectPathLabel(projectDataRoot, normalized),
		};

		var externalPath = BuildExternalPath(externalDataRoot, normalized);
		if (TryReadTextFromFile(externalPath, out text))
		{
			sourceLabel = externalPath!;
			return true;
		}

		if (tryResourceFallback)
		{
			var resourcePath = GetResourcePath(normalized);
			if (TryReadTextFromResource(resourcePath, out text))
			{
				sourceLabel = resourcePath;
				return true;
			}
		}

		var projectPath = BuildProjectPath(projectDataRoot, normalized);
		if (TryReadTextFromFile(projectPath, out text))
		{
			sourceLabel = projectPath!;
			return true;
		}

		text = string.Empty;
		sourceLabel = string.Join(" | ", attemptedSources);
		return false;
	}

	private static string NormalizeRelativeDataPath(string relativeDataPath)
	{
		if (string.IsNullOrWhiteSpace(relativeDataPath))
			throw new ArgumentException("Data path cannot be empty.", nameof(relativeDataPath));

		var normalized = relativeDataPath.Trim().Replace('\\', '/');
		if (normalized.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
		{
			if (!normalized.StartsWith("res://Data/", StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException($"Path is not under res://Data/: {relativeDataPath}", nameof(relativeDataPath));

			normalized = normalized["res://Data/".Length..];
		}
		else if (normalized.StartsWith("Data/", StringComparison.OrdinalIgnoreCase))
		{
			normalized = normalized["Data/".Length..];
		}

		normalized = normalized.TrimStart('/');
		var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (segments.Length == 0)
			throw new ArgumentException("Data path cannot be empty.", nameof(relativeDataPath));
		if (Array.Exists(segments, static segment => segment is "." or ".."))
			throw new ArgumentException($"Data path cannot traverse parent directories: {relativeDataPath}", nameof(relativeDataPath));

		return string.Join('/', segments);
	}

	private static string? GetExpectedExternalDataRoot(string? processPath)
	{
		if (string.IsNullOrWhiteSpace(processPath))
			return null;

		var processDirectory = Path.GetDirectoryName(processPath);
		if (string.IsNullOrWhiteSpace(processDirectory))
			return null;

		return Path.Combine(processDirectory, DataFolderName);
	}

	private static string? ResolveProjectRoot(string? startDirectory)
	{
		if (string.IsNullOrWhiteSpace(startDirectory))
			return null;

		var current = new DirectoryInfo(startDirectory);
		while (current != null)
		{
			if (File.Exists(Path.Combine(current.FullName, "project.godot")))
				return current.FullName;

			current = current.Parent;
		}

		return null;
	}

	private static bool CanUseGodotResourceFileAccess() =>
		CanUseGodotResourceFileAccess(System.Environment.ProcessPath);

	private static bool CanUseGodotResourceFileAccess(string? processPath)
	{
		try
		{
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

	private static string GetResourcePath(string normalizedRelativeDataPath) =>
		$"res://{DataFolderName}/{normalizedRelativeDataPath}";

	private static string ToSystemRelativePath(string relativeDataPath) =>
		NormalizeRelativeDataPath(relativeDataPath).Replace('/', Path.DirectorySeparatorChar);

	private static string? BuildExternalPath(string? externalDataRoot, string normalizedRelativeDataPath)
	{
		if (string.IsNullOrWhiteSpace(externalDataRoot))
			return null;

		return Path.Combine(externalDataRoot, normalizedRelativeDataPath.Replace('/', Path.DirectorySeparatorChar));
	}

	private static string BuildExternalPathLabel(string? externalDataRoot, string normalizedRelativeDataPath)
	{
		if (!string.IsNullOrWhiteSpace(externalDataRoot))
			return BuildExternalPath(externalDataRoot, normalizedRelativeDataPath)!;

		var expectedRoot = GetExpectedExternalDataRoot(System.Environment.ProcessPath);
		if (!string.IsNullOrWhiteSpace(expectedRoot))
			return BuildExternalPath(expectedRoot, normalizedRelativeDataPath)!;

		return $"<external Data unavailable>/{normalizedRelativeDataPath}";
	}

	private static string? BuildProjectPath(string? projectDataRoot, string normalizedRelativeDataPath)
	{
		if (string.IsNullOrWhiteSpace(projectDataRoot))
			return null;

		return Path.Combine(projectDataRoot, normalizedRelativeDataPath.Replace('/', Path.DirectorySeparatorChar));
	}

	private static string BuildProjectPathLabel(string? projectDataRoot, string normalizedRelativeDataPath) =>
		BuildProjectPath(projectDataRoot, normalizedRelativeDataPath)
		?? $"<project Data unavailable>/{normalizedRelativeDataPath}";

	private static bool TryReadTextFromFile(string? path, out string text)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
			{
				text = File.ReadAllText(path);
				return true;
			}
		}
		catch
		{
			// Fall through to the next source.
		}

		text = string.Empty;
		return false;
	}

	private static bool TryReadTextFromResource(string resourcePath, out string text)
	{
		try
		{
			using var file = GodotFileAccess.Open(resourcePath, GodotFileAccess.ModeFlags.Read);
			if (file != null)
			{
				text = file.GetAsText();
				return true;
			}
		}
		catch
		{
			// Fall through to the next source.
		}

		text = string.Empty;
		return false;
	}
}
