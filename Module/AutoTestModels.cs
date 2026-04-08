using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;

namespace MiniRPG.Module;

public interface IAutoTestHost
{
	GameState State { get; }
	GameSessionModule Session { get; }
	FogOfWarTracker FogTracker { get; }
	DebugConfig AutoTestConfig { get; }
	bool ResourcesReady { get; }

	Task StartDefaultNewGameAsync();
	Task<SaveLoadStatus> LoadPresetScenarioAsync(string scenarioId);
	void EnterGame();
	void ExecuteCommand(string command);
	void FlushMap();
	void Dispatch(List<GameEvent> events);
	AutoTestRuntimeSnapshot CaptureSnapshot();
	Task WaitStepAsync(float? delaySeconds = null);
}

public sealed class AutoTestScenarioDef
{
	public required string Id { get; init; }
	public required string DisplayName { get; init; }
	public string? PresetScenarioId { get; init; }
	public required Func<AutoTestScenarioContext, Task> ExecuteAsync { get; init; }
}

public sealed class AutoTestRunReport
{
	[JsonPropertyName("run_id")]
	public string RunId { get; init; } = "";

	[JsonPropertyName("started_at_utc")]
	public DateTimeOffset StartedAtUtc { get; init; }

	[JsonPropertyName("finished_at_utc")]
	public DateTimeOffset? FinishedAtUtc { get; set; }

	[JsonPropertyName("continue_on_failure")]
	public bool ContinueOnFailure { get; init; }

	[JsonPropertyName("step_delay_seconds")]
	public float StepDelaySeconds { get; init; }

	[JsonPropertyName("structured_log_enabled")]
	public bool StructuredLogEnabled { get; init; }

	[JsonPropertyName("resource_smoke_enabled")]
	public bool ResourceSmokeEnabled { get; init; }

	[JsonPropertyName("scenario_filter")]
	public List<string> ScenarioFilter { get; init; } = [];

	[JsonPropertyName("scenarios")]
	public List<AutoTestScenarioResult> Scenarios { get; } = [];

	[JsonIgnore]
	public IEnumerable<AutoTestCaseResult> Cases => Scenarios.SelectMany(static scenario => scenario.Cases);

	[JsonIgnore]
	public int PassCount => Cases.Count(static item => item.Status == "pass");

	[JsonIgnore]
	public int FailCount => Cases.Count(static item => item.Status == "fail");

	[JsonIgnore]
	public int WarnCount => Cases.Count(static item => item.Status == "warn");
}

public sealed class AutoTestScenarioResult
{
	[JsonPropertyName("scenario_id")]
	public required string ScenarioId { get; init; }

	[JsonPropertyName("display_name")]
	public required string DisplayName { get; init; }

	[JsonPropertyName("preset_scenario_id")]
	public string? PresetScenarioId { get; init; }

	[JsonPropertyName("started_at_utc")]
	public DateTimeOffset StartedAtUtc { get; init; }

	[JsonPropertyName("finished_at_utc")]
	public DateTimeOffset? FinishedAtUtc { get; set; }

	[JsonPropertyName("cases")]
	public List<AutoTestCaseResult> Cases { get; } = [];

	[JsonIgnore]
	public int PassCount => Cases.Count(static item => item.Status == "pass");

	[JsonIgnore]
	public int FailCount => Cases.Count(static item => item.Status == "fail");

	[JsonIgnore]
	public int WarnCount => Cases.Count(static item => item.Status == "warn");

	[JsonIgnore]
	public TimeSpan Duration => (FinishedAtUtc ?? StartedAtUtc) - StartedAtUtc;

	[JsonIgnore]
	public string Status => FailCount > 0 ? "fail" : WarnCount > 0 ? "warn" : "pass";
}

public sealed class AutoTestCaseResult
{
	[JsonPropertyName("run_id")]
	public required string RunId { get; init; }

	[JsonPropertyName("scenario_id")]
	public required string ScenarioId { get; init; }

	[JsonPropertyName("case_id")]
	public required string CaseId { get; init; }

	[JsonPropertyName("status")]
	public required string Status { get; init; }

	[JsonPropertyName("severity")]
	public required string Severity { get; init; }

	[JsonPropertyName("message")]
	public required string Message { get; init; }

	[JsonPropertyName("turn")]
	public int Turn { get; init; }

	[JsonPropertyName("player_pos")]
	public AutoTestPlayerPosition? PlayerPos { get; init; }

	[JsonPropertyName("preset_scenario_id")]
	public string? PresetScenarioId { get; init; }

	[JsonPropertyName("metrics")]
	public Dictionary<string, double>? Metrics { get; init; }

	[JsonPropertyName("exception")]
	public string? Exception { get; init; }

	[JsonPropertyName("snapshot")]
	public AutoTestRuntimeSnapshot Snapshot { get; init; } = new();

	[JsonPropertyName("timestamp_utc")]
	public DateTimeOffset TimestampUtc { get; init; }
}

public sealed class AutoTestRuntimeSnapshot
{
	[JsonPropertyName("game_started")]
	public bool GameStarted { get; init; }

	[JsonPropertyName("resources_ready")]
	public bool ResourcesReady { get; init; }

