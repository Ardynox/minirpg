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
/// 架构：
///   后台线程 — 收集 PNG、拼合 Atlas Image（CPU 密集）
///   主线程 _Process — 状态机分帧执行（每帧处理 1 个 atlas source），
///     包括 ImageTexture 上传、TileSet 组装、保存。
///     每步之间让出控制权，保证 UI 不卡死。
///
/// 使用方法：
///   在 Godot 编辑器中打开 Tools/TilesetImporter.tscn，按 F6 运行当前场景。
/// </summary>
public partial class UnityTilesetImporter : Node
{
	private const string SpritesDir   = "res://FantasyKingdomTileset_Godot/Environment/Sprites";
	private const string AnimDir      = "res://FantasyKingdomTileset_Godot/Animations";
	private const string TileSetPath  = "res://FantasyKingdomTileSet.tres";
	private const string IdMapPath    = "res://Tools/tile_name_to_id.json";
	private const int    MaxAtlasSize = 4096;

	/// <summary>
	/// 单张 atlas 最多容纳的 tile 数。控制主线程每帧 CreateFromImage 的数据量，
	/// 64 tiles × 256×256 = 2048×2048 ≈ 16MB RGBA8，上传耗时可控。
	/// </summary>
	private const int    MaxTilesPerAtlas = 64;

	// ── UI ───────────────────────────────────────────────
	private ProgressBar _bar = null!;
	private Label _label = null!;

	// ── 线程间通信 ──────────────────────────────────────
	private volatile string _status = "";
	private volatile float  _progress;
	private volatile bool   _bgDone;
	private volatile string? _bgError;

	// ── 后台产出 ────────────────────────────────────────
	private List<AtlasResult>? _atlasResults;
	private Dictionary<string, TileRef>? _nameToRef;
	private int _totalStatic;

	// ── 主线程状态机 ────────────────────────────────────
	private enum Phase { BgRunning, AssembleTileSet, AnimScan, SaveTileSet, SaveJson, Done }
	private Phase _phase = Phase.BgRunning;

	// 分帧组装用
	private TileSet? _tileSet;
	private int _assembleIdx;
	private int _nextSourceId;
	private double _doneTimer;

	// ═══════════════════════════════════════════════════
	//  生命周期
	// ═══════════════════════════════════════════════════

	public override void _Ready()
	{
		BuildUI();
		_status = "正在启动…";
		new Thread(BackgroundWork) { IsBackground = true }.Start();
	}

	public override void _Process(double delta)
	{
		_bar.Value = _progress * 100;
		_label.Text = _status;

		switch (_phase)
		{
			case Phase.BgRunning:
				if (!_bgDone) return;
				if (_bgError != null) { ShowError(_bgError); return; }
				// 准备分帧组装
				_tileSet = new TileSet { TileSize = new Vector2I(128, 128) };
				_assembleIdx = 0;
				_phase = Phase.AssembleTileSet;
				break;

			case Phase.AssembleTileSet:
				StepAssembleOneSource();
				break;

			case Phase.AnimScan:
				StepAnimScan();
				break;

			case Phase.SaveTileSet:
				StepSaveTileSet();
				break;

			case Phase.SaveJson:
				StepSaveJson();
				break;

			case Phase.Done:
				_doneTimer += delta;
				if (_doneTimer >= 3.0)
					GetTree().Quit();
				break;
		}
	}

	// ═══════════════════════════════════════════════════
	//  分帧步骤
	// ═══════════════════════════════════════════════════

	/// <summary>每帧处理 1 个 atlas source：CreateFromImage + AddSource + CreateTile。</summary>
	private void StepAssembleOneSource()
	{
		var results = _atlasResults!;
		if (_assembleIdx >= results.Count)
		{
			_nextSourceId = results.Count > 0 ? results.Max(a => a.SourceId) + 1 : 0;
			_phase = Phase.AnimScan;
			return;
		}

		var ar = results[_assembleIdx];
		int texW = ar.AtlasImg?.GetWidth() ?? 0;
		int texH = ar.AtlasImg?.GetHeight() ?? 0;
		int mb = texW * texH * 4 / (1024 * 1024);
		_status = $"上传纹理：source {_assembleIdx + 1} / {results.Count}" +
			$"（{ar.Tiles.Count} tiles, {texW}×{texH}, ~{mb}MB）";

		var atlasTex = ImageTexture.CreateFromImage(ar.AtlasImg);
		var source = new TileSetAtlasSource();
		source.Texture = atlasTex;
		source.TextureRegionSize = new Vector2I(ar.TileW, ar.TileH);
		_tileSet!.AddSource(source, ar.SourceId);

		foreach (var (_, col, row) in ar.Tiles)
			source.CreateTile(new Vector2I(col, row));

		// 释放后台持有的大 Image，减少内存
		ar.AtlasImg = null!;

		_assembleIdx++;
		_progress = 0.80f + (float)_assembleIdx / results.Count * 0.10f;
	}

