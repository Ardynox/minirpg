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
	public GameState State { get; init; } = null!;
	public GameSessionModule? Session { get; set; }
	public LogModule? Log { get; set; }
	public InputModule? Input { get; set; }
	public IGameSessionBackend? SessionBackend { get; set; }
	public ILocalServerLauncher? LocalServerLauncher { get; set; }
	public FogOfWarTracker? FogTracker { get; set; }
	public MenuModule? Menu { get; set; }
	public PanelManager? Panels { get; set; }
}

internal sealed class RuntimeUiRefs
{
	public TileMapRenderModule? MapRender { get; set; }
	public WorldManagerModule? WorldManager { get; set; }
	public SettingsPanelModule? SettingsPanel { get; set; }
	public MultiplayerHubModule? MultiplayerHub { get; set; }
	public MultiplayerRoomPanelModule? MultiplayerRoomPanel { get; set; }
	public StatusPanelModule? StatusPanel { get; set; }
	public SkillBarModule? SkillBar { get; set; }
	public SkillManagerModule? SkillManager { get; set; }
	public InventoryPanelModule? Inventory { get; set; }
	public GroundPanelModule? Ground { get; set; }
	public TurnPanelModule? TurnPanel { get; set; }
	public ChestPanelModule? ChestPanel { get; set; }
	public DialogPanelModule? DialogPanel { get; set; }
	public TradePanelModule? TradePanel { get; set; }
	public QuestPanelModule? QuestPanel { get; set; }
	public ActorInspectPanelModule? ActorInspectPanel { get; set; }
	public LimbTargetPanelModule? LimbTargetPanel { get; set; }
}

internal sealed class RuntimeHooks
{
	public Action? Quit { get; set; }
	public Action? ShowMainMenuWithCurrentContinue { get; set; }
	public Action? SetInputHandled { get; set; }
}
