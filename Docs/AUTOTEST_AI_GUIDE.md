# AutoTest AI Guide

> This document is for AI/code agents working on MiniRPG. It explains how to use, inspect, and extend the in-game AutoTest inspector without re-discovering the workflow from code each time.

---

## Purpose

- `AutoTest` is the in-game one-click inspection runner.
- It is separate from the existing `xUnit` project.
- Primary usage is:
  1. User launches the game manually.
  2. User clicks the main menu button `自动化测试`.
  3. Game writes `user://test_results.log`.
  4. AI reads the log file, locates failures, and then fixes code or test expectations.

Use this system for end-to-end gameplay diagnostics, resource smoke checks, runtime state assertions, and readable failure context.

## Branch Policy

- Long-lived branch for this work: `test/automation`
- Baseline branch: `dev`
- When making AutoTest-specific changes, prefer landing them on `test/automation` first.

## What AutoTest Covers

Current scenarios:

- `resource_smoke`
- `new_game`
- `qa_smoke_core`
- `qa_interaction_hub`
- `qa_combat_arena`

Current goals:

- Verify startup-critical resources can load.
- Verify new game/session/save/load/floor/FOV chains do not silently regress.
- Verify interaction flows such as inventory, pickup/drop, dialog, and trade.
- Verify combat flow, AI tick, and AI vision benchmark metrics.
- Verify weather/runtime snapshot consistency, health alert/status smoke, and responsive layout observability.

Non-goals for the current generation:

- Screenshot comparison
- Editor/tooling coverage
- Pixel-level UI assertions

## Runtime Entry Points

Primary code entry points:

- [`../App/Main.cs`](../App/Main.cs)
  Main menu button wiring and runtime event dispatch.
- [`../App/Main.AutoTest.cs`](../App/Main.AutoTest.cs)
  `IAutoTestHost` bridge from `Main` into AutoTest.
- [`../Module/AutoTestModule.cs`](../Module/AutoTestModule.cs)
  Scenario orchestration and assertions.
- [`../Module/AutoTestModels.cs`](../Module/AutoTestModels.cs)
  Scenario/result/report/log DTOs.
- [`../Module/AutoTestLogWriter.cs`](../Module/AutoTestLogWriter.cs)
  Human summary + NDJSON writer.
- [`../Core/Config/GameConfig.cs`](../Core/Config/GameConfig.cs)
  `DebugConfig` / AutoTest config DTOs.
- [`../Data/Config/debug.json`](../Data/Config/debug.json)
  Runtime AutoTest config values.

Related systems that commonly explain failures:

- Save/session:
  [`../Module/GameSessionModule.cs`](../Module/GameSessionModule.cs),
  [`../Core/Map/SaveModule.cs`](../Core/Map/SaveModule.cs)
- Interaction/trade/dialog:
  [`../Core/Data/InteractionModule.cs`](../Core/Data/InteractionModule.cs),
  [`../Core/Trade/TradeModule.cs`](../Core/Trade/TradeModule.cs),
  [`../Module/TradeUIModule.cs`](../Module/TradeUIModule.cs)
- Vision/FOV/render:
  [`../Module/Render/FogOfWarTracker.cs`](../Module/Render/FogOfWarTracker.cs),
  [`../Core/World/VisibilityUtil.cs`](../Core/World/VisibilityUtil.cs)
- AI metrics:
  [`../Core/AI/AIDispatcher.cs`](../Core/AI/AIDispatcher.cs),
  [`../Core/AI/AIVisionBatch.cs`](../Core/AI/AIVisionBatch.cs),
  [`../Core/Combat/TurnModule.cs`](../Core/Combat/TurnModule.cs)

## Log Contract

AutoTest always writes a single file and overwrites the previous run:

- `user://test_results.log`

Typical Windows path for this project:

- `C:\Users\12536\AppData\Roaming\Godot\app_userdata\MiniRPG\test_results.log`

File layout:

- Human-readable summary first
- NDJSON block second

NDJSON markers:

```text
=== AUTO_TEST_JSON_BEGIN ===
=== AUTO_TEST_JSON_END ===
```

