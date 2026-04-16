using Godot;
using MiniRPG.Core.Needs;
using MiniRPG.Module.Panel;

namespace MiniRPG.Module;

public sealed class NeedsHudModule
{
	private readonly PanelContainer _root;
	private readonly Label _hungerLabel;
	private readonly Label _restLabel;
	private readonly Label _moodLabel;
	private readonly ProgressBar _hungerBar;
	private readonly ProgressBar _restBar;
	private readonly ProgressBar _moodBar;

	private float _prevHunger = -1f;
	private float _prevRest = -1f;
	private float _prevMood = -1f;

	public NeedsHudModule(PanelContainer root)
	{
		_root = root;
		var vbox = _root.GetNode<VBoxContainer>("Margin/VBox");
		_hungerLabel = vbox.GetNode<Label>("HungerRow/Hunger");
		_restLabel = vbox.GetNode<Label>("RestRow/Rest");
		_moodLabel = vbox.GetNode<Label>("MoodRow/Mood");
		_hungerBar = vbox.GetNode<ProgressBar>("HungerRow/HungerBar");
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
			OffsetBottom = 130f,
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
		if (!visible || player == null)
		{
			_root.Visible = false;
			return;
		}

		_root.Visible = true;
		var hunger = NeedSystem.GetNeedValueSnapshot(player, NeedIds.Hunger);
		var rest = NeedSystem.GetNeedValueSnapshot(player, NeedIds.Rest);
		var mood = player.MoodValue;

		ApplyNeedLabel(_hungerLabel, NeedIds.Hunger, hunger);
		ApplyNeedLabel(_restLabel, NeedIds.Rest, rest);
		ApplyMoodLabel(_moodLabel, mood, NeedCatalog.GetProfileForActor(player).AllowMood);

		AnimateBar(_hungerBar, hunger, _prevHunger, ResolveNeedColor(NeedIds.Hunger, hunger));
		AnimateBar(_restBar, rest, _prevRest, ResolveNeedColor(NeedIds.Rest, rest));
		var moodAllowed = NeedCatalog.GetProfileForActor(player).AllowMood;
		AnimateBar(_moodBar, moodAllowed ? mood : 0, _prevMood, moodAllowed ? ResolveMoodColor(mood) : UIColors.TextDim);

		if (_prevHunger >= 0 && Mathf.Abs(hunger - _prevHunger) > 5f)
			PulseLabel(_hungerLabel);
		if (_prevRest >= 0 && Mathf.Abs(rest - _prevRest) > 5f)
			PulseLabel(_restLabel);
		if (_prevMood >= 0 && Mathf.Abs(mood - _prevMood) > 5f)
			PulseLabel(_moodLabel);

		_prevHunger = hunger;
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
		label.Text = $"{name}: {value:0}";
		label.AddThemeColorOverride("font_color", ResolveNeedColor(needId, value));
	}

	private static void ApplyMoodLabel(Label label, float moodValue, bool visible)
	{
		var name = NeedCatalog.GetNeedDisplayName(NeedIds.Mood);
		label.Text = visible ? $"{name}: {moodValue:0}" : $"{name}: --";
		label.AddThemeColorOverride("font_color", visible ? ResolveMoodColor(moodValue) : UIColors.TextDim);
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
