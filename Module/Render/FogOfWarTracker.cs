using System.Collections.Generic;
using MiniRPG.Core.World;

namespace MiniRPG.Module.Render;

public enum PlayerVisionBand
{
	Unknown,
	Memory,
	Peripheral,
	Focused,
}

/// <summary>
/// 迷雾追踪器：使用 Shadowcasting FOV 计算视线，维护四态：
/// - Focused：主视野，完整信息
/// - Peripheral：周边感知，低保真实时信息
/// - Memory：已探索但当前不可感知，只保留地形记忆
/// - 未探索：从未见过，黑色迷雾
/// 朝向视野和全向视野都会破开迷雾（写入已探索）。
/// </summary>
public class FogOfWarTracker
{
	private readonly Dictionary<int, HashSet<long>> _seen = new();

	/// <summary>朝向视野内的格子（前方远、后方近）。</summary>
	private HashSet<(int X, int Y)> _directionalVisible = new();

	/// <summary>全向视野内的格子（不受朝向影响的完整圆）。</summary>
	private HashSet<(int X, int Y)> _fullVisible = new();

	private int _currentZ;

	/// <summary>基础视野半径。</summary>
	public int BaseVisionRadius { get; set; } = 12;

	/// <summary>后方视野占前方视野的比例。</summary>
	public float RearVisionRatio { get; set; } = 0.4f;

	/// <summary>环境光照等级：0=完全黑暗，1=正常光照。</summary>
	public float AmbientLight { get; set; } = 1.0f;

	public int MinimumVisionRadius { get; set; } = 2;

	public int MinimumRearVisionRadius { get; set; } = 2;

	private static long Pack(int x, int y) => ((long)x << 32) | (uint)y;

	public FogOfWarTracker()
	{
	}

	public FogOfWarTracker(PlayerVisionConfig config)
	{
		BaseVisionRadius = config.BaseVisionRadius;
		RearVisionRatio = config.RearVisionRatio;
		AmbientLight = config.AmbientLight;
		MinimumVisionRadius = config.MinimumVisionRadius;
		MinimumRearVisionRadius = config.MinimumRearVisionRadius;
	}

	/// <summary>
	/// 更新迷雾。计算两套视野：
	/// 1. 全向视野（完整半径圆）→ 破开迷雾 + 周边感知
	/// 2. 朝向视野（前方远后方近）→ 正常亮色可见
	/// </summary>
	public void Update(GameState state)
	{
		if (state.World == null) return;

		var z = state.PlayerZ;
		_currentZ = z;

		if (!_seen.TryGetValue(z, out var seenSet))
		{
			seenSet = new HashSet<long>();
			_seen[z] = seenSet;
		}

		var player = ActorModule.GetPlayer(state);
		var sight = System.Math.Max(0f, player?.GetCapacity(Caps.Sight) ?? 1.0f);
		var baseRadius = BaseVisionRadius;
		if (z == 0 && state.World.IsWeatherExposed(state.PlayerX, state.PlayerY, z))
		{
			var weather = WeatherRules.GetLocalWeather(state, state.PlayerX, state.PlayerY, z);
			baseRadius = System.Math.Max(1, (int)System.MathF.Round(baseRadius * WeatherRules.GetVisionMultiplier(weather)));
		}

		var vision = VisionRangeScaler.ScaleDirectional(
			baseRadius,
			sight,
			RearVisionRatio,
			MinimumVisionRadius,
			MinimumRearVisionRadius,
			AmbientLight);
		var frontRadius = vision.FrontRadius;
		var rearRadius = vision.RearRadius;

		var cx = state.PlayerX;
		var cy = state.PlayerY;
		var world = state.World;
		bool isOpaque(int x, int y) => world.BlocksSight(x, y, z);

		_fullVisible = ShadowcastFOV.Compute(cx, cy, frontRadius, isOpaque);

		var facingX = player?.FacingX ?? 0;
		var facingY = player?.FacingY ?? 1;
		_directionalVisible = ShadowcastFOV.ComputeDirectional(
			cx, cy, frontRadius, rearRadius, facingX, facingY, _fullVisible);

		foreach (var (vx, vy) in _fullVisible)
			seenSet.Add(Pack(vx, vy));
	}

	public PlayerVisionBand GetVisionBand(int x, int y, int z)
	{
		if (z != _currentZ) return PlayerVisionBand.Unknown;
		if (_directionalVisible.Contains((x, y))) return PlayerVisionBand.Focused;
		if (_fullVisible.Contains((x, y))) return PlayerVisionBand.Peripheral;
		if (HasSeen(x, y, z)) return PlayerVisionBand.Memory;
		return PlayerVisionBand.Unknown;
	}

	/// <summary>在朝向视野锥内 → 完整信息。</summary>
	public bool IsVisible(int x, int y, int z)
	{
		return GetVisionBand(x, y, z) == PlayerVisionBand.Focused;
	}

	/// <summary>在全向视野内但不在朝向锥内 → 低保真实时信息。</summary>
	public bool IsPeripheral(int x, int y, int z)
	{
		return GetVisionBand(x, y, z) == PlayerVisionBand.Peripheral;
	}

	/// <summary>曾经进入过视野。</summary>
	public bool HasSeen(int x, int y, int z)
	{
		if (!_seen.TryGetValue(z, out var set)) return false;
		return set.Contains(Pack(x, y));
	}

	public (int MinX, int MinY, int MaxX, int MaxY)? GetBounds(int z)
	{
		if (!_seen.TryGetValue(z, out var set) || set.Count == 0) return null;

		int minX = int.MaxValue, minY = int.MaxValue;
		int maxX = int.MinValue, maxY = int.MinValue;

		foreach (var packed in set)
		{
			var x = (int)(packed >> 32);
			var y = (int)(packed & 0xFFFFFFFF);
			if (x < minX) minX = x;
			if (x > maxX) maxX = x;
			if (y < minY) minY = y;
			if (y > maxY) maxY = y;
		}
		return (minX, minY, maxX, maxY);
	}

	public void Clear()
	{
		_seen.Clear();
		_directionalVisible.Clear();
		_fullVisible.Clear();
	}

	public int ExploredCount(int z) =>
		_seen.TryGetValue(z, out var set) ? set.Count : 0;
}
