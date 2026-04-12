using MiniRPG.Core.Weather;
using MiniRPG.Core.World;

namespace MiniRPG.Core.Map;

/// <summary>
/// Adapts <see cref="SaveModule.DirtyChunkCache"/> to <see cref="IWeatherAccumulationStore"/>
/// so the weather system can read/clear accumulation without knowing about save internals.
/// </summary>
internal sealed class SaveModuleWeatherAdapter : IWeatherAccumulationStore
{
	public bool TryGetAccumulation(ChunkCoord coord, int localIndex, out WeatherAccumulation data)
	{
		if (!SaveModule.DirtyChunkCache.TryGetValue(coord, out var snapshot))
		{
			data = default;
			return false;
		}

		data = new WeatherAccumulation(
			ReadAt(snapshot.SnowDepth, localIndex),
			ReadAt(snapshot.SandDepth, localIndex),
			ReadAt(snapshot.Wetness, localIndex),
			ReadAt(snapshot.IceDepth, localIndex));
		return true;
	}

	public void ClearAllAccumulation(int currentTurn)
	{
		foreach (var snapshot in SaveModule.DirtyChunkCache.Values)
		{
			snapshot.SnowDepth ??= [];
			snapshot.SandDepth ??= [];
			snapshot.Wetness ??= [];
			snapshot.IceDepth ??= [];
			System.Array.Fill(snapshot.SnowDepth, (byte)0);
			System.Array.Fill(snapshot.SandDepth, (byte)0);
			System.Array.Fill(snapshot.Wetness, (byte)0);
			System.Array.Fill(snapshot.IceDepth, (byte)0);
			snapshot.LastWeatherSimTurn = currentTurn;
		}
	}

	private static byte ReadAt(byte[]? values, int index) =>
		values != null && (uint)index < (uint)values.Length ? values[index] : (byte)0;
}
