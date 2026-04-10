using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace MiniRPG.Module;

public sealed class AutoTestLogWriter
{
	public const string RelativeLogPath = "user://test_results.log";
	public const string JsonBeginMarker = "=== AUTO_TEST_JSON_BEGIN ===";
	public const string JsonEndMarker = "=== AUTO_TEST_JSON_END ===";

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		WriteIndented = false,
	};

	public string Write(AutoTestRunReport report)
	{
		var path = ProjectSettings.GlobalizePath(RelativeLogPath);
		var directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directory))
			Directory.CreateDirectory(directory);

		File.WriteAllText(path, BuildText(report), Encoding.UTF8);
		GD.Print($"[AutoTest] Log written to: {path}");
		return path;
	}

	internal static string BuildText(AutoTestRunReport report)
	{
		var builder = new StringBuilder();
		builder.AppendLine("MiniRPG Auto Test Report");
		builder.AppendLine($"run_id: {report.RunId}");
		builder.AppendLine($"started_at_utc: {report.StartedAtUtc:O}");
		builder.AppendLine($"finished_at_utc: {report.FinishedAtUtc:O}");
		builder.AppendLine($"continue_on_failure: {report.ContinueOnFailure}");
		builder.AppendLine($"step_delay_seconds: {report.StepDelaySeconds:F2}");
		builder.AppendLine($"structured_log_enabled: {report.StructuredLogEnabled}");
		builder.AppendLine($"resource_smoke_enabled: {report.ResourceSmokeEnabled}");
		builder.AppendLine($"display_server_name: {FormatText(report.DisplayServerName)}");
		builder.AppendLine($"headless_mode: {report.HeadlessMode}");
		builder.AppendLine($"invoked_from_cli: {report.InvokedFromCli}");
		builder.AppendLine($"scenario_filter: {(report.ScenarioFilter.Count == 0 ? "<default>" : string.Join(", ", report.ScenarioFilter))}");
		builder.AppendLine();
		builder.AppendLine($"result: {report.PassCount} pass / {report.FailCount} fail / {report.WarnCount} warn");
		builder.AppendLine();
		builder.AppendLine("scenario_summary:");
		foreach (var scenario in report.Scenarios)
		{
			builder.AppendLine(
				$"- {scenario.ScenarioId}: {scenario.Status} ({scenario.PassCount} pass / {scenario.FailCount} fail / {scenario.WarnCount} warn, {scenario.Duration.TotalMilliseconds:F1} ms)");
		}

		var firstFailures = report.Cases
			.Where(static item => item.Status == "fail")
			.Take(10)
			.ToList();
		if (firstFailures.Count > 0)
		{
			builder.AppendLine();
			builder.AppendLine("first_failures:");
			foreach (var failure in firstFailures)
			{
				builder.AppendLine(
					$"- {failure.ScenarioId}/{failure.CaseId}: {failure.Message} @ turn={failure.Turn} pos={FormatPosition(failure.PlayerPos)}");
				if (!string.IsNullOrWhiteSpace(failure.Exception))
					builder.AppendLine($"  exception: {failure.Exception}");
				if (!string.IsNullOrWhiteSpace(failure.Snapshot.LastLogLine))
					builder.AppendLine($"  last_log_line: {failure.Snapshot.LastLogLine}");
			}
		}

		var warnings = report.Cases
			.Where(static item => item.Status == "warn")
			.Take(10)
			.ToList();
		if (warnings.Count > 0)
		{
			builder.AppendLine();
			builder.AppendLine("warnings:");
			foreach (var warning in warnings)
				builder.AppendLine($"- {warning.ScenarioId}/{warning.CaseId}: {warning.Message}");
		}

		AppendCuratedSummary(
			builder,
			report,
			"false_positive_risks:",
			static item => item.Status != "pass"
				&& (item.CaseId.Contains(".preconditions", StringComparison.Ordinal)
					|| item.CaseId.Contains(".false_positive", StringComparison.Ordinal)));

		AppendCuratedSummary(
			builder,
			report,
			"state_inconsistencies:",
			static item => item.Status != "pass"
				&& (item.CaseId.Contains(".exclusive", StringComparison.Ordinal)
					|| item.CaseId.Contains(".state_consistency", StringComparison.Ordinal)));

		AppendBenchmarkBreakdown(builder, report);

		if (report.StructuredLogEnabled)
		{
			builder.AppendLine();
			builder.AppendLine(JsonBeginMarker);
			foreach (var item in report.Cases)
				builder.AppendLine(JsonSerializer.Serialize(item, JsonOptions));
			builder.AppendLine(JsonEndMarker);
		}

		return builder.ToString();
	}

	private static string FormatPosition(AutoTestPlayerPosition? pos) =>
		pos == null ? "<none>" : $"({pos.X},{pos.Y},{pos.Z})";

	private static void AppendCuratedSummary(
		StringBuilder builder,
		AutoTestRunReport report,
		string heading,
		Func<AutoTestCaseResult, bool> predicate)
	{
		var items = report.Cases
			.Where(predicate)
			.Take(10)
			.ToList();
		if (items.Count == 0)
			return;

		builder.AppendLine();
		builder.AppendLine(heading);
		foreach (var item in items)
			builder.AppendLine($"- {item.ScenarioId}/{item.CaseId}: {item.Message}");
	}

	private static void AppendBenchmarkBreakdown(StringBuilder builder, AutoTestRunReport report)
	{
		var benchmarks = report.Cases
			.Where(static item =>
				string.Equals(item.ScenarioId, "qa_combat_arena", StringComparison.Ordinal)
				&& item.CaseId.StartsWith("qa_combat_arena.benchmark.count_", StringComparison.Ordinal)
				&& item.CaseId.EndsWith(".thresholds", StringComparison.Ordinal)
				&& item.Metrics != null)
			.OrderBy(static item => item.Metrics!.GetValueOrDefault("spawned_count"))
			.ToList();
		if (benchmarks.Count == 0)
			return;

		builder.AppendLine();
		builder.AppendLine("benchmark_breakdown:");
		foreach (var benchmark in benchmarks)
		{
			var metrics = benchmark.Metrics!;
			builder.AppendLine(
				$"- count={FormatMetric(metrics, "spawned_count", "F0")} tick_ms={FormatMetric(metrics, "tick_ms")} advance_world_ms={FormatMetric(metrics, "advance_world_ms")} ai_dispatch_ms={FormatMetric(metrics, "ai_dispatch_ms")} vision_ms={FormatMetric(metrics, "vision_ms")} vision_dead_check_ms={FormatMetric(metrics, "vision_dead_check_ms")} vision_rank_ms={FormatMetric(metrics, "vision_rank_ms")} vision_los_ms={FormatMetric(metrics, "vision_los_ms")} observer_count={FormatMetric(metrics, "observer_count", "F0")} candidate_count={FormatMetric(metrics, "candidate_count", "F0")} shortlist_count={FormatMetric(metrics, "shortlist_count", "F0")}");
		}
	}

	private static string FormatMetric(Dictionary<string, double> metrics, string key, string format = "F3") =>
		metrics.TryGetValue(key, out var value)
			? value.ToString(format)
			: "n/a";

	private static string FormatText(string? value) =>
		string.IsNullOrWhiteSpace(value) ? "<unknown>" : value;
}
