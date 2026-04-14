using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Module;
using MiniRPG.Module.Panel;

namespace MiniRPG;

internal sealed class RuntimeStatusPanelController
{
	private const string StatusPanelScenePath = "res://Scene/StatusPanel.tscn";
	private static readonly Vector2 CascadeStep = new(36f, 28f);

	private readonly GameState _state;
	private readonly PanelContainer _templatePanel;
	private readonly Theme? _theme;
	private readonly HBoxContainer _topRow;
	private readonly PanelManager _panels;
	private readonly PanelLayoutService _panelLayouts;
	private readonly PanelDragService _panelDrag;
	private readonly PanelHoverChromeService _panelChrome;
	private readonly Dictionary<string, PanelEntry> _entries = new(StringComparer.Ordinal);

	public RuntimeStatusPanelController(
		GameState state,
		PanelContainer templatePanel,
		Theme? theme,
		HBoxContainer topRow,
		PanelManager panels,
		PanelLayoutService panelLayouts,
		PanelDragService panelDrag,
		PanelHoverChromeService panelChrome)
	{
		_state = state;
		_templatePanel = templatePanel;
		_theme = theme;
		_topRow = topRow;
		_panels = panels;
		_panelLayouts = panelLayouts;
		_panelDrag = panelDrag;
		_panelChrome = panelChrome;
	}

	public event Action? PanelsChanged;

	public bool HasFocusedPanel =>
		_panels.FocusedId is { } focusedId
		&& _entries.Values.Any(entry => string.Equals(entry.Module.PanelId, focusedId, StringComparison.Ordinal));

	public bool IsActiveActorPanelVisible
	{
		get
		{
			var actor = PartyModule.GetActiveActor(_state);
			return actor != null
				&& _entries.TryGetValue(BuildActorKey(actor.Id), out var entry)
				&& entry.Module.Visible;
		}
	}

	public void ToggleActiveActorPanel()
	{
		var actor = PartyModule.GetActiveActor(_state);
		if (actor == null)
			return;

		var key = BuildActorKey(actor.Id);
		if (_entries.ContainsKey(key))
		{
			CloseByKey(key);
			return;
		}

		OpenActor(actor);
	}

	public void OpenActor(Actor actor)
	{
		var key = BuildActorKey(actor.Id);
		var entry = EnsureEntry(key, BuildPanelId("actor", actor.Id));
		entry.TargetKind = RuntimeStatusTargetKind.Actor;
		entry.ActorId = actor.Id;
		entry.ItemInstanceId = null;
		entry.Cell = new Vector3I(actor.X, actor.Y, actor.Z);
		RefreshActorEntry(entry, actor);
		FocusEntry(entry);
	}

	public void OpenCorpse(Item corpse, Vector3I cell)
	{
		if (string.IsNullOrWhiteSpace(corpse.InstanceId))
			return;

		var key = BuildCorpseKey(corpse.InstanceId);
		var entry = EnsureEntry(key, BuildPanelId("corpse", corpse.InstanceId));
		entry.TargetKind = RuntimeStatusTargetKind.Corpse;
		entry.ActorId = null;
		entry.ItemInstanceId = corpse.InstanceId;
		entry.Cell = cell;
		RefreshCorpseEntry(entry, corpse);
		FocusEntry(entry);
	}

	public void OpenInspectTarget(LookInspectTarget target)
	{
		switch (target.Kind)
		{
			case LookInspectTargetKind.Actor when target.Actor != null:
				OpenActor(target.Actor);
				break;
			case LookInspectTargetKind.CorpseItem when target.Item != null:
				OpenCorpse(target.Item, target.Cell);
				break;
		}
	}

	public bool TryCycleFocusedPanelTab(int delta)
	{
		var entry = ResolveFocusedEntry() ?? ResolveActiveActorEntry();
		if (entry == null)
			return false;

		entry.Module.CycleTab(delta);
		return true;
	}

	public void MarkDirty()
	{
		foreach (var entry in _entries.Values)
			entry.Dirty = true;
	}

	public void FlushDirtyPanels()
	{
		foreach (var entry in _entries.Values.ToArray())
		{
			if (!entry.Module.Visible || !entry.Dirty)
				continue;

			RefreshEntry(entry);
		}
	}

	public void RefreshVisiblePanels()
	{
		foreach (var entry in _entries.Values.ToArray())
		{
			if (!entry.Module.Visible)
				continue;

			RefreshEntry(entry);
		}
	}

	public void CloseFocusedPanel()
	{
		var entry = ResolveFocusedEntry();
		if (entry != null)
			CloseByKey(entry.Key);
	}

	public void CloseAll()
	{
		foreach (var key in _entries.Keys.ToArray())
			CloseByKey(key);
	}

