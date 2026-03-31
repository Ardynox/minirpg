using System.Text;
using MiniRPG.Core;
using MiniRPG.Core.World;

namespace MiniRPG.Module;

/// <summary>
/// 大地图模块（带迷雾）：显示当前 Z 层已探索的全部区域。
/// 未探索的格子显示为迷雾（暗色填充），已探索的显示真实地形。
/// 自动计算已探索边界，以玩家为中心裁剪显示。
/// 大地图模式是一个全屏叠加，按键打开/关闭。
/// </summary>
public class FogMapModule
{
	/// <summary>大地图显示窗口的宽高（字符数）。</summary>
	public int DisplayW { get; set; } = 81;
	public int DisplayH { get; set; } = 41;
	public bool Visible { get; set; }

	/// <summary>大地图视点偏移（用于滚动浏览）。</summary>
	public int OffsetX { get; set; }
	public int OffsetY { get; set; }

	private readonly FogOfWarTracker _fog;

	public FogMapModule(FogOfWarTracker fog) => _fog = fog;

	/// <summary>以玩家为中心重置视点。</summary>
	public void CenterOnPlayer(GameState state)
	{
		OffsetX = 0;
		OffsetY = 0;
	}

	/// <summary>滚动大地图视点。</summary>
	public void Scroll(int dx, int dy)
	{
		OffsetX += dx * 10;
		OffsetY += dy * 5;
	}

	/// <summary>
	/// 生成大地图的 BBCode 富文本字符串。
	/// 已探索区域显示真实地形（缩略），未探索区域显示迷雾。
	/// 玩家位置用 @ 高亮。
	/// </summary>
	public string Render(GameState state)
	{
		if (state.World == null) return "";

		var cx = state.PlayerX + OffsetX;
		var cy = state.PlayerY + OffsetY;
		var cz = state.PlayerZ;
		var halfW = DisplayW / 2;
		var halfH = DisplayH / 2;

		var sb = new StringBuilder();
		sb.Append($"[color=#888888]── 大地图  Z{cz}  已探索: {_fog.ExploredCount(cz)} 格 ──[/color]\n");

		for (var vy = 0; vy < DisplayH; vy++)
		{
			var wy = cy - halfH + vy;
			for (var vx = 0; vx < DisplayW; vx++)
			{
				var wx = cx - halfW + vx;

				if (wx == state.PlayerX && wy == state.PlayerY && OffsetX == 0 && OffsetY == 0)
				{
					sb.Append("[color=#44ee44]@[/color]");
					continue;
				}

				if (!_fog.HasSeen(wx, wy, cz))
				{
					sb.Append("[color=#0a0a0a]░[/color]");
					continue;
				}

				var actor = FindActorFast(state, wx, wy, cz);
				if (actor != null)
				{
					var col = actor.Faction switch
					{
						Factions.Hostile => "#ee4444",
						Factions.Friendly => "#44cc44",
						Factions.Player => "#44ee44",
						_ => "#eeee44",
					};
					sb.Append($"[color={col}]*[/color]");
					continue;
				}

				var fixture = state.World.GetFirstEntity(wx, wy, cz, CellEntityType.Fixture);
				if (fixture != null)
				{
					sb.Append(FixtureMiniChar(fixture.EntityId));
					continue;
				}

				var terrain = state.World.GetTerrain(wx, wy, cz);
				sb.Append(TerrainMiniChar(terrain));
			}
			sb.Append('\n');
		}

		sb.Append("[color=#555555]方向键移动视点 | C 回到玩家 | M 关闭[/color]");
		return sb.ToString();
	}

	private static Actor? FindActorFast(GameState state, int x, int y, int z)
	{
		foreach (var a in state.Actors.Values)
			if (a.X == x && a.Y == y && a.Z == z)
				return a;
		return null;
	}

	private static string FixtureMiniChar(string fixtureId) => fixtureId switch
	{
		Entities.StairDown => "[color=#00ccff]>[/color]",
		Entities.StairUp => "[color=#00ccff]<[/color]",
		Entities.Nest => "[color=#aa44ff]N[/color]",
		Entities.House => "[color=#aa8844]H[/color]",
		Entities.Door => "[color=#ffaa00]D[/color]",
		_ => "[color=#666666]?[/color]",
	};

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
