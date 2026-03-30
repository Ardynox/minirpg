namespace MiniRPG.Core;

/// <summary>
/// 游戏事件：逻辑层产出，副作用层（渲染/日志/音效）消费。
/// </summary>
public class GameEvent
{
	public string Type { get; }
	public int TargetX { get; set; }
	public int TargetY { get; set; }

	public GameEvent(string type)
	{
		Type = type;
	}
}
