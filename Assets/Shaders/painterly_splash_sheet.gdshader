// Painterly splash SHEET shader.
//
// The "missing piece" between the water surface and the airborne droplets:
// the connected, membrane-like geometry that exists ONLY for the impact
// moment. Real splashes have three such sheets, all transient (~150-500ms):
//
//   mode 0 — Crown sheet
//     A flared, expanding cylindrical wall that lifts off the water at
//     impact, the "kingly wreath" you see in milk-drop photography. Top
//     edge tears into a ragged toothed silhouette as surface tension can't
//     hold the rim together; eventually those teeth pinch off into the
//     airborne crown droplets that the CPU particle system handles.
//
//   mode 1 — Jet column
//     The vertical Worthington pillar that shoots up after the crater
//     collapses. A continuous tapered column, narrow at top (where it's
//     about to break off into the airborne bead chain), wide at base
//     (where it joins the recovering water surface).
//
//   mode 2 — Aerosol mist
//     The faint, low-altitude haze of micro-droplets thrown out at the
//     instant of impact. Reads on screen as a soft white half-dome that
//     puffs out fast and dissipates faster.
//
// All three modes share a single CylinderMesh (no top/bottom cap, just the
// side wall) — the vertex shader reshapes that wall into the mode-specific
// silhouette using only `mode`, `age`, and a few size uniforms.
//
// Per-instance uniforms:
//   mode             0/1/2 — picks the shape transform
//   age              seconds since spawn (CPU pushes per frame)
//   max_life         total lifetime; both shape envelope AND alpha fade key off this
//   radius_base      starting / fixed radius (m)
//   radius_growth    radial expansion speed (m/s) — used by crown & aerosol
//   height_max       peak vertical extent (m) at t = max_life/2
//   tint             base RGBA — alpha controls overall density

shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_opaque, cull_disabled, shadows_disabled;

uniform int   mode          : hint_range(0, 2) = 0;
uniform float age            = 0.0;
uniform float max_life       = 0.30;
uniform float radius_base    = 0.20;
uniform float radius_growth  = 1.5;
uniform float height_max     = 0.40;
uniform vec4  tint : source_color = vec4(0.92, 0.96, 1.0, 0.80);

// --- "更仿真" lighting & surface noise ---
// Per-spawn random; drives 3D surface noise & jet phase so two crowns
// from two clicks don't look identical.
uniform float seed              : hint_range(0.0, 1.0) = 0.5;
// Scene sun direction & color; needed for fresnel-glass and Phong spec.
uniform vec3  sun_dir              = vec3(-0.6, 0.75, 0.3);
uniform vec3  sun_color : source_color = vec3(1.25, 1.10, 0.85);
// Surface turbulence amplitude (in radial units). Real water sheets are
// not smooth — surface tension + drag ripple them with crinkles.
uniform float surface_noise_amp : hint_range(0.0, 0.30) = 0.06;

// --- Procedural environment (Paint Streams trick, see droplet shader) ---
// Crown's silhouette and jet's curved surface should reflect the world
// around them, not always be the same hardcoded white. Same 3-tone
// gradient as droplet so the whole splash reads as "in the same scene".
uniform vec3  env_sky_color     : source_color = vec3(0.62, 0.78, 0.95);
uniform vec3  env_horizon_color : source_color = vec3(0.85, 0.78, 0.65);
uniform vec3  env_ground_color  : source_color = vec3(0.32, 0.30, 0.26);

vec3 sample_env(vec3 dir) {
	float h = dir.y;
	if (h > 0.0) {
		return mix(env_horizon_color, env_sky_color, smoothstep(0.0, 0.55, h));
	}
	return mix(env_horizon_color, env_ground_color, smoothstep(0.0, -0.55, h));
}

// v_height_norm: 0 at the bottom rim of the original cylinder, 1 at the top.
// v_angle_param: 0..1 around the cylinder (used for crown teeth pattern).
// v_world / v_normal_world: needed for fresnel & sun spec in fragment.
varying float v_height_norm;
varying float v_angle_param;
varying vec3  v_world;
varying vec3  v_normal_world;

// --- 3D value noise (cheap; trilinear-interpolated hash grid) ---
float hash13(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.yzx + 19.19);
	return fract((p.x + p.y) * p.z);
}
float vnoise3d(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = hash13(i);
	float b = hash13(i + vec3(1.0, 0.0, 0.0));
	float c = hash13(i + vec3(0.0, 1.0, 0.0));
	float d = hash13(i + vec3(1.0, 1.0, 0.0));
	float e = hash13(i + vec3(0.0, 0.0, 1.0));
	float g = hash13(i + vec3(1.0, 0.0, 1.0));
	float h = hash13(i + vec3(0.0, 1.0, 1.0));
	float k = hash13(i + vec3(1.0, 1.0, 1.0));
	return mix(
		mix(mix(a, b, f.x), mix(c, d, f.x), f.y),
		mix(mix(e, g, f.x), mix(h, k, f.x), f.y),
		f.z);
}

