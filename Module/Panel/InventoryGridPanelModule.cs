using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MiniRPG.Core.Config;

namespace MiniRPG.Module.Panel;

/// <summary>
/// Tarkov ????????????? + ?????????pockets / ??? backpack/vest/belt ??????
/// ??????????????/??/??/??/????R ??????????????/????
/// ???? InventoryMoveItem/InventoryRotateItem/InventoryAutoPack??? fallback ? ServerActionGateway ?????
/// </summary>
public sealed class InventoryGridPanelModule : IPanel, ITooltipRegistrar
{
	public string PanelId => "inventory";
	public PanelContainer PanelNode => _panel;
	public bool CanFocus => true;
	public bool ConsumeUnhandledKeys => false;
	public bool AllowGlobalClose => true;
	public bool Dirty { get; set; }

	private RichTooltipLayer? _tooltipLayer;

	public void RegisterTooltips(RichTooltipLayer layer) => _tooltipLayer = layer;

	// ?????????????? W*CellSize ? H*CellSize ????? hit ?????????
	private const int CellSize = 32;
	private const int CellGap = 1;

	private readonly PanelContainer _panel;
	private readonly RichTextLabel _header;
	private readonly Control _equipmentList;
	private readonly VBoxContainer _gridStack;
	private readonly Label _unplacedHint;
	private readonly RichTextLabel _detailBox;
	private readonly Button _equipBtn;
	private readonly Button _useBtn;
	private readonly Button _rotateBtn;
	private readonly Button _dropBtn;
	private readonly Button _autoPackBtn;
	private readonly PopupMenu _contextMenu;
	private readonly IInventoryPanelHost _host;

	private readonly Dictionary<string, Control> _gridCanvases = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Control> _placementVisuals = new(StringComparer.Ordinal);
	private string _selectedInstanceId = string.Empty;
	private bool _visible;

	private const int ContextEquipUnequip = 0;
	private const int ContextUse = 1;
	private const int ContextDrop = 2;
	private const int ContextOpenContainer = 3;
	private const int ContextRotate = 4;

	public bool Visible
	{
		get => _visible;
		set
		{
			_visible = value;
			_panel.Visible = value;
			if (value) Refresh();
		}
	}

	public InventoryGridPanelModule(PanelContainer panel, IInventoryPanelHost host)
	{
		_panel = panel;
		_host = host;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_header = vbox.GetNode<RichTextLabel>("HeaderBar/Header");
		var equipmentColumn = vbox.GetNode("GridArea/EquipmentColumn");
		_equipmentList = equipmentColumn.GetNode<Control>("EquipmentList");
		var equipmentTitle = equipmentColumn.GetNodeOrNull<Label>("EquipmentTitle");
		if (equipmentTitle != null)
			equipmentTitle.Text = LocalizationService.TOrFallback("ui.inventory.equipment_title", "Equipped");
		var gridScroll = vbox.GetNode<ScrollContainer>("GridArea/GridScroll");
		_gridStack = gridScroll.GetNode<VBoxContainer>("GridStack");
		_unplacedHint = vbox.GetNode<Label>("UnplacedHint");
		_detailBox = vbox.GetNode<RichTextLabel>("DetailBox");
		var hintBar = vbox.GetNodeOrNull<Label>("HintBar");
		if (hintBar != null)
			hintBar.Text = LocalizationService.TOrFallback(
				"ui.inventory.hint_grid",
				"Drag to move | R rotates | Right-click for options | F auto-packs");
		var actionBar = vbox.GetNode<HBoxContainer>("ActionBar");
		_equipBtn = actionBar.GetNode<Button>("EquipBtn");
		_useBtn = actionBar.GetNode<Button>("UseBtn");
		_rotateBtn = actionBar.GetNode<Button>("RotateBtn");
		_dropBtn = actionBar.GetNode<Button>("DropBtn");
		_autoPackBtn = actionBar.GetNode<Button>("AutoPackBtn");
		_contextMenu = panel.GetNode<PopupMenu>("ContextMenu");

		WireActionButtons();
		_contextMenu.IdPressed += OnContextMenuAction;
	}

	public bool HandleCommand(string cmd) => cmd switch
	{
		"action1" => TryEquipSelected(),
		"action5" => TryUseSelected(),
		"action2" => TryDropSelected(),
		"action3" => TryAutoPack(),
		"action4" => TryRotateSelected(),
		"close" => Close(),
		_ => false,
	};

	public void OnFocus() { }
	public void OnBlur() { }

	public void FlushIfDirty()
	{
		if (!Dirty) return;
		Dirty = false;
		Refresh();
	}

	public void Refresh()
	{
		var player = ActorModule.GetPlayer(_host.State);
		if (player == null) return;

		InventoryModule.EnsureGridSynchronized(player);
		RebuildEquipmentList(player);
		RebuildGridStack(player);
		RebuildUnplacedHint(player);
		RenderHeader(player);
		RenderDetail(player);
		UpdateActionButtons(player);
	}

	private void WireActionButtons()
	{
		foreach (var btn in new[] { _equipBtn, _useBtn, _rotateBtn, _dropBtn, _autoPackBtn })
			btn.FocusMode = Control.FocusModeEnum.None;
		_equipBtn.Pressed += () => TryEquipSelected();
		_useBtn.Pressed += () => TryUseSelected();
		_rotateBtn.Pressed += () => TryRotateSelected();
		_dropBtn.Pressed += () => TryDropSelected();
		_autoPackBtn.Pressed += () => TryAutoPack();
	}

