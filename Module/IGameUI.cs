using System;

namespace MiniRPG.Module;

/// <summary>
/// UI 模块与 Main 之间的通信契约。
/// 所有 UI 子模块通过此接口访问日志、选择模式、地图刷新和游戏状态。
/// </summary>
public interface IGameUI
{
	void AddLog(string msg);
	void EnterSelection(Action<int> callback);
	void CancelSelection();
	void FlushMap();
	void Dispatch(System.Collections.Generic.List<GameEvent> events);
	void SubmitPlayerAction(TimelinePlayerAction action);
	bool TryHandleItemRightClick(Item item);
	GameState State { get; }
	bool PlayerDead { get; set; }

	/// <summary>统一的玩家死亡处理入口。reason: "killed" / "incapacitated"。</summary>
	void HandlePlayerDeath(string reason);
}
