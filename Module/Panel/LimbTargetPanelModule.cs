using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;

namespace MiniRPG.Module.Panel;

public sealed class LimbTargetPanelModule : ListPanelBase, ITooltipRegistrar
{
	public sealed class LimbTargetOption
	{
		public string LimbId { get; init; } = "";
		public string Label { get; init; } = "";
		public int CurrentDurability { get; init; }
		public int MaxDurability { get; init; }
		public bool IsVital { get; init; }
		public bool IsMissing { get; init; }
	}

	public sealed class LimbTargetRequest
	{
		public Actor? TargetActor { get; init; }
		public Item? TargetItem { get; init; }
		public InteractionDef Skill { get; init; } = new();
		public string TargetName { get; init; } = "";
		public IReadOnlyList<LimbTargetOption> Options { get; init; } = [];
	}

	public override string PanelId => "limb_target";
	public override PanelContainer PanelNode => _panel;
	public override bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public event Action? CloseRequested;
	public event Action<Actor, InteractionDef, Limb>? LimbConfirmed;
	public event Action<LimbTargetRequest, LimbTargetOption>? TargetConfirmed;

	private readonly PanelContainer _panel;
	private readonly RichTextLabel _header;
	private readonly Button _cancelButton;

	private GameState? _state;
	private LimbTargetRequest? _request;
	private readonly List<LimbTargetOption> _options = [];
	private RichTooltipLayer? _tooltipLayer;

	public void RegisterTooltips(RichTooltipLayer layer) => _tooltipLayer = layer;

	public LimbTargetPanelModule(PanelContainer panel)
	{
		_panel = panel;
		var vbox = panel.GetNode<VBoxContainer>("MarginContainer/VBox");
		_header = vbox.GetNode<RichTextLabel>("HeaderBar/Header");
		var itemScroll = vbox.GetNode<ScrollContainer>("ItemScroll");
		var itemList = itemScroll.GetNode<VBoxContainer>("ItemList");
		BindListNodes(itemScroll, itemList);
		_cancelButton = vbox.GetNode<HBoxContainer>("ActionBar").GetNode<Button>("CancelBtn");
		_cancelButton.FocusMode = Control.FocusModeEnum.None;
		_cancelButton.Pressed += () => CloseRequested?.Invoke();
	}

	public static PanelContainer CreateControl(Theme? theme)
	{
		var panel = new PanelContainer
		{
			Name = "LimbTargetPanel",
			Visible = false,
			Theme = theme,
			CustomMinimumSize = new Vector2(280, 260),
			SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};

		var margin = new MarginContainer { Name = "MarginContainer" };
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_top", 12);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_bottom", 12);
		panel.AddChild(margin);

		var vbox = new VBoxContainer { Name = "VBox" };
		vbox.AddThemeConstantOverride("separation", 8);
		margin.AddChild(vbox);

		var headerBar = new HBoxContainer { Name = "HeaderBar" };
		vbox.AddChild(headerBar);

