using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using MiniRPG.Core;

namespace MiniRPG.Module;

public enum InvSortMode { Name, Weight, Price }

public class InventoryPanelModule
{
	public interface IHost
	{
		GameState State { get; }
		bool HasFocus { get; }
		void AddLog(string msg);
		void Dispatch(List<GameEvent> events);
		void FlushMap();
		void OpenChestFromInventory(Item chestItem);
	}

	private static readonly string[] FilterIds =
	[
		"all",
		ItemCategories.Weapon, ItemCategories.Armor, ItemCategories.Clothing,
		ItemCategories.Consumable, ItemCategories.Tool, ItemCategories.Material,
		ItemCategories.Food, ItemCategories.Misc,
	];

	private static readonly string[] FilterLabels =
		["全部", "武器", "护甲", "衣物", "消耗", "工具", "材料", "食物", "杂项"];

	private readonly PanelContainer _panel;
	private readonly RichTextLabel _header;
	private readonly HBoxContainer _filterBar;
	private readonly ScrollContainer _itemScroll;
	private readonly VBoxContainer _itemList;
	private readonly RichTextLabel _detailBox;
	private readonly RichTextLabel _hintBar;
	private readonly HBoxContainer _actionBar;
	private readonly Button _equipBtn;
	private readonly Button _useBtn;
	private readonly Button _dropBtn;
	private readonly Button _sortBtn;
	private readonly PopupMenu _contextMenu;
	private readonly IHost _host;

	private readonly List<Button> _filterButtons = [];
	private readonly List<Button> _itemRows = [];

	private int _filterIndex;
	private InvSortMode _sortMode = InvSortMode.Name;
	private int _cursor;
	private int _hoverIndex = -1;
	private List<(int InvIndex, Item Item)> _displayItems = [];
	private ulong _lastClickTime;
	private int _lastClickIndex = -1;
	private const ulong DoubleClickMs = 400;

	private static readonly Color ColorNormal = new(0.8f, 0.8f, 0.8f);
	private static readonly Color ColorDim = new(0.5f, 0.5f, 0.5f);
	private static readonly Color ColorSelected = new(1f, 1f, 1f);
	private static readonly Color ColorEquipped = new(1f, 0.8f, 0f);
	private static readonly Color ColorRowBg = new(0, 0, 0, 0);
	private static readonly Color ColorHoverBg = new(0.25f, 0.28f, 0.35f);
	private static readonly Color ColorSelectedBg = new(0.18f, 0.3f, 0.25f);
	private static readonly Color ColorFocusBorder = new(0.3f, 0.65f, 0.4f);

	public bool Visible
	{
		get => _panel.Visible;
		set
		{
			_panel.Visible = value;
			if (value) Refresh();
		}
	}

	public InventoryPanelModule(PanelContainer panel, IHost host)
	{
		_panel = panel;
		_host = host;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_header = vbox.GetNode<RichTextLabel>("Header");
		_filterBar = vbox.GetNode<HBoxContainer>("FilterBar");
		_itemScroll = vbox.GetNode<ScrollContainer>("ItemScroll");
		_itemList = _itemScroll.GetNode<VBoxContainer>("ItemList");
		_detailBox = vbox.GetNode<RichTextLabel>("DetailBox");
		_hintBar = vbox.GetNode<RichTextLabel>("HintBar");
		_actionBar = vbox.GetNode<HBoxContainer>("ActionBar");
		_equipBtn = _actionBar.GetNode<Button>("EquipBtn");
		_useBtn = _actionBar.GetNode<Button>("UseBtn");
		_dropBtn = _actionBar.GetNode<Button>("DropBtn");
		_sortBtn = _actionBar.GetNode<Button>("SortBtn");
		_contextMenu = panel.GetNode<PopupMenu>("ContextMenu");

		BuildFilterButtons();
		WireActionButtons();
		_contextMenu.IdPressed += OnContextMenuAction;

		_hintBar.Clear();
		_hintBar.AppendText("[color=#666666]↑↓选择 ←→分类 E装备 U使用 Q丢弃 R排序 I/Esc关闭[/color]");
	}

