using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MiniRPG.Core;
using MiniRPG.Core.AI;
using MiniRPG.Core.World;

namespace MiniRPG.Module;

/// <summary>
/// 自动测试编排器：在真实游戏世界中按序执行 6 条调用链的可视化测试。
/// 结果输出到 Godot 控制台 + 日志文件 (user://test_results.log)。
/// </summary>
public class AutoTestModule
{
	private float _stepDelay = 0.05f;
	private bool _stopOnFail;
	private int _pass, _fail;
	private bool _aborted;
	private readonly List<string> _logLines = [];

	/// <summary>
	/// 启动全部测试。
	/// </summary>
	/// <param name="main">Main 节点引用，用于访问 SceneTree 和驱动命令。</param>
	/// <param name="state">当前游戏状态。</param>
	/// <param name="session">会话模块。</param>
	/// <param name="fogTracker">迷雾追踪器。</param>
	/// <param name="mapRender">地图渲染模块。</param>
	/// <param name="log">日志模块。</param>
	/// <param name="runCommand">执行命令的委托（转发到 Main.OnCommand）。</param>
	/// <param name="flushMap">刷新地图的委托。</param>
	/// <param name="delay">步间延迟秒数。</param>
	/// <param name="stopOnFail">遇到失败是否停止。</param>
	public async void RunAll(
		Node main, GameState state, GameSessionModule session,
		FogOfWarTracker fogTracker, MapRenderModule mapRender,
		LogModule log, Action<string> runCommand, Action flushMap,
		float delay = 0.05f, bool stopOnFail = false)
	{
		_stepDelay = delay;
		_stopOnFail = stopOnFail;
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

		await TestChain2_Render(tree, state, fogTracker, mapRender, flushMap);
		if (_aborted) { Finish(); return; }

		await TestChain3_Command(tree, state, runCommand, flushMap);
		if (_aborted) { Finish(); return; }

		await TestChain4_EventDispatch(tree, state, log);
		if (_aborted) { Finish(); return; }

		await TestChain5_AI(tree, state, runCommand, flushMap);
		if (_aborted) { Finish(); return; }

		await TestChain6_MainLoop(tree, state, session, mapRender, flushMap);

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
		MapRenderModule mapRender, Action flushMap)
	{
		Log("── Chain 2: 渲染管线 ──");

		fogTracker.Update(state);
		Assert(fogTracker.IsVisible(state.PlayerX, state.PlayerY, state.PlayerZ),
			"FOV: player position visible");
		await Step(tree);

		var viewMode = new SingleLayerViewMode();
		var displayMap = viewMode.BuildDisplayMap(state, 21, 11);
		Assert(displayMap != null && displayMap.Count == 11, "ViewMode: displayMap has 11 rows");
		Assert(displayMap![0].Count == 21, "ViewMode: displayMap has 21 cols");
		await Step(tree);

		var render = mapRender.Render;
		var text = render.RenderMap(displayMap);
		Assert(!string.IsNullOrEmpty(text), "RenderMap: output non-empty");
		await Step(tree);

		try
		{
			flushMap();
			Assert(true, "MapRenderModule.Flush: no exception");
		}
		catch (Exception ex)
		{
			Assert(false, $"MapRenderModule.Flush: threw {ex.GetType().Name}");
		}
		await Step(tree);

		var wasMini = mapRender.MinimapVisible;
		mapRender.ToggleMinimap();
		Assert(mapRender.MinimapVisible != wasMini, "ToggleMinimap: state flipped");
		flushMap();
		await Step(tree);
		mapRender.ToggleMinimap();
		flushMap();

		var wasFog = mapRender.FogMapVisible;
		mapRender.ToggleFogMap();
		Assert(mapRender.FogMapVisible != wasFog, "ToggleFogMap: state flipped");
		flushMap();
		await Step(tree);
		mapRender.ToggleFogMap();
		flushMap();

		var modeMsg = mapRender.ToggleRenderMode();
		Assert(modeMsg != null, "ToggleRenderMode: returns message");
		flushMap();
		await Step(tree);
		mapRender.ToggleRenderMode();
		flushMap();
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
			var player = ActorModule.GetPlayer(state);
			if (player != null)
			{
				var aiEvents = AIDispatcher.TickAll(state, player.X, player.Y, 20);
				Assert(aiEvents != null, "AIDispatcher.TickAll: returns event list");
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
		await Step(tree);
	}

	// ── Chain 6: 主循环验证 ───────────────────────────────

	private async Task TestChain6_MainLoop(
		SceneTree tree, GameState state, GameSessionModule session,
		MapRenderModule mapRender, Action flushMap)
	{
		Log("── Chain 6: 主循环验证 ──");

		Assert(state != null!, "Init: GameState != null");
		Assert(session != null!, "Init: GameSessionModule != null");
		Assert(mapRender != null!, "Init: MapRenderModule != null");
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
			var lookText = LookModule.BuildLookText(state!);
			Assert(!string.IsNullOrEmpty(lookText), "LookModule.BuildLookText: non-empty");
		}
		catch (Exception ex)
		{
			Assert(false, $"LookModule: threw {ex.GetType().Name}");
		}
		await Step(tree);
	}

	// ── 基础设施 ──────────────────────────────────────────

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
