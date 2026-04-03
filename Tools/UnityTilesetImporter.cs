using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace MiniRPG.Tools;

/// <summary>
/// Fantasy Kingdom Tileset → Godot TileSet 导入器（Atlas 合图版）。
///
/// 重活（收集 PNG、拼合 Atlas Image）在后台线程执行，
/// 主线程显示实时进度条 + 阶段文字，不会卡死编辑器。
///
/// 必须在主线程执行的 Godot API（TileSet 组装、ResourceSaver）
/// 在后台阶段完成后回到主线程的 _Process 中执行。
///
/// 使用方法：
///   在 Godot 编辑器中打开 Tools/TilesetImporter.tscn，按 F6 运行当前场景。
/// </summary>
public partial class UnityTilesetImporter : Node
{
	private const string SpritesDir  = "res://FantasyKingdomTileset_Godot/Environment/Sprites";
	private const string AnimDir     = "res://FantasyKingdomTileset_Godot/Animations";
	private const string TileSetPath = "res://FantasyKingdomTileSet.tres";
	private const string IdMapPath   = "res://Tools/tile_name_to_id.json";
	private const int    MaxAtlasSize = 4096;

	// ── 进度 UI 节点 ─────────────────────────────────────
	private ProgressBar _bar = null!;
	private Label _label = null!;

	// ── 线程间通信（volatile 保证主线程可见性）──────────
	private volatile string _status = "";
	private volatile float  _progress;
	private volatile bool   _bgDone;
	private volatile string? _bgError;

	// ── 后台阶段产出（主线程消费）──────────────────────
	private List<AtlasResult>? _atlasResults;
	private Dictionary<string, TileRef>? _nameToRef;
	private int _totalStatic;
	private int _phase; // 0=后台跑, 1=主线程组装, 2=完成

	// ═══════════════════════════════════════════════════
	//  Godot 生命周期
	// ═══════════════════════════════════════════════════

	public override void _Ready()
	{
		BuildUI();
		_status = "正在启动…";
		_progress = 0;
		_phase = 0;

		var thread = new Thread(BackgroundWork) { IsBackground = true };
		thread.Start();
	}

	public override void _Process(double delta)
	{
		_bar.Value = _progress * 100;
		_label.Text = _status;

		switch (_phase)
		{
			case 0 when _bgDone:
				if (_bgError != null)
				{
					_status = $"错误：{_bgError}";
					_bar.Modulate = Colors.Red;
					_phase = 2;
					return;
				}
				_phase = 1;
				CallDeferred(MethodName.MainThreadAssemble);
				break;
			case 2:
				break;
		}
	}

	// ═══════════════════════════════════════════════════
	//  UI 构建
	// ═══════════════════════════════════════════════════

	private void BuildUI()
	{
		var root = new Control();
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);

		var bg = new ColorRect();
		bg.Color = new Color(0.12f, 0.12f, 0.15f);
		bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		root.AddChild(bg);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsPreset(Control.LayoutPreset.Center);
		vbox.GrowHorizontal = Control.GrowDirection.Both;
		vbox.GrowVertical = Control.GrowDirection.Both;
		vbox.CustomMinimumSize = new Vector2(600, 0);
		vbox.AddThemeConstantOverride("separation", 16);
		root.AddChild(vbox);

		var title = new Label();
		title.Text = "TileSet Atlas 导入器";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontSizeOverride("font_size", 28);
		vbox.AddChild(title);

		_label = new Label();
		_label.Text = "准备中…";
		_label.HorizontalAlignment = HorizontalAlignment.Center;
		_label.AddThemeFontSizeOverride("font_size", 18);
		vbox.AddChild(_label);

		_bar = new ProgressBar();
		_bar.MinValue = 0;
		_bar.MaxValue = 100;
		_bar.Value = 0;
		_bar.CustomMinimumSize = new Vector2(600, 32);
		_bar.ShowPercentage = true;
		vbox.AddChild(_bar);

