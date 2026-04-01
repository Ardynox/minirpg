using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Module.Panel;

namespace MiniRPG.Module;

/// <summary>
/// 对话流程编排：连接 DialogRuleEngine/TemplateRenderer/DialogPool 和 DialogPanelModule。
/// 类似 CombatUIModule/TradeUIModule 的角色。
/// </summary>
public class DialogUIModule
{
	private readonly IGameUI _ui;
	private readonly DialogPanelModule _panel;
	private readonly PanelManager _panels;

	private DialogContext? _ctx;
	private Actor? _npc;
	private List<DialogOption>? _currentOptions;
	private DialogEntry? _currentEntry;
	private Random _rng = new();

	public bool InDialog => _panel.Visible;

	public DialogUIModule(IGameUI ui, DialogPanelModule panel, PanelManager panels)
	{
		_ui = ui;
		_panel = panel;
		_panels = panels;

		_panel.OnOptionSelected += OnOptionSelected;
		_panel.OnDialogClosed += () =>
		{
			_panel.Close();
			_ctx = null;
			_npc = null;
			_currentOptions = null;
			_currentEntry = null;
		};
	}

	/// <summary>开始与 NPC 对话。</summary>
	public void OpenDialog(Actor npc)
	{
		_npc = npc;
		_rng = new Random(_ui.State.RngSeed + _ui.State.Turn + npc.Id.GetHashCode());

		_ctx = DialogContext.Build(_ui.State, ActorModule.GetPlayer(_ui.State)!, npc);
		npc.DialogTalkCount++;

		var candidates = DialogPool.GetGreetCandidates();
		var entry = DialogRuleEngine.SelectEntry(_ctx, candidates, _rng);

		if (entry == null)
		{
			_ui.AddLog($"{npc.DisplayName}: 「……」");
			return;
		}

		ShowEntry(entry);
		_panels.SetFocus(_panel);
	}

	/// <summary>关闭对话面板，恢复正常输入。</summary>
	public void CloseDialog()
	{
		_panel.Close();
		_ctx = null;
		_npc = null;
		_currentOptions = null;
		_currentEntry = null;
		_panels.ClearFocus();
	}

	private void ShowEntry(DialogEntry entry)
	{
		_currentEntry = entry;
		var text = TemplateRenderer.Render(entry.Template, _ctx!, _rng);

		ApplyEffects(entry.Effects);

		var filteredOptions = DialogRuleEngine.FilterOptions(_ctx!, entry.Options);
		_currentOptions = filteredOptions;

		var mood = _ctx!.NumTags.GetValueOrDefault("mood");
		var moodIcon = DialogPanelModule.GetMoodIcon(mood);
		var npcName = _npc!.DisplayName;

		if (filteredOptions.Count == 0)
		{
			if (entry.NextId != null)
			{
				var next = DialogPool.FindById(entry.NextId);
				if (next != null)
				{
					_panel.ShowEnd(npcName, moodIcon, text);
					_currentEntry = next;
					return;
				}
			}
			_panel.ShowEnd(npcName, moodIcon, text);
			return;
		}

		var optionTexts = filteredOptions.Select(o => o.Text).ToList();
		_panel.Show(npcName, moodIcon, text, optionTexts);
	}

	private void OnOptionSelected(int index)
	{
		if (_currentOptions == null || _ctx == null || _npc == null)
		{
			CloseDialog();
			return;
		}

		if (_currentOptions.Count == 0)
		{
			if (_currentEntry?.NextId != null)
			{
				var next = DialogPool.FindById(_currentEntry.NextId);
				if (next != null)
				{
					_ctx = DialogContext.Build(_ui.State, ActorModule.GetPlayer(_ui.State)!, _npc);
					ShowEntry(next);
					return;
				}
			}
			CloseDialog();
			return;
		}

		if (index < 0 || index >= _currentOptions.Count)
		{
			CloseDialog();
			return;
		}

		var option = _currentOptions[index];
		ApplyEffects(option.Effects);

		if (option.NextId != null)
		{
			var next = DialogPool.FindById(option.NextId);
			if (next != null)
			{
				_ctx = DialogContext.Build(_ui.State, ActorModule.GetPlayer(_ui.State)!, _npc);
				ShowEntry(next);
				return;
			}
		}

		CloseDialog();
	}

	private void ApplyEffects(List<DialogEffect> effects)
	{
		if (_npc == null) return;
		foreach (var eff in effects)
		{
			switch (eff.Type)
			{
				case "affinity":
					_npc.DialogAffinity = Math.Clamp(_npc.DialogAffinity + eff.Value, 0, 100);
					if (_ctx != null) _ctx.NumTags["affinity"] = _npc.DialogAffinity;
					break;
				case "mood":
					_npc.DialogMood = Math.Clamp(_npc.DialogMood + eff.Value, -1f, 1f);
					if (_ctx != null) _ctx.NumTags["mood"] = _npc.DialogMood;
					break;
				case "memory":
					if (eff.Key != null && !_npc.DialogMemory.Contains(eff.Key))
					{
						_npc.DialogMemory.Add(eff.Key);
						_ctx?.BoolTags.Add($"memory:{eff.Key}");
					}
					break;
				case "gold":
					var player = ActorModule.GetPlayer(_ui.State);
					if (player != null)
						player.Gold += (int)eff.Value;
					break;
				case "tag":
					if (eff.Key != null && _ctx != null)
					{
						_ctx.BoolTags.Add(eff.Key);
						_ctx.NumTags[eff.Key] = eff.Value;
					}
					break;
			}
		}
	}
}
