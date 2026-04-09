using System;
using System.IO;
using Xunit;

namespace MiniRPG.Tests;

public sealed class PanelManagerTests
{
	[Fact]
	public void PanelManagerSource_ContainsLifecycleCleanupApis()
	{
		var source = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Module", "Panel", "PanelManager.cs"));

		Assert.Contains("public void Unregister(IPanel panel)", source);
		Assert.Contains("public void UnregisterPassive(PanelContainer node)", source);
		Assert.Contains("public void PruneInvalidPanels()", source);
		Assert.Contains("SwitchFocus(PopPreviousFocusable(), clearStack: false);", source);
	}

	[Fact]
	public void PanelManagerSource_ContainsNodeValidityGuards()
	{
		var source = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Module", "Panel", "PanelManager.cs"));

		Assert.Contains("private static bool IsNodeInvalid(PanelContainer node)", source);
		Assert.Contains("!GodotObject.IsInstanceValid(node) || node.IsQueuedForDeletion()", source);
		Assert.Contains("if (IsPanelInvalid(panel))", source);
		Assert.Contains("RegisterNode(panel.PanelNode, panel.PanelId);", source);
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
