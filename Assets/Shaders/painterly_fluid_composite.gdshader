// Painterly fluid COMPOSITE pass — screen-space fluid surface render.
//
// This is the second half of the "黏着水" (viscous water) pipeline,
// inspired by michael0884's "Paint streams" (Shadertoy WtfyDj):
//
//   1. SubViewport renders all droplets as Gaussian density blobs with
//      additive blending → density texture (handled by the density shader).
//   2. THIS shader runs over the whole screen as a CanvasLayer overlay,
//      reads that density texture, and turns it into a unified fluid
//      surface: gradient-derived normals, fresnel + environment reflection
//      + sun specular + back-buffer refraction.
//
// The crucial property: where multiple droplets overlap, their densities
// ADD in the SubViewport, and THIS shader sees a single connected blob
// with one continuous surface. That's metaball merging — the visual
// signature of viscous water that no per-droplet 3D shader can fake.

shader_type canvas_item;
render_mode blend_mix, unshaded;

// Density buffer — SubViewport.GetTexture() pushed in from C#.
uniform sampler2D density_texture : filter_linear;
// Back buffer (= scene color BELOW this canvas overlay) — used for
// refraction. Reads what's already been rendered into the main viewport.
uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;

// Fluid surface controls.
uniform float density_threshold  : hint_range(0.0, 1.0)  = 0.30;
uniform float density_softness   : hint_range(0.0, 0.5)  = 0.18;
// How much the gradient tilts the surface normal in screen XY.
// Higher = sharper bulge at droplet centers.
uniform float normal_tilt        : hint_range(0.0, 200.0) = 60.0;
// Refraction strength on the back buffer (how much surface tilt warps
// the underlying scene).
uniform float refraction_strength : hint_range(0.0, 0.10) = 0.025;

// Lighting / scene constants — same trio used by droplet & sheet shaders.
uniform vec3  sun_dir              = vec3(-0.6, 0.75, 0.3);
uniform vec3  sun_color : source_color = vec3(1.25, 1.10, 0.85);
uniform float specular_power      : hint_range(8.0, 256.0) = 64.0;

uniform vec3  env_sky_color     : source_color = vec3(0.62, 0.78, 0.95);
uniform vec3  env_horizon_color : source_color = vec3(0.85, 0.78, 0.65);
uniform vec3  env_ground_color  : source_color = vec3(0.32, 0.30, 0.26);

uniform vec4  body_tint : source_color = vec4(0.78, 0.88, 0.96, 1.0);
// Beer-Lambert-style absorption: thicker fluid (higher density) shows
// more body color. Thin patches stay almost-clear.
uniform float absorption_strength : hint_range(0.0, 2.0) = 0.45;

vec3 sample_env(vec3 dir) {
	float h = dir.y;
	if (h > 0.0) {
		return mix(env_horizon_color, env_sky_color, smoothstep(0.0, 0.55, h));
	}
	return mix(env_horizon_color, env_ground_color, smoothstep(0.0, -0.55, h));
}

void fragment() {
	float d = texture(density_texture, SCREEN_UV).r;

	// Hard discard well below threshold so we never touch the GPU
	// pipeline (especially the screen_texture sample) for empty pixels.
	if (d < density_threshold * 0.4) {
		discard;
	}

	// --- Density gradient (= screen-space surface tilt) ---
	// Central differences. Pixel size from textureSize so it scales with
	// SubViewport resolution.
	vec2 px = vec2(1.0) / vec2(textureSize(density_texture, 0));
	float dxp = texture(density_texture, SCREEN_UV + vec2( px.x, 0.0)).r;
	float dxm = texture(density_texture, SCREEN_UV + vec2(-px.x, 0.0)).r;
	float dyp = texture(density_texture, SCREEN_UV + vec2(0.0,  px.y)).r;
	float dym = texture(density_texture, SCREEN_UV + vec2(0.0, -px.y)).r;
	vec2 grad = vec2(dxp - dxm, dyp - dym);
	float grad_len = length(grad);

	// --- Surface normal in screen space ---
	// Direction: opposite of gradient (gradient points toward density
	// boundary OUT, normal points OUT away from fluid → same direction
	// as gradient). Magnitude scaled by tilt strength + density.
	// Z component (toward camera) = sqrt(1 - |xy|²) keeps unit length.
	vec2 n_xy = grad * normal_tilt;
	// Clamp so we never produce |xy|² > 1 (would NaN the sqrt).
	float xy_len2 = clamp(dot(n_xy, n_xy), 0.0, 0.95);
	vec3 N = vec3(n_xy, sqrt(1.0 - xy_len2));

	// View vector for a camera-facing screen overlay = pure +Z.
	vec3 V = vec3(0.0, 0.0, 1.0);

	// --- Fresnel ---
	float ndv = max(dot(N, V), 0.0);
	float fres = pow(1.0 - ndv, 4.0);

	// --- Refraction ---
	// Sample the back buffer offset by the surface tilt — the more the
	// surface bulges sideways, the more the scene below appears warped.
	// Multiply by (1 - fres·0.5) so grazing edges refract less (where
	// they reflect more).
	vec3 refracted = textureLod(screen_texture,
		SCREEN_UV + N.xy * refraction_strength * (1.0 - fres * 0.5),
		0.0).rgb;

	// --- Environment reflection ---
	// Reflection vector for screen-aligned view: reflect(-V, N) where V
	// is +Z. Closed form: reflect direction has (xy = 2·N.xy·N.z, z = 2·N.z² - 1).
	// We just use the formula reflect(-V,N).
	vec3 R_env = reflect(-V, N);
	vec3 env = sample_env(R_env);

	// --- Sun specular ---
	vec3 L = normalize(sun_dir);
	vec3 R_sun = reflect(-L, N);
	float spec = pow(max(dot(R_sun, V), 0.0), specular_power);

	// --- Compose ---
	// Body: Beer-Lambert-ish absorption — thick fluid (high d) absorbs
	// more, showing its tint; thin films stay nearly clear-refracted.
	float thickness = clamp((d - density_threshold) * 2.0, 0.0, 1.0);
	vec3 col = mix(refracted, body_tint.rgb, thickness * absorption_strength);

	// Fresnel-weighted env reflection (Paint Streams trick).
	col += env * fres * 0.55;

	// Sun hot-spot.
	col += sun_color * spec * (0.7 + fres * 0.3);

	// Tiny ambient so deep-shadow blobs don't go pure black.
	col += body_tint.rgb * 0.08;

	// Reinhard soft shoulder for HDR-ish range.
	col = vec3(1.0) - exp(-col * 1.4);

	// Surface alpha — sharp threshold with soft AA edge. body_tint.a
	// is the master opacity knob.
	float surface_alpha = smoothstep(density_threshold,
	                                 density_threshold + density_softness, d);
	COLOR = vec4(col, surface_alpha * body_tint.a);
}