void vertex() {
	// Default CylinderMesh (Height=1) spans VERTEX.y in [-0.5, +0.5].
	float v = VERTEX.y + 0.5;
	v_height_norm = v;

	// Angle around the cylinder, derived from local XZ (radius is 1 on the
	// source mesh). atan returns [-π, π] so normalize to [0, 1].
	v_angle_param = atan(VERTEX.z, VERTEX.x) / 6.2831853 + 0.5;

	float t = clamp(age / max(max_life, 0.001), 0.0, 1.0);

	if (mode == 0) {
		// --- Crown sheet ---
		// REAL crown geometry is mostly cylindrical with a slight outward
		// tilt at the top, NOT a goblet flare. Previous version had ×1.5
		// flare which read as "lampshade"; ×1.1 reads as "wall with a
		// slight lean".
		float radius = radius_base + radius_growth * age;
		float r_at_v = radius * (1.0 + v * 0.10);
		float height_env = sin(t * 3.1415926);
		float height = height_max * height_env;

		// PHYSICAL upward jets at the rim. Real crown sheets tear into a
		// ring of 6-10 thin upward jets, each pinching off a droplet at
		// its tip. We push the top-band vertices upward by a sin^4 of the
		// angular position around the cylinder — sin^4 keeps only the
		// peaks tall and the troughs near zero, giving discrete spikes
		// instead of a smooth sinusoidal rim.
		float angle = atan(VERTEX.z, VERTEX.x);
		float jet_signal = max(0.0, sin(angle * 7.0 + age * 1.5));
		jet_signal = jet_signal * jet_signal;          // sin²
		jet_signal *= jet_signal;                       // sin⁴ — sharp peaks
		float jet_envelope = smoothstep(0.65, 1.0, v);  // top band only
		float jet_extra = jet_signal * jet_envelope * height_max * 0.55 * height_env;

		VERTEX.xz *= r_at_v;
		VERTEX.y = v * height + jet_extra;
	} else if (mode == 1) {
		// --- Jet column ---
		// Real Worthington jets aren't smooth tubes — they're a beaded
		// chain about to fission. We modulate the radius along height
		// with cos(v · 1.5τ) so the silhouette is bulge-narrow-bulge-
		// narrow up the column. The top still tapers (1 - 0.7v) so it
		// pinches off where the airborne bead chain takes over.
		float pinch = 0.65 + 0.35 * cos(v * 6.2831853 * 1.5);
		float r_at_v = radius_base * (1.0 - v * 0.7) * pinch;
		float height = height_max * pow(sin(t * 3.1415926), 0.6);
		VERTEX.xz *= r_at_v;
		VERTEX.y = v * height;
	} else {
		// --- Aerosol mist ---
		// Half-dome: r(v) = R · sqrt(1 - v²). Flat ratio (0.4 × R) so it
		// reads as a low-altitude puff, not a full hemisphere.
		float radius = radius_base + radius_growth * age;
		float r_at_v = radius * sqrt(max(0.0, 1.0 - v * v));
		VERTEX.xz *= r_at_v;
		VERTEX.y = v * radius * 0.40;
	}

	// --- Surface turbulence (3D value noise pushed along radial normal) ---
	// Real water membranes ripple under surface tension + air drag — they
	// are NOT smooth cylinders. We compute the radial outward direction in
	// mesh-local space (the side-wall normal direction before any reshape)
	// and push by 3D noise sampled in mesh-local position. Frequency is in
	// mesh units, so the noise scale is consistent regardless of how big
	// the sheet is rendered. Per-spawn `seed` shifts the noise so two
	// crowns aren't visually identical.
	vec2 radial = VERTEX.xz;
	float radial_len = length(radial);
	if (radial_len > 1e-4 && mode != 2) {
		vec3 nrm_dir = vec3(radial.x / radial_len, 0.0, radial.y / radial_len);
		// Frequency 6.0 gives ~6 crinkles around the cylinder + along its
		// length; signed (×2-1) so noise can push inward AND outward.
		float n = vnoise3d(vec3(VERTEX.x, VERTEX.y, VERTEX.z) * 6.0
		                   + vec3(seed * 31.0, age * 1.5, seed * 17.0));
		n = n * 2.0 - 1.0;
		// Amplitude ramps with sheet maturity — fresh sheets are smooth,
		// older sheets have had time to develop visible turbulence.
		float n_envelope = smoothstep(0.0, 0.4, t);
		VERTEX += nrm_dir * n * surface_noise_amp * n_envelope * radius_base;
	}

	// World position & normal for fragment-stage lighting.
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// Approximate world normal as the radial direction (fine for thin
	// shells; real normal would require recomputing after the per-mode
	// reshape — too much GPU for negligible visual gain here).
	vec3 mesh_normal = (radial_len > 1e-4)
		? vec3(VERTEX.x, 0.0, VERTEX.z) / max(length(vec2(VERTEX.x, VERTEX.z)), 1e-4)
		: vec3(0.0, 1.0, 0.0);
	v_normal_world = normalize((MODEL_MATRIX * vec4(mesh_normal, 0.0)).xyz);
}

