using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using MiniRPG.Module.Panel;

namespace MiniRPG.Module;

public sealed class HealthAlertsModule
{
	private readonly PanelContainer _root;
	private readonly HBoxContainer[] _rows;
	private readonly TextureRect[] _icons;
	private readonly Label[] _labels;
	private GameState? _cachedState;
	private Actor? _cachedActor;

	public HealthAlertsModule(PanelContainer root)
	{
		_root = root;
		var vbox = _root.GetNode<VBoxContainer>("Margin/VBox");
		_rows =
		[
			vbox.GetNode<HBoxContainer>("Alert1"),
			vbox.GetNode<HBoxContainer>("Alert2"),
			vbox.GetNode<HBoxContainer>("Alert3"),
			vbox.GetNode<HBoxContainer>("Alert4"),
			vbox.GetNode<HBoxContainer>("Alert5"),
			vbox.GetNode<HBoxContainer>("Alert6"),
		];
		_icons =
		[
			vbox.GetNode<TextureRect>("Alert1/Icon"),
			vbox.GetNode<TextureRect>("Alert2/Icon"),
			vbox.GetNode<TextureRect>("Alert3/Icon"),
			vbox.GetNode<TextureRect>("Alert4/Icon"),
			vbox.GetNode<TextureRect>("Alert5/Icon"),
			vbox.GetNode<TextureRect>("Alert6/Icon"),
		];
		_labels =
		[
			vbox.GetNode<Label>("Alert1/Text"),
			vbox.GetNode<Label>("Alert2/Text"),
			vbox.GetNode<Label>("Alert3/Text"),
			vbox.GetNode<Label>("Alert4/Text"),
			vbox.GetNode<Label>("Alert5/Text"),
			vbox.GetNode<Label>("Alert6/Text"),
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
			var row = new HBoxContainer
			{
				Name = $"Alert{i.ToString(CultureInfo.InvariantCulture)}",
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Visible = false,
			};
			row.AddThemeConstantOverride("separation", 4);

			var icon = new TextureRect
			{
				Name = "Icon",
				MouseFilter = Control.MouseFilterEnum.Ignore,
				CustomMinimumSize = new Vector2(22, 22),
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				Visible = false,
			};
			var label = new Label
			{
				Name = "Text",
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Visible = false,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			row.AddChild(icon);
			row.AddChild(label);
			vbox.AddChild(row);
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
		foreach (var row in _rows)
		{
			row.Visible = false;
		}

		foreach (var label in _labels)
		{
			label.Visible = false;
			label.Text = string.Empty;
		}

		foreach (var icon in _icons)
		{
			icon.Visible = false;
			icon.Texture = null;
		}
	}

	private void RenderAlerts(IReadOnlyList<(string Text, Color Color, Texture2D? Icon)> alerts)
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
				_rows[i].Visible = true;
				_labels[i].Visible = true;
				_labels[i].Text = alerts[i].Text;
				_labels[i].AddThemeColorOverride("font_color", alerts[i].Color);
				var tex = alerts[i].Icon;
				_icons[i].Texture = tex;
				_icons[i].Visible = tex != null;
			}
			else
			{
				_rows[i].Visible = false;
				_labels[i].Visible = false;
				_labels[i].Text = string.Empty;
				_icons[i].Visible = false;
				_icons[i].Texture = null;
			}
		}
	}

	private static Texture2D? ResolveDyingAlertIcon(string? fatalCause, Actor actor, HealthConditionState? infection)
	{
		var icon = fatalCause switch
		{
			"death_blood_loss" => PlaceholderUiIconCatalog.TryLoadTexture(
				PlaceholderUiIconCatalog.PathForCondition(HealthConditionIds.BloodLoss)),
			"death_infection" => PlaceholderUiIconCatalog.TryLoadTexture(
				PlaceholderUiIconCatalog.PathForCondition(HealthConditionIds.Infection)),
			_ => null,
		};
		if (icon != null)
			return icon;

		if (actor.BloodLossValue >= 85f)
		{
			var blood = PlaceholderUiIconCatalog.TryLoadTexture(
				PlaceholderUiIconCatalog.PathForCondition(HealthConditionIds.BloodLoss));
			if (blood != null)
				return blood;
		}

		if ((infection?.Severity ?? 0f) >= 90f)
			return PlaceholderUiIconCatalog.TryLoadTexture(
				PlaceholderUiIconCatalog.PathForCondition(HealthConditionIds.Infection));

		return null;
	}

	private static List<(string Text, Color Color, Texture2D? Icon)> BuildAlerts(Actor actor)
	{
		var alerts = new List<(int Priority, string Text, Color Color, Texture2D? Icon)>();
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
			var dyingIcon = ResolveDyingAlertIcon(fatalCause, actor, infection);
			alerts.Add((
				0,
				LocalizationService.TOrFallback("ui.health.alert.dying", "Dying: {reason}", ("reason", reason)),
				UIColors.TextWarning,
				dyingIcon));
		}

		var temperatureCondition = (hypothermia?.Severity ?? 0f) >= (heatstroke?.Severity ?? 0f)
			? hypothermia
			: heatstroke;
		if ((temperatureCondition?.Severity ?? 0f) >= 20f)
		{
			var color = string.Equals(temperatureCondition!.Id, HealthConditionIds.Hypothermia, System.StringComparison.Ordinal)
				? UIColors.TextUtility
				: UIColors.TextEquipped;
			var tempId = string.Equals(temperatureCondition.Id, HealthConditionIds.Hypothermia, System.StringComparison.Ordinal)
				? HealthConditionIds.Hypothermia
				: HealthConditionIds.Heatstroke;
			var tempIcon = PlaceholderUiIconCatalog.TryLoadTexture(PlaceholderUiIconCatalog.PathForCondition(tempId));
			alerts.Add((1, $"{HealthCatalog.GetConditionDisplayName(temperatureCondition.Id)} {temperatureCondition.Severity:0.#}", color, tempIcon));
		}

		if ((onFire?.Severity ?? 0f) > 0f)
		{
			var fireIcon = PlaceholderUiIconCatalog.TryLoadTexture(PlaceholderUiIconCatalog.PathForCondition(HealthConditionIds.OnFire));
			alerts.Add((1, $"{HealthCatalog.GetConditionDisplayName(HealthConditionIds.OnFire)} {onFire!.Severity:0.#}", UIColors.TextWarning, fireIcon));
		}

		if ((infection?.Severity ?? 0f) >= 8f)
		{
			var infIcon = PlaceholderUiIconCatalog.TryLoadTexture(PlaceholderUiIconCatalog.PathForCondition(HealthConditionIds.Infection));
			alerts.Add((2, $"{HealthCatalog.GetConditionDisplayName(HealthConditionIds.Infection)} {infection!.Severity:0.#}", UIColors.TextEquipped, infIcon));
		}

		if (actor.BloodLossValue >= 8f)
		{
			var bleedIcon = PlaceholderUiIconCatalog.TryLoadTexture(PlaceholderUiIconCatalog.PathForCondition(HealthConditionIds.BloodLoss));
			alerts.Add((3, $"{LocalizationService.TOrFallback("ui.health.alert.bleeding", "Bleeding")} {actor.BloodLossValue:0.#}", UIColors.TextWarning, bleedIcon));
		}

		if (actor.PainValue >= 70f)
			alerts.Add((4, $"{LocalizationService.TOrFallback("ui.health.alert.extreme_pain", "Extreme pain")} {actor.PainValue:0.#}", UIColors.TextWarning, null));

		if (actor.WetnessValue >= 70f)
			alerts.Add((5, $"{LocalizationService.TOrFallback("ui.health.alert.soaked", "Severe wetness")} {actor.WetnessValue:0.#}", UIColors.TextUtility, null));

		return alerts
			.OrderBy(static entry => entry.Priority)
			.Select(static entry => (entry.Text, entry.Color, entry.Icon))
			.ToList();
	}
}
