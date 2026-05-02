using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Module.Render;

// Iso-2.5D debug harness for WaterPainterlyBlockSubview.
//
// Lays out a small 5×5 grid of flat iso "ground" tiles using the game's
// actual IsoCoordUtil projection (TileHalfW=64, TileHalfH=32, ZStep=64)
// and drops a WaterPainterlyBlockSubview into the centre cell, scaled to
// the same pixel footprint as one iso tile. Lets you eyeball whether the
// 3D subview's iso angle, colour, and height match what the 2D tile
// system would draw — which is the whole point of the subview existing.
//
// Controls:
//   Left click on water → spawn ripple at that point
//   [ / ]               → decrease / increase ripple amp
//   F                   → toggle splash effects (droplets/sheets)
//   C                   → cycle debug background (solid / iso grid / dark)
//   R                   → clear ripples
//   Mouse wheel         → zoom in/out (camera zoom)
//   Middle-drag         → pan camera
public partial class WaterPainterlyBlockSubviewDebug : Node2D
{
	// Container we paint into — its size is set to match one iso tile so
	// the SubViewport renders at the exact resolution an in-game tile
	// sprite would occupy, surfacing any scale mismatches early.
	private const int GroundGridHalfExtent = 2;    // 5x5 = 2*2+1
	private const float RipplePrimaryAmp = 0.18f;

	private Camera2D _camera = null!;
	private Node2D _groundLayer = null!;
	private Label _hud = null!;
	private ColorRect _background = null!;
	private WaterPainterlyBlockSubview _waterSubview = null!;
	// Where in the iso grid the water pool's ORIGIN cell lives (the
	// (0,0) local cell of the water grid). (0,0,0) = scene centre; y
	// is the game's horizontal Y, not vertical.
	private (int X, int Y, int Z) _waterCell = (0, 0, 0);
	private int _waterGridX = 1;
	private int _waterGridZ = 1;
	// Tile footprint in pixels — matches one diamond top + one side so
	// the subview's output occupies the same rect as a native iso tile.
	// Width = 2*TileHalfW = 128, Height = 2*TileHalfH + ZStep = 128.
	private const int TilePixelWidth = (int)(IsoCoordUtil.TileHalfW * 2);
	private const int TilePixelHeight = (int)(IsoCoordUtil.TileHalfH * 2 + IsoCoordUtil.ZStep);

	private float _rippleAmp = RipplePrimaryAmp;
	private bool _splashEnabled = true;
	private int _bgMode;
	private bool _panning;
	private Vector2 _panStart;
	private Vector2 _cameraStart;

