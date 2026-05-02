using System;
using MiniRPG.Module;
using MiniRPG.Module.Panel;
using MiniRPG.Module.Render;
using MiniRPG.Module.Network;
using MiniRPG.Core.Multiplayer;

namespace MiniRPG;

internal sealed class MainRuntimeComposition
{
	public RuntimeServices Services { get; }
	public RuntimeUiRefs UiRefs { get; }
	public RuntimeHooks Hooks { get; }

	private MainRuntimeComposition(RuntimeServices services, RuntimeUiRefs uiRefs, RuntimeHooks hooks)
	{
		Services = services;
		UiRefs = uiRefs;
		Hooks = hooks;
	}

	public static MainRuntimeComposition Create(RuntimeServices services, RuntimeUiRefs uiRefs, RuntimeHooks hooks)
		=> new(services, uiRefs, hooks);
}

internal sealed class RuntimeServices
{
	public required GameState State { get; init; }
	public required GameSessionModule Session { get; init; }
	public required LogModule Log { get; init; }
	public required InputModule Input { get; init; }
	public required IGameSessionBackend SessionBackend { get; init; }
	public ILocalServerLauncher? LocalServerLauncher { get; init; }
	public required FogOfWarTracker FogTracker { get; init; }
	public required MenuModule Menu { get; init; }
	public required PanelManager Panels { get; init; }
}

internal sealed class RuntimeUiRefs
{
	public IsometricVoxelRenderer? MapRender { get; set; }
	public required WorldManagerModule WorldManager { get; init; }
	public required SettingsPanelModule SettingsPanel { get; init; }
	public MultiplayerHubModule? MultiplayerHub { get; init; }
	public MultiplayerRoomPanelModule? MultiplayerRoomPanel { get; init; }
	public StatusPanelModule? StatusPanel { get; init; }
	public required SkillBarModule SkillBar { get; init; }
	public required SkillManagerModule SkillManager { get; init; }
	public InventoryGridPanelModule? Inventory { get; init; }
	public required GroundPanelModule Ground { get; init; }
	public required TurnPanelModule TurnPanel { get; init; }
	public ChestPanelModule? ChestPanel { get; init; }
	public ConversationPanelModule? ConversationPanel { get; init; }
	public TradePanelModule? TradePanel { get; init; }
	public QuestPanelModule? QuestPanel { get; init; }
	public ActorInspectPanelModule? ActorInspectPanel { get; init; }
	public LimbTargetPanelModule? LimbTargetPanel { get; init; }
}

internal sealed class RuntimeHooks
{
	public required Action Quit { get; init; }
	public required Action ShowMainMenuWithCurrentContinue { get; init; }
	public required Action SetInputHandled { get; init; }
}
