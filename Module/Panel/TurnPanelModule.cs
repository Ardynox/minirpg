using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;

namespace MiniRPG.Module.Panel;

public sealed class TurnPanelModule
{
	private readonly PanelContainer _panel;
	private readonly Label _header;
	private readonly RichTextLabel _statusText;
	private readonly RichTextLabel _summaryText;
	private readonly RichTextLabel _queueText;

	public TurnPanelModule(PanelContainer panel)
	{
		_panel = panel;
		var vbox = panel.GetNode<VBoxContainer>("MarginContainer/VBox");
		_header = vbox.GetNode<Label>("Header");
		_statusText = vbox.GetNode<RichTextLabel>("StatusText");
		_summaryText = vbox.GetNode<RichTextLabel>("SummaryText");
		_queueText = vbox.GetNode<RichTextLabel>("QueueText");
	}

	public PanelContainer PanelNode => _panel;
	public bool Dirty { get; set; } = true;

	public void FlushIfDirty(GameState state, bool playerDead, bool watchModeEnabled)
	{
		if (!Dirty)
			return;

		Refresh(TimelineTurnManager.CreateDebugSnapshot(state, playerDead, watchModeEnabled));
	}

	public void Refresh(TimelineDebugSnapshot snapshot)
	{
		Dirty = false;
		_header.Text = LocalizationService.T("ui.turn_panel.title");
		RenderStatus(snapshot);
		RenderSummary(snapshot);
		RenderQueue(snapshot);
	}

	private void RenderStatus(TimelineDebugSnapshot snapshot)
	{
		var statusKey = snapshot.Phase switch
		{
			TimelineDebugPhase.PlayerTurn => "ui.turn_panel.phase.player_turn",
			TimelineDebugPhase.AutoAdvance => "ui.turn_panel.phase.auto_advance",
			TimelineDebugPhase.WatchMode => "ui.turn_panel.phase.watch_mode",
			TimelineDebugPhase.NoActiveActor => "ui.turn_panel.phase.no_actor",
			TimelineDebugPhase.Dead => "ui.turn_panel.phase.dead",
			_ => "ui.turn_panel.phase.no_actor",
		};

		var color = snapshot.Phase switch
		{
			TimelineDebugPhase.PlayerTurn => UIColors.TextSelected,
			TimelineDebugPhase.AutoAdvance => new Color(0.98f, 0.85f, 0.45f),
			TimelineDebugPhase.WatchMode => new Color(0.55f, 0.84f, 1f),
			TimelineDebugPhase.NoActiveActor => UIColors.TextDim,
			TimelineDebugPhase.Dead => new Color(0.96f, 0.45f, 0.45f),
			_ => UIColors.TextNormal,
		};

		_statusText.Clear();
		_statusText.AppendText(
			$"[b][color=#{color.ToHtml(false)}]{LocalizationService.T(statusKey)}[/color][/b]");
	}

	private void RenderSummary(TimelineDebugSnapshot snapshot)
	{
		var currentActor = snapshot.CurrentActorName ?? LocalizationService.T("ui.common.none");
		var lastActor = snapshot.LastActorName ?? LocalizationService.T("ui.common.none");
		var inputKey = snapshot.InputLockedReason switch
		{
			TimelineInputLockReason.None => "ui.turn_panel.input.ready",
			TimelineInputLockReason.OtherActorsActing => "ui.turn_panel.input.locked_auto",
			TimelineInputLockReason.WatchMode => "ui.turn_panel.input.locked_watch",
			TimelineInputLockReason.Dead => "ui.turn_panel.input.dead",
			_ => "ui.turn_panel.input.none",
		};

		var sb = new StringBuilder();
		AppendSummaryLine(sb, "ui.turn_panel.summary.current", currentActor);
		AppendSummaryLine(sb, "ui.turn_panel.summary.last", lastActor);
		AppendSummaryLine(sb, "ui.turn_panel.summary.input", LocalizationService.T(inputKey));
		AppendSummaryLine(sb, "ui.turn_panel.summary.turn", snapshot.WorldTurn.ToString());

		_summaryText.Clear();
		_summaryText.AppendText(sb.ToString());
	}

	private void RenderQueue(TimelineDebugSnapshot snapshot)
	{
		_queueText.Clear();
		var sb = new StringBuilder();
		sb.Append($"[b]{LocalizationService.T("ui.turn_panel.queue.title")}[/b]");

		if (snapshot.Entries.Count == 0)
		{
			sb.Append('\n');
			sb.Append($"[color=#{UIColors.TextDim.ToHtml(false)}]{LocalizationService.T("ui.turn_panel.queue.empty")}[/color]");
			_queueText.AppendText(sb.ToString());
			return;
		}

		foreach (var entry in snapshot.Entries)
		{
			sb.Append('\n');
			sb.Append(RenderEntry(entry));
		}

		_queueText.AppendText(sb.ToString());
	}

	private static void AppendSummaryLine(StringBuilder sb, string labelKey, string value)
	{
		if (sb.Length > 0)
			sb.Append('\n');

		sb.Append($"[color=#{UIColors.TextDim.ToHtml(false)}]{LocalizationService.T(labelKey)}:[/color] ");
		sb.Append(value);
	}

	private static string RenderEntry(TimelineDebugEntry entry)
	{
		var color = entry.IsCurrent
			? UIColors.TextSelected
			: entry.IsLast
				? UIColors.TextDim
				: UIColors.TextNormal;
		var prefix = entry.IsCurrent ? ">" : "-";
		var flags = new List<string>();
		if (entry.IsPlayer)
			flags.Add(LocalizationService.T("ui.turn_panel.tag.you"));
		if (entry.IsLast)
			flags.Add(LocalizationService.T("ui.turn_panel.tag.last"));

		var label = flags.Count > 0
			? $" [{string.Join("] [", flags)}]"
			: string.Empty;
		var charge = (int)MathF.Round(entry.Charge);
		return $"[color=#{color.ToHtml(false)}]{prefix} {entry.ActorName}{label}  C:{charge:0}/{TimelineTurnManager.ActionThreshold:0}  S:{entry.Speed:0.00}  ETA:{entry.EtaToAct:0.0}[/color]";
	}
}
