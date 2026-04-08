using System;
using System.Globalization;
using System.Linq;
using Godot;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Module.Panel;

namespace MiniRPG.Module;

public enum ThreatHudMode
{
	Hidden,
	Suspicious,
	Combat,
	Search,
}

public readonly record struct ThreatHudSnapshot(ThreatHudMode Mode, int Count, string PrimaryName)
{
	public static ThreatHudSnapshot Hidden => new(ThreatHudMode.Hidden, 0, string.Empty);
}

public sealed class ThreatHudModule
{
	private const double FadeInSeconds = 0.12;
	private const double FadeDelaySeconds = 1.2;
	private const double FadeOutSeconds = 0.35;

	private readonly PanelContainer _root;
	private readonly StyleBoxFlat _panelStyle;
	private readonly Label _modeLabel;
	private readonly Label _countLabel;
	private readonly Label _summaryLabel;

	private ThreatHudSnapshot _displaySnapshot = ThreatHudSnapshot.Hidden;
	private double _hideDelayRemaining;
	private float _alpha;

	public ThreatHudModule(PanelContainer root)
	{
		_root = root;
		_panelStyle = (StyleBoxFlat)_root.GetThemeStylebox("panel");
		var vbox = _root.GetNode<VBoxContainer>("Margin/VBox");
		var header = vbox.GetNode<HBoxContainer>("Header");
		_modeLabel = header.GetNode<Label>("Mode");
		_countLabel = header.GetNode<Label>("Count");
		_summaryLabel = vbox.GetNode<Label>("Summary");
		HideImmediate();
	}

