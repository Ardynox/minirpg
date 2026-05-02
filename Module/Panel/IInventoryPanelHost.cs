using System.Collections.Generic;
using MiniRPG.Core.Data;
using MiniRPG.Core.Multiplayer;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 背包面板（grid 与遗留 list 实现共用）的宿主契约。
/// Main 实现这个接口，把日志 / 事件分发 / 协议提交 / 容器面板切换等统一注入面板。
/// </summary>
public interface IInventoryPanelHost
{
	GameState State { get; }
	bool HasFocus { get; }
	void AddLog(string msg);
	void Dispatch(List<GameEvent> events);
	void SubmitPlayerAction(TimelinePlayerAction action);
	bool TrySubmitClientCommand(ClientCommand command);
	void FlushMap();
	void OpenChestFromInventory(Item chestItem);
	void CloseInventory();
	bool TryHandleItemRightClick(Item item);
}
