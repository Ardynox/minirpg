using System;
using System.IO;
using Xunit;

namespace MiniRPG.Tests;

public sealed class WorldManagerModuleTests
{
	[Fact]
	public void SceneAndModule_IncludeDangerZoneBindings()
	{
		var moduleSource = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Module", "WorldManagerModule.cs"));
		var sceneSource = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Scene", "WorldManager.tscn"));

		Assert.Contains("public event Action<string>? DeleteWorldRequested;", moduleSource);
		Assert.Contains("_deleteWorldButton = panel.GetNode<Button>(\"Margin/VBox/Pages/WorldsPage/Body/DetailColumn/DangerSection/DangerActions/DeleteWorldBtn\");", moduleSource);
		Assert.Contains("public event Action<string>? DeleteSaveDataRequested;", moduleSource);
		Assert.Contains("public event Action<string>? CleanAssetsRequested;", moduleSource);
		Assert.Contains("_cleanAssetsButton = panel.GetNode<Button>(\"Margin/VBox/Pages/WorldsPage/Body/DetailColumn/DangerSection/DangerActions/CleanAssetsBtn\");", moduleSource);
		Assert.Contains("_deleteSaveDataButton = panel.GetNode<Button>(\"Margin/VBox/Pages/WorldsPage/Body/DetailColumn/DangerSection/DangerActions/DeleteSaveDataBtn\");", moduleSource);
		Assert.Contains("[node name=\"DangerSection\" type=\"VBoxContainer\" parent=\"Margin/VBox/Pages/WorldsPage/Body/DetailColumn\"]", sceneSource);
		Assert.Contains("[node name=\"DeleteWorldBtn\" type=\"Button\" parent=\"Margin/VBox/Pages/WorldsPage/Body/DetailColumn/DangerSection/DangerActions\"]", sceneSource);
		Assert.Contains("text = \"ui.world_manager.delete_world\"", sceneSource);
		Assert.Contains("text = \"ui.world_manager.delete_save_data\"", sceneSource);
		Assert.Contains("text = \"ui.world_manager.clean_assets\"", sceneSource);
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