	private void BuildFilterButtons()
	{
		for (var i = 0; i < FilterLabels.Length; i++)
		{
			var btn = new Button
			{
				Text = FilterLabels[i],
				ToggleMode = true,
				SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
				FocusMode = Control.FocusModeEnum.None,
			};
			var idx = i;
			btn.Pressed += () => SetFilter(idx);
			_filterBar.AddChild(btn);
			_filterButtons.Add(btn);
		}
		UpdateFilterHighlight();
	}

	private void WireActionButtons()
	{
		_equipBtn.FocusMode = Control.FocusModeEnum.None;
		_useBtn.FocusMode = Control.FocusModeEnum.None;
		_dropBtn.FocusMode = Control.FocusModeEnum.None;
		_sortBtn.FocusMode = Control.FocusModeEnum.None;

		_equipBtn.Pressed += () => TryEquip();
		_useBtn.Pressed += () => TryUse();
		_dropBtn.Pressed += () => TryDrop();
		_sortBtn.Pressed += () => CycleSort();
	}

	private void OnContextMenuAction(long id)
	{
		switch (id)
		{
			case 0: TryEquip(); break;
			case 1: TryUse(); break;
			case 2: TryDrop(); break;
			case 3: TryOpenContainer(); break;
		}
	}

	// ── Public API ───────────────────────────────────────

	public void Refresh()
	{
		var player = ActorModule.GetPlayer(_host.State);
		if (player == null) return;
		RebuildList(player);
		RebuildItemNodes();
		RenderHeader(player);
		UpdateFilterHighlight();
		UpdateRowVisuals();
		RenderDetail();
		UpdateActionButtons();
	}

	public void MoveCursor(int delta)
	{
		if (_displayItems.Count == 0) return;
		_cursor = Math.Clamp(_cursor + delta, 0, _displayItems.Count - 1);
		UpdateRowVisuals();
		RenderDetail();
		UpdateActionButtons();
		EnsureCursorVisible();
	}

	public void SetFilter(int index)
	{
		_filterIndex = index;
		_cursor = 0;
		Refresh();
	}

	public void CycleFilter(int dir)
	{
		_filterIndex = (_filterIndex + dir + FilterIds.Length) % FilterIds.Length;
		_cursor = 0;
		Refresh();
	}

	public void CycleSort()
	{
		_sortMode = _sortMode switch
		{
			InvSortMode.Name => InvSortMode.Weight,
			InvSortMode.Weight => InvSortMode.Price,
			_ => InvSortMode.Name,
		};
		_cursor = 0;
		Refresh();
	}

	public bool TryEquip()
	{
		var player = ActorModule.GetPlayer(_host.State);
		if (player == null || _cursor < 0 || _cursor >= _displayItems.Count) return false;
		var (invIdx, _) = _displayItems[_cursor];
		var result = InventoryModule.ToggleEquip(player, invIdx);
		_host.AddLog(result.Message);
		Refresh();
		_host.FlushMap();
		return true;
	}

	public bool TryUse()
	{
		var player = ActorModule.GetPlayer(_host.State);
		if (player == null || _cursor < 0 || _cursor >= _displayItems.Count) return false;
		var (invIdx, item) = _displayItems[_cursor];
		if (!item.Tags.ContainsKey("治疗"))
		{
			_host.AddLog($"{item.Name} 不是消耗品");
			return false;
		}
		var result = InventoryModule.Use(player, invIdx);
		_host.AddLog(result.Message);
		Refresh();
		_host.FlushMap();
		return true;
	}

	public bool TryOpenContainer()
	{
		if (_cursor < 0 || _cursor >= _displayItems.Count) return false;
		var (_, item) = _displayItems[_cursor];
		if (!item.IsContainer) return false;
		_host.OpenChestFromInventory(item);
		return true;
	}

