using System.Collections.Generic;
using MiniRPG.Core;
using MiniRPG.Core.World;

namespace MiniRPG.Module;

/// <summary>
/// 单层视图模式：只显示玩家所在 Z 层的 viewW x viewH 区域。
/// 移植自旧 Main.BuildDisplayMap 逻辑。
/// </summary>
public class SingleLayerViewMode : IViewMode
{
	public string Id => "single_layer";
	public string Name => "单层视图";

	public List<List<string>> BuildDisplayMap(GameState state, int viewW, int viewH)
	{
		var cx = state.PlayerX;
		var cy = state.PlayerY;
		var cz = state.PlayerZ;
		var halfW = viewW / 2;
		var halfH = viewH / 2;

		var result = new List<List<string>>();
		for (var vy = 0; vy < viewH; vy++)
		{
			var row = new List<string>();
			var my = cy - halfH + vy;
			for (var vx = 0; vx < viewW; vx++)
			{
				var mx = cx - halfW + vx;
				row.Add(state.World!.GetDisplayCell(mx, my, cz, state.Actors));
			}
			result.Add(row);
		}
		return result;
	}
}
