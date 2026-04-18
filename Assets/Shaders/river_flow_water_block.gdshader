shader_type spatial;
render_mode world_vertex_coords, unshaded, cull_disabled, depth_draw_opaque;

uniform int surface_mode = 0; // 0 = top, 1 = side wall
uniform float normal_step : hint_range(0.01, 0.2) = 0.05;
uniform float speed : hint_range(0.0, 4.0) = 1.0;
uniform float steepness : hint_range(0.0, 2.0) = 0.78;

// direction.xy, amplitude.z, wavelength.w
uniform vec4 wave_a = vec4(0.96, 0.22, 0.16, 2.4);
uniform vec4 wave_b = vec4(0.78, 0.58, 0.09, 1.3);
uniform vec4 wave_c = vec4(0.54, -0.18, 0.05, 0.72);

uniform float river_half_width : hint_range(0.2, 4.0) = 1.2;
uniform float river_depth : hint_range(0.05, 2.0) = 0.72;
uniform float river_bank_softness : hint_range(0.05, 1.5) = 0.42;
uniform float meander_amp : hint_range(0.0, 2.0) = 0.48;
uniform float meander_freq : hint_range(0.05, 1.5) = 0.42;
uniform float foam_gain : hint_range(0.0, 2.0) = 0.95;
uniform float detail_strength : hint_range(0.0, 1.0) = 0.38;
uniform float fresnel_power : hint_range(0.5, 8.0) = 4.4;
uniform float fresnel_gain : hint_range(0.0, 1.5) = 0.32;

uniform vec4 shallow_color : source_color = vec4(0.24, 0.34, 0.30, 1.0);
uniform vec4 deep_color : source_color = vec4(0.07, 0.12, 0.14, 1.0);
uniform vec4 foam_color : source_color = vec4(0.90, 0.95, 0.92, 1.0);
uniform vec4 wall_color : source_color = vec4(0.06, 0.09, 0.10, 1.0);
uniform vec4 specular_tint : source_color = vec4(0.88, 0.93, 0.90, 1.0);
uniform vec3 sun_direction = vec3(-0.92, 0.64, 0.19);
uniform vec3 sun_color = vec3(1.45, 1.2615, 1.044);
uniform vec3 sky_color = vec3(0.099, 0.242, 0.363);

varying float v_height;
varying float v_side_mask;
varying vec3 v_world_pos;

const vec2 MOD2 = vec2(4.438975, 3.972973);

float hash11(float p) {
	vec2 p2 = fract(vec2(p) * MOD2);
	p2 += dot(p2.yx, p2.xy + 19.19);
	return fract(p2.x * p2.y);
}

vec2 hash21(float p) {
	vec3 p3 = fract(vec3(p) * vec3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yzx + 19.19);
	return fract((p3.xx + p3.yz) * p3.zy);
}

float smooth_noise(vec2 o) {
	vec2 p = floor(o);
	vec2 f = fract(o);
	float n = p.x + p.y * 57.0;

	float a = hash11(n + 0.0);
	float b = hash11(n + 1.0);
	float c = hash11(n + 57.0);
	float d = hash11(n + 58.0);

	vec2 f2 = f * f;
	vec2 f3 = f2 * f;
	vec2 t = 3.0 * f2 - 2.0 * f3;

	return a + (b - a) * t.x + (c - a) * t.y + (a - b + d - c) * t.x * t.y;
}

vec3 smooth_noise_dxy(vec2 o) {
	vec2 p = floor(o);
	vec2 f = fract(o);
	float n = p.x + p.y * 57.0;

	float a = hash11(n + 0.0);
	float b = hash11(n + 1.0);
	float c = hash11(n + 57.0);
	float d = hash11(n + 58.0);

	vec2 f2 = f * f;
	vec2 f3 = f2 * f;
	vec2 t = 3.0 * f2 - 2.0 * f3;
	vec2 dt = 6.0 * f - 6.0 * f2;

	float res = a + (b - a) * t.x + (c - a) * t.y + (a - b + d - c) * t.x * t.y;
	float dx = (b - a) * dt.x + (a - b + d - c) * dt.x * t.y;
	float dy = (c - a) * dt.y + (a - b + d - c) * t.x * dt.y;

	return vec3(dx, dy, res);
}

float fbm(vec2 p, float persistence) {
	float value = 0.0;
	float total = 0.0;
	float amplitude = 1.0;

	for (int octave = 0; octave < 4; octave++) {
		value += smooth_noise(p) * amplitude;
		total += amplitude;
		p *= 2.0;
		amplitude *= persistence;
	}

	return value / max(total, 0.0001);
}

vec3 fbm_dxy(vec2 p, vec2 flow, float persistence, float feedback) {
	vec3 value = vec3(0.0);
	float total = 0.0;
	float amplitude = 1.0;

	for (int octave = 0; octave < 4; octave++) {
		p += flow;
		flow *= -0.72;
		vec3 sample_value = smooth_noise_dxy(p);
		value += sample_value * amplitude;
		p += sample_value.xy * feedback;
		p *= 2.0;
		total += amplitude;
		amplitude *= persistence;
	}

	return value / max(total, 0.0001);
}

