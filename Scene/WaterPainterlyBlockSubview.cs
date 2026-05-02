using System;
using System.Collections.Generic;
using Godot;

// Isometric-friendly subview wrapper around the painterly water block.
//
// Goal: embed the 3D painterly water block(s) into a 2D/2.5D isometric game
// as a ViewportTexture / SubViewportContainer, so gameplay code can drop
// this scene anywhere on a Control/Node2D parent and treat it like a sprite
// that happens to show live, animated, ripple-capable water.
//
// Design differences from WaterPainterlyBlockPreview:
//   • Root is a SubViewportContainer (Control) instead of Node3D — the whole
//     3D world renders into a SubViewport whose texture is displayed in 2D.
//   • Camera3D is locked to the game's isometric projection convention
//     (yaw 45°, pitch 30° ≈ 2:1 dimetric — matches IsoCoordUtil's 2:1
//     pixel-iso aspect).
//   • Only the two iso-visible side faces are emitted on the outer edges of
//     the grid (front-right at +X outer edge and front-left at +Z outer
//     edge). Back sides save fragment cost and wouldn't be visible anyway.
//   • No debug panel, no orbit camera, no sail boat, no drop-test object,
//     no mouse-click ripple input — those belong to the preview.
//   • External gameplay spawns ripples via the SpawnRipple() public API.
//
// The fluid pipeline (droplet Gaussian density → fullscreen composite) is
// preserved: it's what gives splashes their "real water" metaball look and
// was explicitly requested ("后续起水效果也需要"). Droplets and splash
// sheets render inside this subview's own SubViewport so they don't bleed
// into the game's main viewport.
//
// Iso aspect note: true 3D iso projection (pitch=arctan(1/sqrt(2))≈35.264°)
// gives a 1:1:1 cube a sqrt(3)/2 : 1 (≈0.866:1) projected aspect on its
// top diamond. Game pixel-iso tile aspect is 1:1 (128×128 px). So the 3D
// cube's diamond renders ~13% narrower than the 2D iso tile's diamond.
// To fix that without giving up the 50/50 vertical split between diamond
// and sides, we render the inner SubViewport at iso-correct aspect
// (e.g. 111×128) and stretch horizontally to the displayed Control size
// (e.g. 128×128). DisplayStretchToSquare controls this; turn off if you
// want pixel-perfect 3D ortho output without horizontal squashing.
public partial class WaterPainterlyBlockSubview : Control
{
	// Canonical cell size in meters (matches IsoCoordUtil / Docs/开发约定.md
	// "度量衡口径": 1 voxel cell = 1.5m × 1.5m × 1.5m). All in-game water
	// block defaults and auto-fits derive from this so gameplay code can
	// express water pool size in *cells* rather than meters.
	public const float WorldCellMeters = 1.5f;

	// --- Exported params -------------------------------------------------
	// All exported via [Export] so the scene can be tweaked from the editor
	// inspector or overridden per-instance at runtime.

	// Display rect target size. Shown via TextureRect at this size; the
	// inner SubViewport renders at iso-correct aspect derived from this
	// height (see DisplayStretchToSquare). Defaults to one game iso tile
	// (128×128 px = TileHalfW*2 × (TileHalfH*2 + ZStep)).
	[Export] public Vector2I ViewportSize { get; set; } = new(128, 128);

	// When true, the inner SubViewport is sized at the 3D-iso-correct
	// aspect ratio (height = ViewportSize.Y, width derived from projected
	// 3D extent), then non-uniformly stretched horizontally to fill the
	// Control's ViewportSize.X. Result: the rendered diamond top fits the
	// 2D iso tile pixel boundary EXACTLY (128 px wide for a 128 px tall
	// tile). Trade-off: a 1:1:1 cube ends up looking ~15% wider than tall
	// in the displayed image (it's the same effect as 2D pixel-iso games:
	// the cube's top diamond aspect is 2:1, not the true-iso sqrt(3):1).
	//
	// When false, the SubViewport is exactly ViewportSize and no stretch
	// is applied — geometry comes out at the true 3D iso aspect. The
	// diamond will appear narrower than a 2D iso tile's diamond would
	// occupy (~13% gutter on each horizontal side).
	[Export] public bool DisplayStretchToSquare { get; set; } = true;

	// BlockSize / BlockHeight are in meters. Defaults to 1 game cell
	// (1.5m), matching IsoCoordUtil. Override only if you need a
	// non-standard voxel (e.g. half-height shallow pond = 0.75m height,
	// or a stylised 2m-wide ornamental fountain).
	[Export] public float BlockSize { get; set; } = WorldCellMeters;
	[Export] public float BlockHeight { get; set; } = WorldCellMeters;
	// Grid dimensions in CELLS. A 2×3 water pool = GridX=2, GridZ=3
	// (covers 3m × 4.5m in world), then renders at 2×3 iso tiles worth
	// of pixels in the SubViewport.
	[Export] public int GridX { get; set; } = 1;
	[Export] public int GridZ { get; set; } = 1;

	[Export] public int TopSubdivisions { get; set; } = 48;
	[Export] public int SideSubdivisionsH { get; set; } = 48;
	[Export] public int SideSubdivisionsV { get; set; } = 18;

