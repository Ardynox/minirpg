using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MiniRPG.Core.AI;
using MiniRPG.Core.World;
using MiniRPG.Module.Render;

namespace MiniRPG.Module;

/// <summary>
/// 自动测试编排器：在真实游戏世界中按序执行调用链与视觉回归测试。
/// 结果输出到 Godot 控制台 + 日志文件 (user://test_results.log)。
/// </summary>
public class AutoTestModule
{
	private float _stepDelay;
	private bool _stopOnFail;
	private int _pass, _fail;
	private bool _aborted;
	private readonly List<string> _logLines = [];

	public async void RunAll(
		Node main, GameState state, GameSessionModule session,
		FogOfWarTracker fogTracker, TileMapRenderModule mapRender,
		LogModule log, Action<string> runCommand, Action flushMap,
		float? delay = null, bool? stopOnFail = null)
	{
		var config = GameConfig.Debug;
		_stepDelay = delay ?? config.AutoTestStepDelay;
		_stopOnFail = stopOnFail ?? config.AutoTestStopOnFail;
		_pass = 0;
		_fail = 0;
		_aborted = false;
		_logLines.Clear();

		var tree = main.GetTree();

		Log("========================================");
		Log("  AUTO TEST START");
		Log($"  delay={_stepDelay}s  stopOnFail={_stopOnFail}");
		Log("========================================");

		await TestChain1_Session(tree, state, session, flushMap);
		if (_aborted) { Finish(); return; }

		await TestChain2_Render(tree, state, fogTracker, flushMap);
		if (_aborted) { Finish(); return; }

		await TestChain3_Command(tree, state, runCommand, flushMap);
		if (_aborted) { Finish(); return; }

		await TestChain4_EventDispatch(tree, state, log);
		if (_aborted) { Finish(); return; }

		await TestChain5_AI(tree, state, runCommand, flushMap);
		if (_aborted) { Finish(); return; }

		await TestChain6_MainLoop(tree, state, session, fogTracker, flushMap);
		if (_aborted) { Finish(); return; }

		await TestChain7_AIVisionBenchmark(tree, state);

		Finish();
	}

	// ── Chain 1: 会话生命周期 ─────────────────────────────

	private async Task TestChain1_Session(
		SceneTree tree, GameState state, GameSessionModule session, Action flushMap)
	{
		Log("── Chain 1: 会话生命周期 ──");

		Assert(state.World != null, "NewGame: World != null");
		Assert(ActorModule.GetPlayer(state) != null, "NewGame: Player exists");
		Assert(session.GameStarted, "NewGame: GameStarted == true");
		Assert(state.Turn == 0, "NewGame: Turn == 0");
		await Step(tree);

		var savePath = System.IO.Path.Combine(
			OS.GetUserDataDir(), "save", "_autotest_temp.json");
		session.SaveGame(savePath);
		Assert(System.IO.File.Exists(savePath), "SaveGame: file created");
		await Step(tree);

		var seedBefore = state.WorldSeed;
		var playerXBefore = state.PlayerX;
		var loaded = session.LoadGame(savePath);
		Assert(loaded, "LoadGame: returns true");
		Assert(state.WorldSeed == seedBefore, "LoadGame: WorldSeed preserved");
		Assert(state.PlayerX == playerXBefore, "LoadGame: PlayerX preserved");
		flushMap();
		await Step(tree);

		var zBefore = state.PlayerZ;
		session.ChangeFloor(goDown: true);
		Assert(state.PlayerZ == zBefore + 1, "ChangeFloor: Z incremented");
		flushMap();
		await Step(tree);

		session.ChangeFloor(goDown: false);
		Assert(state.PlayerZ == zBefore, "ChangeFloor: Z restored");
		flushMap();
		await Step(tree);

		try { System.IO.File.Delete(savePath); } catch { }
	}

	// ── Chain 2: 渲染管线 ─────────────────────────────────

