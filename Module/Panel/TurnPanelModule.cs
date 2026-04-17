using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 时间轴紧凑条 — 单行横向布局：阶段徽章 | 当前角色 | 回合数 | 队列预览。
/// 设计目标：最小化纵向占用，把空间还给地图。
/// </summary>
public sealed class TurnPanelModule
{
	private readonly PanelContainer _panel;
	private readonly RichTextLabel _phaseLabel;
	private readonly RichTextLabel _actorLabel;
	private readonly RichTextLabel _turnLabel;
	private readonly RichTextLabel _renderModeLabel;
	private readonly HBoxContainer _queueFlow;
	private readonly List<PanelContainer> _queueChipPool = [];
	private readonly List<RichTextLabel> _queueChipLabels = [];
	private readonly List<ProgressBar> _queueChipBars = [];
	private static readonly Color HiddenBarTint = new(1f, 1f, 1f, 0f);

	/// <summary>队列中最多显示的角色数（避免溢出）。</summary>
	private const int MaxQueueSlots = 4;
	private const float QueueChipMinWidth = 96f;
	private const float QueueChipMinHeight = 26f;

	public TurnPanelModule(PanelContainer panel)
	{
		_panel = panel;
		var hbox = panel.GetNode<HBoxContainer>("Margin/HBox");
		_phaseLabel = hbox.GetNode<RichTextLabel>("PhaseLabel");
		_actorLabel = hbox.GetNode<RichTextLabel>("ActorLabel");
		_turnLabel = hbox.GetNode<RichTextLabel>("TurnLabel");
		_renderModeLabel = hbox.GetNode<RichTextLabel>("RenderModeLabel");
		_queueFlow = hbox.GetNode<HBoxContainer>("QueueFlow");
		ConfigureStaticLabel(_phaseLabel);
		ConfigureStaticLabel(_actorLabel);
		ConfigureStaticLabel(_turnLabel);
		ConfigureStaticLabel(_renderModeLabel);
	}

	public PanelContainer PanelNode => _panel;
	public bool Dirty { get; set; } = true;

	public void FlushIfDirty(GameState state, bool playerDead, bool watchModeEnabled, bool isIsometricMode)
	{
		if (!Dirty) return;
		Refresh(TimelineTurnManager.CreateDebugSnapshot(state, playerDead, watchModeEnabled), isIsometricMode);
	}

	public void Refresh(TimelineDebugSnapshot snapshot, bool isIsometricMode)
	{
		Dirty = false;
		RenderPhase(snapshot);
		RenderActor(snapshot);
		RenderTurn(snapshot);
		RenderRenderMode(isIsometricMode);
		RenderQueue(snapshot);
	}

	// ── 阶段徽章 ──────────────────────────────────────────

	private void RenderPhase(TimelineDebugSnapshot snapshot)
	{
		var (key, hex) = snapshot.Phase switch
		{
			TimelineDebugPhase.PlayerTurn   => ("ui.turn_panel.phase.player_turn", UIColors.HexSelected),
			TimelineDebugPhase.AutoAdvance  => ("ui.turn_panel.phase.auto_advance", UIColors.HexEquipped),
			TimelineDebugPhase.WatchMode    => ("ui.turn_panel.phase.watch_mode", UIColors.HexUtility),
			TimelineDebugPhase.Dead         => ("ui.turn_panel.phase.dead", UIColors.HexCombat),
			_                               => ("ui.turn_panel.phase.no_actor", UIColors.HexDim),
		};

		_phaseLabel.Clear();
		_phaseLabel.AppendText($"[b][color={hex}]● {LocalizationService.T(key)}[/color][/b]");
	}

	// ── 当前角色 ──────────────────────────────────────────

	private void RenderActor(TimelineDebugSnapshot snapshot)
	{
		var name = snapshot.CurrentActorName ?? LocalizationService.T("ui.common.none");
		_actorLabel.Clear();
		_actorLabel.AppendText(
			$"[color={UIColors.HexDim}]{LocalizationService.T("ui.turn_panel.summary.current")}:[/color] {name}");
	}

	// ── 回合数 ────────────────────────────────────────────

	private void RenderTurn(TimelineDebugSnapshot snapshot)
	{
		_turnLabel.Clear();
		_turnLabel.AppendText(
			$"[color={UIColors.HexDim}]T:[/color]{snapshot.WorldTurn}");
	}

	private void RenderRenderMode(bool _)
	{
		var modeText = LocalizationService.T("render.view_mode.iso_only");
		_renderModeLabel.Clear();
		_renderModeLabel.AppendText(
			$"[color={UIColors.HexDim}]{LocalizationService.T("ui.turn_panel.render_mode")}:[/color] {modeText}");
	}

	// ── 队列预览（横向角色名片）─────────────────────────────

	private static void ConfigureStaticLabel(RichTextLabel label)
	{
		// TurnPanel 需要稳定成单行，避免自动推进时因为重新换行而改高度。
		label.FitContent = false;
		label.ScrollActive = false;
		label.AutowrapMode = TextServer.AutowrapMode.Off;
	}

