using System;
using System.Linq;
using Godot;

namespace MiniRPG;

public partial class Main
{
	private void BindPanelLauncherBar()
	{
		_panelLauncherBar = GetNode<HBoxContainer>($"{HudRootPath}/PanelLauncherBar");
		_statusLauncherBtn = GetNode<Button>($"{HudRootPath}/PanelLauncherBar/StatusBtn");
		_skillBarLauncherBtn = GetNode<Button>($"{HudRootPath}/PanelLauncherBar/SkillBarBtn");
		_skillMgrLauncherBtn = GetNode<Button>($"{HudRootPath}/PanelLauncherBar/SkillMgrBtn");
		_inventoryLauncherBtn = GetNode<Button>($"{HudRootPath}/PanelLauncherBar/InventoryBtn");
		_questLauncherBtn = GetNode<Button>($"{HudRootPath}/PanelLauncherBar/QuestBtn");
		_debugLauncherBtn = GetNode<Button>($"{HudRootPath}/PanelLauncherBar/DebugBtn");
		_settingsLauncherBtn = GetNode<Button>($"{HudRootPath}/PanelLauncherBar/SettingsBtn");

		_statusLauncherBtn.Pressed += ToggleStatusPanel;
		_skillBarLauncherBtn.Pressed += ToggleSkillBarPanel;
		_skillMgrLauncherBtn.Pressed += ToggleSkillManager;
		_inventoryLauncherBtn.Pressed += ToggleInventory;
		_questLauncherBtn.Pressed += ToggleQuestPanel;
		_debugLauncherBtn.Pressed += ToggleDebugPanel;
		_settingsLauncherBtn.Pressed += () => _mainAppFlowCoordinator.ToggleSettingsPanel();

		ApplyPanelLauncherTooltips();
		RefreshPanelLauncherState();
	}

	private void ApplyPanelLauncherTooltips()
	{
		_statusLauncherBtn.TooltipText = BuildActionTooltip("toggle_status", "Toggle status panel");
		_skillBarLauncherBtn.TooltipText = BuildActionTooltip("skillbar", "Toggle skill bar");
		_skillMgrLauncherBtn.TooltipText = BuildActionTooltip("skills", "Toggle skills panel");
		_inventoryLauncherBtn.TooltipText = BuildActionTooltip("inventory", "Toggle inventory");
		_questLauncherBtn.TooltipText = BuildActionTooltip("quests", "Toggle quest panel");
		_debugLauncherBtn.TooltipText = BuildActionTooltip("debug_panel", "Toggle debug panel");
		_settingsLauncherBtn.TooltipText = BuildActionTooltip("open_settings", "Open settings");
	}

	private string BuildActionTooltip(string actionId, string fallback)
	{
		if (_inputBindings == null)
			return fallback;

		var action = _inputBindings
			.GetActions(InputBindingContext.Action)
			.FirstOrDefault(candidate => string.Equals(candidate.Id, actionId, StringComparison.Ordinal));
		if (string.IsNullOrEmpty(action.Id))
			return fallback;

		var primary = action.Primary.ToDisplayString();
		var secondary = action.Secondary.ToDisplayString();
		if (action.Secondary.IsEmpty)
			return $"{action.Label} [{primary}]";

		return $"{action.Label} [{primary} / {secondary}]";
	}

	private string BuildActionTooltipBbcode(string actionId, string fallback)
	{
		if (_inputBindings == null)
			return fallback;

		var action = _inputBindings
			.GetActions(InputBindingContext.Action)
			.FirstOrDefault(candidate => string.Equals(candidate.Id, actionId, StringComparison.Ordinal));
		if (string.IsNullOrEmpty(action.Id))
			return fallback;

		var primary = action.Primary.ToDisplayString();
		var keyText = action.Secondary.IsEmpty
			? primary
			: $"{primary} / {action.Secondary.ToDisplayString()}";
		return $"[b]{action.Label}[/b]  [color=#888888][{keyText}][/color]";
	}

	private void BindPanelLauncherRichTooltips()
	{
		if (_richTooltips == null || _statusLauncherBtn == null)
			return;

		AttachLauncherRichTooltip(_statusLauncherBtn, "toggle_status", "Toggle status panel");
		AttachLauncherRichTooltip(_skillBarLauncherBtn, "skillbar", "Toggle skill bar");
		AttachLauncherRichTooltip(_skillMgrLauncherBtn, "skills", "Toggle skills panel");
		AttachLauncherRichTooltip(_inventoryLauncherBtn, "inventory", "Toggle inventory");
		AttachLauncherRichTooltip(_questLauncherBtn, "quests", "Toggle quest panel");
		AttachLauncherRichTooltip(_debugLauncherBtn, "debug_panel", "Toggle debug panel");
		AttachLauncherRichTooltip(_settingsLauncherBtn, "open_settings", "Open settings");
	}

	private void AttachLauncherRichTooltip(Button button, string actionId, string fallback)
	{
		// factory 闭包动态读 _inputBindings：玩家改键后 hover 立即显示新键位，
		// 不需要在 InputBindingService.Changed 上重新 Attach。
		_richTooltips!.Attach(button, () => BuildActionTooltipBbcode(actionId, fallback));
	}

	private void RefreshPanelLauncherState()
	{
		if (_statusLauncherBtn == null || _settingsFlow == null || _debugPanelController == null)
			return;

		ConfigureLauncherButton(_statusLauncherBtn, _statusPanelController.IsActiveActorPanelVisible);
		ConfigureLauncherButton(_skillBarLauncherBtn, _skillBar.Visible);
		ConfigureLauncherButton(_skillMgrLauncherBtn, _skillMgr.Visible);
		ConfigureLauncherButton(_inventoryLauncherBtn, _inventoryPanel.Visible);
		ConfigureLauncherButton(_questLauncherBtn, _questPanel?.Visible == true);
		ConfigureLauncherButton(_debugLauncherBtn, _debugPanelController.IsVisible);
		ConfigureLauncherButton(_settingsLauncherBtn, _settingsFlow.SettingsVisible);
		UpdatePanelLauncherInteractivity();
	}

	private void UpdatePanelLauncherInteractivity()
	{
		if (_panelLauncherBar == null)
			return;

		var disabled = _busyOperationActive || !ResourcesReady;
		foreach (var child in _panelLauncherBar.GetChildren())
		{
			if (child is Button button)
				button.Disabled = disabled;
		}
	}

	private static void ConfigureLauncherButton(Button button, bool pressed)
	{
		button.ButtonPressed = pressed;
		button.Modulate = pressed ? new Color(1f, 1f, 1f, 1f) : new Color(0.86f, 0.86f, 0.9f, 1f);
	}
}
