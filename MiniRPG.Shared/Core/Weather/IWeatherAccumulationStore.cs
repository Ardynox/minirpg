using MiniRPG.Core.World;

namespace MiniRPG.Core.Weather;

/// <summary>
/// Provides weather accumulation data for chunks that are not currently loaded in memory
/// but exist in a persistence cache. This decouples the weather system from save/load internals.
/// </summary>
public interface IWeatherAccumulationStore
{
	bool TryGetAccumulation(ChunkCoord coord, int localIndex, out WeatherAccumulation data);
	void ClearAllAccumulation(int currentTurn);
}
