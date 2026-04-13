using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Module.Panel;
using MiniRPG.Module.Render;

namespace MiniRPG;

public partial class Main
{
	private bool _altLabelOverlayActive;
	private readonly List<(PanelContainer Root, RichTextLabel Text)> _altLabelPool = [];
	private int _altLabelCount;
	private Control? _altLabelRoot;

	private void BindAltLabelOverlay()
	{
		var overlayLayer = GetNode<CanvasLayer>(OverlayRootPath);
		overlayLayer.GetNodeOrNull<Control>("AltLabelOverlayRoot")?.QueueFree();
		var root = new Control
		{
			Name = "AltLabelOverlayRoot",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 75,
		};
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		overlayLayer.AddChild(root);
		_altLabelRoot = root;
	}

	private void TickAltLabelOverlay(RuntimeUiModeSnapshot snapshot)
	{
		if (_altLabelRoot == null)
			return;

		var altHeld = Input.IsKeyPressed(Key.Alt)
		              && snapshot.AllowGameplayInput
		              && !_menu.InMenu
		              && _session.GameStarted
		              && RenderReady;

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
		if (_mapRender == null || _state.World == null || _altLabelRoot == null)
		{
			HideAllAltLabels();
			return;
		}

		var cx = _state.PlayerX;
		var cy = _state.PlayerY;
		var cz = _state.PlayerZ;
		var (halfW, halfH) = GetCurrentVisibleWorldHalfExtents();

		_altLabelCount = 0;

		// Actors
		foreach (var actor in _state.Actors.Values)
		{
			if (actor.Z != cz)
				continue;
			if (Math.Abs(actor.X - cx) > halfW || Math.Abs(actor.Y - cy) > halfH)
				continue;
			var band = _fogTracker.GetVisionBand(actor.X, actor.Y, actor.Z);
			if (band is not (PlayerVisionBand.Focused or PlayerVisionBand.Peripheral))
				continue;
			if (!_mapRender.TryGetWorldOverlayPosition(actor.X, actor.Y, actor.Z, out var pos))
				continue;

			var name = IdentificationModule.GetActorDisplayName(_state, actor);
			var color = actor.Faction == Factions.Hostile ? UIColors.HexCombat
				: actor.Faction == Factions.Player ? UIColors.HexSuccess
				: UIColors.HexNormal;
			AddAltLabel(pos, name, color);
		}

		// Ground items
		for (var wy = cy - halfH; wy <= cy + halfH; wy++)
		for (var wx = cx - halfW; wx <= cx + halfW; wx++)
		{
			var band = _fogTracker.GetVisionBand(wx, wy, cz);
			if (band is not (PlayerVisionBand.Focused or PlayerVisionBand.Peripheral))
				continue;
			var items = _state.World.PeekGroundItems(wx, wy, cz);
			if (items.Count == 0)
				continue;
			if (!_mapRender.TryGetWorldOverlayPosition(wx, wy, cz, out var pos))
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
				Theme = GetNode<Control>(HudRootPath).Theme,
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