	private async Task TestChain2_Render(
		SceneTree tree, GameState state, FogOfWarTracker fogTracker,
		Action flushMap)
	{
		Log("── Chain 2: 渲染管线 ──");

		var player = ActorModule.GetPlayer(state)!;
		PrepareVisionArena(state, radius: GameConfig.Debug.AutoTestRenderArenaRadius);
		player.FacingX = 1;
		player.FacingY = 0;

		var px = player.X;
		var py = player.Y;
		var pz = player.Z;

		state.World!.SetTerrain(px + 1, py, pz, Terrains.Floor);
		state.World.SetTerrain(px + 2, py, pz, Terrains.Floor);
		state.World.SetTerrain(px - 1, py, pz, Terrains.Floor);

		fogTracker.Clear();
		fogTracker.Update(state);
		Assert(fogTracker.GetVisionBand(px + 1, py, pz) == PlayerVisionBand.Focused,
			"FOV: front tile focused");
		Assert(fogTracker.GetVisionBand(px - 1, py, pz) == PlayerVisionBand.Peripheral,
			"FOV: rear tile peripheral");
		Assert(fogTracker.IsVisible(state.PlayerX, state.PlayerY, state.PlayerZ),
			"FOV: player position visible");
		await Step(tree);

		state.World.SetTerrain(px + 1, py, pz, Terrains.WallStone);
		fogTracker.Update(state);
		Assert(state.World.BlocksSight(px + 1, py, pz), "BlocksSight: wall blocks sight");
		Assert(fogTracker.GetVisionBand(px + 2, py, pz) == PlayerVisionBand.Memory,
			"FOV: previously seen tile falls back to memory when blocked");
		await Step(tree);

		state.World.SetTerrain(px + 1, py, pz, Terrains.Floor);
		fogTracker.Update(state);

		try
		{
			flushMap();
			Assert(true, "TileMapRenderModule.Flush: no exception");
		}
		catch (Exception ex)
		{
			Assert(false, $"TileMapRenderModule.Flush: threw {ex.GetType().Name}");
		}
		await Step(tree);
	}

	// ── Chain 3: 命令处理 ─────────────────────────────────

	private async Task TestChain3_Command(
		SceneTree tree, GameState state, Action<string> runCommand, Action flushMap)
	{
		Log("── Chain 3: 命令处理 ──");

		var player = ActorModule.GetPlayer(state)!;
		var xBefore = player.X;
		var yBefore = player.Y;
		var turnBefore = state.Turn;

		runCommand("d");
		flushMap();
		await Step(tree);

		var moved = player.X != xBefore || player.Y != yBefore;
		Assert(moved || state.Turn > turnBefore, "DoMove: player moved or turn advanced (wall)");
		Assert(state.Turn > turnBefore, "DoMove: turn advanced after Tick");
		await Step(tree);

		var turnBefore2 = state.Turn;
		var events = ActionModule.TryMove(state, player, 0, 1);
		Assert(events != null, "ActionModule.TryMove: returns event list");
		var tickEvents = TurnModule.Tick(state);
		Assert(state.Turn > turnBefore2, "TurnModule.Tick: turn incremented");
		flushMap();
		await Step(tree);

		var goldBefore = player.Gold;
		runCommand("/gold 500");
		Assert(player.Gold == goldBefore + 500, "Debug /gold: gold increased by 500");
		await Step(tree);

		foreach (var limb in player.Limbs)
			limb.Durability = 1;
		runCommand("/heal");
		var allHealed = true;
		foreach (var limb in player.Limbs)
			if (limb.Durability < limb.MaxDurability) { allHealed = false; break; }
		Assert(allHealed, "Debug /heal: all limbs fully healed");
		await Step(tree);
	}

	// ── Chain 4: 事件分发 ─────────────────────────────────

	private async Task TestChain4_EventDispatch(
		SceneTree tree, GameState state, LogModule log)
	{
		Log("── Chain 4: 事件分发 ──");

		var hitWall = new GameEvent("hit_wall");
		var dispatched = log.DispatchEvent(hitWall, state);
		Assert(dispatched, "DispatchEvent(hit_wall): returns true");
		await Step(tree);

		var attack = new GameEvent("combat_attack")
		{
			TargetId = "test_target",
			TargetActorName = "测试目标",
			LimbName = "头",
			Damage = 10,
			ActionName = "拳击",
		};
		var attackDispatched = log.DispatchEvent(attack, state);
		Assert(attackDispatched, "DispatchEvent(combat_attack): returns true");
		await Step(tree);

		var pickup = new GameEvent("item_picked_up") { ItemName = "测试物品" };
		var pickupDispatched = log.DispatchEvent(pickup, state);
		Assert(pickupDispatched, "DispatchEvent(item_picked_up): returns true");
		await Step(tree);

		var moved = new GameEvent("actor_moved");
		var movedDispatched = log.DispatchEvent(moved, state);
		Assert(!movedDispatched, "DispatchEvent(actor_moved): returns false (silent)");
		await Step(tree);
	}

	// ── Chain 5: AI 回合 ──────────────────────────────────

