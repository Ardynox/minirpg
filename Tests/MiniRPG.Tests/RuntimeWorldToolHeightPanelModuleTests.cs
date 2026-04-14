using System;
using System.IO;
using Xunit;

namespace MiniRPG.Tests;

public sealed class RuntimeWorldToolHeightPanelModuleTests
{
	[Fact]
	public void ModuleSource_UsesDedicatedDragZone_ForDragging()
	{
		var source = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Module", "WorldTool", "RuntimeWorldToolHeightPanelModule.cs"));

		Assert.Contains("Name = \"DragZone\"", source);
		Assert.Contains("public Control DragHandle => _dragZone;", source);
		Assert.DoesNotContain("public Control DragHandle => _headerRow;", source);
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