	private PanelEntry EnsureEntry(string key, string panelId)
	{
		if (_entries.TryGetValue(key, out var existing))
			return existing;

		var scene = ResAccess.Get<PackedScene>(StatusPanelScenePath)
			?? throw new InvalidOperationException($"Failed to load PackedScene: {StatusPanelScenePath}");
		var node = scene.Instantiate<PanelContainer>();
		node.Name = $"RuntimeStatusPanel_{SanitizeForNodeName(panelId)}";
		node.Theme = _theme;
		node.Visible = true;
		_topRow.AddChild(node);
		LocalizationService.LocalizeTree(node);

		var module = new StatusPanelModule(node, panelId);
		module.CloseRequested += () => CloseByKey(key);
		_panels.Register(module);
		_panelLayouts.RegisterPanel(panelId, node);
		_panelDrag.Register(new DraggablePanelRegistration(
			panelId,
			node,
			PanelDragAvailability.Always,
			[],
			DefaultFloating: true,
			PersistPosition: false));
		_panelChrome.Register(new PanelHoverChromeRegistration(
			panelId,
			node,
			[node.GetNode<Control>("MarginContainer/VBox/HeaderBar/NameInfo")],
			() => CloseByKey(key)));

		var entry = new PanelEntry(key, module)
		{
			Dirty = false,
		};
		_entries[key] = entry;
		node.GlobalPosition = ResolveDefaultPosition(_entries.Count - 1);
		NotifyPanelsChanged();
		return entry;
	}

	private void RefreshEntry(PanelEntry entry)
	{
		switch (entry.TargetKind)
		{
			case RuntimeStatusTargetKind.Actor when !string.IsNullOrWhiteSpace(entry.ActorId):
			{
				var actor = ActorModule.GetById(_state, entry.ActorId);
				if (actor == null)
				{
					CloseByKey(entry.Key);
					return;
				}

				RefreshActorEntry(entry, actor);
				return;
			}
			case RuntimeStatusTargetKind.Corpse when !string.IsNullOrWhiteSpace(entry.ItemInstanceId):
			{
				var corpse = ResolveCorpse(entry.Cell, entry.ItemInstanceId);
				if (corpse == null)
				{
					CloseByKey(entry.Key);
					return;
				}

				RefreshCorpseEntry(entry, corpse);
				return;
			}
		}
	}

	private void RefreshActorEntry(PanelEntry entry, Actor actor)
	{
		ActorDerivedStateUpdater.SyncInspectActor(_state, actor);
		entry.Cell = new Vector3I(actor.X, actor.Y, actor.Z);
		entry.Dirty = false;
		entry.Module.Refresh(
			_state,
			actor,
			actor.Z,
			_state.Turn,
			usePlayerHeader: PartyModule.IsPartyMember(_state, actor.Id));
	}

	private void RefreshCorpseEntry(PanelEntry entry, Item corpse)
	{
		entry.Dirty = false;
		entry.Module.RefreshCorpse(_state, corpse, entry.Cell);
	}

	private Item? ResolveCorpse(Vector3I cell, string instanceId)
	{
		if (_state.World == null)
			return null;

		return _state.World
			.PeekGroundItems(cell.X, cell.Y, cell.Z)
			.FirstOrDefault(item =>
				item.IsCorpse
				&& string.Equals(item.InstanceId, instanceId, StringComparison.Ordinal));
	}

	private void FocusEntry(PanelEntry entry)
	{
		entry.Module.Visible = true;
		BringToFront(entry.Module.PanelNode);
		_panels.PushFocus(entry.Module);
	}

	private void CloseByKey(string key)
	{
		if (!_entries.Remove(key, out var entry))
			return;

		_panelChrome.Unregister(entry.Module.PanelId);
		_panelDrag.Unregister(entry.Module.PanelId);
		_panelLayouts.UnregisterPanel(entry.Module.PanelId);
		_panels.Unregister(entry.Module);
		entry.Module.PanelNode.GetParent()?.RemoveChild(entry.Module.PanelNode);
		entry.Module.PanelNode.QueueFree();
		NotifyPanelsChanged();
	}

	private PanelEntry? ResolveFocusedEntry()
	{
		if (_panels.FocusedId is not { } focusedId)
			return null;

		return _entries.Values.FirstOrDefault(entry =>
			string.Equals(entry.Module.PanelId, focusedId, StringComparison.Ordinal));
	}

	private PanelEntry? ResolveActiveActorEntry()
	{
		var actor = PartyModule.GetActiveActor(_state);
		if (actor == null)
			return null;

		_entries.TryGetValue(BuildActorKey(actor.Id), out var entry);
		return entry;
	}

	private Vector2 ResolveDefaultPosition(int cascadeIndex) =>
		_templatePanel.GlobalPosition + CascadeStep * cascadeIndex;

	private static string BuildActorKey(string actorId) => $"actor:{actorId}";

	private static string BuildCorpseKey(string instanceId) => $"corpse:{instanceId}";

	private static string BuildPanelId(string prefix, string value) => $"runtime_status_{prefix}_{SanitizeForNodeName(value)}";

	private static string SanitizeForNodeName(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return "panel";

		var chars = value
			.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
			.ToArray();
		return new string(chars);
	}

	private static void BringToFront(Control panel)
	{
		if (panel.GetParent() is Control parent)
			parent.MoveChild(panel, parent.GetChildCount() - 1);
	}

	private void NotifyPanelsChanged() => PanelsChanged?.Invoke();

	private enum RuntimeStatusTargetKind
	{
		Actor,
		Corpse,
	}

	private sealed class PanelEntry(string key, StatusPanelModule module)
	{
		public string Key { get; } = key;
		public StatusPanelModule Module { get; } = module;
		public RuntimeStatusTargetKind TargetKind { get; set; }
		public string? ActorId { get; set; }
		public string? ItemInstanceId { get; set; }
		public Vector3I Cell { get; set; }
		public bool Dirty { get; set; }
	}
}
