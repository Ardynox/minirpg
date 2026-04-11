using Godot;

namespace MiniRPG;

public partial class Main
{
	private static Control CreateMapOverlayRoot(Control mapPanel)
	{
		mapPanel.GetNodeOrNull<Control>("CombatFxTextRoot")?.QueueFree();
		var root = new Control
		{
			Name = "CombatFxTextRoot",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 40,
		};
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		mapPanel.AddChild(root);
		return root;
	}

	private Node2D CreatePlayerCharacterNode()
	{
		_playerCharacterVisual = new FantasyCharacterAnimatable();
		_playerCharacterVisual.Name = "PlayerCharacter";
		RefreshPlayerCharacterVisual();
		return _playerCharacterVisual;
	}

	private void RefreshPlayerCharacterVisual()
	{
		if (_playerCharacterVisual == null)
			return;

		var appearance = PlayerAppearanceCatalog.GetOrDefault(_state.PlayerAppearanceId);
		_playerCharacterVisual.Configure(appearance.SheetDir, appearance.DefaultAnim);
		_playerCharacterVisual.Visible = false;
		_playerCharacterVisual.SetMovementDirection(1, 0);
		ResAccess.RegisterAnimatable(Factions.Player, _playerCharacterVisual);
		if (!string.IsNullOrWhiteSpace(_state.PlayerId))
			ResAccess.RegisterAnimatable(_state.PlayerId, _playerCharacterVisual);
	}

	private void PlayCombatFx(GameEvent e)
	{
		if (_combatFxPlayer == null || _mapRender == null)
			return;

		var sourceVisible = _mapRender.IsWorldCellVisible(e.SourceX, e.SourceY, _state.PlayerZ);
		var targetVisible = _mapRender.IsWorldCellVisible(e.TargetX, e.TargetY, _state.PlayerZ);
		var commands = _combatFxRegistry.Resolve(e, sourceVisible, targetVisible);
		if (commands.Count == 0)
			return;

		_combatFxPlayer.Play(commands, _state.PlayerZ);
	}

	private void PlayWeatherLightningFx(GameEvent e)
	{
		if (_combatFxPlayer == null || _mapRender == null)
			return;

		if (!_mapRender.IsWorldCellVisible(e.TargetX, e.TargetY, _state.PlayerZ))
			return;

		_combatFxPlayer.Play(
		[
			new CombatFxCommand
			{
				Kind = CombatFxCommandKind.Sprite,
				Anchor = CombatFxAnchor.Target,
				ResourceId = "fx_lightning_strike",
				WorldX = e.TargetX,
				WorldY = e.TargetY,
				TargetWorldX = e.TargetX,
				TargetWorldY = e.TargetY,
				DurationSeconds = 0.28f,
				Scale = 1.25f,
				Layer = 7,
			},
			new CombatFxCommand
			{
				Kind = CombatFxCommandKind.Text,
				Anchor = CombatFxAnchor.Target,
				Text = e.Damage > 0 ? e.Damage.ToString() : "!",
				WorldX = e.TargetX,
				WorldY = e.TargetY,
				DurationSeconds = 0.55f,
				Tint = Colors.LightYellow,
				Layer = 8,
				RisePixels = 76f,
			},
		], _state.PlayerZ);
	}

	private void ToggleRender()
	{
		if (_mapRender == null)
			return;

		var msg = _mapRender.ToggleRenderMode();
		if (_session.GameStarted && !_menu.InMenu && msg != null)
			_log.Add(msg);
		if (!_menu.InMenu) FlushMap();
	}

	private void CycleLightingProfile()
	{
		if (_mapRender == null)
			return;

		var msg = _mapRender.CycleIsometricLightingProfile();
		if (_session.GameStarted && !_menu.InMenu)
			_log.Add(msg);
		if (!_menu.InMenu)
			FlushMap();
	}

	/// <summary>
	/// 将 PlayerX/Y/Z 同步到当前激活角色的位置。
	/// 这样所有依赖 PlayerX/Y/Z 的渲染和 UI 面板自动跟随激活角色。
	/// </summary>
	private void SyncViewToActiveActor()
	{
		var active = PartyModule.GetActiveActor(_state);
		if (active == null) return;
		_state.PlayerX = active.X;
		_state.PlayerY = active.Y;
		_state.PlayerZ = active.Z;
	}

	/// <summary>立即刷新地图，标记 UI 面板为脏（由 _Process 统一驱动刷新）。</summary>
	private void FlushMap()
	{
		_runtimeViewCoordinator.FlushMap(
			_inspectModeActive,
			_inspectWorldCell,
			_hoverWorldCell,
			MapEditorActive,
			_mapEditor.CameraX,
			_mapEditor.CameraY,
			_mapEditor.HoverWorld,
			SyncViewToActiveActor,
			MarkUIDirty);
	}

