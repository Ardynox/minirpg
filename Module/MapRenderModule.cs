using System.Collections.Generic;
using Godot;
using MiniRPG.Core;
using MiniRPG.Core.World;

namespace MiniRPG.Module;

/// <summary>
/// 地图渲染管线：FOV 更新 → 构建 displayMap → 应用迷雾 → 渲染文本 → 写入 RichTextLabel。
/// 同时管理小地图和大地图的开关与滚动。
/// </summary>
public class MapRenderModule
{
	private readonly int _viewW;
	private readonly int _viewH;
	private readonly GameState _state;
	private readonly FogOfWarTracker _fogTracker;
	private readonly RenderModule _render;
	private readonly MinimapModule _minimap;
	private readonly FogMapModule _fogMap;
	private readonly RichTextLabel _mapText;
	private readonly System.Func<IViewMode> _getViewMode;

	public FogMapModule FogMap => _fogMap;
	public MinimapModule Minimap => _minimap;
	public RenderModule Render => _render;

	public MapRenderModule(
		GameState state, FogOfWarTracker fogTracker,
		RenderModule render, MinimapModule minimap, FogMapModule fogMap,
		RichTextLabel mapText, int viewW, int viewH,
		System.Func<IViewMode> getViewMode)
	{
		_state = state;
		_fogTracker = fogTracker;
		_render = render;
		_minimap = minimap;
		_fogMap = fogMap;
		_mapText = mapText;
		_viewW = viewW;
		_viewH = viewH;
		_getViewMode = getViewMode;
	}

	/// <summary>立即刷新地图面板。</summary>
	public void Flush()
	{
		_fogTracker.Update(_state);

		if (_fogMap.Visible)
		{
			_mapText.BbcodeEnabled = true;
			_mapText.Clear();
			_mapText.AppendText(_fogMap.Render(_state));
			return;
		}

		var displayMap = _getViewMode().BuildDisplayMap(_state, _viewW, _viewH);
		ApplyFOV(displayMap);
		var text = _render.RenderMap(displayMap);

		if (_minimap.Visible)
			text += "\n" + _minimap.Render(_state);

		if (_render.UsesBBCode || _minimap.Visible)
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

	/// <summary>切换 ASCII / Emoji 渲染模式。返回日志文本或 null。</summary>
	public string? ToggleRenderMode()
	{
		var mode = _render.ToggleMode();
		_render.ApplyFont(_mapText);
		return mode == RenderMode.Emoji ? "渲染模式: Emoji 🎨" : "渲染模式: ASCII ⌨️";
	}

	/// <summary>切换小地图。返回日志文本。</summary>
	public string ToggleMinimap()
	{
		if (_fogMap.Visible)
			return "";
		_minimap.Visible = !_minimap.Visible;
		return _minimap.Visible ? "小地图: 开启 (Tab 关闭)" : "小地图: 关闭";
	}

	/// <summary>切换大地图。返回日志文本。</summary>
	public string ToggleFogMap()
	{
		_fogMap.Visible = !_fogMap.Visible;
		if (_fogMap.Visible)
		{
			_fogMap.CenterOnPlayer(_state);
			return "大地图: 开启 (WASD 滚动 / C 回中心 / M 关闭)";
		}
		return "大地图: 关闭";
	}

	public void CenterFogMap()
	{
		if (!_fogMap.Visible) return;
		_fogMap.CenterOnPlayer(_state);
	}

	public void ScrollFogMap(int dx, int dy)
	{
		_fogMap.Scroll(dx, dy);
	}

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
}
