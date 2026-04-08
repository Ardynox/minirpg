using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using MiniRPG.Module.Panel;

namespace MiniRPG.Module;

public sealed class HealthAlertsModule
{
	private readonly PanelContainer _root;
	private readonly Label[] _labels;
	private GameState? _cachedState;
	private Actor? _cachedActor;

	public HealthAlertsModule(PanelContainer root)
	{
		_root = root;
		var vbox = _root.GetNode<VBoxContainer>("Margin/VBox");
		_labels =
		[
			vbox.GetNode<Label>("Alert1"),
			vbox.GetNode<Label>("Alert2"),
			vbox.GetNode<Label>("Alert3"),
			vbox.GetNode<Label>("Alert4"),
			vbox.GetNode<Label>("Alert5"),
			vbox.GetNode<Label>("Alert6"),
		];
		HideImmediate();
	}

	public static PanelContainer CreateControl(Theme? theme)
	{
		var root = new PanelContainer
		{
			Name = "HealthAlerts",
			Visible = false,
			Theme = theme,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			FocusMode = Control.FocusModeEnum.None,
			AnchorLeft = 1f,
			AnchorTop = 0f,
			AnchorRight = 1f,
			AnchorBottom = 0f,
			OffsetLeft = -220f,
			OffsetTop = 114f,
			OffsetRight = -14f,
			OffsetBottom = 270f,
			ZIndex = 57,
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

		for (var i = 1; i <= 6; i++)
		{
			vbox.AddChild(new Label
			{
				Name = $"Alert{i.ToString(CultureInfo.InvariantCulture)}",
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Visible = false,
			});
		}

		return root;
	}

	public static IReadOnlyList<string> DescribeAlerts(Actor actor) =>
		BuildAlerts(actor)
			.Select(static alert => alert.Text)
			.ToList();

	public void Update(GameState state, Actor? player, int currentTurn, bool visible)
	{
		_cachedState = state;
		_cachedActor = player;
		if (!visible || player == null)
		{
			HideImmediate();
			return;
		}

		RenderAlerts(BuildAlerts(player));
	}

	public void RefreshTexts()
	{
		if (_cachedState == null || _cachedActor == null)
		{
			HideImmediate();
			return;
		}

		RenderAlerts(BuildAlerts(_cachedActor));
	}

	public void HideImmediate()
	{
		_root.Visible = false;
		foreach (var label in _labels)
		{
			label.Visible = false;
			label.Text = string.Empty;
		}
	}

	private void RenderAlerts(IReadOnlyList<(string Text, Color Color)> alerts)
	{
		if (alerts.Count == 0)
		{
			HideImmediate();
			return;
		}

		_root.Visible = true;
		for (var i = 0; i < _labels.Length; i++)
		{
			if (i < alerts.Count)
			{
				_labels[i].Visible = true;
				_labels[i].Text = alerts[i].Text;
				_labels[i].AddThemeColorOverride("font_color", alerts[i].Color);
			}
			else
			{
				_labels[i].Visible = false;
				_labels[i].Text = string.Empty;
			}
		}
	}

	private static List<(string Text, Color Color)> BuildAlerts(Actor actor)
	{
		var alerts = new List<(int Priority, string Text, Color Color)>();
		var infection = actor.HealthConditions.FirstOrDefault(condition =>
			string.Equals(condition.Id, HealthConditionIds.Infection, System.StringComparison.Ordinal));
		var hypothermia = actor.HealthConditions.FirstOrDefault(condition =>
			string.Equals(condition.Id, HealthConditionIds.Hypothermia, System.StringComparison.Ordinal));
		var heatstroke = actor.HealthConditions.FirstOrDefault(condition =>
			string.Equals(condition.Id, HealthConditionIds.Heatstroke, System.StringComparison.Ordinal));
		var onFire = actor.HealthConditions.FirstOrDefault(condition =>
			string.Equals(condition.Id, HealthConditionIds.OnFire, System.StringComparison.Ordinal));
		var fatalCause = HealthSystem.GetFatalCause(actor);

		if (fatalCause != null || actor.BloodLossValue >= 85f || (infection?.Severity ?? 0f) >= 90f)
		{
			var reason = fatalCause switch
			{
				"death_blood_loss" => HealthCatalog.GetConditionDisplayName(HealthConditionIds.BloodLoss),
				"death_infection" => HealthCatalog.GetConditionDisplayName(HealthConditionIds.Infection),
				_ => LocalizationService.TOrFallback("ui.health.alert.critical", "critical"),
			};
			alerts.Add((
				0,
				LocalizationService.TOrFallback("ui.health.alert.dying", "Dying: {reason}", ("reason", reason)),
				UIColors.TextWarning));
		}

		var temperatureCondition = (hypothermia?.Severity ?? 0f) >= (heatstroke?.Severity ?? 0f)
			? hypothermia
			: heatstroke;
		if ((temperatureCondition?.Severity ?? 0f) >= 20f)
		{
			var color = string.Equals(temperatureCondition!.Id, HealthConditionIds.Hypothermia, System.StringComparison.Ordinal)
				? UIColors.TextUtility
				: UIColors.TextEquipped;
			alerts.Add((1, $"{HealthCatalog.GetConditionDisplayName(temperatureCondition.Id)} {temperatureCondition.Severity:0.#}", color));
		}

		if ((onFire?.Severity ?? 0f) > 0f)
			alerts.Add((1, $"{HealthCatalog.GetConditionDisplayName(HealthConditionIds.OnFire)} {onFire!.Severity:0.#}", UIColors.TextWarning));

		if ((infection?.Severity ?? 0f) >= 8f)
			alerts.Add((2, $"{HealthCatalog.GetConditionDisplayName(HealthConditionIds.Infection)} {infection!.Severity:0.#}", UIColors.TextEquipped));
		if (actor.BloodLossValue >= 8f)
			alerts.Add((3, $"{LocalizationService.TOrFallback("ui.health.alert.bleeding", "Bleeding")} {actor.BloodLossValue:0.#}", UIColors.TextWarning));
		if (actor.PainValue >= 70f)
			alerts.Add((4, $"{LocalizationService.TOrFallback("ui.health.alert.extreme_pain", "Extreme pain")} {actor.PainValue:0.#}", UIColors.TextWarning));
		if (actor.WetnessValue >= 70f)
			alerts.Add((5, $"{LocalizationService.TOrFallback("ui.health.alert.soaked", "Severe wetness")} {actor.WetnessValue:0.#}", UIColors.TextUtility));

		return alerts
			.OrderBy(static entry => entry.Priority)
			.Select(static entry => (entry.Text, entry.Color))
			.ToList();
	}
}
