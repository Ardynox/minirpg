#if TOOLS
using Godot;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using GodotFileAccess = Godot.FileAccess;

namespace MiniRPG.Tools;

/// <summary>
/// Godot Editor 工具：读取 Python 脚本生成的 tileset_mapping.json，
/// 在编辑器中创建 TileSet 资源并保存为 .tres 文件。
///
/// 使用方法：
///   菜单 → Project → Tools → Run → 选择此脚本（Ctrl+Shift+X）
/// </summary>
[Tool]
public partial class UnityTilesetImporter : EditorScript
{
	private const string MappingPath  = "res://Tools/tileset_mapping.json";
	private const string TileSetPath  = "res://FantasyKingdomTileSet.tres";
	private const string IdMapPath    = "res://Tools/tile_name_to_id.json";

	public override void _Run()
	{
		GD.Print("=== UnityTilesetImporter 开始 ===");

		if (!GodotFileAccess.FileExists(MappingPath))
		{
			GD.PrintErr($"找不到映射文件：{MappingPath}");
			GD.PrintErr("请先运行：python Tools/convert_unity_tileset.py");
			return;
		}

		var json    = GodotFileAccess.GetFileAsString(MappingPath);
		var mapping = JsonSerializer.Deserialize<TilesetMapping>(json);
		if (mapping == null)
		{
			GD.PrintErr("JSON 解析失败");
			return;
		}

		var tileSet = new TileSet();
		// tile_size 在等距模式下会被 TileMap 节点的 tile_size 覆盖，这里设默认值
		tileSet.TileSize = new Vector2I(128, 128);

		// tile 名称 → (sourceId, atlasCoords) 的映射，供渲染模块使用
		var nameToSource = new Dictionary<string, TileRef>();

		int sourceId = 0;

		// ── 静态 tile ───────────────────────────────────────────────────────
		GD.Print($"导入 {mapping.StaticTiles.Count} 个静态 tile...");

		foreach (var tile in mapping.StaticTiles)
		{
			var godotPath = "res://" + tile.Png;
			var tex       = ResourceLoader.Load<Texture2D>(godotPath);

			if (tex == null)
			{
				GD.PrintErr($"  [跳过] 贴图不存在：{godotPath}");
				continue;
			}

			var atlas = new TileSetAtlasSource();
			atlas.Texture           = tex;
			atlas.TextureRegionSize = new Vector2I(tile.Rect.W, tile.Rect.H);
			// 如果贴图有边距（大部分没有），在这里设置 atlas.Margins

			int id = tileSet.AddSource(atlas, sourceId);
			atlas.CreateTile(Vector2I.Zero);

			nameToSource[tile.Name] = new TileRef { SourceId = id, AtlasX = 0, AtlasY = 0 };
			sourceId++;
		}

		// ── 动画 tile ───────────────────────────────────────────────────────
		GD.Print($"导入 {mapping.AnimatedTiles.Count} 个动画 tile...");

		foreach (var tile in mapping.AnimatedTiles)
		{
			if (tile.Frames.Count == 0) continue;

			// 动画 tile：帧图片各自作为独立 TileSetAtlasSource，
			// 然后通过 TileData.SetAnimation 绑定帧序列。
			// Godot 4 中 TileSetAtlasSource 本身支持多帧动画（columns × rows × duration）。
			// 这里每一帧是独立 PNG，故采用逐帧 source 方式。

			var frameSourceIds = new List<int>();
			foreach (var frame in tile.Frames)
			{
				var godotPath = "res://" + frame.Png;
				var tex       = ResourceLoader.Load<Texture2D>(godotPath);
				if (tex == null) continue;

				var atlas = new TileSetAtlasSource();
				atlas.Texture           = tex;
				atlas.TextureRegionSize = new Vector2I(frame.Rect.W, frame.Rect.H);

				int id = tileSet.AddSource(atlas, sourceId);
				atlas.CreateTile(Vector2I.Zero);
				frameSourceIds.Add(id);
				sourceId++;
			}

			// 记录第一帧的 source ID 作为代表（动画在运行时由 AnimatedTileRenderModule 处理）
			if (frameSourceIds.Count > 0)
			{
				nameToSource[tile.Name] = new TileRef
				{
					SourceId   = frameSourceIds[0],
					AtlasX     = 0,
					AtlasY     = 0,
					IsAnimated = true,
					Fps        = tile.Fps,
					FrameSourceIds = frameSourceIds,
				};
			}
		}

		// ── 保存 TileSet ────────────────────────────────────────────────────
		var saveErr = ResourceSaver.Save(tileSet, TileSetPath);
		if (saveErr != Error.Ok)
		{
			GD.PrintErr($"TileSet 保存失败：{saveErr}");
			return;
		}
		GD.Print($"TileSet 已保存：{TileSetPath}（{sourceId} 个 source）");

		// ── 保存名称→ID 映射 ────────────────────────────────────────────────
		var idMapJson = JsonSerializer.Serialize(nameToSource, new JsonSerializerOptions { WriteIndented = true });
		using var file = GodotFileAccess.Open(IdMapPath, GodotFileAccess.ModeFlags.Write);
		if (file != null)
		{
			file.StoreString(idMapJson);
			GD.Print($"ID 映射已保存：{IdMapPath}");
		}

		GD.Print("=== 导入完成 ===");
		GD.Print("下一步：");
		GD.Print("  1. 在 Main.tscn 中添加 TileMapLayer 节点");
		GD.Print("  2. 将 FantasyKingdomTileSet.tres 赋给 TileMapLayer.tile_set");
		GD.Print("  3. 替换 RenderModule / MapRenderModule 为 TileMapRenderModule");
	}
}

