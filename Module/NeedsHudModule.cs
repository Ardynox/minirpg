using Godot;
using MiniRPG.Core.Needs;
using MiniRPG.Module.Panel;

namespace MiniRPG.Module;

public sealed class NeedsHudModule : ITooltipRegistrar
{
	private readonly PanelContainer _root;
	private readonly TextureRect _hungerIcon;
	private readonly TextureRect _thirstIcon;
	private readonly TextureRect _restIcon;
	private readonly TextureRect _moodIcon;
	private readonly Label _hungerLabel;
	private readonly Label _thirstLabel;
	private readonly Label _restLabel;
	private readonly Label _moodLabel;
	private readonly ProgressBar _hungerBar;
	private readonly ProgressBar _thirstBar;
	private readonly ProgressBar _restBar;
	private readonly ProgressBar _moodBar;

	private float _prevHunger = -1f;
	private float _prevThirst = -1f;
	private float _prevRest = -1f;
	private float _prevMood = -1f;

	private Actor? _currentActor;
	private RichTooltipLayer? _tooltipLayer;

	public void RegisterTooltips(RichTooltipLayer layer)
	{
		_tooltipLayer = layer;
		AttachNeedTooltip(_hungerBar, NeedIds.Hunger);
		AttachNeedTooltip(_thirstBar, NeedIds.Thirst);
		AttachNeedTooltip(_restBar, NeedIds.Rest);
		AttachNeedTooltip(_moodBar, NeedIds.Mood);
	}

	private void AttachNeedTooltip(ProgressBar bar, string needId)
	{
		if (_tooltipLayer == null) return;
		// 进度条默认 MouseFilter=Ignore 收不到 hover，改为 Stop 才能 Attach 起作用
		bar.MouseFilter = Control.MouseFilterEnum.Stop;
		_tooltipLayer.Attach(bar, () => BuildNeedTooltipBbcode(needId));
	}

	private string BuildNeedTooltipBbcode(string needId)
	{
		if (_currentActor == null) return string.Empty;

		var name = NeedCatalog.GetNeedDisplayName(needId);
		var sb = new System.Text.StringBuilder();
		sb.AppendLine($"[b]{name}[/b]");

		float value;
		if (needId == NeedIds.Mood)
		{
			value = _currentActor.MoodValue;
			sb.AppendLine($"[color=#aaaaaa]当前[/color] {value:0.0}");
		}
		else
		{
			value = NeedSystem.GetNeedValueSnapshot(_currentActor, needId);
			sb.AppendLine($"[color=#aaaaaa]当前[/color] {value:0.0}");
			var def = NeedCatalog.GetNeed(needId);
			if (def != null)
				sb.AppendLine($"[color=#aaaaaa]警告阈值[/color] {def.WarningThreshold:0}");
		}

		var stageId = NeedCatalog.ResolveStageId(needId, value);
		if (!string.IsNullOrEmpty(stageId))
			sb.AppendLine($"[color=#aaaaaa]状态[/color] {NeedCatalog.GetThoughtDisplayName(stageId)}");

		return sb.ToString();
	}

	public NeedsHudModule(PanelContainer root)
	{
		_root = root;
		var vbox = _root.GetNode<VBoxContainer>("Margin/VBox");
		_hungerIcon = vbox.GetNode<TextureRect>("HungerRow/Icon");
		_thirstIcon = vbox.GetNode<TextureRect>("ThirstRow/Icon");
		_restIcon = vbox.GetNode<TextureRect>("RestRow/Icon");
		_moodIcon = vbox.GetNode<TextureRect>("MoodRow/Icon");
		_hungerLabel = vbox.GetNode<Label>("HungerRow/Hunger");
		_thirstLabel = vbox.GetNode<Label>("ThirstRow/Thirst");
		_restLabel = vbox.GetNode<Label>("RestRow/Rest");
		_moodLabel = vbox.GetNode<Label>("MoodRow/Mood");
		_hungerBar = vbox.GetNode<ProgressBar>("HungerRow/HungerBar");
		_thirstBar = vbox.GetNode<ProgressBar>("ThirstRow/ThirstBar");
		_restBar = vbox.GetNode<ProgressBar>("RestRow/RestBar");
		_moodBar = vbox.GetNode<ProgressBar>("MoodRow/MoodBar");
	}

