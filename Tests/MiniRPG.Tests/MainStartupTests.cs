using System.Reflection;
using System.Runtime.CompilerServices;
using MiniRPG.Core.Data;
using Xunit;

namespace MiniRPG.Tests;

public sealed class MainStartupTests
{
	[Fact]
	public void TransitionToStartupFailed_SetsFailedState_AndEnablesRuntimeShortCircuit()
	{
		var mainType = typeof(MiniRPG.Main);
		var main = RuntimeHelpers.GetUninitializedObject(mainType);
		var transitionMethod = mainType.GetMethod("TransitionToStartupFailed", BindingFlags.Instance | BindingFlags.NonPublic);
		var exception = Record.Exception(() =>
			transitionMethod!.Invoke(main, ["startup-bootstrap"]));

		Assert.Null(exception);
		Assert.Equal("Failed", mainType.GetField("_startupState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!.ToString());
		Assert.Equal("startup-bootstrap", mainType.GetField("_startupLastLoadPath", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main));
		Assert.False((bool)mainType.GetProperty("ResourcesReady", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!);
		Assert.True((bool)mainType.GetMethod("ShouldSkipRuntimeCallbacks", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!);
	}
}
