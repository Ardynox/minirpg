using System;
using System.Collections;
using System.Reflection;
using Godot;
using MiniRPG.Module.Panel;
using Xunit;

namespace MiniRPG.Tests;

public sealed class PanelButtonScaleServiceTests : IDisposable
{
	public void Dispose()
	{
		PanelButtonScaleRegistry.Bind(null);
	}

	[Fact]
	public void RegisterPanel_TracksNestedButtonsUsingExistingScale()
	{
		var service = new PanelButtonScaleService();
		service.ApplyScale("hud", 2f);

		var panel = new PanelContainer();
		var wrapper = new VBoxContainer();
		panel.AddChild(wrapper);

		var primary = CreateButton(new Vector2(10f, 20f), 12);
		var secondary = CreateButton(new Vector2(8f, 14f), 10);
		wrapper.AddChild(primary);
		wrapper.AddChild(secondary);

		service.RegisterPanel("hud", panel);

		Assert.Equal(new Vector2(20f, 40f), primary.CustomMinimumSize);
		Assert.Equal(24, primary.GetThemeFontSize("font_size"));
		Assert.Equal(new Vector2(16f, 28f), secondary.CustomMinimumSize);
		Assert.Equal(20, secondary.GetThemeFontSize("font_size"));
	}

	[Fact]
	public void TrackButton_DoesNotOverwriteBaselineWhenTrackedAgain()
	{
		var service = new PanelButtonScaleService();
		var button = CreateButton(new Vector2(10f, 20f), 12);

		service.TrackButton("inventory", button);
		button.CustomMinimumSize = new Vector2(99f, 99f);
		button.AddThemeFontSizeOverride("font_size", 30);

		service.TrackButton("inventory", button);
		service.ApplyScale("inventory", 2f);

		Assert.Equal(new Vector2(20f, 40f), button.CustomMinimumSize);
		Assert.Equal(24, button.GetThemeFontSize("font_size"));
	}

	[Fact]
	public void ApplyScale_PrunesInvalidButtons()
	{
		var service = new PanelButtonScaleService();
		var button = CreateButton(new Vector2(12f, 18f), 11);

		service.TrackButton("status", button);
		Assert.Equal(1, GetTrackedButtonCount(service, "status"));

		button.Free();
		Assert.False(GodotObject.IsInstanceValid(button));

		service.ApplyScale("status", 1.5f);

		Assert.Equal(0, GetTrackedButtonCount(service, "status"));
	}

	private static Button CreateButton(Vector2 minimumSize, int fontSize)
	{
		var button = new Button
		{
			CustomMinimumSize = minimumSize,
		};
		button.AddThemeFontSizeOverride("font_size", fontSize);
		return button;
	}

	private static int GetTrackedButtonCount(PanelButtonScaleService service, string panelId)
	{
		var statesField = typeof(PanelButtonScaleService).GetField("_states", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(statesField);

		var states = Assert.IsAssignableFrom<IDictionary>(statesField!.GetValue(service));
		var state = states[panelId];
		Assert.NotNull(state);

		var buttonsProperty = state!.GetType().GetProperty("Buttons", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		Assert.NotNull(buttonsProperty);

		var buttons = Assert.IsAssignableFrom<IDictionary>(buttonsProperty!.GetValue(state));
		return buttons.Count;
	}
}