float river_center(float x) {
	float broad = sin(x * meander_freq) * meander_amp;
	float small = sin(x * (meander_freq * 0.43) + 1.7) * meander_amp * 0.35;
	return broad + small;
}

float river_center_dx(float x) {
	float broad = cos(x * meander_freq) * meander_amp * meander_freq;
	float small = cos(x * (meander_freq * 0.43) + 1.7) * meander_amp * 0.35 * (meander_freq * 0.43);
	return broad + small;
}

float obstacle_amount(vec2 p, vec2 center, float radius, float height) {
	float dist = length(p - center);
	float mask = 1.0 - smoothstep(radius * 0.35, radius, dist);
	return mask * height;
}

float river_depth_at(vec2 p) {
	float center = river_center(p.x);
	float distance_to_center = abs(p.y - center);
	float bank_mask = smoothstep(river_half_width + river_bank_softness, river_half_width * 0.2, distance_to_center);
	float bed_noise = fbm(p * vec2(0.75, 1.55) + vec2(6.3, -2.1), 0.55);
	float depth = bank_mask * river_depth * (0.72 + bed_noise * 0.42);

	float obstacle = 0.0;
	obstacle += obstacle_amount(p, vec2(-1.45, river_center(-1.45) + 0.22), 0.44, 0.18);
	obstacle += obstacle_amount(p, vec2(1.55, river_center(1.55) - 0.28), 0.58, 0.22);

	return max(depth - obstacle, 0.0);
}

vec2 base_flow(vec2 p) {
	return normalize(vec2(1.0, river_center_dx(p.x)));
}

vec2 depth_gradient(vec2 p) {
	vec2 delta = vec2(0.03, 0.0);
	float dx = river_depth_at(p + delta.xy) - river_depth_at(p - delta.xy);
	float dy = river_depth_at(p + delta.yx) - river_depth_at(p - delta.yx);
	return vec2(dx, dy);
}

vec3 flow_rate(vec2 p) {
	float depth = river_depth_at(p);
	float depth01 = clamp(depth / max(river_depth, 0.0001), 0.0, 1.0);
	vec2 gradient = depth_gradient(p);
	vec2 flow = base_flow(p);

	flow += gradient * (1.8 / (0.35 + depth));
	flow += vec2(-gradient.y, gradient.x) * 0.24;
	flow *= mix(0.45, 1.35, depth01);

	float grad_len = length(gradient);
	float foam = clamp(length(flow) * 0.22 + grad_len * 3.2 + (1.0 - depth01) * 0.7, 0.0, 1.0);
	return vec3(flow, foam);
}

vec2 wave_dir(vec4 wave) {
	return normalize(wave.xy);
}

float wave_phase(vec4 wave, vec2 sample_pos, float t) {
	vec2 dir = wave_dir(wave);
	float k = TAU / max(wave.w, 0.001);
	float c = sqrt(9.8 / k) * speed;
	return dot(dir, sample_pos) * k + t * c;
}

vec3 gerstner_wave(vec4 wave, vec2 sample_pos, float t, float depth01) {
	vec2 dir = wave_dir(wave);
	float amplitude = wave.z * mix(0.35, 1.0, depth01);
	float k = TAU / max(wave.w, 0.001);
	float phase = wave_phase(wave, sample_pos, t);
	float q = clamp(steepness / max(k * amplitude * 3.0, 0.001), 0.0, 1.2);
	float s = sin(phase);
	float c = cos(phase);

	return vec3(
		dir.x * (q * amplitude * c),
		amplitude * s,
		dir.y * (q * amplitude * c)
	);
}

vec3 gerstner_offset(vec2 sample_pos, float t, float depth01) {
	vec2 drift = base_flow(sample_pos) * t * 0.42;
	vec2 p = sample_pos + drift;
	return gerstner_wave(wave_a, p, t, depth01)
		+ gerstner_wave(wave_b, p * 1.17, t * 1.08, depth01)
		+ gerstner_wave(wave_c, p * 1.41, t * 1.2, depth01);
}

vec3 displaced_top_world(vec3 world_pos, float t) {
	float depth01 = clamp(river_depth_at(world_pos.xz) / max(river_depth, 0.0001), 0.0, 1.0);
	return world_pos + gerstner_offset(world_pos.xz, t, depth01);
}

vec4 sample_water_normal(vec2 uv, vec2 flow_offset, float mag, float foam) {
	vec3 detail = fbm_dxy(uv * 16.0, flow_offset * 14.0, 0.78 + foam * 0.08, 0.18 - foam * 0.22);
	vec3 blended = normalize(vec3(detail.x, mag, detail.y));
	return vec4(blended, detail.z);
}

float sample_water_foam(vec2 uv, vec2 flow_offset, float foam) {
	float value = fbm_dxy(uv * 24.0, flow_offset * 18.0, 0.82, -0.32).z;
	float threshold = mix(0.62, 0.36, foam);
	return smoothstep(threshold, 1.0, value);
}

