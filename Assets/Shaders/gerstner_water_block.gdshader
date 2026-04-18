shader_type spatial;
render_mode world_vertex_coords, cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

uniform int surface_mode = 0; // 0 = top, 1 = side wall
uniform float normal_step : hint_range(0.01, 0.2) = 0.05;
uniform float steepness : hint_range(0.0, 2.0) = 0.85;
uniform float speed : hint_range(0.0, 4.0) = 1.0;

// direction.xy, amplitude.z, wavelength.w
uniform vec4 wave_a = vec4(0.92, 0.38, 0.20, 2.7);
uniform vec4 wave_b = vec4(-0.31, 0.95, 0.11, 1.5);
uniform vec4 wave_c = vec4(0.68, -0.18, 0.06, 0.85);

uniform vec4 deep_color : source_color = vec4(0.05, 0.08, 0.10, 1.0);
uniform vec4 shallow_color : source_color = vec4(0.17, 0.23, 0.20, 1.0);
uniform vec4 crest_color : source_color = vec4(0.45, 0.57, 0.53, 1.0);
uniform vec4 wall_color : source_color = vec4(0.07, 0.10, 0.11, 1.0);
uniform vec4 specular_tint : source_color = vec4(0.86, 0.92, 0.88, 1.0);
uniform float fresnel_power : hint_range(0.5, 8.0) = 4.2;
uniform float fresnel_gain : hint_range(0.0, 1.5) = 0.35;
uniform float crest_start : hint_range(0.0, 0.6) = 0.12;
uniform float crest_end : hint_range(0.0, 0.8) = 0.28;

varying float v_height;
varying float v_side_mask;
varying vec3 v_world_pos;

vec2 wave_dir(vec4 wave) {
	return normalize(wave.xy);
}

float wave_phase(vec4 wave, vec2 sample_pos, float t) {
	vec2 dir = wave_dir(wave);
	float k = TAU / max(wave.w, 0.001);
	float c = sqrt(9.8 / k) * speed;
	return dot(dir, sample_pos) * k + t * c;
}

vec3 gerstner_wave(vec4 wave, vec2 sample_pos, float t) {
	vec2 dir = wave_dir(wave);
	float amplitude = wave.z;
	float k = TAU / max(wave.w, 0.001);
	float phase = wave_phase(wave, sample_pos, t);
	float q = clamp(steepness / max(k * amplitude * 3.0, 0.001), 0.0, 1.25);
	float s = sin(phase);
	float c = cos(phase);
	return vec3(
		dir.x * (q * amplitude * c),
		amplitude * s,
		dir.y * (q * amplitude * c)
	);
}

vec3 gerstner_offset(vec2 sample_pos, float t) {
	return gerstner_wave(wave_a, sample_pos, t)
		+ gerstner_wave(wave_b, sample_pos, t)
		+ gerstner_wave(wave_c, sample_pos, t);
}

float gerstner_height(vec2 sample_pos, float t) {
	return gerstner_offset(sample_pos, t).y;
}

vec3 displaced_top_world(vec3 world_pos, float t) {
	return world_pos + gerstner_offset(world_pos.xz, t);
}

void vertex() {
	float t = TIME;
	v_side_mask = 0.0;

	if (surface_mode == 0) {
		vec3 displaced = displaced_top_world(VERTEX, t);
		vec3 px = displaced_top_world(VERTEX + vec3(normal_step, 0.0, 0.0), t);
		vec3 pz = displaced_top_world(VERTEX + vec3(0.0, 0.0, normal_step), t);

		VERTEX = displaced;
		NORMAL = normalize(cross(pz - displaced, px - displaced));
		v_height = gerstner_height(displaced.xz, t);
	} else {
		float top_mask = smoothstep(0.82, 1.0, UV.y);
		float offset_y = gerstner_height(VERTEX.xz, t);
		VERTEX.y += offset_y * top_mask;
		v_height = offset_y;
		v_side_mask = top_mask;
	}

	v_world_pos = VERTEX;
}

void fragment() {
	vec3 n = normalize(NORMAL);
	vec3 view_dir = normalize(VIEW);
	float fresnel = pow(1.0 - max(dot(n, view_dir), 0.0), fresnel_power) * fresnel_gain;
	float height_mix = clamp((v_height + 0.28) / 0.56, 0.0, 1.0);
	float crest = smoothstep(crest_start, crest_end, v_height);

	if (surface_mode == 0) {
		vec3 base = mix(deep_color.rgb, shallow_color.rgb, height_mix);
		base = mix(base, crest_color.rgb, crest);
		ALBEDO = base + specular_tint.rgb * fresnel;
		ROUGHNESS = mix(0.08, 0.18, height_mix);
		SPECULAR = 0.8;
	} else {
		float wall_fade = clamp(v_world_pos.y * 0.9 + 0.55, 0.0, 1.0);
		vec3 wall = mix(wall_color.rgb, deep_color.rgb, wall_fade);
		wall += crest_color.rgb * crest * 0.1;
		ALBEDO = wall + specular_tint.rgb * fresnel * v_side_mask * 0.45;
		ROUGHNESS = 0.22;
		SPECULAR = 0.45;
	}
}
