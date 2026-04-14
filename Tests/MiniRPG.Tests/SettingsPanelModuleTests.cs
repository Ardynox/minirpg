using System;
using System.IO;
using MiniRPG.Core.Config;
using MiniRPG.Module.Panel;
using Xunit;

namespace MiniRPG.Tests;

public sealed class SettingsPanelModuleTests
{
	[Fact]
	public void ModuleBindingRoots_MatchCurrentSceneHierarchy()
	{
		var moduleSource = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Module", "Panel", "SettingsPanelModule.cs"));
		var sceneSource = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Scene", "SettingsPanel.tscn"));

		Assert.Contains("private const string ContentScrollPath = \"Margin/VBox/ContentScroll\";", moduleSource);
		Assert.Contains("private const string LanguageRowPath = GeneralPagePath + \"/DisplaySection/Margin/VBox/LanguageRow\";", moduleSource);
		Assert.Contains("private const string AutoNavigationInterruptPolicyRowPath = ControlsPagePath + \"/ControlOptionsSection/Margin/VBox/AutoNavigationInterruptPolicyRow\";", moduleSource);
		Assert.Contains("private const string BindingsRowPath = ControlsPagePath + \"/BindingsSection/Margin/VBox/BindingsRow\";", moduleSource);
		Assert.Contains("private const string KeyBindingsRootPath = ControlsPagePath + \"/BindingsSection/Margin/VBox/KeyBindingsView\";", moduleSource);
		Assert.Contains("private const string LayoutEditRowPath = SessionPagePath + \"/ToolsSection/Margin/VBox/LayoutEditRow\";", moduleSource);

		Assert.Contains("[node name=\"ContentScroll\" type=\"ScrollContainer\" parent=\"Margin/VBox\"]", sceneSource);
		Assert.Contains("[node name=\"LanguageRow\" type=\"PanelContainer\" parent=\"Margin/VBox/ContentScroll/Pages/GeneralPage/DisplaySection/Margin/VBox\"]", sceneSource);
		Assert.Contains("[node name=\"AutoNavigationInterruptPolicyRow\" type=\"PanelContainer\" parent=\"Margin/VBox/ContentScroll/Pages/ControlsPage/ControlOptionsSection/Margin/VBox\"]", sceneSource);
		Assert.Contains("[node name=\"BindingsRow\" type=\"PanelContainer\" parent=\"Margin/VBox/ContentScroll/Pages/ControlsPage/BindingsSection/Margin/VBox\"]", sceneSource);
		Assert.Contains("[node name=\"KeyBindingsView\" parent=\"Margin/VBox/ContentScroll/Pages/ControlsPage/BindingsSection/Margin/VBox\" instance=ExtResource(\"1_keybindings\")]", sceneSource);
		Assert.Contains("[node name=\"LayoutEditRow\" type=\"PanelContainer\" parent=\"Margin/VBox/ContentScroll/Pages/SessionPage/ToolsSection/Margin/VBox\"]", sceneSource);
	}

	[Fact]
	public void SelectionModel_MainMenuHidesSessionTabAndWatchMode()
	{
		var model = new SettingsPanelSelectionModel();
		model.ApplyState(CreateState(
			context: SettingsEntryContext.MainMenu,
			canOpenSessionTab: false));

		Assert.Equal([SettingsTab.General, SettingsTab.Controls], model.GetVisibleTabs());
		Assert.Equal(
			[SettingsPanelRowId.Language, SettingsPanelRowId.Render, SettingsPanelRowId.MapZoomMin, SettingsPanelRowId.MapZoomMax],
			model.GetVisibleRows(SettingsTab.General));
		Assert.Empty(model.GetVisibleRows(SettingsTab.Session));
	}

	[Fact]
	public void SelectionModel_PauseContextShowsSessionRowsAndClampsSelection()
	{
		var model = new SettingsPanelSelectionModel();
		model.ApplyState(CreateState(
			context: SettingsEntryContext.InGamePause,
			watchModeEnabled: true,
			canOpenSessionTab: true));

		Assert.Equal(
			[SettingsTab.General, SettingsTab.Controls, SettingsTab.Session],
			model.GetVisibleTabs());
		Assert.Equal(
			[SettingsPanelRowId.Language, SettingsPanelRowId.Render, SettingsPanelRowId.MapZoomMin, SettingsPanelRowId.MapZoomMax, SettingsPanelRowId.WatchMode, SettingsPanelRowId.FastTurnMode],
			model.GetVisibleRows(SettingsTab.General));
		Assert.Equal(
			[SettingsPanelRowId.KeyboardTargeting, SettingsPanelRowId.AutoNavigationInterruptPolicy, SettingsPanelRowId.DebugPanel, SettingsPanelRowId.KeyBindings],
			model.GetVisibleRows(SettingsTab.Controls));

		model.SetTab(SettingsTab.Session);
		model.MoveSelection(10);
		Assert.Equal(SettingsPanelRowId.LayoutEdit, model.SelectedRow);

		model.MoveSelection(-10);
		Assert.Equal(SettingsPanelRowId.Save, model.SelectedRow);
	}

