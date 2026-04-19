// Painterly droplet — physical 3D glass-bead shader.
//
// Now rendered as a true 3D oblate / prolate ellipsoid (SphereMesh in CPU,
// then non-uniformly scaled per droplet) instead of a screen-aligned
// billboard. This means:
//   • Looks correct from ANY camera angle (no billboard "spinning")
//   • Real fresnel from view direction → bright rim, dim center
//   • Refraction uses the actual 3D world-space normal projected to view
//     space — center of bead refracts a tiny bit, rim refracts a lot
//     (the "magnifying-glass" behaviour real glass spheres have)
//   • Real Phong-style sun specular that moves as the camera orbits
//
// Drops the previous "Heartfelt cluster" approach — that was a stylized
// 2D fake. Real flying water beads are near-spherical, with no visible
// satellites unless they actually fragment (which the spray particle
// system handles separately by spawning individual smaller drops).
//
// --- Surface tension wobble ---
// Real water beads in flight oscillate in shape due to surface tension
// (one of Worthington's classical observations). Modeled here as 3 axes
// of independent ±4% scale, phase-offset by 120° so the bead "tumbles"
// its bulge between X / Y / Z over time. Per-droplet phase from `seed`.
//
// Per-droplet uniforms:
//   albedo              very mild tint applied over the refraction
//   fade_alpha          life-end fade (0..1), CPU pushes per frame
//   emission_strength   velocity-driven sheen brightness (0.4..1.15)
//   seed                [0,1) per-droplet random, drives wobble phase
//   refraction_strength how strongly the 3D normal warps the back-buffer
//   sun_dir, sun_color  scene sun for the Phong specular
//   specular_power      Phong cosine exponent (high = sharper highlight)

shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_opaque, cull_back, shadows_disabled;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;

uniform vec4  albedo : source_color = vec4(0.78, 0.88, 0.96, 0.85);
uniform float fade_alpha          : hint_range(0.0, 1.0)   = 1.0;
uniform float emission_strength   : hint_range(0.0, 4.0)   = 0.45;
uniform float seed                : hint_range(0.0, 1.0)   = 0.5;
uniform float refraction_strength : hint_range(0.0, 0.20)  = 0.06;
uniform vec3  sun_dir              = vec3(-0.6, 0.75, 0.3);
uniform vec3  sun_color : source_color = vec3(1.25, 1.10, 0.85);
uniform float specular_power      : hint_range(8.0, 256.0) = 64.0;

// --- Procedural environment ("poor-man's cubemap") ---
// Borrowed from michael0884's "Paint streams" (Shadertoy WtfyDj): the most
// "water-like" specular cue is REFLECTING THE WORLD AROUND THE BEAD, not a
// hardcoded blue rim. Real glass beads pick up sky color from above, ground
// color from below, sun-warm color near the horizon.
//
// We don't have access to a cubemap in unshaded spatial shaders without
// extra plumbing, so we sample a 3-tone procedural sky/horizon/ground
// gradient based on the reflection direction's Y component. Painterly
// fidelity is fine for this — high-frequency cubemap detail isn't needed.
uniform vec3  env_sky_color     : source_color = vec3(0.62, 0.78, 0.95);
uniform vec3  env_horizon_color : source_color = vec3(0.85, 0.78, 0.65);
uniform vec3  env_ground_color  : source_color = vec3(0.32, 0.30, 0.26);

varying vec3 v_world;
varying vec3 v_normal_world;

vec3 sample_env(vec3 dir) {
	// Three-tone gradient. dir.y > 0 → sky-side, dir.y < 0 → ground-side,
	// horizon = the warm midpoint where the sun lives.
	float h = dir.y;
	if (h > 0.0) {
		return mix(env_horizon_color, env_sky_color, smoothstep(0.0, 0.55, h));
	}
	return mix(env_horizon_color, env_ground_color, smoothstep(0.0, -0.55, h));
}

