using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace MiniRPG.Tools;

/// <summary>
/// Fantasy Kingdom Tileset → Godot TileSet 导入器（Atlas 合图版）。
///
/// 核心改进：将散装 PNG 按尺寸分组，拼合成若干大纹理（atlas），
/// 同一 atlas 内的所有 tile 共享一个 Texture2D → GPU 可以 batch 渲染。
///
/// 使用方法：
///   在 Godot 编辑器中打开 Tools/TilesetImporter.tscn，按 F6 运行当前场景。
///   完成后程序自动退出，生成：
///     FantasyKingdomTileSet.tres
///     Tools/tile_name_to_id.json
/// </summary>
public partial class UnityTilesetImporter : Node
{
	private const string SpritesDir  = "res://FantasyKingdomTileset_Godot/Environment/Sprites";
	private const string AnimDir     = "res://FantasyKingdomTileset_Godot/Animations";
	private const string TileSetPath = "res://FantasyKingdomTileSet.tres";
	private const string IdMapPath   = "res://Tools/tile_name_to_id.json";

	/// <summary>单张 atlas 纹理的最大边长。4096 是安全上限，适合绝大多数 GPU。</summary>
	private const int MaxAtlasSize = 4096;

	public override void _Ready()
	{
		GD.Print("=== TilesetImporter (Atlas) 开始 ===");

		var tileSet = new TileSet();
		tileSet.TileSize = new Vector2I(128, 128);

		var nameToRef = new Dictionary<string, TileRef>();
		int nextSourceId = 0;

		// ── 收集所有静态 tile 的 Image + 原始尺寸 ────────────────────────
		var entries = CollectStaticTiles(SpritesDir);
		GD.Print($"  静态 tile：{entries.Count} 个");

		// ── 按像素尺寸分组 ──────────────────────────────────────────────
		var groups = entries
			.GroupBy(e => (e.Width, e.Height))
			.OrderByDescending(g => g.Count())
			.ToList();

		GD.Print($"  尺寸分组：{groups.Count} 种");
		foreach (var g in groups)
			GD.Print($"    {g.Key.Width}×{g.Key.Height} → {g.Count()} 个 tile");

		// ── 每组拼合成 atlas ──────────────────────────────────────────────
		foreach (var group in groups)
		{
			var tileW = group.Key.Width;
			var tileH = group.Key.Height;
			var tiles = group.ToList();

			var cols = MaxAtlasSize / tileW;
			if (cols < 1) cols = 1;
			var rows = MaxAtlasSize / tileH;
			if (rows < 1) rows = 1;
			int tilesPerAtlas = cols * rows;

			for (int batch = 0; batch < tiles.Count; batch += tilesPerAtlas)
			{
				var chunk = tiles.Skip(batch).Take(tilesPerAtlas).ToList();

				int usedCols = System.Math.Min(cols, chunk.Count);
				int usedRows = (chunk.Count + usedCols - 1) / usedCols;
				int atlasW = usedCols * tileW;
				int atlasH = usedRows * tileH;

				var atlasImg = Image.CreateEmpty(atlasW, atlasH, false, Image.Format.Rgba8);

				for (int i = 0; i < chunk.Count; i++)
				{
					int col = i % usedCols;
					int row = i / usedCols;
					var dst = new Vector2I(col * tileW, row * tileH);
					atlasImg.BlitRect(chunk[i].Img, new Rect2I(0, 0, tileW, tileH), dst);
				}

				var atlasTex = ImageTexture.CreateFromImage(atlasImg);

				var source = new TileSetAtlasSource();
				source.Texture = atlasTex;
				source.TextureRegionSize = new Vector2I(tileW, tileH);

				int sid = tileSet.AddSource(source, nextSourceId);
				nextSourceId++;

				for (int i = 0; i < chunk.Count; i++)
				{
					int col = i % usedCols;
					int row = i / usedCols;
					var coord = new Vector2I(col, row);

					source.CreateTile(coord);

					nameToRef[chunk[i].Name] = new TileRef
					{
						SourceId = sid,
						AtlasX   = col,
						AtlasY   = row,
					};
				}

				GD.Print($"    Atlas source {sid}: {tileW}×{tileH}, {chunk.Count} tiles, " +
					$"texture {atlasW}×{atlasH}");
			}
		}

		// ── 动画 tile（保持散装 source，数量很少不影响 batching）─────────
		GD.Print($"扫描动画 tile：{AnimDir}");
		int animCount = 0;
		ScanAnimDir($"{AnimDir}/Props",          tileSet, nameToRef, ref nextSourceId, ref animCount);
		ScanAnimDir($"{AnimDir}/Animated Tiles", tileSet, nameToRef, ref nextSourceId, ref animCount);
		GD.Print($"  完成：{animCount} 个动画 tile");

		// ── 保存 TileSet ────────────────────────────────────────────────
		var saveErr = ResourceSaver.Save(tileSet, TileSetPath);
		if (saveErr != Error.Ok)
		{
			GD.PrintErr($"TileSet 保存失败：{saveErr}");
			GetTree().Quit(1);
			return;
		}
		GD.Print($"TileSet 已保存：{TileSetPath}（共 {nextSourceId} 个 source）");

		// ── 保存名称 → ID+坐标 映射 ──────────────────────────────────────
		var opts = new JsonSerializerOptions { WriteIndented = true };
		var json = JsonSerializer.Serialize(nameToRef, opts);
		using var f = Godot.FileAccess.Open(IdMapPath, Godot.FileAccess.ModeFlags.Write);
		if (f != null)
		{
			f.StoreString(json);
			GD.Print($"ID 映射已保存：{IdMapPath}");
		}

		GD.Print("=== 导入完成，程序退出 ===");
		GetTree().Quit();
	}