vec4 sample_flowing_normal(vec2 uv, vec2 flow, float foam, float time, out float foam_tex) {
	float mag = mix(2.8, 1.15, clamp(length(flow) * 0.35, 0.0, 1.0));
	float t = time * 0.32;
	float t0 = fract(t);
	float t1 = fract(t + 0.5);
	float i0 = floor(t);
	float i1 = floor(t + 0.5);
	float o0 = t0 - 0.5;
	float o1 = t1 - 0.5;

	vec2 uv0 = uv + hash21(i0) * 2.3;
	vec2 uv1 = uv + hash21(i1) * 2.3;

	vec4 sample0 = sample_water_normal(uv0, flow * o0, mag, foam);
	vec4 sample1 = sample_water_normal(uv1, flow * o1, mag, foam);

	float foam0 = sample_water_foam(uv0, flow * o0 * 0.3, foam);
	float foam1 = sample_water_foam(uv1, flow * o1 * 0.3, foam);
	float weight = abs(t0 - 0.5) * 2.0;

	vec4 result = mix(sample0, sample1, weight);
	result.xyz = normalize(result.xyz);
	foam_tex = mix(foam0, foam1, weight);
	return result;
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
		v_height = gerstner_offset(VERTEX.xz, t, clamp(river_depth_at(VERTEX.xz) / max(river_depth, 0.0001), 0.0, 1.0)).y;
	} else {
		float top_mask = smoothstep(0.82, 1.0, UV.y);
		float depth01 = clamp(river_depth_at(VERTEX.xz) / max(river_depth, 0.0001), 0.0, 1.0);
		float offset_y = gerstner_offset(VERTEX.xz, t, depth01).y;
		VERTEX.y += offset_y * top_mask;
		v_height = offset_y;
		v_side_mask = top_mask;
	}

	v_world_pos = VERTEX;
}

void fragment() {
	vec2 sample_pos = v_world_pos.xz;
	float depth = river_depth_at(sample_pos);
	float depth01 = clamp(depth / max(river_depth, 0.0001), 0.0, 1.0);
	vec3 flow_info = flow_rate(sample_pos);
	float foam_bias = clamp(flow_info.z * foam_gain, 0.0, 1.0);

	float foam_tex = 0.0;
	vec4 flowing = sample_flowing_normal(sample_pos * 0.65, flow_info.xy, foam_bias, TIME, foam_tex);
	vec3 geom_normal = normalize(NORMAL);
	vec3 detail_normal = normalize(flowing.xyz);
	vec3 final_normal = normalize(vec3(
		geom_normal.x + detail_normal.x * detail_strength,
		max(0.08, geom_normal.y * 0.85 + detail_normal.y * 0.75),
		geom_normal.z + detail_normal.z * detail_strength
	));

	vec3 view_dir = normalize(CAMERA_POSITION_WORLD - v_world_pos);
	vec3 sun_dir = normalize(sun_direction);
	float ndotl = max(dot(final_normal, sun_dir), 0.0);
	float fresnel = pow(1.0 - max(dot(final_normal, view_dir), 0.0), fresnel_power) * fresnel_gain;
	float spec = pow(max(dot(reflect(-sun_dir, final_normal), view_dir), 0.0), mix(26.0, 52.0, depth01)) * (0.18 + depth01 * 0.34);

	float shallow_edge = 1.0 - depth01;
	float foam_edge = smoothstep(0.55, 0.0, depth01);
	float foam = clamp(max(foam_edge, foam_bias * 0.9) * foam_tex, 0.0, 1.0);
	float caustic = fbm(sample_pos * vec2(1.8, 3.2) + flow_info.xy * TIME * 0.7, 0.58);

	if (surface_mode == 0) {
		vec3 water = mix(shallow_color.rgb, deep_color.rgb, depth01);
		water += vec3(0.08, 0.11, 0.08) * caustic * shallow_edge * 0.22;

		vec3 light_mix = sky_color * (0.85 + final_normal.y * 0.35) + sun_color * (0.18 + ndotl * 0.82);
		vec3 color = water * light_mix;
		color += specular_tint.rgb * spec;
		color += specular_tint.rgb * fresnel * 0.22;
		color = mix(color, foam_color.rgb * (0.75 + ndotl * 0.35), foam);

		ALBEDO = color;
		ALPHA = 1.0;
	} else {
		float wall_fade = clamp((v_world_pos.y + river_depth * 1.15) / (river_depth * 1.3), 0.0, 1.0);
		vec3 wall = mix(wall_color.rgb, shallow_color.rgb * 0.55, wall_fade);
		wall *= sky_color * 0.7 + sun_color * (0.15 + ndotl * 0.35);
		wall += foam_color.rgb * foam * v_side_mask * 0.25;
		wall += specular_tint.rgb * fresnel * v_side_mask * 0.18;

		ALBEDO = wall;
		ALPHA = 1.0;
	}
}
