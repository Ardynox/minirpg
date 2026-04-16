using MiniRPG.Core.Config;

namespace MiniRPG.Module.Panel;

/// <summary>
/// <see cref="SettingsPanelModule"/> 的纯文案/状态映射函数集。
/// 把"给定 state 返回哪一个 localization key"的决策从面板模块里独立出来，
/// 让 SettingsPanelModule 只负责"从 Scene 拿节点 + 绑事件 + 调用 Resolver 填文本"。
/// <para>
/// 这些方法全部为纯函数（无副作用、无字段），方便测试与复用。
/// </para>
/// </summary>
internal static class SettingsPanelTextResolver
{
	public static bool ShouldShowWatchMode(SettingsUiState state) =>
		state.Context == SettingsEntryContext.InGamePause;

	public static string GetSubtitleKey(SettingsUiState state) =>
		state.Context == SettingsEntryContext.InGamePause
			? "ui.settings.subtitle.in_game"
			: "ui.settings.subtitle.main_menu";

	public static string GetFooterHintKey(bool keyBindingsMode) =>
		keyBindingsMode
			? "ui.settings.hint.bindings"
			: "ui.settings.hint.default";

	public static string GetRenderStatusKey(SettingsUiState state) =>
		state.RenderReady
			? "ui.settings.render.status.ready"
			: "ui.settings.render.status.unavailable";

	public static string GetWatchModeStatusKey(SettingsUiState state) =>
		state.WatchModeEnabled
			? "ui.settings.watch_mode.status.on"
			: "ui.settings.watch_mode.status.off";

	public static string GetFastTurnModeStatusKey(SettingsUiState state) =>
		state.FastTurnModeEnabled
			? "ui.settings.fast_turn_mode.status.on"
			: "ui.settings.fast_turn_mode.status.off";

	public static string GetKeyboardTargetingStatusKey(SettingsUiState state) =>
		state.EnableKeyboardTargeting
			? "ui.settings.keyboard_targeting.status.on"
			: "ui.settings.keyboard_targeting.status.off";

	public static string GetAutoNavigationInterruptPolicyStatusKey(SettingsUiState state) =>
		state.AutoNavigationInterruptPolicy switch
		{
			AutoNavigationInterruptPolicy.ManualOnly => "ui.settings.auto_navigation_interrupt_policy.status.manual_only",
			AutoNavigationInterruptPolicy.HostileProximityStop => "ui.settings.auto_navigation_interrupt_policy.status.hostile_proximity_stop",
			_ => "ui.settings.auto_navigation_interrupt_policy.status.conservative_stop",
		};

	public static string GetDebugPanelStatusKey(SettingsUiState state) =>
		state.EnableDebugPanel
			? "ui.settings.debug_panel.status.on"
			: "ui.settings.debug_panel.status.off";

	public static string GetBindingsStatusKey(bool keyBindingsMode, bool isCapturing) =>
		isCapturing
			? "ui.settings.key_bindings.status.capturing"
			: keyBindingsMode
				? "ui.settings.key_bindings.status.active"
				: "ui.settings.key_bindings.status.idle";

	public static string GetBindingsButtonKey(bool keyBindingsMode) =>
		keyBindingsMode
			? "ui.settings.key_bindings.return"
			: "ui.settings.key_bindings.open";

	public static string GetMapEditorStatusKey(SettingsUiState state) =>
		state.MapEditorActive
			? "ui.settings.map_editor.status.active"
			: "ui.settings.map_editor.status.inactive";

	public static bool CanActivateRow(SettingsPanelRowId rowId, SettingsUiState state) =>
		rowId switch
		{
			SettingsPanelRowId.Render or SettingsPanelRowId.MapZoomMin or SettingsPanelRowId.MapZoomMax => state.RenderReady,
			_ => true,
		};

	public static string GetToggleStateKey(bool enabled) =>
		enabled
			? "ui.settings.state.on"
			: "ui.settings.state.off";
}
