using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MiniRPG.Core.Data;
using MiniRPG.Core.Debug;
using Xunit;

namespace MiniRPG.Tests;

public sealed class DebugPanelControllerTests
{
	[Fact]
	public void BuildRenderPerfStatus_FormatsPerfLines()
	{
		var controllerType = typeof(GameState).Assembly.GetType("MiniRPG.DebugPanelController", throwOnError: true)!;
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

	private static void SetField(object target, string fieldName, object? value)
	{
		var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(field);
		field!.SetValue(target, value);
	}
}
