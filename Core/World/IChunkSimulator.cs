namespace MiniRPG.Core.World;

/// <summary>
/// 最小模拟接口：对已加载但远离玩家的 chunk 执行简化模拟。
/// 初始实现为空（NullSimulator），内核后续填充。
/// </summary>
public interface IChunkSimulator
{
	/// <summary>对单个 chunk 执行一回合的简化模拟。</summary>
	void TickChunk(ChunkData chunk, GameState state);
}

/// <summary>空模拟器：不做任何事。作为默认值使用。</summary>
public class NullSimulator : IChunkSimulator
{
	public void TickChunk(ChunkData chunk, GameState state) { }
}
