using System.Collections.Generic;
using System.Text;
using Godot;
using MiniRPG.Core.World;

namespace MiniRPG.Module.Render;

/// <summary>
/// 地图渲染管线：FOV 更新 → 构建 displayMap → 应用迷雾 → 渲染文本 → 写入 RichTextLabel。
/// 同时内置小地图和大地图的渲染与开关控制。
/// </summary>
public class MapRenderModule
{
	private readonly int _viewW;
	private readonly int _viewH;
	private readonly GameState _state;
	private readonly FogOfWarTracker _fogTracker;
	private readonly RenderModule _render;
	private readonly RichTextLabel _mapText;
	private readonly System.Func<IViewMode> _getViewMode;

	public RenderModule Render => _render;

	public bool FogMapVisible { get; set; }
	public bool MinimapVisible { get; set; }

	private int _minimapW = 41;
	private int _minimapH = 21;

	private int _fogMapW = 81;
	private int _fogMapH = 41;
	private int _fogOffsetX;
	private int _fogOffsetY;

	public MapRenderModule(
		GameState state, FogOfWarTracker fogTracker,
		RenderModule render, RichTextLabel mapText,
		int viewW, int viewH, System.Func<IViewMode> getViewMode)
	{
		_state = state;
		_fogTracker = fogTracker;
		_render = render;
		_mapText = mapText;
		_viewW = viewW;
		_viewH = viewH;
		_getViewMode = getViewMode;
	}

	/// <summary>新游戏时重置叠加层状态。</summary>
	public void ResetOverlays()
	{
		FogMapVisible = false;
		MinimapVisible = false;
	}

	// ── 主渲染 ────────────────────────────────────────────

	public void Flush()
	{
		_fogTracker.Update(_state);

		if (FogMapVisible)
		{
			_mapText.BbcodeEnabled = true;
			_mapText.Clear();
			_mapText.AppendText(RenderFogMap());
			return;
		}

		var displayMap = _getViewMode().BuildDisplayMap(_state, _viewW, _viewH);
		ApplyFOV(displayMap);
		var text = _render.RenderMap(displayMap);

		if (MinimapVisible)
			text += "\n" + RenderMinimap();

		if (_render.UsesBBCode || MinimapVisible)
		{
			_mapText.BbcodeEnabled = true;
			_mapText.Clear();
			_mapText.AppendText(text);
		}
		else
		{
			_mapText.BbcodeEnabled = false;
			_mapText.Text = text;
		}
	}

	// ── 模式切换 ──────────────────────────────────────────

	public string? ToggleRenderMode()
	{
		var mode = _render.ToggleMode();
		_render.ApplyFont(_mapText);
		return mode == RenderMode.Emoji ? "渲染模式: Emoji 🎨" : "渲染模式: ASCII ⌨️";
	}

	public string ToggleMinimap()
	{
		if (FogMapVisible) return "";
		MinimapVisible = !MinimapVisible;
		return MinimapVisible ? "小地图: 开启 (Tab 关闭)" : "小地图: 关闭";
	}

	public string ToggleFogMap()
	{
		FogMapVisible = !FogMapVisible;
		if (FogMapVisible)
		{
			_fogOffsetX = 0;
			_fogOffsetY = 0;
			return "大地图: 开启 (WASD 滚动 / C 回中心 / M 关闭)";
		}
		return "大地图: 关闭";
	}

	public void CenterFogMap()
	{
		if (!FogMapVisible) return;
		_fogOffsetX = 0;
		_fogOffsetY = 0;
	}

	public void ScrollFogMap(int dx, int dy)
	{
		_fogOffsetX += dx * 10;
		_fogOffsetY += dy * 5;
	}

	// ── FOV 叠加 ─────────────────────────────────────────

