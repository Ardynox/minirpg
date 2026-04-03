using System.Collections.Generic;
using System.Text;
using Godot;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 技能悬浮栏：可拖动的小图标网格，鼠标悬浮弹出 tooltip 显示技能详情。
/// 不参与 PanelManager 焦点系统（始终可见，不遮挡其他面板）。
/// </summary>
public class SkillBarModule
{
	private readonly PanelContainer _root;
	private readonly Godot.Panel _dragHandle;
	private readonly HFlowContainer _grid;
	private readonly PanelContainer _tooltip;
	private readonly RichTextLabel _tooltipText;

	private readonly List<Button> _slots = [];
	private readonly List<InteractionDef> _skills = [];
	private bool _dragging;
	private Vector2 _dragOffset;
	private int _lastSkillHash;

	public bool Visible
	{
		get => _root.Visible;
		set => _root.Visible = value;
	}

	public SkillBarModule(PanelContainer root)
	{
		_root = root;
		var vbox = root.GetNode<VBoxContainer>("VBox");
		_dragHandle = vbox.GetNode<Godot.Panel>("DragHandle");
		_grid = vbox.GetNode<HFlowContainer>("SkillGrid");
		_tooltip = root.GetNode<PanelContainer>("Tooltip");
		_tooltipText = _tooltip.GetNode("TooltipMargin").GetNode<RichTextLabel>("TooltipText");

		ApplyStyle();

		_dragHandle.GuiInput += OnDragHandleInput;
	}

	/// <summary>刷新技能栏：从玩家获取可用技能，重建图标格子。</summary>
	public void Refresh(Actor? player)
	{
		var newSkills = player != null ? SkillQuery.GetSkills(player) : [];
		var hash = ComputeSkillHash(newSkills);
		if (hash == _lastSkillHash && _skills.Count == newSkills.Count)
			return;

		_lastSkillHash = hash;
		_skills.Clear();
		_skills.AddRange(newSkills);

		RebuildGrid();
		_tooltip.Visible = false;
	}

	private static int ComputeSkillHash(List<InteractionDef> skills)
	{
		var h = 17;
		foreach (var s in skills)
			h = unchecked(h * 31 + (s.Id?.GetHashCode() ?? 0));
		return h;
	}

	private void RebuildGrid()
	{
		var needed = _skills.Count;

		while (_slots.Count > needed)
		{
			_slots[^1].QueueFree();
			_slots.RemoveAt(_slots.Count - 1);
		}
		while (_slots.Count < needed)
		{
			var btn = CreateSlot(_slots.Count);
			_grid.AddChild(btn);
			_slots.Add(btn);
		}

		for (var i = 0; i < needed; i++)
		{
			var skill = _skills[i];
			var icon = GetCategoryIcon(skill.Category);
			var abbr = skill.Name.Length >= 2 ? skill.Name[..2] : skill.Name;
			_slots[i].Text = $"{icon}{abbr}";
			_slots[i].Visible = true;

			var variation = skill.Category switch
			{
				"combat" => "CombatSkillSlot",
				"utility" => "UtilitySkillSlot",
				"social" => "SocialSkillSlot",
				_ => "SkillSlot",
			};
			_slots[i].ThemeTypeVariation = variation;
		}
	}

	private Button CreateSlot(int index)
	{
		var btn = new Button
		{
			CustomMinimumSize = new Vector2(42, 32),
			FocusMode = Control.FocusModeEnum.None,
			Flat = false,
			ClipText = true,
			ThemeTypeVariation = "SkillSlot",
		};

		var idx = index;
		btn.MouseEntered += () => ShowTooltip(idx);
		btn.MouseExited += () => HideTooltip();

		return btn;
	}

	private void ShowTooltip(int index)
	{
		if (index < 0 || index >= _skills.Count) return;
		var skill = _skills[index];

		var sb = new StringBuilder();
		var color = GetCategoryColorHex(skill.Category);
		sb.AppendLine($"[b][color={color}]{skill.Name}[/color][/b]");

		var catLabel = skill.Category switch
		{
			"combat" => "战斗",
			"utility" => "工具",
			"social" => "社交",
			_ => skill.Category,
		};
		sb.AppendLine($"[color=#888888]{catLabel}技能[/color]");

		if (skill.Power > 0)
			sb.AppendLine($"[color=#ffcc00]威力: {skill.Power}[/color]");
		if (skill.Cooldown > 0)
			sb.AppendLine($"[color=#aaaaaa]冷却: {skill.Cooldown} 回合[/color]");
		if (skill.Range > 1)
			sb.AppendLine($"[color=#aaaaaa]射程: {skill.Range}[/color]");
		if (!string.IsNullOrEmpty(skill.DamageType))
		{
			var dmgLabel = skill.DamageType switch
			{
				"sharp" => "锐伤",
				"blunt" => "钝伤",
				"poison" => "毒伤",
				_ => skill.DamageType,
			};
			sb.AppendLine($"[color=#cc8866]伤害类型: {dmgLabel}[/color]");
		}
		if (!string.IsNullOrEmpty(skill.Description))
			sb.AppendLine($"\n[color=#bbbbbb]{skill.Description}[/color]");

		if (skill.Required.Count > 0)
		{
			sb.Append("[color=#666666]需要:");
			foreach (var (k, v) in skill.Required)
				sb.Append($" {k}≥{v}");
			sb.AppendLine("[/color]");
		}

		_tooltipText.Clear();
		_tooltipText.AppendText(sb.ToString());

		var slotBtn = _slots[index];
		var slotPos = slotBtn.GlobalPosition;
		_tooltip.GlobalPosition = new Vector2(slotPos.X, slotPos.Y + slotBtn.Size.Y + 4);
		_tooltip.Visible = true;

		_tooltip.ResetSize();
	}

	private void HideTooltip()
	{
		_tooltip.Visible = false;
	}

	private void OnDragHandleInput(InputEvent ev)
	{
		if (ev is InputEventMouseButton mb)
		{
			if (mb.ButtonIndex == MouseButton.Left)
			{
				if (mb.Pressed)
				{
					_dragging = true;
					_dragOffset = _root.GlobalPosition - mb.GlobalPosition;
				}
				else
				{
					_dragging = false;
				}
			}
		}
		else if (ev is InputEventMouseMotion mm && _dragging)
		{
			_root.GlobalPosition = mm.GlobalPosition + _dragOffset;
		}
	}

	private void ApplyStyle()
	{
		_root.ThemeTypeVariation = "SkillBarPanel";
		_tooltip.ThemeTypeVariation = "TooltipPanel";
		_dragHandle.ThemeTypeVariation = "DragHandle";
	}

	private static string GetCategoryIcon(string cat) => cat switch
	{
		"combat" => "⚔",
		"utility" => "🔧",
		"social" => "💬",
		_ => "◆",
	};

	private static Color GetCategoryColor(string cat) => cat switch
	{
		"combat" => new Color(1f, 0.4f, 0.4f),
		"utility" => new Color(0.4f, 0.8f, 1f),
		"social" => new Color(0.4f, 1f, 0.53f),
		_ => new Color(0.8f, 0.8f, 0.8f),
	};

	private static string GetCategoryColorHex(string cat) => cat switch
	{
		"combat" => "#ff6666",
		"utility" => "#66ccff",
		"social" => "#66ff88",
		_ => "#cccccc",
	};
}