	public bool TryDrop()
	{
		var player = ActorModule.GetPlayer(_host.State);
		if (player == null || _cursor < 0 || _cursor >= _displayItems.Count) return false;
		var (invIdx, _) = _displayItems[_cursor];
		var events = InteractionModule.DropItem(_host.State, player, invIdx);
		_host.Dispatch(events);
		Refresh();
		_host.FlushMap();
		return true;
	}

	// ── Data ─────────────────────────────────────────────

	private void RebuildList(Actor player)
	{
		var all = InventoryModule.List(player);
		var filterId = FilterIds[_filterIndex];

		_displayItems = filterId == "all"
			? all
			: all.Where(x => x.Item.Category == filterId).ToList();

		_displayItems = _sortMode switch
		{
			InvSortMode.Weight => [.. _displayItems.OrderByDescending(x => x.Item.Equipped)
				.ThenByDescending(x => x.Item.EffectiveWeight)],
			InvSortMode.Price => [.. _displayItems.OrderByDescending(x => x.Item.Equipped)
				.ThenByDescending(x => x.Item.Price)],
			_ => [.. _displayItems.OrderByDescending(x => x.Item.Equipped)
				.ThenBy(x => x.Item.Name)],
		};

		if (_cursor >= _displayItems.Count)
			_cursor = Math.Max(0, _displayItems.Count - 1);
	}

	// ── Row nodes (Button-based) ─────────────────────────

	private void RebuildItemNodes()
	{
		foreach (var old in _itemRows)
			old.QueueFree();
		_itemRows.Clear();

		for (var i = 0; i < _displayItems.Count; i++)
		{
			var row = CreateItemRow(i, _displayItems[i].Item);
			_itemList.AddChild(row);
			_itemRows.Add(row);
		}

		if (_displayItems.Count == 0)
		{
			var empty = new Button
			{
				Text = "  (空)",
				Flat = true,
				FocusMode = Control.FocusModeEnum.None,
				Disabled = true,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			empty.AddThemeColorOverride("font_disabled_color", ColorDim);
			_itemList.AddChild(empty);
			_itemRows.Add(empty);
		}
	}

	private Button CreateItemRow(int index, Item item)
	{
		var eqTag = item.Equipped ? "[E] " : "    ";
		var stats = FormatInlineStats(item);
		var weight = $" {item.EffectiveWeight:F1}kg";
		var text = $"{eqTag}{item.Name}  {stats}{weight}";

		var row = new Button
		{
			Text = text,
			Flat = true,
			FocusMode = Control.FocusModeEnum.None,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 26),
			Alignment = HorizontalAlignment.Left,
			ClipText = true,
		};

		ApplyRowStyle(row, false, false);

		var idx = index;
		row.GuiInput += ev => OnRowInput(ev, idx);
		row.MouseEntered += () => OnRowHover(idx);
		row.MouseExited += () => OnRowHoverExit(idx);

		return row;
	}

	private static void ApplyRowStyle(Button row, bool selected, bool hovered)
	{
		Color bg;
		if (selected) bg = ColorSelectedBg;
		else if (hovered) bg = ColorHoverBg;
		else bg = ColorRowBg;

		var sb = new StyleBoxFlat
		{
			BgColor = bg,
			ContentMarginLeft = 4,
			ContentMarginRight = 4,
		};

		if (selected)
		{
			sb.BorderWidthLeft = 3;
			sb.BorderColor = ColorFocusBorder;
			sb.ContentMarginLeft = 6;
		}

		row.AddThemeStyleboxOverride("normal", sb);
		row.AddThemeStyleboxOverride("hover", sb);
		row.AddThemeStyleboxOverride("pressed", sb);
		row.AddThemeColorOverride("font_color", selected ? ColorSelected : ColorNormal);
		row.AddThemeColorOverride("font_hover_color", selected ? ColorSelected : ColorNormal);
	}