	public static PanelContainer CreateControl(Theme? theme)
	{
		var root = new PanelContainer
		{
			Name = "NeedsHud",
			Visible = false,
			Theme = theme,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			FocusMode = Control.FocusModeEnum.None,
			AnchorLeft = 1f,
			AnchorTop = 0f,
			AnchorRight = 1f,
			AnchorBottom = 0f,
			OffsetLeft = -230f,
			OffsetTop = 14f,
			OffsetRight = -14f,
			OffsetBottom = 156f,
			ZIndex = 58,
		};

		var style = new StyleBoxFlat
		{
			BgColor = new Color(UIColors.PanelBg, 0.9f),
			BorderColor = UIColors.IdleBorder,
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 10,
			CornerRadiusTopRight = 10,
			CornerRadiusBottomLeft = 10,
			CornerRadiusBottomRight = 10,
			ShadowColor = new Color(0, 0, 0, 0.35f),
			ShadowSize = 8,
			ShadowOffset = new Vector2(0, 2),
		};
		root.AddThemeStyleboxOverride("panel", style);

		var margin = new MarginContainer { Name = "Margin", MouseFilter = Control.MouseFilterEnum.Ignore };
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_top", 10);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_bottom", 10);
		root.AddChild(margin);

		var vbox = new VBoxContainer { Name = "VBox", MouseFilter = Control.MouseFilterEnum.Ignore };
		vbox.AddThemeConstantOverride("separation", 5);
		margin.AddChild(vbox);

