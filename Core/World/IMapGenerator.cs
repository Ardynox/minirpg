namespace MiniRPG.Core.World;

/// <summary>
/// 地图生成器接口。每种生成风格实现一个。
/// 必须是确定性的：相同 (ChunkCoord, worldSeed) → 相同结果。
/// </summary>
public interface IMapGenerator
{
	string Id { get; }
	string Name { get; }

	/// <summary>
	/// 生成单个 chunk 的地形数据（TerrainIds + Hardness）。
	/// chunk 已被分配但未填充，生成器应写入 TerrainIds 和 Hardness 数组。
	/// </summary>
	void GenerateChunk(ChunkData chunk, int worldSeed);

	/// <summary>
	/// 在已生成地形的 chunk 中放置设施、巢穴等。
	/// 在 GenerateChunk 之后调用。
	/// </summary>
	void PopulateChunk(ChunkData chunk, int worldSeed);
}
