using System.Collections.Generic;
using Godot;
using MiniRPG.Core;
using MiniRPG.Core.Data;
using MiniRPG.Core.Needs;
using MiniRPG.Module.Panel;

namespace MiniRPG.Module;

/// <summary>
/// 队伍 HUD：在屏幕左上角显示队伍成员列表。
/// 高亮激活角色，显示 HP/心情简要指示器。
/// 点击切换激活角色，Tab 键循环。
/// </summary>
public sealed class PartyHudModule
{
	private readonly PanelContainer _root;
	private readonly VBoxContainer _memberList;
	private readonly List<PartyMemberSlot> _slots = [];

	private GameState? _state;

	public PartyHudModule(PanelContainer root)
	{
		_root = root;
		_memberList = root.GetNode<VBoxContainer>("Margin/VBox");
	}

	public static PanelContainer CreateControl(Theme? theme)
	{
		var root = new PanelContainer
		{
			Name = "PartyHud",
			Visible = false,
			Theme = theme,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			FocusMode = Control.FocusModeEnum.None,
			AnchorLeft = 0f,
			AnchorTop = 0f,
			AnchorRight = 0f,
			AnchorBottom = 0f,
			OffsetLeft = 8,
			OffsetTop = 8,
			CustomMinimumSize = new Vector2(160, 0),
		};

		var style = new StyleBoxFlat
		{
			BgColor = new Color(UIColors.PanelBg, 0.85f),
			BorderColor = UIColors.IdleBorder,
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			CornerRadiusBottomLeft = 6,
			CornerRadiusBottomRight = 6,
		};
		root.AddThemeStyleboxOverride("panel", style);

		var margin = new MarginContainer { Name = "Margin", MouseFilter = Control.MouseFilterEnum.Ignore };
		margin.AddThemeConstantOverride("margin_left", 8);
		margin.AddThemeConstantOverride("margin_top", 6);
		margin.AddThemeConstantOverride("margin_right", 8);
		margin.AddThemeConstantOverride("margin_bottom", 6);
		root.AddChild(margin);

		var vbox = new VBoxContainer { Name = "VBox", MouseFilter = Control.MouseFilterEnum.Ignore };
		vbox.AddThemeConstantOverride("separation", 2);
		margin.AddChild(vbox);

		return root;
	}

	/// <summary>Tab 键循环切换激活角色。</summary>
	public void CycleActive()
	{
		if (_state == null) return;
		PartyModule.CycleActive(_state);
		Refresh();
	}

	/// <summary>点击指定成员切换激活角色。</summary>
	public void SetActive(string actorId)
	{
		if (_state == null) return;
		PartyModule.TrySetActive(_state, actorId);
		Refresh();
	}

	public void Update(GameState state)
	{
		_state = state;
		var members = PartyModule.GetMembers(state);
		var activeId = PartyModule.GetActiveId(state);

		// 只有多于 1 人时才显示
		_root.Visible = members.Count > 1;
		if (!_root.Visible) return;

		// 确保 slot 数量匹配
		while (_slots.Count < members.Count)
		{
			var slot = new PartyMemberSlot(this);
			_memberList.AddChild(slot.Root);
			_slots.Add(slot);
		}

		while (_slots.Count > members.Count)
		{
			var last = _slots[^1];
			_memberList.RemoveChild(last.Root);
			last.Root.QueueFree();
			_slots.RemoveAt(_slots.Count - 1);
		}

		for (var i = 0; i < members.Count; i++)
		{
			_slots[i].Update(members[i], string.Equals(members[i].Id, activeId, System.StringComparison.Ordinal));
		}
	}

	private void Refresh()
	{
		if (_state != null) Update(_state);
	}

	/// <summary>单个队伍成员的 UI 槽位。</summary>
	private sealed class PartyMemberSlot
	{
		public HBoxContainer Root { get; }
		private readonly Label _nameLabel;
		private readonly Label _hpLabel;
		private readonly PartyHudModule _parent;
		private string _actorId = "";

		public PartyMemberSlot(PartyHudModule parent)
		{
			_parent = parent;
			Root = new HBoxContainer
			{
				MouseFilter = Control.MouseFilterEnum.Stop,
				CustomMinimumSize = new Vector2(140, 22),
			};
			Root.AddThemeConstantOverride("separation", 6);
			Root.GuiInput += OnGuiInput;

			_nameLabel = new Label
			{
				MouseFilter = Control.MouseFilterEnum.Ignore,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			Root.AddChild(_nameLabel);

			_hpLabel = new Label
			{
				MouseFilter = Control.MouseFilterEnum.Ignore,
				HorizontalAlignment = HorizontalAlignment.Right,
				CustomMinimumSize = new Vector2(50, 0),
			};
			Root.AddChild(_hpLabel);
		}

		public void Update(Actor actor, bool isActive)
		{
			_actorId = actor.Id;

			var displayName = actor.DisplayName ?? actor.Id;
			_nameLabel.Text = isActive ? $"▶ {displayName}" : $"  {displayName}";
			_nameLabel.AddThemeColorOverride("font_color",
				isActive ? UIColors.TextSelected : UIColors.TextNormal);

			// HP 指示器
			var totalHp = 0;
			var maxHp = 0;
			foreach (var limb in actor.Limbs)
			{
				totalHp += limb.Durability;
				maxHp += limb.MaxDurability;
			}

			var hpRatio = maxHp > 0 ? (float)totalHp / maxHp : 0f;
			_hpLabel.Text = $"{totalHp}/{maxHp}";
			_hpLabel.AddThemeColorOverride("font_color", hpRatio switch
			{
				> 0.6f => UIColors.TextNormal,
				> 0.3f => new Color(1f, 0.8f, 0.3f),
				_ => new Color(1f, 0.3f, 0.3f),
			});
		}

		private void OnGuiInput(InputEvent @event)
		{
			if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
				_parent.SetActive(_actorId);
		}
	}
}
