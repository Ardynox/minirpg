using System;
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
///
/// 立体视野：在每个 actor 所在层及其上下相邻层分别执行一次 FOV，
/// 并按高度差衰减半径（越偏离 actor 层，可见半径越小）。
/// </summary>
public class FogOfWarTracker
{
	private readonly Dictionary<int, HashSet<long>> _seen = new();

	/// <summary>朝向视野内的格子（前方远、后方近）。</summary>
	private HashSet<(int X, int Y, int Z)> _directionalVisible = new();

	/// <summary>全向视野内的格子（不受朝向影响的完整圆）。</summary>
	private HashSet<(int X, int Y, int Z)> _fullVisible = new();

	/// <summary>基础视野半径。</summary>
	public int BaseVisionRadius { get; set; } = 12;

	/// <summary>后方视野占前方视野的比例。</summary>
	public float RearVisionRatio { get; set; } = 0.4f;

	/// <summary>环境光照等级：0=完全黑暗，1=正常光照。</summary>
	public float AmbientLight { get; set; } = 1.0f;

	public int MinimumVisionRadius { get; set; } = 2;

	public int MinimumRearVisionRadius { get; set; } = 2;

	/// <summary>立体视野上下采样层数（1 表示仅上下各一层）。</summary>
	public int VerticalVisionDepth { get; set; } = 1;

	/// <summary>高度差每增加 1 层时，前向半径衰减值。</summary>
	public int VerticalRadiusFalloffPerLevel { get; set; } = 2;

	/// <summary>调试：全图视野模式，所有格子视为 Focused。</summary>
	public bool RevealAll { get; set; }

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
	///
	/// 在 actor 层及其上下相邻层执行，形成 3D 视野体。
	/// </summary>
	public void Update(GameState state)
	{
		if (state.World == null) return;

		_fullVisible = [];
		_directionalVisible = [];

		var world = state.World;
		var visionActors = RoomRuntimeModule.GetVisionActors(state, connectedOnly: false);
		if (visionActors.Count == 0)
		{
			var fallback = ActorModule.GetPlayer(state);
			if (fallback != null)
				visionActors = [fallback];
		}

		foreach (var actor in visionActors)
		{
			var sight = Math.Max(0f, actor.GetCapacity(Caps.Sight));
			var baseRadius = BaseVisionRadius;
			if (world.IsWeatherExposed(actor.X, actor.Y, actor.Z))
			{
				var weather = WeatherRules.GetLocalWeather(state, actor.X, actor.Y, actor.Z);
				baseRadius = Math.Max(1, (int)MathF.Round(baseRadius * WeatherRules.GetVisionMultiplier(weather)));
			}

			var baseVision = VisionRangeScaler.ScaleDirectional(
				baseRadius,
				sight,
				RearVisionRatio,
				MinimumVisionRadius,
				MinimumRearVisionRadius,
				AmbientLight);

			var maxDepth = Math.Max(0, VerticalVisionDepth);
			for (var dz = -maxDepth; dz <= maxDepth; dz++)
			{
				var targetZ = actor.Z + dz;
				var absDz = Math.Abs(dz);
				var frontRadius = Math.Max(
					MinimumVisionRadius,
					baseVision.FrontRadius - absDz * Math.Max(0, VerticalRadiusFalloffPerLevel));
				var rearRadius = Math.Max(
					MinimumRearVisionRadius,
					baseVision.RearRadius - absDz * Math.Max(0, VerticalRadiusFalloffPerLevel));

				bool isOpaque(int x, int y) => world.BlocksSight(x, y, targetZ);

				var fullVisible = ShadowcastFOV.Compute(actor.X, actor.Y, frontRadius, isOpaque);
				var directionalVisible = ShadowcastFOV.ComputeDirectional(
					actor.X,
					actor.Y,
					frontRadius,
					rearRadius,
					actor.FacingX,
					actor.FacingY,
					fullVisible);

				if (!_seen.TryGetValue(targetZ, out var seenSet))
				{
					seenSet = new HashSet<long>();
					_seen[targetZ] = seenSet;
				}

				foreach (var (vx, vy) in fullVisible)
				{
					seenSet.Add(Pack(vx, vy));
					_fullVisible.Add((vx, vy, targetZ));
				}

				foreach (var (vx, vy) in directionalVisible)
					_directionalVisible.Add((vx, vy, targetZ));
			}
		}
	}

	public PlayerVisionBand GetVisionBand(int x, int y, int z)
	{
		if (RevealAll) return PlayerVisionBand.Focused;
		if (_directionalVisible.Contains((x, y, z))) return PlayerVisionBand.Focused;
		if (_fullVisible.Contains((x, y, z))) return PlayerVisionBand.Peripheral;
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
