using System;
using System.IO;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;

namespace MiniRPG.Tests;

internal static class TestSupport
{
	public static void EnsureGameplayDataLoaded()
	{
		LocalizationService.Initialize();
		LocalizationService.SetLocale("en", notify: false);
		GameConfig.Load();
		PresetDB.Load();
		if (!HasExpectedTerrain(Terrains.Floor) || !HasExpectedTerrain(Terrains.Water))
			TerrainRegistry.Load("terrains.json");
	}

	private static bool HasExpectedTerrain(string terrainId) =>
		TerrainRegistry.Get(terrainId)?.StringId == terrainId;

	public static string CreateTempDirectory(string prefix)
	{
		var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);
		return path;
	}

	public static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
				Directory.Delete(path, recursive: true);
		}
		catch
		{
			// Ignore cleanup failures in tests.
		}
	}
}

internal sealed class ContinueStateScope : IDisposable
{
	private readonly ContinueState _previous;

	public ContinueStateScope()
	{
		_previous = AppSettingsStore.LoadContinueState();
	}

	public void Dispose()
	{
		AppSettingsStore.SaveContinueState(_previous);
	}
}
