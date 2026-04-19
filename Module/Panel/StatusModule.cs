using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using MiniRPG.Core.Data;

namespace MiniRPG.Module.Panel;

public enum StatusTab { Limb, Capacity, Tag, Buff, Equip, Needs, Health, Family, Genome }

public class StatusPanelModule : IPanel, ITooltipRegistrar
{
	private enum StatusContentMode
	{
		Actor,
		Corpse,
	}

	public string PanelId { get; }
	public PanelContainer PanelNode => _panel;
	public bool Visible { get => _panel.Visible; set => _panel.Visible = value; }
	bool IPanel.Visible { get => Visible; set => Visible = value; }

	public event Action? CloseRequested;

	bool IPanel.HandleCommand(string cmd)
	{
		switch (cmd)
		{
			case "up":
				MoveCursor(-1);
				return true;
			case "down":
				MoveCursor(1);
				return true;
			case "left" or "tab_prev":
				if (_contentMode == StatusContentMode.Actor)
				{
					CycleTab(-1);
					return true;
				}
				return false;
			case "right" or "tab_next":
				if (_contentMode == StatusContentMode.Actor)
				{
					CycleTab(1);
					return true;
				}
				return false;
			case "close":
				RequestClose();
				return true;
		}

		return false;
	}

	private static readonly StatusTab[] Tabs =
	[
		StatusTab.Limb, StatusTab.Capacity, StatusTab.Tag, StatusTab.Buff, StatusTab.Equip, StatusTab.Needs, StatusTab.Health,
		StatusTab.Family, StatusTab.Genome,
	];

	private readonly PanelContainer _panel;
	private readonly Label _nameInfo;
	private readonly HBoxContainer _tabBar;
	private readonly Label _hintBar;
	private readonly List<Button> _tabButtons;
	private readonly RichTextLabel _contentText;
	private readonly VBoxContainer _perItemContainer;
	private readonly List<string> _lines = [];

	private StatusTab _currentTab = StatusTab.Limb;
	private int _cursor;
	private GameState? _cachedState;
	private Actor? _cachedActor;
	private Item? _cachedCorpse;
	private Vector3I _cachedCorpseCell;
	private int _cachedFloor;
	private int _cachedTurn;
	private bool _cachedUsePlayerHeader = true;
	private StatusContentMode _contentMode = StatusContentMode.Actor;
	private RichTooltipLayer? _tooltipLayer;

	public void RegisterTooltips(RichTooltipLayer layer)
	{
		_tooltipLayer = layer;
		// 重新渲染让已存在的逐项 row 拿到 tooltip（_perItemContainer 模式下）
		if (_perItemContainer.Visible)
			RebuildPerItemRows();
	}

	public bool Dirty { get; set; }

