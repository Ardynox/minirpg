using System;
using System.IO;
using Xunit;

namespace MiniRPG.Tests;

public sealed class MultiplayerRoomIntegrationSourceTests
{
	[Fact]
	public void PauseMenuAndSettingsFlow_ExposeMultiplayerRoomEntry()
	{
		var repoRoot = ResolveRepoRoot();
		var pauseMenuModule = File.ReadAllText(Path.Combine(repoRoot, "Module", "Panel", "PauseMenuPanelModule.cs"));
		var pauseMenuScene = File.ReadAllText(Path.Combine(repoRoot, "Scene", "PauseMenuPanel.tscn"));
		var settingsFlowCoordinator = File.ReadAllText(Path.Combine(repoRoot, "Module", "Panel", "SettingsFlowCoordinator.cs"));

		Assert.Contains("PauseMenuAction.OpenMultiplayerRoom", pauseMenuModule, StringComparison.Ordinal);
		Assert.Contains("MultiplayerRoomBtn", pauseMenuScene, StringComparison.Ordinal);
		Assert.Contains("public event Action? MultiplayerRoomRequested;", settingsFlowCoordinator, StringComparison.Ordinal);
		Assert.Contains("case PauseMenuAction.OpenMultiplayerRoom:", settingsFlowCoordinator, StringComparison.Ordinal);
	}

	[Fact]
	public void MainSceneAndCoordinator_ExposeMultiplayerRoomOverlayAndSnapshotHosting()
	{
		var repoRoot = ResolveRepoRoot();
		var mainScene = File.ReadAllText(Path.Combine(repoRoot, "App", "Main.tscn"));
		var roomPanelScene = File.ReadAllText(Path.Combine(repoRoot, "Scene", "MultiplayerRoomPanel.tscn"));
		var roomPanelModule = File.ReadAllText(Path.Combine(repoRoot, "Module", "MultiplayerRoomPanelModule.cs"));
		var flowCoordinator = File.ReadAllText(Path.Combine(repoRoot, "App", "RuntimeUi", "MultiplayerFlowCoordinator.cs"));

		Assert.Contains("MultiplayerRoomPanel", mainScene, StringComparison.Ordinal);
		Assert.Contains("HostCurrentSessionBtn", roomPanelScene, StringComparison.Ordinal);
		Assert.Contains("AssignPrimaryActorBtn", roomPanelScene, StringComparison.Ordinal);
		Assert.Contains("public event Action<MultiplayerRoomHostRequest>? HostCurrentSessionRequested;", roomPanelModule, StringComparison.Ordinal);
		Assert.Contains("CreateSnapshotRoomAsync", flowCoordinator, StringComparison.Ordinal);
		Assert.Contains("InitialSnapshot = request.Snapshot", flowCoordinator, StringComparison.Ordinal);
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