// Cheap value-noise-ish hash for the aerosol's grainy alpha mask.
float hash11(float x) {
	return fract(sin(x * 91.273) * 43758.5453);
}

void fragment() {
	float t = clamp(age / max(max_life, 0.001), 0.0, 1.0);
	// Half-sine life envelope so every sheet smoothly fades in AND out.
	float fade_env = sin(t * 3.1415926);

	float v = v_height_norm;
	float u = v_angle_param;

	vec3 col = tint.rgb;
	float alpha = tint.a * fade_env;

	// --- Lighting (crown + jet only; aerosol below stays diffuse) ---
	// Real water membranes are dominated by fresnel REFLECTION OF THE
	// ENVIRONMENT at view-grazing angles, and almost transparent face-on.
	// Plus sharp Phong from the sun. All manual since unshaded.
	vec3 N = v_normal_world;
	vec3 cam_pos = INV_VIEW_MATRIX[3].xyz;
	vec3 V = normalize(cam_pos - v_world);
	float ndv = max(dot(N, V), 0.0);
	float fres = pow(1.0 - ndv, 4.0);
	vec3 L = normalize(sun_dir);
	vec3 R_sun = reflect(-L, N);
	float spec = pow(max(dot(R_sun, V), 0.0), 32.0);
	// Environment reflection direction & sample — the membrane's
	// silhouette will pick up sky / horizon / ground based on which way
	// it's tilted, not always the same white.
	vec3 R_env = reflect(-V, N);
	vec3 env = sample_env(R_env);

	if (mode == 0) {
		// --- Crown: physical-jet rim, env-reflective membrane ---
		// Geometry already provides discrete upward spikes (vertex pushes
		// the top band by sin⁴ of angle, plus radial noise for crinkles).
		// Fragment adds: tip fade, inner-wall glow, env fresnel, sun spec.
		float tip_fade = 1.0 - smoothstep(0.78, 1.0, v) * 0.55;
		alpha *= tip_fade;
		// Inner-wall brightening (constant, height-driven) — the wet
		// inside catches diffuse skylight regardless of view angle.
		col += vec3(0.18, 0.20, 0.22) * v;
		// Fresnel ENVIRONMENT reflection — the sheet's silhouette
		// becomes the COLOR OF WHATEVER IT'S REFLECTING (sky overhead,
		// ground below, warm horizon at sun-side). Replaces the old
		// hardcoded white-tint mix.
		col += env * fres * 0.65;
		// Sun specular — sharp glints where the sun reflects off the
		// crinkled inner surface.
		col += sun_color * spec * 0.6;
		// Alpha follows fresnel: the sheet is more opaque at the rim,
		// nearly invisible face-on. Real thin-sheet behavior.
		alpha *= 0.35 + fres * 0.65;
	} else if (mode == 1) {
		// --- Jet: smooth column with fading top, env-reflective ---
		// Top fades out so the column visually "frays" into airborne beads.
		float top_taper = 1.0 - smoothstep(0.5, 1.0, v);
		alpha *= top_taper * 1.4;
		col += vec3(0.15, 0.18, 0.22) * v;
		col += env * fres * 0.65;
		col += sun_color * spec * 0.55;
		alpha *= 0.40 + fres * 0.60;
	} else {
		// --- Aerosol: grainy translucent puff ---
		// Two-octave hash modulation so the mist doesn't read as a smooth
		// dome (which would look like a soap bubble). Want lumpy / cloudy.
		float n0 = hash11(floor(u * 23.0) + floor(v * 7.0) * 17.0);
		float n1 = hash11(floor(u * 47.0) - floor(v * 13.0) * 11.0);
		float lumpy = 0.55 + 0.45 * (n0 * 0.6 + n1 * 0.4);
		// Fade the rim of the puff (in u·v space, distance from center).
		// Center reference: v=0 (bottom of dome) is dense; top thins out.
		float top_thin = 1.0 - smoothstep(0.3, 1.0, v);
		alpha *= lumpy * top_thin * 0.55;
	}

	// Reinhard-like soft shoulder so env + sun spec don't clip harshly
	// at the brightest fresnel peaks.
	col = vec3(1.0) - exp(-col * 1.4);

	ALBEDO = col;
	ALPHA  = clamp(alpha, 0.0, 1.0);
}
