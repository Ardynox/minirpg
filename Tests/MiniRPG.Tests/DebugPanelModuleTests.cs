using System;
using System.IO;
using Xunit;

namespace MiniRPG.Tests;

public sealed class DebugPanelModuleTests
{
	[Fact]
	public void ModuleBindingRoots_MatchCurrentSceneHierarchy()
	{
		var moduleSource = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Module", "Panel", "DebugPanelModule.cs"));
		var sceneSource = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Scene", "DebugPanel.tscn"));

		Assert.Contains("ContentScroll/Content/QuickSection/GoldRow/GoldAmountEdit", moduleSource);
		Assert.Contains("ContentScroll/Content/SpawnSection/FilterRow/SpawnFilterOption", moduleSource);
		Assert.Contains("ContentScroll/Content/WeatherSection/TypeRow/WeatherTypeOption", moduleSource);
		Assert.Contains("ContentScroll/Content/FacilitySection/FacilityRow/FacilityOption", moduleSource);
		Assert.Contains("ResultsSection/ResultsText", moduleSource);

		Assert.Contains("[node name=\"GoldAmountEdit\" type=\"LineEdit\" parent=\"MarginContainer/VBox/ContentScroll/Content/QuickSection/GoldRow\"]", sceneSource);
		Assert.Contains("[node name=\"SpawnTemplateOption\" type=\"OptionButton\" parent=\"MarginContainer/VBox/ContentScroll/Content/SpawnSection/TemplateRow\"]", sceneSource);
		Assert.Contains("[node name=\"WeatherTypeOption\" type=\"OptionButton\" parent=\"MarginContainer/VBox/ContentScroll/Content/WeatherSection/TypeRow\"]", sceneSource);
		Assert.Contains("[node name=\"FacilityOption\" type=\"OptionButton\" parent=\"MarginContainer/VBox/ContentScroll/Content/FacilitySection/FacilityRow\"]", sceneSource);
		Assert.Contains("[node name=\"ResultsText\" type=\"RichTextLabel\" parent=\"MarginContainer/VBox/ResultsSection\"]", sceneSource);
	}

	[Fact]
	public void ModuleSource_ContainsExpectedHostRoutingAndCloseBehavior()
	{
		var moduleSource = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Module", "Panel", "DebugPanelModule.cs"));

		Assert.Contains("public interface IHost", moduleSource);
		Assert.Contains("DebugModule.Result ExecuteAddGold(int amount);", moduleSource);
		Assert.Contains("ApplyHostResult(_host.ExecuteAddGold(amount));", moduleSource);
		Assert.Contains("ApplyHostResult(_host.ExecuteSpawnActor(templateId));", moduleSource);
		Assert.Contains("ApplyHostResult(_host.ExecutePlaceFacility(facilityId, directionId));", moduleSource);
		Assert.Contains("if (cmd == \"close\")", moduleSource);
		Assert.Contains("Close();", moduleSource);
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