// ── JSON 数据模型 ─────────────────────────────────────────────────────────────

public class TilesetMapping
{
	[JsonPropertyName("static_tiles")]
	public List<StaticTileData> StaticTiles { get; set; } = new();

	[JsonPropertyName("animated_tiles")]
	public List<AnimatedTileData> AnimatedTiles { get; set; } = new();
}

public class StaticTileData
{
	[JsonPropertyName("name")]        public string   Name          { get; set; } = "";
	[JsonPropertyName("png")]         public string   Png           { get; set; } = "";
	[JsonPropertyName("rect")]        public RectData Rect          { get; set; } = new();
	[JsonPropertyName("collider_type")] public int    ColliderType  { get; set; }
}

public class AnimatedTileData
{
	[JsonPropertyName("name")]          public string         Name         { get; set; } = "";
	[JsonPropertyName("frames")]        public List<FrameData> Frames      { get; set; } = new();
	[JsonPropertyName("fps")]           public float          Fps          { get; set; }
	[JsonPropertyName("collider_type")] public int            ColliderType { get; set; }
}

public class FrameData
{
	[JsonPropertyName("png")]  public string   Png  { get; set; } = "";
	[JsonPropertyName("rect")] public RectData Rect { get; set; } = new();
}

public class RectData
{
	[JsonPropertyName("x")] public int X { get; set; }
	[JsonPropertyName("y")] public int Y { get; set; }
	[JsonPropertyName("w")] public int W { get; set; }
	[JsonPropertyName("h")] public int H { get; set; }
}

public class TileRef
{
	[JsonPropertyName("source_id")]       public int        SourceId       { get; set; }
	[JsonPropertyName("atlas_x")]         public int        AtlasX         { get; set; }
	[JsonPropertyName("atlas_y")]         public int        AtlasY         { get; set; }
	[JsonPropertyName("is_animated")]     public bool       IsAnimated     { get; set; }
	[JsonPropertyName("fps")]             public float      Fps            { get; set; }
	[JsonPropertyName("frame_source_ids")] public List<int>? FrameSourceIds { get; set; }
}
#endif
