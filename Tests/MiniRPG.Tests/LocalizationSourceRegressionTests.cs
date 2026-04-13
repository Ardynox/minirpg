using System;
using System.IO;
using Xunit;

namespace MiniRPG.Tests;

public sealed class LocalizationSourceRegressionTests
{
	[Fact]
	public void MainScene_UsesLocalizedLauncherKeys_InsteadOfHardcodedEnglish()
	{
		var mainScene = File.ReadAllText(GetRepoPath("App", "Main.tscn"));

		Assert.Contains("text = \"input.action.toggle_status\"", mainScene, StringComparison.Ordinal);
		Assert.Contains("text = \"input.action.skillbar\"", mainScene, StringComparison.Ordinal);
		Assert.Contains("text = \"input.action.skills\"", mainScene, StringComparison.Ordinal);
		Assert.Contains("text = \"input.action.inventory\"", mainScene, StringComparison.Ordinal);
		Assert.Contains("text = \"input.action.quests\"", mainScene, StringComparison.Ordinal);
		Assert.Contains("text = \"input.action.debug_panel\"", mainScene, StringComparison.Ordinal);
		Assert.Contains("text = \"input.action.open_settings\"", mainScene, StringComparison.Ordinal);

		Assert.DoesNotContain("text = \"Status\"", mainScene, StringComparison.Ordinal);
		Assert.DoesNotContain("text = \"Skill Bar\"", mainScene, StringComparison.Ordinal);
		Assert.DoesNotContain("text = \"Skills\"", mainScene, StringComparison.Ordinal);
		Assert.DoesNotContain("text = \"Inventory\"", mainScene, StringComparison.Ordinal);
		Assert.DoesNotContain("text = \"Quest\"", mainScene, StringComparison.Ordinal);
		Assert.DoesNotContain("text = \"Debug\"", mainScene, StringComparison.Ordinal);
		Assert.DoesNotContain("text = \"Settings\"", mainScene, StringComparison.Ordinal);
	}

	[Fact]
	public void MapEditorSources_RebuildLocalizedEnvironmentOptions_AndAvoidOldEnglishLiterals()
	{
		var sceneSource = File.ReadAllText(GetRepoPath("Scene", "MapEditorBar.tscn"));
		var moduleSource = File.ReadAllText(GetRepoPath("Module", "Editor", "MapEditorBarModule.cs"));

		Assert.Contains("text = \"ui.map_editor.category.environment\"", sceneSource, StringComparison.Ordinal);
		Assert.Contains("text = \"ui.map_editor.time_of_day\"", sceneSource, StringComparison.Ordinal);
		Assert.Contains("text = \"ui.map_editor.weather\"", sceneSource, StringComparison.Ordinal);
		Assert.Contains("text = \"ui.map_editor.lighting_profile\"", sceneSource, StringComparison.Ordinal);
		Assert.Contains("text = \"ui.map_editor.turn_controller\"", sceneSource, StringComparison.Ordinal);
		Assert.Contains("text = \"ui.map_editor.undo\"", sceneSource, StringComparison.Ordinal);
		Assert.Contains("text = \"ui.map_editor.redo\"", sceneSource, StringComparison.Ordinal);
		Assert.Contains("text = \"ui.map_editor.hint.v2\"", sceneSource, StringComparison.Ordinal);

		Assert.Contains("RebuildEnvironmentOptions();", moduleSource, StringComparison.Ordinal);
		Assert.Contains("RebuildOptionButton(_weatherSelect, WeatherOptionTextKeys, weatherIndex);", moduleSource, StringComparison.Ordinal);
		Assert.Contains("RebuildOptionButton(_intensitySelect, WeatherIntensityTextKeys, intensityIndex);", moduleSource, StringComparison.Ordinal);
		Assert.Contains("RebuildOptionButton(_lightingSelect, LightingProfileTextKeys, lightingIndex);", moduleSource, StringComparison.Ordinal);
		Assert.Contains("LocalizationService.T(\"ui.map_editor.category.environment\")", moduleSource, StringComparison.Ordinal);
		Assert.Contains("LocalizationService.T(\"ui.map_editor.hint.v2\")", moduleSource, StringComparison.Ordinal);
		Assert.Contains("LocalizationService.T(\"ui.map_editor.undo\")", moduleSource, StringComparison.Ordinal);
		Assert.Contains("LocalizationService.T(\"ui.map_editor.redo\")", moduleSource, StringComparison.Ordinal);
		Assert.Contains("LocalizationService.T(\"ui.map_editor.turn_controller\")", moduleSource, StringComparison.Ordinal);

		Assert.DoesNotContain("\"Environment\"", moduleSource, StringComparison.Ordinal);
		Assert.DoesNotContain("\"LMB: Place  RMB: Erase  Scroll: Cycle\\nTab: Category  Ctrl+Z/Y: Undo/Redo\"", moduleSource, StringComparison.Ordinal);
		Assert.DoesNotContain("\"Undo\"", moduleSource, StringComparison.Ordinal);
		Assert.DoesNotContain("\"Redo\"", moduleSource, StringComparison.Ordinal);
		Assert.DoesNotContain("\"Turn Play\"", moduleSource, StringComparison.Ordinal);
		Assert.DoesNotContain("\"Clear\"", moduleSource, StringComparison.Ordinal);
		Assert.DoesNotContain("\"Rain\"", moduleSource, StringComparison.Ordinal);
		Assert.DoesNotContain("\"Fog\"", moduleSource, StringComparison.Ordinal);
		Assert.DoesNotContain("\"Snow\"", moduleSource, StringComparison.Ordinal);
		Assert.DoesNotContain("\"Storm\"", moduleSource, StringComparison.Ordinal);
		Assert.DoesNotContain("\"Thunderstorm\"", moduleSource, StringComparison.Ordinal);
		Assert.DoesNotContain("\"Sandstorm\"", moduleSource, StringComparison.Ordinal);
		Assert.DoesNotContain("\"Light\"", moduleSource, StringComparison.Ordinal);
		Assert.DoesNotContain("\"Normal\"", moduleSource, StringComparison.Ordinal);
		Assert.DoesNotContain("\"Heavy\"", moduleSource, StringComparison.Ordinal);
		Assert.DoesNotContain("\"Default\"", moduleSource, StringComparison.Ordinal);
		Assert.DoesNotContain("\"Cinematic\"", moduleSource, StringComparison.Ordinal);
		Assert.DoesNotContain("\"Soft\"", moduleSource, StringComparison.Ordinal);
	}

