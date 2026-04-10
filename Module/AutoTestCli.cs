using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MiniRPG.Core.Config;

namespace MiniRPG.Module;

internal static class AutoTestCli
{
	public const int SuccessExitCode = 0;
	public const int FailureExitCode = 1;
	public const int UsageErrorExitCode = 2;

	public static AutoTestCliParseResult Parse(IReadOnlyList<string> args)
	{
		var enabled = false;
		var sawOverride = false;
		List<string>? scenarioFilter = null;
		float? stepDelaySeconds = null;
		var stopOnFail = false;

		for (var i = 0; i < args.Count; i++)
		{
			var arg = args[i];
			if (string.Equals(arg, "--autotest", StringComparison.Ordinal))
			{
				enabled = true;
				continue;
			}

			if (TryReadOptionValue(args, ref i, arg, "--autotest-scenarios", out var scenarioValue, out var scenarioError))
			{
				if (scenarioError != null)
					return AutoTestCliParseResult.Invalid(scenarioError);

				sawOverride = true;
				var parsedScenarios = scenarioValue!
					.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
					.Where(static item => !string.IsNullOrWhiteSpace(item))
					.ToList();
				if (parsedScenarios.Count == 0)
					return AutoTestCliParseResult.Invalid("AutoTest scenarios override cannot be empty.");

				scenarioFilter = parsedScenarios;
				continue;
			}

			if (TryReadOptionValue(args, ref i, arg, "--autotest-step-delay", out var delayValue, out var delayError))
			{
				if (delayError != null)
					return AutoTestCliParseResult.Invalid(delayError);

				sawOverride = true;
				if (!float.TryParse(delayValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDelay))
				{
					return AutoTestCliParseResult.Invalid(
						$"Invalid AutoTest step delay '{delayValue}'. Expected a floating-point number.");
				}

				stepDelaySeconds = parsedDelay;
				continue;
			}

			if (string.Equals(arg, "--autotest-stop-on-fail", StringComparison.Ordinal))
			{
				sawOverride = true;
				stopOnFail = true;
				continue;
			}

			if (arg.StartsWith("--autotest-", StringComparison.Ordinal))
			{
				return AutoTestCliParseResult.Invalid($"Unknown AutoTest CLI option '{arg}'.");
			}
		}

		if (!enabled && sawOverride)
			return AutoTestCliParseResult.Invalid("AutoTest override options require '--autotest'.");

		return AutoTestCliParseResult.Valid(new AutoTestCliOptions
		{
			Enabled = enabled,
			ScenarioFilter = scenarioFilter,
			StepDelaySeconds = stepDelaySeconds,
			StopOnFail = stopOnFail,
		});
	}

	public static DebugConfig CloneConfig(DebugConfig source)
	{
		return new DebugConfig
		{
			AutoTestStepDelay = source.AutoTestStepDelay,
			AutoTestContinueOnFailure = source.AutoTestContinueOnFailure,
			AutoTestStopOnFail = source.AutoTestStopOnFail,
			AutoTestScenarios = [.. source.AutoTestScenarios],
			AutoTestEnableResourceSmoke = source.AutoTestEnableResourceSmoke,
			AutoTestWriteStructuredLog = source.AutoTestWriteStructuredLog,
			AutoTestRenderArenaRadius = source.AutoTestRenderArenaRadius,
			AutoTestAiArenaRadius = source.AutoTestAiArenaRadius,
			AutoTestBenchmarkArenaRadius = source.AutoTestBenchmarkArenaRadius,
			AutoTestBenchmarkCounts = [.. source.AutoTestBenchmarkCounts],
			AutoTestAiVisionWarnMs = source.AutoTestAiVisionWarnMs,
			AutoTestTickWarnMs = source.AutoTestTickWarnMs,
		};
	}

	public static DebugConfig CreateRuntimeConfig(DebugConfig baseConfig, AutoTestCliOptions options)
	{
		var runtimeConfig = CloneConfig(baseConfig);
		if (!options.Enabled)
			return runtimeConfig;

		if (options.ScenarioFilter != null)
			runtimeConfig.AutoTestScenarios = [.. options.ScenarioFilter];
		if (options.StepDelaySeconds.HasValue)
			runtimeConfig.AutoTestStepDelay = options.StepDelaySeconds.Value;
		if (options.StopOnFail)
			runtimeConfig.AutoTestStopOnFail = true;

		return runtimeConfig;
	}

	public static int GetExitCode(AutoTestRunReport report) =>
		report.FailCount > 0 ? FailureExitCode : SuccessExitCode;

	public static bool IsHeadlessDisplayServer(string? displayServerName) =>
		string.Equals(displayServerName, "headless", StringComparison.OrdinalIgnoreCase);

	public static string BuildUsageText() =>
		"Usage: godot --path <project> -- --autotest [--autotest-scenarios=a,b] [--autotest-step-delay=0.05] [--autotest-stop-on-fail]";

	private static bool TryReadOptionValue(
		IReadOnlyList<string> args,
		ref int index,
		string currentArg,
		string optionName,
		out string? value,
		out string? error)
	{
		value = null;
		error = null;

		if (string.Equals(currentArg, optionName, StringComparison.Ordinal))
		{
			if (index + 1 >= args.Count)
			{
				error = $"Missing value for '{optionName}'.";
				return true;
			}

			value = args[++index];
			return true;
		}

		var prefix = $"{optionName}=";
		if (!currentArg.StartsWith(prefix, StringComparison.Ordinal))
			return false;

		value = currentArg[prefix.Length..];
		if (string.IsNullOrWhiteSpace(value))
			error = $"Missing value for '{optionName}'.";
		return true;
	}
}

internal sealed class AutoTestCliOptions
{
	public bool Enabled { get; init; }
	public List<string>? ScenarioFilter { get; init; }
	public float? StepDelaySeconds { get; init; }
	public bool StopOnFail { get; init; }
}

internal sealed class AutoTestCliParseResult
{
	private AutoTestCliParseResult(bool isValid, string? error, AutoTestCliOptions options)
	{
		IsValid = isValid;
		Error = error;
		Options = options;
	}

	public static AutoTestCliParseResult Disabled { get; } =
		new(true, null, new AutoTestCliOptions());

	public bool IsValid { get; }
	public string? Error { get; }
	public AutoTestCliOptions Options { get; }

	public static AutoTestCliParseResult Valid(AutoTestCliOptions options) =>
		new(true, null, options);

	public static AutoTestCliParseResult Invalid(string error) =>
		new(false, error, new AutoTestCliOptions());
}
