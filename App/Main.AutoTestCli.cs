using System;
using System.Threading.Tasks;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Module;

namespace MiniRPG;

public partial class Main
{
	private AutoTestCliParseResult _autoTestCliParseResult = AutoTestCliParseResult.Disabled;
	private DebugConfig _autoTestRuntimeConfig = new();
	private bool _autoTestCliStarted;
	private bool _autoTestCliExitRequested;

	private bool IsAutoTestCliEnabled =>
		_autoTestCliParseResult != null
		&& _autoTestCliParseResult.IsValid
		&& _autoTestCliParseResult.Options.Enabled;

	private void ParseAutoTestCliOptions()
	{
		_autoTestCliParseResult = AutoTestCli.Parse(OS.GetCmdlineUserArgs());
		_autoTestRuntimeConfig = AutoTestCli.CreateRuntimeConfig(_autoTestRuntimeConfig, _autoTestCliParseResult.Options);
		if (_autoTestCliParseResult.IsValid)
			return;

		GD.PrintErr($"[AutoTestCli] {_autoTestCliParseResult.Error}");
		GD.PrintErr($"[AutoTestCli] {AutoTestCli.BuildUsageText()}");
		RequestAutoTestCliQuit(AutoTestCli.UsageErrorExitCode);
	}

	private void FinalizeAutoTestCliConfig()
	{
		_autoTestRuntimeConfig = AutoTestCli.CreateRuntimeConfig(GameConfig.Debug, _autoTestCliParseResult.Options);
	}

	private void TryStartAutoTestCli()
	{
		if (!IsAutoTestCliEnabled
			|| _autoTestCliStarted
			|| _autoTestCliExitRequested
			|| !ResourcesReady
			|| _mapRender == null)
		{
			return;
		}

		_autoTestCliStarted = true;
		_ = RunAutoTestFromCliAsync();
	}

	private async Task RunAutoTestFromCliAsync()
	{
		try
		{
			var report = await new AutoTestModule().RunAllAsync(this);
			RequestAutoTestCliQuit(AutoTestCli.GetExitCode(report));
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AutoTestCli] Unexpected AutoTest failure: {ex}");
			RequestAutoTestCliQuit(AutoTestCli.FailureExitCode);
		}
	}

	private void HandleAutoTestCliStartupFailed(string path)
	{
		if (!IsAutoTestCliEnabled || _autoTestCliExitRequested)
			return;

		var report = AutoTestModule.CreateStartupFailureReport(_autoTestRuntimeConfig, CaptureAutoTestSnapshot(), path);
		_ = new AutoTestLogWriter().Write(report);
		GD.PrintErr($"[AutoTestCli] Startup failed before AutoTest could begin. path={path}");
		RequestAutoTestCliQuit(AutoTestCli.FailureExitCode);
	}

	private void RequestAutoTestCliQuit(int exitCode)
	{
		if (_autoTestCliExitRequested)
			return;

		_autoTestCliExitRequested = true;
		GetTree()?.Quit(exitCode);
	}

	private string GetCurrentDisplayServerName()
	{
		try
		{
			return DisplayServer.GetName();
		}
		catch
		{
			return string.Empty;
		}
	}
}