	private void RenderQueue(TimelineDebugSnapshot snapshot)
	{
		var visibleCount = 0;
		if (snapshot.Entries.Count == 0)
		{
			ConfigureChip(
				EnsureQueueChip(visibleCount),
				LocalizationService.T("ui.turn_panel.queue.empty"),
				UIColors.HexDim,
				isCurrent: false,
				chargePct: 0f,
				showBar: false);
			visibleCount++;
		}
		else
		{
			var count = Math.Min(snapshot.Entries.Count, MaxQueueSlots);
			for (var i = 0; i < count; i++)
			{
				var entry = snapshot.Entries[i];
				var hex = entry.IsCurrent ? UIColors.HexSelected
					: entry.IsLast ? UIColors.HexDim
					: UIColors.HexNormal;
				var charge = (int)MathF.Round(entry.Charge);
				var pct = MathF.Min(charge / TimelineTurnManager.ActionThreshold, 1f);
				var label = entry.IsPlayer ? $"{entry.ActorName}★" : entry.ActorName;
				var showBar = pct > 0.01f;

				ConfigureChip(
					EnsureQueueChip(visibleCount),
					label,
					hex,
					entry.IsCurrent,
					pct,
					showBar);
				visibleCount++;
			}

		}

		HideUnusedQueueChips(visibleCount);
	}

	private PanelContainer EnsureQueueChip(int index)
	{
		while (_queueChipPool.Count <= index)
		{
			var (chip, label, bar) = CreateQueueChip();
			_queueChipPool.Add(chip);
			_queueChipLabels.Add(label);
			_queueChipBars.Add(bar);
			_queueFlow.AddChild(chip);
		}

		var target = _queueChipPool[index];
		target.Visible = true;
		if (target.GetParent() != _queueFlow)
			_queueFlow.AddChild(target);
		_queueFlow.MoveChild(target, index);
		return target;
	}

	private void HideUnusedQueueChips(int visibleCount)
	{
		for (var i = visibleCount; i < _queueChipPool.Count; i++)
			_queueChipPool[i].Visible = false;
	}

	private void ConfigureChip(
		PanelContainer chip,
		string text,
		string colorHex,
		bool isCurrent,
		float chargePct,
		bool showBar)
	{
		var index = _queueChipPool.IndexOf(chip);
		if (index < 0)
			return;

		ApplyChipStyle(chip, isCurrent);
		var label = _queueChipLabels[index];
		label.Clear();
		label.AppendText($"[color={colorHex}]{text}[/color]");

		var bar = _queueChipBars[index];
		bar.Value = Math.Clamp(chargePct, 0f, 1f);
		bar.SelfModulate = showBar ? Colors.White : HiddenBarTint;
	}

	private static void ApplyChipStyle(PanelContainer chip, bool isCurrent)
	{
		var style = new StyleBoxFlat
		{
			BgColor = isCurrent
				? new Color(0.18f, 0.15f, 0.08f, 0.9f)
				: new Color(0.1f, 0.1f, 0.16f, 0.7f),
			CornerRadiusBottomLeft = 3,
			CornerRadiusBottomRight = 3,
			CornerRadiusTopLeft = 3,
			CornerRadiusTopRight = 3,
			ContentMarginLeft = 6,
			ContentMarginRight = 6,
			ContentMarginTop = 1,
			ContentMarginBottom = 1,
			BorderWidthBottom = 2,
			BorderColor = isCurrent ? UIColors.FocusBorder : Colors.Transparent,
		};

		chip.AddThemeStyleboxOverride("panel", style);
	}

	private static (PanelContainer Chip, RichTextLabel Label, ProgressBar Bar) CreateQueueChip()
	{
		var chip = new PanelContainer
		{
			CustomMinimumSize = new Vector2(QueueChipMinWidth, QueueChipMinHeight),
			Visible = false,
		};
		ApplyChipStyle(chip, isCurrent: false);

		var vbox = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.Fill,
		};
		vbox.AddThemeConstantOverride("separation", 0);
		chip.AddChild(vbox);

		var label = new RichTextLabel
		{
			BbcodeEnabled = true,
			CustomMinimumSize = new Vector2(0, 16),
			FitContent = false,
			ScrollActive = false,
			AutowrapMode = TextServer.AutowrapMode.Off,
			SizeFlagsHorizontal = Control.SizeFlags.Fill,
		};
		vbox.AddChild(label);

		var bar = new ProgressBar
		{
			CustomMinimumSize = new Vector2(0, 2),
			MaxValue = 1.0,
			Value = 0.0,
			ShowPercentage = false,
			SizeFlagsHorizontal = Control.SizeFlags.Fill,
			SelfModulate = HiddenBarTint,
		};
		var barBg = new StyleBoxFlat
		{
			BgColor = new Color(0.15f, 0.15f, 0.2f),
			CornerRadiusBottomLeft = 1,
			CornerRadiusBottomRight = 1,
			CornerRadiusTopLeft = 1,
			CornerRadiusTopRight = 1,
		};
		bar.AddThemeStyleboxOverride("background", barBg);
		var barFill = new StyleBoxFlat
		{
			BgColor = new Color(0.4f, 0.4f, 0.5f),
			CornerRadiusBottomLeft = 1,
			CornerRadiusBottomRight = 1,
			CornerRadiusTopLeft = 1,
			CornerRadiusTopRight = 1,
		};
		bar.AddThemeStyleboxOverride("fill", barFill);
		vbox.AddChild(bar);

		return (chip, label, bar);
	}
}
