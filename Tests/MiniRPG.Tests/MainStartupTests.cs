using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace MiniRPG.Tests;

public sealed class MainStartupTests
{
	[Fact]
	public void TransitionToStartupFailed_SetsFailedState_AndInvokesFailureCallback()
	{
		var failedCallbacks = 0;
		var coordinator = CreateCoordinator(() => failedCallbacks++);
		var transitionMethod = typeof(MainStartupCoordinator).GetMethod(
			"TransitionToStartupFailed",
			BindingFlags.Instance | BindingFlags.NonPublic);

		var exception = Record.Exception(() => transitionMethod!.Invoke(coordinator, ["startup-bootstrap"]));

		Assert.Null(exception);
		Assert.Equal(StartupState.Failed, coordinator.State);
		Assert.Equal("startup-bootstrap", coordinator.LastFailedPath);
		Assert.False(coordinator.ResourcesReady);
		Assert.Equal(1, failedCallbacks);
	}

	[Fact]
	public void ShouldSkipRuntimeCallbacks_ReturnsTrue_WhenStartupCoordinatorFailed()
	{
		var mainType = typeof(MiniRPG.Main);
		var main = RuntimeHelpers.GetUninitializedObject(mainType);
		var coordinator = CreateCoordinator();
		typeof(MainStartupCoordinator).GetMethod("TransitionToStartupFailed", BindingFlags.Instance | BindingFlags.NonPublic)!
			.Invoke(coordinator, ["startup-bootstrap"]);

		mainType.GetField("_startupCoordinator", BindingFlags.Instance | BindingFlags.NonPublic)!
			.SetValue(main, coordinator);

		var shouldSkip = (bool)mainType.GetMethod("ShouldSkipRuntimeCallbacks", BindingFlags.Instance | BindingFlags.NonPublic)!
			.Invoke(main, null)!;

		Assert.True(shouldSkip);
	}

	private static MainStartupCoordinator CreateCoordinator(Action? onStartupFailed = null) =>
		InitializeCoordinator(
			(MainStartupCoordinator)RuntimeHelpers.GetUninitializedObject(typeof(MainStartupCoordinator)),
			onStartupFailed ?? Noop);

	private static MainStartupCoordinator InitializeCoordinator(MainStartupCoordinator coordinator, Action onStartupFailed)
	{
		var coordinatorType = typeof(MainStartupCoordinator);
		coordinatorType.GetField("_onStartupReady", BindingFlags.Instance | BindingFlags.NonPublic)!
			.SetValue(coordinator, (Action)Noop);
		coordinatorType.GetField("_onStartupFailed", BindingFlags.Instance | BindingFlags.NonPublic)!
			.SetValue(coordinator, onStartupFailed);
		coordinatorType.GetField("_onWarning", BindingFlags.Instance | BindingFlags.NonPublic)!
			.SetValue(coordinator, (Action<string>)(_ => { }));
		coordinatorType.GetField("_refreshStartupUi", BindingFlags.Instance | BindingFlags.NonPublic)!
			.SetValue(coordinator, (Action)Noop);
		coordinatorType.GetField("_prewarmDeferredUiScenes", BindingFlags.Instance | BindingFlags.NonPublic)!
			.SetValue(coordinator, (Action)Noop);
		return coordinator;
	}

	private static void Noop()
	{
	}
}