	private async Task TestChain5_AI(
		SceneTree tree, GameState state, Action<string> runCommand, Action flushMap)
	{
		Log("── Chain 5: AI 回合 ──");

		var player = ActorModule.GetPlayer(state)!;
		PrepareVisionArena(state, radius: GameConfig.Debug.AutoTestAiArenaRadius);
		RemoveActorsByPrefix(state, "test_ai_");

		var observer = SpawnTestActor(state, "goblin", "test_ai_observer", player.X - 2, player.Y, player.Z);
		observer.FacingX = 1;
		observer.FacingY = 0;

		state.World!.SetTerrain(player.X - 1, player.Y, player.Z, Terrains.WallStone);
		var blocked = PerceptionBuilder.Build(state, observer, SimDetail.Full);
		Assert(blocked.NearbyActors.All(a => a.Id != player.Id),
			"AI vision: wall blocks player detection");

		state.World.SetTerrain(player.X - 1, player.Y, player.Z, Terrains.Floor);
		var open = PerceptionBuilder.Build(state, observer, SimDetail.Full);
		Assert(open.NearbyActors.Any(a => a.Id == player.Id),
			"AI vision: open line detects player");

		var simplified = PerceptionBuilder.Build(state, observer, SimDetail.Simplified);
		Assert(simplified.NearbyActors.Any(a => a.Id == player.Id),
			"AI vision: simplified still detects nearby player");

		var summary = PerceptionBuilder.Build(state, observer, SimDetail.Summary);
		Assert(summary.NearbyActors.Count == 0,
			"AI vision: summary skips live actor scan");
		await Step(tree);

		var countBefore = state.Actors.Count;
		runCommand("/spawn goblin");
		flushMap();
		await Step(tree);
		Assert(state.Actors.Count > countBefore, "SpawnEnemy: actor count increased");

		var turnBefore = state.Turn;
		try
		{
			var events = TurnModule.Tick(state);
			Assert(state.Turn > turnBefore, "TurnModule.Tick with AI: turn advanced");
			Assert(events != null, "TurnModule.Tick: returns event list");
			flushMap();
		}
		catch (Exception ex)
		{
			Assert(false, $"TurnModule.Tick with AI: threw {ex.GetType().Name}: {ex.Message}");
		}
		await Step(tree);

		try
		{
			player = ActorModule.GetPlayer(state);
			if (player != null)
			{
				var aiEvents = AIDispatcher.TickAll(state, player.X, player.Y, 20);
				Assert(aiEvents != null, "AIDispatcher.TickAll: returns event list");
				var metrics = AIVisionBatch.LastMetrics;
				Log($"  [INFO] ai_vision observers={metrics.ObserverCount} candidates={metrics.CandidateCount} " +
					$"shortlist={metrics.ShortlistCount} los={metrics.LosChecks} visible={metrics.VisibleActorCount} " +
					$"elapsed={metrics.ElapsedMs:F3}ms");
				Assert(metrics.ObserverCount > 0, "AIVisionBatch: metrics recorded");
			}
			else
			{
				Assert(false, "AIDispatcher.TickAll: player is null");
			}
		}
		catch (Exception ex)
		{
			Assert(false, $"AIDispatcher.TickAll: threw {ex.GetType().Name}: {ex.Message}");
		}
		flushMap();
		RemoveActorsByPrefix(state, "test_ai_");
		await Step(tree);
	}

	// ── Chain 6: 主循环验证 ───────────────────────────────

	private async Task TestChain6_MainLoop(
		SceneTree tree, GameState state, GameSessionModule session,
		FogOfWarTracker fogTracker,
		Action flushMap)
	{
		Log("── Chain 6: 主循环验证 ──");

		Assert(state != null!, "Init: GameState != null");
		Assert(session != null!, "Init: GameSessionModule != null");
		await Step(tree);

		try
		{
			flushMap();
			Assert(true, "FlushMap: no exception");
		}
		catch (Exception ex)
		{
			Assert(false, $"FlushMap: threw {ex.GetType().Name}");
		}
		await Step(tree);

		try
		{
			var lookText = LookModule.BuildLookText(state!, fogTracker);
			Assert(!string.IsNullOrEmpty(lookText), "LookModule.BuildLookText: non-empty");
		}
		catch (Exception ex)
		{
			Assert(false, $"LookModule: threw {ex.GetType().Name}");
		}
		await Step(tree);
	}

	// ── Chain 7: AI 视觉基准 ──────────────────────────────

