using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Panel;

public static class PanelButtonScaleRegistry
{
	private static PanelButtonScaleService? _service;

	public static void Bind(PanelButtonScaleService? service)
	{
		_service = service;
	}

	public static void Track(string panelId, Button button)
	{
		_service?.TrackButton(panelId, button);
	}

	public static void Reapply(string panelId, Button button)
	{
		_service?.ReapplyButton(panelId, button);
	}
}

public sealed class PanelButtonScaleService
{
	private const string IgnoreMeta = "__panel_scale_ignore";

	private readonly Dictionary<string, PanelButtonState> _states = [];

	public static void MarkIgnored(Control control)
	{
		control.SetMeta(IgnoreMeta, true);
	}

	public void RegisterPanel(string panelId, PanelContainer panel)
	{
		var state = GetOrCreate(panelId);
		state.Panel = panel;
		TrackButtonsRecursive(panelId, panel);
	}

	public void TrackButton(string panelId, Button button)
	{
		if (!GodotObject.IsInstanceValid(button) || button.HasMeta(IgnoreMeta))
			return;

		var state = GetOrCreate(panelId);
		if (!state.Buttons.ContainsKey(button))
			state.Buttons[button] = CaptureBaseline(button);

		ApplyScale(button, state.Buttons[button], state.Scale);
	}

	public void ReapplyButton(string panelId, Button button)
	{
		if (!_states.TryGetValue(panelId, out var state))
			return;

		TrackButton(panelId, button);
		if (state.Buttons.TryGetValue(button, out var baseline))
			ApplyScale(button, baseline, state.Scale);
	}

	public void ApplyScale(string panelId, float scale)
	{
		var state = GetOrCreate(panelId);
		state.Scale = Math.Max(0.5f, scale);

		if (state.Panel != null && GodotObject.IsInstanceValid(state.Panel))
			TrackButtonsRecursive(panelId, state.Panel);

		var invalidButtons = new List<Button>();
		foreach (var (button, baseline) in state.Buttons)
		{
			if (!GodotObject.IsInstanceValid(button))
			{
				invalidButtons.Add(button);
				continue;
			}

			ApplyScale(button, baseline, state.Scale);
		}

		foreach (var button in invalidButtons)
			state.Buttons.Remove(button);
	}

	private void TrackButtonsRecursive(string panelId, Node node)
	{
		foreach (var child in node.GetChildren())
		{
			if (child is Button button)
				TrackButton(panelId, button);

			TrackButtonsRecursive(panelId, child);
		}
	}

	private static ButtonBaseline CaptureBaseline(Button button)
	{
		var minimumSize = button.CustomMinimumSize;
		var combinedMinimum = button.GetCombinedMinimumSize();
		var baselineSize = new Vector2(
			minimumSize.X > 0f ? minimumSize.X : combinedMinimum.X,
			minimumSize.Y > 0f ? minimumSize.Y : combinedMinimum.Y);
		var fontSize = button.GetThemeFontSize("font_size");
		if (fontSize <= 0)
			fontSize = 16;

		return new ButtonBaseline(baselineSize, fontSize);
	}

	private static void ApplyScale(Button button, ButtonBaseline baseline, float scale)
	{
		button.CustomMinimumSize = new Vector2(
			baseline.MinimumSize.X > 0f ? baseline.MinimumSize.X * scale : 0f,
			baseline.MinimumSize.Y > 0f ? baseline.MinimumSize.Y * scale : 0f);
		button.AddThemeFontSizeOverride("font_size", Math.Max(1, (int)Math.Round(baseline.FontSize * scale)));
	}

	private PanelButtonState GetOrCreate(string panelId)
	{
		if (_states.TryGetValue(panelId, out var state))
			return state;

		state = new PanelButtonState();
		_states[panelId] = state;
		return state;
	}

	private sealed class PanelButtonState
	{
		public PanelContainer? Panel;
		public float Scale = 1f;
		public Dictionary<Button, ButtonBaseline> Buttons { get; } = [];
	}

	private readonly record struct ButtonBaseline(Vector2 MinimumSize, int FontSize);
}
