using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniRPG.Core;

/// <summary>
/// 存档模块：将地图四层数据 + 玩家坐标 + 巢穴列表序列化为 JSON 文件。
/// </summary>
public static class SaveModule
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	public static void SaveMap(GameState state, string filePath)
	{
		var data = new MapSaveData
		{
			Width = state.MapWidth,
			Height = state.MapHeight,
			Terrain = FlattenLayer(state.Terrain),
			Fixtures = FlattenLayer(state.Fixtures),
			Objects = FlattenLayer(state.Objects),
			Meta = FlattenMeta(state.Meta),
			PlayerX = state.PlayerX,
			PlayerY = state.PlayerY,
			Nests = state.Nests,
			Turn = state.Turn,
			RngSeed = state.RngSeed,
		};
		var json = JsonSerializer.Serialize(data, JsonOpts);
		var dir = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			Directory.CreateDirectory(dir);
		File.WriteAllText(filePath, json);
	}

	public static bool LoadMap(GameState state, string filePath)
	{
		if (!File.Exists(filePath))
			return false;

		var json = File.ReadAllText(filePath);
		var data = JsonSerializer.Deserialize<MapSaveData>(json, JsonOpts);
		if (data is null)
			return false;

		state.MapWidth = data.Width;
		state.MapHeight = data.Height;
		state.Terrain = UnflattenLayer(data.Terrain, data.Width, data.Height);
		state.Fixtures = UnflattenLayer(data.Fixtures ?? [], data.Width, data.Height);
		state.Objects = UnflattenLayer(data.Objects, data.Width, data.Height);
		state.Meta = UnflattenMeta(data.Meta, data.Width, data.Height);
		state.PlayerX = data.PlayerX;
		state.PlayerY = data.PlayerY;
		state.Nests = data.Nests ?? [];
		state.Turn = data.Turn;
		state.RngSeed = data.RngSeed;
		return true;
	}

	private static List<string> FlattenLayer(List<List<string>> layer)
	{
		var flat = new List<string>();
		foreach (var row in layer)
			flat.AddRange(row);
		return flat;
	}

	private static List<List<string>> UnflattenLayer(List<string> flat, int w, int h)
	{
		var layer = new List<List<string>>();
		for (var y = 0; y < h; y++)
		{
			var row = new List<string>();
			for (var x = 0; x < w; x++)
				row.Add(y * w + x < flat.Count ? flat[y * w + x] : "");
			layer.Add(row);
		}
		return layer;
	}

	private static List<Dictionary<string, string>?> FlattenMeta(
		List<List<Dictionary<string, string>?>> meta)
	{
		var flat = new List<Dictionary<string, string>?>();
		foreach (var row in meta)
			flat.AddRange(row);
		return flat;
	}

	private static List<List<Dictionary<string, string>?>> UnflattenMeta(
		List<Dictionary<string, string>?>? flat, int w, int h)
	{
		var meta = new List<List<Dictionary<string, string>?>>();
		for (var y = 0; y < h; y++)
		{
			var row = new List<Dictionary<string, string>?>();
			for (var x = 0; x < w; x++)
				row.Add(flat != null && y * w + x < flat.Count ? flat[y * w + x] : null);
			meta.Add(row);
		}
		return meta;
	}
}

public class MapSaveData
{
	public int Width { get; set; }
	public int Height { get; set; }
	public List<string> Terrain { get; set; } = [];
	public List<string>? Fixtures { get; set; }
	public List<string> Objects { get; set; } = [];
	public List<Dictionary<string, string>?>? Meta { get; set; }
	public int PlayerX { get; set; }
	public int PlayerY { get; set; }
	public List<NestData>? Nests { get; set; }
	public int Turn { get; set; }
	public int RngSeed { get; set; }
}