	public static PanelContainer CreateControl(Theme? theme)
	{
		var root = new PanelContainer
		{
			Name = "ThreatHud",
			Visible = false,
			Theme = theme,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			FocusMode = Control.FocusModeEnum.None,
			AnchorLeft = 0.5f,
			AnchorTop = 0f,
			AnchorRight = 0.5f,
			AnchorBottom = 0f,
			OffsetLeft = -188f,
			OffsetTop = 14f,
			OffsetRight = 188f,
			OffsetBottom = 82f,
			ZIndex = 60,
		};

		var style = new StyleBoxFlat
		{
			BgColor = new Color(UIColors.PanelBg, 0.92f),
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

		var margin = new MarginContainer
		{
			Name = "Margin",
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_top", 10);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_bottom", 10);
		root.AddChild(margin);

		var vbox = new VBoxContainer
		{
			Name = "VBox",
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		vbox.AddThemeConstantOverride("separation", 2);
		margin.AddChild(vbox);

		var header = new HBoxContainer
		{
			Name = "Header",
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		vbox.AddChild(header);

		var modeLabel = new Label
		{
			Name = "Mode",
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		header.AddChild(modeLabel);

		var countLabel = new Label
		{
			Name = "Count",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			HorizontalAlignment = HorizontalAlignment.Right,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		header.AddChild(countLabel);

		var summaryLabel = new Label
		{
			Name = "Summary",
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		vbox.AddChild(summaryLabel);

		return root;
	}

	public static ThreatHudSnapshot BuildSnapshot(GameState state)
	{
		var player = ActorModule.GetPlayer(state);
		if (player == null)
			return ThreatHudSnapshot.Hidden;

		var activeThreats = state.Actors.Values
			.Where(actor =>
				actor.Id != player.Id
				&& actor.Z == player.Z
				&& actor.BrainId != null
				&& !CombatModule.IsDead(actor)
				&& FactionRelation.IsHostile(actor.Faction, player.Faction)
				&& actor.AwarenessState != AwarenessState.Idle)
			.ToList();
		if (activeThreats.Count == 0)
			return ThreatHudSnapshot.Hidden;

		var mode = ResolveMode(activeThreats);
		var primary = activeThreats
			.Where(actor => MatchesMode(actor, mode))
			.OrderBy(actor => Math.Abs(actor.X - player.X) + Math.Abs(actor.Y - player.Y))
			.ThenBy(actor => actor.Id, StringComparer.Ordinal)
			.FirstOrDefault();

		return new ThreatHudSnapshot(
			mode,
			activeThreats.Count,
			primary == null ? string.Empty : IdentificationModule.GetActorDisplayName(state, primary));
	}

	public void Update(ThreatHudSnapshot snapshot, double delta, bool suppressed)
	{
		if (suppressed)
		{
			HideImmediate();
			return;
		}

		if (snapshot.Mode != ThreatHudMode.Hidden)
		{
			if (!_displaySnapshot.Equals(snapshot))
			{
				_displaySnapshot = snapshot;
				RefreshTexts();
			}

			_hideDelayRemaining = 0;
			_root.Visible = true;
			_alpha = Mathf.MoveToward(_alpha, 1f, (float)(delta / FadeInSeconds));
			ApplyAlpha();
			return;
		}

		if (_displaySnapshot.Mode == ThreatHudMode.Hidden)
		{
			HideImmediate();
			return;
		}

		_root.Visible = true;
		if (_hideDelayRemaining <= 0)
			_hideDelayRemaining = FadeDelaySeconds;

		if (_hideDelayRemaining > 0)
		{
			_hideDelayRemaining = Math.Max(0, _hideDelayRemaining - delta);
			ApplyAlpha();
			return;
		}

		_alpha = Mathf.MoveToward(_alpha, 0f, (float)(delta / FadeOutSeconds));
		if (_alpha <= 0.01f)
		{
			HideImmediate();
			return;
		}

		ApplyAlpha();
	}

	public void RefreshTexts()
	{
		if (_displaySnapshot.Mode == ThreatHudMode.Hidden)
		{
			_modeLabel.Text = string.Empty;
			_countLabel.Text = string.Empty;
			_summaryLabel.Text = string.Empty;
			return;
		}

		var accent = ResolveAccent(_displaySnapshot.Mode);
		_panelStyle.BorderColor = accent;
		_modeLabel.AddThemeColorOverride("font_color", accent);
		_countLabel.AddThemeColorOverride("font_color", accent);
		_summaryLabel.AddThemeColorOverride("font_color", UIColors.TextNormal);

		_modeLabel.Text = _displaySnapshot.Mode switch
		{
			ThreatHudMode.Suspicious => LocalizationService.T("ui.threat_hud.mode.suspicious"),
			ThreatHudMode.Combat => LocalizationService.T("ui.threat_hud.mode.combat"),
			ThreatHudMode.Search => LocalizationService.T("ui.threat_hud.mode.search"),
			_ => string.Empty,
		};
		_countLabel.Text = $"x{_displaySnapshot.Count.ToString(CultureInfo.InvariantCulture)}";

		if (_displaySnapshot.Count == 1 && !string.IsNullOrWhiteSpace(_displaySnapshot.PrimaryName))
		{
			_summaryLabel.Text = LocalizationService.T(
				"ui.threat_hud.summary.single",
				("name", _displaySnapshot.PrimaryName));
			return;
		}

		if (!string.IsNullOrWhiteSpace(_displaySnapshot.PrimaryName))
		{
			_summaryLabel.Text = LocalizationService.T(
				"ui.threat_hud.summary.multiple",
				("count", _displaySnapshot.Count),
				("name", _displaySnapshot.PrimaryName));
			return;
		}

		_summaryLabel.Text = LocalizationService.T(
			"ui.threat_hud.summary.nearby",
			("count", _displaySnapshot.Count));
	}

	public void HideImmediate()
	{
		_displaySnapshot = ThreatHudSnapshot.Hidden;
		_hideDelayRemaining = 0;
		_alpha = 0f;
		ApplyAlpha();
		_root.Visible = false;
	}

	private void ApplyAlpha()
	{
		_root.Modulate = new Color(1f, 1f, 1f, _alpha);
	}

	private static ThreatHudMode ResolveMode(System.Collections.Generic.IReadOnlyCollection<Actor> threats)
	{
		if (threats.Any(actor => actor.AwarenessState == AwarenessState.Alerted))
			return ThreatHudMode.Combat;
		if (threats.Any(actor => actor.AwarenessState == AwarenessState.Searching))
			return ThreatHudMode.Search;
		if (threats.Any(actor => actor.AwarenessState == AwarenessState.Suspicious))
			return ThreatHudMode.Suspicious;
		return ThreatHudMode.Hidden;
	}

	private static bool MatchesMode(Actor actor, ThreatHudMode mode) => mode switch
	{
		ThreatHudMode.Combat => actor.AwarenessState == AwarenessState.Alerted,
		ThreatHudMode.Search => actor.AwarenessState == AwarenessState.Searching,
		ThreatHudMode.Suspicious => actor.AwarenessState == AwarenessState.Suspicious,
		_ => false,
	};

	private static Color ResolveAccent(ThreatHudMode mode) => mode switch
	{
		ThreatHudMode.Suspicious => UIColors.TextEquipped,
		ThreatHudMode.Combat => UIColors.TextWarning,
		ThreatHudMode.Search => UIColors.TextUtility,
		_ => UIColors.TextNormal,
	};
}
