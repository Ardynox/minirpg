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

	public void UnregisterPanel(string panelId)
	{
		_states.Remove(panelId);
	}

	public void TrackButton(string panelId, Button button)
	{
		if (!GodotObject.IsInstanceValid(button) || button.HasMeta(IgnoreMeta))
			return;

		var state = GetOrCreate(panelId);
		TryAddBaseline(state.Buttons, button, CaptureBaseline(button));

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
		state.Scale = NormalizeScale(scale);

		if (state.Panel != null && GodotObject.IsInstanceValid(state.Panel))
			TrackButtonsRecursive(panelId, state.Panel);

		var invalidButtons = CollectInvalidKeys(state.Buttons, GodotObject.IsInstanceValid);
		foreach (var (button, baseline) in state.Buttons)
		{
			if (!GodotObject.IsInstanceValid(button))
				continue;

			ApplyScale(button, baseline, state.Scale);
		}

		foreach (var button in invalidButtons)
			state.Buttons.Remove(button);
	}

	private void TrackButtonsRecursive(string panelId, Node node)
	{
		TraverseTree(node, GetChildNodes, child =>
		{
			if (child is Button button)
				TrackButton(panelId, button);
		});
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
		var metrics = ScaleMetrics(baseline.MinimumSize, baseline.FontSize, scale);
		button.CustomMinimumSize = metrics.MinimumSize;
		button.AddThemeFontSizeOverride("font_size", metrics.FontSize);
	}

	private static float NormalizeScale(float scale) => Math.Max(0.5f, scale);

	private static bool TryAddBaseline<TKey, TValue>(Dictionary<TKey, TValue> tracked, TKey key, TValue value)
		where TKey : notnull
	{
		if (tracked.ContainsKey(key))
			return false;

		tracked[key] = value;
		return true;
	}

	private static List<TKey> CollectInvalidKeys<TKey, TValue>(Dictionary<TKey, TValue> tracked, Func<TKey, bool> isValid)
		where TKey : notnull
	{
		var invalid = new List<TKey>();
		foreach (var (key, _) in tracked)
		{
			if (!isValid(key))
				invalid.Add(key);
		}

		return invalid;
	}

	private static ScaledButtonMetrics ScaleMetrics(Vector2 baselineMinimumSize, int baselineFontSize, float scale)
	{
		var normalizedScale = NormalizeScale(scale);
		return new ScaledButtonMetrics(
			new Vector2(
				baselineMinimumSize.X > 0f ? baselineMinimumSize.X * normalizedScale : 0f,
				baselineMinimumSize.Y > 0f ? baselineMinimumSize.Y * normalizedScale : 0f),
			Math.Max(1, (int)Math.Round(baselineFontSize * normalizedScale)));
	}

	private static void TraverseTree<TNode>(TNode node, Func<TNode, IEnumerable<TNode>> getChildren, Action<TNode> visitor)
	{
		foreach (var child in getChildren(node))
		{
			visitor(child);
			TraverseTree(child, getChildren, visitor);
		}
	}

	private static IEnumerable<Node> GetChildNodes(Node node)
	{
		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
				yield return childNode;
		}
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

	private readonly record struct ScaledButtonMetrics(Vector2 MinimumSize, int FontSize);
	private readonly record struct ButtonBaseline(Vector2 MinimumSize, int FontSize);
}
