using Godot;
using System.Collections.Generic;
using System.Text.Json;

namespace MiniRPG.Tools;

/// <summary>
/// Fantasy Kingdom Tileset → Godot TileSet 导入器。
///
/// 使用方法：
///   在 Godot 编辑器中打开 Tools/TilesetImporter.tscn，按 F6 运行当前场景。
///   完成后程序自动退出，生成：
///     FantasyKingdomTileSet.tres
///     Tools/tile_name_to_id.json
/// </summary>
public partial class UnityTilesetImporter : Node
{
	private const string SpritesDir   = "res://FantasyKingdomTileset_Godot/Environment/Sprites";
	private const string AnimDir      = "res://FantasyKingdomTileset_Godot/Animations";
	private const string TileSetPath  = "res://FantasyKingdomTileSet.tres";
	private const string IdMapPath    = "res://Tools/tile_name_to_id.json";

	public override void _Ready()
	{
		GD.Print("=== TilesetImporter 开始 ===");

		var tileSet      = new TileSet();
		tileSet.TileSize = new Vector2I(128, 128);

		var nameToSource = new Dictionary<string, TileRef>();
		int sourceId     = 0;
		int skipped      = 0;

		// ── 静态 tile：每个 PNG 文件 = 一个 tile ─────────────────────────────
		GD.Print($"扫描静态 tile：{SpritesDir}");

		var dir = DirAccess.Open(SpritesDir);
		if (dir == null)
		{
			GD.PrintErr($"无法打开目录：{SpritesDir}");
			GetTree().Quit(1);
			return;
		}

		dir.ListDirBegin();
		var files = new List<string>();
		string entry;
		while ((entry = dir.GetNext()) != "")
		{
			if (!dir.CurrentIsDir() && entry.EndsWith(".png"))
				files.Add(entry);
		}
		dir.ListDirEnd();
		files.Sort();

		GD.Print($"  找到 {files.Count} 个 PNG");

		foreach (var file in files)
		{
			var resPath = $"{SpritesDir}/{file}";
			var tex     = ResourceLoader.Load<Texture2D>(resPath);
			if (tex == null)
			{
				skipped++;
				continue;
			}

			var atlas               = new TileSetAtlasSource();
			atlas.Texture           = tex;
			atlas.TextureRegionSize = new Vector2I(tex.GetWidth(), tex.GetHeight());

			int id = tileSet.AddSource(atlas, sourceId);
			atlas.CreateTile(Vector2I.Zero);

			var name = file[..^4]; // strip ".png"
			nameToSource[name] = new TileRef { SourceId = id };
			sourceId++;
		}

		GD.Print($"  完成：{sourceId} 个，跳过：{skipped} 个");

		// ── 动画 tile：每个子文件夹 = 一个动画 tile ───────────────────────────
		GD.Print($"扫描动画 tile：{AnimDir}");
		int animCount = 0;
		ScanAnimDir($"{AnimDir}/Props",           tileSet, nameToSource, ref sourceId, ref animCount);
		ScanAnimDir($"{AnimDir}/Animated Tiles",  tileSet, nameToSource, ref sourceId, ref animCount);
		GD.Print($"  完成：{animCount} 个动画 tile");

		// ── 保存 TileSet ──────────────────────────────────────────────────────
		var saveErr = ResourceSaver.Save(tileSet, TileSetPath);
		if (saveErr != Error.Ok)
		{
			GD.PrintErr($"TileSet 保存失败：{saveErr}");
			GetTree().Quit(1);
			return;
		}
		GD.Print($"TileSet 已保存：{TileSetPath}（共 {sourceId} 个 source）");

		// ── 保存名称 → ID 映射 ────────────────────────────────────────────────
		var idMapJson = JsonSerializer.Serialize(nameToSource, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
		using var f   = Godot.FileAccess.Open(IdMapPath, Godot.FileAccess.ModeFlags.Write);
		if (f != null)
		{
			f.StoreString(idMapJson);
			GD.Print($"ID 映射已保存：{IdMapPath}");
		}

		GD.Print("=== 导入完成，程序退出 ===");
		GetTree().Quit();
	}

	private static void ScanAnimDir(string baseDir, TileSet tileSet,
		Dictionary<string, TileRef> nameToSource, ref int sourceId, ref int animCount)
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

				var atlas               = new TileSetAtlasSource();
				atlas.Texture           = tex;
				atlas.TextureRegionSize = new Vector2I(tex.GetWidth(), tex.GetHeight());

				int id = tileSet.AddSource(atlas, sourceId);
				atlas.CreateTile(Vector2I.Zero);
				frameSourceIds.Add(id);
				sourceId++;
			}

			if (frameSourceIds.Count > 0)
			{
				nameToSource[sub] = new TileRef
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
}

// ── 数据模型 ──────────────────────────────────────────────────────────────────

public class TileRef
{
	[System.Text.Json.Serialization.JsonPropertyName("source_id")]        public int        SourceId       { get; set; }
	[System.Text.Json.Serialization.JsonPropertyName("is_animated")]      public bool       IsAnimated     { get; set; }
	[System.Text.Json.Serialization.JsonPropertyName("fps")]              public float      Fps            { get; set; }
	[System.Text.Json.Serialization.JsonPropertyName("frame_source_ids")] public List<int>? FrameSourceIds { get; set; }
}
