using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace MiniRPG.Module.Panel;

public readonly record struct PanelAppearance(float? Width, float? Height, float? ButtonScale);

public sealed class PanelLayoutStore(string path = "user://panel_layout.json")
{
	private readonly string _path = path;
	private readonly Dictionary<string, PanelLayoutEntry> _entries = [];

	public void Load()
	{
		_entries.Clear();
		if (!Godot.FileAccess.FileExists(_path))
			return;

		try
		{
			using var file = Godot.FileAccess.Open(_path, Godot.FileAccess.ModeFlags.Read);
			if (file == null)
				return;

			if (!LoadFromRawJson(file.GetAsText()))
				GD.PushWarning("[PanelLayoutStore] load failed, fallback to empty.");
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[PanelLayoutStore] load failed, fallback to empty: {ex.Message}");
			_entries.Clear();
		}
	}

	public bool TryGetPosition(string panelId, out Vector2 position)
	{
		if (_entries.TryGetValue(panelId, out var entry)
			&& entry.X.HasValue
			&& entry.Y.HasValue)
		{
			position = new Vector2(entry.X.Value, entry.Y.Value);
			return true;
		}

		position = Vector2.Zero;
		return false;
	}

	public bool TryGetAppearance(string panelId, out PanelAppearance appearance)
	{
		if (_entries.TryGetValue(panelId, out var entry))
		{
			appearance = new PanelAppearance(
				entry.Width,
				entry.Height,
				entry.ButtonScale);
			return true;
		}

		appearance = new PanelAppearance(null, null, null);
		return false;
	}

	public void SetPosition(string panelId, Vector2 position)
	{
		var entry = GetOrCreate(panelId);
		entry.X = position.X;
		entry.Y = position.Y;
	}

	public void RemovePosition(string panelId)
	{
		if (!_entries.TryGetValue(panelId, out var entry))
			return;

		entry.X = null;
		entry.Y = null;
		Cleanup(panelId, entry);
	}

	public void SetWidth(string panelId, float width)
	{
		if (width <= 0f)
		{
			if (!_entries.TryGetValue(panelId, out var entry))
				return;

			entry.Width = null;
			Cleanup(panelId, entry);
			return;
		}

		GetOrCreate(panelId).Width = width;
	}

	public void SetHeight(string panelId, float height)
	{
		if (height <= 0f)
		{
			if (!_entries.TryGetValue(panelId, out var entry))
				return;

			entry.Height = null;
			Cleanup(panelId, entry);
			return;
		}

		GetOrCreate(panelId).Height = height;
	}

	public void SetButtonScale(string panelId, float buttonScale)
	{
		GetOrCreate(panelId).ButtonScale = buttonScale;
	}

	public void RemoveAppearance(string panelId)
	{
		if (!_entries.TryGetValue(panelId, out var entry))
			return;

		entry.Width = null;
		entry.Height = null;
		entry.ButtonScale = null;
		Cleanup(panelId, entry);
	}

	public void Remove(string panelId)
	{
		_entries.Remove(panelId);
	}

	public void Save()
	{
		try
		{
			var raw = SerializeEntries(_entries);
			using var file = Godot.FileAccess.Open(_path, Godot.FileAccess.ModeFlags.Write);
			file?.StoreString(raw);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[PanelLayoutStore] save failed: {ex.Message}");
		}
	}

	private PanelLayoutEntry GetOrCreate(string panelId)
	{
		if (_entries.TryGetValue(panelId, out var entry))
			return entry;

		entry = new PanelLayoutEntry();
		_entries[panelId] = entry;
		return entry;
	}

	private void Cleanup(string panelId, PanelLayoutEntry entry)
	{
		if (entry.X.HasValue
			|| entry.Y.HasValue
			|| entry.Width.HasValue
			|| entry.Height.HasValue
			|| entry.ButtonScale.HasValue)
			return;

		_entries.Remove(panelId);
	}

	private bool LoadFromRawJson(string? raw)
	{
		_entries.Clear();
		if (string.IsNullOrWhiteSpace(raw))
			return true;

		if (!TryDeserializeEntries(raw, out var parsed))
			return false;

		foreach (var (panelId, entry) in parsed)
			_entries[panelId] = entry;

		return true;
	}

	private static bool TryDeserializeEntries(string raw, out Dictionary<string, PanelLayoutEntry> parsed)
	{
		parsed = [];
		try
		{
			parsed = JsonSerializer.Deserialize<Dictionary<string, PanelLayoutEntry>>(raw) ?? [];
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static string SerializeEntries(Dictionary<string, PanelLayoutEntry> entries) =>
		JsonSerializer.Serialize(entries, new JsonSerializerOptions
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
			WriteIndented = true,
		});

	private sealed class PanelLayoutEntry
	{
		[JsonPropertyName("x")]
		public float? X { get; set; }

		[JsonPropertyName("y")]
		public float? Y { get; set; }

		[JsonPropertyName("width")]
		public float? Width { get; set; }

		[JsonPropertyName("height")]
		public float? Height { get; set; }

		[JsonPropertyName("button_scale")]
		public float? ButtonScale { get; set; }
	}
}