	// ── Mouse events ─────────────────────────────────────

	private void OnRowInput(InputEvent ev, int index)
	{
		if (ev is not InputEventMouseButton mb || !mb.Pressed) return;

		if (mb.ButtonIndex == MouseButton.Left)
		{
			var now = Time.GetTicksMsec();
			if (index == _lastClickIndex && now - _lastClickTime < DoubleClickMs)
			{
				_cursor = index;
				var (_, dblItem) = _displayItems[_cursor];
				if (dblItem.IsContainer)
					_host.OpenChestFromInventory(dblItem);
				else
					TryEquip();
				_lastClickIndex = -1;
			}
			else
			{
				_cursor = index;
				UpdateRowVisuals();
				RenderDetail();
				UpdateActionButtons();
				_lastClickIndex = index;
				_lastClickTime = now;
			}
		}
		else if (mb.ButtonIndex == MouseButton.Right)
		{
			_cursor = index;
			UpdateRowVisuals();
			RenderDetail();
			UpdateActionButtons();
			ShowContextMenu(mb.GlobalPosition);
		}
	}

	private void OnRowHover(int index)
	{
		_hoverIndex = index;
		UpdateRowVisuals();
	}

	private void OnRowHoverExit(int index)
	{
		if (_hoverIndex == index) _hoverIndex = -1;
		UpdateRowVisuals();
	}

	private void ShowContextMenu(Vector2 pos)
	{
		if (_cursor < 0 || _cursor >= _displayItems.Count) return;
		var (_, item) = _displayItems[_cursor];

		_contextMenu.Clear();
		if (item.IsContainer)
			_contextMenu.AddItem("打开", 3);
		_contextMenu.AddItem(item.Equipped ? "卸下" : "装备", 0);
		if (item.Tags.ContainsKey("治疗"))
			_contextMenu.AddItem("使用", 1);
		_contextMenu.AddItem("丢弃", 2);

		_contextMenu.Position = new Vector2I((int)pos.X, (int)pos.Y);
		_contextMenu.ResetSize();
		_contextMenu.Popup();
	}

	// ── Visuals ──────────────────────────────────────────

	private void UpdateRowVisuals()
	{
		for (var i = 0; i < _itemRows.Count && i < _displayItems.Count; i++)
			ApplyRowStyle(_itemRows[i], i == _cursor, i == _hoverIndex);
	}

	private void UpdateFilterHighlight()
	{
		for (var i = 0; i < _filterButtons.Count; i++)
			_filterButtons[i].ButtonPressed = i == _filterIndex;
	}

	private void UpdateActionButtons()
	{
		var hasItem = _cursor >= 0 && _cursor < _displayItems.Count;
		_equipBtn.Disabled = !hasItem;
		_useBtn.Disabled = !hasItem;
		_dropBtn.Disabled = !hasItem;

		if (hasItem)
		{
			var (_, item) = _displayItems[_cursor];
			_equipBtn.Text = item.Equipped ? "卸下 [E]" : "装备 [E]";
			_useBtn.Disabled = !item.Tags.ContainsKey("治疗");
		}
		else
		{
			_equipBtn.Text = "装备 [E]";
		}
	}


	private void RenderHeader(Actor player)
	{
		var wt = player.CarryWeight;
		var maxWt = player.MaxCarryWeight;
		var wtColor = player.IsOverweight ? "#ff4444" : "#cccccc";
		var sortLabel = _sortMode switch
		{
			InvSortMode.Weight => "重量",
			InvSortMode.Price => "价格",
			_ => "名称",
		};

		var focusTag = _host.HasFocus
			? " [color=#66ff88]●[/color]"
			: " [color=#666666]○[/color]";

		_header.Clear();
		_header.AppendText(
			$"[center]── 背包 ──{focusTag}[/center]\n" +
			$"💰 {player.Gold}G  [color={wtColor}]⚖ {wt:F1}/{maxWt:F1}kg[/color]  " +
			$"[color=#888888]排序:{sortLabel}  {_displayItems.Count}件[/color]");
	}

