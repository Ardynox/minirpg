using Godot;
using MiniRPG.Core;
using MiniRPG.Core.World;

namespace MiniRPG.Module;

/// <summary>
/// 游戏会话生命周期管理：新建游戏、加载存档、保存存档、世界初始化。
/// 不持有任何 UI 节点引用——纯逻辑层，由调用方（Main/MenuModule）驱动。
/// </summary>
public class GameSessionModule
{
	private static readonly string SaveDir =
		System.IO.Path.Combine(OS.GetUserDataDir(), "save");

	public static string QuickSavePath =>
		System.IO.Path.Combine(SaveDir, "quicksave.json");
	public static string ManualSavePath =>
		System.IO.Path.Combine(SaveDir, "save.json");

	private readonly GameState _state;
	private readonly FogOfWarTracker _fogTracker;

	public bool GameStarted { get; private set; }

	public GameSessionModule(GameState state, FogOfWarTracker fogTracker)
	{
		_state = state;
		_fogTracker = fogTracker;
	}

	/// <summary>
	/// 创建新游戏：重置状态、生成世界、清除迷雾缓存。
	/// 返回后调用方负责刷新 UI 面板可见性和地图渲染。
	/// </summary>
	public void NewGame()
	{
		_state.Reset();
		_state.WorldSeed = System.Environment.TickCount;
		_fogTracker.Clear();
		InitializeWorld();
		GameStarted = true;
	}

	/// <summary>
	/// 从指定路径加载存档。成功返回 true，失败返回 false。
	/// 成功后世界已重建、玩家已恢复，调用方负责刷新 UI。
	/// </summary>
	public bool LoadGame(string path)
	{
		if (!SaveModule.LoadGame(_state, path)) return false;
		MapGenModule.InitializeWorld(_state);
		EnsurePlayerActor();
		SyncViewMode();
		GameStarted = true;
		return true;
	}

	/// <summary>尝试按优先级加载存档（用于「继续」按钮）。</summary>
	public bool TryContinue()
	{
		return LoadGame(QuickSavePath) || LoadGame(ManualSavePath);
	}

	/// <summary>尝试加载手动存档，失败则尝试快速存档。</summary>
	public bool TryLoadGame()
	{
		return LoadGame(ManualSavePath) || LoadGame(QuickSavePath);
	}

	public void SaveGame(string path)
	{
		SaveModule.SaveGame(_state, path);
	}

	public bool HasAnySave()
	{
		return System.IO.File.Exists(QuickSavePath)
			|| System.IO.File.Exists(ManualSavePath);
	}

	/// <summary>
	/// 扫描玩家附近是否有楼梯，有则切换楼层。
	/// 返回 true 表示成功切换，logMessage 为日志文本。
	/// </summary>
	public bool TryUseStairs(out string? logMessage)
	{
		logMessage = null;
		var px = _state.PlayerX;
		var py = _state.PlayerY;
		var dirs = new (int Dx, int Dy)[] { (0, 0), (0, -1), (0, 1), (-1, 0), (1, 0) };

		foreach (var (dx, dy) in dirs)
		{
			if (MapModule.HasFixture(_state, px + dx, py + dy, Entities.StairDown))
			{
				ChangeFloor(goDown: true);
				logMessage = $"你进入了第 {_state.PlayerZ} 层 ⬇️";
				return true;
			}
			if (MapModule.HasFixture(_state, px + dx, py + dy, Entities.StairUp))
			{
				ChangeFloor(goDown: false);
				logMessage = $"你回到了第 {_state.PlayerZ} 层 ⬆️";
				return true;
			}
		}
		return false;
	}

	/// <summary>执行楼层切换：修改 Z 层、加载区块、传送玩家到对应楼梯。</summary>
	public void ChangeFloor(bool goDown)
	{
		if (goDown) MapModule.GoDown(_state);
		else MapModule.GoUp(_state);
		var center = new WorldCoord(_state.PlayerX, _state.PlayerY, _state.PlayerZ);
		_state.World?.Chunks.UpdateLoadedChunks(center, _state.Turn);
		MapModule.PlacePlayerAtFixture(_state, goDown ? Entities.StairUp : Entities.StairDown);
	}

	private void InitializeWorld()
	{
		MapGenModule.InitializeWorld(_state);
		MapGenModule.FindSpawnPoint(_state);
		MapGenModule.SpawnPlayer(_state);
		SyncViewMode();
	}

	private IViewMode _viewMode = new SingleLayerViewMode();
	public IViewMode ViewMode => _viewMode;

	private void SyncViewMode()
	{
		_viewMode = _state.ViewModeId switch
		{
			"multi_layer" => new MultiLayerViewMode(),
			_ => new SingleLayerViewMode(),
		};
	}

	/// <summary>
	/// 防御性保障：确保玩家 Actor 存在于 Actors 字典中。
	/// </summary>
	private void EnsurePlayerActor()
	{
		if (_state.Actors.ContainsKey(_state.PlayerId))
			return;

		foreach (var a in _state.Actors.Values)
		{
			if (a.Faction != Factions.Player) continue;
			GD.PushWarning($"EnsurePlayerActor: PlayerId '{_state.PlayerId}' missing, recovered existing player Actor '{a.Id}'");
			_state.PlayerId = a.Id;
			_state.PlayerX = a.X;
			_state.PlayerY = a.Y;
			_state.PlayerZ = a.Z;
			return;
		}

		GD.PushWarning("EnsurePlayerActor: no player Actor found at all, creating minimal fallback");
		var player = ActorTemplates.Spawn("player", _state.PlayerId);
		player.X = _state.PlayerX;
		player.Y = _state.PlayerY;
		player.Z = _state.PlayerZ;
		_state.Actors[player.Id] = player;
	}
}
