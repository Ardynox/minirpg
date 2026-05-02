using System;
using System.Collections.Generic;
using Godot;

// Standalone preview for the painterly 2.5D water block shader.
// Shows a 2x2 grid of identical water blocks sharing a single material,
// proving that the world-space wave field stitches seamlessly across
// neighboring tiles (which is how the game actually places water).
//
// Animation is driven by a C#-pumped `time_now` uniform so we can pause /
// scrub time from the debug panel.
public partial class WaterPainterlyBlockPreview : Node3D
{
	private const float BlockSize = 2.4f;
	private const float BlockHeight = 1.3f;
	private const int GridX = 2;
	private const int GridZ = 2;
	private const int TopSubdivisions = 64;
	private const int SideSubdivisionsH = 64;
	private const int SideSubdivisionsV = 24;

	private ShaderMaterial _topMaterial = null!;
	private ShaderMaterial _sideMaterial = null!;

	// --- Debug / runtime state ---
	private Camera3D _camera = null!;
	private Vector3 _cameraBasePos;
	private Vector3 _cameraLookAt = new(0f, -0.2f, 0f);
	private Label _fpsLabel = null!;
	private float _timeNow;
	private float _orbitAngle;
	private bool _paused;
	private bool _orbiting;
	// Registered-row metadata used by Dump / Reset buttons. Populated by
	// AddFloatRow / AddVec2Row / AddColorRow as they construct the panel.
	private readonly List<Action> _resetActions = new();
	private readonly List<(string param, Func<string> valueToCode)> _dumpEntries = new();

	// --- Ripple demo state ---
	private struct Ripple { public Vector2 WorldXZ; public float TStart; public float Amp; }
	// Was 16 originally. Bumped to 64 so a continuously-moving emitter
	// (sailing boat) can shed a fresh ripple every ~0.05s for the full
	// 1.5s lifetime without evicting older slots — otherwise the wake
	// reads as discrete pulses ("一下一下点过") instead of a continuous
	// trail. Must stay in sync with `ripples[64]` in painterly_water_block.gdshader.
	private const int MaxRipples = 64;
	private readonly List<Ripple> _ripples = new();
	private readonly Vector4[] _packedRipples = new Vector4[MaxRipples];
	private float _rippleAmp = 0.18f;
	private float _rippleLifetime = 1.5f;
	// Wake ripples (boat trail) live shorter and expand slower than click
	// ripples — this is what makes the Kelvin V-wake visible (V-angle =
	// arcsin(wake_speed / boat_speed)). Both values must match the shader
	// uniforms `ripple_wake_lifetime` and `ripple_wake_speed`.
	private float _wakeRippleLifetime = 0.70f;
	private float _wakeRippleSpeed    = 0.30f;
	private Label _rippleCountLbl = null!;
	// Visualized "object entering water" node — removed after splash.
	private MeshInstance3D? _dropObject;
	private float _dropVelY;

	// --- Droplet splash particles ---
	// Tiny CPU-side particle system fired on ripple spawn. Three populations:
	//   jet   — 2-5 stacked Worthington bead chain (milk-drop look)
	//   crown — ~6-10 low, tight ring skimming the water surface outward
	//   spray — ~6-16 main-burst droplets, cosine-hemisphere weighted upward
	// Each droplet is a unit SphereMesh (shared) transformed per-frame: basis
	// aligned to velocity with non-uniform scale along velocity = "stretched
	// teardrop" look. Alpha fades in the last 30% of life.
	//
	// References:
	//   • Worthington bead chain — borrowed from milk-drop high-speed
	//     photography (Edgerton 1937) and the way SPH demos like Shadertoy
	//     `dscfRf` resolve the central pillar as a stack of merging spheres.
	//   • Crown ring footprint (in the shader) — the surface counterpart of
	//     this CPU ring; mimics the "impact wreath" you see in stylized
	//     water shaders (Sea of Stars / Tunic style).
	private sealed class Droplet
	{
		public MeshInstance3D Node = null!;
		public ShaderMaterial Mat = null!;
		public Vector3 Velocity;
		public float Life;
		public float MaxLife;
		public float BaseRadius;
		public Color BaseColor;
		public float DragPerSec;   // horizontal air drag (0..1, e.g. 0.5 = half/sec)
		public float StretchGain;  // per-(m/s) stretch factor
		// --- Per-droplet randomness ---
		// WobbleDir: unit XZ direction the droplet sways toward when WobblePhase
		// hits sin = +1. Fixed at spawn, so each bead has its own quirk.
		// WobblePhase: phase offset for the lateral sway sin so the whole
		// population doesn't oscillate in unison.
		public Vector2 WobbleDir;
		public float WobblePhase;
		// Capillary breakup "tail" companion droplet that follows behind a
		// fast-moving primary. Tails skip secondary effects (no bead-impact
		// crown, no further tails) so they can't cascade-spawn forever.
		public bool IsTail;
	}
	private readonly List<Droplet> _droplets = new();
	private static readonly Random _rng = new();

	// --- Pending spawn queue ---
	// Real splashes happen in PHASES (impact → crown rise → crater collapse
	// → jet emerges → bead chain pinches off), not all at once. Items
	// scheduled here wait their delay then fire their action. Cleaner than
	// scattering N timers across _Process.
	private struct PendingSpawn
	{
		public float Delay;
		public Action Action;
	}
	private readonly List<PendingSpawn> _pendingSpawns = new();
	// Shared low-poly sphere — every droplet is a true 3D ellipsoid in the
	// scene, scaled non-uniformly by the velocity-aligned basis to fake
	// motion stretch. ~100 triangles per droplet at this resolution; with
	// the per-droplet glass shader doing fresnel + refraction + Phong the
	// dominant cost is fragment shading, not geometry.
	private static readonly SphereMesh _dropletMesh = new()
	{
		Radius = 0.5f,
		Height = 1.0f,
		RadialSegments = 12,
		Rings = 6
	};
	// Sun direction shared between droplet & sheet shaders. Matches the
	// scene's DirectionalLight orientation so droplet sun specular and
	// the water-block sun spec line up.
	private static readonly Vector3 _sunDir = new(-0.6f, 0.75f, 0.3f);
	private static readonly Color _sunColor = new(1.25f, 1.10f, 0.85f);
	// Procedural environment colors — sampled by reflection direction in
	// both droplet and sheet shaders (the Paint Streams "reflect into
	// world" trick, Shadertoy WtfyDj). Drives the COLOR of fresnel rims:
	// reflections pointing up grab sky, sideways grab warm horizon,
	// down grab the ground plane gray.
	private static readonly Color _envSkyColor     = new(0.62f, 0.78f, 0.95f);
	private static readonly Color _envHorizonColor = new(0.85f, 0.78f, 0.65f);
	private static readonly Color _envGroundColor  = new(0.32f, 0.30f, 0.26f);
	// --- Fluid pipeline (黏着水 / metaball rendering) ---
	// Replaces the per-droplet glass shader with a 2-pass system:
	//   pass 1 — each droplet is a Gaussian density blob in a SubViewport,
	//            additively blended → density texture
	//   pass 2 — fullscreen ColorRect samples that density, computes
	//            gradient → screen-space normal → unified fluid surface
	//            (fresnel + env reflection + sun spec + back-buffer
	//            refraction). When droplets overlap, their densities add
	//            and the composite renders ONE merged blob — true metaball
	//            behaviour, what makes water look like real water.
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
	// Single shared density material — every droplet's MeshInstance3D
	// uses this (since they ALL output the same Gaussian density blob,
	// no per-droplet state to differentiate). Saves N material allocations.
	private ShaderMaterial _sharedDensityMaterial = null!;

	private SubViewport _fluidViewport = null!;
	private Camera3D _fluidCamera = null!;
	private ColorRect _fluidRect = null!;
	private ShaderMaterial _fluidCompositeMaterial = null!;

	// --- Splash SHEETS ---
	// The continuous-membrane forms (crown wreath, jet column, aerosol
	// mist) that exist for ~150-500ms per impact. Without these the splash
	// is just discrete particles with nothing to anchor them to the water.
	// Each sheet is a CylinderMesh side wall (no caps) reshaped per-mode in
	// the vertex shader of painterly_splash_sheet.gdshader.
	private sealed class Sheet
	{
		public MeshInstance3D Node = null!;
		public ShaderMaterial Mat = null!;
		public float Age;
		public float MaxLife;
	}
	private readonly List<Sheet> _sheets = new();
	// Shared open cylinder side-wall mesh. 32 radial segments give visible
	// crown-tooth count of ~7 (matches the shader's teeth_freq); 4 vertical
	// rings let the vertex-shape transform interpolate smoothly.
	private static readonly CylinderMesh _sheetMesh = new()
	{
		TopRadius = 1.0f,
		BottomRadius = 1.0f,
		Height = 1.0f,
		RadialSegments = 32,
		Rings = 4,
		CapTop = false,
		CapBottom = false
	};
	private static Shader? _sheetShaderCache;
	private static Shader SheetShader => _sheetShaderCache ??=
		GD.Load<Shader>("res://Assets/Shaders/painterly_splash_sheet.gdshader")
		?? throw new InvalidOperationException(
			"painterly_splash_sheet.gdshader missing — open project in editor once to import.");