	[Fact]
	public void SelectionModel_CloseExitsBindingsBeforeClosingPanel_AndCaptureDoesNotEscape()
	{
		var model = new SettingsPanelSelectionModel();
		model.ApplyState(CreateState(
			context: SettingsEntryContext.InGamePause,
			canOpenSessionTab: true));
		model.SetTab(SettingsTab.Controls);
		Assert.True(model.SetSelectedRow(SettingsPanelRowId.KeyBindings));

		model.EnterKeyBindingsMode();
		Assert.True(model.KeyBindingsMode);

		Assert.Equal(SettingsPanelCloseAction.None, model.HandleClose(keyBindingsCapturing: true));
		Assert.True(model.KeyBindingsMode);

		Assert.Equal(SettingsPanelCloseAction.ExitKeyBindingsMode, model.HandleClose(keyBindingsCapturing: false));
		Assert.False(model.KeyBindingsMode);

		Assert.Equal(SettingsPanelCloseAction.ClosePanel, model.HandleClose(keyBindingsCapturing: false));
	}

	[Fact]
	public void DynamicStateHelpers_ReturnExpectedKeys()
	{
		var readyState = CreateState(
			context: SettingsEntryContext.InGamePause,
			renderReady: true,
			watchModeEnabled: true,
			mapEditorActive: true,
			enableKeyboardTargeting: true,
			autoNavigationInterruptPolicy: AutoNavigationInterruptPolicy.HostileProximityStop,
			enableDebugPanel: false,
			canOpenSessionTab: true);

		Assert.True(SettingsPanelModule.ShouldShowWatchMode(readyState));
		Assert.Equal("ui.settings.subtitle.in_game", SettingsPanelModule.GetSubtitleKey(readyState));
		Assert.Equal("ui.settings.render.status.ready", SettingsPanelModule.GetRenderStatusKey(readyState));
		Assert.Equal("ui.settings.watch_mode.status.on", SettingsPanelModule.GetWatchModeStatusKey(readyState));
		Assert.Equal("ui.settings.keyboard_targeting.status.on", SettingsPanelModule.GetKeyboardTargetingStatusKey(readyState));
		Assert.Equal("ui.settings.auto_navigation_interrupt_policy.status.hostile_proximity_stop", SettingsPanelModule.GetAutoNavigationInterruptPolicyStatusKey(readyState));
		Assert.Equal("ui.settings.debug_panel.status.off", SettingsPanelModule.GetDebugPanelStatusKey(readyState));
		Assert.Equal("ui.settings.map_editor.status.active", SettingsPanelModule.GetMapEditorStatusKey(readyState));
		Assert.Equal("ui.settings.key_bindings.status.idle", SettingsPanelModule.GetBindingsStatusKey(keyBindingsMode: false, isCapturing: false));
		Assert.Equal("ui.settings.key_bindings.status.active", SettingsPanelModule.GetBindingsStatusKey(keyBindingsMode: true, isCapturing: false));
		Assert.Equal("ui.settings.key_bindings.status.capturing", SettingsPanelModule.GetBindingsStatusKey(keyBindingsMode: true, isCapturing: true));
		Assert.Equal("ui.settings.key_bindings.return", SettingsPanelModule.GetBindingsButtonKey(keyBindingsMode: true));
		Assert.Equal("ui.settings.hint.bindings", SettingsPanelModule.GetFooterHintKey(keyBindingsMode: true));
	}

	[Fact]
	public void CanActivateRow_BlocksRenderAndZoomRows_WhenRenderIsNotReady()
	{
		var state = CreateState(
			context: SettingsEntryContext.InGamePause,
			renderReady: false,
			canOpenSessionTab: true);

		Assert.False(SettingsPanelModule.CanActivateRow(SettingsPanelRowId.Render, state));
		Assert.False(SettingsPanelModule.CanActivateRow(SettingsPanelRowId.MapZoomMin, state));
		Assert.False(SettingsPanelModule.CanActivateRow(SettingsPanelRowId.MapZoomMax, state));
	}

	[Fact]
	public void CanActivateRow_AllowsRenderAndZoomRows_WhenRenderIsReady()
	{
		var state = CreateState(
			context: SettingsEntryContext.InGamePause,
			renderReady: true,
			canOpenSessionTab: true);

		Assert.True(SettingsPanelModule.CanActivateRow(SettingsPanelRowId.Render, state));
		Assert.True(SettingsPanelModule.CanActivateRow(SettingsPanelRowId.MapZoomMin, state));
		Assert.True(SettingsPanelModule.CanActivateRow(SettingsPanelRowId.MapZoomMax, state));
	}

	private static SettingsUiState CreateState(
		SettingsEntryContext context,
		bool renderReady = false,
		bool watchModeEnabled = false,
		bool mapEditorActive = false,
		bool canOpenSessionTab = false,
		bool enableKeyboardTargeting = false,
		AutoNavigationInterruptPolicy autoNavigationInterruptPolicy = AutoNavigationInterruptPolicy.ConservativeStop,
		bool enableDebugPanel = true) =>
		new(
			context,
			"en",
			renderReady,
			watchModeEnabled,
			false,
			mapEditorActive,
			canOpenSessionTab,
			enableKeyboardTargeting,
			enableDebugPanel,
			0.6f,
			2.4f,
			1.0f,
			autoNavigationInterruptPolicy);

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