	// Game-iso match defaults. Yaw 45° makes +X and +Z faces both visible.
	// Pitch arctan(1/sqrt(2)) ≈ 35.264° is the "true iso" angle that
	// gives a unit cube its diamond top + two square sides projecting to
	// EXACTLY 50/50 vertical split in the rendered image — matching the
	// game's 2D pixel iso tile layout (64 px diamond + 64 px side =
	// 128 px tile). Pitch 30° gives 2:1 horizontal aspect on the diamond
	// but a 30/70 split which doesn't line up with 2D iso tile pixel
	// boundaries; pitch 26.57° gives an even smaller diamond. The
	// horizontal width of the cube projection at 35.264° pitch is
	// ~13% narrower than the 2D pixel-iso tile width (intrinsic
	// non-conformality between true iso and 2:1 pixel iso); accept this
	// or non-uniformly stretch the subview horizontally to compensate.
	[Export] public float CameraYawDegrees { get; set; } = 45f;
	[Export] public float CameraPitchDegrees { get; set; } = 35.264389f;
	// Camera distance is only a framing choice — orthographic size controls
	// actual zoom. Kept as a separate knob so the camera sits far enough
	// back that near/far clip planes don't slice through the water grid.
	[Export] public float CameraDistance { get; set; } = 18f;
	// Orthographic vertical size (world units). Auto-fit default below in
	// _Ready if left at 0; explicit values override the auto-fit.
	[Export] public float OrthographicSize { get; set; } = 0f;

	// Transparent bg lets the subview stack on top of the 2D iso game's
	// existing tile rendering. Disable to get a solid backdrop (useful when
	// previewing standalone).
	[Export] public bool TransparentBackground { get; set; } = true;

	// Droplets / splash sheets cost real GPU/CPU. Toggle off for distant
	// water tiles where the player won't notice the absence.
	[Export] public bool EnableSplashEffects { get; set; } = true;

	// --- Internal nodes --------------------------------------------------
	private SubViewport _viewport = null!;
	private TextureRect _displayRect = null!;
	private Camera3D _camera = null!;
	private ShaderMaterial _topMaterial = null!;
	private ShaderMaterial _sideMaterial = null!;
	private float _timeNow;
	// SubViewport's actual pixel size (may differ from ViewportSize when
	// DisplayStretchToSquare = true, to preserve 3D iso aspect).
	private Vector2I _renderSize;

	// --- Ripple state (identical layout to preview's pack for shader) ----
	private struct Ripple { public Vector2 WorldXZ; public float TStart; public float Amp; }
	private const int MaxRipples = 64;
	private readonly List<Ripple> _ripples = new();
	private readonly Vector4[] _packedRipples = new Vector4[MaxRipples];
	private float _rippleLifetime = 1.5f;
	private float _wakeRippleLifetime = 0.70f;

	// --- Fluid pipeline (droplet density → composite) --------------------
	// An INNER SubViewport that accumulates per-droplet additive Gaussian
	// density blobs. The composite ColorRect (inside this subview's
	// CanvasLayer) samples that density to render the unified fluid
	// surface. Nested SubViewports are supported in Godot; the inner one
	// just needs its own camera synced to the outer _camera every frame.
	private static Shader? _densityShaderCache;
	private static Shader DensityShader => _densityShaderCache ??=
		GD.Load<Shader>("res://Assets/Shaders/painterly_droplet_density.gdshader")
		?? throw new InvalidOperationException(
			"painterly_droplet_density.gdshader missing — open project in editor once to import.");
	private static Shader? _fluidCompositeShaderCache;
	private static Shader FluidCompositeShader => _fluidCompositeShaderCache ??=
		GD.Load<Shader>("res://Assets/Shaders/painterly_fluid_composite.gdshader")
		?? throw new InvalidOperationException(
			"painterly_fluid_composite.gdshader missing — open project in editor once to import.");
	private static Shader? _sheetShaderCache;
	private static Shader SheetShader => _sheetShaderCache ??=
		GD.Load<Shader>("res://Assets/Shaders/painterly_splash_sheet.gdshader")
		?? throw new InvalidOperationException(
			"painterly_splash_sheet.gdshader missing — open project in editor once to import.");

	private ShaderMaterial _sharedDensityMaterial = null!;
	private SubViewport _fluidViewport = null!;
	private Camera3D _fluidCamera = null!;
	private ColorRect _fluidRect = null!;
	private ShaderMaterial _fluidCompositeMaterial = null!;

	// --- Droplet / sheet runtime state ----------------------------------
	// Trimmed down from the preview: keeps the phase-staged splash spawner
	// (impact → crown rim → crater collapse → jet → bead chain) because
	// that's what makes splashes look like real water, not confetti.
	private sealed class Droplet
	{
		public MeshInstance3D Node = null!;
		public Vector3 Velocity;
		public float Life;
		public float MaxLife;
		public float BaseRadius;
		public float DragPerSec;
		public float StretchGain;
		public Vector2 WobbleDir;
		public float WobblePhase;
		public bool IsTail;
	}
	private readonly List<Droplet> _droplets = new();
	private static readonly Random _rng = new();
	private static readonly SphereMesh _dropletMesh = new()
	{
		Radius = 0.5f,
		Height = 1.0f,
		RadialSegments = 12,
		Rings = 6,
	};

	private struct PendingSpawn { public float Delay; public Action Action; }
	private readonly List<PendingSpawn> _pendingSpawns = new();

	private sealed class Sheet
	{
		public MeshInstance3D Node = null!;
		public ShaderMaterial Mat = null!;
		public float Age;
		public float MaxLife;
	}
	private readonly List<Sheet> _sheets = new();
	private static readonly CylinderMesh _sheetMesh = new()
	{
		TopRadius = 1.0f,
		BottomRadius = 1.0f,
		Height = 1.0f,
		RadialSegments = 32,
		Rings = 4,
		CapTop = false,
		CapBottom = false,
	};

