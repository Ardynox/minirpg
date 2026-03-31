using System.Text;
using MiniRPG.Core;
using MiniRPG.Core.World;

namespace MiniRPG.Module;

/// <summary>
/// 小地图模块：在主视口旁或叠加层显示一个缩略的周围区域。
/// 范围比主视口大（默认 41x21），每格用单个字符/颜色表示地形。
/// 未探索区域显示为迷雾。玩家位置高亮。
/// </summary>
public class MinimapModule
{
	public int Width { get; set; } = 41;
	public int Height { get; set; } = 21;
	public bool Visible { get; set; }

	private readonly FogOfWarTracker _fog;

	public MinimapModule(FogOfWarTracker fog) => _fog = fog;

	/// <summary>
	/// 生成小地图的 BBCode 富文本字符串。
	/// 每格一个字符宽，用颜色区分地形。玩家用 @ 标记。
	/// </summary>
	public string Render(GameState state)
	{
		if (state.World == null) return "";

		var cx = state.PlayerX;
		var cy = state.PlayerY;
		var cz = state.PlayerZ;
		var halfW = Width / 2;
		var halfH = Height / 2;
		var sb = new StringBuilder();

		sb.Append("[color=#888888]── 小地图 ──[/color]\n");

		for (var vy = 0; vy < Height; vy++)
		{
			var wy = cy - halfH + vy;
			for (var vx = 0; vx < Width; vx++)
			{
				var wx = cx - halfW + vx;

				if (wx == cx && wy == cy)
				{
					sb.Append("[color=#44ee44]@[/color]");
					continue;
				}

				if (!_fog.HasSeen(wx, wy, cz))
				{
					sb.Append("[color=#111111]·[/color]");
					continue;
				}

				var actor = FindActor(state, wx, wy, cz);
				if (actor != null)
				{
					var actorColor = actor.Faction == Factions.Hostile ? "#ee4444"
						: actor.Faction == Factions.Friendly ? "#44cc44"
						: "#eeee44";
					sb.Append($"[color={actorColor}]*[/color]");
					continue;
				}

				var terrain = state.World.GetTerrain(wx, wy, cz);
				sb.Append(TerrainMiniChar(terrain));
			}
			sb.Append('\n');
		}

		return sb.ToString();
	}

	private static Actor? FindActor(GameState state, int x, int y, int z)
	{
		foreach (var a in state.Actors.Values)
			if (a.X == x && a.Y == y && a.Z == z)
				return a;
		return null;
	}

	private static string TerrainMiniChar(TerrainDef t) => t.StringId switch
	{
		Terrains.Void => " ",
		Terrains.Floor => "[color=#333333].[/color]",
		Terrains.WallSoil => "[color=#665533]#[/color]",
		Terrains.WallStone => "[color=#555555]#[/color]",
		Terrains.WallGranite => "[color=#777777]#[/color]",
		Terrains.WallObsidian => "[color=#333344]#[/color]",
		Terrains.Rubble => "[color=#554433].[/color]",
		Terrains.Grass => "[color=#338833].[/color]",
		Terrains.Water => "[color=#2266cc]~[/color]",
		Terrains.Tree => "[color=#22aa44]T[/color]",
		Terrains.Lava => "[color=#cc4400]~[/color]",
		Terrains.Sand => "[color=#ccbb66].[/color]",
		Terrains.Mountain => "[color=#888888]^[/color]",
		_ => "[color=#444444]?[/color]",
	};
}
