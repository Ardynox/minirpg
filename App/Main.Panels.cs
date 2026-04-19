using System;
using Godot;
using MiniRPG.Module;
using MiniRPG.Module.Panel;

namespace MiniRPG;

public partial class Main
{
	private void RefreshVisiblePanels()
	{
		_runtimeViewCoordinator.RefreshVisiblePanels(
			_debugPanelController,
			_statusPanelController,
			() => _mainAppFlowCoordinator.RefreshWorldManagerContents(),
			RefreshMultiplayerRoomPanelState);

		if (_tradeUI?.InTrade == true)
			_tradeUI.Refresh();
		if (_conversationUI?.InConversation == true)
			_conversationUI.RefreshFromState();
	}

	// ── 懒加载低频面板 ──────────────────────────────────

	private ChestPanelModule EnsureChestPanel()
	{
		if (_chestPanel != null) return _chestPanel;
		var scene = LoadPackedSceneCached(ChestPanelScenePath);
		var node = scene.Instantiate<PanelContainer>();
		TopRow.AddChild(node);
		LocalizationService.LocalizeTree(node);
		_chestPanel = new ChestPanelModule(node, this);
		_chestCoordinator.ChestPanel = _chestPanel;
		_panels.Register(_chestPanel);
		RegisterAlwaysDirectDraggable(_chestPanel);
		RegisterCommonPanelChrome(_chestPanel, "MarginContainer/VBox/HeaderBar/Header", CloseChestPanel);
		TryRegisterPanelTooltip(_chestPanel, _richTooltips);
		return _chestPanel;
	}

	private ConversationPanelModule EnsureConversationPanel()
	{
		if (_conversationPanel != null) return _conversationPanel;
		var scene = LoadPackedSceneCached(DialogPanelScenePath);
		var node = scene.Instantiate<PanelContainer>();
		TopRow.AddChild(node);
		LocalizationService.LocalizeTree(node);
		_conversationPanel = new ConversationPanelModule(node);
		_panels.Register(_conversationPanel);
		RegisterAlwaysDirectDraggable(_conversationPanel);
		RegisterCommonPanelChrome(_conversationPanel, "MarginContainer/VBox/HeaderBar/Header", CloseConversationPanel);
		return _conversationPanel;
	}

	private TradePanelModule EnsureTradePanel()
	{
		if (_tradePanel != null) return _tradePanel;
		var scene = LoadPackedSceneCached(TradePanelScenePath);
		var node = scene.Instantiate<PanelContainer>();
		TopRow.AddChild(node);
		LocalizationService.LocalizeTree(node);
		_tradePanel = new TradePanelModule(node);
		_panels.Register(_tradePanel);
		RegisterAlwaysDirectDraggable(_tradePanel);
		RegisterCommonPanelChrome(_tradePanel, "MarginContainer/VBox/HeaderBar/Header", CloseTradePanel);
		TryRegisterPanelTooltip(_tradePanel, _richTooltips);
		return _tradePanel;
	}

	private QuestPanelModule EnsureQuestPanel()
	{
		if (_questPanel != null) return _questPanel;
		var scene = LoadPackedSceneCached(QuestPanelScenePath);
		var node = scene.Instantiate<PanelContainer>();
		TopRow.AddChild(node);
		LocalizationService.LocalizeTree(node);
		_questPanel = new QuestPanelModule(node);
		_panels.Register(_questPanel);
		RegisterAlwaysDirectDraggable(_questPanel);
		RegisterCommonPanelChrome(_questPanel, "MarginContainer/VBox/HeaderBar/Header", CloseQuestPanel);
		return _questPanel;
	}

	private DebugPanelModule CreateDebugPanel(DebugPanelModule.IHost host, Action closeAction)
	{
		var scene = LoadPackedSceneCached(DebugPanelScenePath);
		var node = scene.Instantiate<PanelContainer>();
		TopRow.AddChild(node);
		LocalizationService.LocalizeTree(node);
		var debugPanel = new DebugPanelModule(node, host);
		_panels.Register(debugPanel);
		RegisterAlwaysDirectDraggable(debugPanel);
		RegisterCommonPanelChrome(debugPanel, "MarginContainer/VBox/HeaderBar/Header", closeAction);
		return debugPanel;
	}