	// Shared lighting constants (match preview's values so shaders see the
	// same sun / environment regardless of which scene is hosting the
	// water). Keeps the painterly look consistent between preview and in-
	// game subview instances.
	private static readonly Vector3 _sunDir = new(-0.6f, 0.75f, 0.3f);
	private static readonly Color _sunColor = new(1.25f, 1.10f, 0.85f);
	private static readonly Color _envSkyColor     = new(0.62f, 0.78f, 0.95f);
	private static readonly Color _envHorizonColor = new(0.85f, 0.78f, 0.65f);
	private static readonly Color _envGroundColor  = new(0.32f, 0.30f, 0.26f);

	// --- Lifecycle -------------------------------------------------------

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		// CustomMinimumSize so the container actually shows up at a
		// reasonable size when dropped into an empty parent for testing;
		// real in-game usage will set the rect explicitly.
		if (CustomMinimumSize == Vector2.Zero)
			CustomMinimumSize = ViewportSize;

		_renderSize = ComputeRenderSize();

		_viewport = new SubViewport
		{
			Name = "WaterSubViewport",
			Size = _renderSize,
			TransparentBg = TransparentBackground,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
			RenderTargetClearMode = SubViewport.ClearMode.Always,
			Disable3D = false,
			HandleInputLocally = false,
		};
		AddChild(_viewport);

		_viewport.AddChild(CreateEnvironment());
		_viewport.AddChild(CreateSunLight());
		_viewport.AddChild(CreateFillLight());

		_camera = CreateIsoCamera();
		_viewport.AddChild(_camera);

		_viewport.AddChild(CreateWaterGrid());

		// Always build the fluid pipeline so EnableSplashEffects can be
		// toggled at runtime — the flag gates spawning, not init. Cost of
		// the idle pipeline is a ~0.1ms-per-frame empty SubViewport clear.
		SetupFluidLayer();

		// Display layer — TextureRect siblings the SubViewport (which is
		// invisible in the 2D tree itself) and shows its texture stretched
		// to fill this Control's rect. Stretching to ViewportSize means
		// in iso-correct mode the inner 3D output gets non-uniformly
		// scaled (horizontal blow-up) so the diamond fills the tile-pixel
		// width exactly.
		_displayRect = new TextureRect
		{
			Name = "WaterDisplay",
			Texture = _viewport.GetTexture(),
			StretchMode = TextureRect.StretchModeEnum.Scale,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			AnchorRight = 1f,
			AnchorBottom = 1f,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		AddChild(_displayRect);

		PushTimeNow();
	}

	// Computes the inner SubViewport pixel size. When DisplayStretchToSquare
	// is true we render at iso-correct aspect (so the 3D ortho projection
	// is uniform/un-distorted internally), then horizontally non-uniformly
	// stretch to ViewportSize at display. When false, render at exactly
	// ViewportSize and let the 3D scene be slightly under-fit horizontally.
	private Vector2I ComputeRenderSize()
	{
		if (!DisplayStretchToSquare)
			return ViewportSize;

		// 3D iso projected aspect for this grid:
		//   horizontal extent (view units) = (GridX + GridZ) * BlockSize / sqrt(2)
		//   vertical extent  (view units)  = horizontal*sin(P) + BlockHeight*cos(P)
		// Aspect = horizontal / vertical
		float pitchRad = Mathf.DegToRad(CameraPitchDegrees);
		float horizontal = (GridX * BlockSize + GridZ * BlockSize) / Mathf.Sqrt(2f);
		float diamondVertical = horizontal * Mathf.Sin(pitchRad);
		float sideVertical = BlockHeight * Mathf.Cos(pitchRad);
		float vertical = diamondVertical + sideVertical;
		float aspect = vertical > 0 ? horizontal / vertical : 1f;

		// Keep height = ViewportSize.Y, derive width from aspect. When
		// later TextureRect stretches to ViewportSize.X, the X axis blows
		// up by ViewportSize.X / renderWidth ≈ 1/aspect (= 1.155 for
		// pitch=35.264° and a unit cube), pixel-matching the 2D iso tile
		// diamond width.
		int renderH = Mathf.Max(8, ViewportSize.Y);
		int renderW = Mathf.Max(8, Mathf.RoundToInt(renderH * aspect));
		return new Vector2I(renderW, renderH);
	}

	public override void _Process(double delta)
	{
		_timeNow += (float)delta;
		PushTimeNow();

		// Stepping existing droplets/sheets keeps running even when the
		// flag is off — otherwise particles already in flight would
		// freeze mid-air when the user toggles splash-fx off. Only new
		// spawns are gated (see SpawnRipple).
		StepPendingSpawns((float)delta);
		StepDroplets((float)delta);
		StepSheets((float)delta);
		SyncFluidCamera();

		PushRipples();
	}

	// --- Public API ------------------------------------------------------

	/// <summary>
	/// Spawns a ripple at world-space position (x, z) on the water surface
	/// (y=0). Amplitude scales the splash size (both ripple envelope and
	/// droplet count). Typical values: 0.05 for small drop, 0.18 for click,
	/// 0.35 for heavy impact.
	/// </summary>
	public void SpawnRipple(Vector2 worldXZ, float amp)
	{
		if (_ripples.Count >= MaxRipples) _ripples.RemoveAt(0);
		_ripples.Add(new Ripple { WorldXZ = worldXZ, TStart = _timeNow, Amp = amp });
		if (EnableSplashEffects)
		{
			int count = Mathf.Clamp((int)(amp / 0.04f) + 4, 6, 22);
			SpawnDroplets(worldXZ, amp, count);
		}
	}

	/// <summary>
	/// Lightweight ripple with no droplet cascade — for wakes, rain, or
	/// cheap ambient effects where the full splash would overload the
	/// 64-slot ripple pool.
	/// </summary>
	public void SpawnRippleQuiet(Vector2 worldXZ, float amp)
	{
		if (_ripples.Count >= MaxRipples) _ripples.RemoveAt(0);
		_ripples.Add(new Ripple { WorldXZ = worldXZ, TStart = _timeNow, Amp = amp });
	}

