using System.IO;
using Xunit;

namespace MiniRPG.Tests;

public sealed class WorldSettingsDialogModuleTests
{
	[Fact]
	public void ModuleBindingRoots_MatchCurrentSceneHierarchy()
	{
		var moduleSource = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Module", "WorldSettingsDialogModule.cs"));
		var sceneSource = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Scene", "WorldSettingsDialog.tscn"));

		Assert.Contains("private const string BasicFormPath = \"Margin/VBox/BasicSection/BasicForm\";", moduleSource);
		Assert.Contains("private const string AdvancedFormPath = \"Margin/VBox/AdvancedSection/AdvancedForm\";", moduleSource);

		Assert.Contains("[node name=\"WorldNameEdit\" type=\"LineEdit\" parent=\"Margin/VBox/BasicSection/BasicForm\"]", sceneSource);
		Assert.Contains("[node name=\"SeedEdit\" type=\"LineEdit\" parent=\"Margin/VBox/BasicSection/BasicForm/SeedRow\"]", sceneSource);
		Assert.Contains("[node name=\"GeneratorOption\" type=\"OptionButton\" parent=\"Margin/VBox/BasicSection/BasicForm\"]", sceneSource);
		Assert.Contains("[node name=\"ClimateEdit\" type=\"LineEdit\" parent=\"Margin/VBox/AdvancedSection/AdvancedForm\"]", sceneSource);
		Assert.Contains("[node name=\"WeatherVolatilityEdit\" type=\"LineEdit\" parent=\"Margin/VBox/AdvancedSection/AdvancedForm\"]", sceneSource);
	}

	[Fact]
	public void SceneIncludesNewSeedAndAdvancedControls()
	{
		var sceneSource = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Scene", "WorldSettingsDialog.tscn"));

		Assert.Contains("[node name=\"RandomizeSeedBtn\" type=\"Button\" parent=\"Margin/VBox/BasicSection/BasicForm/SeedRow\"]", sceneSource);
		Assert.Contains("[node name=\"AdvancedToggle\" type=\"CheckButton\" parent=\"Margin/VBox\"]", sceneSource);
		Assert.Contains("[node name=\"AdvancedSection\" type=\"VBoxContainer\" parent=\"Margin/VBox\"]", sceneSource);
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
}