	// ?? ????????? ?????????????????????????????

	// ??? paper-doll?? (BodyPart, side) ??? 5x6 ????????? layer ????? mini ???
	private const int SlotCellW = 50;
	private const int SlotCellH = 56;
	private const int SlotCellGap = 4;
	private const int SlotGridCols = 5;

	private static readonly EquipLayer[] LayerOuterToInner =
	[
		EquipLayer.Shell,
		EquipLayer.Middle,
		EquipLayer.Skin,
	];

	private void RebuildEquipmentList(Actor player)
	{
		foreach (var child in _equipmentList.GetChildren())
			child.QueueFree();

		AddSilhouette(_equipmentList);

		var slotGroups = new Dictionary<(int Col, int Row), List<(EquipSlot Slot, Limb Limb)>>();
		foreach (var limb in player.Limbs)
		{
			foreach (var slot in limb.EquipSlots)
			{
				var (col, row) = ResolvePaperDollCell(slot.BodyPart, limb.Id);
				if (col < 0) continue;
				var key = (col, row);
				if (!slotGroups.TryGetValue(key, out var list))
				{
					list = new List<(EquipSlot, Limb)>();
					slotGroups[key] = list;
				}
				list.Add((slot, limb));
			}
		}

		foreach (var ((col, row), slots) in slotGroups)
		{
			var cell = BuildSlotCell(player, col, row, slots);
			_equipmentList.AddChild(cell);
		}
	}

	private static (int Col, int Row) ResolvePaperDollCell(string bodyPart, string limbId)
	{
		var lower = limbId.ToLowerInvariant();
		var isRight = lower.Contains("_right") || lower.Contains("_r_") || lower.EndsWith("_r");
		// var isLeft = lower.Contains("_left") || lower.Contains("_l_") || lower.EndsWith("_l");

		return bodyPart switch
		{
			BodyParts.Head => (2, 0),
			BodyParts.Torso => (2, 2),
			BodyParts.Arm => isRight ? (4, 2) : (0, 2),
			BodyParts.Hand => isRight ? (4, 3) : (0, 3),
			BodyParts.Leg => isRight ? (3, 4) : (1, 4),
			BodyParts.Foot => isRight ? (3, 5) : (1, 5),
			_ => (2, 1),
		};
	}

	private void AddSilhouette(Control parent)
	{
		var stride = SlotCellW + SlotCellGap;
		var verticalStride = SlotCellH + SlotCellGap;
		var centerX = SlotGridCols * stride / 2f;
		// ?
		AddSilhouettePart(parent, new Vector2(centerX - 22, 4), new Vector2(44, 44), 0.55f);
		// ??
		AddSilhouettePart(parent, new Vector2(centerX - 36, 2 * verticalStride - 4), new Vector2(72, SlotCellH + 8), 0.45f);
		// ??
		AddSilhouettePart(parent, new Vector2(centerX - 28, 3 * verticalStride), new Vector2(56, SlotCellH), 0.32f);
		// ??
		AddSilhouettePart(parent, new Vector2(centerX - 28, 4 * verticalStride), new Vector2(24, SlotCellH * 2 + SlotCellGap), 0.4f);
		AddSilhouettePart(parent, new Vector2(centerX + 4, 4 * verticalStride), new Vector2(24, SlotCellH * 2 + SlotCellGap), 0.4f);
	}