	private void ApplyFOV(List<List<string>> displayMap)
	{
		var cx = _state.PlayerX;
		var cy = _state.PlayerY;
		var cz = _state.PlayerZ;
		var halfW = _viewW / 2;
		var halfH = _viewH / 2;

		for (var vy = 0; vy < displayMap.Count; vy++)
		{
			var row = displayMap[vy];
			var wy = cy - halfH + vy;
			for (var vx = 0; vx < row.Count; vx++)
			{
				var wx = cx - halfW + vx;

				if (_fogTracker.IsVisible(wx, wy, cz))
					continue;

				if (_fogTracker.IsPeripheral(wx, wy, cz))
				{
					row[vx] = "per:" + row[vx];
					continue;
				}

				if (_fogTracker.HasSeen(wx, wy, cz))
				{
					var terrain = _state.World?.GetTerrain(wx, wy, cz);
					row[vx] = "mem:" + (terrain?.Glyph ?? " ");
				}
				else
				{
					row[vx] = "fog:";
				}
			}
		}
	}

	// ── 小地图渲染 ────────────────────────────────────────

	private string RenderMinimap()
	{
		if (_state.World == null) return "";

		var cx = _state.PlayerX;
		var cy = _state.PlayerY;
		var cz = _state.PlayerZ;
		var halfW = _minimapW / 2;
		var halfH = _minimapH / 2;
		var sb = new StringBuilder();

		sb.Append("[color=#888888]── 小地图 ──[/color]\n");

		for (var vy = 0; vy < _minimapH; vy++)
		{
			var wy = cy - halfH + vy;
			for (var vx = 0; vx < _minimapW; vx++)
			{
				var wx = cx - halfW + vx;

				if (wx == cx && wy == cy)
				{
					sb.Append("[color=#44ee44]@[/color]");
					continue;
				}

				if (!_fogTracker.HasSeen(wx, wy, cz))
				{
					sb.Append("[color=#111111]·[/color]");
					continue;
				}

				var actor = FindActor(wx, wy, cz);
				if (actor != null)
				{
					var actorColor = actor.Faction == Factions.Hostile ? "#ee4444"
						: actor.Faction == Factions.Friendly ? "#44cc44"
						: "#eeee44";
					sb.Append($"[color={actorColor}]*[/color]");
					continue;
				}

				var terrain = _state.World.GetTerrain(wx, wy, cz);
				sb.Append(TerrainMiniChar(terrain));
			}
			sb.Append('\n');
		}

		return sb.ToString();
	}

	// ── 大地图渲染 ────────────────────────────────────────

	private string RenderFogMap()
	{
		if (_state.World == null) return "";

		var cx = _state.PlayerX + _fogOffsetX;
		var cy = _state.PlayerY + _fogOffsetY;
		var cz = _state.PlayerZ;
		var halfW = _fogMapW / 2;
		var halfH = _fogMapH / 2;

		var sb = new StringBuilder();
		sb.Append($"[color=#888888]── 大地图  Z{cz}  已探索: {_fogTracker.ExploredCount(cz)} 格 ──[/color]\n");

		for (var vy = 0; vy < _fogMapH; vy++)
		{
			var wy = cy - halfH + vy;
			for (var vx = 0; vx < _fogMapW; vx++)
			{
				var wx = cx - halfW + vx;

				if (wx == _state.PlayerX && wy == _state.PlayerY && _fogOffsetX == 0 && _fogOffsetY == 0)
				{
					sb.Append("[color=#44ee44]@[/color]");
					continue;
				}

				if (!_fogTracker.HasSeen(wx, wy, cz))
				{
					sb.Append("[color=#0a0a0a]░[/color]");
					continue;
				}

				var actor = FindActor(wx, wy, cz);
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

				var fixture = _state.World.GetFirstEntity(wx, wy, cz, CellEntityType.Fixture);
				if (fixture != null)
				{
					sb.Append(FixtureMiniChar(fixture.EntityId));
					continue;
				}

				var terrain = _state.World.GetTerrain(wx, wy, cz);
				sb.Append(TerrainMiniChar(terrain));
			}
			sb.Append('\n');
		}

		sb.Append("[color=#555555]方向键移动视点 | C 回到玩家 | M 关闭[/color]");
		return sb.ToString();
	}

	// ── 共享工具 ──────────────────────────────────────────

	private Actor? FindActor(int x, int y, int z)
	{
		foreach (var a in _state.Actors.Values)
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
