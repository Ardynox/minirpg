using System;
using System.Reflection;
using MiniRPG.Core.Data;
using Xunit;

namespace MiniRPG.Tests;

public sealed class MainStartupTests
{
	[Fact]
	public void FailStartupBootstrap_SetsFailedState_AndEnablesRuntimeShortCircuit()
	{
		var mainType = typeof(GameState).Assembly.GetType("MiniRPG.Main", throwOnError: true)!;
		var main = Activator.CreateInstance(mainType)!;
		var failMethod = mainType.GetMethod("FailStartupBootstrap", BindingFlags.Instance | BindingFlags.NonPublic);
		var exception = Record.Exception(() =>
			failMethod!.Invoke(main, ["startup-bootstrap", new InvalidOperationException("boom")]));

		Assert.Null(exception);
		Assert.Equal("Failed", mainType.GetField("_startupState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!.ToString());
		Assert.Equal("startup-bootstrap", mainType.GetField("_startupLastLoadPath", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main));
		Assert.False((bool)mainType.GetProperty("ResourcesReady", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!);
		Assert.True((bool)mainType.GetMethod("ShouldSkipRuntimeCallbacks", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!);
	}
}
