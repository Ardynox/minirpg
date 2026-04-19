namespace MiniRPG.Module.Render.Surface;

/// <summary>
/// SurfaceCover 抽象的第一个落地实例：在已绘制的土块顶面之上叠加草地变体。
/// 仅画顶面，不动侧面（侧面继续保持土的外观，是契约的硬要求）。
///
/// Wave 1 路 D：用 <c>object ctx</c> 占位独立开发；Wave 2.2 接入时
/// 由 <see cref="MiniRPG.Module.Render"/> 主渲染器替换为真实上下文类型
/// （最小需要：FaceCommands 列表 + TerrainAtlas 区域 + ScreenPos + SortKey + Top 面 Tint）。
///
/// 接口契约见 <c>Artifacts/grass-task-handoff.md</c>（接口 3）。
/// </summary>
public static class GrassOverlayPass
{
	/// <summary>
	/// 草地变体数量。路 C 还没注册贴图前先用常量 6 占位；
	/// 路 C 完成后由本常量统一对齐到 Atlas 实际注册数（同一处改动）。
	/// </summary>
	internal const int VariantCount = 6;

	/// <summary>
	/// 变体哈希用的常量种子。Wave 2.2 接入时换成真正的 worldSeed
	/// （来自 WorldConfig），让"同一存档同一坐标永远同一变体"。
	/// 现阶段任意稳定常数即可，选 0x9E3779B9（黄金比例）做散列友好的初值。
	/// </summary>
	private const int VariantHashSeed = unchecked((int)0x9E3779B9);

	/// <summary>
	/// 在已绘制的土块顶面之上叠加草地。仅画顶面，不动侧面。
	/// </summary>
	/// <param name="ctx">渲染上下文（Wave 1 占位，Wave 2.2 替换为真实类型）。</param>
	/// <param name="cover">本格 GrassCover byte（0..255），0 = 无草。</param>
	/// <param name="wx">世界 X，用于哈希选变体。</param>
	/// <param name="wy">世界 Y，用于哈希选变体。</param>
	// TODO Wave 2.2: 替换 ctx 为真实渲染上下文（最小需要：FaceCommands 列表
	// + TerrainAtlas 区域查询 + ScreenPos + SortKey + Top 面 Tint）。
	public static void DrawTopFace(object ctx, byte cover, int wx, int wy)
	{
		if (cover == 0) return;

		var variant = SelectVariant(wx, wy);
		EmitVariantSprite(ctx, variant, cover);
	}

	/// <summary>
	/// 根据世界坐标确定性地选择变体索引（0..VariantCount-1）。
	/// 同一 (wx, wy) 永远返回同一变体；分布近似均匀，避免肉眼可见的 tile-repeat。
	/// 暴露为 <c>internal</c> 以便单测做分布手测。
	/// </summary>
	internal static int SelectVariant(int wx, int wy)
	{
		unchecked
		{
			uint h = (uint)VariantHashSeed;
			h = (h ^ (uint)wx) * 0x01000193u;
			h = (h ^ (uint)wy) * 0x01000193u;
			h ^= h >> 13;
			h *= 0x5BD1E995u;
			h ^= h >> 15;
			return (int)(h % (uint)VariantCount);
		}
	}

	/// <summary>
	/// 把"如何取贴图 + 提交一条草地 face sprite 到渲染队列"封装在这一处，
	/// 方便 Wave 2.2 接入时只改本方法（不动 <see cref="DrawTopFace"/> 的接口）。
	///
	/// Wave 1 占位实现：什么都不做（路 C 还没注册贴图、ctx 还是 object）。
	/// Wave 2.2 接入清单：
	///   1. 把 <paramref name="ctx"/> 强转为主渲染器的真实上下文类型；
	///   2. 通过 TerrainAtlas / 专用 GrassOverlayAtlas 查到 variant 对应的 Rect2 区域；
	///   3. 用 ctx 内的 ScreenPos + SortKey 构造 FaceSpriteCommand；
	///   4. cover 强度可换算成 alpha 衰减（如 cover &lt; 64 时让 tint.A 按比例降低），
	///      让低密度草地视觉上稀疏一些；具体曲线在接入时调。
	/// </summary>
	private static void EmitVariantSprite(object ctx, int variant, byte cover)
	{
		_ = ctx;
		_ = variant;
		_ = cover;
	}
}