	[JsonPropertyName("render_ready")]
	public bool RenderReady { get; init; }

	[JsonPropertyName("startup_state")]
	public string StartupState { get; init; } = "";

	[JsonPropertyName("startup_sync_fallback_used")]
	public bool StartupSyncFallbackUsed { get; init; }

	[JsonPropertyName("startup_load_path")]
	public string? StartupLoadPath { get; init; }

	[JsonPropertyName("turn")]
	public int Turn { get; init; }

	[JsonPropertyName("player_id")]
	public string? PlayerId { get; init; }

	[JsonPropertyName("player_pos")]
	public AutoTestPlayerPosition? PlayerPos { get; init; }

	[JsonPropertyName("player_gold")]
	public int PlayerGold { get; init; }

	[JsonPropertyName("player_dead")]
	public bool PlayerDead { get; init; }

	[JsonPropertyName("actor_count")]
	public int ActorCount { get; init; }

	[JsonPropertyName("current_preset_scenario_id")]
	public string? CurrentPresetScenarioId { get; init; }

	[JsonPropertyName("current_save_path")]
	public string? CurrentSavePath { get; init; }

	[JsonPropertyName("watch_mode")]
	public bool WatchMode { get; init; }

	[JsonPropertyName("timeline_auto_advance_pending")]
	public bool TimelineAutoAdvancePending { get; init; }

	[JsonPropertyName("busy_operation_active")]
	public bool BusyOperationActive { get; init; }

	[JsonPropertyName("input_focus")]
	public string InputFocus { get; init; } = "";

	[JsonPropertyName("focused_panel_id")]
	public string? FocusedPanelId { get; init; }

	[JsonPropertyName("inventory_open")]
	public bool InventoryOpen { get; init; }

	[JsonPropertyName("chest_open")]
	public bool ChestOpen { get; init; }

	[JsonPropertyName("dialog_open")]
	public bool DialogOpen { get; init; }

	[JsonPropertyName("dialog_option_count")]
	public int DialogOptionCount { get; init; }

	[JsonPropertyName("trade_open")]
	public bool TradeOpen { get; init; }

	[JsonPropertyName("trade_current_tab")]
	public string? TradeCurrentTab { get; init; }

	[JsonPropertyName("trade_item_count")]
	public int TradeItemCount { get; init; }

	[JsonPropertyName("ground_item_count")]
	public int GroundItemCount { get; init; }

	[JsonPropertyName("weather_type_id")]
	public string? WeatherTypeId { get; init; }

	[JsonPropertyName("weather_intensity_id")]
	public string? WeatherIntensityId { get; init; }

	[JsonPropertyName("weather_exposed")]
	public bool WeatherExposed { get; init; }

	[JsonPropertyName("viewport_width")]
	public int ViewportWidth { get; init; }

	[JsonPropertyName("viewport_height")]
	public int ViewportHeight { get; init; }

	[JsonPropertyName("map_viewport_width")]
	public int MapViewportWidth { get; init; }

	[JsonPropertyName("map_viewport_height")]
	public int MapViewportHeight { get; init; }

	[JsonPropertyName("last_log_line")]
	public string? LastLogLine { get; init; }
}

public sealed class AutoTestPlayerPosition
{
	[JsonPropertyName("x")]
	public int X { get; init; }

	[JsonPropertyName("y")]
	public int Y { get; init; }

	[JsonPropertyName("z")]
	public int Z { get; init; }
}

public sealed class AutoTestScenarioContext
{
	private readonly AutoTestModule _owner;

	internal AutoTestScenarioContext(AutoTestModule owner, IAutoTestHost host, AutoTestScenarioResult scenario)
	{
		_owner = owner;
		Host = host;
		Scenario = scenario;
	}

	public IAutoTestHost Host { get; }
	public AutoTestScenarioResult Scenario { get; }
	public GameState State => Host.State;
	public GameSessionModule Session => Host.Session;
	public FogOfWarTracker FogTracker => Host.FogTracker;
	public DebugConfig Config => Host.AutoTestConfig;

	public Task StepAsync() => Host.WaitStepAsync(Config.AutoTestStepDelay);

	public AutoTestRuntimeSnapshot Snapshot() => Host.CaptureSnapshot();

	public void Pass(string caseId, string message, Dictionary<string, double>? metrics = null) =>
		_owner.RecordCase(Scenario, caseId, "pass", "info", message, null, metrics);

	public void Warn(string caseId, string message, Dictionary<string, double>? metrics = null) =>
		_owner.RecordCase(Scenario, caseId, "warn", "warning", message, null, metrics);

	public void Fail(string caseId, string message, Exception? exception = null, Dictionary<string, double>? metrics = null) =>
		_owner.RecordCase(Scenario, caseId, "fail", "error", message, exception, metrics);

	public bool Check(bool condition, string caseId, string successMessage, string failureMessage, Dictionary<string, double>? metrics = null)
	{
		if (condition)
		{
			Pass(caseId, successMessage, metrics);
			return true;
		}

		Fail(caseId, failureMessage, metrics: metrics);
		return false;
	}

	public bool ShouldAbort => _owner.ShouldAbort;
}
