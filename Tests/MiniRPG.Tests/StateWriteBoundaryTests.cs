using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace MiniRPG.Tests;

public sealed class StateWriteBoundaryTests
{
	private static readonly HashSet<string> IgnoredDirNames =
	[
		".git",
		".godot",
		".claude",
		".codex",
		"bin",
		"obj",
	];

	[Fact]
	public void AppAndModule_MustNotCallNeedOrHealthSyncDirectly()
	{
		var root = ResolveRepoRoot();
		var hits = FindForbiddenCallSites(
			root,
			[
				Path.Combine(root, "App"),
				Path.Combine(root, "Module"),
			],
			[
				"NeedSystem.Sync(",
				"HealthSystem.Sync(",
			],
			allowedRelativePaths:
			[
				"MiniRPG.Shared/Module/ActorDerivedStateUpdater.cs",
			]);

		AssertNoHits(
			hits,
			"Direct Need/Health sync calls are forbidden in App/Module. Use ActorDerivedStateUpdater instead.");
	}

	[Fact]
	public void Main_MustRouteTimelineWritePathsThroughGateway()
	{
		var root = ResolveRepoRoot();
		var mainPath = Path.Combine(root, "App", "Main.cs");
		var hits = FindForbiddenCallSites(
			root,
			[mainPath],
			[
				"TimelineTurnManager.SubmitPlayerAction(",
				"TimelineTurnManager.AdvanceAuto(",
			],
			allowedRelativePaths: []);

		AssertNoHits(
			hits,
			"Main must call TimelineTurnGateway for submit/auto-advance, not TimelineTurnManager directly.");
	}

	[Fact]
	public void SaveSnapshotLoadPaths_MustBeOwnedByGameSessionModule()
	{
		var root = ResolveRepoRoot();
		var hits = FindForbiddenCallSites(
			root,
			[
				Path.Combine(root, "App"),
				Path.Combine(root, "Module"),
				Path.Combine(root, "MiniRPG.Shared"),
			],
			[
				"SaveModule.LoadGame(",
				"SaveModule.ApplySnapshot(",
			],
			allowedRelativePaths:
			[
				"MiniRPG.Shared/Module/GameSessionModule.cs",
				"MiniRPG.Shared/Core/Multiplayer/DedicatedGameServerHost.cs",
			]);

		AssertNoHits(
			hits,
			"Runtime calls to SaveModule.LoadGame/ApplySnapshot are only allowed in GameSessionModule.");
	}

	[Fact]
	public void MapGenInitializeWorld_MustBeOwnedByGameSessionModule()
	{
		var root = ResolveRepoRoot();
		var hits = FindForbiddenCallSites(
			root,
			[
				Path.Combine(root, "App"),
				Path.Combine(root, "Module"),
				Path.Combine(root, "MiniRPG.Shared"),
			],
			[
				"MapGenModule.InitializeWorld(",
			],
			allowedRelativePaths:
			[
				"MiniRPG.Shared/Module/GameSessionModule.cs",
			]);

		AssertNoHits(
			hits,
			"Runtime calls to MapGenModule.InitializeWorld are only allowed in GameSessionModule.");
	}

	private static List<SourceHit> FindForbiddenCallSites(
		string repoRoot,
		IReadOnlyList<string> roots,
		IReadOnlyList<string> forbiddenPatterns,
		IReadOnlyList<string> allowedRelativePaths)
	{
		var allowed = new HashSet<string>(
			allowedRelativePaths.Select(NormalizePath),
			StringComparer.OrdinalIgnoreCase);

		var hits = new List<SourceHit>();
		foreach (var path in EnumerateSourceFiles(roots))
		{
			var relative = NormalizePath(Path.GetRelativePath(repoRoot, path));
			var isAllowedFile = allowed.Contains(relative);
			var lines = File.ReadAllLines(path);
			for (var i = 0; i < lines.Length; i++)
			{
				var line = lines[i];
				foreach (var pattern in forbiddenPatterns)
				{
					if (!line.Contains(pattern, StringComparison.Ordinal))
						continue;
					if (isAllowedFile)
						continue;

					hits.Add(new SourceHit(relative, i + 1, line.Trim(), pattern));
				}
			}
		}

		return hits;
	}

	private static IEnumerable<string> EnumerateSourceFiles(IReadOnlyList<string> roots)
	{
		foreach (var root in roots)
		{
			if (File.Exists(root))
			{
				yield return root;
				continue;
			}

			if (!Directory.Exists(root))
				continue;

			foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
			{
				var file = new FileInfo(path);
				if (file.Directory!.EnumerateDirectoriesUpward().Any(static dir => IgnoredDirNames.Contains(dir.Name)))
					continue;

				yield return path;
			}
		}
	}

	private static void AssertNoHits(IReadOnlyList<SourceHit> hits, string failureMessage)
	{
		Assert.True(
			hits.Count == 0,
			failureMessage + Environment.NewLine + string.Join(Environment.NewLine, hits.Select(static hit => hit.ToString())));
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

	private static string NormalizePath(string path) => path.Replace('\\', '/');

	private readonly record struct SourceHit(string RelativePath, int LineNumber, string LineText, string Pattern)
	{
		public override string ToString() => $"{RelativePath}:{LineNumber}: matched `{Pattern}` in `{LineText}`";
	}
}

file static class DirectoryInfoBoundaryExtensions
{
	public static IEnumerable<DirectoryInfo> EnumerateDirectoriesUpward(this DirectoryInfo directory)
	{
		for (DirectoryInfo? current = directory; current != null; current = current.Parent)
			yield return current;
	}
}