		var header = new RichTextLabel
		{
			Name = "Header",
			BbcodeEnabled = true,
			FitContent = true,
			ScrollActive = false,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		headerBar.AddChild(header);

		var itemScroll = new ScrollContainer
		{
			Name = "ItemScroll",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		vbox.AddChild(itemScroll);

		var itemList = new VBoxContainer { Name = "ItemList" };
		itemList.AddThemeConstantOverride("separation", 4);
		itemScroll.AddChild(itemList);

		var actionBar = new HBoxContainer
		{
			Name = "ActionBar",
			Alignment = BoxContainer.AlignmentMode.End,
		};
		vbox.AddChild(actionBar);

		var cancelButton = new Button
		{
			Name = "CancelBtn",
			ThemeTypeVariation = "ActionButton",
			Text = "ui.common.cancel",
		};
		actionBar.AddChild(cancelButton);

		return panel;
	}

	public void Open(GameState state, LimbTargetRequest request)
	{
		_state = state;
		_request = request;
		_cursor = 0;
		Visible = true;
		Refresh();
	}

	public void Open(GameState state, Actor target, InteractionDef skill)
	{
		Open(state, new LimbTargetRequest
		{
			TargetActor = target,
			Skill = skill,
			TargetName = target.DisplayName,
			Options = target.Limbs.Select(limb => new LimbTargetOption
			{
				LimbId = limb.Id,
				Label = limb.Name,
				CurrentDurability = limb.Durability,
				MaxDurability = limb.MaxDurability,
				IsVital = limb.Tags.ContainsKey(CombatModule.VitalTag),
				IsMissing = false,
			}).ToList(),
		});
	}

	public void Close()
	{
		Visible = false;
		_state = null;
		_request = null;
		_options.Clear();
		_cursor = 0;
	}

	public override void Refresh()
	{
		_options.Clear();
		if (_request != null)
			_options.AddRange(_request.Options);

		if (_cursor >= _options.Count)
			_cursor = Math.Max(0, _options.Count - 1);

		RebuildRows(_options.Count, ApplyRowContent, LocalizationService.T("ui.common.empty_inline"));
		RenderHeader();
		UpdateRowVisuals(_options.Count);
	}

	public override bool HandleCommand(string cmd)
	{
		switch (cmd)
		{
			case "up":
				MoveCursor(-1, _options.Count);
				return true;
			case "down":
				MoveCursor(1, _options.Count);
				return true;
			case "confirm":
				return ConfirmSelection();
			case "close":
				CloseRequested?.Invoke();
				return true;
			default:
				return false;
		}
	}

	public override void OnBlur()
	{
	}

	protected override int GetRowDataCount() => _options.Count;

	protected override void OnRowPressed(int index)
	{
		_cursor = index;
		ConfirmSelection();
	}

	private bool ConfirmSelection()
	{
		if (_cursor < 0 || _cursor >= _options.Count)
			return false;

		if (_request == null)
			return false;

		var option = _options[_cursor];
		TargetConfirmed?.Invoke(_request, option);
		if (_request.TargetActor != null)
		{
			var limb = _request.TargetActor.Limbs.FirstOrDefault(limb => string.Equals(limb.Id, option.LimbId, StringComparison.Ordinal));
			if (limb != null)
				LimbConfirmed?.Invoke(_request.TargetActor, _request.Skill, limb);
		}
		return true;
	}

	private void RenderHeader()
	{
		_header.Clear();
		if (_state == null || _request == null)
			return;

		_header.AppendText($"[center]{_request.Skill.Name}  {_request.TargetName}[/center]");
	}

	private void ApplyRowContent(Button row, int index)
	{
		var option = _options[index];
		var limb = _request?.TargetActor?.Limbs.FirstOrDefault(l =>
			string.Equals(l.Id, option.LimbId, StringComparison.Ordinal));
		var iconTex = limb != null ? PlaceholderUiIconCatalog.ResolveLimbRowIcon(limb) : null;
		if (iconTex != null)
		{
			row.Icon = iconTex;
			row.AddThemeConstantOverride("icon_max_width", 22);
			row.AddThemeConstantOverride("icon_max_height", 22);
		}
		else
		{
			row.Icon = null;
			row.RemoveThemeConstantOverride("icon_max_width");
			row.RemoveThemeConstantOverride("icon_max_height");
		}

		var vital = option.IsVital
			? $" {LocalizationService.T("combat.target_limb.vital")}"
			: string.Empty;
		var missing = option.IsMissing
			? $" {LocalizationService.TOrFallback("ui.limb_target.missing", "(missing)")}"
			: string.Empty;
		row.Text = option.IsMissing
			? $"{option.Label}{missing}{vital}"
			: $"{option.Label}  {option.CurrentDurability}/{option.MaxDurability}{missing}{vital}";

		if (_tooltipLayer != null)
		{
			var capturedIndex = index;
			_tooltipLayer.Attach(row, () =>
			{
				if (capturedIndex < 0 || capturedIndex >= _options.Count)
					return string.Empty;
				return BuildLimbTooltipBbcode(_options[capturedIndex]);
			});
		}
	}

	private static string BuildLimbTooltipBbcode(LimbTargetOption option)
	{
		var sb = new System.Text.StringBuilder();
		sb.AppendLine($"[b]{option.Label}[/b]");
		sb.AppendLine($"[color=#aaaaaa]耐久[/color] {option.CurrentDurability}/{option.MaxDurability}");

		if (option.IsVital)
			sb.AppendLine($"[color=#ff8888]要害[/color] 受重创可致命");
		if (option.IsMissing)
			sb.AppendLine($"[color=#888888]缺失[/color] 不可作为目标");
		if (!option.IsVital && !option.IsMissing && option.MaxDurability > 0)
		{
			var ratio = (float)option.CurrentDurability / option.MaxDurability;
			if (ratio < 0.3f)
				sb.AppendLine($"[color=#ffaa66]状态[/color] 严重受损");
			else if (ratio < 0.7f)
				sb.AppendLine($"[color=#dddd66]状态[/color] 受伤");
			else
				sb.AppendLine($"[color=#88dd88]状态[/color] 良好");
		}

		return sb.ToString();
	}
}
