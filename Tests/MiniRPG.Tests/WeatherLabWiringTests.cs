using System;
using System.IO;
using MiniRPG.Core.Map;
using Xunit;

namespace MiniRPG.Tests;

public sealed class WeatherLabWiringTests
{
	[Fact]
	public void MainMenuScene_ContainsWeatherLabButton()
	{
		var text = File.ReadAllText(GetRepoPath("Scene", "MainMenu.tscn"));

		Assert.Contains("WeatherLabBtn", text, StringComparison.Ordinal);
	}

	[Fact]
	public void SettingsPanelScene_ContainsWeatherLabButton()
	{
		var text = File.ReadAllText(GetRepoPath("Scene", "SettingsPanel.tscn"));

		Assert.Contains("WeatherLabBtn", text, StringComparison.Ordinal);
	}

	[Fact]
	public void MainScene_ContainsWeatherLabPanel()
	{
		var text = File.ReadAllText(GetRepoPath("App", "Main.tscn"));

		Assert.Contains("WeatherLabPanel", text, StringComparison.Ordinal);
	}

	[Fact]
	public void SaveSerialization_DoesNotPersistWeatherLabRuntimeTuning()
	{
		var save = new SaveFile
		{
			Version = SaveModule.CurrentVersion,
			Header = new SaveHeader
			{
				Title = "weather_lab_persist_check",
				SavedAtUtc = new DateTimeOffset(2026, 4, 8, 0, 0, 0, TimeSpan.Zero),
				Turn = 1,
				PlayerZ = 0,
				GeneratorId = "blank_floor",
				ViewModeId = "single_layer",
			},
			Payload = new SavePayload
			{
				WorldSeed = 1,
				Turn = 1,
				PlayerX = 1,
				PlayerY = 1,
				PlayerZ = 0,
				PlayerId = "player",
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

		var json = SaveModule.SerializeSaveFile(save);

		Assert.DoesNotContain("overlay_alpha_scale", json, StringComparison.Ordinal);
		Assert.DoesNotContain("particle_size_scale", json, StringComparison.Ordinal);
	}

	private static string GetRepoPath(params string[] parts) =>
		Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", Path.Combine(parts)));
}
