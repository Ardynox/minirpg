using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace MiniRPG.Module.Panel;

public sealed class PanelLayoutStore(string path = "user://panel_layout.json")
{
	private readonly string _path = path;
	private readonly Dictionary<string, PanelPos> _positions = [];

	public void Load()
	{
		_positions.Clear();
		if (!Godot.FileAccess.FileExists(_path))
			return;

		try
		{
			using var file = Godot.FileAccess.Open(_path, Godot.FileAccess.ModeFlags.Read);
			if (file == null)
				return;

			var raw = file.GetAsText();
			var parsed = JsonSerializer.Deserialize<Dictionary<string, PanelPos>>(raw);
			if (parsed == null)
				return;

			foreach (var (panelId, pos) in parsed)
				_positions[panelId] = pos;
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[PanelLayoutStore] load failed, fallback to empty: {ex.Message}");
			_positions.Clear();
		}
	}

	public bool TryGet(string panelId, out Vector2 position)
	{
		if (_positions.TryGetValue(panelId, out var pos))
		{
			position = new Vector2(pos.X, pos.Y);
			return true;
		}

		position = Vector2.Zero;
		return false;
	}

	public void Set(string panelId, Vector2 position)
	{
		_positions[panelId] = new PanelPos(position.X, position.Y);
	}

	public void Save()
	{
		try
		{
			var raw = JsonSerializer.Serialize(_positions, new JsonSerializerOptions
			{
				WriteIndented = true,
			});

			using var file = Godot.FileAccess.Open(_path, Godot.FileAccess.ModeFlags.Write);
			file?.StoreString(raw);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[PanelLayoutStore] save failed: {ex.Message}");
		}
	}

	private sealed class PanelPos(float x, float y)
	{
		[JsonPropertyName("x")]
		public float X { get; set; } = x;

		[JsonPropertyName("y")]
		public float Y { get; set; } = y;
	}
}
