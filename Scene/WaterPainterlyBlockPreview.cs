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
	private const int MaxRipples = 16;
	private readonly List<Ripple> _ripples = new();
	private readonly Vector4[] _packedRipples = new Vector4[MaxRipples];
	private float _rippleAmp = 0.18f;
	private float _rippleLifetime = 1.5f;
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
	}
	private readonly List<Droplet> _droplets = new();
	private static readonly Random _rng = new();
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
			StepDroplets((float)delta);
			StepSheets((float)delta);
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
	// Three co-ordinated populations fired per splash:
	//   1) Worthington jet (2-5 beads) — vertical bead chain, top pinches off
	//   2) Crown ring    (~4-10 drops) — low, outward, ground-skimming band
	//   3) Main spray    (~6-16 drops) — cosine-weighted upper hemisphere
	//
	// amp scales both count and velocity so tuning ripple_amp also visually
	// scales the splash (panel stays meaningful).
	private void SpawnDroplets(Vector2 worldXZ, float amp, int _unused)
	{
		// --- Continuous-membrane sheets (the "splash itself") ---
		// These are the connected geometric forms that exist for the brief
		// impact moment. Without them the airborne particles below have
		// nothing to emerge FROM and the whole thing reads as "raining
		// pellets" instead of "water splashing".

		// (S0) Crown sheet — flared expanding wreath rising from the rim
		// of the impact crater. Tooth-edged top fades into the airborne
		// crown particles spawned later in this function.
		SpawnSheet(
			mode: 0,
			origin: new Vector3(worldXZ.X, 0.005f, worldXZ.Y),
			radiusBase: 0.08f + amp * 0.18f,
			radiusGrowth: 1.4f + amp * 3.2f,
			heightMax: 0.18f + amp * 0.55f,
			maxLife: 0.32f + amp * 0.25f,
			tint: new Color(0.93f, 0.97f, 1.0f, 0.78f));

		// (S1) Jet column — continuous central pillar that the airborne
		// bead chain visually "pinches off" from. Slight delay isn't
		// modeled (pure age-driven envelope); the sin^0.6 envelope rises
		// fast and falls slow which approximates the real timing.
		SpawnSheet(
			mode: 1,
			origin: new Vector3(worldXZ.X, 0.005f, worldXZ.Y),
			radiusBase: 0.040f + amp * 0.060f,
			radiusGrowth: 0.0f,
			heightMax: 0.45f + amp * 1.45f,
			maxLife: 0.55f + amp * 0.45f,
			tint: new Color(0.95f, 0.97f, 1.0f, 0.85f));

		// (S2) Aerosol mist — low-altitude white-blue puff dispersed at
		// impact instant. Very short life, lumpy alpha, no height — reads
		// as "spray going everywhere" without any visible droplets.
		SpawnSheet(
			mode: 2,
			origin: new Vector3(worldXZ.X, 0.01f, worldXZ.Y),
			radiusBase: 0.05f + amp * 0.10f,
			radiusGrowth: 1.2f + amp * 2.4f,
			heightMax: 0.0f, // unused for mode 2 (vertex shader uses radius)
			maxLife: 0.22f + amp * 0.10f,
			tint: new Color(0.94f, 0.96f, 0.98f, 0.32f));

		// (1) Worthington jet — vertical bead chain. Beads are pre-stacked
		// in Y at t=0 with monotonically decreasing initial velocity going
		// up the column, so gravity naturally pulls the topmost bead away
		// first (the iconic "drop pinching off the jet" silhouette from
		// milk-drop photography). Bigger ripples spawn more beads.
		int beadCount = Mathf.Clamp((int)(amp / 0.05f) + 2, 2, 5);
		float jetBaseSpeed = 4.5f + amp * 22.0f;
		float jetBaseRadius = 0.038f + amp * 0.13f;
		for (int i = 0; i < beadCount; i++)
		{
			// 0 = base bead (biggest, fastest), 1 = topmost (smallest, slowest).
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

		// (2) Crown ring — 4..10 drops in a near-regular ring tangent to the
		// impact point. Mostly horizontal, slight up-bias; very short life.
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

		// (3) Main spray — cosine-weighted upper hemisphere. Bigger, slower,
		// more translucent; these are the "big arcing droplets" you see in
		// slow-mo splash footage.
		int sprayN = Mathf.Clamp((int)(amp / 0.04f) + 4, 6, 16);
		for (int i = 0; i < sprayN; i++)
		{
			float az = (float)(_rng.NextDouble() * Mathf.Tau);
			// Cosine bias toward straight up: phi=0 is up, phi=π/2 is flat.
			// Square-root mapping keeps distribution weighted toward up but
			// still lets a few fire near horizontal.
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

	private void SpawnDropletNode(Vector3 origin, Vector3 velocity, float baseRadius,
		Color baseColor, float maxLife, float drag, float stretchGain)
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
			WobblePhase = (float)(_rng.NextDouble() * Mathf.Tau)
		});
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
				// Fast-enough impact → micro-ripple (cap probability so we
				// don't fill the 16-slot ripple buffer with droplet noise).
				if (p.Y <= 0.0f && d.Velocity.Y < -2.5f && _rng.NextDouble() < 0.20)
				{
					SpawnSmallRipple(new Vector2(p.X, p.Z), 0.025f);
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

			// SphereMesh is unit-diameter (Radius=0.5, Height=1.0). Scale to
			// the world-space ellipsoid axes we want, modulated by fade.
			float width = d.BaseRadius * 2.0f * fade;
			float length = d.BaseRadius * 2.0f * stretch * fade;
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

	private void PushRipples()
	{
		_ripples.RemoveAll(r => _timeNow - r.TStart > _rippleLifetime);

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
