using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;

namespace MiniRPG.Module;

public readonly record struct TargetSummarySnapshot(
	bool Visible,
	string Name,
	AwarenessState AwarenessState,
	string CriticalText,
	bool IsCurrentTarget)
{
	public static TargetSummarySnapshot Hidden => new(false, string.Empty, AwarenessState.Idle, string.Empty, false);
}

public sealed class TargetSummaryHudModule
{
	private readonly PanelContainer _root;
	private readonly Label _nameLabel;
	private readonly Label _metaLabel;
	private TargetSummarySnapshot _snapshot = TargetSummarySnapshot.Hidden;

	public TargetSummaryHudModule(PanelContainer root)
	{
		_root = root;
		var vbox = _root.GetNode<VBoxContainer>("Margin/VBox");
		_nameLabel = vbox.GetNode<Label>("Name");
		_metaLabel = vbox.GetNode<Label>("Meta");
		HideImmediate();
	}

	public static PanelContainer CreateControl(Theme? theme)
	{
		var root = new PanelContainer
		{
			Name = "TargetSummaryHud",
			Visible = false,
			Theme = theme,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			FocusMode = Control.FocusModeEnum.None,
			AnchorLeft = 0.5f,
			AnchorTop = 0f,
			AnchorRight = 0.5f,
			AnchorBottom = 0f,
			OffsetLeft = -176f,
			OffsetTop = 88f,
			OffsetRight = 176f,
			OffsetBottom = 142f,
			ZIndex = 59,
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

		var margin = new MarginContainer
		{
			Name = "Margin",
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_top", 8);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_bottom", 8);
		root.AddChild(margin);

		var vbox = new VBoxContainer
		{
			Name = "VBox",
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		vbox.AddThemeConstantOverride("separation", 2);
		margin.AddChild(vbox);

		var nameLabel = new Label
		{
			Name = "Name",
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		vbox.AddChild(nameLabel);

		var metaLabel = new Label
		{
			Name = "Meta",
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		vbox.AddChild(metaLabel);

		return root;
	}

	public static TargetSummarySnapshot BuildSnapshot(GameState state, Actor? target, bool isCurrentTarget)
	{
		if (target == null)
			return TargetSummarySnapshot.Hidden;

		return new TargetSummarySnapshot(
			true,
			IdentificationModule.GetActorDisplayName(state, target),
			target.AwarenessState,
			BuildCriticalText(state, target),
			isCurrentTarget);
	}

	public void Update(TargetSummarySnapshot snapshot, bool suppressed)
	{
		if (suppressed || !snapshot.Visible)
		{
			HideImmediate();
			return;
		}

		_snapshot = snapshot;
		RefreshTexts();
		_root.Visible = true;
	}

	public void RefreshTexts()
	{
		if (!_snapshot.Visible)
			return;

		var accent = ResolveAccent(_snapshot.AwarenessState);
		_nameLabel.Text = _snapshot.Name;
		_nameLabel.AddThemeColorOverride("font_color", Colors.White);
		var currentPrefix = _snapshot.IsCurrentTarget
			? $"{LocalizationService.T("ui.target_hud.current")} | "
			: string.Empty;
		_metaLabel.Text = $"{currentPrefix}{LocalizeState(_snapshot.AwarenessState)} | {_snapshot.CriticalText}";
		_metaLabel.AddThemeColorOverride("font_color", accent);
	}

	public void HideImmediate()
	{
		_snapshot = TargetSummarySnapshot.Hidden;
		_root.Visible = false;
	}

	private static string BuildCriticalText(GameState state, Actor actor)
	{
		if (!IdentificationModule.IsActorIdentified(state, actor))
			return LocalizationService.T("ui.target_hud.critical.none");

		var vitalParts = actor.Limbs
			.Where(CombatModule.IsVitalLimb)
			.Select(limb => limb.Name)
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToList();
		if (vitalParts.Count > 0)
		{
			var parts = string.Join(", ", vitalParts.Take(2));
			return LocalizationService.T("ui.target_hud.critical.vital", ("parts", parts));
		}

		var torso = actor.Limbs.FirstOrDefault(limb => limb.BodyPart == BodyParts.Torso);
		if (torso != null)
		{
			return LocalizationService.T(
				"ui.target_hud.critical.torso",
				("current", torso.Durability),
				("max", torso.MaxDurability));
		}

		return LocalizationService.T("ui.target_hud.critical.none");
	}

	private static string LocalizeState(AwarenessState state) => state switch
	{
		AwarenessState.Suspicious => LocalizationService.T("ui.target_hud.state.suspicious"),
		AwarenessState.Alerted => LocalizationService.T("ui.target_hud.state.alerted"),
		AwarenessState.Searching => LocalizationService.T("ui.target_hud.state.searching"),
		_ => LocalizationService.T("ui.target_hud.state.idle"),
	};

	private static Color ResolveAccent(AwarenessState state) => state switch
	{
		AwarenessState.Suspicious => UIColors.TextEquipped,
		AwarenessState.Alerted => UIColors.TextWarning,
		AwarenessState.Searching => UIColors.TextUtility,
		_ => UIColors.TextDim,
	};
}