	// --- Sailing boat ---
	// A small wooden boat that wanders back and forth across the pool.
	// Two-layer node: _boatYaw owns position + heading (yaw 0/180), and
	// _boatBob owns wave-following bob/pitch/roll. Splitting them keeps the
	// heading reversal at the edge from also flipping the per-frame wobble.
	//
	// The boat's "wake" reuses the existing ripple system: a tiny ripple
	// (no droplets) is dropped just behind the stern at a high cadence
	// (~0.05s). One ripple per tick — each one self-expands into a small
	// ring while the boat keeps laying down new ones, which composites
	// into a continuous trail rather than discrete "pulses". The MaxRipples
	// pool was bumped to 64 specifically so this densely-packed wake
	// doesn't evict every older click ripple.
	private Node3D? _boatYaw;
	private Node3D? _boatBob;
	private bool _boatEnabled = true;
	private float _boatSpeed = 0.55f;
	private float _boatDirection = 1f;       // +1 sails toward +X, -1 toward -X
	private float _boatPauseTimer;            // small pause when reversing
	private float _boatTrailInterval = 0.08f; // seconds between wake pulses (each pulse = 2N hull ripples + maybe 1 stern)
	private float _boatHullAmp = 0.014f;      // amp for each hull-line sample (per port/starboard slot)
	private float _boatSternAmp = 0.018f;     // amp for central stern ripple (every 2nd tick, fills the V apex)
	private float _boatHullSpread = 0.20f;    // ±Z offset of the hull-line samples (boat half-beam = 0.16)
	private int _boatHullSamples = 3;          // hull-line samples per side (bow + amidships + stern by default)
	private bool _boatTrailToggle;            // halves the stern shed rate (avoid blowing the 64-slot budget)
	private float _boatTrailTimer;
	private float _boatBobPhase;
	private static ArrayMesh? _boatHullMeshCache;

	public override void _Ready()
	{
		AddChild(CreateEnvironment());
		_camera = CreateCamera();
		_cameraBasePos = _camera.Position;
		AddChild(_camera);
		AddChild(CreateSunLight());
		AddChild(CreateFillLight());
		AddChild(CreateGround());
		AddChild(CreateWaterGrid());
		SetupFluidLayer();
		CreateBoat();
		AddChild(BuildTuningPanel());
		PushTimeNow();
	}

	// --- Fluid layer wiring ---
	// Builds the screen-space fluid pipeline: a SubViewport that hosts
	// every droplet (rendering them as additive Gaussian density blobs)
	// + a fullscreen ColorRect on a CanvasLayer above the 3D scene that
	// samples the density texture and renders the unified fluid surface.
	private void SetupFluidLayer()
	{
		// Match the viewport to current screen size; adjust on size_changed.
		var screenSize = GetViewport().GetVisibleRect().Size;
		_fluidViewport = new SubViewport
		{
			Name = "FluidViewport",
			Size = (Vector2I)screenSize,
			TransparentBg = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
			RenderTargetClearMode = SubViewport.ClearMode.Always,
			// Disable HDR / glow so density values stay linear 0..N.
			Disable3D = false,
			HandleInputLocally = false
		};
		AddChild(_fluidViewport);

		// Camera in the fluid viewport — synced every frame to the main
		// preview camera so droplet positions match between buffers.
		_fluidCamera = new Camera3D
		{
			Name = "FluidCamera",
			Current = true,
			Projection = _camera.Projection,
			Size = _camera.Size
		};
		_fluidCamera.GlobalTransform = _camera.GlobalTransform;
		_fluidViewport.AddChild(_fluidCamera);

		// Shared density material — 1 instance, every droplet's mesh
		// references it. Density-only output, no per-droplet uniforms.
		_sharedDensityMaterial = new ShaderMaterial { Shader = DensityShader };
		_sharedDensityMaterial.SetShaderParameter("blob_strength", 0.55f);

		// Composite layer — CanvasLayer above 3D, ColorRect filling screen.
		var canvas = new CanvasLayer { Name = "FluidComposite", Layer = 1 };
		AddChild(canvas);

		_fluidCompositeMaterial = new ShaderMaterial { Shader = FluidCompositeShader };
		_fluidCompositeMaterial.SetShaderParameter("density_texture", _fluidViewport.GetTexture());
		_fluidCompositeMaterial.SetShaderParameter("sun_dir", _sunDir);
		_fluidCompositeMaterial.SetShaderParameter("sun_color", _sunColor);
		_fluidCompositeMaterial.SetShaderParameter("env_sky_color", _envSkyColor);
		_fluidCompositeMaterial.SetShaderParameter("env_horizon_color", _envHorizonColor);
		_fluidCompositeMaterial.SetShaderParameter("env_ground_color", _envGroundColor);

		_fluidRect = new ColorRect
		{
			Name = "FluidRect",
			AnchorRight = 1f,
			AnchorBottom = 1f,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Color = new Color(0, 0, 0, 0),
			Material = _fluidCompositeMaterial
		};
		canvas.AddChild(_fluidRect);

		// Track main viewport resize → keep FluidViewport size in sync.
		GetViewport().SizeChanged += OnMainViewportResized;
	}

	private void OnMainViewportResized()
	{
		var newSize = GetViewport().GetVisibleRect().Size;
		_fluidViewport.Size = (Vector2I)newSize;
		// SubViewport's texture object stays the same Resource, no need to
		// re-push to the composite material.
	}

	// Sync FluidCamera to PreviewCamera every frame so the SubViewport
	// renders droplets from exactly the same point of view as the main
	// scene. Without this, density would land at the wrong screen pixels
	// and the composite would be off-register.
	private void SyncFluidCamera()
	{
		_fluidCamera.GlobalTransform = _camera.GlobalTransform;
		_fluidCamera.Size = _camera.Size;
	}

	public override void _Process(double delta)
	{
		if (!_paused)
		{
			_timeNow += (float)delta;
			PushTimeNow();
			StepDropObject((float)delta);
			StepPendingSpawns((float)delta);
			StepDroplets((float)delta);
			StepSheets((float)delta);
			StepBoat((float)delta);
		}

		PushRipples();
		SyncFluidCamera();

		if (_orbiting)
		{
			_orbitAngle += (float)delta * 0.25f;
			var radius = new Vector2(_cameraBasePos.X, _cameraBasePos.Z).Length();
			var x = Mathf.Cos(_orbitAngle) * radius;
			var z = Mathf.Sin(_orbitAngle) * radius;
			_camera.Position = new Vector3(x, _cameraBasePos.Y, z);
			_camera.LookAtFromPosition(_camera.Position, _cameraLookAt, Vector3.Up);
		}

		if (_fpsLabel != null)
		{
			var fps = Engine.GetFramesPerSecond();
			_fpsLabel.Text = $"FPS {fps:F0}   t={_timeNow:F2}s{(_paused ? "  (paused)" : "")}";
		}
	}

	// --- Ripples ---
	private void SpawnRipple(Vector2 worldXZ, float amp)
	{
		if (_ripples.Count >= MaxRipples) _ripples.RemoveAt(0);
		_ripples.Add(new Ripple { WorldXZ = worldXZ, TStart = _timeNow, Amp = amp });
		// Scale droplet count by amplitude — big splashes throw more water.
		int count = Mathf.Clamp((int)(amp / 0.04f) + 4, 6, 22);
		SpawnDroplets(worldXZ, amp, count);
	}

	// --- Droplet splashes ---
	// PHASE-STAGED splash. Real impact-event sequence (milk-drop / SPH refs):
	//   t=0       ─ aerosol mist + crown rim emerge instantly
	//   t≈0.04s   ─ main spray bursts outward (≈1-frame after impact)
	//   t≈0.06s   ─ crown rim teeth tear, ring drops shed horizontally
	//   t≈0.18s   ─ crater bottoms out, Worthington jet column rises
	//   t≈0.22s   ─ jet tip pinches off into airborne bead chain
	//
	// Old version spawned EVERYTHING at t=0, so the jet & beads appeared
	// before the crater finished collapsing — read backwards. The schedule
	// below puts each phase where high-speed footage actually shows it.
	//
	// amp scales count and velocity so the panel's ripple_amp slider still
	// drives total splash intensity.
	private void SpawnDroplets(Vector2 worldXZ, float amp, int _unused)
	{
		// Phase 0 (t=0): aerosol + crown rim — present from the very first frame.
		SpawnAerosolSheet(worldXZ, amp);
		SpawnCrownSheet(worldXZ, amp);

		// Phase 1 (t≈0.04s): spray bursts outward (sub-frame delay so it
		// doesn't visually merge into the impact instant).
		Schedule(0.04f, () => SpawnSprayDroplets(worldXZ, amp));

		// Phase 2 (t≈0.06s): crown rim has had ~60ms to grow; rim teeth shed.
		Schedule(0.06f, () => SpawnCrownRingDroplets(worldXZ, amp));

		// Phase 3 (t≈0.18+amp·0.06s): jet emerges from collapsing crater.
		float jetDelay = 0.18f + amp * 0.06f;
		Schedule(jetDelay, () => SpawnJetSheet(worldXZ, amp));

		// Phase 4 (t≈0.22+amp·0.06s): bead chain pinches off the jet tip.
		Schedule(jetDelay + 0.04f, () => SpawnWorthingtonBeads(worldXZ, amp));
	}

	// (S0) Crown sheet — flared expanding wreath rising from the rim of the
	// impact crater. Tooth-edged top later sheds the crown-ring drops.
	private void SpawnCrownSheet(Vector2 worldXZ, float amp) =>
		SpawnSheet(
			mode: 0,
			origin: new Vector3(worldXZ.X, 0.005f, worldXZ.Y),
			radiusBase: 0.08f + amp * 0.18f,
			radiusGrowth: 1.4f + amp * 3.2f,
			heightMax: 0.18f + amp * 0.55f,
			maxLife: 0.32f + amp * 0.25f,
			tint: new Color(0.93f, 0.97f, 1.0f, 0.78f));