	private async Task TestChain7_AIVisionBenchmark(SceneTree tree, GameState state)
	{
		Log("── Chain 7: AI 视觉基准 ──");
		var config = GameConfig.Debug;
		PrepareVisionArena(state, radius: config.AutoTestBenchmarkArenaRadius);

		foreach (var count in config.AutoTestBenchmarkCounts)
		{
			var prefix = $"bench_ai_{count}_";
			RemoveActorsByPrefix(state, prefix);
			SpawnBenchmarkActors(state, prefix, count);

			var sw = Stopwatch.StartNew();
			_ = TurnModule.Tick(state);
			sw.Stop();

			var metrics = AIVisionBatch.LastMetrics;
			Log($"  [INFO] bench count={count} tick={sw.Elapsed.TotalMilliseconds:F3}ms " +
				$"vision={metrics.ElapsedMs:F3}ms observers={metrics.ObserverCount} " +
				$"candidates={metrics.CandidateCount} shortlist={metrics.ShortlistCount} " +
				$"los={metrics.LosChecks} visible={metrics.VisibleActorCount}");
			if (metrics.ElapsedMs > config.AutoTestAiVisionWarnMs || sw.Elapsed.TotalMilliseconds > config.AutoTestTickWarnMs)
			{
				Log($"  [WARN] bench thresholds exceeded for count={count} " +
					$"(tick>{config.AutoTestTickWarnMs:F1}ms or vision>{config.AutoTestAiVisionWarnMs:F1}ms)");
			}
			Assert(metrics.ObserverCount >= count, $"AI bench {count}: metrics captured");

			RemoveActorsByPrefix(state, prefix);
			await Step(tree);
		}
	}

	// ── 基础设施 ──────────────────────────────────────────

	private static void PrepareVisionArena(GameState state, int radius)
	{
		if (state.World == null) return;

		for (int dy = -radius; dy <= radius; dy++)
		for (int dx = -radius; dx <= radius; dx++)
		{
			int x = state.PlayerX + dx;
			int y = state.PlayerY + dy;
			state.World.SetTerrain(x, y, state.PlayerZ, Terrains.Floor);
			state.World.RemoveEntitiesByType(x, y, state.PlayerZ, CellEntityType.Fixture);
			state.World.RemoveEntitiesByType(x, y, state.PlayerZ, CellEntityType.Container);
			state.World.RemoveEntitiesByType(x, y, state.PlayerZ, CellEntityType.Item);
		}
	}

	private static Actor SpawnTestActor(GameState state, string templateId, string instanceId, int x, int y, int z)
	{
		var actor = ActorTemplates.Spawn(templateId, instanceId);
		actor.X = x;
		actor.Y = y;
		actor.Z = z;
		ActorModule.Add(state, actor);
		return actor;
	}

	private static void SpawnBenchmarkActors(GameState state, string prefix, int count)
	{
		var player = ActorModule.GetPlayer(state);
		if (player == null) return;

		int spawned = 0;
		for (int ring = 1; spawned < count; ring++)
		{
			for (int dy = -ring; dy <= ring && spawned < count; dy++)
			for (int dx = -ring; dx <= ring && spawned < count; dx++)
			{
				if (Math.Abs(dx) != ring && Math.Abs(dy) != ring) continue;
				int x = player.X + dx;
				int y = player.Y + dy;
				if (x == player.X && y == player.Y) continue;
				if (ActorModule.GetAllAt(state, x, y, player.Z).Count > 0) continue;

				var actor = SpawnTestActor(state, "goblin", $"{prefix}{spawned}", x, y, player.Z);
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

				spawned++;
			}
		}
	}

	private static void RemoveActorsByPrefix(GameState state, string prefix)
	{
		var ids = state.Actors.Keys.Where(id => id.StartsWith(prefix, StringComparison.Ordinal)).ToList();
		foreach (var id in ids)
			ActorModule.Remove(state, id);
	}

	private void Assert(bool condition, string name)
	{
		if (condition)
		{
			_pass++;
			Log($"  [PASS] {name}");
		}
		else
		{
			_fail++;
			Log($"  [FAIL] {name}");
			if (_stopOnFail) _aborted = true;
		}
	}

	private void Log(string msg)
	{
		_logLines.Add(msg);
		GD.Print($"[AutoTest] {msg}");
	}

	private async Task Step(SceneTree tree)
	{
		if (_stepDelay > 0)
			await tree.ToSignal(tree.CreateTimer(_stepDelay), SceneTreeTimer.SignalName.Timeout);
		else
			await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
	}

	private void Finish()
	{
		Log("========================================");
		Log($"  RESULT: {_pass} PASS / {_fail} FAIL");
		if (_aborted) Log("  (ABORTED: stopped on first failure)");
		Log("========================================");
		WriteLogFile();
	}

	private void WriteLogFile()
	{
		try
		{
			var path = OS.GetUserDataDir() + "/test_results.log";
			var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
			var header = $"MiniRPG Auto Test - {timestamp}\n\n";
			System.IO.File.WriteAllText(path, header + string.Join("\n", _logLines) + "\n");
			GD.Print($"[AutoTest] Log written to: {path}");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AutoTest] Failed to write log: {ex.Message}");
		}
	}
}
