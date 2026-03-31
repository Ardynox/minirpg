using System.Collections.Generic;
using MiniRPG.Core;
using MiniRPG.Core.World;

namespace MiniRPG.Module;

/// <summary>
/// 迷雾追踪器：使用 Shadowcasting FOV 计算视线，维护四态：
/// - 朝向可见（IsVisible）：在朝向视野锥内，正常亮色
/// - 周边感知（IsPeripheral）：在全向视野内但不在朝向锥内，灰色但显示实时内容
/// - 已探索（HasSeen）：曾经进入过任何视野，只显示地形记忆
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

	private static long Pack(int x, int y) => ((long)x << 32) | (uint)y;

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
		var sight = player?.GetCapacity(Caps.Sight) ?? 1.0f;
		var frontRadius = (int)(BaseVisionRadius * sight * AmbientLight);
		if (frontRadius < 2) frontRadius = 2;
		var rearRadius = System.Math.Max(2, (int)(frontRadius * RearVisionRatio));

		var cx = state.PlayerX;
		var cy = state.PlayerY;
		var world = state.World;
		bool isOpaque(int x, int y) => world.IsSolid(x, y, z);

		_fullVisible = ShadowcastFOV.Compute(cx, cy, frontRadius, isOpaque);

		var facingX = player?.FacingX ?? 0;
		var facingY = player?.FacingY ?? 1;
		_directionalVisible = ShadowcastFOV.ComputeDirectional(
			cx, cy, frontRadius, rearRadius, facingX, facingY, isOpaque);

		foreach (var (vx, vy) in _fullVisible)
			seenSet.Add(Pack(vx, vy));
	}

	/// <summary>在朝向视野锥内 → 正常亮色渲染。</summary>
	public bool IsVisible(int x, int y, int z)
	{
		if (z != _currentZ) return false;
		return _directionalVisible.Contains((x, y));
	}

	/// <summary>在全向视野内但不在朝向锥内 → 灰色但显示实时内容。</summary>
	public bool IsPeripheral(int x, int y, int z)
	{
		if (z != _currentZ) return false;
		return !_directionalVisible.Contains((x, y)) && _fullVisible.Contains((x, y));
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