	public override void _Ready()
	{
		// Dark neutral background so painterly water isn't competing with
		// gallery-grey from the editor default.
		_background = new ColorRect
		{
			Name = "Background",
			AnchorRight = 1f,
			AnchorBottom = 1f,
			Color = new Color(0.08f, 0.10f, 0.12f, 1f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		var bgCanvas = new CanvasLayer { Name = "BGLayer", Layer = -100 };
		bgCanvas.AddChild(_background);
		AddChild(bgCanvas);

		_camera = new Camera2D
		{
			Name = "DebugCamera",
			Position = Vector2.Zero,
			Zoom = new Vector2(1.0f, 1.0f),
			AnchorMode = Camera2D.AnchorModeEnum.DragCenter,
		};
		AddChild(_camera);

		_groundLayer = new Node2D { Name = "GroundTiles" };
		AddChild(_groundLayer);

		RebuildScene();

		// HUD — simple top-left readout so you can correlate parameters
		// to what you're seeing.
		_hud = new Label
		{
			Name = "HUD",
			Position = new Vector2(12, 12),
			Text = "",
			Modulate = new Color(1f, 1f, 0.85f),
		};
		_hud.AddThemeFontSizeOverride("font_size", 12);
		var hudCanvas = new CanvasLayer { Name = "HUDLayer", Layer = 100 };
		hudCanvas.AddChild(_hud);
		AddChild(hudCanvas);
	}

	public override void _Process(double delta)
	{
		if (_hud == null) return;
		var fps = Engine.GetFramesPerSecond();
		_hud.Text =
			$"FPS {fps:F0}\n" +
			$"water grid = {_waterGridX}×{_waterGridZ} cells  (press 1/2/3 to resize)\n" +
			$"1 cell     = {WaterPainterlyBlockSubview.WorldCellMeters:F1}m  (= {(int)(IsoCoordUtil.TileHalfW * 2)}×{(int)(IsoCoordUtil.TileHalfH * 2 + IsoCoordUtil.ZStep)} px iso tile)\n" +
			$"ripple_amp = {_rippleAmp:F2}  ([ / ])\n" +
			$"splash_fx  = {(_splashEnabled ? "ON" : "OFF")} (F)\n" +
			$"bg_mode    = {_bgMode}  (C)\n" +
			$"cam yaw/pitch = {_waterSubview.CameraYawDegrees:F1}° / {_waterSubview.CameraPitchDegrees:F1}°\n" +
			"Click water = ripple   R=clear   wheel=zoom   MMB=pan";
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb)
		{
			if (mb.Pressed && mb.ButtonIndex == MouseButton.Left)
			{
				TryClickWater(mb.Position);
			}
			else if (mb.ButtonIndex == MouseButton.Middle)
			{
				_panning = mb.Pressed;
				if (_panning)
				{
					_panStart = mb.Position;
					_cameraStart = _camera.Position;
				}
			}
			else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp)
			{
				ZoomBy(1.1f);
			}
			else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown)
			{
				ZoomBy(1f / 1.1f);
			}
		}
		else if (@event is InputEventMouseMotion mm && _panning)
		{
			var delta = mm.Position - _panStart;
			_camera.Position = _cameraStart - delta / _camera.Zoom.X;
		}
		else if (@event is InputEventKey ke && ke.Pressed && !ke.Echo)
		{
			switch (ke.Keycode)
			{
				case Key.Bracketleft:
					_rippleAmp = Mathf.Max(0.02f, _rippleAmp - 0.02f);
					break;
				case Key.Bracketright:
					_rippleAmp = Mathf.Min(0.5f, _rippleAmp + 0.02f);
					break;
				case Key.F:
					_splashEnabled = !_splashEnabled;
					// Subview's EnableSplashEffects gates droplet/sheet
					// *spawning* per-ripple at runtime (the fluid pipeline
					// is always initialised), so flipping the flag here
					// takes effect on the next SpawnRipple call.
					_waterSubview.EnableSplashEffects = _splashEnabled;
					break;
				case Key.C:
					_bgMode = (_bgMode + 1) % 3;
					_background.Color = _bgMode switch
					{
						0 => new Color(0.08f, 0.10f, 0.12f, 1f),  // dark slate
						1 => new Color(0.30f, 0.34f, 0.30f, 1f),  // mossy grey-green
						_ => new Color(0.55f, 0.47f, 0.35f, 1f),  // warm earth
					};
					break;
				case Key.R:
					_waterSubview.ClearRipples();
					break;
				case Key.Space:
					// Spawn a ripple at grid centre (0, 0) in world-XZ.
					_waterSubview.SpawnRipple(Vector2.Zero, _rippleAmp);
					break;
				case Key.Key1:
					_waterGridX = _waterGridZ = 1;
					RebuildScene();
					break;
				case Key.Key2:
					_waterGridX = _waterGridZ = 2;
					RebuildScene();
					break;
				case Key.Key3:
					_waterGridX = _waterGridZ = 3;
					RebuildScene();
					break;
			}
		}
	}

	// Full teardown + rebuild: resize the water pool and redraw ground
	// tiles underneath. Called on key 1/2/3 to A/B different pool sizes
	// without re-opening the scene.
	private void RebuildScene()
	{
		// Dispose old water subview if present.
		if (_waterSubview != null && IsInstanceValid(_waterSubview))
		{
			_waterSubview.QueueFree();
			_waterSubview = null!;
		}
		// Clear ground tiles.
		foreach (var c in _groundLayer.GetChildren())
			c.QueueFree();

		BuildGroundGrid();
		BuildWaterSubview();
	}

	// Compute iso-aligned rect containing the current water pool. Bounds
	// derived from the extreme iso cell positions of the pool footprint
	// plus the 2D diamond / side overhang those cells emit:
	//   top    = WorldToScreen(topCell).Y - TileHalfH
	//   bottom = WorldToScreen(bottomCell).Y + TileHalfH + ZStep
	//   left   = WorldToScreen(leftCell).X  - TileHalfW
	//   right  = WorldToScreen(rightCell).X + TileHalfW
	// This guarantees the subview rect covers every pixel an in-game
	// painter pass would draw for the pool's tiles, so alignment to
	// neighbour ground tiles is pixel-accurate regardless of gx/gz.
	private (Vector2 TopLeft, Vector2I Size) ComputeWaterRect()
	{
		int x0 = _waterCell.X;
		int y0 = _waterCell.Y;
		int x1 = _waterCell.X + _waterGridX - 1;
		int y1 = _waterCell.Y + _waterGridZ - 1;
		int z = _waterCell.Z;

		var topCell = IsoCoordUtil.WorldToScreen(x0, y0, z);
		var bottomCell = IsoCoordUtil.WorldToScreen(x1, y1, z);
		var leftCell = IsoCoordUtil.WorldToScreen(x0, y1, z);
		var rightCell = IsoCoordUtil.WorldToScreen(x1, y0, z);

		float top = topCell.Y - IsoCoordUtil.TileHalfH;
		float bottom = bottomCell.Y + IsoCoordUtil.TileHalfH + IsoCoordUtil.ZStep;
		float left = leftCell.X - IsoCoordUtil.TileHalfW;
		float right = rightCell.X + IsoCoordUtil.TileHalfW;

		return (new Vector2(left, top),
			new Vector2I((int)(right - left), (int)(bottom - top)));
	}

	private void BuildWaterSubview()
	{
		// Centre the pool on the iso origin. For gx=1/3 this lands pool
		// centre exactly on a cell centre (0,0); for gx=2 it lands between
		// 4 cells (still pixel-aligned to iso screen since TileHalfH is
		// an integer). Pool covers cells (_waterCell..+_waterGrid-1).
		_waterCell = (-_waterGridX / 2, -_waterGridZ / 2, 0);

		var (tl, size) = ComputeWaterRect();
		_waterSubview = new WaterPainterlyBlockSubview
		{
			Name = "WaterTile",
			GridX = _waterGridX,
			GridZ = _waterGridZ,
			ViewportSize = size,
			DisplayStretchToSquare = true,
			TransparentBackground = true,
			EnableSplashEffects = _splashEnabled,
		};
		_waterSubview.CustomMinimumSize = size;
		_waterSubview.Size = size;
		_waterSubview.Position = tl;
		// z_index = pool's front-most-cell diagonal so neighbour ground
		// tiles with a lower diagonal draw BEHIND water (occluded by
		// water's side faces where they overlap) and tiles with a higher
		// diagonal draw IN FRONT of water (occluding water's side faces
		// where they overlap). Matches IsoCoordUtil.SortKey semantics —
		// multi-cell features own all diagonals they span.
		int frontDiag = (_waterGridX - 1) + (_waterGridZ - 1);
		_waterSubview.ZIndex = frontDiag * IsoZIndexStep;
		AddChild(_waterSubview);
	}

	private void ZoomBy(float factor)
	{
		var z = _camera.Zoom * factor;
		z.X = Mathf.Clamp(z.X, 0.25f, 8f);
		z.Y = Mathf.Clamp(z.Y, 0.25f, 8f);
		_camera.Zoom = z;
	}

	// z_index multiplier per diagonal step. Has to leave headroom for
	// multiple visual layers per cell (diamond top, side faces, outline)
	// so same-cell pieces never jump OVER the next cell's diamond.
	private const int IsoZIndexStep = 4;

	private static int IsoZIndexForCell(int x, int y, int z)
	{
		// Painter's ordering (matches IsoCoordUtil.SortKey semantics):
		//   primary: x+y ascending (farther → closer)
		//   secondary: -z so "taller" voxels (z<0 in game terms) get
		//   drawn on top of ground-level cells on the same diagonal
		// Here all ground tiles are at z=0 so the z term just zeros out.
		return (x + y) * IsoZIndexStep - z;
	}

	// Is the given iso cell covered by the current water pool?
	// Pool occupies cells (_waterCell.X + i, _waterCell.Y + j, _waterCell.Z)
	// for i in [0, _waterGridX-1], j in [0, _waterGridZ-1].
	private bool IsCellUnderWater(int x, int y, int z)
	{
		if (z != _waterCell.Z) return false;
		int dx = x - _waterCell.X;
		int dy = y - _waterCell.Y;
		return dx >= 0 && dx < _waterGridX && dy >= 0 && dy < _waterGridZ;
	}

	// Build a flat 5×5 iso ground grid around the origin. Uses
	// IsoCoordUtil.WorldToScreen so we're drawing with the same math the
	// in-game renderer uses — if the subview's output lines up with these
	// tiles, the iso angle is correct.
	private void BuildGroundGrid()
	{
		var diamondPoints = new Vector2[]
		{
			new(0, -IsoCoordUtil.TileHalfH),                         // top
			new(IsoCoordUtil.TileHalfW, 0),                           // right
			new(0, IsoCoordUtil.TileHalfH),                           // bottom
			new(-IsoCoordUtil.TileHalfW, 0),                          // left
		};

		for (int y = -GroundGridHalfExtent; y <= GroundGridHalfExtent; y++)
		{
			for (int x = -GroundGridHalfExtent; x <= GroundGridHalfExtent; x++)
			{
				// Skip cells that the water pool already covers — the 3D
				// water subview renders its own top for those cells.
				if (IsCellUnderWater(x, y, 0))
					continue;

				var cellScreen = IsoCoordUtil.WorldToScreen(x, y, 0);
				// Group each cell under a single Node2D so the whole
				// cell (diamond + sides + outline) shares one z_index
				// in the painter pass.
				var cellNode = new Node2D
				{
					Name = $"Cell_{x}_{y}",
					Position = cellScreen,
					ZIndex = IsoZIndexForCell(x, y, 0),
				};
				_groundLayer.AddChild(cellNode);

				// Draw side-face mimic FIRST so diamond top overdraws it
				// in tree order (same as IsometricVoxelRenderer's
				// per-cell sides-before-top ordering).
				DrawFakeSideFaces(cellNode, x, y);

				cellNode.AddChild(new Polygon2D
				{
					Name = "Top",
					Polygon = diamondPoints,
					Color = TileColor(x, y),
				});

				var line = new Line2D
				{
					Name = "Outline",
					Width = 1.2f,
					DefaultColor = new Color(0, 0, 0, 0.35f),
					Closed = true,
				};
				line.AddPoint(diamondPoints[0]);
				line.AddPoint(diamondPoints[1]);
				line.AddPoint(diamondPoints[2]);
				line.AddPoint(diamondPoints[3]);
				cellNode.AddChild(line);
			}
		}
	}

	private void DrawFakeSideFaces(Node2D cellNode, int x, int y)
	{
		// Voxel sides: parallelograms hanging below the diamond top
		// vertex, ZStep tall. Added as children of the cell node so they
		// inherit its position + z_index → same painter bucket as the
		// cell's diamond top.
		var bottom = new Vector2(0, IsoCoordUtil.TileHalfH);
		var right = new Vector2(IsoCoordUtil.TileHalfW, 0);
		var left = new Vector2(-IsoCoordUtil.TileHalfW, 0);
		var downR = right + new Vector2(0, IsoCoordUtil.ZStep);
		var downL = left + new Vector2(0, IsoCoordUtil.ZStep);
		var downB = bottom + new Vector2(0, IsoCoordUtil.ZStep);

		var sideColor = TileColor(x, y) * 0.72f;
		sideColor.A = 1f;
		cellNode.AddChild(new Polygon2D
		{
			Name = "SideRight",
			Polygon = new[] { bottom, right, downR, downB },
			Color = sideColor,
		});
		cellNode.AddChild(new Polygon2D
		{
			Name = "SideLeft",
			Polygon = new[] { bottom, downB, downL, left },
			Color = sideColor * 0.88f,
		});
	}

	// Checkerboard-ish tinting so adjacent tiles are distinguishable and
	// the iso grid pattern reads clearly, while staying within a single
	// natural material family (muted greens for "grass").
	private static Color TileColor(int x, int y)
	{
		bool alt = ((x + y) & 1) == 0;
		return alt
			? new Color(0.32f, 0.42f, 0.26f, 1f)
			: new Color(0.38f, 0.48f, 0.30f, 1f);
	}

	// Click → spawn ripple. Properly inverts the iso projection so
	// ripples appear where the user clicks (including on the pool's
	// front-right vs front-left halves for multi-cell pools).
	//
	// Derivation: at pitch=arctan(1/sqrt(2))+yaw=45° (our subview camera),
	// 1 world-meter step on the ground plane maps to exactly the same
	// screen pixel delta as 1 iso cell (TileHalfW=64 px per 1.5m in X,
	// TileHalfH=32 px per 1.5m in both X and Y, verified in subview
	// construction). That means IsoCoordUtil's inverse projection works
	// directly to recover world cell coords, and multiplying by 1.5 gets
	// us metres.
	private void TryClickWater(Vector2 screenPos)
	{
		var worldMouse = GetGlobalMousePosition();

		// Pool centre in scene-screen coords. For multi-cell pools this
		// may sit between cells (fractional cell coords), which the
		// iso inverse projection handles fine.
		float poolCenterCellX = _waterCell.X + (_waterGridX - 1) * 0.5f;
		float poolCenterCellY = _waterCell.Y + (_waterGridZ - 1) * 0.5f;
		var poolCenterScreen = IsoCoordUtil.WorldToScreen(
			poolCenterCellX, poolCenterCellY, _waterCell.Z);

		var rel = worldMouse - poolCenterScreen;
		// Inverse iso projection (from IsoCoordUtil):
		//   rel.X = (wx_cells - wy_cells) * TileHalfW
		//   rel.Y = (wx_cells + wy_cells) * TileHalfH
		// → wx_cells = (rel.X/TileHalfW + rel.Y/TileHalfH) * 0.5
		//   wy_cells = (rel.Y/TileHalfH - rel.X/TileHalfW) * 0.5
		float wxCells = (rel.X / IsoCoordUtil.TileHalfW + rel.Y / IsoCoordUtil.TileHalfH) * 0.5f;
		float wyCells = (rel.Y / IsoCoordUtil.TileHalfH - rel.X / IsoCoordUtil.TileHalfW) * 0.5f;
		float worldX = wxCells * WaterPainterlyBlockSubview.WorldCellMeters;
		float worldZ = wyCells * WaterPainterlyBlockSubview.WorldCellMeters;

		// Clamp to pool footprint so clicks on the pool's SIDE faces
		// (below the diamond top) don't spawn ripples at bogus
		// positions outside the water surface.
		float halfX = _waterGridX * 0.5f * _waterSubview.BlockSize;
		float halfZ = _waterGridZ * 0.5f * _waterSubview.BlockSize;
		if (Mathf.Abs(worldX) > halfX || Mathf.Abs(worldZ) > halfZ)
			return;

		_waterSubview.SpawnRipple(new Vector2(worldX, worldZ), _rippleAmp);
	}
}