Important machine-readable fields:

- `run_id`
- `scenario_id`
- `case_id`
- `status`
- `severity`
- `message`
- `turn`
- `player_pos`
- `preset_scenario_id`
- `metrics`
- `exception`
- `startup_state`
- `startup_sync_fallback_used`
- `startup_load_path`
- `weather_type_id`
- `weather_intensity_id`
- `weather_exposed`
- `viewport_width`
- `viewport_height`
- `map_viewport_width`
- `map_viewport_height`

Important rule:

- Human summary may be localized.
- Machine-consumed identifiers stay ASCII/English.

## Standard AI Workflow

When a user says `我执行自动测试了` or asks to inspect the run:

1. Open the latest `test_results.log`.
2. Confirm the file timestamp and `run_id`.
3. Read the summary section first:
   `result`, `scenario_summary`, `first_failures`, `warnings`.
4. If needed, inspect the NDJSON record for the failing `case_id`.
5. Use `snapshot`, `metrics`, `exception`, and `last_log_line` to narrow the failing subsystem.
6. Decide whether the issue is:
   a gameplay bug,
   a data/config bug,
   or an outdated/too-strict AutoTest expectation.

Do not ask the user to manually copy logs unless file access is blocked. The intended workflow is AI reads the log directly from disk.

## How To Extend AutoTest

When adding a new scenario or assertion:

- Keep scenario state isolated. Each scenario should start from a fresh session.
- Prefer stable `case_id` values. Do not localize `case_id`.
- Attach minimal debugging context to every failure.
- Prefer state assertions and event assertions over fragile UI/pixel assertions.
- If checking performance, separate:
  functional pass/fail,
  threshold warning,
  raw metrics payload.
- Prefer `expected=... actual=...` wording in messages when the failure could otherwise be ambiguous from the summary alone.

When changing output format:

- Keep the summary readable for humans.
- Keep the NDJSON block stable for machines.
- Avoid introducing non-ASCII machine keys.

## Validation Workflow

After changing AutoTest or systems it exercises:

1. Run:
   `dotnet build MiniRPG.csproj --no-restore`
2. Run:
   `dotnet test Tests\MiniRPG.Tests\MiniRPG.Tests.csproj --no-restore`
3. Ask the user to click `自动化测试` in-game, or do it manually if the session is available.
4. Re-read `user://test_results.log`.
5. Compare the new `run_id`, failure list, and warnings with the previous run.

## Common Failure Triage Heuristics

- `new_game.save.*`
  Look at save-path handling, save-slot enumeration, and incompatible header labeling.
- `*.fov.*`
  Look at facing, rear-vision config, and `FogOfWarTracker`.
- `*.weather.*`
  Compare snapshot weather fields with `WeatherRules.GetLocalWeather(...)` and whether the current tile is weather-exposed.
- `*.health.*`
  Check `HealthAlertsModule`, `ActorStatusTextBuilder`, and whether the staged state change actually reached the player actor.
- `qa_interaction_hub.trade.*`
  Check target interaction requirements, shop goods availability, and trade UI opening.
- `qa_interaction_hub.*.identification_log`
  The preset currently assumes unidentified actors/items, so `Unknown ...` can be the expected output rather than a regression.
- `qa_interaction_hub.dialog.trade_exclusive`
  This is a state-consistency check. If it fails, inspect whether dialog opening leaves trade UI open or focus on the wrong panel.
- `qa_combat_arena.ai.*`
  Check whether metrics come from the same scheduled AI tick that the scenario is asserting.
- `qa_combat_arena.benchmark.*`
  Distinguish spawned actor count from the number of observers actually scheduled by AI detail rules. `*.preconditions` failing means the benchmark result is not trustworthy and threshold cases should be ignored.

## Relationship To xUnit

- `xUnit` remains the logic/regression layer.
- `AutoTest` is the in-game runtime inspection layer.
- Do not merge the two systems conceptually.
- If a bug is pure logic and stable, add or update `xUnit`.
- If a bug requires real session/UI/runtime integration context, prefer `AutoTest`.
