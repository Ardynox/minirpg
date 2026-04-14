using Godot;
using MiniRPG.Module.Editor;
using MiniRPG.Module.WorldTool;

namespace MiniRPG;

public partial class Main
{
	private (int HalfW, int HalfH) GetCurrentVisibleWorldHalfExtents()
	{
		if (_mapRender != null)
		{
			var window = _mapRender.GetVisibleWorldWindow();
			return (window.HalfX, window.HalfY);
		}

		return (ViewW / 2, ViewH / 2);
	}

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
		var runtimePreviewState = ResolveRuntimeWorldToolPreviewState();
		_runtimeViewCoordinator.FlushMap(
			_runtimeWorldToolSession.HoverWorld,
			runtimePreviewState,
			_skillTargetCursorActive ? _skillTargetWorldCell : null,
			MapEditorActive,
			_mapEditor.CameraX,
			_mapEditor.CameraY,
			_mapEditor.CameraZ,
			_runtimeWorldToolSession.CameraX,
			_runtimeWorldToolSession.CameraY,
			_runtimeWorldToolSession.CameraZ,
			_mapEditor.HoverWorld,
			_mapEditor.ResolveHoverState(_mapEditor.HoverWorld),
			SyncViewToActiveActor,
			MarkUIDirty);
	}

	/// <summary>标记所有常驻面板脏标记，下帧统一刷新。</summary>
	private void MarkUIDirty()
	{
		_skillBarDirty = true;
		_runtimeViewCoordinator.MarkUiDirty(
			_debugPanelController,
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
			_debugPanelController);
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

		var root = new PanelContainer
		{
			Name = "WorldHoverOverlay",
			Theme = GetNode<Control>(HudRootPath).Theme,
			ThemeTypeVariation = "TooltipPanel",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false,
			ZIndex = 80,
		};
		root.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		root.GrowHorizontal = Control.GrowDirection.End;
		root.GrowVertical = Control.GrowDirection.End;

		var margin = new MarginContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		margin.AddThemeConstantOverride("margin_left", 10);
		margin.AddThemeConstantOverride("margin_top", 6);
		margin.AddThemeConstantOverride("margin_right", 10);
		margin.AddThemeConstantOverride("margin_bottom", 6);

		var rtl = new RichTextLabel
		{
			Name = "CellInfo",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			BbcodeEnabled = true,
			FitContent = true,
			ScrollActive = false,
			AutowrapMode = TextServer.AutowrapMode.Off,
		};

		margin.AddChild(rtl);
		root.AddChild(margin);
		overlayLayer.AddChild(root);

		_worldHoverRtl = rtl;
		_worldHoverRoot = root;
	}

	private bool HandleWorldHoverInput(InputEvent @event, RuntimeUiModeSnapshot snapshot)
	{
		if (@event is not InputEventMouseMotion motion)
			return false;

		var isPrimaryDragPressed = RuntimeWorldToolInteractionLogic.IsPrimaryDragPressed(
			motion.ButtonMask,
			Input.IsMouseButtonPressed(MouseButton.Left));
		if (!RuntimeWorldToolInteractionLogic.ShouldKeepDragStrokeActive(
			_runtimeWorldToolDragActive,
			isPrimaryDragPressed))
		{
			_runtimeWorldToolDragActive = false;
		}

		if (_mapRender == null || !snapshot.AllowGameplayInput || _menu.InMenu)
		{
			_runtimeWorldToolDragActive = false;
			_runtimeWorldToolLastDraggedHoverCell = null;
			_runtimeWorldToolSession.SetHover(null);
			SetWorldHoverCell(null);
			RefreshRuntimeWorldToolBar(snapshot);
			return false;
		}

		if ((_runtimeWorldToolBar.Visible && _runtimeWorldToolBar.IsPointerOver(motion.GlobalPosition))
			|| (_runtimeWorldToolHeightPanel.Visible && _runtimeWorldToolHeightPanel.IsPointerOver(motion.GlobalPosition)))
		{
			_runtimeWorldToolLastDraggedHoverCell = null;
			_runtimeWorldToolSession.SetHover(null);
			RefreshRuntimeWorldHoverPresentation(null);
			SetWorldHoverCell(null);
			return false;
		}

		var reverseStackChanged = _runtimeWorldToolSession.SetReverseStack(motion.CtrlPressed);
		if (_mapRender.TryGetWorldCellFromGlobalPosition(motion.GlobalPosition, out var worldCell))
		{
			var hoverChanged = _runtimeWorldToolSession.SetHover(worldCell);
			RefreshRuntimeWorldHoverPresentation(worldCell, motion.GlobalPosition);
			if (_runtimeWorldToolDragActive
				&& SupportsRuntimeWorldToolDrag()
				&& isPrimaryDragPressed)
			{
				if (RuntimeWorldToolInteractionLogic.ShouldProcessDragHoverCell(
					worldCell,
					_runtimeWorldToolLastDraggedHoverCell))
				{
					TryApplyRuntimeWorldToolAtCell(worldCell);
					_runtimeWorldToolLastDraggedHoverCell = worldCell;
				}
			}
			if ((hoverChanged || reverseStackChanged) && _session.GameStarted && !_menu.InMenu && RenderReady)
				FlushMap();
			return false;
		}

		_runtimeWorldToolSession.SetHover(null);
		_runtimeWorldToolLastDraggedHoverCell = null;
		RefreshRuntimeWorldHoverPresentation(null);
		SetWorldHoverCell(null);
		return false;
	}

	private void SetWorldHoverCell(Vector3I? cell, bool flushMap = true)
	{
		if (_hoverWorldCell == cell)
			return;

		_hoverWorldCell = cell;
		_hoverDwell = 0f;
		if (_worldHoverRoot != null)
			_worldHoverRoot.Visible = false;
		RefreshWorldHoverOverlay();
		if (flushMap && _session.GameStarted && !_menu.InMenu && RenderReady)
			FlushMap();
	}

	private void SetWorldHoverCellFromMapEditor(Vector3I? cell) =>
		SetWorldHoverCell(cell, flushMap: false);

	private const float HoverShowDelay = 0.35f;
	private const float MapEditorSelectHoverShowDelay = 0.75f;
	private const float HoverFadeDuration = 0.18f;

	private void TickWorldHoverOverlay(float delta)
	{
		if (_worldHoverRoot == null)
			return;

		if (_hoverWorldCell == null)
		{
			_hoverDwell = 0f;
			_worldHoverRoot.Visible = false;
			return;
		}

		_hoverDwell += delta;
		var hoverShowDelay = ResolveWorldHoverShowDelay();

		if (_hoverDwell < hoverShowDelay)
		{
			_worldHoverRoot.Visible = false;
			return;
		}

		if (!_worldHoverRoot.Visible)
		{
			_worldHoverRoot.Visible = true;
			_worldHoverRoot.Modulate = new Color(1f, 1f, 1f, 0f);
		}

		var fadeProgress = Mathf.Clamp((_hoverDwell - hoverShowDelay) / HoverFadeDuration, 0f, 1f);
		_worldHoverRoot.Modulate = new Color(1f, 1f, 1f, fadeProgress);
	}

	private float ResolveWorldHoverShowDelay()
	{
		if (MapEditorActive &&
			_mapEditor.CurrentCategory is MapEditorBrushCategory.Terrain or MapEditorBrushCategory.Fixture &&
			_mapEditor.CurrentToolMode == MapEditorToolMode.Select)
		{
			return MapEditorSelectHoverShowDelay;
		}

		return HoverShowDelay;
	}

	private void RefreshWorldHoverOverlay()
	{
		if (_worldHoverRoot == null || _worldHoverRtl == null)
			return;

		if (_hoverWorldCell is not { } cell || _state.World == null)
		{
			_worldHoverRoot.Visible = false;
			return;
		}

		var terrain = _state.World.GetTerrain(cell.X, cell.Y, cell.Z);
		var terrainName = GameLocalizer.LocalizeTerrainName(terrain.StringId);
		var isWalkable = _state.World.IsWalkable(cell.X, cell.Y, cell.Z);
		var actors = ActorModule.GetAllAt(_state, cell.X, cell.Y, cell.Z);

		var header = $"[color={UIColors.HexHeader}]{terrainName}[/color]  " +
		             $"[color={UIColors.HexDim}]({cell.X}, {cell.Y}, {cell.Z})[/color]";

		var walkableLabel = LocalizationService.T("ui.world_hover.label.walkable");
		var walkableText = LocalizationService.T(
			isWalkable ? "ui.world_hover.walkable.yes" : "ui.world_hover.walkable.no");
		var walkableColor = isWalkable ? UIColors.HexSuccess : UIColors.HexWarning;

		var actorLabel = LocalizationService.T("ui.world_hover.label.actor");
		string actorValue;
		if (actors.Count > 0)
		{
			var names = actors.ConvertAll(a => IdentificationModule.GetActorDisplayName(_state, a));
			actorValue = $"[color={UIColors.HexNormal}]{string.Join(", ", names)}[/color]";
		}
		else
		{
			actorValue = $"[color={UIColors.HexDim}]{LocalizationService.T("ui.common.none")}[/color]";
		}

		var detail = $"[color={UIColors.HexDim}]{walkableLabel}:[/color] " +
		             $"[color={walkableColor}]{walkableText}[/color]  " +
		             $"[color={UIColors.HexDim}]{actorLabel}:[/color] {actorValue}";

		_worldHoverRtl.Text = $"{header}\n{detail}";
	}

	private void PositionWorldHoverOverlay(Vector2 mouseGlobal)
	{
		_hoverLastMousePos = mouseGlobal;
		if (_worldHoverRoot == null)
			return;

		const float offsetX = 16f;
		const float offsetY = 20f;

		var viewportSize = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920, 1080);
		var tooltipSize = _worldHoverRoot.Size;

		var x = mouseGlobal.X + offsetX;
		var y = mouseGlobal.Y + offsetY;

		if (x + tooltipSize.X > viewportSize.X - 8f)
			x = mouseGlobal.X - tooltipSize.X - 8f;
		if (y + tooltipSize.Y > viewportSize.Y - 8f)
			y = mouseGlobal.Y - tooltipSize.Y - 8f;

		x = Mathf.Max(8f, x);
		y = Mathf.Max(8f, y);

		_worldHoverRoot.Position = new Vector2(x, y);
	}
}
