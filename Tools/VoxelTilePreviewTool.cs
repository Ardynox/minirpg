using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Tools;

public partial class VoxelTilePreviewTool : Control
{
	private const string TileRoot = "res://Assets/Art/Generated/voxel_tiles";
	private const float LeftDarken = 0.65f;
	private const float RightDarken = 0.80f;
	private const int SideTextureWidth = 64;
	private const int SideTextureHeight = 160;
	private const int SideFaceHeight = 80;

	private readonly string[] _blockOrder = ["grass_block", "dirt", "stone"];
	private readonly Dictionary<string, BlockPreviewTextures> _cache = new(StringComparer.OrdinalIgnoreCase);

	private int _currentIndex;
	private bool _showSides = true;

	private Label _statusLabel = null!;
	private Sprite2D _topSprite = null!;
	private Sprite2D _leftSprite = null!;
	private Sprite2D _rightSprite = null!;
	private TextureRect _sourceTile = null!;

	public override void _Ready()
	{
		BuildUi();
		UpdatePreview();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventKey key || !key.Pressed || key.Echo)
			return;

		switch (key.Keycode)
		{
			case Key.Space:
			case Key.Right:
				_currentIndex = (_currentIndex + 1) % _blockOrder.Length;
				UpdatePreview();
				GetViewport().SetInputAsHandled();
				break;
			case Key.Left:
				_currentIndex = (_currentIndex - 1 + _blockOrder.Length) % _blockOrder.Length;
				UpdatePreview();
				GetViewport().SetInputAsHandled();
				break;
			case Key.S:
				_showSides = !_showSides;
				UpdatePreview();
				GetViewport().SetInputAsHandled();
				break;
		}
	}

	private void BuildUi()
	{
		var bg = new ColorRect
		{
			Color = new Color(0.08f, 0.09f, 0.12f, 1f),
		};
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(bg);

		var root = new MarginContainer();
		root.SetAnchorsPreset(LayoutPreset.FullRect);
		root.AddThemeConstantOverride("margin_left", 20);
		root.AddThemeConstantOverride("margin_top", 20);
		root.AddThemeConstantOverride("margin_right", 20);
		root.AddThemeConstantOverride("margin_bottom", 20);
		AddChild(root);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 12);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		root.AddChild(vbox);

		vbox.AddChild(new Label
		{
			Text = "Voxel Tile Preview Tool",
			Modulate = new Color(0.95f, 0.97f, 1f),
		});

		vbox.AddChild(new Label
		{
			Text = "左右键/Space 切换方块，S 切换是否显示侧面",
			Modulate = new Color(0.75f, 0.8f, 0.92f),
		});

		_statusLabel = new Label
		{
			Modulate = new Color(0.90f, 0.94f, 1f),
		};
		vbox.AddChild(_statusLabel);

		var body = new HBoxContainer();
		body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		body.SizeFlagsVertical = SizeFlags.ExpandFill;
		body.AddThemeConstantOverride("separation", 24);
		vbox.AddChild(body);

		var previewPanel = new PanelContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		body.AddChild(previewPanel);

		var previewNode = new Node2D();
		var previewContainer = new SubViewportContainer
		{
			Stretch = true,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(640, 420),
		};
		previewPanel.AddChild(previewContainer);

		var previewViewport = new SubViewport
		{
			TransparentBg = false,
			Size = new Vector2I(640, 420),
			Disable3D = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
		};
		previewContainer.AddChild(previewViewport);
		previewViewport.AddChild(previewNode);

		var previewBg = new ColorRect { Color = new Color(0.11f, 0.12f, 0.16f, 1f), Size = new Vector2(640, 420) };
		previewNode.AddChild(previewBg);

		_topSprite = new Sprite2D { Centered = true, Position = new Vector2(240, 150), Scale = new Vector2(2f, 2f) };
		_leftSprite = new Sprite2D { Centered = true, Position = new Vector2(176, 310), Scale = new Vector2(2f, 2f) };
		_rightSprite = new Sprite2D { Centered = true, Position = new Vector2(304, 310), Scale = new Vector2(2f, 2f) };
		previewNode.AddChild(_leftSprite);
		previewNode.AddChild(_rightSprite);
		previewNode.AddChild(_topSprite);

		var sidePanel = new PanelContainer
		{
			CustomMinimumSize = new Vector2(220, 0),
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		body.AddChild(sidePanel);

		var sideVBox = new VBoxContainer();
		sideVBox.AddThemeConstantOverride("separation", 8);
		sideVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		sideVBox.SizeFlagsVertical = SizeFlags.ExpandFill;
		sidePanel.AddChild(sideVBox);

		sideVBox.AddChild(new Label { Text = "原始 tile 预览" });
		_sourceTile = new TextureRect
		{
			ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			CustomMinimumSize = new Vector2(180, 180),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		sideVBox.AddChild(_sourceTile);
	}

	private void UpdatePreview()
	{
		var terrainId = _blockOrder[_currentIndex];
		var textures = GetOrBuildTextures(terrainId);

		_topSprite.Texture = textures.Top;
		_leftSprite.Texture = textures.Left;
		_rightSprite.Texture = textures.Right;
		_leftSprite.Visible = _showSides;
		_rightSprite.Visible = _showSides;
		_sourceTile.Texture = textures.Source;

		_statusLabel.Text = $"当前: {terrainId} | 侧面: {(_showSides ? "开启" : "关闭")}";
	}

	private BlockPreviewTextures GetOrBuildTextures(string terrainId)
	{
		if (_cache.TryGetValue(terrainId, out var cached))
			return cached;

		var topTilePath = ResolveTopPath(terrainId);
		var sideTilePath = ResolveSidePath(terrainId);
		var topTileTexture = GD.Load<Texture2D>(topTilePath);
		if (topTileTexture == null)
			throw new InvalidOperationException($"Missing tile texture: {topTilePath}");

		var sideTileTexture = GD.Load<Texture2D>(sideTilePath) ?? topTileTexture;
		var topSource = topTileTexture.GetImage();
		var sideSource = sideTileTexture.GetImage();

		var top = ImageTexture.CreateFromImage(BuildTopDiamondFromTile(topSource));
		var left = ImageTexture.CreateFromImage(GenerateSideFaceFromTile(sideSource, false, LeftDarken));
		var right = ImageTexture.CreateFromImage(GenerateSideFaceFromTile(sideSource, true, RightDarken));
		var source = ImageTexture.CreateFromImage(topSource);

		var result = new BlockPreviewTextures(top, left, right, source);
		_cache[terrainId] = result;
		return result;
	}

	private static string ResolveTopPath(string terrainId) => terrainId switch
	{
		"grass_block" => $"{TileRoot}/tile_grass_top.png",
		"dirt" => $"{TileRoot}/tile_dirt.png",
		_ => $"{TileRoot}/tile_stone.png",
	};

	private static string ResolveSidePath(string terrainId) => terrainId switch
	{
		"grass_block" => $"{TileRoot}/tile_grass_side.png",
		"dirt" => $"{TileRoot}/tile_dirt.png",
		_ => $"{TileRoot}/tile_stone.png",
	};

	private static Image BuildTopDiamondFromTile(Image tileImage)
	{
		const int targetW = 128;
		const int targetH = 64;
		var result = Image.CreateEmpty(targetW, targetH, false, Image.Format.Rgba8);
		var srcW = tileImage.GetWidth();
		var srcH = tileImage.GetHeight();
		var halfW = targetW / 2f;
		var halfH = targetH / 2f;

		for (var y = 0; y < targetH; y++)
		{
			for (var x = 0; x < targetW; x++)
			{
				var nx = (x - halfW) / halfW;
				var ny = (y - halfH) / halfH;
				if (Math.Abs(nx) + Math.Abs(ny) > 1f)
				{
					result.SetPixel(x, y, Colors.Transparent);
					continue;
				}

				var u = (nx - ny + 1f) * 0.5f;
				var v = (nx + ny + 1f) * 0.5f;
				var sx = Math.Clamp((int)Math.Round(u * (srcW - 1)), 0, srcW - 1);
				var sy = Math.Clamp((int)Math.Round(v * (srcH - 1)), 0, srcH - 1);
				result.SetPixel(x, y, tileImage.GetPixel(sx, sy));
			}
		}

		return result;
	}

	private static Image GenerateSideFaceFromTile(Image tileImage, bool isRight, float darken)
	{
		var iw = SideTextureWidth;
		var ih = SideTextureHeight;
		var faceH = SideFaceHeight;
		var img = Image.CreateEmpty(iw, ih, false, Image.Format.Rgba8);
		var tw = tileImage.GetWidth();
		var th = tileImage.GetHeight();

		for (var px = 0; px < iw; px++)
		{
			var t = px / (float)(iw - 1);
			var sampleX = isRight
				? (tw - 1) - (int)Math.Round(t * (tw - 1))
				: (int)Math.Round(t * (tw - 1));
			var pyStart = isRight
				? (int)((iw - 1 - px) * 0.5f)
				: (int)(px * 0.5f);

			for (var dy = 0; dy < faceH; dy++)
			{
				var py = pyStart + dy;
				if (py >= ih)
					break;

				var sampleY = Math.Clamp((int)Math.Round((dy / (float)(faceH - 1)) * (th - 1)), 0, th - 1);
				var color = tileImage.GetPixel(sampleX, sampleY);
				var gradient = 1.0f - (dy / (float)faceH) * 0.2f;
				var final = color * new Color(darken * gradient, darken * gradient, darken * gradient, 1f);
				final.A = color.A;
				img.SetPixel(px, py, final);
			}
		}

		return img;
	}

	private readonly record struct BlockPreviewTextures(Texture2D Top, Texture2D Left, Texture2D Right, Texture2D Source);
}