	private ActorInspectPanelModule EnsureActorInspectPanel()
	{
		if (_actorInspectPanel != null) return _actorInspectPanel;
		var scene = LoadPackedSceneCached(ActorInspectPanelScenePath);
		var node = scene.Instantiate<PanelContainer>();
		TopRow.AddChild(node);
		LocalizationService.LocalizeTree(node);
		_actorInspectPanel = new ActorInspectPanelModule(node);
		_actorInspectPanel.CloseRequested += CloseActorInspectPanel;
		_panels.Register(_actorInspectPanel);
		RegisterAlwaysDirectDraggable(_actorInspectPanel);
		RegisterCommonPanelChrome(_actorInspectPanel, "MarginContainer/VBox/HeaderBar/NameInfo", CloseActorInspectPanel);
		return _actorInspectPanel;
	}

	private LimbTargetPanelModule EnsureLimbTargetPanel()
	{
		if (_limbTargetPanel != null)
			return _limbTargetPanel;

		var node = LimbTargetPanelModule.CreateControl(GetNode<Control>(HudRootPath).Theme);
		TopRow.AddChild(node);
		_limbTargetPanel = new LimbTargetPanelModule(node);
		_limbTargetCoordinator.LimbTargetPanel = _limbTargetPanel;
		_limbTargetPanel.CloseRequested += CloseLimbTargetPanel;
		_limbTargetPanel.TargetConfirmed += HandleLimbTargetConfirmed;
		_panels.Register(_limbTargetPanel);
		RegisterAlwaysDirectDraggable(_limbTargetPanel);
		RegisterCommonPanelChrome(_limbTargetPanel, "MarginContainer/VBox/HeaderBar/Header", CloseLimbTargetPanel);
		return _limbTargetPanel;
	}

	private TradeUIModule EnsureTradeUI()
	{
		if (_tradeUI != null) return _tradeUI;
		_tradeUI = new TradeUIModule(this, EnsureTradePanel(), _panels);
		return _tradeUI;
	}

	private ConversationUIModule EnsureConversationUI()
	{
		if (_conversationUI != null) return _conversationUI;
		_conversationUI = new ConversationUIModule(this, EnsureConversationPanel(), _panels);
		return _conversationUI;
	}

	private static PackedScene LoadPackedSceneCached(string path)
	{
		var scene = ResAccess.Get<PackedScene>(path);
		if (scene != null)
			return scene;

		throw new InvalidOperationException($"Failed to load PackedScene: {path}");
	}

	private void RegisterAlwaysDirectDraggable(IPanel panel)
	{
		_panelLayouts.RegisterPanel(panel.PanelId, panel.PanelNode);
		_panelDrag.Register(new DraggablePanelRegistration(
			panel.PanelId,
			panel.PanelNode,
			PanelDragAvailability.Always,
			[],
			DefaultFloating: true
		));
	}

	private void RegisterEditModeOnly(string panelId, PanelContainer panelNode, bool defaultFloating, params Control[] dragHandles)
	{
		_panelLayouts.RegisterPanel(panelId, panelNode);
		_panelDrag.Register(new DraggablePanelRegistration(
			panelId,
			panelNode,
			PanelDragAvailability.EditModeOnly,
			dragHandles,
			defaultFloating
		));
	}

	private void RegisterCommonPanelChrome(IPanel panel, string titleDragPath, Action closeAction)
	{
		var dragHandle = panel.PanelNode.GetNodeOrNull<Control>(titleDragPath);
		if (dragHandle == null)
		{
			GD.PushWarning($"[Main] Missing chrome drag handle '{titleDragPath}' for panel '{panel.PanelId}'.");
			return;
		}

		_panelChrome.Register(new PanelHoverChromeRegistration(
			panel.PanelId,
			panel.PanelNode,
			[dragHandle],
			closeAction));
	}

