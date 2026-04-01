using System.Collections.Generic;
using System.Text;
using Godot;
namespace MiniRPG.Module.Panel;

/// <summary>
/// 技能面板 UI 模块：读取 SkillQuery 结果，按分类渲染到 SkillPanel 子场景中。
/// </summary>
public class SkillPanelModule : IPanel
{
	public string PanelId => "skill";
	public PanelContainer PanelNode => _panel;
	bool IPanel.Visible { get => _panel.Visible; set => _panel.Visible = value; }
	bool IPanel.CanFocus => false;
	bool IPanel.HandleCommand(string cmd) => false;

	private readonly PanelContainer _panel;
	private readonly RichTextLabel _combatList;
	private readonly RichTextLabel _utilityList;
	private readonly RichTextLabel _socialList;

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public SkillPanelModule(PanelContainer panel)
	{
		_panel = panel;
		var vbox = panel.GetNode("MarginContainer/ScrollContainer/VBox");
		_combatList = vbox.GetNode<RichTextLabel>("CombatList");
		_utilityList = vbox.GetNode<RichTextLabel>("UtilityList");
		_socialList = vbox.GetNode<RichTextLabel>("SocialList");
	}

	/// <summary>刷新技能面板内容。每次 FlushMap 时调用。</summary>
	public void Refresh(Actor? player)
	{
		if (!Visible) return;

		if (player == null)
		{
			_combatList.Text = "";
			_utilityList.Text = "";
			_socialList.Text = "";
			return;
		}

		var skills = SkillQuery.GetSkills(player);

		var combat = new List<InteractionDef>();
		var utility = new List<InteractionDef>();
		var social = new List<InteractionDef>();

		foreach (var s in skills)
		{
			switch (s.Category)
			{
				case "combat": combat.Add(s); break;
				case "utility": utility.Add(s); break;
				case "social": social.Add(s); break;
				default: utility.Add(s); break;
			}
		}

		SetList(_combatList, combat);
		SetList(_utilityList, utility);
		SetList(_socialList, social);
	}

	private static void SetList(RichTextLabel label, List<InteractionDef> defs)
	{
		if (defs.Count == 0)
		{
			label.Clear();
			label.AppendText("[color=#888888]无[/color]");
			return;
		}

		var sb = new StringBuilder();
		foreach (var d in defs)
		{
			var color = d.Category switch
			{
				"combat" => "#ff6666",
				"utility" => "#66ccff",
				"social" => "#66ff88",
				_ => "#cccccc",
			};

			sb.Append($"[color={color}]{d.Name}[/color]");

			if (d.Power > 0)
				sb.Append($" [color=#ffcc00]威力:{d.Power}[/color]");
			if (d.Cooldown > 0)
				sb.Append($" [color=#aaaaaa]CD:{d.Cooldown}[/color]");

			if (d.Description.Length > 0)
				sb.Append($"\n  [color=#888888]{d.Description}[/color]");

			sb.AppendLine();
		}

		label.Clear();
		label.AppendText(sb.ToString());
	}
}
