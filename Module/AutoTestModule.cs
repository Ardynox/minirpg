using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using MiniRPG.Core.Map;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;
using MiniRPG.Module.Panel;
using MiniRPG.Module.Render;

namespace MiniRPG.Module;

public class AutoTestModule
{
	internal readonly record struct AutoTestViewportAssertionResult(bool Passed, bool SkippedAsHeadless, string Message);

	private const string HeavyTileSetPath = "res://Assets/Art/Tilesets/FantasyKingdom/FantasyKingdomTileSet.tres";
	private static readonly string[] ResourceScenePaths =
	[
		"res://App/Main.tscn",
		"res://Assets/UI/Themes/UITheme.tres",
		"res://Scene/InventoryPanel.tscn",
		"res://Scene/DialogPanel.tscn",
		"res://Scene/TradePanel.tscn",
		"res://Scene/StatusPanel.tscn",
		"res://Scene/KeyBindingsView.tscn",
	];

	private readonly AutoTestLogWriter _logWriter = new();
	private AutoTestRunReport? _report;
	private IAutoTestHost? _lastHost;
	private bool _continueOnFailure = true;

	internal bool ShouldAbort { get; private set; }

	public void RunAll(IAutoTestHost host) => _ = RunAllSafeAsync(host);

	public Task<AutoTestRunReport> RunAllAsync(IAutoTestHost host) => RunAllSafeAsync(host);

	internal static AutoTestRunReport CreateStartupFailureReport(
		DebugConfig config,
		AutoTestRuntimeSnapshot snapshot,
		string startupPath)
	{
		var finishedAt = DateTimeOffset.UtcNow;
		var report = CreateRunReport(config, snapshot);
		var scenario = new AutoTestScenarioResult
		{
			ScenarioId = "autotest",
			DisplayName = "AutoTest Startup",
			StartedAtUtc = report.StartedAtUtc,
			FinishedAtUtc = finishedAt,
		};
		scenario.Cases.Add(new AutoTestCaseResult
		{
			RunId = report.RunId,
			ScenarioId = scenario.ScenarioId,
			CaseId = "autotest.startup_failed",
			Status = "fail",
			Severity = "error",
			Message = $"Startup failed before AutoTest could begin. path={startupPath}.",
			Turn = snapshot.Turn,
			PlayerPos = snapshot.PlayerPos,
			PresetScenarioId = snapshot.CurrentPresetScenarioId,
			Snapshot = snapshot,
			TimestampUtc = finishedAt,
		});
		report.Scenarios.Add(scenario);
		report.FinishedAtUtc = finishedAt;
		return report;
	}

	internal static AutoTestViewportAssertionResult EvaluateViewportAvailability(
		string subject,
		int width,
		int height,
		bool headlessMode)
	{
		if (headlessMode)
		{
			return new AutoTestViewportAssertionResult(
				Passed: true,
				SkippedAsHeadless: true,
				Message: $"Headless mode skipped viewport assertion for {subject}; actual={width}x{height}.");
		}

		return new AutoTestViewportAssertionResult(
			Passed: width > 0 && height > 0,
			SkippedAsHeadless: false,
			Message: $"Expected {subject} size > 0; actual={width}x{height}.");
	}

	internal void RecordCase(
		AutoTestScenarioResult scenario,
		string caseId,
		string status,
		string severity,
		string message,
		Exception? exception = null,
		Dictionary<string, double>? metrics = null)
	{
		if (_report == null)
			return;

		var snapshot = _lastHost?.CaptureSnapshot() ?? new AutoTestRuntimeSnapshot();
		scenario.Cases.Add(new AutoTestCaseResult
		{
			RunId = _report.RunId,
			ScenarioId = scenario.ScenarioId,
			CaseId = caseId,
			Status = status,
			Severity = severity,
			Message = message,
			Turn = snapshot.Turn,
			PlayerPos = snapshot.PlayerPos,
			PresetScenarioId = snapshot.CurrentPresetScenarioId,
			Metrics = metrics,
			Exception = exception == null ? null : $"{exception.GetType().Name}: {exception.Message}",
			Snapshot = snapshot,
			TimestampUtc = DateTimeOffset.UtcNow,
		});

		var line = $"[AutoTest][{scenario.ScenarioId}] [{status.ToUpperInvariant()}] {caseId}: {message}";
		if (status == "fail")
			GD.PrintErr(line);
		else
			GD.Print(line);

		if (status == "fail" && !_continueOnFailure)
			ShouldAbort = true;
	}