	/// <summary>
	/// Wake-style ripple (negative amp sentinel) — the shader treats
	/// negative-amp ripples as continuous-wake emitters, skipping
	/// per-impact crater / crown / foam so densely-packed trails don't
	/// read as discrete pulses.
	/// </summary>
	public void SpawnWakeRipple(Vector2 worldXZ, float amp)
	{
		if (_ripples.Count >= MaxRipples) _ripples.RemoveAt(0);
		_ripples.Add(new Ripple { WorldXZ = worldXZ, TStart = _timeNow, Amp = -Mathf.Abs(amp) });
	}

	public void ClearRipples() => _ripples.Clear();

	/// <summary>Texture that mirrors the SubViewport — assign to a
	/// TextureRect / Sprite2D if you want to display the water somewhere
	/// other than the SubViewportContainer itself.</summary>
	public ViewportTexture GetViewportTexture() => _viewport.GetTexture();

	// --- Scene construction ---------------------------------------------

	private static WorldEnvironment CreateEnvironment()
	{
		// BGMode.ClearColor lets the SubViewport's TransparentBg flag
		// show through to alpha; BGMode.Color would force a solid fill.
		var env = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.ClearColor,
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = new Color(0.82f, 0.86f, 0.88f),
			AmbientLightEnergy = 1.0f,
			AmbientLightSkyContribution = 0.0f,
			TonemapMode = Godot.Environment.ToneMapper.Filmic,
		};
		return new WorldEnvironment { Environment = env };
	}

	private static DirectionalLight3D CreateSunLight() => new()
	{
		Name = "SunLight",
		RotationDegrees = new Vector3(-42.0f, -38.0f, 0.0f),
		LightColor = new Color(1.0f, 0.92f, 0.78f),
		LightEnergy = 1.3f,
		ShadowEnabled = false,
	};

	private static DirectionalLight3D CreateFillLight() => new()
	{
		Name = "FillLight",
		RotationDegrees = new Vector3(-12.0f, 130.0f, 0.0f),
		LightColor = new Color(0.56f, 0.72f, 0.78f),
		LightEnergy = 0.32f,
		ShadowEnabled = false,
	};

	private Camera3D CreateIsoCamera()
	{
		// Iso orthographic camera. Look target is the BLOCK CENTRE
		// (Y = -BlockHeight/2), not the block top — looking at the top
		// would leave the upper half of the rendered image as empty
		// padding (block extends DOWN from Y=0), pushing the visible
		// diamond to the lower 2/3 of the rect and breaking alignment
		// with 2D iso tile diamonds at the same screen position.
		var yaw = Mathf.DegToRad(CameraYawDegrees);
		var pitch = Mathf.DegToRad(CameraPitchDegrees);
		var cosP = Mathf.Cos(pitch);
		// Camera offset is fixed in iso-direction; lookTarget chooses
		// where in the 3D scene the rendered image is centered.
		var lookTarget = new Vector3(0f, -BlockHeight * 0.5f, 0f);
		var dirFromTarget = new Vector3(
			Mathf.Sin(yaw) * cosP,
			Mathf.Sin(pitch),
			Mathf.Cos(yaw) * cosP);
		var pos = lookTarget + dirFromTarget * CameraDistance;

		// Auto-fit orthographic size = exact projected vertical extent
		// of the grid (no padding). For yaw=45° + pitch P:
		//   diamond top vertical extent  = (GridX*BlockSize + GridZ*BlockSize)
		//                                  * sin(P) / sqrt(2)
		//   side face drop               = BlockHeight * cos(P)
		//   total                        = diamond + side
		// At pitch=arctan(1/sqrt(2)) ≈ 35.264° these two terms are equal
		// for a unit cube → image splits 50/50 between diamond top and
		// sides → matches game's iso tile (64 px diamond + 64 px side).
		//
		// User-supplied OrthographicSize > 0 overrides this (useful when
		// you want the 3D block to fit a specific known pixel footprint
		// regardless of aspect-fit).
		float orthoSize = OrthographicSize;
		if (orthoSize <= 0f)
		{
			float diamondHeight = (GridX * BlockSize + GridZ * BlockSize)
				* Mathf.Sin(pitch) / Mathf.Sqrt(2f);
			float sideDrop = BlockHeight * Mathf.Cos(pitch);
			orthoSize = diamondHeight + sideDrop;
		}

		var cam = new Camera3D
		{
			Name = "IsoCamera",
			Current = true,
			Projection = Camera3D.ProjectionType.Orthogonal,
			Size = orthoSize,
			Position = pos,
			Near = 0.05f,
			Far = CameraDistance * 4f + 20f,
		};
		cam.LookAtFromPosition(pos, lookTarget, Vector3.Up);
		return cam;
	}

	private Node3D CreateWaterGrid()
	{
		var root = new Node3D { Name = "WaterGrid" };
		_topMaterial = CreateSharedMaterial(surfaceMode: 0);
		_sideMaterial = CreateSharedMaterial(surfaceMode: 1);

		for (var gx = 0; gx < GridX; gx++)
		{
			for (var gz = 0; gz < GridZ; gz++)
			{
				var offset = new Vector3(
					(gx - (GridX - 1) * 0.5f) * BlockSize,
					0.0f,
					(gz - (GridZ - 1) * 0.5f) * BlockSize);
				var block = BuildBlock($"Block_{gx}_{gz}", offset, gx, gz);
				root.AddChild(block);
			}
		}
		return root;
	}

	private Node3D BuildBlock(string name, Vector3 offset, int gx, int gz)
	{
		var node = new Node3D { Name = name, Position = offset };

		// Top face — always rendered.
		node.AddChild(new MeshInstance3D
		{
			Name = "Top",
			Mesh = new PlaneMesh
			{
				Size = new Vector2(BlockSize, BlockSize),
				SubdivideWidth = TopSubdivisions,
				SubdivideDepth = TopSubdivisions,
			},
			Position = Vector3.Zero,
			MaterialOverride = _topMaterial,
		});

		// Only the two iso-visible outer sides:
		//   • +X outer edge  (gx == GridX-1)  → "SideRight"  (camera sees)
		//   • +Z outer edge  (gz == GridZ-1)  → "SideFront"  (camera sees)
		//
		// With yaw=45° the camera sits in the (+X, +Z) quadrant, so the
		// two quad faces facing +X and +Z are always visible. Back sides
		// (-X, -Z edges) face away and are dropped — that's the "两个侧面"
		// simplification the subview does vs the preview.
		if (gz == GridZ - 1)
			AddSide(node, "SideFront", BlockSize,
				new Vector3(0, -BlockHeight * 0.5f, BlockSize * 0.5f),
				new Vector3(90, 0, 0));
		if (gx == GridX - 1)
			AddSide(node, "SideRight", BlockSize,
				new Vector3(BlockSize * 0.5f, -BlockHeight * 0.5f, 0),
				new Vector3(90, -90, 0));

		return node;
	}

	private void AddSide(Node3D parent, string name, float width, Vector3 position, Vector3 rotationDegrees)
	{
		var mesh = new PlaneMesh
		{
			Size = new Vector2(width, BlockHeight),
			SubdivideWidth = SideSubdivisionsH,
			SubdivideDepth = SideSubdivisionsV,
		};
		parent.AddChild(new MeshInstance3D
		{
			Name = name,
			Mesh = mesh,
			Position = position,
			RotationDegrees = rotationDegrees,
			MaterialOverride = _sideMaterial,
		});
	}

	private ShaderMaterial CreateSharedMaterial(int surfaceMode)
	{
		var shader = GD.Load<Shader>("res://Assets/Shaders/painterly_water_block.gdshader");
		if (shader == null)
			GD.PushError("[WaterPainterlyBlockSubview] Failed to load painterly_water_block.gdshader — open the project in the editor once so the .import file is generated.");

		var mat = new ShaderMaterial { Shader = shader };
		mat.SetShaderParameter("surface_mode", surfaceMode);
		mat.SetShaderParameter("speed", 1.0f);
		mat.SetShaderParameter("flow_dir", new Vector2(1.0f, 0.3f));
		mat.SetShaderParameter("wave_scale", 2.0f);
		mat.SetShaderParameter("wave_amp", 1.0f);
		mat.SetShaderParameter("flow_advect", 0.4f);
		mat.SetShaderParameter("displace_strength", 0.16f);
		mat.SetShaderParameter("normal_strength", 3.4f);

		mat.SetShaderParameter("shallow_color", new Color(0.34f, 0.46f, 0.44f));
		mat.SetShaderParameter("deep_color",    new Color(0.05f, 0.10f, 0.12f));
		mat.SetShaderParameter("foam_color",    new Color(0.90f, 0.92f, 0.88f));
		mat.SetShaderParameter("floor_color",   new Color(0.34f, 0.32f, 0.22f));
		mat.SetShaderParameter("sun_color",     _sunColor);
		mat.SetShaderParameter("sky_color",     new Color(0.42f, 0.55f, 0.62f));

		mat.SetShaderParameter("sun_dir", _sunDir);
		mat.SetShaderParameter("specular_power", 48.0f);
		mat.SetShaderParameter("specular_gain",  1.4f);

		mat.SetShaderParameter("foam_gain",         1.4f);
		mat.SetShaderParameter("foam_threshold",    0.38f);
		mat.SetShaderParameter("posterize_steps",   7.0f);
		mat.SetShaderParameter("posterize_strength", 0.25f);

		mat.SetShaderParameter("block_height",     BlockHeight);
		mat.SetShaderParameter("beam_intensity",   1.15f);
		mat.SetShaderParameter("beam_contrast",    4.2f);
		mat.SetShaderParameter("fresnel_strength", 0.28f);
		mat.SetShaderParameter("sparkle_gain",     0.9f);
		mat.SetShaderParameter("floor_fade",       0.55f);
		mat.SetShaderParameter("waterline_glow",   0.7f);

		mat.SetShaderParameter("ripple_speed",         2.8f);
		mat.SetShaderParameter("ripple_width",         2.0f);
		mat.SetShaderParameter("ripple_wavenumber",   14.0f);
		mat.SetShaderParameter("ripple_gate",          0.12f);
		mat.SetShaderParameter("ripple_damp",          0.6f);
		mat.SetShaderParameter("ripple_lifetime",      _rippleLifetime);
		mat.SetShaderParameter("ripple_crater_depth",  2.0f);
		mat.SetShaderParameter("ripple_crater_radius", 0.18f);
		mat.SetShaderParameter("ripple_crater_life",   0.15f);
		mat.SetShaderParameter("ripple_emit_ramp",     0.08f);
		mat.SetShaderParameter("ripple_flow_coupling", 1.0f);
		mat.SetShaderParameter("ripple_foam_burst",    1.2f);
		mat.SetShaderParameter("ripple_wake_speed",    0.30f);
		mat.SetShaderParameter("ripple_wake_lifetime", _wakeRippleLifetime);
		return mat;
	}

	private void SetupFluidLayer()
	{
		_fluidViewport = new SubViewport
		{
			Name = "FluidDensityViewport",
			Size = _viewport.Size,
			TransparentBg = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
			RenderTargetClearMode = SubViewport.ClearMode.Always,
			Disable3D = false,
			HandleInputLocally = false,
		};
		_viewport.AddChild(_fluidViewport);

		// Keep inner density viewport in sync with outer water viewport
		// so the composite ColorRect samples from a matching-resolution
		// texture. Without this, screen-space gradient normals at the
		// composite stage land at the wrong pixels.
		_viewport.SizeChanged += () =>
		{
			if (_fluidViewport != null && IsInstanceValid(_fluidViewport))
				_fluidViewport.Size = _viewport.Size;
		};

		_fluidCamera = new Camera3D
		{
			Name = "FluidCamera",
			Current = true,
			Projection = _camera.Projection,
			Size = _camera.Size,
		};
		_fluidCamera.GlobalTransform = _camera.GlobalTransform;
		_fluidViewport.AddChild(_fluidCamera);

		_sharedDensityMaterial = new ShaderMaterial { Shader = DensityShader };
		_sharedDensityMaterial.SetShaderParameter("blob_strength", 0.55f);

		// CanvasLayer inside the outer SubViewport renders above the 3D
		// scene but *within* this subview's output — not on top of the
		// game's main viewport. This is what keeps the fluid composite
		// scoped to the water tile instead of plastering over the HUD.
		var canvas = new CanvasLayer { Name = "FluidComposite", Layer = 1 };
		_viewport.AddChild(canvas);

		_fluidCompositeMaterial = new ShaderMaterial { Shader = FluidCompositeShader };
		_fluidCompositeMaterial.SetShaderParameter("density_texture", _fluidViewport.GetTexture());
		_fluidCompositeMaterial.SetShaderParameter("sun_dir",           _sunDir);
		_fluidCompositeMaterial.SetShaderParameter("sun_color",         _sunColor);
		_fluidCompositeMaterial.SetShaderParameter("env_sky_color",     _envSkyColor);
		_fluidCompositeMaterial.SetShaderParameter("env_horizon_color", _envHorizonColor);
		_fluidCompositeMaterial.SetShaderParameter("env_ground_color",  _envGroundColor);

		_fluidRect = new ColorRect
		{
			Name = "FluidRect",
			AnchorRight = 1f,
			AnchorBottom = 1f,
			MouseFilter = MouseFilterEnum.Ignore,
			Color = new Color(0, 0, 0, 0),
			Material = _fluidCompositeMaterial,
		};
		canvas.AddChild(_fluidRect);
	}

	private void SyncFluidCamera()
	{
		if (_fluidCamera == null) return;
		_fluidCamera.GlobalTransform = _camera.GlobalTransform;
		_fluidCamera.Size = _camera.Size;
	}

	private void PushTimeNow()
	{
		_topMaterial?.SetShaderParameter("time_now", _timeNow);
		_sideMaterial?.SetShaderParameter("time_now", _timeNow);
	}

	private void PushRipples()
	{
		// Mixed lifetime cleanup — wake ripples (negative amp) expire on
		// wake_lifetime, click/drop ripples on the global ripple_lifetime.
		_ripples.RemoveAll(r =>
		{
			float life = r.Amp < 0f ? _wakeRippleLifetime : _rippleLifetime;
			return _timeNow - r.TStart > life;
		});

		for (int i = 0; i < MaxRipples; i++)
		{
			if (i < _ripples.Count)
			{
				var r = _ripples[i];
				_packedRipples[i] = new Vector4(r.WorldXZ.X, r.WorldXZ.Y, r.TStart, r.Amp);
			}
			else
			{
				_packedRipples[i] = Vector4.Zero;
			}
		}

		var arr = new Godot.Collections.Array();
		foreach (var v in _packedRipples) arr.Add(v);
		_topMaterial.SetShaderParameter("ripples", arr);
		_sideMaterial.SetShaderParameter("ripples", arr);
	}

	// --- Droplets --------------------------------------------------------
	// Phase-staged spawn — same schedule as the preview. See preview for
	// the full reference + high-speed photography justification.
	private void SpawnDroplets(Vector2 worldXZ, float amp, int _unused)
	{
		SpawnAerosolSheet(worldXZ, amp);
		SpawnCrownSheet(worldXZ, amp);
		Schedule(0.04f, () => SpawnSprayDroplets(worldXZ, amp));
		Schedule(0.06f, () => SpawnCrownRingDroplets(worldXZ, amp));
		float jetDelay = 0.18f + amp * 0.06f;
		Schedule(jetDelay, () => SpawnJetSheet(worldXZ, amp));
		Schedule(jetDelay + 0.04f, () => SpawnWorthingtonBeads(worldXZ, amp));
	}

	private void SpawnCrownSheet(Vector2 worldXZ, float amp) =>
		SpawnSheet(mode: 0,
			origin: new Vector3(worldXZ.X, 0.005f, worldXZ.Y),
			radiusBase: 0.08f + amp * 0.18f,
			radiusGrowth: 1.4f + amp * 3.2f,
			heightMax: 0.18f + amp * 0.55f,
			maxLife: 0.32f + amp * 0.25f,
			tint: new Color(0.93f, 0.97f, 1.0f, 0.78f));

	private void SpawnJetSheet(Vector2 worldXZ, float amp) =>
		SpawnSheet(mode: 1,
			origin: new Vector3(worldXZ.X, 0.005f, worldXZ.Y),
			radiusBase: 0.040f + amp * 0.060f,
			radiusGrowth: 0.0f,
			heightMax: 0.45f + amp * 1.45f,
			maxLife: 0.55f + amp * 0.45f,
			tint: new Color(0.95f, 0.97f, 1.0f, 0.85f));

	private void SpawnAerosolSheet(Vector2 worldXZ, float amp) =>
		SpawnSheet(mode: 2,
			origin: new Vector3(worldXZ.X, 0.01f, worldXZ.Y),
			radiusBase: 0.05f + amp * 0.10f,
			radiusGrowth: 1.2f + amp * 2.4f,
			heightMax: 0.0f,
			maxLife: 0.22f + amp * 0.10f,
			tint: new Color(0.94f, 0.96f, 0.98f, 0.32f));

	private void SpawnWorthingtonBeads(Vector2 worldXZ, float amp)
	{
		int beadCount = Mathf.Clamp((int)(amp / 0.05f) + 2, 2, 5);
		float jetBaseSpeed = 4.5f + amp * 22.0f;
		float jetBaseRadius = 0.038f + amp * 0.13f;
		for (int i = 0; i < beadCount; i++)
		{
			float ti = beadCount > 1 ? (float)i / (beadCount - 1) : 0f;
			SpawnDropletNode(
				origin: new Vector3(worldXZ.X, 0.04f + ti * 0.32f, worldXZ.Y),
				velocity: new Vector3(0f, jetBaseSpeed * (1.0f - ti * 0.40f), 0f),
				baseRadius: jetBaseRadius * (1.0f - ti * 0.45f),
				maxLife: Mathf.Max(0.45f, 0.9f + amp * 1.4f - ti * 0.20f),
				drag: 0.0f,
				stretchGain: 0.16f + ti * 0.06f);
		}
	}

	private void SpawnCrownRingDroplets(Vector2 worldXZ, float amp)
	{
		int crownN = Mathf.Clamp((int)(amp / 0.03f) + 4, 4, 10);
		float crownR = 0.08f + amp * 0.18f;
		for (int i = 0; i < crownN; i++)
		{
			float jitter = ((float)_rng.NextDouble() - 0.5f) * 0.4f;
			float angle = (i + 0.5f) / crownN * Mathf.Tau + jitter;
			float outSpd = 1.8f + amp * 14.0f + (float)_rng.NextDouble() * 1.5f;
			float upSpd = 0.6f + amp * 4.0f;
			SpawnDropletNode(
				origin: new Vector3(
					worldXZ.X + Mathf.Cos(angle) * crownR,
					0.02f,
					worldXZ.Y + Mathf.Sin(angle) * crownR),
				velocity: new Vector3(Mathf.Cos(angle) * outSpd, upSpd, Mathf.Sin(angle) * outSpd),
				baseRadius: 0.018f + amp * 0.06f,
				maxLife: 0.5f + amp * 0.8f,
				drag: 1.4f,
				stretchGain: 0.22f);
		}
	}

	private void SpawnSprayDroplets(Vector2 worldXZ, float amp)
	{
		int sprayN = Mathf.Clamp((int)(amp / 0.04f) + 4, 6, 16);
		for (int i = 0; i < sprayN; i++)
		{
			float az = (float)(_rng.NextDouble() * Mathf.Tau);
			float phi = Mathf.Sqrt((float)_rng.NextDouble()) * Mathf.Pi * 0.42f;
			float up = Mathf.Cos(phi);
			float outR = Mathf.Sin(phi);
			float speed = 2.2f + amp * 16.0f * (0.55f + (float)_rng.NextDouble() * 0.9f);
			var vel = new Vector3(Mathf.Cos(az) * outR * speed, up * speed, Mathf.Sin(az) * outR * speed);
			SpawnDropletNode(
				origin: new Vector3(worldXZ.X, 0.04f, worldXZ.Y),
				velocity: vel,
				baseRadius: 0.028f + amp * 0.14f * (float)_rng.NextDouble(),
				maxLife: 0.8f + amp * 1.8f,
				drag: 0.5f,
				stretchGain: 0.10f);
		}
	}

	private void SpawnMicroCrown(Vector2 worldXZ, float amp) =>
		SpawnSheet(mode: 0,
			origin: new Vector3(worldXZ.X, 0.005f, worldXZ.Y),
			radiusBase: 0.025f + amp * 0.30f,
			radiusGrowth: 0.6f + amp * 1.20f,
			heightMax: 0.05f + amp * 0.30f,
			maxLife: 0.18f + amp * 0.15f,
			tint: new Color(0.95f, 0.97f, 1.0f, 0.70f));

	private void Schedule(float delay, Action action)
	{
		if (delay <= 0f) { action(); return; }
		_pendingSpawns.Add(new PendingSpawn { Delay = delay, Action = action });
	}

	private void StepPendingSpawns(float delta)
	{
		for (int i = _pendingSpawns.Count - 1; i >= 0; i--)
		{
			var ps = _pendingSpawns[i];
			ps.Delay -= delta;
			if (ps.Delay <= 0f)
			{
				ps.Action();
				_pendingSpawns.RemoveAt(i);
			}
			else
			{
				_pendingSpawns[i] = ps;
			}
		}
	}

	private void SpawnDropletNode(Vector3 origin, Vector3 velocity, float baseRadius,
		float maxLife, float drag, float stretchGain, bool isTail = false)
	{
		float sizeJit = 0.85f + (float)_rng.NextDouble() * 0.30f;
		float jitterRadius = baseRadius * sizeJit;
		float swayAngle = (float)(_rng.NextDouble() * Mathf.Tau);

		var node = new MeshInstance3D
		{
			Mesh = _dropletMesh,
			MaterialOverride = _sharedDensityMaterial,
			Position = origin,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		_fluidViewport.AddChild(node);
		_droplets.Add(new Droplet
		{
			Node = node,
			Velocity = velocity,
			Life = 0f,
			MaxLife = maxLife,
			BaseRadius = jitterRadius,
			DragPerSec = drag,
			StretchGain = stretchGain,
			WobbleDir = new Vector2(Mathf.Cos(swayAngle), Mathf.Sin(swayAngle)),
			WobblePhase = (float)(_rng.NextDouble() * Mathf.Tau),
			IsTail = isTail,
		});

		// Capillary-breakup tail: 40ms-delayed smaller companion so the
		// metaball composite reads fast droplets as streaks instead of
		// isolated beads. IsTail=true blocks recursive tails & landing
		// mini-crowns so these can't cascade-spawn forever.
		if (!isTail && velocity.Length() > 5.0f)
		{
			Vector3 tailOrigin = origin - velocity * 0.04f;
			SpawnDropletNode(
				origin: tailOrigin,
				velocity: velocity * 0.85f,
				baseRadius: jitterRadius * 0.45f,
				maxLife: 0.10f,
				drag: drag,
				stretchGain: stretchGain * 0.5f,
				isTail: true);
		}
	}

	private void StepDroplets(float delta)
	{
		const float gravity = 12.0f;
		const float wobbleAccel = 0.4f;
		for (int i = _droplets.Count - 1; i >= 0; i--)
		{
			var d = _droplets[i];
			d.Velocity.Y -= gravity * delta;
			if (d.DragPerSec > 0f)
			{
				float decay = Mathf.Pow(Mathf.Max(0.01f, 1.0f - d.DragPerSec), delta);
				d.Velocity.X *= decay;
				d.Velocity.Z *= decay;
			}
			float swayAmp = Mathf.Sin(_timeNow * 5.0f + d.WobblePhase) * wobbleAccel * delta;
			d.Velocity.X += d.WobbleDir.X * swayAmp;
			d.Velocity.Z += d.WobbleDir.Y * swayAmp;

			var p = d.Node.Position + d.Velocity * delta;
			d.Life += delta;

			if ((p.Y <= 0.0f && d.Velocity.Y < 0f) || d.Life > d.MaxLife)
			{
				// Bead landing cascade — small secondary ripple + micro
				// crown so the player sees every bead actually affecting
				// the surface. Tails skip (cosmetic fragments only).
				if (!d.IsTail && p.Y <= 0.0f && d.Velocity.Y < -2.5f)
				{
					float impactAmp = Mathf.Clamp(-d.Velocity.Y * 0.011f, 0.012f, 0.04f);
					if (_rng.NextDouble() < 0.55)
						SpawnRippleQuiet(new Vector2(p.X, p.Z), impactAmp);
					if (impactAmp > 0.020f && _rng.NextDouble() < 0.65)
						SpawnMicroCrown(new Vector2(p.X, p.Z), impactAmp);
				}
				d.Node.QueueFree();
				_droplets.RemoveAt(i);
				continue;
			}

			float vlen = d.Velocity.Length();
			Vector3 yAxis;
			float stretch;
			if (vlen > 0.4f)
			{
				yAxis = d.Velocity / vlen;
				stretch = 1.0f + Mathf.Min(vlen * d.StretchGain, 0.6f);
			}
			else
			{
				yAxis = Vector3.Up;
				stretch = 1.0f;
			}
			Vector3 refAxis = Mathf.Abs(yAxis.Y) > 0.95f ? Vector3.Right : Vector3.Up;
			Vector3 xAxis = yAxis.Cross(refAxis).Normalized();
			Vector3 zAxis = xAxis.Cross(yAxis).Normalized();

			float lifeRatio = d.Life / d.MaxLife;
			float fade = 1.0f - Mathf.Clamp((lifeRatio - 0.7f) / 0.3f, 0f, 1f);

			float capW = 1.0f + 0.10f * Mathf.Sin(_timeNow * 22.0f + d.WobblePhase);
			float capL = 1.0f / Mathf.Max(0.5f, capW);

			float width = d.BaseRadius * 2.0f * fade * capW;
			float length = d.BaseRadius * 2.0f * stretch * fade * capL;
			var basis = new Basis(xAxis * width, yAxis * length, zAxis * width);
			d.Node.Transform = new Transform3D(basis, p);
		}
	}

	// --- Sheets ----------------------------------------------------------
	private void SpawnSheet(int mode, Vector3 origin, float radiusBase,
		float radiusGrowth, float heightMax, float maxLife, Color tint)
	{
		var mat = new ShaderMaterial { Shader = SheetShader };
		mat.SetShaderParameter("mode", mode);
		mat.SetShaderParameter("age", 0.0f);
		mat.SetShaderParameter("max_life", maxLife);
		mat.SetShaderParameter("radius_base", radiusBase);
		mat.SetShaderParameter("radius_growth", radiusGrowth);
		mat.SetShaderParameter("height_max", heightMax);
		mat.SetShaderParameter("tint", tint);
		mat.SetShaderParameter("seed", (float)_rng.NextDouble());
		mat.SetShaderParameter("sun_dir", _sunDir);
		mat.SetShaderParameter("sun_color", _sunColor);
		mat.SetShaderParameter("env_sky_color", _envSkyColor);
		mat.SetShaderParameter("env_horizon_color", _envHorizonColor);
		mat.SetShaderParameter("env_ground_color", _envGroundColor);
		var node = new MeshInstance3D
		{
			Mesh = _sheetMesh,
			MaterialOverride = mat,
			Position = origin,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		_viewport.AddChild(node);
		_sheets.Add(new Sheet { Node = node, Mat = mat, Age = 0f, MaxLife = maxLife });
	}

	private void StepSheets(float delta)
	{
		for (int i = _sheets.Count - 1; i >= 0; i--)
		{
			var s = _sheets[i];
			s.Age += delta;
			if (s.Age >= s.MaxLife)
			{
				s.Node.QueueFree();
				s.Mat.Dispose();
				_sheets.RemoveAt(i);
				continue;
			}
			s.Mat.SetShaderParameter("age", s.Age);
		}
	}
}
