using System.Collections.Generic;

namespace MiniRPG.Core.World;

/// <summary>
/// 视图模式接口：决定如何将三维世界投影到二维显示地图。
/// 不同实现展示不同的可视化效果（单层 / 多层预览等）。
/// </summary>
public interface IViewMode
{
	string Id { get; }
	string Name { get; }

	/// <summary>
	/// 构建用于渲染的二维显示地图。
	/// 返回 viewH 行 x viewW 列的字符矩阵。
	/// </summary>
	List<List<string>> BuildDisplayMap(GameState state, int viewW, int viewH);
}