	// ══════════════════════════════════════════════════════
	//  状态面板 toggle
	// ══════════════════════════════════════════════════════

	private void ToggleStatusPanel()
	{
		_statusPanelController.ToggleActiveActorPanel();
	}

	private void CloseStatusPanel()
	{
		var actor = PartyModule.GetActiveActor(_state);
		if (actor == null)
			return;
		if (!_statusPanelController.IsActiveActorPanelVisible)
			return;

		_statusPanelController.ToggleActiveActorPanel();
	}

	private void ToggleSkillBarPanel()
	{
		if (_skillBar.Visible)
		{
			CloseSkillBarPanel();
			return;
		}

		_skillBar.Open(ActorModule.GetPlayer(_state));
		_panels.PushFocus(_skillBar);
	}

	private void CloseSkillBarPanel()
	{
		if (!_skillBar.Visible)
			return;

		_skillBar.Close();
		_panels.OnPanelClosed(_skillBar);
	}

	private void ToggleSkillManager()
	{
		if (_skillMgr.Visible)
		{
			CloseSkillManagerPanel();
		}
		else
		{
			var player = ActorModule.GetPlayer(_state);
			_skillMgr.State = _state;
			_skillMgr.Open(player);
			_panels.PushFocus(_skillMgr);
		}
	}

	private void CloseSkillManagerPanel()
	{
		if (!_skillMgr.Visible)
			return;

		_skillMgr.Close();
		_panels.OnPanelClosed(_skillMgr);
	}

	private void ToggleQuestPanel()
	{
		var quest = EnsureQuestPanel();
		if (quest.Visible)
		{
			CloseQuestPanel();
		}
		else
		{
			quest.Open(_state);
			_panels.PushFocus(quest);
		}
	}

	private void CloseQuestPanel()
	{
		if (_questPanel == null || !_questPanel.Visible)
			return;

		_questPanel.Close();
		_panels.OnPanelClosed(_questPanel);
	}

	private void ToggleDebugPanel() => _debugPanelController.Toggle();

	private void CloseDebugPanel() => _debugPanelController.Close();

	private void ToggleInventory()
	{
		if (_inventoryPanel.Visible)
		{
			CloseInventoryPanel();
		}
		else
		{
			_inventoryPanel.Visible = true;
			_panels.PushFocus(_inventoryPanel);
		}
		FlushMap();
	}

	private void CloseInventoryPanel()
	{
		if (!_inventoryPanel.Visible)
			return;

		_inventoryPanel.Visible = false;
		_panels.OnPanelClosed(_inventoryPanel);
	}

	private void CloseTradePanel()
	{
		if (_tradeUI != null && _tradeUI.InTrade)
		{
			_tradeUI.CloseTrade();
			return;
		}

		if (_tradePanel == null || !_tradePanel.Visible)
			return;

		_tradePanel.Close();
		_panels.OnPanelClosed(_tradePanel);
	}

	private void CloseConversationPanel()
	{
		if (_conversationUI != null && _conversationUI.InConversation)
		{
			_conversationUI.CloseConversation();
			return;
		}

		if (_conversationPanel == null || !_conversationPanel.Visible)
			return;

		_conversationPanel.Close();
		_panels.OnPanelClosed(_conversationPanel);
	}

	private void CloseAllInGamePanels()
	{
		CloseChestPanel();
		CloseInventoryPanel();
		_statusPanelController.CloseAll();
		CloseSkillBarPanel();
		CloseSkillManagerPanel();
		CloseQuestPanel();
		CloseConversationPanel();
		CloseTradePanel();
		CloseDebugPanel();
		CloseLimbTargetPanel();
		HideGroundAndLogPanelsIfVisible();
	}

	private void HideGroundAndLogPanelsIfVisible()
	{
		if (_groundPanel?.PanelNode.Visible == true)
			_groundPanel.PanelNode.Visible = false;

		var logPanel = GetNodeOrNull<PanelContainer>($"{HudRootPath}/LogPanel");
		if (logPanel != null && logPanel.Visible)
			logPanel.Visible = false;
	}
}
