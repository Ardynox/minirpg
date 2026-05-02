using System;
using System.IO;
using Xunit;

namespace MiniRPG.Tests;

public sealed class ListPanelBaseTests
{
	[Fact]
	public void BaseClass_KeepsPressedAndGuiInputRoutingHooks()
	{
		var source = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Module", "Panel", "ListPanelBase.cs"));

		Assert.Contains("row.Pressed += () => OnRowPressed(idx);", source);
		Assert.Contains("row.GuiInput += ev => HandleRowGuiInput(ev, idx);", source);
		Assert.Contains("protected virtual void HandleRowGuiInput(InputEvent ev, int index) { }", source);
	}

	[Theory]
	[InlineData("ChestPanelModule.cs")]
	[InlineData("GroundPanelModule.cs")]
	[InlineData("TradePanelModule.cs")]
	public void RowInputPanels_UseGuiInputHookInsteadOfCreateRowOverride(string fileName)
	{
		var source = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Module", "Panel", fileName));

		Assert.DoesNotContain("protected override Button CreateRow", source);
		Assert.Contains("protected override void HandleRowGuiInput(InputEvent ev, int index)", source);
	}

	private static string ResolveRepoRoot()
	{
		var current = new DirectoryInfo(AppContext.BaseDirectory);
		while (current != null)
		{
			if (File.Exists(Path.Combine(current.FullName, "project.godot")))
				return current.FullName;

			current = current.Parent;
		}

		throw new Xunit.Sdk.XunitException("Failed to locate repository root from test base directory.");
	}
}
