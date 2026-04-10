using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Panel;

public static class TabHelper
{
	public static List<Button> BuildTabButtons<TTab>(
		HBoxContainer tabBar,
		IReadOnlyList<string> labels,
		IReadOnlyList<TTab> tabs,
		Action<TTab> onPressed,
		string? panelId = null)
		where TTab : struct
	{
		var plans = BuildTabPlan(labels, tabs, panelId);
		var result = new List<Button>(plans.Count);
		foreach (var plan in plans)
		{
			var btn = new Button
			{
				Text = plan.Label,
				ToggleMode = true,
				SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
				FocusMode = Control.FocusModeEnum.None,
				ThemeTypeVariation = "TabButton",
			};
			btn.Pressed += CreatePressedHandler(onPressed, plan.Tab);
			tabBar.AddChild(btn);
			if (plan.TrackScale)
				PanelButtonScaleRegistry.Track(panelId!, btn);
			result.Add(btn);
		}
		return result;
	}

	public static void UpdateTabHighlight<TTab>(IReadOnlyList<Button> buttons, IReadOnlyList<TTab> tabs, TTab current)
		where TTab : struct
	{
		for (var i = 0; i < buttons.Count && i < tabs.Count; i++)
			buttons[i].ButtonPressed = ShouldHighlight(tabs[i], current);
	}

	private static List<TabButtonPlan<TTab>> BuildTabPlan<TTab>(IReadOnlyList<string> labels, IReadOnlyList<TTab> tabs, string? panelId)
		where TTab : struct
	{
		var result = new List<TabButtonPlan<TTab>>(labels.Count);
		var trackScale = !string.IsNullOrEmpty(panelId);
		for (var i = 0; i < labels.Count; i++)
			result.Add(new TabButtonPlan<TTab>(labels[i], tabs[i], trackScale));
		return result;
	}

	private static Action CreatePressedHandler<TTab>(Action<TTab> onPressed, TTab tab)
		where TTab : struct =>
		() => onPressed(tab);

	private static bool ShouldHighlight<TTab>(TTab tab, TTab current)
		where TTab : struct =>
		EqualityComparer<TTab>.Default.Equals(tab, current);

	private readonly record struct TabButtonPlan<TTab>(string Label, TTab Tab, bool TrackScale);
}