		vbox.AddChild(BuildNeedRow("Hunger"));
		vbox.AddChild(BuildNeedRow("Thirst"));
		vbox.AddChild(BuildNeedRow("Rest"));
		vbox.AddChild(BuildNeedRow("Mood"));
		return root;
	}

	private static HBoxContainer BuildNeedRow(string name)
	{
		var row = new HBoxContainer
		{
			Name = $"{name}Row",
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		row.AddThemeConstantOverride("separation", 8);

		var icon = new TextureRect
		{
			Name = "Icon",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			CustomMinimumSize = new Vector2(24, 24),
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			Visible = false,
		};
		row.AddChild(icon);

		var label = new Label
		{
			Name = name,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			CustomMinimumSize = new Vector2(90, 0),
		};
		row.AddChild(label);

		var bar = new ProgressBar
		{
			Name = $"{name}Bar",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 10),
			MinValue = 0,
			MaxValue = 100,
			ShowPercentage = false,
		};
		row.AddChild(bar);

		return row;
	}

	public void Update(Actor? player, int currentTurn, bool visible)
	{
		_currentActor = player;
		if (!visible || player == null)
		{
			_root.Visible = false;
			return;
		}

		_root.Visible = true;
		var hunger = NeedSystem.GetNeedValueSnapshot(player, NeedIds.Hunger);
		var thirst = NeedSystem.GetNeedValueSnapshot(player, NeedIds.Thirst);
		var rest = NeedSystem.GetNeedValueSnapshot(player, NeedIds.Rest);
		var mood = player.MoodValue;

		ApplyNeedLabel(_hungerLabel, NeedIds.Hunger, hunger);
		ApplyNeedLabel(_thirstLabel, NeedIds.Thirst, thirst);
		ApplyNeedLabel(_restLabel, NeedIds.Rest, rest);
		ApplyMoodLabel(_moodLabel, mood, NeedCatalog.GetProfileForActor(player).AllowMood);

		ApplyNeedRowIcon(_hungerIcon, NeedIds.Hunger, hunger);
		ApplyNeedRowIcon(_thirstIcon, NeedIds.Thirst, thirst);
		ApplyNeedRowIcon(_restIcon, NeedIds.Rest, rest);
		ApplyMoodRowIcon(_moodIcon, NeedCatalog.GetProfileForActor(player).AllowMood);

		AnimateBar(_hungerBar, hunger, _prevHunger, ResolveNeedColor(NeedIds.Hunger, hunger));
		AnimateBar(_thirstBar, thirst, _prevThirst, ResolveNeedColor(NeedIds.Thirst, thirst));
		AnimateBar(_restBar, rest, _prevRest, ResolveNeedColor(NeedIds.Rest, rest));
		var moodAllowed = NeedCatalog.GetProfileForActor(player).AllowMood;
		AnimateBar(_moodBar, moodAllowed ? mood : 0, _prevMood, moodAllowed ? ResolveMoodColor(mood) : UIColors.TextDim);

		if (_prevHunger >= 0 && Mathf.Abs(hunger - _prevHunger) > 5f)
			PulseLabel(_hungerLabel);
		if (_prevThirst >= 0 && Mathf.Abs(thirst - _prevThirst) > 5f)
			PulseLabel(_thirstLabel);
		if (_prevRest >= 0 && Mathf.Abs(rest - _prevRest) > 5f)
			PulseLabel(_restLabel);
		if (_prevMood >= 0 && Mathf.Abs(mood - _prevMood) > 5f)
			PulseLabel(_moodLabel);

		_prevHunger = hunger;
		_prevThirst = thirst;
		_prevRest = rest;
		_prevMood = mood;
	}

	public void RefreshTexts(Actor? player, int currentTurn)
	{
		Update(player, currentTurn, _root.Visible);
	}

	private static void ApplyNeedLabel(Label label, string needId, float value)
	{
		var name = NeedCatalog.GetNeedDisplayName(needId);
		var stageId = NeedCatalog.ResolveStageId(needId, value);
		var stageSuffix = string.IsNullOrEmpty(stageId)
			? string.Empty
			: $" ({NeedCatalog.GetThoughtDisplayName(stageId)})";
		label.Text = $"{name}: {value:0}{stageSuffix}";
		label.AddThemeColorOverride("font_color", ResolveNeedColor(needId, value));
	}

	private static void ApplyMoodLabel(Label label, float moodValue, bool visible)
	{
		var name = NeedCatalog.GetNeedDisplayName(NeedIds.Mood);
		label.Text = visible ? $"{name}: {moodValue:0}" : $"{name}: --";
		label.AddThemeColorOverride("font_color", visible ? ResolveMoodColor(moodValue) : UIColors.TextDim);
	}

	private static void ApplyNeedRowIcon(TextureRect icon, string needId, float value)
	{
		var tex = PlaceholderUiIconCatalog.ResolveNeedRowIcon(needId, value);
		if (tex != null)
		{
			icon.Texture = tex;
			icon.Visible = true;
			icon.Modulate = Colors.White;
		}
		else
		{
			icon.Texture = null;
			icon.Visible = false;
		}
	}

	private static void ApplyMoodRowIcon(TextureRect icon, bool moodAllowed)
	{
		var tex = PlaceholderUiIconCatalog.ResolveMoodRowIcon(moodAllowed);
		if (tex != null)
		{
			icon.Texture = tex;
			icon.Visible = true;
			icon.Modulate = Colors.White;
		}
		else
		{
			icon.Texture = null;
			icon.Visible = false;
		}
	}

	private static void AnimateBar(ProgressBar bar, float targetValue, float prevValue, Color color)
	{
		var fillStyle = new StyleBoxFlat
		{
			BgColor = color,
			CornerRadiusTopLeft = 3,
			CornerRadiusTopRight = 3,
			CornerRadiusBottomLeft = 3,
			CornerRadiusBottomRight = 3,
		};
		bar.AddThemeStyleboxOverride("fill", fillStyle);

		if (prevValue < 0 || Mathf.Abs(targetValue - prevValue) < 0.5f)
		{
			bar.Value = targetValue;
			return;
		}

		var tween = bar.CreateTween();
		tween.SetTrans(Tween.TransitionType.Quad);
		tween.SetEase(Tween.EaseType.Out);
		tween.TweenProperty(bar, "value", (double)targetValue, 0.4f);
	}

	private static void PulseLabel(Label label)
	{
		var tween = label.CreateTween();
		tween.TweenProperty(label, "modulate", new Color(1.3f, 1.3f, 1.3f, 1f), 0.15f);
		tween.TweenProperty(label, "modulate", new Color(1f, 1f, 1f, 1f), 0.25f);
	}

	private static Color ResolveNeedColor(string needId, float value)
	{
		var warning = NeedCatalog.GetNeed(needId)?.WarningThreshold ?? 35f;
		if (value <= warning)
			return UIColors.TextWarning;
		if (value <= warning + 20f)
			return UIColors.TextEquipped;
		return UIColors.TextNormal;
	}

	private static Color ResolveMoodColor(float moodValue)
	{
		if (moodValue <= 35f)
			return UIColors.TextWarning;
		if (moodValue >= 65f)
			return UIColors.TextMoodGood;
		return UIColors.TextNormal;
	}
}
