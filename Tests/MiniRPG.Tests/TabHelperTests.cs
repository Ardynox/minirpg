using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Module.Panel;
using Xunit;

namespace MiniRPG.Tests;

public sealed class TabHelperTests : IDisposable
{
	private enum SampleTab
	{
		General,
		Controls,
		Session,
	}

	public void Dispose()
	{
		PanelButtonScaleRegistry.Bind(null);
	}

	[Fact]
	public void BuildTabButtons_CreatesButtonsInOrderAndInvokesCallback()
	{
		var tabBar = new HBoxContainer();
		var pressed = new List<SampleTab>();

		var buttons = TabHelper.BuildTabButtons(
			tabBar,
			["General", "Controls", "Session"],
			[SampleTab.General, SampleTab.Controls, SampleTab.Session],
			tab => pressed.Add(tab));

		Assert.Equal(3, buttons.Count);
		Assert.Equal(3, tabBar.GetChildCount());
		Assert.Equal("General", buttons[0].Text);
		Assert.Equal("Controls", buttons[1].Text);
		Assert.Equal("Session", buttons[2].Text);

		buttons[1].EmitSignal(BaseButton.SignalName.Pressed);

		Assert.Equal([SampleTab.Controls], pressed);
	}

	[Fact]
	public void BuildTabButtons_WithPanelId_TracksButtonsThroughRegistry()
	{
		var service = new PanelButtonScaleService();
		PanelButtonScaleRegistry.Bind(service);

		var tabBar = new HBoxContainer();
		var buttons = TabHelper.BuildTabButtons(
			tabBar,
			["One", "Two"],
			[SampleTab.General, SampleTab.Controls],
			_ => { },
			panelId: "settings");

		service.ApplyScale("settings", 2f);

		Assert.Equal(32, buttons[0].GetThemeFontSize("font_size"));
		Assert.Equal(32, buttons[1].GetThemeFontSize("font_size"));
	}

	[Fact]
	public void UpdateTabHighlight_OnlyMarksCurrentTabAsPressed()
	{
		var buttons = new[]
		{
			new Button(),
			new Button(),
			new Button(),
		};

		TabHelper.UpdateTabHighlight(
			buttons,
			[SampleTab.General, SampleTab.Controls, SampleTab.Session],
			SampleTab.Controls);

		Assert.False(buttons[0].ButtonPressed);
		Assert.True(buttons[1].ButtonPressed);
		Assert.False(buttons[2].ButtonPressed);
	}
}
