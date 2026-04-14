using System;
using System.IO;
using Xunit;

namespace MiniRPG.Tests;

public sealed class MainMenuPanelVisibilityTests
{
	[Fact]
	public void StatusPanelScene_DefaultsToHidden()
	{
		var sceneSource = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Scene", "StatusPanel.tscn"));

		Assert.Contains("[node name=\"StatusPanel\" type=\"PanelContainer\"]", sceneSource);
		Assert.Contains("visible = false", sceneSource);
	}

	[Fact]
	public void ShowMainMenuWithCurrentContinue_ClosesPanelsBeforeShowingMenu()
	{
		var source = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "App", "Main.Settings.cs"));
		var closeIndex = source.IndexOf("CloseAllInGamePanels();", StringComparison.Ordinal);
		var showMenuIndex = source.IndexOf("_menu.ShowMainMenu(", StringComparison.Ordinal);

		Assert.True(closeIndex >= 0, "Expected ShowMainMenuWithCurrentContinue to close in-game panels.");
		Assert.True(showMenuIndex >= 0, "Expected Main.Settings.cs to invoke _menu.ShowMainMenu.");
		Assert.True(closeIndex < showMenuIndex, "Expected panels to close before the main menu is shown.");
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
