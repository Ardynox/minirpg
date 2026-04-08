using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace MiniRPG.Tests;

public sealed class ModalStateControllerTests
{
	public static IEnumerable<object[]> PrepareCases()
	{
		yield return [(int)RuntimeUiResetReason.SessionTransition, new[]
		{
			ActionIds.ClosePanelChromeSettings,
			ActionIds.HideSettingsPanels,
			ActionIds.ExitMapEditor,
			ActionIds.CancelLayoutEdit,
			ActionIds.CloseConfirmDialog,
			ActionIds.CloseWorldManager,
			ActionIds.CloseWorldSettingsDialog,
			ActionIds.CloseSaveNameDialog,
			ActionIds.CloseCharacterCreationDialog,
		}];
		yield return [(int)RuntimeUiResetReason.OpenWorldManager, new[]
		{
			ActionIds.ClosePanelChromeSettings,
			ActionIds.CloseSettingsOverlayIfVisible,
			ActionIds.ExitMapEditor,
			ActionIds.CancelLayoutEdit,
			ActionIds.CloseConfirmDialog,
			ActionIds.CloseWorldSettingsDialog,
			ActionIds.CloseSaveNameDialog,
			ActionIds.CloseCharacterCreationDialog,
		}];
		yield return [(int)RuntimeUiResetReason.OpenMenuSettings, new[]
		{
			ActionIds.ClosePanelChromeSettings,
			ActionIds.HideSettingsPanels,
			ActionIds.CloseConfirmDialog,
			ActionIds.CloseWorldManager,
			ActionIds.CloseWorldSettingsDialog,
			ActionIds.CloseSaveNameDialog,
			ActionIds.CloseCharacterCreationDialog,
		}];
		yield return [(int)RuntimeUiResetReason.OpenWorldSettings, new[]
		{
			ActionIds.ClosePanelChromeSettings,
			ActionIds.HideSettingsPanels,
			ActionIds.CloseConfirmDialog,
			ActionIds.CloseWorldManager,
			ActionIds.CloseSaveNameDialog,
			ActionIds.CloseCharacterCreationDialog,
		}];
		yield return [(int)RuntimeUiResetReason.OpenCharacterCreation, new[]
		{
			ActionIds.ClosePanelChromeSettings,
			ActionIds.HideSettingsPanels,
			ActionIds.CloseConfirmDialog,
			ActionIds.CloseWorldManager,
			ActionIds.CloseWorldSettingsDialog,
			ActionIds.CloseSaveNameDialog,
		}];
		yield return [(int)RuntimeUiResetReason.EnterMapEditor, new[]
		{
			ActionIds.ClosePanelChromeSettings,
			ActionIds.HideSettingsPanels,
			ActionIds.CancelLayoutEdit,
			ActionIds.CloseConfirmDialog,
			ActionIds.CloseWorldManager,
			ActionIds.CloseWorldSettingsDialog,
			ActionIds.CloseSaveNameDialog,
			ActionIds.CloseCharacterCreationDialog,
		}];
		yield return [(int)RuntimeUiResetReason.EnterLayoutEdit, new[]
		{
			ActionIds.ClosePanelChromeSettings,
			ActionIds.HideSettingsPanels,
		}];
	}

	[Theory]
	[MemberData(nameof(PrepareCases))]
	public void Prepare_CallsExpectedActions_ForEachReason(int reasonValue, string[] expectedActions)
	{
		var harness = new Harness();
		var reason = (RuntimeUiResetReason)reasonValue;

		harness.Controller.Prepare(reason);

		foreach (var actionId in ActionIds.All)
			Assert.Equal(expectedActions.Contains(actionId) ? 1 : 0, harness.GetCount(actionId));
	}

	private static class ActionIds
	{
		public const string ClosePanelChromeSettings = "close_panel_chrome_settings";
		public const string HideSettingsPanels = "hide_settings_panels";
		public const string CloseSettingsOverlayIfVisible = "close_settings_overlay_if_visible";
		public const string ExitMapEditor = "exit_map_editor";
		public const string CancelLayoutEdit = "cancel_layout_edit";
		public const string CloseConfirmDialog = "close_confirm_dialog";
		public const string CloseWorldManager = "close_world_manager";
		public const string CloseWorldSettingsDialog = "close_world_settings_dialog";
		public const string CloseSaveNameDialog = "close_save_name_dialog";
		public const string CloseCharacterCreationDialog = "close_character_creation_dialog";

		public static readonly string[] All =
		[
			ClosePanelChromeSettings,
			HideSettingsPanels,
			CloseSettingsOverlayIfVisible,
			ExitMapEditor,
			CancelLayoutEdit,
			CloseConfirmDialog,
			CloseWorldManager,
			CloseWorldSettingsDialog,
			CloseSaveNameDialog,
			CloseCharacterCreationDialog,
		];
	}

	private sealed class Harness
	{
		private readonly Dictionary<string, int> _counts = new();

		public Harness()
		{
			foreach (var actionId in ActionIds.All)
				_counts[actionId] = 0;

			Controller = new ModalStateController(
				Count(ActionIds.ClosePanelChromeSettings),
				Count(ActionIds.HideSettingsPanels),
				Count(ActionIds.CloseSettingsOverlayIfVisible),
				Count(ActionIds.ExitMapEditor),
				Count(ActionIds.CancelLayoutEdit),
				Count(ActionIds.CloseConfirmDialog),
				Count(ActionIds.CloseWorldManager),
				Count(ActionIds.CloseWorldSettingsDialog),
				Count(ActionIds.CloseSaveNameDialog),
				Count(ActionIds.CloseCharacterCreationDialog));
		}

		public ModalStateController Controller { get; }

		public int GetCount(string actionId) => _counts[actionId];

		private System.Action Count(string actionId) => () => _counts[actionId]++;
	}
}
