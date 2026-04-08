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

	public NeedsHudModule(PanelContainer root)
	{
		_root = root;
		var vbox = _root.GetNode<VBoxContainer>("Margin/VBox");
		_hungerLabel = vbox.GetNode<Label>("Hunger");
		_restLabel = vbox.GetNode<Label>("Rest");
		_moodLabel = vbox.GetNode<Label>("Mood");
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
			OffsetLeft = -220f,
			OffsetTop = 14f,
			OffsetRight = -14f,
			OffsetBottom = 106f,
			ZIndex = 58,
		};

		var style = new StyleBoxFlat
		{
			BgColor = new Color(UIColors.PanelBg, 0.88f),
			BorderColor = UIColors.IdleBorder,
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 8,
			CornerRadiusTopRight = 8,
			CornerRadiusBottomLeft = 8,
			CornerRadiusBottomRight = 8,
		};
		root.AddThemeStyleboxOverride("panel", style);

		var margin = new MarginContainer { Name = "Margin", MouseFilter = Control.MouseFilterEnum.Ignore };
		margin.AddThemeConstantOverride("margin_left", 10);
		margin.AddThemeConstantOverride("margin_top", 8);
		margin.AddThemeConstantOverride("margin_right", 10);
		margin.AddThemeConstantOverride("margin_bottom", 8);
		root.AddChild(margin);

		var vbox = new VBoxContainer { Name = "VBox", MouseFilter = Control.MouseFilterEnum.Ignore };
		vbox.AddThemeConstantOverride("separation", 3);
		margin.AddChild(vbox);

		vbox.AddChild(new Label { Name = "Hunger", MouseFilter = Control.MouseFilterEnum.Ignore });
		vbox.AddChild(new Label { Name = "Rest", MouseFilter = Control.MouseFilterEnum.Ignore });
		vbox.AddChild(new Label { Name = "Mood", MouseFilter = Control.MouseFilterEnum.Ignore });
		return root;
	}

	public void Update(Actor? player, int currentTurn, bool visible)
	{
		if (!visible || player == null)
		{
			_root.Visible = false;
			return;
		}

		_root.Visible = true;
		ApplyNeedLabel(_hungerLabel, NeedIds.Hunger, NeedSystem.GetNeedValueSnapshot(player, NeedIds.Hunger));
		ApplyNeedLabel(_restLabel, NeedIds.Rest, NeedSystem.GetNeedValueSnapshot(player, NeedIds.Rest));
		ApplyMoodLabel(_moodLabel, player.MoodValue, NeedCatalog.GetProfileForActor(player).AllowMood);
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