	// (S1) Jet column — continuous central pillar; the airborne bead chain
	// pinches off from its tip 40ms later.
	private void SpawnJetSheet(Vector2 worldXZ, float amp) =>
		SpawnSheet(
			mode: 1,
			origin: new Vector3(worldXZ.X, 0.005f, worldXZ.Y),
			radiusBase: 0.040f + amp * 0.060f,
			radiusGrowth: 0.0f,
			heightMax: 0.45f + amp * 1.45f,
			maxLife: 0.55f + amp * 0.45f,
			tint: new Color(0.95f, 0.97f, 1.0f, 0.85f));

	// (S2) Aerosol mist — low-altitude white-blue puff dispersed at impact.
	private void SpawnAerosolSheet(Vector2 worldXZ, float amp) =>
		SpawnSheet(
			mode: 2,
			origin: new Vector3(worldXZ.X, 0.01f, worldXZ.Y),
			radiusBase: 0.05f + amp * 0.10f,
			radiusGrowth: 1.2f + amp * 2.4f,
			heightMax: 0.0f, // unused for mode 2 (vertex shader uses radius)
			maxLife: 0.22f + amp * 0.10f,
			tint: new Color(0.94f, 0.96f, 0.98f, 0.32f));

	// (1) Worthington bead chain — beads pre-stacked along Y, monotonically
	// decreasing initial velocity going up so the topmost bead pinches off
	// first (milk-drop silhouette).
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
				baseColor: new Color(0.95f, 0.97f, 1.0f, 0.95f),
				maxLife: Mathf.Max(0.45f, 0.9f + amp * 1.4f - ti * 0.20f),
				drag: 0.0f,
				stretchGain: 0.16f + ti * 0.06f);
		}
	}

	// (2) Crown ring — drops shed when the rim teeth tear. Mostly horizontal,
	// slight up-bias, very short life.
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
				baseColor: new Color(0.92f, 0.96f, 1.0f, 0.85f),
				maxLife: 0.5f + amp * 0.8f,
				drag: 1.4f,
				stretchGain: 0.22f);
		}
	}

	// (3) Main spray — cosine-weighted upper hemisphere; the big arcing
	// droplets visible in slow-mo footage.
	private void SpawnSprayDroplets(Vector2 worldXZ, float amp)
	{
		int sprayN = Mathf.Clamp((int)(amp / 0.04f) + 4, 6, 16);
		for (int i = 0; i < sprayN; i++)
		{
			float az = (float)(_rng.NextDouble() * Mathf.Tau);
			// Square-root cosine bias toward straight up: most drops near
			// vertical, a few near horizontal.
			float phi = Mathf.Sqrt((float)_rng.NextDouble()) * Mathf.Pi * 0.42f;
			float up = Mathf.Cos(phi);
			float outR = Mathf.Sin(phi);
			float speed = 2.2f + amp * 16.0f * (0.55f + (float)_rng.NextDouble() * 0.9f);
			var vel = new Vector3(Mathf.Cos(az) * outR * speed, up * speed, Mathf.Sin(az) * outR * speed);
			SpawnDropletNode(
				origin: new Vector3(worldXZ.X, 0.04f, worldXZ.Y),
				velocity: vel,
				baseRadius: 0.028f + amp * 0.14f * (float)_rng.NextDouble(),
				baseColor: _rng.NextDouble() < 0.35
					? new Color(0.95f, 0.98f, 1.0f, 0.90f)      // bright white highlight
					: new Color(0.55f, 0.72f, 0.88f, 0.72f),    // translucent glass-blue
				maxLife: 0.8f + amp * 1.8f,
				drag: 0.5f,
				stretchGain: 0.10f);
		}
	}

	// Small crown sheet spawned when an airborne bead falls back to the
	// surface. Real splashes always cascade — every bead landing kicks a
	// secondary mini-splash. We keep parameters small and feed-forward
	// (no further droplets, no further mini-crowns) so cascades terminate.
	private void SpawnMicroCrown(Vector2 worldXZ, float amp) =>
		SpawnSheet(
			mode: 0,
			origin: new Vector3(worldXZ.X, 0.005f, worldXZ.Y),
			radiusBase: 0.025f + amp * 0.30f,
			radiusGrowth: 0.6f + amp * 1.20f,
			heightMax: 0.05f + amp * 0.30f,
			maxLife: 0.18f + amp * 0.15f,
			tint: new Color(0.95f, 0.97f, 1.0f, 0.70f));

	// Schedule a delayed action. delay≤0 fires immediately so callers don't
	// have to special-case the t=0 phase.
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
		Color baseColor, float maxLife, float drag, float stretchGain, bool isTail = false)
	{
		// --- Per-droplet jitter (only size + sway now) ---
		// Color/alpha jitter is gone — every droplet contributes the same
		// kind of density blob; the composite shader gives the splash its
		// color from body_tint + reflection. Size jitter still matters
		// (it scales how much density each droplet contributes).
		float sizeJit = 0.85f + (float)_rng.NextDouble() * 0.30f;
		float jitterRadius = baseRadius * sizeJit;
		float swayAngle = (float)(_rng.NextDouble() * Mathf.Tau);

		// All droplets share ONE density material — they're all just
		// Gaussian blobs in the SubViewport's additive accumulation.
		// Reparent to _fluidViewport so they ONLY render into the density
		// buffer, NOT into the main scene.
		var node = new MeshInstance3D
		{
			Mesh = _dropletMesh,
			MaterialOverride = _sharedDensityMaterial,
			Position = origin,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
		};
		_fluidViewport.AddChild(node);
		_droplets.Add(new Droplet
		{
			Node = node,
			Mat = _sharedDensityMaterial, // shared, NOT disposed per-droplet
			Velocity = velocity,
			Life = 0f,
			MaxLife = maxLife,
			BaseRadius = jitterRadius,
			BaseColor = baseColor, // kept for compat / potential future per-droplet tinting
			DragPerSec = drag,
			StretchGain = stretchGain,
			WobbleDir = new Vector2(Mathf.Cos(swayAngle), Mathf.Sin(swayAngle)),
			WobblePhase = (float)(_rng.NextDouble() * Mathf.Tau),
			IsTail = isTail
		});

		// --- Capillary breakup tail ---
		// Real fast-moving droplets aren't isolated spheres — surface
		// tension breaks them up into a head + a faint trailing thread of
		// micro-droplets (Plateau-Rayleigh instability). One companion
		// blob 40ms behind the primary, smaller and shorter-lived, is
		// enough for the metaball composite to merge them into a streak
		// silhouette. IsTail=true so it can't recursively spawn its own
		// tail or trigger bead-impact mini-crowns.
		if (!isTail && velocity.Length() > 5.0f)
		{
			Vector3 tailOrigin = origin - velocity * 0.04f;
			SpawnDropletNode(
				origin: tailOrigin,
				velocity: velocity * 0.85f,
				baseRadius: jitterRadius * 0.45f,
				baseColor: baseColor,
				maxLife: 0.10f,
				drag: drag,
				stretchGain: stretchGain * 0.5f,
				isTail: true);
		}
	}

	private void StepDroplets(float delta)
	{
		const float gravity = 12.0f;
		// Lateral air-current turbulence amplitude. ~0.4 m/s² added in a
		// fixed-per-droplet horizontal direction, modulated by sin(time +
		// phase). For a 5-10 m/s spray it's a 4-8% nudge (subtle curve);
		// for a 1-2 m/s falling tail bead it's 20-40% (visibly drifty).
		// This is what stops every trajectory from looking like a ruler-
		// straight ballistic toss.
		const float wobbleAccel = 0.4f;
		for (int i = _droplets.Count - 1; i >= 0; i--)
		{
			var d = _droplets[i];
			// Gravity.
			d.Velocity.Y -= gravity * delta;
			// Frame-rate-independent horizontal drag (exponential decay).
			// Pow(1 - drag, delta) is the continuous-time analogue of a
			// per-second fractional velocity loss.
			if (d.DragPerSec > 0f)
			{
				float decay = Mathf.Pow(Mathf.Max(0.01f, 1.0f - d.DragPerSec), delta);
				d.Velocity.X *= decay;
				d.Velocity.Z *= decay;
			}
			// Trajectory turbulence — keeps paths from being dead-straight.
			float swayAmp = Mathf.Sin(_timeNow * 5.0f + d.WobblePhase) * wobbleAccel * delta;
			d.Velocity.X += d.WobbleDir.X * swayAmp;
			d.Velocity.Z += d.WobbleDir.Y * swayAmp;

			var p = d.Node.Position + d.Velocity * delta;
			d.Life += delta;

			// Hit water plane going down, or expired.
			if ((p.Y <= 0.0f && d.Velocity.Y < 0f) || d.Life > d.MaxLife)
			{
				// Bead landings cascade into mini-splashes — every airborne
				// droplet that hits the water at impactful speed kicks a
				// secondary ripple AND a micro crown sheet, the same way
				// real splash-on-splash photography shows. Tails are skipped
				// (they're cosmetic streak fragments, not real beads) so we
				// don't get cascading explosions.
				if (!d.IsTail && p.Y <= 0.0f && d.Velocity.Y < -2.5f)
				{
					// Energy proxy = downward speed. Clamped so a single
					// stray bead can't dominate the splash budget.
					float impactAmp = Mathf.Clamp(-d.Velocity.Y * 0.011f, 0.012f, 0.04f);
					if (_rng.NextDouble() < 0.55)
						SpawnSmallRipple(new Vector2(p.X, p.Z), impactAmp);
					if (impactAmp > 0.020f && _rng.NextDouble() < 0.65)
						SpawnMicroCrown(new Vector2(p.X, p.Z), impactAmp);
				}
				d.Node.QueueFree();
				// _sharedDensityMaterial is reused across all droplets —
				// don't dispose it on a single droplet's death.
				_droplets.RemoveAt(i);
				continue;
			}

			// Velocity-aligned 3D ellipsoid scaling. Sphere mesh is
			// rotation-invariant in shape, so we don't need a camera-facing
			// billboard — pick ANY orthonormal triad with +Y along velocity
			// and the droplet looks correct from every angle.
			//
			// Length-along-velocity stretch is capped lower than the old
			// billboard (×1.6 vs ×2.4) because a real water bead in flight
			// stays mostly spherical — extreme prolate stretch reads as
			// "cartoon motion blur" rather than "fast droplet". The shader
			// also fakes additional motion by brightening fresnel rims, so
			// we don't need geometric stretch to do all the work.
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

			// End-of-life fade now happens via SHRINKING the ellipsoid
			// (smaller mesh → smaller density contribution → drops below
			// the composite shader's density_threshold → naturally
			// invisible). This is the metaball-equivalent of alpha fade
			// and avoids per-droplet shader uniforms entirely (the shared
			// density material has none to push).
			float lifeRatio = d.Life / d.MaxLife;
			float fade = 1.0f - Mathf.Clamp((lifeRatio - 0.7f) / 0.3f, 0f, 1f);

			// --- Capillary surface-tension oscillation ---
			// Real water beads in flight pulse between prolate and oblate
			// at ~10-30 Hz (Rayleigh capillary mode). Subtle but it's what
			// keeps a rendered droplet from reading as a fixed glass blob.
			// Width and length modulate inversely so volume stays roughly
			// constant; ±10% magnitude so even tail fragments breathe a bit
			// without overwhelming the primary stretch envelope.
			float capW = 1.0f + 0.10f * Mathf.Sin(_timeNow * 22.0f + d.WobblePhase);
			float capL = 1.0f / Mathf.Max(0.5f, capW);

			// SphereMesh is unit-diameter (Radius=0.5, Height=1.0). Scale to
			// the world-space ellipsoid axes we want, modulated by fade.
			float width = d.BaseRadius * 2.0f * fade * capW;
			float length = d.BaseRadius * 2.0f * stretch * fade * capL;
			var basis = new Basis(xAxis * width, yAxis * length, zAxis * width);
			d.Node.Transform = new Transform3D(basis, p);
		}
	}

	// --- Splash sheet spawn / step ---
	// `mode` is forwarded to the shader's mode uniform (0 crown, 1 jet,
	// 2 aerosol). The shader does all shape transforms in vertex; here we
	// just push the static spawn parameters and tick `age` per frame.
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
		// Per-spawn random seed — drives the surface-noise pattern in
		// vertex stage, so two splashes never produce identical sheets.
		mat.SetShaderParameter("seed", (float)_rng.NextDouble());
		// Sun for fresnel + Phong specular. Same constants as droplets.
		mat.SetShaderParameter("sun_dir", _sunDir);
		mat.SetShaderParameter("sun_color", _sunColor);
		// Environment trio for fresnel-driven reflection (Paint Streams).
		mat.SetShaderParameter("env_sky_color", _envSkyColor);
		mat.SetShaderParameter("env_horizon_color", _envHorizonColor);
		mat.SetShaderParameter("env_ground_color", _envGroundColor);
		var node = new MeshInstance3D
		{
			Mesh = _sheetMesh,
			MaterialOverride = mat,
			Position = origin,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
		};
		AddChild(node);
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

	// Lightweight ripple spawn that doesn't cascade into more droplets —
	// used by droplets landing back on the surface.
	private void SpawnSmallRipple(Vector2 worldXZ, float amp)
	{
		if (_ripples.Count >= MaxRipples) _ripples.RemoveAt(0);
		_ripples.Add(new Ripple { WorldXZ = worldXZ, TStart = _timeNow, Amp = amp });
	}

	// Continuous-wake ripple — for emitters that drop one ripple per tick
	// (the sailing boat's stern). Encoded with a NEGATIVE amplitude so
	// `painterly_water_block.gdshader` knows to skip the per-impact
	// crater / residual / foam burst / crown / caustic flash for this
	// slot. Without that filtering the surface "remembers" each tick as
	// a tiny pulse and the trail reads as 一下一下点 (see issue history).
	// Magnitude (`amp`) still controls the ring brightness exactly the
	// same way as a click ripple.
	private void SpawnWakeRipple(Vector2 worldXZ, float amp)
	{
		if (_ripples.Count >= MaxRipples) _ripples.RemoveAt(0);
		_ripples.Add(new Ripple { WorldXZ = worldXZ, TStart = _timeNow, Amp = -Mathf.Abs(amp) });
	}

	// --- Boat ----------------------------------------------------------------
	// Spawns the persistent sailing boat at the left edge of the pool and
	// adds it to the scene. The visual is a procedurally-built hull mesh
	// (SurfaceTool) plus a cylinder mast and a thin box sail — cheap, no
	// asset dependencies, reads as a small wooden boat from the preview's
	// orthographic top-down camera.
	private void CreateBoat()
	{
		_boatYaw = new Node3D { Name = "Boat" };
		_boatBob = new Node3D { Name = "BoatBob" };
		_boatBob.AddChild(BuildBoatVisual());
		_boatYaw.AddChild(_boatBob);
		// Start near the left edge, sailing toward +X.
		float startX = -BlockSize * GridX * 0.5f + 0.55f;
		_boatYaw.Position = new Vector3(startX, 0f, 0f);
		AddChild(_boatYaw);
	}

	private static Node3D BuildBoatVisual()
	{
		var root = new Node3D { Name = "Visual" };

		var hullMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.46f, 0.30f, 0.18f),
			Roughness = 0.85f
		};
		var mastMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.30f, 0.20f, 0.12f),
			Roughness = 0.7f
		};
		var sailMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.92f, 0.88f, 0.78f),
			Roughness = 0.6f,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled
		};

		// Hull — custom mesh (water-line at y=0, draught ~4cm under bob=0).
		root.AddChild(new MeshInstance3D
		{
			Name = "Hull",
			Mesh = BuildHullMesh(),
			MaterialOverride = hullMat,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
		});

		// Mast — slender vertical pole rising from the deck centre.
		root.AddChild(new MeshInstance3D
		{
			Name = "Mast",
			Mesh = new CylinderMesh
			{
				TopRadius = 0.012f,
				BottomRadius = 0.015f,
				Height = 0.55f,
				RadialSegments = 8
			},
			Position = new Vector3(0f, 0.345f, 0f),
			MaterialOverride = mastMat,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
		});

		// Sail — thin box centred on the mast, slightly aft (looks billowed
		// in the wind even though it's just an extruded rectangle).
		root.AddChild(new MeshInstance3D
		{
			Name = "Sail",
			Mesh = new BoxMesh { Size = new Vector3(0.42f, 0.40f, 0.006f) },
			Position = new Vector3(-0.04f, 0.34f, 0f),
			MaterialOverride = sailMat,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
		});

		// Tiny pennant at the masthead — a splash of colour so the boat
		// reads as more than just brown geometry from a distance.
		var pennantMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.85f, 0.30f, 0.25f),
			Roughness = 0.5f,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled
		};
		root.AddChild(new MeshInstance3D
		{
			Name = "Pennant",
			Mesh = new BoxMesh { Size = new Vector3(0.10f, 0.04f, 0.004f) },
			Position = new Vector3(-0.05f, 0.59f, 0f),
			MaterialOverride = pennantMat,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
		});

		return root;
	}

	// Procedural hull mesh — a simple convex 11-vertex shape that reads as
	// a small rowboat / dinghy from above (pointed bow at +X, flat stern at
	// -X) and as a shallow V-keel from the side. Cached so multiple boats
	// (if added later) share one geometry resource.
	//
	// Vertex layout (deck plane at y = +0.07, keel line at y = -0.07):
	//   v0  bow tip   ( +0.45, +0.07,  0.00 )
	//   v1  fore-stbd ( +0.20, +0.07, +0.16 )
	//   v2  aft-stbd  ( -0.30, +0.07, +0.16 )
	//   v3  stern-stbd( -0.40, +0.07, +0.10 )
	//   v4  stern-port( -0.40, +0.07, -0.10 )
	//   v5  aft-port  ( -0.30, +0.07, -0.16 )
	//   v6  fore-port ( +0.20, +0.07, -0.16 )
	//   k0  keel-fore ( +0.40, -0.07,  0.00 )
	//   k1  keel-aft  ( -0.35, -0.07,  0.00 )
	private static ArrayMesh BuildHullMesh()
	{
		if (_boatHullMeshCache != null) return _boatHullMeshCache;

		var v0 = new Vector3( 0.45f,  0.07f,  0.00f);
		var v1 = new Vector3( 0.20f,  0.07f,  0.16f);
		var v2 = new Vector3(-0.30f,  0.07f,  0.16f);
		var v3 = new Vector3(-0.40f,  0.07f,  0.10f);
		var v4 = new Vector3(-0.40f,  0.07f, -0.10f);
		var v5 = new Vector3(-0.30f,  0.07f, -0.16f);
		var v6 = new Vector3( 0.20f,  0.07f, -0.16f);
		var k0 = new Vector3( 0.40f, -0.07f,  0.00f);
		var k1 = new Vector3(-0.35f, -0.07f,  0.00f);

		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);

		// Deck (top, normal +Y) — fan from the bow.
		AddTri(st, v0, v6, v5);
		AddTri(st, v0, v5, v4);
		AddTri(st, v0, v4, v3);
		AddTri(st, v0, v3, v2);
		AddTri(st, v0, v2, v1);

		// Starboard hull (normal +Z).
		AddTri(st, v0, v1, k0);
		AddTri(st, v1, v2, k0);
		AddTri(st, v2, k1, k0);
		AddTri(st, v2, v3, k1);

		// Port hull (normal -Z, mirrored winding).
		AddTri(st, v0, k0, v6);
		AddTri(st, v6, k0, v5);
		AddTri(st, v5, k0, k1);
		AddTri(st, v5, k1, v4);

		// Stern transom (normal -X).
		AddTri(st, v3, v4, k1);

		st.GenerateNormals();
		_boatHullMeshCache = st.Commit();
		return _boatHullMeshCache!;
	}

	private static void AddTri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c)
	{
		st.AddVertex(a);
		st.AddVertex(b);
		st.AddVertex(c);
	}

	private void StepBoat(float delta)
	{
		if (_boatYaw == null || !IsInstanceValid(_boatYaw)) return;
		_boatYaw.Visible = _boatEnabled;
		if (!_boatEnabled) return;

		// Forward integration. While paused (just after a reversal), the
		// boat sits in place but still bobs and steers — feels like it's
		// settling into a turn rather than abruptly snapping around.
		if (_boatPauseTimer > 0f) _boatPauseTimer -= delta;

		var pos = _boatYaw.Position;
		if (_boatPauseTimer <= 0f)
			pos.X += _boatDirection * _boatSpeed * delta;

		// Reflect at the pool walls with a short pause so the heading
		// transition reads as a deliberate turn instead of a teleport.
		float extent = BlockSize * GridX * 0.5f - 0.45f;
		if (pos.X > extent && _boatDirection > 0f)
		{
			pos.X = extent; _boatDirection = -1f; _boatPauseTimer = 0.5f;
		}
		else if (pos.X < -extent && _boatDirection < 0f)
		{
			pos.X = -extent; _boatDirection = 1f; _boatPauseTimer = 0.5f;
		}
		_boatYaw.Position = pos;

		// Smooth yaw toward the current heading. LerpAngle keeps the turn
		// going the short way around even if we ever set strange targets.
		float targetYawRad = _boatDirection > 0f ? 0f : Mathf.Pi;
		float currentYawRad = _boatYaw.Rotation.Y;
		float newYaw = Mathf.LerpAngle(currentYawRad, targetYawRad, Mathf.Min(1f, delta * 4.0f));
		var rot = _boatYaw.Rotation;
		rot.Y = newYaw;
		_boatYaw.Rotation = rot;

		// Wave-following bob + gentle pitch/roll. Three different sin
		// frequencies so the motion never repeats on a tight loop and
		// the boat feels alive even when the camera is static.
		_boatBobPhase += delta;
		float bobY  = Mathf.Sin(_boatBobPhase * 1.6f)            * 0.030f;
		float pitch = Mathf.Sin(_boatBobPhase * 1.3f + 0.5f)     * Mathf.DegToRad(3.0f);
		float roll  = Mathf.Sin(_boatBobPhase * 1.8f + 1.1f)     * Mathf.DegToRad(4.0f);
		// Push the hull DOWN ~2 cm below its mesh origin so the keel is
		// well underwater (draught ≈ 9 cm; hull half-depth is 7 cm so
		// the deck just barely clears the surface). Previously this was
		// +3 cm, which left the boat sitting on top of the water like a
		// model and the wake ripples appeared to come from below it
		// instead of being broken by the hull. Bob amplitude is small
		// enough (±3 cm) that even at the crest the keel stays wet.
		_boatBob!.Position = new Vector3(0f, -0.020f + bobY, 0f);
		_boatBob.Rotation = new Vector3(pitch, 0f, roll);

		// Wake — hull lines + stern centre + Kelvin V.
		//
		// Each tick samples N points along the boat's keel from bow to
		// stern, shedding ONE wake ripple on each side (port + starboard)
		// at every sample. As the boat keeps moving forward, the youngest
		// row — sampled at the current bow — drops back into the position
		// of the next-older row, and so on. The result is TWO continuous
		// "rails" of densely-overlapping ripple rings tracking each side
		// of the hull, exactly the "两条船身线" shape requested.
		//
		// The Kelvin V-wake behind the stern then forms automatically
		// because each wake ripple expands at ripple_wake_speed (0.30
		// m/s default — see shader) which is SLOWER than the boat's
		// 0.55 m/s. The expanding rings' tangent envelope is a V whose
		// half-angle is arcsin(wake_speed / boat_speed) ≈ 33°. The two
		// rails meet at this V. (Previously wake_speed reused the click
		// ripple's 2.8 m/s, which is faster than the boat — so each
		// ripple raced out as a full circle and there was no V at all.
		// That's why the wake "didn't merge".)
		//
		// A softer central ripple is dropped behind the stern every
		// 2nd tick to fill the V's apex so the wake doesn't read as
		// two disconnected rails.
		//
		// Capacity budget (64-slot pool, wake_lifetime 0.7s):
		//   hull = 2N / interval × wake_lifetime
		//        = 6 / 0.08 × 0.7 = 52 slots @ default
		//   stern = 0.5 / 0.08 × 0.7 = 4 slots
		//   total ≈ 56 ⇒ 8 slots free for click ripples.
		//
		// Frame-rate independent: catch up multiple ripples if delta
		// happened to be larger than the interval (e.g. dropped frame).
		if (_boatPauseTimer > 0f) return;
		_boatTrailTimer -= delta;
		// Hard guard against runaway loop if interval is tweaked to ~0
		// in the panel: cap at a few catch-up steps per frame.
		int safety = 0;
		while (_boatTrailTimer <= 0f && safety++ < 8)
		{
			float interval = Mathf.Max(_boatTrailInterval, 0.005f);
			_boatTrailTimer += interval;

			int n = Mathf.Max(_boatHullSamples, 1);
			for (int i = 0; i < n; i++)
			{
				// Even spread along boat-local X from bow (+0.38) to stern (-0.38).
				float t = (n == 1) ? 0.5f : (float)i / (n - 1);
				float xLocal = Mathf.Lerp(+0.38f, -0.38f, t);
				// Hull tapers — narrower at the very ends, widest amidships.
				float zSpread = _boatHullSpread * (0.85f + 0.15f * Mathf.Sin(t * Mathf.Pi));
				// Bow breaks more water than the stern.
				float ampMul = Mathf.Lerp(1.10f, 0.65f, t);
				float worldX = pos.X + xLocal * _boatDirection;
				SpawnWakeRipple(new Vector2(worldX, pos.Z + zSpread), _boatHullAmp * ampMul);
				SpawnWakeRipple(new Vector2(worldX, pos.Z - zSpread), _boatHullAmp * ampMul);
			}

			_boatTrailToggle = !_boatTrailToggle;
			if (_boatTrailToggle && _boatSternAmp > 0.0001f)
			{
				float sternX = pos.X - 0.42f * _boatDirection;
				SpawnWakeRipple(new Vector2(sternX, pos.Z), _boatSternAmp);
			}
		}
	}

	private void PushRipples()
	{
		// Cleanup uses the per-ripple effective lifetime: wake ripples
		// (negative amp) live for `_wakeRippleLifetime`, click ripples for
		// the global `_rippleLifetime`. If we used a single global
		// lifetime the wake pool would fill up with stale slots that the
		// shader had already faded out.
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

		if (_rippleCountLbl != null)
			_rippleCountLbl.Text = $"活跃 ripple: {_ripples.Count} / {MaxRipples}";
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
		{
			if (TryRaycastWaterPlane(mb.Position, out var worldXZ))
			{
				SpawnRipple(worldXZ, _rippleAmp);
			}
		}
	}

	// Ortho/perspective agnostic: intersect the camera-projected ray with y = 0.
	private bool TryRaycastWaterPlane(Vector2 screenPos, out Vector2 worldXZ)
	{
		worldXZ = default;
		var from = _camera.ProjectRayOrigin(screenPos);
		var dir = _camera.ProjectRayNormal(screenPos);
		if (Mathf.Abs(dir.Y) < 1e-4f) return false;
		var t = -from.Y / dir.Y;
		if (t < 0f) return false;
		var hit = from + dir * t;
		// Only count hits inside the pool footprint so clicks on empty
		// space don't spawn detached ripples.
		var half = BlockSize * 0.5f * GridX;
		if (Mathf.Abs(hit.X) > half || Mathf.Abs(hit.Z) > half) return false;
		worldXZ = new Vector2(hit.X, hit.Z);
		return true;
	}

	// --- Drop test object ---
	private void SpawnDropObject()
	{
		if (_dropObject != null && IsInstanceValid(_dropObject))
			_dropObject.QueueFree();

		var mesh = new SphereMesh { Radius = 0.18f, Height = 0.36f };
		var mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.9f, 0.35f, 0.2f),
			Roughness = 0.4f
		};
		_dropObject = new MeshInstance3D
		{
			Name = "DropTestObject",
			Mesh = mesh,
			MaterialOverride = mat,
			Position = new Vector3(0.4f, 2.2f, 0.4f)
		};
		_dropVelY = 0f;
		AddChild(_dropObject);
	}

	private void StepDropObject(float delta)
	{
		if (_dropObject == null || !IsInstanceValid(_dropObject)) return;
		_dropVelY -= 9.8f * delta;
		var p = _dropObject.Position;
		p.Y += _dropVelY * delta;
		_dropObject.Position = p;

		if (p.Y <= 0.05f)
		{
			// Impact: splash ripple, a little bigger than normal click.
			SpawnRipple(new Vector2(p.X, p.Z), _rippleAmp * 1.8f);
			_dropObject.QueueFree();
			_dropObject = null;
		}
	}

	private void PushTimeNow()
	{
		_topMaterial?.SetShaderParameter("time_now", _timeNow);
		_sideMaterial?.SetShaderParameter("time_now", _timeNow);
	}

	private static WorldEnvironment CreateEnvironment()
	{
		var env = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Color,
			BackgroundColor = new Color(0.47f, 0.48f, 0.48f),
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = new Color(0.82f, 0.86f, 0.88f),
			AmbientLightEnergy = 1.0f,
			AmbientLightSkyContribution = 0.0f,
			TonemapMode = Godot.Environment.ToneMapper.Filmic
		};
		return new WorldEnvironment { Environment = env };
	}

	private Camera3D CreateCamera()
	{
		var camera = new Camera3D
		{
			Name = "PreviewCamera",
			Current = true,
			Projection = Camera3D.ProjectionType.Orthogonal,
			Size = 10.0f,
			Position = new Vector3(9.0f, 7.5f, 9.0f)
		};
		camera.LookAtFromPosition(camera.Position, new Vector3(0.0f, -0.2f, 0.0f), Vector3.Up);
		return camera;
	}

	private static DirectionalLight3D CreateSunLight() => new()
	{
		Name = "SunLight",
		RotationDegrees = new Vector3(-42.0f, -38.0f, 0.0f),
		LightColor = new Color(1.0f, 0.92f, 0.78f),
		LightEnergy = 1.3f,
		ShadowEnabled = false
	};

	private static DirectionalLight3D CreateFillLight() => new()
	{
		Name = "FillLight",
		RotationDegrees = new Vector3(-12.0f, 130.0f, 0.0f),
		LightColor = new Color(0.56f, 0.72f, 0.78f),
		LightEnergy = 0.32f,
		ShadowEnabled = false
	};

	private static MeshInstance3D CreateGround()
	{
		var mesh = new PlaneMesh { Size = new Vector2(40.0f, 40.0f) };
		var material = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.34f, 0.34f, 0.34f),
			Roughness = 1.0f
		};
		return new MeshInstance3D
		{
			Name = "Ground",
			Mesh = mesh,
			Position = new Vector3(0.0f, -BlockHeight - 0.02f, 0.0f),
			MaterialOverride = material
		};
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

		// Top face.
		var top = new MeshInstance3D
		{
			Name = "Top",
			Mesh = new PlaneMesh
			{
				Size = new Vector2(BlockSize, BlockSize),
				SubdivideWidth = TopSubdivisions,
				SubdivideDepth = TopSubdivisions
			},
			Position = Vector3.Zero,
			MaterialOverride = _topMaterial
		};
		node.AddChild(top);

		// Sides. Only add outer sides (not between blocks) so the grid
		// reads as a single pool. Interior sides would just be hidden
		// surfaces pushing into each other.
		if (gz == GridZ - 1) AddSide(node, "SideFront", BlockSize, new Vector3(0, -BlockHeight * 0.5f, BlockSize * 0.5f), new Vector3(90, 0, 0));
		if (gz == 0)          AddSide(node, "SideBack",  BlockSize, new Vector3(0, -BlockHeight * 0.5f, -BlockSize * 0.5f), new Vector3(90, 180, 0));
		if (gx == 0)          AddSide(node, "SideLeft",  BlockSize, new Vector3(-BlockSize * 0.5f, -BlockHeight * 0.5f, 0), new Vector3(90, 90, 0));
		if (gx == GridX - 1)  AddSide(node, "SideRight", BlockSize, new Vector3(BlockSize * 0.5f, -BlockHeight * 0.5f, 0), new Vector3(90, -90, 0));

		return node;
	}

	private void AddSide(Node3D parent, string name, float width, Vector3 position, Vector3 rotationDegrees)
	{
		var mesh = new PlaneMesh
		{
			Size = new Vector2(width, BlockHeight),
			SubdivideWidth = SideSubdivisionsH,
			SubdivideDepth = SideSubdivisionsV
		};
		parent.AddChild(new MeshInstance3D
		{
			Name = name,
			Mesh = mesh,
			Position = position,
			RotationDegrees = rotationDegrees,
			MaterialOverride = _sideMaterial
		});
	}

	private static ShaderMaterial CreateSharedMaterial(int surfaceMode)
	{
		var shader = GD.Load<Shader>("res://Assets/Shaders/painterly_water_block.gdshader");
		if (shader == null)
		{
			GD.PushError("[WaterPainterlyBlockPreview] Failed to load painterly_water_block.gdshader — open the project in the editor once so the .import file is generated.");
		}
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
		mat.SetShaderParameter("deep_color", new Color(0.05f, 0.10f, 0.12f));
		mat.SetShaderParameter("foam_color", new Color(0.90f, 0.92f, 0.88f));
		mat.SetShaderParameter("floor_color", new Color(0.34f, 0.32f, 0.22f));
		mat.SetShaderParameter("sun_color", new Color(1.25f, 1.10f, 0.85f));
		mat.SetShaderParameter("sky_color", new Color(0.42f, 0.55f, 0.62f));

		mat.SetShaderParameter("sun_dir", new Vector3(-0.6f, 0.75f, 0.3f));
		mat.SetShaderParameter("specular_power", 48.0f);
		mat.SetShaderParameter("specular_gain", 1.4f);

		mat.SetShaderParameter("foam_gain", 1.4f);
		mat.SetShaderParameter("foam_threshold", 0.38f);
		mat.SetShaderParameter("posterize_steps", 7.0f);
		mat.SetShaderParameter("posterize_strength", 0.25f);

		mat.SetShaderParameter("block_height", BlockHeight);
		mat.SetShaderParameter("beam_intensity", 1.15f);
		mat.SetShaderParameter("beam_contrast", 4.2f);
		mat.SetShaderParameter("fresnel_strength", 0.28f);
		mat.SetShaderParameter("sparkle_gain", 0.9f);
		mat.SetShaderParameter("floor_fade", 0.55f);
		mat.SetShaderParameter("waterline_glow", 0.7f);

		// Ripple defaults (must match shader hint defaults + panel defaults).
		mat.SetShaderParameter("ripple_speed", 2.8f);
		mat.SetShaderParameter("ripple_width", 2.0f);
		mat.SetShaderParameter("ripple_wavenumber", 14.0f);
		mat.SetShaderParameter("ripple_gate", 0.12f);
		mat.SetShaderParameter("ripple_damp", 0.6f);
		mat.SetShaderParameter("ripple_lifetime", 1.5f);
		mat.SetShaderParameter("ripple_crater_depth", 2.0f);
		mat.SetShaderParameter("ripple_crater_radius", 0.18f);
		mat.SetShaderParameter("ripple_crater_life", 0.15f);
		mat.SetShaderParameter("ripple_emit_ramp", 0.08f);
		mat.SetShaderParameter("ripple_flow_coupling", 1.0f);
		mat.SetShaderParameter("ripple_foam_burst", 1.2f);
		// Boat-wake (Kelvin) parameters — see _wakeRippleSpeed/Lifetime.
		mat.SetShaderParameter("ripple_wake_speed", 0.30f);
		mat.SetShaderParameter("ripple_wake_lifetime", 0.70f);
		return mat;
	}

	// --- Runtime tuning panel -----------------------------------------------
	// Overlay UI with sliders + color pickers that push live changes into
	// both shared materials. Not production UI — just ergonomics for dialing
	// in shader params without re-running the preview.
	private Node BuildTuningPanel()
	{
		var canvas = new CanvasLayer { Name = "TuningOverlay" };

		var scroll = new ScrollContainer
		{
			Name = "Scroll",
			AnchorLeft = 0, AnchorTop = 0, AnchorRight = 0, AnchorBottom = 1,
			OffsetLeft = 8, OffsetTop = 8, OffsetRight = 460, OffsetBottom = -8,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
		};
		canvas.AddChild(scroll);

		var panel = new PanelContainer { Name = "Panel" };
		panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scroll.AddChild(panel);

		var vbox = new VBoxContainer { Name = "Box" };
		vbox.AddThemeConstantOverride("separation", 4);
		panel.AddChild(vbox);

		AddDebugSection(vbox);
		AddRippleSection(vbox);
		AddBoatSection(vbox);

		AddSectionHeader(vbox, "流体");
		AddFloatRow(vbox, "流速",         "speed",              0.0f,  4.0f,   1.0f);
		AddFloatRow(vbox, "波浪尺度",     "wave_scale",         0.2f,  8.0f,   2.0f);
		AddFloatRow(vbox, "波浪幅度",     "wave_amp",           0.0f,  2.0f,   1.0f);
		AddFloatRow(vbox, "流场扰动",     "flow_advect",        0.0f,  1.2f,   0.4f);
		AddVec2Row (vbox, "流向",         "flow_dir",          -2.0f,  2.0f,   new Vector2(1.0f, 0.3f));

		AddSectionHeader(vbox, "形变 / 法线");
		AddFloatRow(vbox, "顶点位移",     "displace_strength",  0.0f,  0.4f,   0.16f);
		AddFloatRow(vbox, "法线强度",     "normal_strength",    0.1f,  8.0f,   3.4f);

		AddSectionHeader(vbox, "光照");
		AddFloatRow(vbox, "菲涅尔",       "fresnel_strength",   0.0f,  1.0f,   0.28f);
		AddFloatRow(vbox, "高光锐度",     "specular_power",     4.0f,  128.0f, 48.0f);
		AddFloatRow(vbox, "高光强度",     "specular_gain",      0.0f,  4.0f,   1.4f);
		AddFloatRow(vbox, "碎光强度",     "sparkle_gain",       0.0f,  3.0f,   0.9f);

		AddSectionHeader(vbox, "泡沫 / 画风");
		AddFloatRow(vbox, "泡沫增益",     "foam_gain",          0.0f,  3.0f,   1.4f);
		AddFloatRow(vbox, "泡沫阈值",     "foam_threshold",     0.0f,  1.0f,   0.38f);
		AddFloatRow(vbox, "色阶数",       "posterize_steps",    1.0f,  12.0f,  7.0f);
		AddFloatRow(vbox, "色阶强度",     "posterize_strength", 0.0f,  1.0f,   0.25f);

		AddSectionHeader(vbox, "侧面");
		AddFloatRow(vbox, "光束强度",     "beam_intensity",     0.0f,  3.0f,   1.15f);
		AddFloatRow(vbox, "光束对比",     "beam_contrast",      0.5f,  6.0f,   4.2f);
		AddFloatRow(vbox, "池底渐变",     "floor_fade",         0.0f,  1.0f,   0.55f);
		AddFloatRow(vbox, "水线高光",     "waterline_glow",     0.0f,  3.0f,   0.7f);

		AddSectionHeader(vbox, "颜色");
		AddColorRow(vbox, "浅水色", "shallow_color", new Color(0.34f, 0.46f, 0.44f));
		AddColorRow(vbox, "深水色", "deep_color",    new Color(0.05f, 0.10f, 0.12f));
		AddColorRow(vbox, "泡沫色", "foam_color",    new Color(0.90f, 0.92f, 0.88f));
		AddColorRow(vbox, "阳光色", "sun_color",     new Color(1.25f, 1.10f, 0.85f));
		AddColorRow(vbox, "天光色", "sky_color",     new Color(0.42f, 0.55f, 0.62f));
		AddColorRow(vbox, "池底色", "floor_color",   new Color(0.34f, 0.32f, 0.22f));

		return canvas;
	}

	private static void AddSectionHeader(VBoxContainer parent, string title)
	{
		parent.AddChild(new HSeparator());
		var lbl = new Label { Text = title };
		lbl.AddThemeFontSizeOverride("font_size", 13);
		lbl.Modulate = new Color(0.85f, 0.95f, 1.0f);
		parent.AddChild(lbl);
	}

	private void AddDebugSection(VBoxContainer parent)
	{
		AddSectionHeader(parent, "调试");

		// FPS / time readout.
		_fpsLabel = new Label { Text = "FPS ..." };
		_fpsLabel.AddThemeFontSizeOverride("font_size", 12);
		_fpsLabel.Modulate = new Color(1f, 0.95f, 0.6f);
		parent.AddChild(_fpsLabel);

		// Debug view dropdown.
		var viewRow = new HBoxContainer();
		viewRow.AddThemeConstantOverride("separation", 6);
		var viewLbl = new Label { Text = "可视化  (debug_view)", CustomMinimumSize = new Vector2(210, 0) };
		viewLbl.AddThemeFontSizeOverride("font_size", 11);
		viewRow.AddChild(viewLbl);
		var viewOpt = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		viewOpt.AddItem("0 正常输出",   0);
		viewOpt.AddItem("1 高度场 h",   1);
		viewOpt.AddItem("2 法线 RGB",   2);
		viewOpt.AddItem("3 梯度模 |∇h|", 3);
		viewOpt.AddItem("4 泡沫 mask",  4);
		viewOpt.AddItem("5 菲涅尔",     5);
		viewOpt.AddItem("6 sparkle",    6);
		viewOpt.AddItem("7 N·L (太阳)", 7);
		viewOpt.AddItem("8 顶点 h (插值)", 8);
		viewOpt.Selected = 0;
		viewOpt.ItemSelected += idx =>
		{
			_topMaterial.SetShaderParameter("debug_view", (int)idx);
			_sideMaterial.SetShaderParameter("debug_view", (int)idx);
		};
		viewRow.AddChild(viewOpt);
		parent.AddChild(viewRow);

		// Pause / orbit checkboxes.
		var pauseCb = new CheckBox { Text = "暂停时间  (Pause)" };
		pauseCb.Toggled += v => _paused = v;
		parent.AddChild(pauseCb);

		var orbitCb = new CheckBox { Text = "相机环绕  (Orbit)" };
		orbitCb.Toggled += v =>
		{
			_orbiting = v;
			if (!v)
			{
				_camera.Position = _cameraBasePos;
				_camera.LookAtFromPosition(_cameraBasePos, _cameraLookAt, Vector3.Up);
			}
		};
		parent.AddChild(orbitCb);

		// Manual time scrubber (only meaningful while paused).
		var scrubRow = new HBoxContainer();
		scrubRow.AddThemeConstantOverride("separation", 6);
		var scrubLbl = new Label { Text = "时间  (time_now)", CustomMinimumSize = new Vector2(210, 0) };
		scrubLbl.AddThemeFontSizeOverride("font_size", 11);
		scrubRow.AddChild(scrubLbl);
		var scrubSlider = new HSlider
		{
			MinValue = 0.0, MaxValue = 60.0, Step = 0.01, Value = 0.0,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		scrubSlider.ValueChanged += v =>
		{
			if (_paused)
			{
				_timeNow = (float)v;
				PushTimeNow();
			}
		};
		scrubRow.AddChild(scrubSlider);
		parent.AddChild(scrubRow);

		// Dump / Reset buttons.
		var btnRow = new HBoxContainer();
		btnRow.AddThemeConstantOverride("separation", 6);
		var dumpBtn = new Button { Text = "导出当前值到日志" };
		dumpBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		dumpBtn.Pressed += DumpCurrentValues;
		btnRow.AddChild(dumpBtn);
		var resetBtn = new Button { Text = "重置为默认" };
		resetBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		resetBtn.Pressed += ResetToDefaults;
		btnRow.AddChild(resetBtn);
		parent.AddChild(btnRow);
	}

	private void AddRippleSection(VBoxContainer parent)
	{
		AddSectionHeader(parent, "入水 Ripple");

		var hint = new Label { Text = "提示：左键点击水面 = 生成 ripple" };
		hint.AddThemeFontSizeOverride("font_size", 11);
		hint.Modulate = new Color(0.7f, 0.9f, 1.0f);
		parent.AddChild(hint);

		_rippleCountLbl = new Label { Text = $"活跃 ripple: 0 / {MaxRipples}" };
		_rippleCountLbl.AddThemeFontSizeOverride("font_size", 11);
		parent.AddChild(_rippleCountLbl);

		// ripple_amp is CPU-side (per-spawn), the rest are shader uniforms.
		AddRippleAmpRow(parent);
		AddFloatRow(parent, "传播速度",    "ripple_speed",          0.5f,  8.0f,  2.8f);
		AddFloatRow(parent, "包络宽度",    "ripple_width",          0.3f,  8.0f,  2.0f);
		AddFloatRow(parent, "环密度 k",    "ripple_wavenumber",     2.0f,  40.0f, 14.0f);
		AddFloatRow(parent, "前沿锐度",    "ripple_gate",           0.02f, 0.5f,  0.12f);
		AddFloatRow(parent, "距离衰减",    "ripple_damp",           0.0f,  2.0f,  0.6f);
		AddRippleLifetimeRow(parent);
		AddFloatRow(parent, "入水凹陷深度", "ripple_crater_depth",   0.0f,  6.0f,  2.0f);
		AddFloatRow(parent, "凹陷半径",    "ripple_crater_radius",  0.05f, 1.0f,  0.18f);
		AddFloatRow(parent, "凹陷时长",    "ripple_crater_life",    0.02f, 0.8f,  0.15f);
		AddFloatRow(parent, "波包生长",    "ripple_emit_ramp",      0.01f, 0.5f,  0.08f);
		AddFloatRow(parent, "随流漂移",    "ripple_flow_coupling",  0.0f,  2.0f,  1.0f);
		AddFloatRow(parent, "入水泡沫",    "ripple_foam_burst",     0.0f,  3.0f,  1.2f);

		var btnRow = new HBoxContainer();
		btnRow.AddThemeConstantOverride("separation", 6);

		var dropBtn = new Button { Text = "投掷测试物" };
		dropBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		dropBtn.Pressed += SpawnDropObject;
		btnRow.AddChild(dropBtn);

		var clearBtn = new Button { Text = "清空 ripple" };
		clearBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		clearBtn.Pressed += () => _ripples.Clear();
		btnRow.AddChild(clearBtn);

		parent.AddChild(btnRow);
	}

	private void AddRippleAmpRow(VBoxContainer parent)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);
		var nameLbl = new Label { Text = "生成幅度  (ripple_amp)", CustomMinimumSize = new Vector2(210, 0) };
		nameLbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(nameLbl);
		var slider = new HSlider
		{
			MinValue = 0.0, MaxValue = 0.5, Step = 0.005, Value = _rippleAmp,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		row.AddChild(slider);
		var valLbl = new Label { Text = _rippleAmp.ToString("F2"), CustomMinimumSize = new Vector2(48, 0) };
		valLbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(valLbl);
		slider.ValueChanged += v => { _rippleAmp = (float)v; valLbl.Text = v.ToString("F2"); };
		parent.AddChild(row);
	}

	private void AddRippleLifetimeRow(VBoxContainer parent)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);
		var nameLbl = new Label { Text = "存活时长  (ripple_lifetime)", CustomMinimumSize = new Vector2(210, 0) };
		nameLbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(nameLbl);
		var slider = new HSlider
		{
			MinValue = 0.2, MaxValue = 4.0, Step = 0.02, Value = _rippleLifetime,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		row.AddChild(slider);
		var valLbl = new Label { Text = _rippleLifetime.ToString("F2"), CustomMinimumSize = new Vector2(48, 0) };
		valLbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(valLbl);
		slider.ValueChanged += v =>
		{
			_rippleLifetime = (float)v;
			valLbl.Text = v.ToString("F2");
			_topMaterial.SetShaderParameter("ripple_lifetime", _rippleLifetime);
			_sideMaterial.SetShaderParameter("ripple_lifetime", _rippleLifetime);
		};
		parent.AddChild(row);
	}

	private void AddBoatSection(VBoxContainer parent)
	{
		AddSectionHeader(parent, "小船");

		var enableCb = new CheckBox { Text = "显示小船  (boat)", ButtonPressed = _boatEnabled };
		enableCb.Toggled += v => _boatEnabled = v;
		parent.AddChild(enableCb);

		AddBoatFloatRow(parent, "航速  (boat_speed)",         0.0f,  2.0f,  _boatSpeed,         v => _boatSpeed = v);
		AddBoatFloatRow(parent, "尾迹间隔  (trail_int)",      0.04f, 0.2f,  _boatTrailInterval, v => _boatTrailInterval = v);
		AddBoatFloatRow(parent, "船身波幅  (hull_amp)",       0.0f,  0.04f, _boatHullAmp,       v => _boatHullAmp = v);
		AddBoatFloatRow(parent, "船尾波幅  (stern_amp)",      0.0f,  0.04f, _boatSternAmp,      v => _boatSternAmp = v);
		AddBoatFloatRow(parent, "船身波宽  (hull_spread)",    0.10f, 0.35f, _boatHullSpread,    v => _boatHullSpread = v);
		// Kelvin V parameters — these are shader uniforms, not C#-side
		// timing. The V half-angle is arcsin(wake_speed / boat_speed).
		AddBoatFloatRow(parent, "尾波速度  (wake_speed)",     0.10f, 1.50f, _wakeRippleSpeed, v =>
		{
			_wakeRippleSpeed = v;
			_topMaterial.SetShaderParameter("ripple_wake_speed", v);
			_sideMaterial.SetShaderParameter("ripple_wake_speed", v);
		});
		AddBoatFloatRow(parent, "尾波寿命  (wake_lifetime)",  0.20f, 2.00f, _wakeRippleLifetime, v =>
		{
			_wakeRippleLifetime = v;
			_topMaterial.SetShaderParameter("ripple_wake_lifetime", v);
			_sideMaterial.SetShaderParameter("ripple_wake_lifetime", v);
		});

		var resetBtn = new Button { Text = "把船送回起点" };
		resetBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		resetBtn.Pressed += () =>
		{
			if (_boatYaw == null) return;
			float startX = -BlockSize * GridX * 0.5f + 0.55f;
			_boatYaw.Position = new Vector3(startX, 0f, 0f);
			_boatDirection = 1f;
			_boatPauseTimer = 0f;
			_boatTrailTimer = 0f;
			_boatTrailToggle = false;
			_boatBobPhase = 0f;
			_boatYaw.Rotation = Vector3.Zero;
		};
		parent.AddChild(resetBtn);
	}

	// Slim float-row builder for the boat section. Differs from the
	// shader-uniform-driven AddFloatRow above in that the value is fed back
	// through a C# setter instead of pushed into both water materials.
	private void AddBoatFloatRow(VBoxContainer parent, string label, float min, float max, float def, Action<float> setter)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);

		var nameLbl = new Label { Text = label, CustomMinimumSize = new Vector2(210, 0) };
		nameLbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(nameLbl);

		var slider = new HSlider
		{
			MinValue = min, MaxValue = max,
			Step = (max - min) / 200.0,
			Value = def,
			CustomMinimumSize = new Vector2(150, 18),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		row.AddChild(slider);

		var valLbl = new Label { Text = def.ToString("F2"), CustomMinimumSize = new Vector2(48, 0) };
		valLbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(valLbl);

		slider.ValueChanged += v =>
		{
			valLbl.Text = v.ToString("F2");
			setter((float)v);
		};

		parent.AddChild(row);
	}

	private void ResetToDefaults()
	{
		foreach (var a in _resetActions) a();
		GD.Print("[WaterPainterlyBlockPreview] reset to defaults");
	}

	private void DumpCurrentValues()
	{
		GD.Print("// --- WaterPainterlyBlockPreview current values ---");
		foreach (var (param, toCode) in _dumpEntries)
		{
			GD.Print($"mat.SetShaderParameter(\"{param}\", {toCode()});");
		}
		GD.Print("// --- end ---");
	}

	private void AddFloatRow(VBoxContainer parent, string cnLabel, string param, float min, float max, float def)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);

		var nameLbl = new Label { Text = $"{cnLabel}  ({param})", CustomMinimumSize = new Vector2(210, 0) };
		nameLbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(nameLbl);

		var slider = new HSlider
		{
			MinValue = min, MaxValue = max,
			Step = (max - min) / 200.0,
			Value = def,
			CustomMinimumSize = new Vector2(150, 18),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		row.AddChild(slider);

		var valLbl = new Label { Text = def.ToString("F2"), CustomMinimumSize = new Vector2(48, 0) };
		valLbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(valLbl);

		slider.ValueChanged += v =>
		{
			valLbl.Text = v.ToString("F2");
			_topMaterial.SetShaderParameter(param, (float)v);
			_sideMaterial.SetShaderParameter(param, (float)v);
		};

		_resetActions.Add(() => slider.Value = def);
		_dumpEntries.Add((param, () => $"{((float)slider.Value).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}f"));

		parent.AddChild(row);
	}

	private void AddVec2Row(VBoxContainer parent, string cnLabel, string param, float min, float max, Vector2 def)
	{
		var current = def;
		HSlider? sx = null, sy = null;
		for (var axis = 0; axis < 2; axis++)
		{
			var idx = axis;
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 6);

			var suffix = axis == 0 ? ".x" : ".y";
			var label = $"{cnLabel}{suffix}  ({param}{suffix})";
			var nameLbl = new Label { Text = label, CustomMinimumSize = new Vector2(210, 0) };
			nameLbl.AddThemeFontSizeOverride("font_size", 11);
			row.AddChild(nameLbl);

			var slider = new HSlider
			{
				MinValue = min, MaxValue = max,
				Step = (max - min) / 200.0,
				Value = axis == 0 ? def.X : def.Y,
				CustomMinimumSize = new Vector2(150, 18),
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
			};
			row.AddChild(slider);

			var valLbl = new Label
			{
				Text = (axis == 0 ? def.X : def.Y).ToString("F2"),
				CustomMinimumSize = new Vector2(48, 0)
			};
			valLbl.AddThemeFontSizeOverride("font_size", 11);
			row.AddChild(valLbl);

			slider.ValueChanged += v =>
			{
				valLbl.Text = v.ToString("F2");
				if (idx == 0) current.X = (float)v; else current.Y = (float)v;
				_topMaterial.SetShaderParameter(param, current);
				_sideMaterial.SetShaderParameter(param, current);
			};

			if (axis == 0) sx = slider; else sy = slider;

			parent.AddChild(row);
		}

		_resetActions.Add(() => { sx!.Value = def.X; sy!.Value = def.Y; });
		_dumpEntries.Add((param, () =>
		{
			var inv = System.Globalization.CultureInfo.InvariantCulture;
			return $"new Vector2({((float)sx!.Value).ToString("0.###", inv)}f, {((float)sy!.Value).ToString("0.###", inv)}f)";
		}));
	}

	private void AddColorRow(VBoxContainer parent, string cnLabel, string param, Color def)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);

		var nameLbl = new Label { Text = $"{cnLabel}  ({param})", CustomMinimumSize = new Vector2(210, 0) };
		nameLbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(nameLbl);

		var picker = new ColorPickerButton
		{
			Color = def,
			CustomMinimumSize = new Vector2(200, 20),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		picker.EditAlpha = false;
		row.AddChild(picker);

		picker.ColorChanged += c =>
		{
			_topMaterial.SetShaderParameter(param, c);
			_sideMaterial.SetShaderParameter(param, c);
		};

		_resetActions.Add(() =>
		{
			picker.Color = def;
			_topMaterial.SetShaderParameter(param, def);
			_sideMaterial.SetShaderParameter(param, def);
		});
		_dumpEntries.Add((param, () =>
		{
			var inv = System.Globalization.CultureInfo.InvariantCulture;
			var c = picker.Color;
			return $"new Color({c.R.ToString("0.###", inv)}f, {c.G.ToString("0.###", inv)}f, {c.B.ToString("0.###", inv)}f)";
		}));

		parent.AddChild(row);
	}
}