	private void StepAnimScan()
	{
		_status = "扫描动画 tile…";
		int animCount = 0;
		ScanAnimDir($"{AnimDir}/Props",          _tileSet!, _nameToRef!, ref _nextSourceId, ref animCount);
		ScanAnimDir($"{AnimDir}/Animated Tiles", _tileSet!, _nameToRef!, ref _nextSourceId, ref animCount);
		_progress = 0.92f;
		_status = $"动画 tile：{animCount} 个";
		_phase = Phase.SaveTileSet;
	}

	private void StepSaveTileSet()
	{
		_status = "保存 TileSet（可能需要几秒）…";
		_progress = 0.94f;

		var err = ResourceSaver.Save(_tileSet!, TileSetPath);
		if (err != Error.Ok) { ShowError($"TileSet 保存失败：{err}"); return; }

		_progress = 0.97f;
		_phase = Phase.SaveJson;
	}

	private void StepSaveJson()
	{
		_status = "保存 ID 映射…";
		var opts = new JsonSerializerOptions { WriteIndented = true };
		var json = JsonSerializer.Serialize(_nameToRef, opts);
		using var f = Godot.FileAccess.Open(IdMapPath, Godot.FileAccess.ModeFlags.Write);
		f?.StoreString(json);

		int atlasCount = _atlasResults?.Count ?? 0;
		_progress = 1.0f;
		_status = $"完成！{_totalStatic} 静态 tile → {atlasCount} 张 atlas。3 秒后退出…";
		_bar.Modulate = Colors.Green;
		_phase = Phase.Done;
		_doneTimer = 0;

		GD.Print($"=== 导入完成：{atlasCount} atlas sources ===");
	}

	private void ShowError(string msg)
	{
		_status = $"错误：{msg}";
		_bar.Modulate = Colors.Red;
		_phase = Phase.Done;
		_doneTimer = -9999; // 不自动退出
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
			_status = "扫描 PNG 文件…";
			var files = ListPngFiles(SpritesDir);
			_totalStatic = files.Count;

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

			_status = "按尺寸分组拼合…";
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

				var maxCols = Math.Max(1, MaxAtlasSize / tileW);
				var maxRows = Math.Max(1, MaxAtlasSize / tileH);
				int tilesPerAtlas = Math.Min(maxCols * maxRows, MaxTilesPerAtlas);

				for (int batch = 0; batch < tiles.Count; batch += tilesPerAtlas)
				{
					var chunk = tiles.Skip(batch).Take(tilesPerAtlas).ToList();
					int usedCols = Math.Min(maxCols, chunk.Count);
					int usedRows = (chunk.Count + usedCols - 1) / usedCols;

					var atlasImg = Image.CreateEmpty(
						usedCols * tileW, usedRows * tileH, false, Image.Format.Rgba8);

					for (int i = 0; i < chunk.Count; i++)
					{
						int c = i % usedCols;
						int r = i / usedCols;
						atlasImg.BlitRect(chunk[i].Img,
							new Rect2I(0, 0, tileW, tileH),
							new Vector2I(c * tileW, r * tileH));

						blitted++;
						_progress = 0.3f + (float)blitted / totalToBlit * 0.5f;
						_status = $"拼合 Atlas：{blitted} / {totalToBlit}（{tileW}×{tileH}）";
					}

					// 释放小图 Image，后面不再需要
					foreach (var t in chunk) t.Img = null!;

					int sid = nextSourceId++;
					var tileCoords = new List<(string Name, int Col, int Row)>();
					for (int i = 0; i < chunk.Count; i++)
					{
						int c = i % usedCols;
						int r = i / usedCols;
						tileCoords.Add((chunk[i].Name, c, r));
						nameToRef[chunk[i].Name] = new TileRef
						{
							SourceId = sid, AtlasX = c, AtlasY = r,
						};
					}

					results.Add(new AtlasResult
					{
						SourceId = sid, TileW = tileW, TileH = tileH,
						AtlasImg = atlasImg, Tiles = tileCoords,
					});
				}
			}

			_atlasResults = results;
			_nameToRef = nameToRef;
			_status = "拼合完成，准备上传纹理…";
			_progress = 0.80f;
		}
		catch (Exception ex)
		{
			_bgError = ex.Message;
			GD.PrintErr($"[TilesetImporter] 后台异常：{ex}");
		}
		finally
		{
			_bgDone = true;
		}
	}

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
	//  动画 tile
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