		AddChild(root);
	}

	// ═══════════════════════════════════════════════════
	//  后台线程：收集 PNG + 拼合 Atlas Image
	// ═══════════════════════════════════════════════════

	private void BackgroundWork()
	{
		try
		{
			// ── 阶段 1：收集 PNG ──────────────────────────────
			_status = "扫描 PNG 文件…";
			_progress = 0;

			var files = ListPngFiles(SpritesDir);
			_totalStatic = files.Count;
			_status = $"加载图片：0 / {_totalStatic}";

			var entries = new List<TileEntry>();
			for (int i = 0; i < files.Count; i++)
			{
				var file = files[i];
				var img = Image.LoadFromFile(ProjectSettings.GlobalizePath($"{SpritesDir}/{file}"));
				if (img == null) continue;

				if (img.GetFormat() != Image.Format.Rgba8)
					img.Convert(Image.Format.Rgba8);

				entries.Add(new TileEntry
				{
					Name   = file[..^4],
					Img    = img,
					Width  = img.GetWidth(),
					Height = img.GetHeight(),
				});

				_progress = (float)(i + 1) / files.Count * 0.3f;
				_status = $"加载图片：{i + 1} / {_totalStatic}";
			}

			// ── 阶段 2：按尺寸分组 + 拼合 Atlas Image ────────
			_status = "按尺寸分组…";
			var groups = entries
				.GroupBy(e => (e.Width, e.Height))
				.OrderByDescending(g => g.Count())
				.ToList();

			var results = new List<AtlasResult>();
			var nameToRef = new Dictionary<string, TileRef>();
			int nextSourceId = 0;
			int totalToBlit = entries.Count;
			int blitted = 0;

			foreach (var group in groups)
			{
				var tileW = group.Key.Width;
				var tileH = group.Key.Height;
				var tiles = group.ToList();

				var cols = Math.Max(1, MaxAtlasSize / tileW);
				var rows = Math.Max(1, MaxAtlasSize / tileH);
				int tilesPerAtlas = cols * rows;

				for (int batch = 0; batch < tiles.Count; batch += tilesPerAtlas)
				{
					var chunk = tiles.Skip(batch).Take(tilesPerAtlas).ToList();

					int usedCols = Math.Min(cols, chunk.Count);
					int usedRows = (chunk.Count + usedCols - 1) / usedCols;
					int atlasW = usedCols * tileW;
					int atlasH = usedRows * tileH;

					var atlasImg = Image.CreateEmpty(atlasW, atlasH, false, Image.Format.Rgba8);

					for (int i = 0; i < chunk.Count; i++)
					{
						int c = i % usedCols;
						int r = i / usedCols;
						atlasImg.BlitRect(chunk[i].Img, new Rect2I(0, 0, tileW, tileH),
							new Vector2I(c * tileW, r * tileH));

						blitted++;
						_progress = 0.3f + (float)blitted / totalToBlit * 0.5f;
						_status = $"拼合 Atlas：{blitted} / {totalToBlit}（{tileW}×{tileH}）";
					}

					int sid = nextSourceId++;

					var tileCoords = new List<(string Name, int Col, int Row)>();
					for (int i = 0; i < chunk.Count; i++)
					{
						int c = i % usedCols;
						int r = i / usedCols;
						tileCoords.Add((chunk[i].Name, c, r));

						nameToRef[chunk[i].Name] = new TileRef
						{
							SourceId = sid,
							AtlasX   = c,
							AtlasY   = r,
						};
					}

					results.Add(new AtlasResult
					{
						SourceId  = sid,
						TileW     = tileW,
						TileH     = tileH,
						AtlasImg  = atlasImg,
						Tiles     = tileCoords,
					});
				}
			}

			_atlasResults = results;
			_nameToRef = nameToRef;

			_status = "图片拼合完成，等待主线程组装 TileSet…";
			_progress = 0.8f;
		}
		catch (Exception ex)
		{
			_bgError = ex.Message;
			GD.PrintErr($"[TilesetImporter] 后台线程异常：{ex}");
		}
		finally
		{
			_bgDone = true;
		}
	}

	/// <summary>后台线程安全的文件列表（DirAccess 可在非主线程使用）。</summary>
	private static List<string> ListPngFiles(string dir)
	{
		var files = new List<string>();
		var d = DirAccess.Open(dir);
		if (d == null) return files;

		d.ListDirBegin();
		string entry;
		while ((entry = d.GetNext()) != "")
		{
			if (!d.CurrentIsDir() && entry.EndsWith(".png"))
				files.Add(entry);
		}
		d.ListDirEnd();
		files.Sort();
		return files;
	}

	// ═══════════════════════════════════════════════════
	//  主线程：组装 TileSet + 保存（必须在主线程）
	// ═══════════════════════════════════════════════════

	private void MainThreadAssemble()
	{
		if (_atlasResults == null || _nameToRef == null) return;

		_status = "组装 TileSet…";
		_progress = 0.82f;

		var tileSet = new TileSet();
		tileSet.TileSize = new Vector2I(128, 128);

		int total = _atlasResults.Count;
		for (int ai = 0; ai < total; ai++)
		{
			var ar = _atlasResults[ai];

			var atlasTex = ImageTexture.CreateFromImage(ar.AtlasImg);
			var source = new TileSetAtlasSource();
			source.Texture = atlasTex;
			source.TextureRegionSize = new Vector2I(ar.TileW, ar.TileH);

			tileSet.AddSource(source, ar.SourceId);

			foreach (var (_, col, row) in ar.Tiles)
				source.CreateTile(new Vector2I(col, row));

			_progress = 0.82f + (float)(ai + 1) / total * 0.08f;
			_status = $"组装 TileSet：source {ai + 1} / {total}";
		}

		// ── 动画 tile ────────────────────────────────────────
		_status = "扫描动画 tile…";
		int nextSourceId = _atlasResults.Count > 0
			? _atlasResults.Max(a => a.SourceId) + 1 : 0;
		int animCount = 0;
		ScanAnimDir($"{AnimDir}/Props",          tileSet, _nameToRef, ref nextSourceId, ref animCount);
		ScanAnimDir($"{AnimDir}/Animated Tiles", tileSet, _nameToRef, ref nextSourceId, ref animCount);
		_progress = 0.92f;

		// ── 保存 TileSet ─────────────────────────────────────
		_status = "保存 TileSet…";
		var saveErr = ResourceSaver.Save(tileSet, TileSetPath);
		if (saveErr != Error.Ok)
		{
			_status = $"TileSet 保存失败：{saveErr}";
			_bar.Modulate = Colors.Red;
			_phase = 2;
			return;
		}
		_progress = 0.96f;

		// ── 保存 JSON 映射 ───────────────────────────────────
		_status = "保存 ID 映射…";
		var opts = new JsonSerializerOptions { WriteIndented = true };
		var json = JsonSerializer.Serialize(_nameToRef, opts);
		using var f = Godot.FileAccess.Open(IdMapPath, Godot.FileAccess.ModeFlags.Write);
		if (f != null)
			f.StoreString(json);

		_progress = 1.0f;
		_status = $"完成！{_totalStatic} 个静态 tile → {_atlasResults.Count} 张 atlas，" +
			$"{animCount} 个动画 tile。3 秒后退出…";
		_bar.Modulate = Colors.Green;
		_phase = 2;

		GD.Print($"=== 导入完成：{_atlasResults.Count} atlas sources, {animCount} 动画 ===");

		GetTree().CreateTimer(3.0).Timeout += () => GetTree().Quit();
	}

	// ═══════════════════════════════════════════════════
	//  动画 tile（保持散装 source，数量少）
	// ═══════════════════════════════════════════════════

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

	// ═══════════════════════════════════════════════════
	//  内部数据类型
	// ═══════════════════════════════════════════════════

	private class TileEntry
	{
		public string Name   = "";
		public Image  Img    = null!;
		public int    Width;
		public int    Height;
	}

	private class AtlasResult
	{
		public int   SourceId;
		public int   TileW;
		public int   TileH;
		public Image AtlasImg = null!;
		public List<(string Name, int Col, int Row)> Tiles = [];
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
