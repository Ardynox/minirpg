using Godot;
using MiniRPG.Module.Panel;
using Xunit;

namespace MiniRPG.Tests;

public sealed class RowStyleHelperTests
{
	[Fact]
	public void Apply_ChoosesExpectedThemeVariants()
	{
		var row = new Button();

		RowStyleHelper.Apply(row, selected: false, hovered: false, transparentBg: true);
		Assert.Equal("RowButton", row.ThemeTypeVariation);

		RowStyleHelper.Apply(row, selected: false, hovered: true, transparentBg: true);
		Assert.Equal("HoveredRowButton", row.ThemeTypeVariation);

		RowStyleHelper.Apply(row, selected: true, hovered: false, transparentBg: true);
		Assert.Equal("SelectedRowButton", row.ThemeTypeVariation);

		RowStyleHelper.Apply(row, selected: false, hovered: false, isContainer: true, transparentBg: true);
		Assert.Equal("ContainerRowButton", row.ThemeTypeVariation);

		RowStyleHelper.Apply(row, selected: false, hovered: false, transparentBg: false);
		Assert.Equal("OpaqueRowButton", row.ThemeTypeVariation);

		RowStyleHelper.Apply(row, selected: false, hovered: true, transparentBg: false);
		Assert.Equal("OpaqueHoveredRowButton", row.ThemeTypeVariation);

		RowStyleHelper.Apply(row, selected: true, hovered: false, transparentBg: false);
		Assert.Equal("OpaqueSelectedRowButton", row.ThemeTypeVariation);
	}

	[Fact]
	public void EnsureVisible_ScrollsUpAndDownToRevealRow()
	{
		var scroll = new ScrollContainer
		{
			Size = new Vector2(120f, 100f),
			ScrollVertical = 20,
		};
		var row = new Control
		{
			Position = new Vector2(0f, 5f),
			Size = new Vector2(90f, 20f),
		};

		RowStyleHelper.EnsureVisible(scroll, row);
		Assert.Equal(5, scroll.ScrollVertical);

		scroll.ScrollVertical = 20;
		row.Position = new Vector2(0f, 150f);
		row.Size = new Vector2(90f, 30f);

		RowStyleHelper.EnsureVisible(scroll, row);
		Assert.Equal(80, scroll.ScrollVertical);
	}
}