	public StatusPanelModule(PanelContainer panel, string panelId = "status")
	{
		PanelId = panelId;
		_panel = panel;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_nameInfo = vbox.GetNode<Label>("HeaderBar/NameInfo");
		_tabBar = vbox.GetNode<HBoxContainer>("TabBar");
		_contentText = vbox.GetNode<RichTextLabel>("ContentText");
		_hintBar = vbox.GetNode<Label>("HintBar");

		_perItemContainer = new VBoxContainer
		{
			Name = "PerItemContainer",
			Visible = false,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		_perItemContainer.AddThemeConstantOverride("separation", 2);
		vbox.AddChild(_perItemContainer);
		// 紧跟 _contentText 之后，HintBar 之前
		vbox.MoveChild(_perItemContainer, _contentText.GetIndex() + 1);

		var tabLabels = new string[Tabs.Length];
		for (var i = 0; i < Tabs.Length; i++)
			tabLabels[i] = ActorStatusTextBuilder.GetTabLabel(Tabs[i]);

		_tabButtons = TabHelper.BuildTabButtons(_tabBar, tabLabels, Tabs, SetTab, panelId);
		UpdateTabHighlight();
	}

	public void FlushIfDirty()
	{
		if (!Dirty)
			return;

		Dirty = false;
		if (_cachedState == null)
			return;

		if (_contentMode == StatusContentMode.Corpse)
		{
			RefreshCorpse(_cachedState, _cachedCorpse, _cachedCorpseCell);
			return;
		}

		Refresh(_cachedState, _cachedActor, _cachedFloor, _cachedTurn, _cachedUsePlayerHeader);
	}

	public void SetTab(StatusTab tab)
	{
		if (_contentMode != StatusContentMode.Actor)
			return;

		_currentTab = tab;
		_cursor = 0;
		UpdateTabHighlight();
		BuildLines();
		RenderContent();
	}

	public void CycleTab(int dir)
	{
		if (_contentMode != StatusContentMode.Actor)
			return;

		var idx = Array.IndexOf(Tabs, _currentTab);
		idx = (idx + dir + Tabs.Length) % Tabs.Length;
		SetTab(Tabs[idx]);
	}

	public void MoveCursor(int delta)
	{
		if (_lines.Count == 0)
			return;

		var prev = _cursor;
		_cursor = Math.Clamp(_cursor + delta, 0, _lines.Count - 1);
		if (_cursor != prev)
			RenderContent();
	}

	public void Refresh(GameState state, Actor? actor, int floor, int turn = 0, bool usePlayerHeader = true)
	{
		_cachedState = state;
		_cachedActor = actor;
		_cachedFloor = floor;
		_cachedTurn = turn;
		_cachedUsePlayerHeader = usePlayerHeader;
		_cachedCorpse = null;
		_contentMode = StatusContentMode.Actor;
		Dirty = false;
		_tabBar.Visible = true;
		_hintBar.Visible = true;
		if (actor == null)
		{
			ClearContent();
			return;
		}

		_nameInfo.Text = usePlayerHeader
			? ActorStatusTextBuilder.BuildPlayerHeader(state, actor, floor, turn)
			: ActorStatusTextBuilder.BuildInspectHeader(state, actor);
		UpdateTabHighlight();
		BuildLines();
		RenderContent();
	}

	public void RefreshCorpse(GameState state, Item? corpse, Vector3I cell)
	{
		_cachedState = state;
		_cachedCorpse = corpse;
		_cachedCorpseCell = cell;
		_cachedActor = null;
		_contentMode = StatusContentMode.Corpse;
		Dirty = false;
		_tabBar.Visible = false;
		_hintBar.Visible = false;
		if (corpse == null)
		{
			ClearContent();
			return;
		}

		_nameInfo.Text = ActorStatusTextBuilder.BuildCorpseHeader(state, corpse, cell);
		BuildLines();
		RenderContent();
	}

	private void UpdateTabHighlight()
	{
		for (var i = 0; i < _tabButtons.Count && i < Tabs.Length; i++)
			_tabButtons[i].Text = ActorStatusTextBuilder.GetTabLabel(Tabs[i]);

		TabHelper.UpdateTabHighlight(_tabButtons, Tabs, _currentTab);
	}

	private void BuildLines()
	{
		_lines.Clear();
		if (_contentMode == StatusContentMode.Corpse)
		{
			if (_cachedState != null && _cachedCorpse != null)
				ActorStatusTextBuilder.BuildCorpseLines(_lines, _cachedState, _cachedCorpse);
			return;
		}

		if (_cachedActor == null)
			return;

		if (_cachedState == null)
		{
			ActorStatusTextBuilder.BuildLines(_lines, _currentTab, _cachedActor);
		}
		else
		{
			ActorStatusTextBuilder.BuildLines(_lines, _currentTab, _cachedState, _cachedActor);
		}

		if (_cursor >= _lines.Count)
			_cursor = Math.Max(0, _lines.Count - 1);
	}

	private void RenderContent()
	{
		// Buff / 装备 / 标签 三 Tab 走逐项 Control 模式以支持 per-item hover tooltip。
		// 其他 Tab（含尸体内容）保持原 RichTextLabel 整段渲染。
		if (_contentMode == StatusContentMode.Actor && IsPerItemTab(_currentTab))
		{
			_contentText.Visible = false;
			_perItemContainer.Visible = true;
			RebuildPerItemRows();
			return;
		}

		ClearPerItemRows();
		_perItemContainer.Visible = false;
		_contentText.Visible = true;

		_contentText.Clear();
		if (_lines.Count == 0)
			return;

		var sb = new StringBuilder();
		for (var i = 0; i < _lines.Count; i++)
		{
			if (i > 0)
				sb.Append('\n');

			if (i == _cursor)
				sb.Append($"[color={UIColors.HexSelected}]鈻?{_lines[i]}[/color]");
			else
				sb.Append($"  {_lines[i]}");
		}

		_contentText.AppendText(sb.ToString());
	}

	private static bool IsPerItemTab(StatusTab tab) =>
		tab == StatusTab.Buff || tab == StatusTab.Equip || tab == StatusTab.Tag;

	private void ClearPerItemRows()
	{
		foreach (var child in _perItemContainer.GetChildren())
			child.QueueFree();
	}

	private void RebuildPerItemRows()
	{
		ClearPerItemRows();
		if (_cachedActor == null)
			return;

		switch (_currentTab)
		{
			case StatusTab.Buff:
				RebuildBuffRows(_cachedActor);
				break;
			case StatusTab.Equip:
				RebuildEquipRows(_cachedActor, _cachedState);
				break;
			case StatusTab.Tag:
				RebuildTagRows(_cachedActor);
				break;
		}
	}

	private void RebuildBuffRows(Actor actor)
	{
		if (actor.Buffs.Count == 0)
		{
			AddRowLabel(LocalizationService.T("ui.common.none"), tooltipFactory: null);
			return;
		}

		foreach (var buff in actor.Buffs)
		{
			var capturedBuffId = buff.Id;
			var turns = buff.RemainingTurns < 0
				? LocalizationService.T("ui.status.buff.permanent")
				: LocalizationService.T("ui.status.buff.turns", ("value", buff.RemainingTurns));
			var rowText = $"{buff.Name} ({turns})";

			AddRowLabel(rowText, () => BuildBuffTooltipBbcode(actor, capturedBuffId));
		}
	}

	private void RebuildEquipRows(Actor actor, GameState? state)
	{
		var hasAny = false;
		foreach (var limb in actor.Limbs)
		{
			if (limb.EquipSlots.Count == 0)
				continue;

			var limbHeaderAdded = false;
			foreach (var slot in limb.EquipSlots)
			{
				if (slot.ItemId == null)
					continue;

				var item = actor.Inventory.Find(i => i.InstanceId == slot.ItemId);
				if (item == null)
					continue;

				if (!limbHeaderAdded)
				{
					AddRowLabel(
						LocalizationService.T("ui.status.equip.section", ("limb", limb.Name)),
						tooltipFactory: null,
						isHeader: true);
					limbHeaderAdded = true;
				}
				hasAny = true;

				var itemName = state == null
					? item.Name
					: IdentificationModule.GetItemDisplayName(state, item);
				var inlineStats = state == null
					? ItemFormatHelper.InlineStats(item)
					: ItemFormatHelper.InlineStats(state, item);
				var statStr = string.IsNullOrWhiteSpace(inlineStats) ? string.Empty : $" {inlineStats}";
				var rowText = $"  {itemName} ({GameLocalizer.LocalizeEquipLayer(slot.Layer)}){statStr}";
				var capturedInstanceId = slot.ItemId;
				AddRowLabel(rowText, () =>
				{
					var current = actor.Inventory.Find(i => i.InstanceId == capturedInstanceId);
					return current == null
						? string.Empty
						: ItemFormatHelper.BuildDetail(state, current);
				});
			}
		}

		if (!hasAny)
		{
			AddRowLabel(LocalizationService.T("ui.status.empty.equipment"), tooltipFactory: null);
			return;
		}

		var weight = LocalizationService.T(
			"ui.status.equip.weight",
			("current", actor.CarryWeight.ToString("F1")),
			("max", actor.MaxCarryWeight.ToString("F1")));
		if (actor.IsOverweight)
			weight += $" {LocalizationService.T("ui.status.equip.overweight")}";
		AddRowLabel(weight, tooltipFactory: null);
	}

	private void RebuildTagRows(Actor actor)
	{
		var tags = actor.ComputeTags();
		if (tags.Count == 0)
		{
			AddRowLabel(LocalizationService.T("ui.common.none"), tooltipFactory: null);
			return;
		}

		foreach (var (key, value) in tags)
		{
			var capturedKey = key;
			var rowText = $"{GameLocalizer.LocalizeTagKey(key)}: {value}";
			AddRowLabel(rowText, () => BuildTagTooltipBbcode(actor, capturedKey));
		}
	}

	private void AddRowLabel(string text, Func<string>? tooltipFactory, bool isHeader = false)
	{
		var label = new Label
		{
			Text = text,
			MouseFilter = tooltipFactory != null
				? Control.MouseFilterEnum.Stop
				: Control.MouseFilterEnum.Ignore,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		if (isHeader)
			label.AddThemeColorOverride("font_color", UIColors.TextHeader);

		_perItemContainer.AddChild(label);

		if (tooltipFactory != null && _tooltipLayer != null)
			_tooltipLayer.Attach(label, tooltipFactory);
	}

	private static string BuildBuffTooltipBbcode(Actor actor, string buffId)
	{
		var buff = actor.Buffs.Find(b => string.Equals(b.Id, buffId, StringComparison.Ordinal));
		if (buff == null)
			return string.Empty;

		var sb = new StringBuilder();
		sb.AppendLine($"[b]{buff.Name}[/b]");
		var turns = buff.RemainingTurns < 0
			? LocalizationService.T("ui.status.buff.permanent")
			: LocalizationService.T("ui.status.buff.turns", ("value", buff.RemainingTurns));
		sb.AppendLine($"[color=#aaaaaa]{turns}[/color]");
		if (buff.Tags.Count > 0)
		{
			sb.AppendLine();
			foreach (var (key, value) in buff.Tags)
			{
				var sign = value >= 0 ? "+" : string.Empty;
				sb.AppendLine($"  [color=#cccccc]{GameLocalizer.LocalizeTagKey(key)}[/color] {sign}{value}");
			}
		}
		return sb.ToString();
	}

	private static string BuildTagTooltipBbcode(Actor actor, string tagKey)
	{
		var tags = actor.ComputeTags();
		if (!tags.TryGetValue(tagKey, out var value))
			return string.Empty;

		var sb = new StringBuilder();
		sb.AppendLine($"[b]{GameLocalizer.LocalizeTagKey(tagKey)}[/b]");
		sb.AppendLine($"[color=#aaaaaa]value[/color] {value}");
		return sb.ToString();
	}

	private void ClearContent()
	{
		_nameInfo.Text = string.Empty;
		_contentText.Clear();
		ClearPerItemRows();
		_perItemContainer.Visible = false;
		_contentText.Visible = true;
		_lines.Clear();
	}

	private void RequestClose()
	{
		if (CloseRequested != null)
			CloseRequested.Invoke();
		else
			_panel.Visible = false;
	}
}