	/// <summary>标记所有常驻面板脏标记，下帧统一刷新。</summary>
	private void MarkUIDirty()
	{
		_skillBarDirty = true;
		_runtimeViewCoordinator.MarkUiDirty(
			_debugPanelController,
			_weatherLabPanelController,
			_actorInspectPanel?.Visible == true);
	}

	/// <summary>在 _Process 中统一驱动脏面板刷新，避免单帧重复刷新。</summary>
	private void ProcessDirtyPanels()
	{
		_runtimeViewCoordinator.ProcessDirtyPanels(
			ref _skillBarDirty,
			PlayerDead,
			_watchModeEnabled,
			_mapRender?.IsIsometricMode ?? true,
			RefreshActorInspectPanel,
			RefreshPanelLauncherState,
			_debugPanelController,
			_weatherLabPanelController);
	}

	private void ToggleMinimap()
	{
		if (_mapRender == null)
			return;

		var msg = _mapRender.ToggleMinimap();
		if (msg.Length > 0) _log.Add(msg);
		FlushMap();
	}

	private void ToggleFogMap()
	{
		if (_mapRender == null)
			return;

		_log.Add(_mapRender.ToggleFogMap());
		FlushMap();
	}

	private void CenterFogMap()
	{
		if (_mapRender == null)
			return;

		_mapRender.CenterFogMap();
		FlushMap();
	}

	private void RefreshAllBorders() => _panels.RefreshBorders();

	private void BindWorldHoverOverlay()
	{
		var overlayLayer = GetNode<CanvasLayer>(OverlayRootPath);
		overlayLayer.GetNodeOrNull<Control>("WorldHoverOverlay")?.QueueFree();
		var root = new MarginContainer
		{
			Name = "WorldHoverOverlay",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false,
			ZIndex = 80,
		};
		root.SetAnchorsPreset(Control.LayoutPreset.TopWide);
		root.OffsetLeft = 12f;
		root.OffsetTop = 64f;
		root.OffsetRight = -12f;
		root.OffsetBottom = 0f;
		root.AddThemeConstantOverride("margin_left", 8);
		root.AddThemeConstantOverride("margin_top", 4);
		root.AddThemeConstantOverride("margin_right", 8);
		root.AddThemeConstantOverride("margin_bottom", 4);

		var panel = new PanelContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = true,
		};
		var label = new Label
		{
			Name = "CellInfo",
			Visible = true,
			AutowrapMode = TextServer.AutowrapMode.Off,
			HorizontalAlignment = HorizontalAlignment.Left,
		};
		panel.AddChild(label);
		root.AddChild(panel);
		overlayLayer.AddChild(root);
		_worldHoverLabel = label;
	}

	private bool HandleWorldHoverInput(InputEvent @event, RuntimeUiModeSnapshot snapshot)
	{
		if (@event is not InputEventMouseMotion motion)
			return false;

		if (_mapRender == null || !snapshot.AllowGameplayInput || _menu.InMenu)
		{
			SetWorldHoverCell(null);
			return false;
		}

		if (_mapRender.TryGetWorldCellFromGlobalPosition(motion.GlobalPosition, out var worldCell))
		{
			SetWorldHoverCell(worldCell);
			return false;
		}

		SetWorldHoverCell(null);
		return false;
	}

	private void SetWorldHoverCell(Vector3I? cell)
	{
		if (_hoverWorldCell == cell)
			return;

		_hoverWorldCell = cell;
		RefreshWorldHoverOverlay();
		if (_session.GameStarted && !_menu.InMenu && RenderReady)
			FlushMap();
	}

	private void RefreshWorldHoverOverlay()
	{
		var overlay = GetNodeOrNull<Control>($"{OverlayRootPath}/WorldHoverOverlay");
		if (overlay == null || _worldHoverLabel == null)
			return;

		if (_hoverWorldCell is not { } cell || _state.World == null)
		{
			overlay.Visible = false;
			return;
		}

		var terrain = _state.World.GetTerrain(cell.X, cell.Y, cell.Z);
		var actor = ActorModule.GetAt(_state, cell.X, cell.Y, cell.Z);
		var actorText = actor != null
			? IdentificationModule.GetActorDisplayName(_state, actor)
			: LocalizationService.T("ui.common.none");
		var walkable = LocalizationService.T(
			_state.World.IsWalkable(cell.X, cell.Y, cell.Z)
				? "ui.world_hover.walkable.yes"
				: "ui.world_hover.walkable.no");
		_worldHoverLabel.Text = LocalizationService.T(
			"ui.world_hover.cell",
			("x", cell.X),
			("y", cell.Y),
			("z", cell.Z),
			("terrain", GameLocalizer.LocalizeTerrainName(terrain.StringId)),
			("walkable", walkable),
			("actor", actorText));
		overlay.Visible = true;
	}
}