	private static void AddSilhouettePart(Control parent, Vector2 position, Vector2 size, float alpha)
	{
		var rect = new ColorRect
		{
			Position = position,
			Size = size,
			Color = new Color(0.30f, 0.32f, 0.36f, alpha),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		parent.AddChild(rect);
	}

	private Control BuildSlotCell(Actor player, int col, int row, List<(EquipSlot Slot, Limb Limb)> slots)
	{
		var stride = SlotCellW + SlotCellGap;
		var verticalStride = SlotCellH + SlotCellGap;
		var cell = new Control
		{
			Position = new Vector2(col * stride, row * verticalStride),
			Size = new Vector2(SlotCellW, SlotCellH),
			CustomMinimumSize = new Vector2(SlotCellW, SlotCellH),
			MouseFilter = Control.MouseFilterEnum.Stop,
		};

		var bodyPart = slots[0].Slot.BodyPart;
		var limbId = slots[0].Limb.Id.ToLowerInvariant();
		var sideHint = limbId.Contains("_left") ? "L" : (limbId.Contains("_right") ? "R" : "");
		var partLabel = GameLocalizer.LocalizeBodyPart(bodyPart);
		if (!string.IsNullOrEmpty(sideHint)) partLabel = $"{partLabel} {sideHint}";

		var bg = new ColorRect
		{
			Color = new Color(0.10f, 0.12f, 0.14f, 0.92f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		cell.AddChild(bg);

		var partTitle = new Label
		{
			Text = partLabel,
			Position = new Vector2(2, 2),
			Size = new Vector2(SlotCellW - 4, 14),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		partTitle.AddThemeColorOverride("font_color", new Color(0.78f, 0.82f, 0.9f));
		partTitle.AddThemeFontSizeOverride("font_size", 10);
		cell.AddChild(partTitle);

		// ?? layer ???????
		var stripTop = 18;
		var stripH = 12;
		var stripGap = 1;
		foreach (var layer in LayerOuterToInner)
		{
			var slotForLayer = slots.FirstOrDefault(s => s.Slot.Layer == layer);
			var strip = BuildLayerStrip(player, slotForLayer.Slot, layer);
			strip.Position = new Vector2(2, stripTop);
			strip.Size = new Vector2(SlotCellW - 4, stripH);
			strip.CustomMinimumSize = strip.Size;
			cell.AddChild(strip);
			stripTop += stripH + stripGap;
		}

		return cell;
	}

	private Control BuildLayerStrip(Actor player, EquipSlot? slot, EquipLayer layer)
	{
		var capturedSlot = slot;
		var strip = new DragDropControl
		{
			MouseFilter = Control.MouseFilterEnum.Stop,
			TooltipText = GameLocalizer.LocalizeEquipLayer(layer),
			OnCanDrop = (_, data) => CanDropOnEquipStrip(capturedSlot, data),
			OnDrop = (_, data) => DropOnEquipStrip(data),
		};

		var item = slot is { ItemId: { } itemId }
			? player.Inventory.Find(i => i.InstanceId == itemId)
			: null;
		var isSelected = item != null
			&& string.Equals(item.InstanceId, _selectedInstanceId, StringComparison.Ordinal);

		Color bgColor;
		string text;
		if (item != null)
		{
			bgColor = ItemColor(item, isSelected);
			text = ItemFormatHelper.GetDisplayName(_host.State, item);
		}
		else
		{
			bgColor = new Color(0.18f, 0.20f, 0.24f, 0.55f);
			text = LayerShortLabel(layer);
		}

		var bg = new ColorRect
		{
			Color = bgColor,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		strip.AddChild(bg);

		if (isSelected)
		{
			var border = new ColorRect
			{
				Color = new Color(1f, 0.85f, 0.4f, 1f),
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			border.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			strip.AddChild(border);
		}

		var label = new Label
		{
			Text = text,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Center,
			ClipText = true,
		};
		label.AddThemeColorOverride("font_color", item != null
			? new Color(0.98f, 0.98f, 0.98f)
			: new Color(0.55f, 0.6f, 0.65f));
		label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.95f));
		label.AddThemeConstantOverride("outline_size", 2);
		label.AddThemeFontSizeOverride("font_size", 9);
		label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		label.OffsetLeft = 4;
		label.OffsetRight = -2;
		strip.AddChild(label);

		if (item != null)
		{
			var capturedInstance = item.InstanceId;
			strip.GuiInput += ev => OnEquipStripGuiInput(ev, capturedInstance);
			// 装备 strip 有装备时支持拖出：drop 到 grid 时 source=equipped 走 ToggleEquip + MoveTo
			strip.OnGetDragData = (_) => GetDragDataForEquippedItem(capturedInstance);
		}

		if (_tooltipLayer != null)
		{
			var capturedSlotItemId = capturedSlot?.ItemId;
			var capturedBodyPart = capturedSlot?.BodyPart;
			var layerLabel = GameLocalizer.LocalizeEquipLayer(layer);
			_tooltipLayer.Attach(strip, () =>
			{
				if (!string.IsNullOrEmpty(capturedSlotItemId))
				{
					var p = ActorModule.GetPlayer(_host.State);
					var equipped = p?.Inventory.Find(i => i.InstanceId == capturedSlotItemId);
					if (equipped != null)
						return ItemFormatHelper.BuildDetail(_host.State, equipped);
				}
				var partLabel = capturedBodyPart != null
					? GameLocalizer.LocalizeBodyPart(capturedBodyPart)
					: string.Empty;
				return string.IsNullOrEmpty(partLabel)
					? $"[b]{layerLabel}[/b]"
					: $"[b]{partLabel}[/b]\n[color=#888888]{layerLabel}[/color]";
			});
		}

		return strip;
	}

	private bool CanDropOnEquipStrip(EquipSlot? slot, Variant data)
	{
		if (slot == null || data.VariantType != Variant.Type.Dictionary) return false;
		var dict = data.AsGodotDictionary();
		var instanceId = dict.TryGetValue("instance_id", out var iv) ? iv.AsString() : "";
		if (string.IsNullOrEmpty(instanceId)) return false;

		var player = ActorModule.GetPlayer(_host.State);
		if (player == null) return false;
		var item = player.Inventory.Find(i => i.InstanceId == instanceId);
		if (item == null || !item.IsEquippable) return false;

		// ??? BodyPart + Layer ??????????????????
		return string.Equals(item.BodyPart, slot.BodyPart, StringComparison.Ordinal)
			&& item.Layer == slot.Layer;
	}

	private void DropOnEquipStrip(Variant data)
	{
		if (data.VariantType != Variant.Type.Dictionary) return;
		var dict = data.AsGodotDictionary();
		var instanceId = dict.TryGetValue("instance_id", out var iv) ? iv.AsString() : "";
		if (string.IsNullOrEmpty(instanceId)) return;
		var source = dict.TryGetValue("source", out var sv) ? sv.AsString() : DragSourceGrid;

		var player = ActorModule.GetPlayer(_host.State);
		if (player == null) return;
		var item = player.Inventory.Find(i => i.InstanceId == instanceId);
		if (item == null) return;

		// ????????????/???????????????
		if (string.Equals(source, DragSourceEquipped, StringComparison.Ordinal) && item.Equipped)
			return;

		SubmitToggleEquip(player, instanceId);
	}

	private void SubmitToggleEquip(Actor actor, string instanceId)
	{
		var command = new InventoryToggleEquipClientCommand
		{
			ActorId = actor.Id,
			ItemInstanceId = instanceId,
		};
		if (_host.TrySubmitClientCommand(command)) return;

		var result = ServerActionGateway.Execute(_host.State, command);
		ApplyServerActionResult(result);
	}

	private static string LayerShortLabel(EquipLayer layer) => layer switch
	{
		EquipLayer.Shell => "Outer",
		EquipLayer.Middle => "Mid",
		EquipLayer.Skin => "Skin",
		_ => layer.ToString(),
	};

	private void OnEquipStripGuiInput(InputEvent ev, string instanceId)
	{
		if (ev is not InputEventMouseButton mb || !mb.Pressed) return;
		_selectedInstanceId = instanceId;
		if (mb.ButtonIndex == MouseButton.Right)
			ShowContextMenu(mb.GlobalPosition);
		else
			Refresh();
	}

	// ?? ???????? ???????????????????????????????

	private void RebuildGridStack(Actor player)
	{
		foreach (var child in _gridStack.GetChildren())
			child.QueueFree();
		_gridCanvases.Clear();
		_placementVisuals.Clear();

		// pockets ?????????? backpack > vest > belt ??
		var ordered = player.GridInventories
			.OrderBy(kv => kv.Key == GridInventory.PocketsId ? 0 : KindOrder(kv.Value.Kind))
			.ThenBy(kv => kv.Key, StringComparer.Ordinal)
			.ToList();

		foreach (var (gridId, grid) in ordered)
		{
			var section = BuildGridSection(player, gridId, grid);
			_gridStack.AddChild(section);
		}
	}

	private static int KindOrder(GridContainerKind kind) => kind switch
	{
		GridContainerKind.Pocket => 1,
		GridContainerKind.Backpack => 2,
		GridContainerKind.Vest => 3,
		GridContainerKind.Belt => 4,
		GridContainerKind.Holster => 5,
		_ => 9,
	};

	private VBoxContainer BuildGridSection(Actor player, string gridId, GridInventory grid)
	{
		var section = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		section.AddThemeConstantOverride("separation", 2);

		var title = new Label
		{
			Text = LocalizeGridTitle(grid),
		};
		section.AddChild(title);

		// 网格画布：用 DragDropControl（自带 _GetDragData/_CanDropData/_DropData override），
		// 比 SetDragForwarding+Callable 路径稳——Callable.From 对 Variant 返回值的 marshal
		// 在 Godot 4 C# 里有坑，之前拖不动多半是这个。
		var canvas = new DragDropControl
		{
			OnGetDragData = (pos) => GetDragDataForGrid(gridId, pos),
			OnCanDrop = (pos, data) => CanDropAtGrid(gridId, pos, data),
			OnDrop = (pos, data) => DropAtGrid(gridId, pos, data),
			CustomMinimumSize = new Vector2(
				grid.Width * CellSize + (grid.Width + 1) * CellGap,
				grid.Height * CellSize + (grid.Height + 1) * CellGap),
			SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
			SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
			MouseFilter = Control.MouseFilterEnum.Stop,
			ClipContents = true,
		};
		canvas.SetMeta("grid_id", gridId);

		var background = new ColorRect
		{
			Color = new Color(0.08f, 0.08f, 0.10f, 0.9f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		canvas.AddChild(background);

		_gridCanvases[gridId] = canvas;

		AddCellGuides(canvas, grid);
		foreach (var placement in grid.Placements)
		{
			var item = player.Inventory.Find(i => i.InstanceId == placement.ItemInstanceId);
			if (item == null) continue;
			var visual = BuildPlacementVisual(item, placement, gridId);
			canvas.AddChild(visual);
			_placementVisuals[placement.ItemInstanceId] = visual;
		}
		section.AddChild(canvas);
		return section;
	}

	private void AddCellGuides(Control canvas, GridInventory grid)
	{
		// ????????????????ColorRect ??? theme???????????
		for (var y = 0; y < grid.Height; y++)
		{
			for (var x = 0; x < grid.Width; x++)
			{
				var cell = new ColorRect
				{
					Position = new Vector2(
						CellGap + x * (CellSize + CellGap),
						CellGap + y * (CellSize + CellGap)),
					Size = new Vector2(CellSize, CellSize),
					Color = new Color(0.18f, 0.20f, 0.24f, 0.85f),
					MouseFilter = Control.MouseFilterEnum.Ignore,
				};
				canvas.AddChild(cell);
			}
		}
	}

	private Control BuildPlacementVisual(Item item, GridPlacement placement, string sourceGridId)
	{
		var w = placement.Width * CellSize + (placement.Width - 1) * CellGap;
		var h = placement.Height * CellSize + (placement.Height - 1) * CellGap;
		var isSelected = string.Equals(item.InstanceId, _selectedInstanceId, StringComparison.Ordinal);

		var container = new Control
		{
			Position = new Vector2(
				CellGap + placement.X * (CellSize + CellGap),
				CellGap + placement.Y * (CellSize + CellGap)),
			Size = new Vector2(w, h),
			CustomMinimumSize = new Vector2(w, h),
			TooltipText = ItemFormatHelper.GetDisplayName(_host.State, item),
			// MouseFilter=Pass：click/right-click 走 placement.GuiInput（选中 + 上下文菜单），
			// 同时让鼠标拖拽事件冒泡到父 canvas 的 SetDragForwarding 启动拖拽。
			MouseFilter = Control.MouseFilterEnum.Pass,
		};
		container.SetMeta("instance_id", item.InstanceId);

		// ????????????
		if (isSelected)
		{
			var border = new ColorRect
			{
				Color = new Color(1f, 0.85f, 0.4f, 1f),
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			border.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			container.AddChild(border);

			var fillSelected = new ColorRect
			{
				Color = ItemColor(item, isSelected: true),
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Position = new Vector2(2, 2),
				Size = new Vector2(Math.Max(0, w - 4), Math.Max(0, h - 4)),
			};
			container.AddChild(fillSelected);
		}
		else
		{
			var fill = new ColorRect
			{
				Color = ItemColor(item, isSelected: false),
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			fill.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			container.AddChild(fill);
		}

		var label = new Label
		{
			Text = ShortName(item),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		label.AddThemeColorOverride("font_color", new Color(0.98f, 0.98f, 0.98f));
		label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.95f));
		label.AddThemeConstantOverride("outline_size", 3);
		label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		container.AddChild(label);

		var capturedInstance = item.InstanceId;
		container.GuiInput += ev => OnPlacementGuiInput(ev, capturedInstance);

		if (_tooltipLayer != null)
		{
			_tooltipLayer.Attach(container, () =>
			{
				var p = ActorModule.GetPlayer(_host.State);
				var current = p?.Inventory.Find(i => i.InstanceId == capturedInstance);
				return current != null
					? ItemFormatHelper.BuildDetail(_host.State, current)
					: string.Empty;
			});
		}

		return container;
	}

	private static string ShortName(Item item)
	{
		var name = item.Name;
		if (string.IsNullOrEmpty(name)) name = item.Id;
		if (item.IsStackable && item.SafeStackCount > 1)
			return $"{name}\nx{item.SafeStackCount}";
		return name;
	}

	private static Color ItemColor(Item item, bool isSelected)
	{
		var baseColor = item.Category switch
		{
			ItemCategories.Weapon => new Color(0.85f, 0.45f, 0.45f),
			ItemCategories.Armor => new Color(0.55f, 0.65f, 0.85f),
			ItemCategories.Clothing => new Color(0.65f, 0.55f, 0.85f),
			ItemCategories.Food => new Color(0.55f, 0.85f, 0.55f),
			ItemCategories.Tool => new Color(0.85f, 0.75f, 0.45f),
			ItemCategories.Consumable => new Color(0.85f, 0.55f, 0.85f),
			ItemCategories.Ammo => new Color(0.95f, 0.85f, 0.45f),
			ItemCategories.Material => new Color(0.65f, 0.55f, 0.45f),
			_ => new Color(0.6f, 0.6f, 0.6f),
		};
		return isSelected
			? new Color(baseColor.R + 0.15f, baseColor.G + 0.15f, baseColor.B + 0.15f, 1f)
			: new Color(baseColor.R, baseColor.G, baseColor.B, 0.95f);
	}

	private void OnPlacementGuiInput(InputEvent ev, string instanceId)
	{
		if (ev is not InputEventMouseButton mb || !mb.Pressed) return;
		_selectedInstanceId = instanceId;
		if (mb.ButtonIndex == MouseButton.Right)
			ShowContextMenu(mb.GlobalPosition);
		else
			Refresh();
	}

	private string LocalizeGridTitle(GridInventory grid) => grid.Kind switch
	{
		GridContainerKind.Pocket => LocalizationService.TOrFallback("ui.inventory.grid.pocket", "Pockets"),
		GridContainerKind.Backpack => LocalizationService.TOrFallback("ui.inventory.grid.backpack", "Backpack"),
		GridContainerKind.Vest => LocalizationService.TOrFallback("ui.inventory.grid.vest", "Vest"),
		GridContainerKind.Belt => LocalizationService.TOrFallback("ui.inventory.grid.belt", "Belt"),
		GridContainerKind.Holster => LocalizationService.TOrFallback("ui.inventory.grid.holster", "Holster"),
		_ => grid.Id,
	};

	// ?? ?? ?????????????????????????????????????????????

	private const string DragSourceGrid = "grid";
	private const string DragSourceEquipped = "equipped";

	// canvas 用 atPosition hit-test 找哪个 placement 被拖：placement.MouseFilter=Pass
	// 把鼠标拖拽事件冒泡上来，canvas 的 SetDragForwarding 在这里启动拖拽。
	private Variant GetDragDataForGrid(string gridId, Vector2 atPosition)
	{
		var player = ActorModule.GetPlayer(_host.State);
		if (player == null) return default;
		if (!player.GridInventories.TryGetValue(gridId, out var grid)) return default;

		var (cellX, cellY) = WorldToCell(atPosition);
		var hit = grid.Placements.FirstOrDefault(p =>
			cellX >= p.X && cellX < p.X + p.Width &&
			cellY >= p.Y && cellY < p.Y + p.Height);
		if (hit == null) return default;

		var item = player.Inventory.Find(i => i.InstanceId == hit.ItemInstanceId);
		if (item == null) return default;

		_selectedInstanceId = hit.ItemInstanceId;
		AttachDragPreview(item, hit.Width, hit.Height);

		return new Godot.Collections.Dictionary
		{
			["source"] = DragSourceGrid,
			["instance_id"] = hit.ItemInstanceId,
			["source_grid_id"] = gridId,
			["pickup_offset_x"] = cellX - hit.X,
			["pickup_offset_y"] = cellY - hit.Y,
		};
	}

	// ?? strip ???? drag start?source = "equipped"?drop ?? ToggleEquip ??? MoveTo?
	private Variant GetDragDataForEquippedItem(string instanceId)
	{
		var player = ActorModule.GetPlayer(_host.State);
		if (player == null) return default;
		var item = player.Inventory.Find(i => i.InstanceId == instanceId);
		if (item == null) return default;

		_selectedInstanceId = instanceId;
		var def = ItemSizeRegistry.GetDef(item);
		AttachDragPreview(item, def.Width, def.Height);

		return new Godot.Collections.Dictionary
		{
			["source"] = DragSourceEquipped,
			["instance_id"] = instanceId,
			["source_grid_id"] = "",
			["pickup_offset_x"] = 0,
			["pickup_offset_y"] = 0,
		};
	}

	private void AttachDragPreview(Item item, int width, int height)
	{
		var w = width * CellSize + Math.Max(0, width - 1) * CellGap;
		var h = height * CellSize + Math.Max(0, height - 1) * CellGap;
		var preview = new Control
		{
			Size = new Vector2(w, h),
			CustomMinimumSize = new Vector2(w, h),
		};
		var baseColor = ItemColor(item, isSelected: false);
		var fill = new ColorRect
		{
			Color = new Color(baseColor.R, baseColor.G, baseColor.B, 0.75f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		fill.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		preview.AddChild(fill);
		var label = new Label
		{
			Text = ShortName(item),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		label.AddThemeColorOverride("font_color", new Color(0.98f, 0.98f, 0.98f));
		label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.95f));
		label.AddThemeConstantOverride("outline_size", 3);
		label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		preview.AddChild(label);
		_panel.SetDragPreview(preview);
	}

	private bool CanDropAtGrid(string gridId, Vector2 atPosition, Variant data)
	{
		if (data.VariantType != Variant.Type.Dictionary) return false;
		var dict = data.AsGodotDictionary();
		var instanceId = dict.TryGetValue("instance_id", out var iv) ? iv.AsString() : "";
		if (string.IsNullOrEmpty(instanceId)) return false;

		var player = ActorModule.GetPlayer(_host.State);
		if (player == null) return false;
		if (!player.GridInventories.TryGetValue(gridId, out var grid)) return false;

		var item = player.Inventory.Find(i => i.InstanceId == instanceId);
		if (item == null) return false;
		var def = ItemSizeRegistry.GetDef(item);
		var (cellX, cellY) = WorldToCell(atPosition);
		var offsetX = dict.TryGetValue("pickup_offset_x", out var oxv) ? oxv.AsInt32() : 0;
		var offsetY = dict.TryGetValue("pickup_offset_y", out var oyv) ? oyv.AsInt32() : 0;
		var anchorX = cellX - offsetX;
		var anchorY = cellY - offsetY;
		return grid.CanPlaceAt(anchorX, anchorY, def.Width, def.Height, excludeInstanceId: instanceId)
			|| (def.AllowRotation && grid.CanPlaceAt(anchorX, anchorY, def.Height, def.Width, excludeInstanceId: instanceId));
	}

	private void DropAtGrid(string gridId, Vector2 atPosition, Variant data)
	{
		if (data.VariantType != Variant.Type.Dictionary) return;
		var dict = data.AsGodotDictionary();
		var instanceId = dict.TryGetValue("instance_id", out var iv) ? iv.AsString() : "";
		if (string.IsNullOrEmpty(instanceId)) return;
		var source = dict.TryGetValue("source", out var sv) ? sv.AsString() : DragSourceGrid;

		var (cellX, cellY) = WorldToCell(atPosition);
		var offsetX = dict.TryGetValue("pickup_offset_x", out var oxv) ? oxv.AsInt32() : 0;
		var offsetY = dict.TryGetValue("pickup_offset_y", out var oyv) ? oyv.AsInt32() : 0;
		var anchorX = cellX - offsetX;
		var anchorY = cellY - offsetY;

		var player = ActorModule.GetPlayer(_host.State);
		if (player == null) return;
		var item = player.Inventory.Find(i => i.InstanceId == instanceId);
		if (item == null) return;
		var def = ItemSizeRegistry.GetDef(item);

		// ?????????????ToggleEquip ?? Equipped ????? MoveTo ?????
		if (string.Equals(source, DragSourceEquipped, StringComparison.Ordinal))
		{
			SubmitToggleEquip(player, instanceId);
		}

		var rotated = !player.GridInventories[gridId].CanPlaceAt(anchorX, anchorY, def.Width, def.Height, excludeInstanceId: instanceId)
			&& def.AllowRotation;

		var command = new InventoryMoveItemClientCommand
		{
			ActorId = player.Id,
			ItemInstanceId = instanceId,
			TargetGridId = gridId,
			X = anchorX,
			Y = anchorY,
			Rotated = rotated,
		};
		if (_host.TrySubmitClientCommand(command))
		{
			Refresh();
			_host.FlushMap();
			return;
		}

		var result = ServerActionGateway.Execute(_host.State, command);
		ApplyServerActionResult(result);
		Refresh();
		_host.FlushMap();
	}

	private static (int X, int Y) WorldToCell(Vector2 pos)
	{
		var stride = CellSize + CellGap;
		var cellX = Mathf.Max(0, Mathf.FloorToInt((pos.X - CellGap) / stride));
		var cellY = Mathf.Max(0, Mathf.FloorToInt((pos.Y - CellGap) / stride));
		return (cellX, cellY);
	}

	// ?? ????? / ??? ?????????????????????????????

	private bool TryEquipSelected()
	{
		var player = ActorModule.GetPlayer(_host.State);
		if (player == null || string.IsNullOrEmpty(_selectedInstanceId)) return false;
		var index = player.Inventory.FindIndex(i => i.InstanceId == _selectedInstanceId);
		if (index < 0) return false;
		var command = new InventoryToggleEquipClientCommand
		{
			ActorId = player.Id,
			ItemInstanceId = _selectedInstanceId,
			InventoryIndex = index,
		};
		if (_host.TrySubmitClientCommand(command))
		{
			Refresh();
			_host.FlushMap();
			return true;
		}

		var result = ServerActionGateway.Execute(_host.State, command);
		ApplyServerActionResult(result);
		Refresh();
		_host.FlushMap();
		return result.Ok;
	}

	private bool TryUseSelected()
	{
		var player = ActorModule.GetPlayer(_host.State);
		if (player == null || string.IsNullOrEmpty(_selectedInstanceId)) return false;
		var index = player.Inventory.FindIndex(i => i.InstanceId == _selectedInstanceId);
		if (index < 0) return false;
		var item = player.Inventory[index];

		if (item.Category == ItemCategories.Food || item.Tags.ContainsKey(ItemTags.Nutrition))
		{
			_host.SubmitPlayerAction(TimelinePlayerAction.EatInventory(index));
			Refresh();
			_host.FlushMap();
			return true;
		}
		_host.AddLog(LocalizationService.T("log.inventory.not_consumable",
			("item", ItemFormatHelper.GetDisplayName(_host.State, item))));
		return false;
	}

	private bool TryDropSelected()
	{
		var player = ActorModule.GetPlayer(_host.State);
		if (player == null || string.IsNullOrEmpty(_selectedInstanceId)) return false;
		var command = new InventoryDropClientCommand
		{
			ActorId = player.Id,
			ItemInstanceId = _selectedInstanceId,
		};
		if (_host.TrySubmitClientCommand(command))
		{
			_selectedInstanceId = string.Empty;
			Refresh();
			_host.FlushMap();
			return true;
		}

		var result = ServerActionGateway.Execute(_host.State, command);
		ApplyServerActionResult(result);
		_selectedInstanceId = string.Empty;
		Refresh();
		_host.FlushMap();
		return result.Ok;
	}

	private bool TryRotateSelected()
	{
		var player = ActorModule.GetPlayer(_host.State);
		if (player == null || string.IsNullOrEmpty(_selectedInstanceId)) return false;
		var command = new InventoryRotateItemClientCommand
		{
			ActorId = player.Id,
			ItemInstanceId = _selectedInstanceId,
		};
		if (_host.TrySubmitClientCommand(command))
		{
			Refresh();
			_host.FlushMap();
			return true;
		}

		var result = ServerActionGateway.Execute(_host.State, command);
		ApplyServerActionResult(result);
		Refresh();
		_host.FlushMap();
		return result.Ok;
	}

	private bool TryAutoPack()
	{
		var player = ActorModule.GetPlayer(_host.State);
		if (player == null) return false;
		var command = new InventoryAutoPackClientCommand { ActorId = player.Id };
		if (_host.TrySubmitClientCommand(command))
		{
			Refresh();
			_host.FlushMap();
			return true;
		}

		var result = ServerActionGateway.Execute(_host.State, command);
		ApplyServerActionResult(result);
		Refresh();
		_host.FlushMap();
		return result.Ok;
	}

	private bool Close()
	{
		_host.CloseInventory();
		return true;
	}

	// ?? ???? ?????????????????????????????????????????

	private void ShowContextMenu(Vector2 pos)
	{
		var player = ActorModule.GetPlayer(_host.State);
		if (player == null || string.IsNullOrEmpty(_selectedInstanceId)) return;
		var item = player.Inventory.Find(i => i.InstanceId == _selectedInstanceId);
		if (item == null) return;

		_contextMenu.Clear();
		if (item.IsContainer)
			_contextMenu.AddItem(LocalizationService.T("ui.inventory.context.open"), ContextOpenContainer);
		_contextMenu.AddItem(item.Equipped
			? LocalizationService.T("ui.inventory.context.unequip")
			: LocalizationService.T("ui.inventory.context.equip"), ContextEquipUnequip);
		if (item.Category == ItemCategories.Food || item.Tags.ContainsKey(ItemTags.Nutrition))
			_contextMenu.AddItem(LocalizationService.T("ui.inventory.context.eat"), ContextUse);
		var def = ItemSizeRegistry.GetDef(item);
		if (def.AllowRotation && def.Width != def.Height)
			_contextMenu.AddItem(LocalizationService.TOrFallback("ui.inventory.context.rotate", "Rotate"), ContextRotate);
		_contextMenu.AddItem(LocalizationService.T("ui.inventory.context.drop"), ContextDrop);
		_contextMenu.Position = new Vector2I((int)pos.X, (int)pos.Y);
		_contextMenu.ResetSize();
		_contextMenu.Popup();
	}

	private void OnContextMenuAction(long id)
	{
		switch (id)
		{
			case ContextEquipUnequip: TryEquipSelected(); break;
			case ContextUse: TryUseSelected(); break;
			case ContextDrop: TryDropSelected(); break;
			case ContextRotate: TryRotateSelected(); break;
			case ContextOpenContainer:
				var player = ActorModule.GetPlayer(_host.State);
				var item = player?.Inventory.Find(i => i.InstanceId == _selectedInstanceId);
				if (item != null && item.IsContainer)
					_host.OpenChestFromInventory(item);
				break;
		}
	}

	// ?? Header / ?? / ?? ?????????????????????????????

	private void RebuildUnplacedHint(Actor player)
	{
		var unplaced = InventoryModule.GetUnplacedItems(player);
		if (unplaced.Count == 0)
		{
			_unplacedHint.Visible = false;
			return;
		}
		_unplacedHint.Visible = true;
		_unplacedHint.Text = LocalizationService.TOrFallback(
			"log.inventory.grid_unplaced",
			"{count} items have no slot in your grid.",
			("count", unplaced.Count));
	}

	private void RenderHeader(Actor player)
	{
		var wt = player.CarryWeight;
		var maxWt = player.MaxCarryWeight;
		var wtColor = player.IsOverweight ? "#ff4444" : "#cccccc";
		var focusTag = _host.HasFocus ? " [color=#66ff88]?[/color]" : " [color=#666666]?[/color]";
		_header.Clear();
		_header.AppendText($"[center]{LocalizationService.T("ui.inventory.header.title")}{focusTag}[/center]\n" +
			LocalizationService.T("ui.inventory.header.meta",
				("gold", player.Gold),
				("weight_color", wtColor),
				("current_weight", wt.ToString("F1")),
				("max_weight", maxWt.ToString("F1"))));
	}

	private void RenderDetail(Actor player)
	{
		_detailBox.Clear();
		if (string.IsNullOrEmpty(_selectedInstanceId))
		{
			_detailBox.AppendText(LocalizationService.T("ui.common.detail_hint.item"));
			return;
		}
		var item = player.Inventory.Find(i => i.InstanceId == _selectedInstanceId);
		if (item == null)
		{
			_detailBox.AppendText(LocalizationService.T("ui.common.detail_hint.item"));
			return;
		}
		_detailBox.AppendText(ItemFormatHelper.BuildDetail(_host.State, item));
		var def = ItemSizeRegistry.GetDef(item);
		_detailBox.AppendText($"\n[color=#888888]{def.Width}x{def.Height}{(def.AllowRotation ? " (rotatable)" : "")}[/color]");
		_detailBox.AppendText($"\n[color=#666666]{LocalizationService.TOrFallback("ui.inventory.detail_hint_grid", "Drag to move | Right-click for options | R rotates")}[/color]");
	}

	private void UpdateActionButtons(Actor player)
	{
		var item = string.IsNullOrEmpty(_selectedInstanceId)
			? null
			: player.Inventory.Find(i => i.InstanceId == _selectedInstanceId);
		var hasItem = item != null;
		_equipBtn.Disabled = !hasItem || !item!.IsEquippable;
		_useBtn.Disabled = !hasItem || !(item!.Category == ItemCategories.Food || item.Tags.ContainsKey(ItemTags.Nutrition));
		var def = hasItem ? ItemSizeRegistry.GetDef(item) : null;
		_rotateBtn.Disabled = !hasItem || !def!.AllowRotation || def.Width == def.Height;
		_dropBtn.Disabled = !hasItem;
		_autoPackBtn.Disabled = false;

		if (hasItem)
		{
			_equipBtn.Text = item!.Equipped
				? $"[E] {LocalizationService.T("ui.inventory.unequip")}"
				: $"[E] {LocalizationService.T("ui.inventory.equip")}";
		}
		else
		{
			_equipBtn.Text = $"[E] {LocalizationService.T("ui.inventory.equip")}";
		}
		_useBtn.Text = $"[U] {LocalizationService.T("ui.inventory.use")}";
		_rotateBtn.Text = $"[R] {LocalizationService.TOrFallback("ui.inventory.rotate", "Rotate")}";
		_dropBtn.Text = $"[Q] {LocalizationService.T("ui.inventory.drop")}";
		_autoPackBtn.Text = $"[F] {LocalizationService.TOrFallback("ui.inventory.autopack", "Auto-pack")}";
	}

	private void ApplyServerActionResult(ServerActionResult result)
	{
		foreach (var log in result.Logs)
			_host.AddLog(log);
		if (result.Events.Count > 0)
			_host.Dispatch(result.Events);
	}
}

/// <summary>
/// 通用拖放 Control：直接 override Godot 的 _GetDragData / _CanDropData / _DropData。
/// 比 SetDragForwarding + Callable.From 更稳——后者在 Godot 4 C# 里对 Variant 返回值的
/// marshal 有微妙坑，会让 drag start 静默失败拖不动。
/// </summary>
internal sealed partial class DragDropControl : Control
{
	public Func<Vector2, Variant>? OnGetDragData { get; set; }
	public Func<Vector2, Variant, bool>? OnCanDrop { get; set; }
	public Action<Vector2, Variant>? OnDrop { get; set; }

	public override Variant _GetDragData(Vector2 atPosition)
		=> OnGetDragData?.Invoke(atPosition) ?? default;

	public override bool _CanDropData(Vector2 atPosition, Variant data)
		=> OnCanDrop?.Invoke(atPosition, data) ?? false;

	public override void _DropData(Vector2 atPosition, Variant data)
		=> OnDrop?.Invoke(atPosition, data);
}