void vertex() {
	// Surface tension wobble — 3 independent axes, 120° phase offsets,
	// per-droplet phase from `seed`. Net effect is a slow asymmetric
	// "tumble" where the bulge migrates from X-bias → Y-bias → Z-bias
	// over the wobble period (~0.8s), exactly the visual signature of
	// surface-tension oscillation in free-falling beads.
	float wob_t = TIME * 8.0 + seed * 6.2831853;
	float wx = 1.0 + sin(wob_t              ) * 0.04;
	float wy = 1.0 + sin(wob_t + 2.0944) * 0.04;
	float wz = 1.0 + sin(wob_t + 4.1888) * 0.04;
	VERTEX.x *= wx;
	VERTEX.y *= wy;
	VERTEX.z *= wz;
	// Normal isn't strictly correct after non-uniform scale, but for ±4%
	// wobble the visual error is well below the lighting tolerance.

	v_world        = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_normal_world = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec3 N = v_normal_world;
	vec3 cam_pos = INV_VIEW_MATRIX[3].xyz;
	vec3 V = normalize(cam_pos - v_world);

	// --- Fresnel ---
	// Schlick approximation (R0 = 0 for water surface viewed from air).
	// Center of bead (N·V ≈ 1) → near 0 fresnel; rim (N·V ≈ 0) → near 1.
	float ndv = max(dot(N, V), 0.0);
	float fres = pow(1.0 - ndv, 5.0);

	// --- Refraction ---
	// Project the world-space normal to view-space and use its XY as a
	// screen-UV offset. (1 - fres) factor reduces refraction at the rim
	// where fresnel-driven reflection takes over — physically correct
	// behaviour: at grazing angle, water acts more like a mirror than
	// a lens.
	vec3 N_view = (VIEW_MATRIX * vec4(N, 0.0)).xyz;
	vec2 refr_offset = N_view.xy * refraction_strength * (1.0 - fres * 0.5);
	vec3 refracted = textureLod(screen_texture, SCREEN_UV + refr_offset, 0.0).rgb;

	// --- Environment reflection (Paint Streams move) ---
	// Reflection vector in world space, sampled against the procedural
	// sky/horizon/ground gradient. THIS is what makes the droplet look
	// like it lives in the scene — its rim color now shifts as it moves
	// through the world (warm near the sun-lit horizon, cool overhead,
	// dark when reflecting the ground plane).
	vec3 R_env = reflect(-V, N);
	vec3 env = sample_env(R_env);

	// --- Sun specular (Phong) ---
	// Sharp, narrow hot-spot ON TOP of the smooth env reflection.
	vec3 L = normalize(sun_dir);
	vec3 R_sun = reflect(-L, N);
	float spec = pow(max(dot(R_sun, V), 0.0), specular_power);

	// --- Compose ---
	// Body: refracted back-buffer mildly tinted by albedo.
	vec3 col = mix(refracted, albedo.rgb, 0.12);

	// Fresnel-weighted environment reflection — replaces the old hardcoded
	// blue-white rim. At silhouette (fres ≈ 1) the rim becomes the actual
	// reflected world color; at face-on (fres ≈ 0) the body refraction
	// dominates, like real glass.
	col += env * fres * 0.55;

	// Sun hot-spot. Stronger at the rim (where curved surface acts like
	// a mirror), driven by emission_strength so fast-moving droplets
	// (which the CPU pushes higher) catch the sun harder.
	col += sun_color * spec * (0.7 + fres * 0.3) * emission_strength;

	// Tiny ambient so beads in shadow don't go to pure black.
	col += albedo.rgb * 0.10;

	// --- Soft HDR shoulder (Paint Streams uses tanh; we use Reinhard) ---
	// Sun spec + env + body can easily overshoot 1.0; clip would alias.
	// Reinhard 1 - exp(-c·k) compresses highlights smoothly while
	// preserving most of the body range.
	col = vec3(1.0) - exp(-col * 1.4);

	// Edge-opaque alpha — the rim is where you "see" the droplet. In
	// the center, the refracted back-buffer dominates anyway, so making
	// the center less opaque lets the lens effect read clearer.
	float a = (0.50 + fres * 0.50) * fade_alpha * albedo.a;

	ALBEDO = col;
	ALPHA  = a;
}
