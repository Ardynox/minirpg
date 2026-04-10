using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MiniRPG.Module.Panel;
using Xunit;

namespace MiniRPG.Tests;

public sealed class TabHelperTests
{
	private enum SampleTab
	{
		General,
		Controls,
		Session,
	}

	[Fact]
	public void BuildTabPlan_PreservesOrderAndTracksPanelScaleWhenRequested()
	{
		var plans = InvokeBuildTabPlan(
			["General", "Controls", "Session"],
			[SampleTab.General, SampleTab.Controls, SampleTab.Session],
			"settings");

		Assert.Equal(3, plans.Count);
		Assert.Equal("General", plans[0].Label);
		Assert.Equal(SampleTab.General, plans[0].Tab);
		Assert.True(plans[0].TrackScale);
		Assert.Equal("Controls", plans[1].Label);
		Assert.Equal(SampleTab.Controls, plans[1].Tab);
		Assert.Equal("Session", plans[2].Label);
		Assert.Equal(SampleTab.Session, plans[2].Tab);
	}

	[Fact]
	public void CreatePressedHandler_InvokesCallbackForCapturedTab()
	{
		var pressed = new List<SampleTab>();

		var handler = InvokeCreatePressedHandler<SampleTab>(tab => pressed.Add(tab), SampleTab.Controls);
		handler();

		Assert.Equal([SampleTab.Controls], pressed);
	}

	[Fact]
	public void ShouldHighlight_OnlyMatchesCurrentTab()
	{
		Assert.False(InvokeShouldHighlight(SampleTab.General, SampleTab.Controls));
		Assert.True(InvokeShouldHighlight(SampleTab.Controls, SampleTab.Controls));
		Assert.False(InvokeShouldHighlight(SampleTab.Session, SampleTab.Controls));
	}

	private static IReadOnlyList<TabPlanView> InvokeBuildTabPlan(IReadOnlyList<string> labels, IReadOnlyList<SampleTab> tabs, string? panelId)
	{
		var method = typeof(TabHelper).GetMethod("BuildTabPlan", BindingFlags.Static | BindingFlags.NonPublic);
		Assert.NotNull(method);
		var rawPlans = Assert.IsAssignableFrom<IEnumerable>(method!.MakeGenericMethod(typeof(SampleTab)).Invoke(null, [labels, tabs, panelId]));
		var result = new List<TabPlanView>();
		foreach (var rawPlan in rawPlans)
		{
			Assert.NotNull(rawPlan);
			var type = rawPlan!.GetType();
			result.Add(new TabPlanView(
				(string)type.GetProperty("Label", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(rawPlan)!,
				(SampleTab)type.GetProperty("Tab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(rawPlan)!,
				(bool)type.GetProperty("TrackScale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(rawPlan)!));
		}

		return result;
	}

	private static Action InvokeCreatePressedHandler<TTab>(Action<TTab> onPressed, TTab tab)
		where TTab : struct
	{
		var method = typeof(TabHelper).GetMethod("CreatePressedHandler", BindingFlags.Static | BindingFlags.NonPublic);
		Assert.NotNull(method);
		return Assert.IsType<Action>(method!.MakeGenericMethod(typeof(TTab)).Invoke(null, [onPressed, tab])!);
	}

	private static bool InvokeShouldHighlight(SampleTab tab, SampleTab current)
	{
		var method = typeof(TabHelper).GetMethod("ShouldHighlight", BindingFlags.Static | BindingFlags.NonPublic);
		Assert.NotNull(method);
		return (bool)method!.MakeGenericMethod(typeof(SampleTab)).Invoke(null, [tab, current])!;
	}

	private readonly record struct TabPlanView(string Label, SampleTab Tab, bool TrackScale);
}