	// ── 收集静态 tile ───────────────────────────────────────────────────

	private static List<TileEntry> CollectStaticTiles(string dir)
	{
		var result = new List<TileEntry>();
		var d = DirAccess.Open(dir);
		if (d == null)
		{
			GD.PrintErr($"无法打开目录：{dir}");
			return result;
		}

		d.ListDirBegin();
		var files = new List<string>();
		string entry;
		while ((entry = d.GetNext()) != "")
		{
			if (!d.CurrentIsDir() && entry.EndsWith(".png"))
				files.Add(entry);
		}
		d.ListDirEnd();
		files.Sort();

		foreach (var file in files)
		{
			var img = Image.LoadFromFile(ProjectSettings.GlobalizePath($"{dir}/{file}"));
			if (img == null) continue;

			if (img.GetFormat() != Image.Format.Rgba8)
				img.Convert(Image.Format.Rgba8);

			result.Add(new TileEntry
			{
				Name   = file[..^4],
				Img    = img,
				Width  = img.GetWidth(),
				Height = img.GetHeight(),
			});
		}
		return result;
	}

	// ── 动画 tile（保持散装，数量少）────────────────────────────────────

	private static void ScanAnimDir(string baseDir, TileSet tileSet,
		Dictionary<string, TileRef> nameToRef, ref int nextSourceId, ref int animCount)
	{
		var dir = DirAccess.Open(baseDir);
		if (dir == null) return;

		dir.ListDirBegin();
		var subDirs = new List<string>();
		string entry;
		while ((entry = dir.GetNext()) != "")
		{
			if (dir.CurrentIsDir() && entry != "." && entry != "..")
				subDirs.Add(entry);
		}
		dir.ListDirEnd();
		subDirs.Sort();

		foreach (var sub in subDirs)
		{
			var frameDir = DirAccess.Open($"{baseDir}/{sub}");
			if (frameDir == null) continue;

			frameDir.ListDirBegin();
			var frames = new List<string>();
			string frame;
			while ((frame = frameDir.GetNext()) != "")
			{
				if (!frameDir.CurrentIsDir() && frame.EndsWith(".png"))
					frames.Add(frame);
			}
			frameDir.ListDirEnd();
			frames.Sort();

			if (frames.Count == 0) continue;

			var frameSourceIds = new List<int>();
			foreach (var frameFile in frames)
			{
				var tex = ResourceLoader.Load<Texture2D>($"{baseDir}/{sub}/{frameFile}");
				if (tex == null) continue;

				var atlas = new TileSetAtlasSource();
				atlas.Texture           = tex;
				atlas.TextureRegionSize = new Vector2I(tex.GetWidth(), tex.GetHeight());

				int id = tileSet.AddSource(atlas, nextSourceId);
				atlas.CreateTile(Vector2I.Zero);
				frameSourceIds.Add(id);
				nextSourceId++;
			}

			if (frameSourceIds.Count > 0)
			{
				nameToRef[sub] = new TileRef
				{
					SourceId       = frameSourceIds[0],
					IsAnimated     = true,
					Fps            = 10f,
					FrameSourceIds = frameSourceIds,
				};
				animCount++;
			}
		}
	}

	// ── 数据类 ──────────────────────────────────────────────────────────

	private class TileEntry
	{
		public string Name   = "";
		public Image  Img    = null!;
		public int    Width;
		public int    Height;
	}
}

// ── JSON 数据模型 ──────────────────────────────────────────────────────────

public class TileRef
{
	[System.Text.Json.Serialization.JsonPropertyName("source_id")]
	public int SourceId { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("atlas_x")]
	public int AtlasX { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("atlas_y")]
	public int AtlasY { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("is_animated")]
	public bool IsAnimated { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("fps")]
	public float Fps { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("frame_source_ids")]
	public List<int>? FrameSourceIds { get; set; }
}
