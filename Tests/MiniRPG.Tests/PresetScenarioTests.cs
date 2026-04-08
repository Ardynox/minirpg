using System;
using System.IO;
using System.Linq;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Map;
using MiniRPG.Core.World;
using MiniRPG.Module;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class PresetScenarioTests
{
	[Fact]
	public void PresetScenarioManifest_Loads_And_AllTemplatesDeserialize()
	{
		var scenarios = PresetScenarioCatalog.List();
		Assert.Equal(4, scenarios.Count);
		Assert.Contains(scenarios, static scenario => string.Equals(scenario.Id, "weather_lab", StringComparison.Ordinal));

		foreach (var scenario in scenarios)
		{
			var json = PresetScenarioCatalog.ReadText(scenario.TemplatePath);
			var saveFile = SaveModule.DeserializeSaveFile(json);
			Assert.NotNull(saveFile);
		}
	}

	[Fact]
	public void WeatherLabPresetScenario_LoadsSuccessfully()
	{
		EnsureTerrainRegistryLoaded();
		var state = new GameState();
		var session = new GameSessionModule(state, new FogOfWarTracker());

		var status = session.LoadPresetScenario("weather_lab");

		Assert.Equal(SaveLoadStatus.Success, status);
		Assert.Equal("weather_lab", session.CurrentPresetScenarioId);
		Assert.Equal(0, state.PlayerZ);
		Assert.True(state.World?.IsWeatherExposed(state.PlayerX, state.PlayerY, state.PlayerZ));
	}

	[Fact]
	public void ListBrowserEntries_PutsPresetScenariosBeforeUserSaves()
	{
		var session = new GameSessionModule(new GameState(), new FogOfWarTracker());
		var tempPath = Path.GetFullPath(Path.Combine(
			Path.GetDirectoryName(GameSessionModule.ManualSavePath)!,
			$"_browser_entry_{Guid.NewGuid():N}.json"));

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
			File.WriteAllText(tempPath, SaveModule.SerializeSaveFile(CreateMinimalSaveFile("browser_entry")));

			var entries = session.ListBrowserEntries();
			var presetCount = PresetScenarioCatalog.List().Count;

			Assert.NotEmpty(entries);
			Assert.All(entries.Take(presetCount), entry => Assert.Equal(SaveSlotKind.PresetScenario, entry.Kind));
			Assert.Contains(entries.Skip(presetCount), entry =>
				entry.Kind == SaveSlotKind.UserSave
				&& string.Equals(entry.SourcePath, tempPath, StringComparison.OrdinalIgnoreCase));
		}
		finally
		{
			TryDelete(tempPath);
		}
	}

	[Fact]
	public void ListSaveSlots_IncludesIncompatibleSaveWithIncompatibleSummary()
	{
		var session = new GameSessionModule(new GameState(), new FogOfWarTracker());
		var tempPath = Path.GetFullPath(Path.Combine(
			Path.GetDirectoryName(GameSessionModule.ManualSavePath)!,
			$"_legacy_slot_{Guid.NewGuid():N}.json"));

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
			File.WriteAllText(tempPath, "{\"turn\":1,\"playerX\":2}");

			var slot = Assert.Single(
				session.ListSaveSlots(),
				entry => string.Equals(entry.SourcePath, tempPath, StringComparison.OrdinalIgnoreCase));
			var incompatiblePrefix = LocalizationService.T("ui.save_browser.summary.incompatible", ("timestamp", ""))
				.Split('|', StringSplitOptions.TrimEntries)[0];

			Assert.Contains(incompatiblePrefix, slot.Summary, StringComparison.Ordinal);
		}
		finally
		{
			TryDelete(tempPath);
		}
	}

	[Fact]
	public void LoadPresetScenario_KeepsTemplateReadOnly()
	{
		EnsureTerrainRegistryLoaded();
		var state = new GameState();
		var session = new GameSessionModule(state, new FogOfWarTracker());
		var scenario = PresetScenarioCatalog.List().First();
		var status = session.LoadPresetScenario(scenario.Id);

		Assert.Equal(SaveLoadStatus.Success, status);
		Assert.True(session.GameStarted);
		Assert.Null(session.CurrentSavePath);
		Assert.Equal(scenario.Id, session.CurrentPresetScenarioId);
		Assert.Equal(GameSessionModule.ManualSavePath, session.GetPreferredSavePath());
		Assert.NotEqual(
			PresetScenarioCatalog.ResolveFileSystemPath(scenario.TemplatePath),
			session.GetPreferredSavePath());
		Assert.True(state.Actors.Count > 0);
	}

	[Fact]
	public void BuildPresetScenarioJson_IsDeterministic()
	{
		EnsureTerrainRegistryLoaded();
		var session = new GameSessionModule(new GameState(), new FogOfWarTracker());
		var scenario = PresetScenarioCatalog.List().First();
		Assert.Equal(SaveLoadStatus.Success, session.LoadPresetScenario(scenario.Id));

		var first = session.BuildPresetScenarioJson(scenario.Id);
		var second = session.BuildPresetScenarioJson(scenario.Id);
		var header = SaveModule.DeserializeSaveHeader(first);

		Assert.Equal(first, second);
		Assert.NotNull(header);
		Assert.Equal(PresetScenarioCatalog.ExportTimestamp, header!.SavedAtUtc);
	}

	private static SaveFile CreateMinimalSaveFile(string title) => new()
	{
		Version = SaveModule.CurrentVersion,
		Header = new SaveHeader
		{
			Title = title,
			SavedAtUtc = new DateTimeOffset(2026, 4, 6, 0, 0, 0, TimeSpan.Zero),
			Turn = 3,
			PlayerZ = 0,
			GeneratorId = "blank_floor",
			ViewModeId = "single_layer",
		},
		Payload = new SavePayload
		{
			WorldSeed = 123,
			Turn = 3,
			PlayerX = 1,
			PlayerY = 1,
			PlayerZ = 0,
			PlayerId = "player",
			PlayerAppearanceId = null,
			KillCount = 0,
			GeneratorId = "blank_floor",
			ViewModeId = "single_layer",
			Actors = [],
			Quests = [],
			DirtyChunks = [],
			Timeline = new TimelineSnapshot
			{
				Actors = [],
			},
		},
	};

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
				File.Delete(path);
		}
		catch
		{
			// Ignore cleanup failures in tests.
		}
	}

	private static void EnsureTerrainRegistryLoaded()
	{
		var floor = TerrainRegistry.Get(Terrains.Floor);
		if (floor != null && floor.StringId == Terrains.Floor)
			return;

		var path = PresetScenarioCatalog.ResolveFileSystemPath("res://Data/terrains.json");
		TerrainRegistry.LoadFromFile(path);
	}
}
