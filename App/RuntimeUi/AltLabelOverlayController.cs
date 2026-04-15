using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Module.Panel;
using MiniRPG.Module.Render;

namespace MiniRPG;

internal sealed class AltLabelOverlayController
{
	private readonly GameState _state;
	private readonly FogOfWarTracker _fogTracker;
	private readonly Func<IsometricVoxelRenderer?> _getMapRender;
	private readonly Func<GameSessionModule> _getSession;
	private readonly Func<MenuModule> _getMenu;
	private readonly Func<bool> _renderReady;
	private readonly Func<(int HalfW, int HalfH)> _getVisibleWorldHalfExtents;
	private readonly Func<Control> _getHudThemeSource;
	private readonly Node _overlayLayer;

	private bool _altLabelOverlayActive;
	private readonly List<(PanelContainer Root, RichTextLabel Text)> _altLabelPool = [];
	private int _altLabelCount;
	private Control? _altLabelRoot;

	public AltLabelOverlayController(
		GameState state,
		FogOfWarTracker fogTracker,
		Func<IsometricVoxelRenderer?> getMapRender,
		Func<GameSessionModule> getSession,
		Func<MenuModule> getMenu,
		Func<bool> renderReady,
		Func<(int HalfW, int HalfH)> getVisibleWorldHalfExtents,
		Func<Control> getHudThemeSource,
		Node overlayLayer)
	{
		_state = state;
		_fogTracker = fogTracker;
		_getMapRender = getMapRender;
		_getSession = getSession;
		_getMenu = getMenu;
		_renderReady = renderReady;
		_getVisibleWorldHalfExtents = getVisibleWorldHalfExtents;
		_getHudThemeSource = getHudThemeSource;
		_overlayLayer = overlayLayer;
	}

	public void Bind()
	{
		_overlayLayer.GetNodeOrNull<Control>("AltLabelOverlayRoot")?.QueueFree();
		var root = new Control
		{
			Name = "AltLabelOverlayRoot",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 75,
		};
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_overlayLayer.AddChild(root);
		_altLabelRoot = root;
	}

	public void Tick(RuntimeUiModeSnapshot snapshot)
	{
		if (_altLabelRoot == null)
			return;

		var altHeld = Input.IsKeyPressed(Key.Alt)
		              && snapshot.AllowGameplayInput
		              && !_getMenu().InMenu
		              && _getSession().GameStarted
		              && _renderReady();

		if (altHeld)
		{
			_altLabelOverlayActive = true;
			RefreshAltLabelOverlay();
		}
		else if (_altLabelOverlayActive)
		{
			_altLabelOverlayActive = false;
			HideAllAltLabels();
		}
	}

	private void RefreshAltLabelOverlay()
	{
		var mapRender = _getMapRender();
		if (mapRender == null || _state.World == null || _altLabelRoot == null)
		{
			HideAllAltLabels();
			return;
		}

		var cx = _state.PlayerX;
		var cy = _state.PlayerY;
		var cz = _state.PlayerZ;
		var (halfW, halfH) = _getVisibleWorldHalfExtents();

		_altLabelCount = 0;

		foreach (var actor in _state.Actors.Values)
		{
			if (actor.Z != cz)
				continue;
			if (Math.Abs(actor.X - cx) > halfW || Math.Abs(actor.Y - cy) > halfH)
				continue;
			var band = _fogTracker.GetVisionBand(actor.X, actor.Y, actor.Z);
			if (band is not (PlayerVisionBand.Focused or PlayerVisionBand.Peripheral))
				continue;
			if (!mapRender.TryGetWorldOverlayPosition(actor.X, actor.Y, actor.Z, out var pos))
				continue;

			var name = IdentificationModule.GetActorDisplayName(_state, actor);
			var color = actor.Faction == Factions.Hostile ? UIColors.HexCombat
				: actor.Faction == Factions.Player ? UIColors.HexSuccess
				: UIColors.HexNormal;
			AddAltLabel(pos, name, color);
		}

		for (var wy = cy - halfH; wy <= cy + halfH; wy++)
		for (var wx = cx - halfW; wx <= cx + halfW; wx++)
		{
			var band = _fogTracker.GetVisionBand(wx, wy, cz);
			if (band is not (PlayerVisionBand.Focused or PlayerVisionBand.Peripheral))
				continue;
			var items = _state.World.PeekGroundItems(wx, wy, cz);
			if (items.Count == 0)
				continue;
			if (!mapRender.TryGetWorldOverlayPosition(wx, wy, cz, out var pos))
				continue;

			var itemName = IdentificationModule.GetItemDisplayName(_state, items[0]);
			var label = items.Count > 1 ? $"{itemName} (+{items.Count - 1})" : itemName;
			AddAltLabel(pos, label, UIColors.HexUtility);
		}

		HideUnusedAltLabels();
	}

	private void AddAltLabel(Vector2 overlayPos, string text, string hexColor)
	{
		var idx = _altLabelCount++;
		if (idx >= _altLabelPool.Count)
		{
			var panel = new PanelContainer
			{
				Theme = _getHudThemeSource().Theme,
				ThemeTypeVariation = "TooltipPanel",
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			var rtl = new RichTextLabel
			{
				MouseFilter = Control.MouseFilterEnum.Ignore,
				BbcodeEnabled = true,
				FitContent = true,
				ScrollActive = false,
				AutowrapMode = TextServer.AutowrapMode.Off,
			};
			panel.AddChild(rtl);
			_altLabelRoot!.AddChild(panel);
			_altLabelPool.Add((panel, rtl));
		}

		var (root, label) = _altLabelPool[idx];
		label.Text = $"[center][color={hexColor}]{text}[/color][/center]";
		root.Visible = true;
		root.ResetSize();
		root.Position = new Vector2(
			overlayPos.X - root.Size.X / 2f,
			overlayPos.Y - root.Size.Y - 40f);
	}

	private void HideAllAltLabels()
	{
		for (var i = 0; i < _altLabelPool.Count; i++)
			_altLabelPool[i].Root.Visible = false;
		_altLabelCount = 0;
	}

	private void HideUnusedAltLabels()
	{
		for (var i = _altLabelCount; i < _altLabelPool.Count; i++)
			_altLabelPool[i].Root.Visible = false;
	}
}
