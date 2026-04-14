using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Debug;
using MiniRPG.Module.Panel;
using Xunit;

namespace MiniRPG.Tests;

public sealed class DebugPanelControllerTests
{
	public DebugPanelControllerTests()
	{
		LocalizationService.Initialize();
		LocalizationService.SetLocale("en", notify: false);
	}

	[Fact]
	public void BuildRenderPerfStatus_FormatsPerfLines()
	{
		var controllerType = typeof(MiniRPG.DebugPanelController);
		var controller = RuntimeHelpers.GetUninitializedObject(controllerType);

		SetField(
			controller,
			"_renderPerfSnapshot",
			(Func<(int ActiveSpriteCount, int DrawCommandCount, double FrameTimeAvgMs)>)(() => (12, 34, 16.789)));

		var method = controllerType.GetMethod("BuildRenderPerfStatus", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(method);

		var result = (DebugModule.Result)method!.Invoke(controller, null)!;

		Assert.Collection(
			result.Logs,
			line => Assert.Equal("[perf] active_sprite_count=12", line),
			line => Assert.Equal("[perf] draw_command_count=34", line),
			line => Assert.Equal("[perf] frame_time_avg_ms=16.79", line));
	}

	[Fact]
	public void ExecuteSetTurn_UpdatesState_AndRequestsFlushAndUiRefresh()
	{
		var controllerType = typeof(MiniRPG.DebugPanelController);
		var controller = RuntimeHelpers.GetUninitializedObject(controllerType);
		var state = new GameState { Turn = 12 };
		var flushRequested = false;
		var uiRefreshRequested = false;

		SetField(controller, "_state", state);
		SetField(controller, "_log", null);
		SetField(controller, "_markUiDirty", (Action)(() => uiRefreshRequested = true));
		SetField(controller, "_flushMap", (Action)(() => flushRequested = true));

		var result = ((DebugPanelModule.IHost)controller).ExecuteSetTurn(135);

		Assert.Equal(135, state.Turn);
		Assert.True(flushRequested);
		Assert.True(uiRefreshRequested);
		Assert.True(result.NeedsFlush);
		Assert.True(result.NeedsUiRefresh);
		Assert.Contains("turn set to 135", result.Logs[0], StringComparison.OrdinalIgnoreCase);
	}

	private static void SetField(object target, string fieldName, object? value)
	{
		var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(field);
		field!.SetValue(target, value);
	}
}