	[Fact]
	public void PauseMenuModule_UsesLocalizedMultiplayerRoomKey_WithoutFallbackEnglish()
	{
		var pauseMenuModule = File.ReadAllText(GetRepoPath("Module", "Panel", "PauseMenuPanelModule.cs"));

		Assert.Contains("ui.pause_menu.multiplayer_room", pauseMenuModule, StringComparison.Ordinal);
		Assert.Contains("LocalizationService.T(_items[i].TextKey)", pauseMenuModule, StringComparison.Ordinal);
		Assert.DoesNotContain("\"Multiplayer Room\"", pauseMenuModule, StringComparison.Ordinal);
	}

	[Fact]
	public void DialogScenes_DoNotShipHardcodedEnglishDefaults()
	{
		var loadRecoveryScene = File.ReadAllText(GetRepoPath("Scene", "LoadRecoveryDialog.tscn"));
		var confirmDialogScene = File.ReadAllText(GetRepoPath("Scene", "ConfirmDialog.tscn"));

		Assert.Contains("text = \"ui.load_recovery.title\"", loadRecoveryScene, StringComparison.Ordinal);
		Assert.Contains("text = \"ui.load_recovery.message\"", loadRecoveryScene, StringComparison.Ordinal);
		Assert.Contains("text = \"ui.load_recovery.confirm\"", loadRecoveryScene, StringComparison.Ordinal);
		Assert.Contains("text = \"ui.load_recovery.cancel\"", loadRecoveryScene, StringComparison.Ordinal);
		Assert.DoesNotContain("text = \"Load Recovery\"", loadRecoveryScene, StringComparison.Ordinal);
		Assert.DoesNotContain("text = \"Rebind\"", loadRecoveryScene, StringComparison.Ordinal);

		Assert.DoesNotContain("text = \"Confirm\"", confirmDialogScene, StringComparison.Ordinal);
		Assert.DoesNotContain("text = \"Cancel\"", confirmDialogScene, StringComparison.Ordinal);
	}

	private static string GetRepoPath(params string[] parts) =>
		Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", Path.Combine(parts)));
}
