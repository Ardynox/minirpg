using Godot;
using MiniRPG.Core.Combat;
using MiniRPG.Module.Editor;
using MiniRPG.Module.WorldTool;

namespace MiniRPG;

internal sealed class RuntimeViewCoordinator
{
	private readonly GameState _state;
	private readonly RuntimeUiRefs _ui;
	private readonly RuntimeServices _services;

	public RuntimeViewCoordinator(GameState state, RuntimeUiRefs ui, RuntimeServices services)
	{
		_state = state;
		_ui = ui;
		_services = services;
	}

	public void FlushMap(
		Vector3I? gameplayHoverWorldCell,
		WorldToolPreviewState? runtimeWorldToolPreviewState,
		Vector3I? targetCursorWorldCell,
		bool mapEditorActive,
		int mapEditorCameraX,
		int mapEditorCameraY,
		int mapEditorCameraZ,
		int runtimeCameraX,
		int runtimeCameraY,
		int runtimeCameraZ,
		Vector3I? mapEditorHoverWorld,
		MapEditorHoverState? mapEditorHoverState,
		Action syncViewToActiveActor,
		Action markUiDirty)
	{
		if (_ui.MapRender == null)
			return;

		syncViewToActiveActor();
		var editorPreviewState = MapEditorWorldToolPreviewAdapter.FromMapEditorHoverState(mapEditorHoverState);
		_ui.MapRender.TargetCursorWorldCell = targetCursorWorldCell;
		_ui.MapRender.HoverWorldCell = mapEditorActive ? mapEditorHoverWorld : gameplayHoverWorldCell;
		_ui.MapRender.EditorHoverState = mapEditorActive ? mapEditorHoverState : null;
		_ui.MapRender.WorldToolPreviewState = mapEditorActive ? editorPreviewState : runtimeWorldToolPreviewState;
		_ui.MapRender.SetEditorView(
			mapEditorActive,
			mapEditorCameraX,
			mapEditorCameraY,
			mapEditorCameraZ);
		_ui.MapRender.SetRuntimeView(
			!mapEditorActive,
			runtimeCameraX,
			runtimeCameraY,
			runtimeCameraZ);
		_ui.MapRender.Flush();
		markUiDirty();
	}

	public void MarkUiDirty(DebugPanelController debugPanelController, bool actorInspectVisible)
	{
		if (_ui.StatusPanel != null)
			_ui.StatusPanel.Dirty = true;
		if (_ui.SkillManager != null)
			_ui.SkillManager.Dirty = true;
		if (_ui.Inventory?.Visible == true)
			_ui.Inventory.Dirty = true;
		if (_ui.Ground != null)
			_ui.Ground.Dirty = true;
		if (_ui.TurnPanel != null)
			_ui.TurnPanel.Dirty = true;
		debugPanelController.MarkDirty();
		if (actorInspectVisible && _ui.ActorInspectPanel != null)
			_ui.ActorInspectPanel.Dirty = true;
	}

	public void ProcessDirtyPanels(
		ref bool skillBarDirty,
		bool playerDead,
		bool watchModeEnabled,
		bool isometricMode,
		Action refreshActorInspectPanel,
		Action refreshPanelLauncherState,
		DebugPanelController debugPanelController)
	{
		if (_ui.StatusPanel?.Dirty == true && _ui.StatusPanel.PanelNode.Visible)
		{
			var player = ActorModule.GetPlayer(_state);
			_ui.StatusPanel.Refresh(_state, player, _state.PlayerZ, _state.Turn);
		}

		if (_ui.TurnPanel?.Dirty == true && _ui.TurnPanel.PanelNode.Visible)
			_ui.TurnPanel.FlushIfDirty(_state, playerDead, watchModeEnabled, isometricMode);

		if (skillBarDirty && _ui.SkillBar?.Visible == true)
		{
			_ui.SkillBar.Refresh(ActorModule.GetPlayer(_state));
			skillBarDirty = false;
		}

		if (_ui.SkillManager?.Visible == true)
			_ui.SkillManager.FlushIfDirty();
		if (_ui.Inventory?.Visible == true && _ui.Inventory.Dirty)
			_ui.Inventory.FlushIfDirty();
		if (_ui.Ground?.Dirty == true)
			_ui.Ground.FlushIfDirty();

		debugPanelController.FlushIfDirty();

		if (_ui.ActorInspectPanel?.Visible == true && _ui.ActorInspectPanel.Dirty)
			refreshActorInspectPanel();

		refreshPanelLauncherState();
	}

	public void RefreshVisiblePanels(
		DebugPanelController debugPanelController,
		Action refreshActorInspectPanel,
		Action refreshWorldManagerContents,
		Action refreshMultiplayerRoomPanelState)
	{
		var player = ActorModule.GetPlayer(_state);

		if (_ui.StatusPanel?.PanelNode.Visible == true)
			_ui.StatusPanel.Refresh(_state, player, _state.PlayerZ, _state.Turn);
		if (_ui.SkillBar?.Visible == true)
			_ui.SkillBar.Refresh(player);
		if (_ui.SkillManager?.Visible == true)
			_ui.SkillManager.State = _state;
		if (_ui.SkillManager?.Visible == true)
			_ui.SkillManager.Refresh();
		if (_ui.Inventory?.Visible == true)
			_ui.Inventory.Refresh();
		if (_ui.Ground?.Visible == true)
			_ui.Ground.Refresh();
		if (_ui.ChestPanel?.Visible == true)
			_ui.ChestPanel.Refresh();
		if (_ui.QuestPanel?.Visible == true)
			_ui.QuestPanel.Refresh();
		debugPanelController.MarkDirty();
		debugPanelController.FlushIfDirty();
		if (_ui.ActorInspectPanel?.Visible == true)
			refreshActorInspectPanel();

		if (_services.Session != null && _services.Session.GameStarted)
		{
			if (_ui.WorldManager?.Visible == true)
				refreshWorldManagerContents();
			if (_ui.MultiplayerRoomPanel?.Visible == true)
				refreshMultiplayerRoomPanelState();
		}
	}
}
