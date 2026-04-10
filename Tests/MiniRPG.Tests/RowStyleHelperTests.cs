using System.Reflection;
using MiniRPG.Module.Panel;
using Xunit;

namespace MiniRPG.Tests;

public sealed class RowStyleHelperTests
{
	[Fact]
	public void ResolveThemeVariation_ChoosesExpectedThemeVariants()
	{
		Assert.Equal("RowButton", InvokeResolveThemeVariation(selected: false, hovered: false, isContainer: false, transparentBg: true));
		Assert.Equal("HoveredRowButton", InvokeResolveThemeVariation(selected: false, hovered: true, isContainer: false, transparentBg: true));
		Assert.Equal("SelectedRowButton", InvokeResolveThemeVariation(selected: true, hovered: false, isContainer: false, transparentBg: true));
		Assert.Equal("ContainerRowButton", InvokeResolveThemeVariation(selected: false, hovered: false, isContainer: true, transparentBg: true));
		Assert.Equal("OpaqueRowButton", InvokeResolveThemeVariation(selected: false, hovered: false, isContainer: false, transparentBg: false));
		Assert.Equal("OpaqueHoveredRowButton", InvokeResolveThemeVariation(selected: false, hovered: true, isContainer: false, transparentBg: false));
		Assert.Equal("OpaqueSelectedRowButton", InvokeResolveThemeVariation(selected: true, hovered: false, isContainer: false, transparentBg: false));
	}

	[Fact]
	public void GetVisibleScrollPosition_AdjustsTopAndBottomOverflow()
	{
		Assert.Equal(5, InvokeGetVisibleScrollPosition(rowTop: 5f, rowHeight: 20f, scrollTop: 20, viewportHeight: 100f));
		Assert.Equal(80, InvokeGetVisibleScrollPosition(rowTop: 150f, rowHeight: 30f, scrollTop: 20, viewportHeight: 100f));
		Assert.Null(InvokeGetVisibleScrollPosition(rowTop: 40f, rowHeight: 20f, scrollTop: 20, viewportHeight: 100f));
	}

	private static string InvokeResolveThemeVariation(bool selected, bool hovered, bool isContainer, bool transparentBg)
	{
		var method = typeof(RowStyleHelper).GetMethod("ResolveThemeVariation", BindingFlags.Static | BindingFlags.NonPublic);
		Assert.NotNull(method);
		return (string)method!.Invoke(null, [selected, hovered, isContainer, transparentBg])!;
	}

	private static int? InvokeGetVisibleScrollPosition(float rowTop, float rowHeight, int scrollTop, float viewportHeight)
	{
		var method = typeof(RowStyleHelper).GetMethod("GetVisibleScrollPosition", BindingFlags.Static | BindingFlags.NonPublic);
		Assert.NotNull(method);
		var result = method!.Invoke(null, [rowTop, rowHeight, scrollTop, viewportHeight]);
		return result == null ? null : (int)result;
	}
}
