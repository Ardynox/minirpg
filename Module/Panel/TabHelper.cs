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
		var result = new List<Button>(labels.Count);
		for (var i = 0; i < labels.Count; i++)
		{
			var btn = new Button
			{
				Text = labels[i],
				ToggleMode = true,
				SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
				FocusMode = Control.FocusModeEnum.None,
			};
			var tab = tabs[i];
			btn.Pressed += () => onPressed(tab);
			tabBar.AddChild(btn);
			if (!string.IsNullOrEmpty(panelId))
				PanelButtonScaleRegistry.Track(panelId, btn);
			result.Add(btn);
		}
		return result;
	}

	public static void UpdateTabHighlight<TTab>(IReadOnlyList<Button> buttons, IReadOnlyList<TTab> tabs, TTab current)
		where TTab : struct
	{
		for (var i = 0; i < buttons.Count && i < tabs.Count; i++)
			buttons[i].ButtonPressed = EqualityComparer<TTab>.Default.Equals(tabs[i], current);
	}
}
