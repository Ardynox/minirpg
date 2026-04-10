using System.Collections.Generic;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Event;

/// <summary>
/// 事件执行器接口：每种事件类型实现一个 Worker。
/// </summary>
public interface IIncidentWorker
{
	/// <summary>
	/// 检查事件是否可以在当前状态下触发。
	/// </summary>
	bool CanFire(GameState state, IncidentDef def);

	/// <summary>
	/// 执行事件，返回产生的 GameEvent 列表。
	/// </summary>
	List<GameEvent> Execute(GameState state, IncidentDef def, Dictionary<string, string> runtimeParams);
}
