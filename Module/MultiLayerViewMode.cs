using System.Collections.Generic;
using MiniRPG.Core;
using MiniRPG.Core.World;

namespace MiniRPG.Module;

/// <summary>
/// 多层预览视图模式：显示当前层 + 下一层（z+1）的透视效果。
/// 当前层正常渲染；若当前层格子是非实心地形（地板等），
/// 则显示下层内容的暗色版本（字符前缀 "dim:" 标记，由 RenderModule 处理）。
/// </summary>
public class MultiLayerViewMode : IViewMode
{
	public string Id => "multi_layer";
	public string Name => "多层预览";

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
				var cell = GetMultiLayerCell(state, mx, my, cz);
				row.Add(cell);
			}
			result.Add(row);
		}
		return result;
	}

	private static string GetMultiLayerCell(GameState state, int x, int y, int z)
	{
		var currentCell = state.World!.GetDisplayCell(x, y, z, state.Actors);

		if (state.World.IsSolid(x, y, z))
			return currentCell;

		var hasActorOrEntity = currentCell != state.World.GetTerrain(x, y, z).Glyph;
		if (hasActorOrEntity)
			return currentCell;

		var belowCell = state.World.GetDisplayCell(x, y, z + 1, state.Actors);
		var belowTerrain = state.World.GetTerrain(x, y, z + 1);

		if (belowTerrain.Solid || belowTerrain.StringId != "floor")
			return "dim:" + belowCell;

		return currentCell;
	}
}
