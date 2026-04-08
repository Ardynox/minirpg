using System;
using System.IO;
using Xunit;

namespace MiniRPG.Tests;

public sealed class WorldManagerModuleTests
{
	[Fact]
	public void SceneAndModule_IncludeDeleteWorldBinding()
	{
		var moduleSource = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Module", "WorldManagerModule.cs"));
		var sceneSource = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Scene", "WorldManager.tscn"));

		Assert.Contains("public event Action<string>? DeleteWorldRequested;", moduleSource);
		Assert.Contains("_deleteWorldButton = panel.GetNode<Button>(\"Margin/VBox/Footer/DeleteWorldBtn\");", moduleSource);
		Assert.Contains("[node name=\"DeleteWorldBtn\" type=\"Button\" parent=\"Margin/VBox/Footer\"]", sceneSource);
		Assert.Contains("text = \"ui.world_manager.delete_world\"", sceneSource);
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
