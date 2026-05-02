// Painterly splash SHEET — DENSITY pass.
//
// Companion to painterly_splash_sheet.gdshader. The main sheet shader
// renders the visible 3D water membrane (crown / jet / aerosol) with
// fresnel + env reflection + sun spec into the main scene. THIS shader
// renders the SAME geometry into the fluid SubViewport as additive
// density — so the sheet's silhouette participates in the same metaball
// merge as the airborne droplets do.
//
// Why two shaders for one sheet:
//   The vertex reshape that turns a unit cylinder side-wall into a crown
//   spike-rim / jet beaded-column / aerosol half-dome must be IDENTICAL
//   between the main render and the density render — otherwise the
//   sheet's silhouette in the fluid buffer wouldn't line up with the
//   sheet's actual rendered position. So this shader duplicates the
//   reshape verbatim from painterly_splash_sheet.gdshader (any change
//   there must mirror here). Fragment is replaced with a density-only
//   output that uses additive blending, no lighting.
//
// Result: where a sheet's pixels overlap droplet density blobs in screen
// space, the densities ADD and the composite shader sees them as one
// connected fluid surface — crown drops visually emerge FROM the crown
// sheet rather than appearing alongside it.

shader_type spatial;
render_mode unshaded, blend_add, depth_test_disabled, cull_disabled,
            depth_draw_never, shadows_disabled;

uniform int   mode          : hint_range(0, 2) = 0;
uniform float age            = 0.0;
uniform float max_life       = 0.30;
uniform float radius_base    = 0.20;
uniform float radius_growth  = 1.5;
uniform float height_max     = 0.40;
uniform float seed              : hint_range(0.0, 1.0) = 0.5;
uniform float surface_noise_amp : hint_range(0.0, 0.30) = 0.06;

// How much density a sheet contributes per unit shell area. Tuned so the
// crown's silhouette pixels exceed composite's density_threshold (~0.30)
// after the half-sine life envelope, but don't blow past 1.0 either.
uniform float density_strength  : hint_range(0.0, 2.0) = 0.55;

varying float v_height_norm;

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
	float v = VERTEX.y + 0.5;
	v_height_norm = v;
	float t = clamp(age / max(max_life, 0.001), 0.0, 1.0);

	// --- MIRROR of painterly_splash_sheet.gdshader vertex reshape ---
	// (keep these branches in lockstep with the main shader, otherwise
	// the density buffer will be off-register from the visible sheet)
	if (mode == 0) {
		// MUST mirror painterly_splash_sheet.gdshader exactly — collapse +
		// lean changes there have to be duplicated here so the density
		// silhouette stays in lockstep with the visible rim.
		float collapse = smoothstep(0.7, 1.0, t);
		float r_growth_eff = mix(radius_growth, -radius_growth * 0.55, collapse);
		float radius = max(0.0, radius_base + r_growth_eff * age);
		float lean = mix(0.10, 0.45, smoothstep(0.0, 0.5, t)) * (1.0 - collapse * 0.85);
		float r_at_v = radius * (1.0 + v * lean);
		float height_env = sin(t * 3.1415926);
		float height = height_max * height_env;
		float angle = atan(VERTEX.z, VERTEX.x);
		float jet_signal = max(0.0, sin(angle * 7.0 + age * 1.5));
		jet_signal = jet_signal * jet_signal;
		jet_signal *= jet_signal;
		float jet_envelope = smoothstep(0.65, 1.0, v);
		float jet_extra = jet_signal * jet_envelope * height_max * 0.55 * height_env;
		VERTEX.xz *= r_at_v;
		VERTEX.y = v * height + jet_extra;
	} else if (mode == 1) {
		float pinch = 0.65 + 0.35 * cos(v * 6.2831853 * 1.5);
		float r_at_v = radius_base * (1.0 - v * 0.7) * pinch;
		float height = height_max * pow(sin(t * 3.1415926), 0.6);
		VERTEX.xz *= r_at_v;
		VERTEX.y = v * height;
	} else {
		float radius = radius_base + radius_growth * age;
		float r_at_v = radius * sqrt(max(0.0, 1.0 - v * v));
		VERTEX.xz *= r_at_v;
		VERTEX.y = v * radius * 0.40;
	}

	// Surface noise (skip for aerosol).
	vec2 radial = VERTEX.xz;
	float radial_len = length(radial);
	if (radial_len > 1e-4 && mode != 2) {
		vec3 nrm_dir = vec3(radial.x / radial_len, 0.0, radial.y / radial_len);
		float n = vnoise3d(vec3(VERTEX.x, VERTEX.y, VERTEX.z) * 6.0
		                   + vec3(seed * 31.0, age * 1.5, seed * 17.0));
		n = n * 2.0 - 1.0;
		float n_envelope = smoothstep(0.0, 0.4, t);
		VERTEX += nrm_dir * n * surface_noise_amp * n_envelope * radius_base;
	}
}

void fragment() {
	// Density output, modulated by the same half-sine life envelope the
	// main sheet shader uses for its alpha. Rises 0→1 over t∈[0,0.5],
	// falls 1→0 over t∈[0.5,1]. So the sheet's density contribution
	// peaks when the sheet itself is at its visible peak.
	float t = clamp(age / max(max_life, 0.001), 0.0, 1.0);
	float fade_env = sin(t * 3.1415926);
	float density = density_strength * fade_env;
	// Crown rim must taper density toward the top, otherwise the metaball
	// composite welds the discrete teeth into one mushroom-cap silhouette.
	// Reducing density to ~30% above v=0.6 lets the teeth read as
	// individual spikes in the merged fluid surface.
	if (mode == 0) {
		density *= 1.0 - smoothstep(0.6, 1.0, v_height_norm) * 0.7;
	}
	ALBEDO = vec3(density);
	ALPHA  = 1.0;
}