	private void RenderDetail()
	{
		_detailBox.Clear();

		if (_cursor < 0 || _cursor >= _displayItems.Count)
		{
			_detailBox.AppendText("[color=#888888]选择物品查看详情[/color]");
			return;
		}

		var (_, item) = _displayItems[_cursor];
		var sb = new StringBuilder();

		sb.Append($"[color=#ffffff]{item.Name}[/color]");
		var catDef = ItemCategoryDef.Get(item.Category);
		var catName = catDef?.Name ?? item.Category;
		sb.AppendLine($"  [color=#888888][{catName}][/color]");

		if (item.SharpDamage > 0 || item.BluntDamage > 0)
		{
			sb.Append("[color=#ff6666]伤害:[/color] ");
			if (item.SharpDamage > 0) sb.Append($"锐{item.SharpDamage:F0} ");
			if (item.BluntDamage > 0) sb.Append($"钝{item.BluntDamage:F0} ");
			sb.AppendLine();
		}

		if (item.SharpArmor > 0 || item.BluntArmor > 0)
		{
			sb.Append("[color=#6699ff]护甲:[/color] ");
			if (item.SharpArmor > 0) sb.Append($"锐防{item.SharpArmor:F0} ");
			if (item.BluntArmor > 0) sb.Append($"钝防{item.BluntArmor:F0} ");
			sb.AppendLine();
		}

		if (item.IsEquippable)
		{
			var layerName = item.Layer switch
			{
				EquipLayer.Skin => "贴身",
				EquipLayer.Middle => "中层",
				EquipLayer.Shell => "外壳",
				EquipLayer.Overhead => "最外",
				_ => item.Layer.ToString(),
			};
			sb.AppendLine($"[color=#66cc99]位置:[/color] {item.BodyPart} / {layerName}");

			if (item.CoveredParts.Count > 0)
				sb.AppendLine($"[color=#66cc99]覆盖:[/color] {string.Join(", ", item.CoveredParts)}");
		}

		if (item.GrantedSkills.Count > 0)
		{
			var skillNames = new List<string>();
			foreach (var sid in item.GrantedSkills)
			{
				var def = InteractionDefs.Get(sid);
				skillNames.Add(def?.Name ?? sid);
			}
			sb.AppendLine($"[color=#cc99ff]技能:[/color] {string.Join(", ", skillNames)}");
		}

		if (item.IsContainer)
		{
			var count = item.Contents?.Count ?? 0;
			sb.AppendLine($"[color=#66ccff]容器:[/color] {count}件物品");
		}

		sb.Append($"[color=#888888]重量: {item.EffectiveWeight:F1}kg  价格: {item.Price}G[/color]");

		_detailBox.AppendText(sb.ToString());
	}

	private void EnsureCursorVisible()
	{
		if (_cursor < 0 || _cursor >= _itemRows.Count) return;
		var row = _itemRows[_cursor];
		var rowY = row.Position.Y;
		var rowH = row.Size.Y;
		var scrollH = _itemScroll.Size.Y;
		var currentScroll = _itemScroll.ScrollVertical;

		if (rowY < currentScroll)
			_itemScroll.ScrollVertical = (int)rowY;
		else if (rowY + rowH > currentScroll + scrollH)
			_itemScroll.ScrollVertical = (int)(rowY + rowH - scrollH);
	}

	private static string FormatInlineStats(Item item)
	{
		var parts = new List<string>();
		if (item.SharpDamage > 0) parts.Add($"锐{item.SharpDamage:F0}");
		if (item.BluntDamage > 0) parts.Add($"钝{item.BluntDamage:F0}");
		if (item.SharpArmor > 0) parts.Add($"锐防{item.SharpArmor:F0}");
		if (item.BluntArmor > 0) parts.Add($"钝防{item.BluntArmor:F0}");
		return string.Join(" ", parts);
	}
}
