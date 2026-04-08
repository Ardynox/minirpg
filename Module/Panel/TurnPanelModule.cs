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
	private readonly HBoxContainer _queueFlow;

	/// <summary>队列中最多显示的角色数（避免溢出）。</summary>
	private const int MaxQueueSlots = 8;

	public TurnPanelModule(PanelContainer panel)
	{
		_panel = panel;
		var hbox = panel.GetNode<HBoxContainer>("Margin/HBox");
		_phaseLabel = hbox.GetNode<RichTextLabel>("PhaseLabel");
		_actorLabel = hbox.GetNode<RichTextLabel>("ActorLabel");
		_turnLabel = hbox.GetNode<RichTextLabel>("TurnLabel");
		_queueFlow = hbox.GetNode<HBoxContainer>("QueueFlow");
	}

	public PanelContainer PanelNode => _panel;
	public bool Dirty { get; set; } = true;

	public void FlushIfDirty(GameState state, bool playerDead, bool watchModeEnabled)
	{
		if (!Dirty) return;
		Refresh(TimelineTurnManager.CreateDebugSnapshot(state, playerDead, watchModeEnabled));
	}

	public void Refresh(TimelineDebugSnapshot snapshot)
	{
		Dirty = false;
		RenderPhase(snapshot);
		RenderActor(snapshot);
		RenderTurn(snapshot);
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

	// ── 队列预览（横向角色名片）─────────────────────────────

	private void RenderQueue(TimelineDebugSnapshot snapshot)
	{
		// 清除旧的队列子节点
		foreach (var child in _queueFlow.GetChildren())
		{
			child.QueueFree();
		}

		if (snapshot.Entries.Count == 0)
		{
			var empty = CreateQueueChip(
				LocalizationService.T("ui.turn_panel.queue.empty"),
				UIColors.HexDim, isCurrent: false);
			_queueFlow.AddChild(empty);
			return;
		}

		var count = Math.Min(snapshot.Entries.Count, MaxQueueSlots);
		for (int i = 0; i < count; i++)
		{
			var entry = snapshot.Entries[i];
			var hex = entry.IsCurrent ? UIColors.HexSelected
				: entry.IsLast ? UIColors.HexDim
				: UIColors.HexNormal;

			var charge = (int)MathF.Round(entry.Charge);
			var pct = MathF.Min(charge / TimelineTurnManager.ActionThreshold, 1f);
			var label = entry.IsPlayer
				? $"{entry.ActorName}★"
				: entry.ActorName;

			var chip = CreateQueueChip(label, hex, entry.IsCurrent, pct);
			_queueFlow.AddChild(chip);
		}

		// 溢出指示
		if (snapshot.Entries.Count > MaxQueueSlots)
		{
			var more = CreateQueueChip(
				$"+{snapshot.Entries.Count - MaxQueueSlots}",
				UIColors.HexDim, isCurrent: false);
			_queueFlow.AddChild(more);
		}
	}

	/// <summary>
	/// 创建一个队列名片：小型 PanelContainer 内含 RichTextLabel。
	/// 当前行动者用金色边框高亮。底部有充能进度条。
	/// </summary>
	private static PanelContainer CreateQueueChip(
		string text, string colorHex, bool isCurrent, float chargePct = 0f)
	{
		var chip = new PanelContainer();
		chip.CustomMinimumSize = new Vector2(0, 24);

		// 背景样式
		var style = new StyleBoxFlat();
		style.BgColor = isCurrent
			? new Color(0.18f, 0.15f, 0.08f, 0.9f)   // 金色底
			: new Color(0.1f, 0.1f, 0.16f, 0.7f);     // 暗底
		style.CornerRadiusBottomLeft = 3;
		style.CornerRadiusBottomRight = 3;
		style.CornerRadiusTopLeft = 3;
		style.CornerRadiusTopRight = 3;
		style.ContentMarginLeft = 6;
		style.ContentMarginRight = 6;
		style.ContentMarginTop = 1;
		style.ContentMarginBottom = 1;

		if (isCurrent)
		{
			style.BorderWidthBottom = 2;
			style.BorderColor = UIColors.FocusBorder;
		}

		chip.AddThemeStyleboxOverride("panel", style);

		// 内容布局
		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 0);
		chip.AddChild(vbox);

		// 名称标签
		var label = new RichTextLabel();
		label.BbcodeEnabled = true;
		label.FitContent = true;
		label.ScrollActive = false;
		label.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		label.AppendText($"[color={colorHex}]{text}[/color]");
		vbox.AddChild(label);

		// 充能进度条（仅在有数据时显示）
		if (chargePct > 0.01f)
		{
			var bar = new ProgressBar();
			bar.CustomMinimumSize = new Vector2(0, 2);
			bar.MaxValue = 1.0;
			bar.Value = chargePct;
			bar.ShowPercentage = false;
			bar.SizeFlagsHorizontal = Control.SizeFlags.Fill;

			// 进度条样式
			var barBg = new StyleBoxFlat();
			barBg.BgColor = new Color(0.15f, 0.15f, 0.2f);
			barBg.CornerRadiusBottomLeft = 1;
			barBg.CornerRadiusBottomRight = 1;
			barBg.CornerRadiusTopLeft = 1;
			barBg.CornerRadiusTopRight = 1;
			bar.AddThemeStyleboxOverride("background", barBg);

			var barFill = new StyleBoxFlat();
			barFill.BgColor = isCurrent
				? UIColors.FocusBorder
				: new Color(0.4f, 0.4f, 0.5f);
			barFill.CornerRadiusBottomLeft = 1;
			barFill.CornerRadiusBottomRight = 1;
			barFill.CornerRadiusTopLeft = 1;
			barFill.CornerRadiusTopRight = 1;
			bar.AddThemeStyleboxOverride("fill", barFill);

			vbox.AddChild(bar);
		}

		return chip;
	}
}
