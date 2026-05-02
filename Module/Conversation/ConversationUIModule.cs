using System;
using System.Collections.Generic;
using MiniRPG.Core.Config;
using MiniRPG.Core.Conversation;
using MiniRPG.Core.Data;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Module.Panel;

namespace MiniRPG.Module;

/// <summary>节点图对话 UI：读 <see cref="GameState.ActiveConversations"/>，通过 ClientCommand 驱动权威状态。</summary>
/// <remarks>
/// 构造时订阅 <see cref="ConversationPanelModule.OnOptionSelected"/> / <see cref="ConversationPanelModule.OnConversationClosed"/>。
/// 会话期间通常与面板同寿；提供 <see cref="Dispose"/> 入口是为了在热重载 / 测试 / 面板被重建时能显式 -= 委托，
/// 避免重复订阅或残留回调触发到已释放的 UI 上。
/// </remarks>
public sealed class ConversationUIModule : IDisposable
{
	private readonly IGameUI _ui;
	private readonly ConversationPanelModule _panel;
	private readonly PanelManager _panels;
	private Random _rng = new();

	private string? _npcId;
	private List<int> _visibleBranchIndices = [];
	private bool _disposed;

	public bool InConversation => _panel.Visible;

	public ConversationUIModule(IGameUI ui, ConversationPanelModule panel, PanelManager panels)
	{
		_ui = ui;
		_panel = panel;
		_panels = panels;
		_panel.OnOptionSelected += OnOptionSelected;
		_panel.OnConversationClosed += CloseConversation;
	}

	public void Dispose()
	{
		if (_disposed)
			return;
		_disposed = true;
		_panel.OnOptionSelected -= OnOptionSelected;
		_panel.OnConversationClosed -= CloseConversation;
	}

	public void OpenForNpc(string npcActorId)
	{
		_npcId = npcActorId;
		_rng = new Random(_ui.State.WorldSeed + _ui.State.Turn + npcActorId.GetHashCode(StringComparison.Ordinal));
		RefreshView();
		if (_panel.Visible)
			_panels.PushFocus(_panel);
	}

	public void RefreshFromState()
	{
		if (_npcId == null || !_panel.Visible)
			return;
		RefreshView();
	}

	public void CloseConversation()
	{
		var was = _panel.Visible;
		_panel.Close();
		_npcId = null;
		_visibleBranchIndices.Clear();
		if (was)
			_panels.OnPanelClosed(_panel);
	}

	private void OnAdvanceLinePressed()
	{
		if (_npcId == null)
			return;
		var player = ActorModule.GetPlayer(_ui.State);
		if (player == null)
			return;
		var cmd = new ConversationAdvanceLineClientCommand
		{
			ActorId = player.Id,
			NpcActorId = _npcId,
		};
		if (!Submit(cmd))
			return;
		RefreshFromState();
	}

	private void OnOptionSelected(int displayIndex)
	{
		if (_npcId == null)
			return;
		if (!_ui.State.ActiveConversations.TryGetValue(_npcId, out var conv))
			return;
		if (conv.Stage == ConversationStage.Lines)
		{
			OnAdvanceLinePressed();
			return;
		}

		if (_visibleBranchIndices.Count == 0)
		{
			var pl = ActorModule.GetPlayer(_ui.State);
			if (pl != null && _npcId != null)
			{
				var leave = new ConversationLeaveClientCommand { ActorId = pl.Id, NpcActorId = _npcId };
				if (Submit(leave))
					CloseConversation();
			}

			return;
		}

		if (displayIndex < 0 || displayIndex >= _visibleBranchIndices.Count)
			return;
		var branchIndex = _visibleBranchIndices[displayIndex];
		var player = ActorModule.GetPlayer(_ui.State);
		if (player == null)
			return;
		var cmd = new ConversationChooseClientCommand
		{
			ActorId = player.Id,
			NpcActorId = _npcId,
			BranchIndex = branchIndex,
		};
		if (!Submit(cmd))
			return;
		RefreshFromState();
		if (!_ui.State.ActiveConversations.ContainsKey(_npcId))
			CloseConversation();
	}

	private bool Submit(ClientCommand command)
	{
		if (_ui.TrySubmitClientCommand(command))
			return true;
		var result = ServerActionGateway.Execute(_ui.State, command);
		foreach (var log in result.Logs)
			_ui.AddLog(log);
		if (result.Events.Count > 0)
			_ui.Dispatch(result.Events);
		return result.Ok;
	}

	private void RefreshView()
	{
		if (_npcId == null || !_ui.State.ActiveConversations.TryGetValue(_npcId, out var conv))
		{
			CloseConversation();
			return;
		}

		if (!ConversationRegistry.TryGet(conv.ConversationDefId, out var def) || def == null)
		{
			CloseConversation();
			return;
		}

		var npc = ActorModule.GetById(_ui.State, _npcId);
		var player = ActorModule.GetPlayer(_ui.State);
		if (npc == null || player == null)
		{
			CloseConversation();
			return;
		}

		var node = ConversationRegistry.FindNode(def, conv.CurrentNodeId);
		if (node == null)
		{
			CloseConversation();
			return;
		}

		var npcName = IdentificationModule.GetActorDisplayName(_ui.State, npc);
		var moodIcon = ConversationPanelModule.GetMoodIcon(npc.DialogMood);
		if (conv.Stage == ConversationStage.Lines && node.Lines.Count > 0)
		{
			var line = node.Lines[Math.Clamp(conv.LineIndex, 0, node.Lines.Count - 1)];
			_panel.Show(npcName, moodIcon, line,
				[LocalizationService.TOrFallback("ui.conversation.continue", "(Enter) Next")]);
			_visibleBranchIndices.Clear();
			return;
		}

		_visibleBranchIndices = [.. ConversationModule.GetVisibleBranchIndices(_ui.State, def, conv, node, player, npc)];
		var labels = new List<string>(_visibleBranchIndices.Count);
		foreach (var bi in _visibleBranchIndices)
		{
			var b = node.Branches[bi];
			var label = b.Text;
			if (string.Equals(b.Kind, "check", StringComparison.OrdinalIgnoreCase) && b.Check != null)
			{
				var pct = ConversationCheckResolver.EstimateSuccessPercent(_ui.State, player, b.Check, _rng);
				label += $" [DC{b.Check.Dc} ~{pct}%]";
			}

			labels.Add(label);
		}

		if (labels.Count == 0)
		{
			_panel.Show(npcName, moodIcon,
				LocalizationService.TOrFallback("ui.conversation.no_options", "(No further branches.)"),
				[LocalizationService.T("ui.dialog.end")]);
			_visibleBranchIndices.Clear();
		}
		else
		{
			var body = LocalizationService.TOrFallback("ui.conversation.choose_prompt", "Choose a reply:");
			_panel.Show(npcName, moodIcon, body, labels);
		}
	}
}