	private async Task<AutoTestRunReport> RunAllSafeAsync(IAutoTestHost host)
	{
		try
		{
			return await RunAllCoreAsync(host);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AutoTest] Fatal error: {ex.GetType().Name}: {ex.Message}");
			if (_report == null)
			{
				var config = host.AutoTestConfig;
				_continueOnFailure = config.AutoTestContinueOnFailure && !config.AutoTestStopOnFail;
				_report = CreateRunReport(config, CaptureSnapshotSafely(host));
			}

			var fatalScenario = new AutoTestScenarioResult
			{
				ScenarioId = "autotest",
				DisplayName = "AutoTest Fatal",
				StartedAtUtc = _report.StartedAtUtc,
				FinishedAtUtc = DateTimeOffset.UtcNow,
			};
			_report.Scenarios.Add(fatalScenario);
			_lastHost = host;
			RecordCase(fatalScenario, "autotest.fatal", "fail", "error", "Auto test crashed before completion.", ex);
			_report.FinishedAtUtc = DateTimeOffset.UtcNow;
			_logWriter.Write(_report);
			return _report;
		}
	}

	private async Task<AutoTestRunReport> RunAllCoreAsync(IAutoTestHost host)
	{
		_lastHost = host;
		var config = host.AutoTestConfig;
		_continueOnFailure = config.AutoTestContinueOnFailure && !config.AutoTestStopOnFail;
		ShouldAbort = false;
		_report = CreateRunReport(config, CaptureSnapshotSafely(host));

		GD.Print("[AutoTest] ========================================");
		GD.Print("[AutoTest] AUTO TEST START");
		GD.Print($"[AutoTest] run_id={_report.RunId} delay={config.AutoTestStepDelay:F2}s continue_on_failure={_continueOnFailure}");
		GD.Print($"[AutoTest] display_server={_report.DisplayServerName} headless={_report.HeadlessMode} cli={_report.InvokedFromCli}");
		GD.Print("[AutoTest] ========================================");

		foreach (var scenario in BuildScenarioSequence(config))
		{
			if (ShouldAbort)
				break;

			await RunScenarioAsync(host, scenario);
		}

		_report.FinishedAtUtc = DateTimeOffset.UtcNow;
		var path = _logWriter.Write(_report);
		GD.Print($"[AutoTest] RESULT: {_report.PassCount} PASS / {_report.FailCount} FAIL / {_report.WarnCount} WARN");
		GD.Print($"[AutoTest] Report path: {path}");
		return _report;
	}

	private IEnumerable<AutoTestScenarioDef> BuildScenarioSequence(DebugConfig config)
	{
		var definitions = new Dictionary<string, AutoTestScenarioDef>(StringComparer.OrdinalIgnoreCase)
		{
			["resource_smoke"] = new AutoTestScenarioDef
			{
				Id = "resource_smoke",
				DisplayName = "Resource Smoke",
				ExecuteAsync = RunResourceSmokeAsync,
			},
			["new_game"] = new AutoTestScenarioDef
			{
				Id = "new_game",
				DisplayName = "New Game",
				ExecuteAsync = RunNewGameScenarioAsync,
			},
			["qa_smoke_core"] = new AutoTestScenarioDef
			{
				Id = "qa_smoke_core",
				DisplayName = "QA Smoke Core",
				PresetScenarioId = "qa_smoke_core",
				ExecuteAsync = RunSmokeCoreScenarioAsync,
			},
			["qa_interaction_hub"] = new AutoTestScenarioDef
			{
				Id = "qa_interaction_hub",
				DisplayName = "QA Interaction Hub",
				PresetScenarioId = "qa_interaction_hub",
				ExecuteAsync = RunInteractionHubScenarioAsync,
			},
			["qa_combat_arena"] = new AutoTestScenarioDef
			{
				Id = "qa_combat_arena",
				DisplayName = "QA Combat Arena",
				PresetScenarioId = "qa_combat_arena",
				ExecuteAsync = RunCombatArenaScenarioAsync,
			},
		};

		var requested = config.AutoTestScenarios.Count == 0
			? new List<string> { "resource_smoke", "new_game", "qa_smoke_core", "qa_interaction_hub", "qa_combat_arena" }
			: config.AutoTestScenarios;
		var ordered = new List<AutoTestScenarioDef>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var scenarioId in requested)
		{
			if (!config.AutoTestEnableResourceSmoke
				&& string.Equals(scenarioId, "resource_smoke", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (definitions.TryGetValue(scenarioId, out var definition) && seen.Add(definition.Id))
			{
				ordered.Add(definition);
			}
			else if (!definitions.ContainsKey(scenarioId))
			{
				GD.PushWarning($"AutoTest: unknown scenario id '{scenarioId}', skipping.");
			}
		}

		if (ordered.Count == 0)
		{
			if (config.AutoTestEnableResourceSmoke)
				ordered.Add(definitions["resource_smoke"]);
			ordered.Add(definitions["new_game"]);
			ordered.Add(definitions["qa_smoke_core"]);
			ordered.Add(definitions["qa_interaction_hub"]);
			ordered.Add(definitions["qa_combat_arena"]);
		}

		return ordered;
	}

	private async Task RunScenarioAsync(IAutoTestHost host, AutoTestScenarioDef definition)
	{
		var scenario = new AutoTestScenarioResult
		{
			ScenarioId = definition.Id,
			DisplayName = definition.DisplayName,
			PresetScenarioId = definition.PresetScenarioId,
			StartedAtUtc = DateTimeOffset.UtcNow,
		};
		_report!.Scenarios.Add(scenario);

		GD.Print($"[AutoTest] Scenario start: {definition.Id}");

		try
		{
			if (definition.PresetScenarioId == null && definition.Id == "new_game")
			{
				await host.StartDefaultNewGameAsync();
				host.EnterGame();
				host.FlushMap();
				await host.WaitStepAsync(host.AutoTestConfig.AutoTestStepDelay);
			}
			else if (!string.IsNullOrEmpty(definition.PresetScenarioId))
			{
				var loadStatus = await host.LoadPresetScenarioAsync(definition.PresetScenarioId);
				if (loadStatus != SaveLoadStatus.Success)
				{
					RecordCase(
						scenario,
						$"{definition.Id}.load_preset",
						"fail",
						"error",
						$"Failed to load preset scenario '{definition.PresetScenarioId}' ({loadStatus}).");
					scenario.FinishedAtUtc = DateTimeOffset.UtcNow;
					return;
				}

				host.EnterGame();
				host.FlushMap();
				await host.WaitStepAsync(host.AutoTestConfig.AutoTestStepDelay);
			}

			var context = new AutoTestScenarioContext(this, host, scenario);
			await definition.ExecuteAsync(context);
		}
		catch (Exception ex)
		{
			RecordCase(scenario, $"{definition.Id}.unhandled_exception", "fail", "error", "Unhandled scenario exception.", ex);
		}
		finally
		{
			scenario.FinishedAtUtc = DateTimeOffset.UtcNow;
			GD.Print($"[AutoTest] Scenario end: {definition.Id} => {scenario.Status}");
		}
	}

	private async Task RunResourceSmokeAsync(AutoTestScenarioContext context)
	{
		var startupSnapshot = context.Snapshot();
		context.Check(
			context.Host.ResourcesReady,
			"resource_smoke.startup.resources_ready",
			$"Expected startup resources ready; actual ready={context.Host.ResourcesReady}.",
			$"Expected startup resources ready; actual ready={context.Host.ResourcesReady} state={startupSnapshot.StartupState} load_path={startupSnapshot.StartupLoadPath ?? "<none>"}.");
		context.Check(
			string.Equals(startupSnapshot.StartupState, "ready", StringComparison.Ordinal),
			"resource_smoke.startup.state_ready",
			$"Expected startup_state=ready; actual startup_state={startupSnapshot.StartupState}.",
			$"Expected startup_state=ready; actual startup_state={startupSnapshot.StartupState} load_path={startupSnapshot.StartupLoadPath ?? "<none>"}.");
		context.Pass(
			"resource_smoke.startup.sync_fallback_observed",
			$"Observed startup sync fallback used={startupSnapshot.StartupSyncFallbackUsed} last_path={startupSnapshot.StartupLoadPath ?? "<none>"}.");		
		RecordViewportAvailability(
			context,
			"resource_smoke.layout.viewport_nonzero",
			"viewport",
			startupSnapshot.ViewportWidth,
			startupSnapshot.ViewportHeight,
			startupSnapshot.HeadlessMode);
		RecordViewportAvailability(
			context,
			"resource_smoke.layout.map_viewport_nonzero",
			"map viewport",
			startupSnapshot.MapViewportWidth,
			startupSnapshot.MapViewportHeight,
			startupSnapshot.HeadlessMode);

		foreach (var path in ResourceScenePaths)
			ProbeResourceLoad(context, $"resource_smoke.load.{SanitizeId(Path.GetFileName(path))}", path);

		ProbeResourceLoad(context, "resource_smoke.load.heavy_tileset", HeavyTileSetPath);
		foreach (var path in TileMapRenderModule.EnumerateWeatherAssetPaths())
			ProbeResourceLoad(context, $"resource_smoke.load.{SanitizeId(Path.GetFileNameWithoutExtension(path))}", path);

		try
		{
			var scenarios = PresetScenarioCatalog.List();
			context.Check(scenarios.Count > 0,
				"resource_smoke.preset_manifest.load",
				$"Preset scenario manifest loaded ({scenarios.Count} entries).",
				"Preset scenario manifest did not return any entries.");

			foreach (var scenario in scenarios)
			{
				var json = PresetScenarioCatalog.ReadText(scenario.TemplatePath);
				var saveFile = SaveModule.DeserializeSaveFile(json);
				context.Check(saveFile != null,
					$"resource_smoke.preset.{scenario.Id}.deserialize",
					$"Preset scenario '{scenario.Id}' deserialized.",
					$"Preset scenario '{scenario.Id}' failed to deserialize.");
			}
		}
		catch (Exception ex)
		{
			context.Fail("resource_smoke.preset_manifest.exception", "Preset scenario manifest probe threw an exception.", ex);
		}

		await context.StepAsync();
	}

	private async Task RunNewGameScenarioAsync(AutoTestScenarioContext context)
	{
		var state = context.State;
		var player = ActorModule.GetPlayer(state);

		context.Check(state.World != null,
			"new_game.session.world_exists",
			"Expected world initialized for new game; actual world!=null.",
			"Expected world initialized for new game; actual world was null.");
		context.Check(player != null,
			"new_game.session.player_exists",
			$"Expected player actor present after new game; actual player_id={player?.Id ?? "<null>"}.",
			"Expected player actor present after new game; actual player was missing.");
		context.Check(context.Session.GameStarted,
			"new_game.session.game_started",
			$"Expected game_started=true; actual game_started={context.Session.GameStarted}.",
			$"Expected game_started=true; actual game_started={context.Session.GameStarted}.");
		context.Check(state.Turn == 0,
			"new_game.session.turn_zero",
			"Expected turn=0 after new game; actual turn=0.",
			$"Expected turn=0 after new game; actual turn={state.Turn}.");

		if (player == null || state.World == null)
			return;

		await context.StepAsync();
		AssertWeatherSnapshotConsistency(context, "new_game.weather.surface_sample");

		var turnBeforeMove = state.Turn;
		var xBeforeMove = player.X;
		var yBeforeMove = player.Y;
		context.Host.ExecuteCommand("d");
		context.Host.FlushMap();
		await context.StepAsync();

		var moved = player.X != xBeforeMove || player.Y != yBeforeMove;
		context.Check(moved || state.Turn > turnBeforeMove,
			"new_game.command.move_or_tick",
			$"Expected move command to change position or turn; actual moved={moved} turn_before={turnBeforeMove} turn_after={state.Turn}.",
			$"Expected move command to change position or turn; actual moved={moved} turn_before={turnBeforeMove} turn_after={state.Turn}.");
		context.Check(state.Turn > turnBeforeMove,
			"new_game.command.turn_advanced",
			$"Expected turn_after > {turnBeforeMove}; actual turn_after={state.Turn}.",
			$"Expected turn_after > {turnBeforeMove}; actual turn_after={state.Turn}.");

		var goldBefore = player.Gold;
		context.Host.ExecuteCommand("/gold 500");
		context.Check(player.Gold == goldBefore + 500,
			"new_game.command.debug_gold",
			$"Expected player_gold={goldBefore + 500}; actual player_gold={player.Gold}.",
			$"Expected player_gold={goldBefore + 500}; actual player_gold={player.Gold}.");

		foreach (var limb in player.Limbs)
			limb.Durability = 1;
		context.Host.ExecuteCommand("/heal");
		context.Check(player.Limbs.All(static limb => limb.Durability >= limb.MaxDurability),
			"new_game.command.debug_heal",
			"Expected all limb durabilities restored to max; actual all limbs restored.",
			$"Expected all limb durabilities restored to max; actual damaged_limb_count={player.Limbs.Count(static limb => limb.Durability < limb.MaxDurability)}.");

		var savePath = NormalizeFilePath(Path.Combine(OS.GetUserDataDir(), "save", "_autotest_temp.json"));
		var legacyPath = NormalizeFilePath(Path.Combine(OS.GetUserDataDir(), "save", "_autotest_legacy.json"));
		var version4Path = NormalizeFilePath(Path.Combine(OS.GetUserDataDir(), "save", "_autotest_v4.json"));

		try
		{
			var surfaceWeatherBeforeSave = WeatherRules.GetLocalWeather(state, state.PlayerX, state.PlayerY, state.PlayerZ);
			var weatherPhaseBeforeSave = state.Weather.FrontPhase;
			context.Session.SaveGame(savePath);
			context.Check(File.Exists(savePath),
				"new_game.save.file_created",
				$"Expected save file to exist at '{savePath}'; actual exists={File.Exists(savePath)}.",
				$"Expected save file to exist at '{savePath}'; actual exists={File.Exists(savePath)}.");

			context.Check(
				TryReadSaveVersion(savePath, out var saveVersion) && saveVersion == SaveModule.CurrentVersion,
				"new_game.save.version_current",
				$"Expected save version={SaveModule.CurrentVersion}; actual version={saveVersion}.",
				$"Expected save version={SaveModule.CurrentVersion}; actual version={saveVersion}.");			
			var savedWeatherPhaseAvailable = TryReadSaveWeatherPhase(savePath, out var savedWeatherPhase);
			context.Check(
				savedWeatherPhaseAvailable && NearlyEqual(savedWeatherPhase, weatherPhaseBeforeSave),
				"new_game.save.weather_payload_phase",
				$"Expected saved weather phase={weatherPhaseBeforeSave:F4}; actual payload phase={savedWeatherPhase:F4}.",
				$"Expected payload weather.frontPhase={weatherPhaseBeforeSave:F4}; actual payload phase={savedWeatherPhase:F4}.");

			var headerStatus = SaveModule.TryReadSaveHeader(savePath, out var header);
			context.Check(headerStatus == SaveLoadStatus.Success,
				"new_game.save.header_status",
				$"Expected header_status={SaveLoadStatus.Success}; actual header_status={headerStatus}.",
				$"Expected header_status={SaveLoadStatus.Success}; actual header_status={headerStatus}.");
			context.Check(header != null && header.Turn == state.Turn,
				"new_game.save.header_turn",
				$"Expected header.turn={state.Turn}; actual header.turn={header?.Turn}.",
				$"Expected header.turn={state.Turn}; actual header.turn={header?.Turn}.");
			context.Check(header != null && header.PlayerZ == state.PlayerZ,
				"new_game.save.header_player_z",
				$"Expected header.playerZ={state.PlayerZ}; actual header.playerZ={header?.PlayerZ}.",
				$"Expected header.playerZ={state.PlayerZ}; actual header.playerZ={header?.PlayerZ}.");
			context.Check(header != null && string.Equals(header.GeneratorId, state.GeneratorId, StringComparison.Ordinal),
				"new_game.save.header_generator_id",
				$"Expected header.generatorId={state.GeneratorId}; actual header.generatorId={header?.GeneratorId ?? "<null>"}.",
				$"Expected header.generatorId={state.GeneratorId}; actual header.generatorId={header?.GeneratorId ?? "<null>"}.");
			if (!string.IsNullOrWhiteSpace(header?.WorldId) || !string.IsNullOrWhiteSpace(header?.CharacterId))
			{
				context.Check(
					!string.IsNullOrWhiteSpace(header?.WorldId)
					&& !string.IsNullOrWhiteSpace(header?.CharacterId)
					&& !string.IsNullOrWhiteSpace(header?.CharacterName),
					"new_game.save.header_world_character_metadata",
					$"Expected world/character metadata populated; actual worldId={header?.WorldId ?? "<null>"} characterId={header?.CharacterId ?? "<null>"} characterName={header?.CharacterName ?? "<null>"}.",
					$"Expected world/character metadata populated; actual worldId={header?.WorldId ?? "<null>"} characterId={header?.CharacterId ?? "<null>"} characterName={header?.CharacterName ?? "<null>"}.");
			}

			var seedBefore = state.WorldSeed;
			var playerXBefore = state.PlayerX;
			var loadStatus = context.Session.LoadGame(savePath);
			context.Check(loadStatus == SaveLoadStatus.Success,
				"new_game.load.status",
				$"Expected load_status={SaveLoadStatus.Success}; actual load_status={loadStatus}.",
				$"Expected load_status={SaveLoadStatus.Success}; actual load_status={loadStatus}.");
			context.Check(state.WorldSeed == seedBefore,
				"new_game.load.world_seed",
				$"Expected world_seed={seedBefore}; actual world_seed={state.WorldSeed}.",
				$"Expected world_seed={seedBefore}; actual world_seed={state.WorldSeed}.");
			context.Check(state.PlayerX == playerXBefore,
				"new_game.load.player_x",
				$"Expected player_x={playerXBefore}; actual player_x={state.PlayerX}.",
				$"Expected player_x={playerXBefore}; actual player_x={state.PlayerX}.");

			context.Host.FlushMap();
			await context.StepAsync();
			player = ActorModule.GetPlayer(state);
			AssertWeatherSnapshotConsistency(context, "new_game.weather.surface_after_load");
			if (player != null)
			{
				var surfaceWeatherAfterLoad = WeatherRules.GetLocalWeather(state, player.X, player.Y, player.Z);
				var expectedPhaseAfterLoad = savedWeatherPhaseAvailable ? savedWeatherPhase : weatherPhaseBeforeSave;
				context.Check(
					string.Equals(surfaceWeatherAfterLoad.TypeId, surfaceWeatherBeforeSave.TypeId, StringComparison.Ordinal)
					&& string.Equals(surfaceWeatherAfterLoad.IntensityId, surfaceWeatherBeforeSave.IntensityId, StringComparison.Ordinal)
					&& NearlyEqual(state.Weather.FrontPhase, expectedPhaseAfterLoad),
					"new_game.weather.persisted_after_load",
					$"Expected weather to persist across save/load; actual type={surfaceWeatherAfterLoad.TypeId} intensity={surfaceWeatherAfterLoad.IntensityId} phase={state.Weather.FrontPhase:F4}.",
					$"Expected weather type={surfaceWeatherBeforeSave.TypeId} intensity={surfaceWeatherBeforeSave.IntensityId} phase={expectedPhaseAfterLoad:F4}; actual type={surfaceWeatherAfterLoad.TypeId} intensity={surfaceWeatherAfterLoad.IntensityId} phase={state.Weather.FrontPhase:F4}.");
			}

			File.WriteAllText(legacyPath, "{\"turn\":1,\"playerX\":2}");
			var slots = context.Session.ListSaveSlots();
			var legacySlot = slots.FirstOrDefault(slot =>
				string.Equals(NormalizeFilePath(slot.SourcePath), legacyPath, StringComparison.OrdinalIgnoreCase));
			var incompatiblePrefix = LocalizationService.T("ui.save_browser.summary.incompatible", ("timestamp", ""))
				.Split('|', StringSplitOptions.TrimEntries)[0];
			context.Check(legacySlot != null,
				"new_game.save.legacy_visible",
				$"Expected incompatible legacy save visible in save list; actual visible={legacySlot != null}.",
				$"Expected incompatible legacy save visible in save list; actual visible={legacySlot != null}.");
			context.Check(
				legacySlot != null && legacySlot.Summary.Contains(incompatiblePrefix, StringComparison.Ordinal),
				"new_game.save.legacy_labeled",
				$"Expected legacy summary to contain incompatible label '{incompatiblePrefix}'; actual summary='{legacySlot?.Summary ?? "<null>"}'.",
				$"Expected legacy summary to contain incompatible label '{incompatiblePrefix}'; actual summary='{legacySlot?.Summary ?? "<null>"}'.");

			if (TryRewriteSaveVersion(savePath, version4Path, 4))
			{
				var version4HeaderStatus = SaveModule.TryReadSaveHeader(version4Path, out _);
				context.Check(
					version4HeaderStatus == SaveLoadStatus.Incompatible,
					"new_game.save.version4.header_incompatible",
					$"Expected version 4 header status={SaveLoadStatus.Incompatible}; actual header_status={version4HeaderStatus}.",
					$"Expected version 4 header status={SaveLoadStatus.Incompatible}; actual header_status={version4HeaderStatus}.");
				var version4LoadStatus = context.Session.LoadGame(version4Path);
				context.Check(
					version4LoadStatus == SaveLoadStatus.Incompatible,
					"new_game.save.version4.load_incompatible",
					$"Expected version 4 load status={SaveLoadStatus.Incompatible}; actual load_status={version4LoadStatus}.",
					$"Expected version 4 load status={SaveLoadStatus.Incompatible}; actual load_status={version4LoadStatus}.");
			}
			else
			{
				context.Fail(
					"new_game.save.version4.prepare",
					$"Expected version 4 fixture to be created from '{savePath}'; actual fixture creation failed.");
			}

			var legacyHeaderStatus = SaveModule.TryReadSaveHeader(legacyPath, out _);
			context.Check(
				legacyHeaderStatus == SaveLoadStatus.Incompatible,
				"new_game.save.legacy.header_incompatible",
				$"Expected legacy header status={SaveLoadStatus.Incompatible}; actual header_status={legacyHeaderStatus}.",
				$"Expected legacy header status={SaveLoadStatus.Incompatible}; actual header_status={legacyHeaderStatus}.");
			var legacyLoadStatus = context.Session.LoadGame(legacyPath);
			context.Check(
				legacyLoadStatus == SaveLoadStatus.Incompatible,
				"new_game.save.legacy.load_incompatible",
				$"Expected legacy load status={SaveLoadStatus.Incompatible}; actual load_status={legacyLoadStatus}.",
				$"Expected legacy load status={SaveLoadStatus.Incompatible}; actual load_status={legacyLoadStatus}.");

			var zBefore = state.PlayerZ;
			context.Session.ChangeFloor(goDown: true);
			context.Check(state.PlayerZ == zBefore + 1,
				"new_game.floor.down",
				$"Expected player_z={zBefore + 1} after floor down; actual player_z={state.PlayerZ}.",
				$"Expected player_z={zBefore + 1} after floor down; actual player_z={state.PlayerZ}.");
			context.Host.FlushMap();
			await context.StepAsync();
			player = ActorModule.GetPlayer(state);
			if (player != null && state.World != null)
			{
				var undergroundSurface = WeatherSurface.GetSurfaceState(state, player.X, player.Y, player.Z);
				var undergroundWeather = WeatherRules.GetLocalWeather(state, player.X, player.Y, player.Z);
				context.Check(
					!undergroundSurface.IsExposed,
					"new_game.weather.underground_not_exposed",
					$"Expected underground surface not weather exposed; actual exposed={undergroundSurface.IsExposed}.",
					$"Expected underground surface not weather exposed; actual exposed={undergroundSurface.IsExposed} at pos={FormatPosition(player.X, player.Y, player.Z)}.");
				context.Check(
					undergroundWeather.Type == WeatherType.Clear && undergroundWeather.Intensity == WeatherIntensity.Normal,
					"new_game.weather.underground_clear",
					$"Expected underground weather clear/normal; actual type={undergroundWeather.TypeId} intensity={undergroundWeather.IntensityId}.",
					$"Expected underground weather clear/normal; actual type={undergroundWeather.TypeId} intensity={undergroundWeather.IntensityId}.");
			}

			context.Session.ChangeFloor(goDown: false);
			context.Check(state.PlayerZ == zBefore,
				"new_game.floor.up",
				$"Expected player_z={zBefore} after floor up; actual player_z={state.PlayerZ}.",
				$"Expected player_z={zBefore} after floor up; actual player_z={state.PlayerZ}.");
			context.Host.FlushMap();
		}
		finally
		{
			TryDelete(savePath);
			TryDelete(legacyPath);
			TryDelete(version4Path);
		}

		await RunVisionBaselineAsync(context, "new_game");
	}

	private async Task RunSmokeCoreScenarioAsync(AutoTestScenarioContext context)
	{
		var state = context.State;
		var player = ActorModule.GetPlayer(state);
		context.Check(string.Equals(context.Session.CurrentPresetScenarioId, "qa_smoke_core", StringComparison.Ordinal),
			"qa_smoke_core.session.current_preset",
			"Expected current preset scenario to be 'qa_smoke_core'; actual preset='qa_smoke_core'.",
			$"Expected current preset scenario to be 'qa_smoke_core'; actual preset='{context.Session.CurrentPresetScenarioId ?? "<null>"}'.");
		context.Check(state.World != null,
			"qa_smoke_core.session.world_exists",
			"Expected QA smoke core world loaded; actual world!=null.",
			"Expected QA smoke core world loaded; actual world was null.");
		context.Check(player != null,
			"qa_smoke_core.session.player_exists",
			$"Expected QA smoke core player exists; actual player_id={player?.Id ?? "<null>"}.",
			"Expected QA smoke core player exists; actual player actor was missing.");
		context.Check(state.Actors.Count > 0,
			"qa_smoke_core.session.actors_present",
			$"Expected actor_count > 0; actual actor_count={state.Actors.Count}.",
			$"Expected actor_count > 0; actual actor_count={state.Actors.Count}.");

		TryFlushMap(context, "qa_smoke_core.render.flush");
		AssertWeatherSnapshotConsistency(context, "qa_smoke_core.weather.surface_sample");
		if (player != null)
			RunHealthSmokeChecks(context, "qa_smoke_core.health", player);

		try
		{
			var lookText = LookModule.BuildLookText(state, context.FogTracker);
			context.Check(!string.IsNullOrWhiteSpace(lookText),
				"qa_smoke_core.look.non_empty",
				$"Expected LookModule text non-empty; actual length={lookText?.Length ?? 0}.",
				$"Expected LookModule text non-empty; actual length={lookText?.Length ?? 0}.");
		}
		catch (Exception ex)
		{
			context.Fail("qa_smoke_core.look.exception", "LookModule threw an exception.", ex);
		}

		var turnBefore = state.Turn;
		try
		{
			var events = TurnModule.Tick(state);
			context.Host.Dispatch(events);
			context.Host.FlushMap();
			context.Check(state.Turn > turnBefore,
				"qa_smoke_core.turn.tick",
				$"Expected turn_after > {turnBefore}; actual turn_after={state.Turn}.",
				$"Expected turn_after > {turnBefore}; actual turn_after={state.Turn}.");
			AssertWeatherSnapshotConsistency(context, "qa_smoke_core.weather.after_tick");
			var tickSnapshot = context.Snapshot();
			context.Check(
				!string.IsNullOrWhiteSpace(tickSnapshot.WeatherTypeId)
				&& !string.IsNullOrWhiteSpace(tickSnapshot.WeatherIntensityId),
				"qa_smoke_core.weather.snapshot_populated",
				$"Expected weather snapshot fields populated after tick; actual type={tickSnapshot.WeatherTypeId ?? "<null>"} intensity={tickSnapshot.WeatherIntensityId ?? "<null>"}.",
				$"Expected weather snapshot fields populated after tick; actual type={tickSnapshot.WeatherTypeId ?? "<null>"} intensity={tickSnapshot.WeatherIntensityId ?? "<null>"}.");
		}
		catch (Exception ex)
		{
			context.Fail("qa_smoke_core.turn.exception", "TurnModule.Tick threw an exception.", ex);
		}

		await context.StepAsync();
	}

	private async Task RunInteractionHubScenarioAsync(AutoTestScenarioContext context)
	{
		var state = context.State;
		var player = ActorModule.GetPlayer(state);
		context.Check(player != null,
			"qa_interaction_hub.player.exists",
			"Interaction hub player exists.",
			"Interaction hub player was missing.");
		if (player == null || state.World == null)
			return;

		context.Host.ExecuteCommand(":inventory");
		context.Check(context.Snapshot().InventoryOpen,
			"qa_interaction_hub.inventory.open",
			"Inventory panel opens through command.",
			"Inventory panel did not open through command.");
		context.Host.ExecuteCommand(":inventory");
		context.Check(!context.Snapshot().InventoryOpen,
			"qa_interaction_hub.inventory.close",
			"Inventory panel closes through command toggle.",
			"Inventory panel did not close through command toggle.");

		var pickupCandidate = FindGroundItem(state, static item => !item.IsContainer, radius: 12);
		if (pickupCandidate == null)
		{
			context.Fail("qa_interaction_hub.pickup.find_item", "Could not find a non-container ground item to test pickup.");
		}
		else
		{
			ActorModule.MoveActor(state, player.Id, pickupCandidate.X, pickupCandidate.Y, pickupCandidate.Z);
			context.Host.FlushMap();
			await context.StepAsync();

			var inventoryBefore = player.Inventory.Count;
			var pickupEvents = InteractionModule.PickupItem(state, player, pickupCandidate.Item.InstanceId);
			context.Host.Dispatch(pickupEvents);
			context.Host.FlushMap();
			await context.StepAsync();

			context.Check(player.Inventory.Count == inventoryBefore + 1,
				"qa_interaction_hub.pickup.consume_ground_item",
				$"Expected inventory_count={inventoryBefore + 1} after pickup; actual inventory_count={player.Inventory.Count}.",
				$"Expected inventory_count={inventoryBefore + 1} after pickup; actual inventory_count={player.Inventory.Count}.");
			var pickupSnapshot = context.Snapshot();
			var expectedPickupName = IdentificationModule.GetItemDisplayName(state, pickupCandidate.Item);
			context.Check(
				pickupSnapshot.LastLogLine?.Contains(expectedPickupName, StringComparison.Ordinal) == true,
				"qa_interaction_hub.pickup.identification_log",
				$"Expected pickup log to include '{expectedPickupName}'; actual last_log_line='{pickupSnapshot.LastLogLine ?? "<null>"}'.",
				$"Expected pickup log to include runtime item display name '{expectedPickupName}'; actual last_log_line='{pickupSnapshot.LastLogLine ?? "<null>"}'.");

			var dropIndex = player.Inventory.FindIndex(item => item.Id == pickupCandidate.Item.Id && !item.Equipped);
			if (dropIndex >= 0)
			{
				var expectedDropName = IdentificationModule.GetItemDisplayName(state, player.Inventory[dropIndex]);
				var dropEvents = InteractionModule.DropItem(state, player, dropIndex);
				context.Host.Dispatch(dropEvents);
				context.Host.FlushMap();
				await context.StepAsync();

				var dropped = MapModule.PeekGroundItems(state, player.X, player.Y)
					.Any(item => item.Id == pickupCandidate.Item.Id);
				context.Check(dropped,
					"qa_interaction_hub.drop.restore_ground_item",
					$"Expected dropped item '{pickupCandidate.Item.Id}' on ground; actual dropped={dropped}.",
					$"Expected dropped item '{pickupCandidate.Item.Id}' on ground; actual dropped={dropped}.");
				var dropSnapshot = context.Snapshot();
				context.Check(
					dropSnapshot.LastLogLine?.Contains(expectedDropName, StringComparison.Ordinal) == true,
					"qa_interaction_hub.drop.identification_log",
					$"Expected drop log to include '{expectedDropName}'; actual last_log_line='{dropSnapshot.LastLogLine ?? "<null>"}'.",
					$"Expected drop log to include runtime item display name '{expectedDropName}'; actual last_log_line='{dropSnapshot.LastLogLine ?? "<null>"}'.");
			}
			else
			{
				context.Fail("qa_interaction_hub.drop.find_inventory_item", "Could not locate the picked-up item in inventory for drop test.");
			}
		}

		var merchant = state.Actors.Values.FirstOrDefault(actor => actor.Id.Contains("merchant", StringComparison.OrdinalIgnoreCase));
		if (merchant == null)
		{
			context.Fail("qa_interaction_hub.trade.find_merchant", "Could not find merchant actor in interaction hub.");
		}
		else
		{
			MovePlayerAdjacentToActor(state, player, merchant);
			var tradeDef = InteractionModule
				.GetInteractions(player, merchant, InteractionDefs.All)
				.FirstOrDefault(def => string.Equals(def.EffectType, "trade", StringComparison.Ordinal));
			if (tradeDef == null)
			{
				context.Fail("qa_interaction_hub.trade.find_interaction", "Could not find trade interaction for merchant.");
			}
			else
			{
				var tradeEvents = InteractionModule.Execute(state, player, merchant, tradeDef);
				context.Host.Dispatch(tradeEvents);
				await context.StepAsync();

				var tradeSnapshot = context.Snapshot();
				context.Check(tradeSnapshot.TradeOpen,
					"qa_interaction_hub.trade.open_panel",
					$"Expected trade_open=true after merchant interaction; actual trade_open={tradeSnapshot.TradeOpen}.",
					$"Expected trade_open=true after merchant interaction; actual trade_open={tradeSnapshot.TradeOpen}.");
				var expectedTraderName = IdentificationModule.GetActorDisplayName(state, merchant);
				context.Check(
					tradeSnapshot.LastLogLine?.Contains(expectedTraderName, StringComparison.Ordinal) == true,
					"qa_interaction_hub.trade.identification_log",
					$"Expected trade start log to include '{expectedTraderName}'; actual last_log_line='{tradeSnapshot.LastLogLine ?? "<null>"}'.",
					$"Expected trade start log to include runtime actor display name '{expectedTraderName}'; actual last_log_line='{tradeSnapshot.LastLogLine ?? "<null>"}'.");

				var goods = TradeModule.ListGoods(merchant);
				if (goods.Count == 0)
				{
					context.Fail("qa_interaction_hub.trade.goods_present", "Merchant had no goods to trade.");
				}
				else
				{
					var inventoryBefore = player.Inventory.Count;
					var goldBefore = player.Gold;
					var result = TradeModule.Buy(player, merchant, goods[0]);
					context.Check(result.Ok,
						"qa_interaction_hub.trade.buy_first_good",
						$"Expected TradeModule.Buy ok=true for '{goods[0].Item.Id}'; actual ok={result.Ok}.",
						$"Expected TradeModule.Buy ok=true for '{goods[0].Item.Id}'; actual ok={result.Ok} message='{result.Message}'.");
					if (result.Ok)
					{
						context.Check(player.Inventory.Count >= inventoryBefore,
							"qa_interaction_hub.trade.inventory_changed",
							$"Expected inventory_count >= {inventoryBefore} after trade; actual inventory_count={player.Inventory.Count}.",
							$"Expected inventory_count >= {inventoryBefore} after trade; actual inventory_count={player.Inventory.Count}.");
						context.Check(player.Gold <= goldBefore,
							"qa_interaction_hub.trade.gold_spent",
							$"Expected player_gold <= {goldBefore} after trade; actual player_gold={player.Gold}.",
							$"Expected player_gold <= {goldBefore} after trade; actual player_gold={player.Gold}.");
					}
				}
			}
		}

		var villager = state.Actors.Values.FirstOrDefault(actor =>
			actor.Id.Contains("villager", StringComparison.OrdinalIgnoreCase)
			|| actor.Id.Contains("blacksmith", StringComparison.OrdinalIgnoreCase));
		if (villager == null)
		{
			context.Fail("qa_interaction_hub.dialog.find_target", "Could not find dialog target actor in interaction hub.");
		}
		else
		{
			MovePlayerAdjacentToActor(state, player, villager);
			var talkDef = InteractionModule
				.GetInteractions(player, villager, InteractionDefs.All)
				.FirstOrDefault(def => string.Equals(def.EffectType, "talk", StringComparison.Ordinal));
			if (talkDef == null)
			{
				context.Fail("qa_interaction_hub.dialog.find_interaction", "Could not find talk interaction for dialog target.");
			}
			else
			{
				var talkEvents = InteractionModule.Execute(state, player, villager, talkDef);
				context.Host.Dispatch(talkEvents);
				await context.StepAsync();

				var dialogSnapshot = context.Snapshot();
				context.Check(dialogSnapshot.DialogOpen,
					"qa_interaction_hub.dialog.open_panel",
					$"Expected dialog_open=true after talk interaction; actual dialog_open={dialogSnapshot.DialogOpen}.",
					$"Expected dialog_open=true after talk interaction; actual dialog_open={dialogSnapshot.DialogOpen}.");
				context.Check(dialogSnapshot.DialogOptionCount > 0,
					"qa_interaction_hub.dialog.options_present",
					$"Expected dialog_option_count > 0; actual dialog_option_count={dialogSnapshot.DialogOptionCount}.",
					$"Expected dialog_option_count > 0; actual dialog_option_count={dialogSnapshot.DialogOptionCount}.");
				context.Check(
					!dialogSnapshot.TradeOpen
					&& dialogSnapshot.TradeItemCount == 0
					&& string.Equals(dialogSnapshot.FocusedPanelId, "dialog", StringComparison.Ordinal),
					"qa_interaction_hub.dialog.trade_exclusive",
					$"Expected dialog state exclusive with trade closed; actual trade_open={dialogSnapshot.TradeOpen} trade_item_count={dialogSnapshot.TradeItemCount} focused_panel_id={dialogSnapshot.FocusedPanelId ?? "<null>"}.",
					$"Expected dialog state exclusive with trade_open=false trade_item_count=0 focused_panel_id=dialog; actual trade_open={dialogSnapshot.TradeOpen} trade_item_count={dialogSnapshot.TradeItemCount} focused_panel_id={dialogSnapshot.FocusedPanelId ?? "<null>"}.");
			}
		}
	}

	private async Task RunCombatArenaScenarioAsync(AutoTestScenarioContext context)
	{
		var state = context.State;
		var player = ActorModule.GetPlayer(state);
		if (player == null || state.World == null)
		{
			context.Fail("qa_combat_arena.player.exists", "Combat arena player or world was missing.");
			return;
		}

		var target = ActorModule.GetById(state, "arena_goblin") ?? ActorModule.GetAllHostile(state).FirstOrDefault();
		context.Check(target != null,
			"qa_combat_arena.target.find_primary_enemy",
			$"Expected hostile combat target present; actual target_id={target?.Id ?? "<null>"}.",
			$"Expected hostile combat target present; actual target_id={target?.Id ?? "<null>"}.");
		if (target == null)
			return;

		MovePlayerAdjacentToActor(state, player, target);
		context.Host.FlushMap();
		await context.StepAsync();

		var attackSkills = SkillQuery.GetAttackSkills(player);
		var selectedSkill = attackSkills.FirstOrDefault(static skill => ActionModule.ResolveSkillTargetType(skill) == SkillTargetType.Actor)
			?? InteractionDefs.Get("melee_attack");
		context.Check(selectedSkill != null,
			"qa_combat_arena.skill.find_attack_skill",
			$"Expected actor-targeting combat skill available; actual skill_id={selectedSkill?.Id ?? "<null>"}.",
			$"Expected actor-targeting combat skill available; actual skill_id={selectedSkill?.Id ?? "<null>"}.");
		if (selectedSkill == null)
			return;

		var durabilityBefore = GetTotalDurability(target);
		var castResult = ActionModule.TryCastSkill(
			state,
			player,
			selectedSkill.Id,
			SkillTargetType.Actor,
			targetActor: target);
		context.Host.Dispatch(castResult.Events);
		context.Host.FlushMap();
		await context.StepAsync();

		context.Check(castResult.Consumed,
			"qa_combat_arena.skill.cast_consumed",
			$"Expected skill '{selectedSkill.Id}' to consume an action; actual consumed={castResult.Consumed}.",
			$"Expected skill '{selectedSkill.Id}' to consume an action; actual consumed={castResult.Consumed}.");
		context.Check(castResult.Events.Any(evt => evt.Type == "combat_attack"),
			"qa_combat_arena.skill.combat_attack_event",
			$"Expected skill '{selectedSkill.Id}' to emit combat_attack event; actual event_count={castResult.Events.Count(evt => evt.Type == "combat_attack")}.",
			$"Expected skill '{selectedSkill.Id}' to emit combat_attack event; actual event_count={castResult.Events.Count(evt => evt.Type == "combat_attack")}.");
		context.Check(GetTotalDurability(target) <= durabilityBefore,
			"qa_combat_arena.skill.damage_applied",
			$"Expected target durability <= {durabilityBefore:F1}; actual durability={GetTotalDurability(target):F1}.",
			$"Expected target durability <= {durabilityBefore:F1}; actual durability={GetTotalDurability(target):F1}.");

		var turnBefore = state.Turn;
		var expectedObserverCount = CountScheduledAiObservers(
			state,
			player.X,
			player.Y,
			Math.Max(0, GameConfig.AIVision.ActivationViewRange),
			state.Turn + 1);
		context.Check(
			expectedObserverCount > 0,
			"qa_combat_arena.ai.preconditions",
			$"Expected scheduled AI observers > 0 before tick; actual scheduled_observer_count={expectedObserverCount}.",
			$"Expected scheduled AI observers > 0 before tick; actual scheduled_observer_count={expectedObserverCount}.");
		try
		{
			var events = TurnModule.Tick(state);
			context.Host.Dispatch(events);
			context.Host.FlushMap();
			context.Check(state.Turn > turnBefore,
				"qa_combat_arena.ai.tick_turn_advanced",
				$"Expected turn_after > {turnBefore}; actual turn_after={state.Turn}.",
				$"Expected turn_after > {turnBefore}; actual turn_after={state.Turn}.");
			var metrics = CreateAIVisionMetrics();
			context.Check(metrics.GetValueOrDefault("observer_count") > 0,
				"qa_combat_arena.ai.metrics_recorded",
				$"Expected observer_count > 0; actual observer_count={metrics.GetValueOrDefault("observer_count"):F0}.",
				$"Expected observer_count > 0; actual observer_count={metrics.GetValueOrDefault("observer_count"):F0} candidate_count={metrics.GetValueOrDefault("candidate_count"):F0} visible_actor_count={metrics.GetValueOrDefault("visible_actor_count"):F0}.",
				metrics);
			metrics["expected_observer_count"] = expectedObserverCount;
			context.Check(
				Math.Abs(metrics.GetValueOrDefault("observer_count") - expectedObserverCount) < 0.5d,
				"qa_combat_arena.ai.metrics_match_expected",
				$"Expected observer_count={expectedObserverCount}; actual observer_count={metrics.GetValueOrDefault("observer_count"):F0}.",
				$"Expected observer_count={expectedObserverCount}; actual observer_count={metrics.GetValueOrDefault("observer_count"):F0}.",
				metrics);
		}
		catch (Exception ex)
		{
			context.Fail("qa_combat_arena.ai.dispatch_exception", "AIDispatcher.TickAll threw an exception.", ex);
		}

		await RunBenchmarkSuiteAsync(context);
	}

	private async Task RunBenchmarkSuiteAsync(AutoTestScenarioContext context)
	{
		var state = context.State;
		var player = ActorModule.GetPlayer(state);
		if (player == null || state.World == null)
		{
			context.Fail("qa_combat_arena.benchmark.player_exists", "Benchmark suite could not find player or world.");
			return;
		}

		PrepareVisionArena(state, context.Config.AutoTestBenchmarkArenaRadius);

		foreach (var count in context.Config.AutoTestBenchmarkCounts)
		{
			var prefix = $"bench_ai_{count}_";
			RemoveActorsByPrefix(state, prefix);
			SpawnBenchmarkActors(state, prefix, count);
			var scheduledObserverCount = CountScheduledAiObservers(
				state,
				player.X,
				player.Y,
				Math.Max(0, GameConfig.AIVision.ActivationViewRange),
				state.Turn + 1);
			var caseId = $"qa_combat_arena.benchmark.count_{count}";
			var preconditionMetrics = new Dictionary<string, double>
			{
				["spawned_count"] = count,
				["expected_observer_count"] = scheduledObserverCount,
			};
			var benchmarkPreconditionsMet = context.Check(
				scheduledObserverCount > 0,
				$"{caseId}.preconditions",
				$"Expected scheduled observers > 0 for benchmark spawn count {count}; actual scheduled_observer_count={scheduledObserverCount}.",
				$"Expected scheduled observers > 0 for benchmark spawn count {count}; actual scheduled_observer_count={scheduledObserverCount}.",
				preconditionMetrics);
			if (!benchmarkPreconditionsMet)
			{
				RemoveActorsByPrefix(state, prefix);
				await context.StepAsync();
				continue;
			}

			var tickResult = TurnModule.TickProfiled(state);
			var metrics = AutoTestMetricBuilder.CreateTurnTickMetrics(tickResult.Metrics);
			metrics["spawned_count"] = count;
			metrics["expected_observer_count"] = scheduledObserverCount;
			context.Check(
				Math.Abs(metrics.GetValueOrDefault("observer_count") - scheduledObserverCount) < 0.5d,
				$"{caseId}.metrics",
				$"Expected observer_count={scheduledObserverCount} for benchmark spawn count {count}; actual observer_count={metrics.GetValueOrDefault("observer_count"):F0}.",
				$"Expected observer_count={scheduledObserverCount} for benchmark spawn count {count}; actual observer_count={metrics.GetValueOrDefault("observer_count"):F0}.",
				metrics);

			if (metrics.GetValueOrDefault("vision_ms") > context.Config.AutoTestAiVisionWarnMs
				|| metrics.GetValueOrDefault("tick_ms") > context.Config.AutoTestTickWarnMs)
			{
				context.Warn(
					$"{caseId}.thresholds",
					$"Expected tick_ms<={context.Config.AutoTestTickWarnMs:F3} and vision_ms<={context.Config.AutoTestAiVisionWarnMs:F3}; actual tick_ms={metrics["tick_ms"]:F3} vision_ms={metrics["vision_ms"]:F3}.",
					metrics);
			}
			else
			{
				context.Pass(
					$"{caseId}.thresholds",
					$"Expected tick_ms<={context.Config.AutoTestTickWarnMs:F3} and vision_ms<={context.Config.AutoTestAiVisionWarnMs:F3}; actual tick_ms={metrics["tick_ms"]:F3} vision_ms={metrics["vision_ms"]:F3}.",
					metrics);
			}

			RemoveActorsByPrefix(state, prefix);
			await context.StepAsync();
		}
	}

	private async Task RunVisionBaselineAsync(AutoTestScenarioContext context, string prefix)
	{
		var state = context.State;
		var player = ActorModule.GetPlayer(state);
		if (player == null || state.World == null)
		{
			context.Fail($"{prefix}.fov.player_exists", "FOV baseline could not find player or world.");
			return;
		}

		PrepareVisionArena(state, Math.Max(context.Config.AutoTestRenderArenaRadius, 8));
		player.FacingX = 1;
		player.FacingY = 0;
		var world = state.World;

		var px = player.X;
		var py = player.Y;
		var pz = player.Z;
		var weatherExposed = world.IsWeatherExposed(px, py, pz);
		var weather = weatherExposed
			? WeatherRules.GetLocalWeather(state, px, py, pz)
			: new WeatherSample(WeatherType.Clear, WeatherIntensity.Normal);
		var visionProbe = AutoTestVisionProbeHelper.Create(
			context.FogTracker.BaseVisionRadius,
			player.GetCapacity(Caps.Sight),
			context.FogTracker.RearVisionRatio,
			context.FogTracker.MinimumVisionRadius,
			context.FogTracker.MinimumRearVisionRadius,
			context.FogTracker.AmbientLight,
			weatherExposed,
			weather);
		var vision = visionProbe.Vision;

		world.SetTerrain(px + 1, py, pz, Terrains.Floor);
		world.SetTerrain(px + 2, py, pz, Terrains.Floor);
		world.SetTerrain(px + 6, py, pz, Terrains.Floor);
		world.SetTerrain(px + 7, py, pz, Terrains.Floor);
		world.SetTerrain(px - 1, py, pz, Terrains.Floor);

		context.FogTracker.Clear();
		context.FogTracker.Update(state);
		context.Check(
			context.FogTracker.GetVisionBand(px + 1, py, pz) == PlayerVisionBand.Focused,
			$"{prefix}.fov.front_tile_focused",
			$"Expected front tile vision_band=Focused; actual vision_band={context.FogTracker.GetVisionBand(px + 1, py, pz)}.",
			$"Expected front tile vision_band=Focused; actual vision_band={context.FogTracker.GetVisionBand(px + 1, py, pz)}.");
		context.Check(
			context.FogTracker.GetVisionBand(px - 1, py, pz) == PlayerVisionBand.Focused,
			$"{prefix}.fov.rear_tile_focused",
			$"Expected rear tile vision_band=Focused under current rear-radius rules; actual vision_band={context.FogTracker.GetVisionBand(px - 1, py, pz)}.",
			$"Expected rear tile vision_band=Focused under current rear-radius rules; actual vision_band={context.FogTracker.GetVisionBand(px - 1, py, pz)}.");
		var rearBeyondFocusX = px - Math.Max(vision.RearRadius + 1, 2);
		world.SetTerrain(rearBeyondFocusX, py, pz, Terrains.Floor);
		context.Check(
			context.FogTracker.GetVisionBand(rearBeyondFocusX, py, pz) != PlayerVisionBand.Focused,
			$"{prefix}.fov.rear_far_not_focused",
			$"Expected rear tile beyond rear radius not Focused; actual vision_band={context.FogTracker.GetVisionBand(rearBeyondFocusX, py, pz)} at x={rearBeyondFocusX}.",
			$"Expected rear tile beyond rear radius not Focused; actual vision_band={context.FogTracker.GetVisionBand(rearBeyondFocusX, py, pz)} at x={rearBeyondFocusX}.");
		context.Check(
			context.FogTracker.IsVisible(state.PlayerX, state.PlayerY, state.PlayerZ),
			$"{prefix}.fov.player_visible",
			$"Expected player tile visible; actual visible={context.FogTracker.IsVisible(state.PlayerX, state.PlayerY, state.PlayerZ)}.",
			$"Expected player tile visible; actual visible={context.FogTracker.IsVisible(state.PlayerX, state.PlayerY, state.PlayerZ)}.");
		await context.StepAsync();

		world.SetTerrain(px + 1, py, pz, Terrains.WallStone);
		context.FogTracker.Update(state);
		context.Check(
			world.BlocksSight(px + 1, py, pz),
			$"{prefix}.fov.wall_blocks_sight",
			$"Expected terrain at ({px + 1},{py},{pz}) to block sight; actual blocks_sight={world.BlocksSight(px + 1, py, pz)}.",
			$"Expected terrain at ({px + 1},{py},{pz}) to block sight; actual blocks_sight={world.BlocksSight(px + 1, py, pz)}.");
		context.Check(
			context.FogTracker.GetVisionBand(px + 2, py, pz) == PlayerVisionBand.Memory,
			$"{prefix}.fov.memory_after_block",
			$"Expected blocked tile vision_band=Memory; actual vision_band={context.FogTracker.GetVisionBand(px + 2, py, pz)}.",
			$"Expected blocked tile vision_band=Memory; actual vision_band={context.FogTracker.GetVisionBand(px + 2, py, pz)}.");

		world.SetTerrain(px + 1, py, pz, Terrains.Floor);
		context.FogTracker.Update(state);

		var sightLimbs = GetCapacityLimbs(player, Caps.Sight);
		context.Check(sightLimbs.Count > 0,
			$"{prefix}.fov.sight_limbs_present",
			$"Expected sight-capable limb count > 0; actual sight_limb_count={sightLimbs.Count}.",
			$"Expected sight-capable limb count > 0; actual sight_limb_count={sightLimbs.Count}.");
		if (sightLimbs.Count > 0)
		{
			var durabilitySnapshot = SnapshotDurability(sightLimbs);

			sightLimbs[0].Durability = 0;
			var partialVisionProbe = AutoTestVisionProbeHelper.Create(
				context.FogTracker.BaseVisionRadius,
				player.GetCapacity(Caps.Sight),
				context.FogTracker.RearVisionRatio,
				context.FogTracker.MinimumVisionRadius,
				context.FogTracker.MinimumRearVisionRadius,
				context.FogTracker.AmbientLight,
				weatherExposed,
				weather);
			var partialVisibleX = px + partialVisionProbe.PartialVisibleForwardOffset;
			var partialHiddenX = px + partialVisionProbe.PartialHiddenForwardOffset;
			world.SetTerrain(partialVisibleX, py, pz, Terrains.Floor);
			world.SetTerrain(partialHiddenX, py, pz, Terrains.Floor);
			context.FogTracker.Clear();
			context.FogTracker.Update(state);
			var partialVisibleBand = context.FogTracker.GetVisionBand(partialVisibleX, py, pz);
			context.Check(
				partialVisibleBand == PlayerVisionBand.Focused,
				$"{prefix}.fov.partial_sight_visible",
				$"Expected partial-sight tile at x={partialVisibleX} vision_band=Focused; actual vision_band={partialVisibleBand}.",
				$"Expected partial-sight tile at x={partialVisibleX} vision_band=Focused; actual vision_band={partialVisibleBand}.");
			var partialHiddenBand = context.FogTracker.GetVisionBand(partialHiddenX, py, pz);
			context.Check(
				partialHiddenBand == PlayerVisionBand.Unknown,
				$"{prefix}.fov.partial_sight_range_limit",
				$"Expected beyond-range tile at x={partialHiddenX} vision_band=Unknown; actual vision_band={partialHiddenBand}.",
				$"Expected beyond-range tile at x={partialHiddenX} vision_band=Unknown; actual vision_band={partialHiddenBand}.");

			foreach (var limb in sightLimbs)
				limb.Durability = 0;
			context.FogTracker.Clear();
			context.FogTracker.Update(state);
			context.Check(
				context.FogTracker.GetVisionBand(px, py, pz) == PlayerVisionBand.Focused,
				$"{prefix}.fov.blind_self_visible",
				$"Expected blind self tile vision_band=Focused; actual vision_band={context.FogTracker.GetVisionBand(px, py, pz)}.",
				$"Expected blind self tile vision_band=Focused; actual vision_band={context.FogTracker.GetVisionBand(px, py, pz)}.");
			context.Check(
				context.FogTracker.GetVisionBand(px + 1, py, pz) == PlayerVisionBand.Unknown,
				$"{prefix}.fov.blind_adjacent_hidden",
				$"Expected blind adjacent tile vision_band=Unknown; actual vision_band={context.FogTracker.GetVisionBand(px + 1, py, pz)}.",
				$"Expected blind adjacent tile vision_band=Unknown; actual vision_band={context.FogTracker.GetVisionBand(px + 1, py, pz)}.");

			RestoreDurability(durabilitySnapshot);
			context.FogTracker.Clear();
			context.FogTracker.Update(state);
		}

		TryFlushMap(context, $"{prefix}.render.flush");
	}

	private void AssertWeatherSnapshotConsistency(AutoTestScenarioContext context, string caseId)
	{
		var state = context.State;
		var player = ActorModule.GetPlayer(state);
		if (player == null || state.World == null)
		{
			context.Fail($"{caseId}.preconditions", "Expected weather snapshot preconditions to hold; actual player or world was missing.");
			return;
		}

		var sample = WeatherRules.GetLocalWeather(state, player.X, player.Y, player.Z);
		var exposed = state.World.IsWeatherExposed(player.X, player.Y, player.Z);
		var snapshot = context.Snapshot();
		context.Check(
			string.Equals(snapshot.WeatherTypeId, sample.TypeId, StringComparison.Ordinal)
			&& string.Equals(snapshot.WeatherIntensityId, sample.IntensityId, StringComparison.Ordinal)
			&& snapshot.WeatherExposed == exposed,
			caseId,
			$"Expected snapshot weather type={sample.TypeId} intensity={sample.IntensityId} exposed={exposed}; actual type={snapshot.WeatherTypeId ?? "<null>"} intensity={snapshot.WeatherIntensityId ?? "<null>"} exposed={snapshot.WeatherExposed}.",
			$"Expected snapshot weather type={sample.TypeId} intensity={sample.IntensityId} exposed={exposed}; actual type={snapshot.WeatherTypeId ?? "<null>"} intensity={snapshot.WeatherIntensityId ?? "<null>"} exposed={snapshot.WeatherExposed}.");
	}

	private void RunHealthSmokeChecks(AutoTestScenarioContext context, string prefix, Actor player)
	{
		var baselineLines = new List<string>();
		ActorDerivedStateUpdater.SyncActor(context.State, player);
		ActorStatusTextBuilder.BuildLines(baselineLines, StatusTab.Health, context.State, player);

		var originalPain = player.PainValue;
		var originalBloodLoss = player.BloodLossValue;
		var originalWetness = player.WetnessValue;
		try
		{
			player.PainValue = Math.Max(player.PainValue, 72f);
			player.BloodLossValue = Math.Max(player.BloodLossValue, 16f);
			player.WetnessValue = Math.Max(player.WetnessValue, 74f);
			ActorDerivedStateUpdater.SyncActor(context.State, player);

			var alerts = HealthAlertsModule.DescribeAlerts(player);
			context.Check(
				alerts.Count > 0,
				$"{prefix}.alerts_present",
				$"Expected health alerts count > 0 after staged damage; actual alert_count={alerts.Count}.",
				$"Expected health alerts count > 0 after staged damage; actual alert_count={alerts.Count}.");

			var updatedLines = new List<string>();
			ActorStatusTextBuilder.BuildLines(updatedLines, StatusTab.Health, context.State, player);
			context.Check(
				updatedLines.Count > 0,
				$"{prefix}.status_lines_present",
				$"Expected health status lines count > 0; actual line_count={updatedLines.Count}.",
				$"Expected health status lines count > 0; actual line_count={updatedLines.Count}.");
			context.Check(
				!baselineLines.SequenceEqual(updatedLines),
				$"{prefix}.status_changed",
				$"Expected health status lines to change after staged damage; actual baseline_count={baselineLines.Count} updated_count={updatedLines.Count}.",
				$"Expected health status lines to change after staged damage; actual baseline_count={baselineLines.Count} updated_count={updatedLines.Count}.");
		}
		finally
		{
			player.PainValue = originalPain;
			player.BloodLossValue = originalBloodLoss;
			player.WetnessValue = originalWetness;
			ActorDerivedStateUpdater.SyncActor(context.State, player);
		}
	}

	private void ProbeResourceLoad(AutoTestScenarioContext context, string caseId, string path)
	{
		context.Check(ResourceLoader.Exists(path),
			$"{caseId}.exists",
			$"Resource exists: {path}",
			$"Resource does not exist: {path}");

		try
		{
			var resource = ResourceLoader.Load(path, string.Empty, ResourceLoader.CacheMode.Ignore);
			context.Check(resource != null,
				$"{caseId}.load",
				$"Resource loaded successfully: {path}",
				$"Resource failed to load: {path}");
		}
		catch (Exception ex)
		{
			context.Fail($"{caseId}.exception", $"Resource load threw an exception for '{path}'.", ex);
		}
	}

	private void TryFlushMap(AutoTestScenarioContext context, string caseId)
	{
		try
		{
			context.Host.FlushMap();
			context.Pass(caseId, "Map flush completed without exception.");
		}
		catch (Exception ex)
		{
			context.Fail(caseId, "Map flush threw an exception.", ex);
		}
	}

	private void RecordViewportAvailability(
		AutoTestScenarioContext context,
		string caseId,
		string subject,
		int width,
		int height,
		bool headlessMode)
	{
		var result = EvaluateViewportAvailability(subject, width, height, headlessMode);
		if (result.Passed)
			context.Pass(caseId, result.Message);
		else
			context.Fail(caseId, result.Message);
	}

	private static void PrepareVisionArena(GameState state, int radius)
	{
		if (state.World == null)
			return;

		for (var dy = -radius; dy <= radius; dy++)
		for (var dx = -radius; dx <= radius; dx++)
		{
			var x = state.PlayerX + dx;
			var y = state.PlayerY + dy;
			state.World.SetTerrain(x, y, state.PlayerZ, Terrains.Floor);
			state.World.RemoveEntitiesByType(x, y, state.PlayerZ, CellEntityType.Fixture);
			state.World.RemoveEntitiesByType(x, y, state.PlayerZ, CellEntityType.Container);
			state.World.RemoveEntitiesByType(x, y, state.PlayerZ, CellEntityType.Item);
		}
	}

	private static void SpawnBenchmarkActors(GameState state, string prefix, int count)
	{
		var player = ActorModule.GetPlayer(state);
		if (player == null)
			return;

		var spawned = 0;
		for (var ring = 1; spawned < count; ring++)
		{
			for (var dy = -ring; dy <= ring && spawned < count; dy++)
			for (var dx = -ring; dx <= ring && spawned < count; dx++)
			{
				if (Math.Abs(dx) != ring && Math.Abs(dy) != ring)
					continue;

				var x = player.X + dx;
				var y = player.Y + dy;
				if (x == player.X && y == player.Y)
					continue;
				if (ActorModule.GetAllAt(state, x, y, player.Z).Count > 0)
					continue;

				var actor = ActorTemplates.Spawn("goblin", $"{prefix}{spawned}");
				actor.X = x;
				actor.Y = y;
				actor.Z = player.Z;
				if (Math.Abs(dx) >= Math.Abs(dy))
				{
					actor.FacingX = dx > 0 ? -1 : 1;
					actor.FacingY = 0;
				}
				else
				{
					actor.FacingX = 0;
					actor.FacingY = dy > 0 ? -1 : 1;
				}

				ActorModule.Add(state, actor);
				spawned++;
			}
		}
	}

	private static void RemoveActorsByPrefix(GameState state, string prefix)
	{
		var ids = state.Actors.Keys
			.Where(id => id.StartsWith(prefix, StringComparison.Ordinal))
			.ToList();
		foreach (var id in ids)
			ActorModule.Remove(state, id);
	}

	private static List<Limb> GetCapacityLimbs(Actor actor, string capacityId) =>
		actor.Limbs
			.Where(limb => limb.Capacities.TryGetValue(capacityId, out var weight) && weight > 0f)
			.ToList();

	private static Dictionary<Limb, int> SnapshotDurability(IEnumerable<Limb> limbs) =>
		limbs.ToDictionary(static limb => limb, static limb => limb.Durability);

	private static void RestoreDurability(Dictionary<Limb, int> snapshot)
	{
		foreach (var (limb, durability) in snapshot)
			limb.Durability = durability;
	}

	private static double GetTotalDurability(Actor actor) =>
		actor.Limbs.Sum(static limb => limb.Durability);

	private static void MovePlayerAdjacentToActor(GameState state, Actor player, Actor target)
	{
		var candidates = new (int X, int Y)[]
		{
			(target.X - 1, target.Y),
			(target.X + 1, target.Y),
			(target.X, target.Y - 1),
			(target.X, target.Y + 1),
		};

		foreach (var candidate in candidates)
		{
			if (ActorModule.GetAllAt(state, candidate.X, candidate.Y, target.Z).Count == 0)
			{
				ActorModule.MoveActor(state, player.Id, candidate.X, candidate.Y, target.Z);
				player.FacingX = Math.Sign(target.X - candidate.X);
				player.FacingY = Math.Sign(target.Y - candidate.Y);
				return;
			}
		}

		ActorModule.MoveActor(state, player.Id, target.X - 1, target.Y, target.Z);
		player.FacingX = 1;
		player.FacingY = 0;
	}

	private static GroundItemLocation? FindGroundItem(GameState state, Predicate<Item> predicate, int radius)
	{
		if (state.World == null)
			return null;

		for (var dy = -radius; dy <= radius; dy++)
		for (var dx = -radius; dx <= radius; dx++)
		{
			var x = state.PlayerX + dx;
			var y = state.PlayerY + dy;
			var items = MapModule.PeekGroundItems(state, x, y);
			var item = items.FirstOrDefault(found => predicate(found));
			if (item != null)
				return new GroundItemLocation(x, y, state.PlayerZ, item);
		}

		return null;
	}

	private static Dictionary<string, double> CreateAIVisionMetrics()
	{
		return AutoTestMetricBuilder.CreateAIVisionMetrics(AIVisionBatch.LastMetrics);
	}

	private static int CountScheduledAiObservers(
		GameState state,
		int viewCenterX,
		int viewCenterY,
		int viewRange,
		int turnNumber)
	{
		var simplifiedUpdateInterval = Math.Max(1, GameConfig.AIVision.SimplifiedUpdateIntervalTurns);
		var count = 0;

		foreach (var actor in state.Actors.Values)
		{
			if (actor.BrainId == null || actor.Id == state.PlayerId || CombatModule.IsDead(actor))
				continue;

			var detail = AIDispatcher.Classify(state, actor, viewCenterX, viewCenterY, viewRange);
			if (detail == SimDetail.Summary && actor.AwarenessState != AwarenessState.Idle)
				detail = SimDetail.Simplified;
			if (detail == SimDetail.Summary)
				continue;
			if (detail == SimDetail.Simplified
				&& actor.AwarenessState == AwarenessState.Idle
				&& turnNumber % simplifiedUpdateInterval != 0)
				continue;

			count++;
		}

		return count;
	}

	private static bool TryReadSaveVersion(string path, out int version)
	{
		version = 0;
		if (!File.Exists(path))
			return false;

		try
		{
			using var doc = JsonDocument.Parse(File.ReadAllText(path));
			return doc.RootElement.TryGetProperty("version", out var versionElement)
				&& versionElement.ValueKind == JsonValueKind.Number
				&& versionElement.TryGetInt32(out version);
		}
		catch
		{
			version = 0;
			return false;
		}
	}

	private static bool TryReadSaveWeatherPhase(string path, out float phase)
	{
		phase = 0f;
		if (!File.Exists(path))
			return false;

		try
		{
			using var doc = JsonDocument.Parse(File.ReadAllText(path));
			return doc.RootElement.TryGetProperty("payload", out var payloadElement)
				&& payloadElement.ValueKind == JsonValueKind.Object
				&& payloadElement.TryGetProperty("weather", out var weatherElement)
				&& weatherElement.ValueKind == JsonValueKind.Object
				&& weatherElement.TryGetProperty("frontPhase", out var phaseElement)
				&& phaseElement.ValueKind == JsonValueKind.Number
				&& phaseElement.TryGetSingle(out phase);
		}
		catch
		{
			phase = 0f;
			return false;
		}
	}

	private static bool TryRewriteSaveVersion(string sourcePath, string destinationPath, int version)
	{
		if (!File.Exists(sourcePath))
			return false;

		var original = File.ReadAllText(sourcePath);
		var rewritten = original.Replace(
			$"\"version\": {SaveModule.CurrentVersion}",
			$"\"version\": {version}",
			StringComparison.Ordinal);
		if (string.Equals(original, rewritten, StringComparison.Ordinal))
			return false;

		File.WriteAllText(destinationPath, rewritten);
		return true;
	}

	private static bool NearlyEqual(float left, float right, float epsilon = 0.0001f) =>
		Math.Abs(left - right) <= epsilon;

	private static AutoTestRuntimeSnapshot CaptureSnapshotSafely(IAutoTestHost host)
	{
		try
		{
			return host.CaptureSnapshot();
		}
		catch
		{
			return new AutoTestRuntimeSnapshot();
		}
	}

	private static AutoTestRunReport CreateRunReport(DebugConfig config, AutoTestRuntimeSnapshot snapshot)
	{
		return new AutoTestRunReport
		{
			RunId = Guid.NewGuid().ToString("N"),
			StartedAtUtc = DateTimeOffset.UtcNow,
			ContinueOnFailure = config.AutoTestContinueOnFailure && !config.AutoTestStopOnFail,
			StepDelaySeconds = config.AutoTestStepDelay,
			StructuredLogEnabled = config.AutoTestWriteStructuredLog,
			ResourceSmokeEnabled = config.AutoTestEnableResourceSmoke,
			DisplayServerName = snapshot.DisplayServerName,
			HeadlessMode = snapshot.HeadlessMode,
			InvokedFromCli = snapshot.InvokedFromCli,
			ScenarioFilter = [.. config.AutoTestScenarios],
		};
	}

	private static string FormatPosition(int x, int y, int z) => $"({x},{y},{z})";

	private static string NormalizeFilePath(string path) =>
		Path.GetFullPath(path)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
				File.Delete(path);
		}
		catch
		{
			// Ignore cleanup failures for auto test temp files.
		}
	}

	private static string SanitizeId(string value)
	{
		var chars = value
			.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_')
			.ToArray();
		return new string(chars).Trim('_');
	}

	private sealed record GroundItemLocation(int X, int Y, int Z, Item Item);
}
