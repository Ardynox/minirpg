using System;
using MiniRPG.Core.Config;
using MiniRPG.Module;
using Xunit;

namespace MiniRPG.Tests;

public sealed class AutoTestCliTests
{
	[Fact]
	public void Parse_AutotestOnly_EnablesCliRun()
	{
		var result = AutoTestCli.Parse(["--autotest"]);

		Assert.True(result.IsValid);
		Assert.True(result.Options.Enabled);
		Assert.Null(result.Options.ScenarioFilter);
		Assert.Null(result.Options.StepDelaySeconds);
		Assert.False(result.Options.StopOnFail);
	}

	[Fact]
	public void Parse_InlineScenarioOverride_SplitsCsv()
	{
		var result = AutoTestCli.Parse(["--autotest", "--autotest-scenarios=resource_smoke,qa_combat_arena"]);

		Assert.True(result.IsValid);
		Assert.Equal(["resource_smoke", "qa_combat_arena"], result.Options.ScenarioFilter);
	}

	[Fact]
	public void Parse_SeparateScenarioOverride_SplitsCsv()
	{
		var result = AutoTestCli.Parse(["--autotest", "--autotest-scenarios", "new_game,qa_smoke_core"]);

		Assert.True(result.IsValid);
		Assert.Equal(["new_game", "qa_smoke_core"], result.Options.ScenarioFilter);
	}

	[Fact]
	public void Parse_StepDelayOverride_ParsesInvariantFloat()
	{
		var result = AutoTestCli.Parse(["--autotest", "--autotest-step-delay=0.25"]);

		Assert.True(result.IsValid);
		Assert.Equal(0.25f, result.Options.StepDelaySeconds!.Value, 3);
	}

	[Fact]
	public void Parse_InvalidStepDelay_ReturnsUsageError()
	{
		var result = AutoTestCli.Parse(["--autotest", "--autotest-step-delay=abc"]);

		Assert.False(result.IsValid);
		Assert.Contains("Invalid AutoTest step delay", result.Error, StringComparison.Ordinal);
	}

	[Fact]
	public void Parse_UnknownAutotestOption_ReturnsUsageError()
	{
		var result = AutoTestCli.Parse(["--autotest", "--autotest-unknown"]);

		Assert.False(result.IsValid);
		Assert.Contains("Unknown AutoTest CLI option", result.Error, StringComparison.Ordinal);
	}

	[Fact]
	public void CreateRuntimeConfig_AppliesOverridesWithoutMutatingBaseConfig()
	{
		var baseConfig = new DebugConfig
		{
			AutoTestStepDelay = 0.05f,
			AutoTestStopOnFail = false,
			AutoTestScenarios = ["resource_smoke", "new_game"],
			AutoTestBenchmarkCounts = [10, 50],
		};
		var runtimeConfig = AutoTestCli.CreateRuntimeConfig(baseConfig, new AutoTestCliOptions
		{
			Enabled = true,
			ScenarioFilter = ["qa_smoke_core"],
			StepDelaySeconds = 0f,
			StopOnFail = true,
		});

		Assert.Equal(["resource_smoke", "new_game"], baseConfig.AutoTestScenarios);
		Assert.Equal(["qa_smoke_core"], runtimeConfig.AutoTestScenarios);
		Assert.Equal(0f, runtimeConfig.AutoTestStepDelay);
		Assert.True(runtimeConfig.AutoTestStopOnFail);
		Assert.Equal([10, 50], runtimeConfig.AutoTestBenchmarkCounts);
	}

	[Fact]
	public void GetExitCode_ReturnsSuccessForWarnOnlyReport()
	{
		var report = new AutoTestRunReport();
		var scenario = new AutoTestScenarioResult
		{
			ScenarioId = "qa_smoke_core",
			DisplayName = "QA Smoke Core",
			StartedAtUtc = DateTimeOffset.UtcNow,
			FinishedAtUtc = DateTimeOffset.UtcNow,
		};
		scenario.Cases.Add(new AutoTestCaseResult
		{
			RunId = "run-1",
			ScenarioId = scenario.ScenarioId,
			CaseId = "qa_smoke_core.warn",
			Status = "warn",
			Severity = "warning",
			Message = "warning only",
			Snapshot = new AutoTestRuntimeSnapshot(),
			TimestampUtc = DateTimeOffset.UtcNow,
		});
		report.Scenarios.Add(scenario);

		Assert.Equal(AutoTestCli.SuccessExitCode, AutoTestCli.GetExitCode(report));
	}

	[Fact]
	public void GetExitCode_ReturnsFailureForFailedReport()
	{
		var report = new AutoTestRunReport();
		var scenario = new AutoTestScenarioResult
		{
			ScenarioId = "qa_smoke_core",
			DisplayName = "QA Smoke Core",
			StartedAtUtc = DateTimeOffset.UtcNow,
			FinishedAtUtc = DateTimeOffset.UtcNow,
		};
		scenario.Cases.Add(new AutoTestCaseResult
		{
			RunId = "run-1",
			ScenarioId = scenario.ScenarioId,
			CaseId = "qa_smoke_core.fail",
			Status = "fail",
			Severity = "error",
			Message = "failure",
			Snapshot = new AutoTestRuntimeSnapshot(),
			TimestampUtc = DateTimeOffset.UtcNow,
		});
		report.Scenarios.Add(scenario);

		Assert.Equal(AutoTestCli.FailureExitCode, AutoTestCli.GetExitCode(report));
	}

	[Fact]
	public void EvaluateViewportAvailability_HeadlessModeSkipsFailure()
	{
		var result = AutoTestModule.EvaluateViewportAvailability("viewport", 0, 0, headlessMode: true);

		Assert.True(result.Passed);
		Assert.True(result.SkippedAsHeadless);
		Assert.Contains("Headless mode skipped viewport assertion", result.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void CreateStartupFailureReport_IncludesMetadataAndFailCase()
	{
		var config = new DebugConfig
		{
			AutoTestScenarios = ["resource_smoke"],
		};
		var snapshot = new AutoTestRuntimeSnapshot
		{
			DisplayServerName = "headless",
			HeadlessMode = true,
			InvokedFromCli = true,
			StartupState = "failed",
		};

		var report = AutoTestModule.CreateStartupFailureReport(config, snapshot, "startup-bootstrap");

		Assert.Equal("headless", report.DisplayServerName);
		Assert.True(report.HeadlessMode);
		Assert.True(report.InvokedFromCli);
		Assert.Equal(1, report.FailCount);
		var scenario = Assert.Single(report.Scenarios);
		var @case = Assert.Single(scenario.Cases);
		Assert.Equal("autotest.startup_failed", @case.CaseId);
		Assert.Equal("fail", @case.Status);
		Assert.Equal(snapshot, @case.Snapshot);
	}
}
