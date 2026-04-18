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

uniform float pool_depth : hint_range(0.0, 0.5) = 0.06;
uniform float river_half_width : hint_range(0.2, 4.0) = 1.2;
uniform float river_depth : hint_range(0.05, 2.0) = 0.72;
uniform float river_bank_softness : hint_range(0.05, 1.5) = 0.42;
uniform float meander_amp : hint_range(0.0, 2.0) = 0.48;
uniform float meander_freq : hint_range(0.05, 1.5) = 0.42;
uniform float foam_gain : hint_range(0.0, 2.0) = 0.95;
uniform float detail_strength : hint_range(0.0, 1.0) = 0.38;
uniform float flow_gradient_gain : hint_range(0.0, 24.0) = 7.5;
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
uniform vec3 env_floor_color = vec3(0.3, 0.2, 0.2);

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

float fbm_simple(vec2 p, float persistence) {
	float value = 0.0;
	float total = 0.0;
	float amplitude = 1.0;

	for (int octave = 0; octave < 3; octave++) {
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
		flow *= -0.75;
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

float river_bed_offset(vec2 p) {
	float bed_depth = 0.26 + (0.5 + 0.5 * sin(p.x * 0.55 + 3.0)) * river_depth * 0.68;
	float bed_width = river_half_width + cos(p.x * 0.45) * 0.28;
	float amount = smoothstep(
		bed_width + river_bank_softness,
		bed_width * 0.45,
		abs(p.y - river_center(p.x))
	);

	float obstacle = 0.0;
	obstacle += obstacle_amount(p, vec2(-1.45, river_center(-1.45) + 0.22), 0.44, 0.18);
	obstacle += obstacle_amount(p, vec2(1.55, river_center(1.55) - 0.28), 0.58, 0.22);

	float bed_variation = 0.82 + fbm_simple(p * vec2(0.65, 1.15) + vec2(6.3, -2.1), 0.5) * 0.35;
	return max(amount * bed_depth * bed_variation - obstacle, 0.0);
}

float terrain_height_simple(vec2 p) {
	float terrain_fbm = fbm_simple(p * vec2(0.5, 1.0), 0.5);
	float terrain_height = terrain_fbm * terrain_fbm * 0.16;
	terrain_height -= river_bed_offset(p);
	return terrain_height;
}

float flow_distance(vec2 p) {
	return pool_depth - terrain_height_simple(p);
}

vec2 base_flow(vec2 p) {
	return vec2(1.0, river_center_dx(p.x));
}

vec2 normalize_safe(vec2 v) {
	float len_sq = dot(v, v);
	if (len_sq <= 0.00001) {
		return vec2(0.0);
	}
	return v * inversesqrt(len_sq);
}

vec2 depth_gradient(vec2 p) {
	vec2 delta = vec2(0.025, 0.0);
	float dx = flow_distance(p + delta.xy) - flow_distance(p - delta.xy);
	float dy = flow_distance(p + delta.yx) - flow_distance(p - delta.yx);
	return vec2(dx, dy);
}

vec4 flow_rate_info(vec2 p) {
	vec2 base = base_flow(p);
	vec2 flow = base;
	float depth = max(flow_distance(p), 0.0001);
	vec2 gradient = depth_gradient(p);

	flow += -gradient * flow_gradient_gain / (1.0 + depth * 1.5);
	flow *= 1.0 / (1.0 + depth * 0.5);

	float behind_obstacle = 0.5 - dot(normalize_safe(gradient), -normalize_safe(flow)) * 0.5;
	float slow_dist = clamp(depth * 5.0, 0.0, 1.0);
	slow_dist = mix(slow_dist * 0.9 + 0.1, 1.0, behind_obstacle * 0.9);
	slow_dist = 0.5 + slow_dist * 0.5;
	flow *= slow_dist;

	float foam = abs(length(flow)) * 0.5;
	foam += clamp(foam - 0.4, 0.0, 1.0);
	foam = 1.0 - pow(max(depth, 0.001), foam * 0.35);

	return vec4(flow * 0.6, foam, depth);
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
	vec2 drift = normalize_safe(base_flow(sample_pos)) * t * 0.42;
	vec2 p = sample_pos + drift;
	return gerstner_wave(wave_a, p, t, depth01)
		+ gerstner_wave(wave_b, p * 1.17, t * 1.08, depth01)
		+ gerstner_wave(wave_c, p * 1.41, t * 1.2, depth01);
}

vec3 displaced_top_world(vec3 world_pos, float t) {
	float depth = max(flow_distance(world_pos.xz), 0.0001);
	float depth01 = clamp(depth / max(river_depth + pool_depth, 0.0001), 0.0, 1.0);
	return world_pos + gerstner_offset(world_pos.xz, t, depth01);
}

vec4 sample_water_normal(vec2 uv, vec2 flow_offset, float mag, float foam) {
	vec2 filter_width = max(abs(dFdx(uv)), abs(dFdy(uv)));
	float filter_amount = max(filter_width.x, filter_width.y);
	float scale = 1.0 / (1.0 + filter_amount * filter_amount * 2000.0);
	float gradient_ascent = 0.25 + (foam * -1.5);
	vec3 dxy = fbm_dxy(uv * 20.0, flow_offset * 20.0, 0.75 + foam * 0.25, gradient_ascent);
	scale *= max(0.25, 1.0 - foam * 5.0);
	vec3 blended = mix(vec3(0.0, 1.0, 0.0), normalize(vec3(dxy.x, mag, dxy.y)), scale);
	return vec4(normalize(blended), dxy.z * scale);
}

float sample_water_foam(vec2 uv, vec2 flow_offset, float foam) {
	float value = fbm_dxy(uv * 30.0, flow_offset * 50.0, 0.8, -0.5).z;
	float amount = 0.2;
	value = max(0.0, (value - amount) / amount);
	return pow(0.5, value);
}

vec4 sample_flowing_normal(vec2 uv, vec2 flow, float foam, float time, out float foam_tex) {
	float mag = 2.5 / (1.0 + dot(flow, flow) * 5.0);
	float phase_time = time * speed;
	float t0 = fract(phase_time);
	float t1 = fract(phase_time + 0.5);
	float i0 = floor(phase_time);
	float i1 = floor(phase_time + 0.5);
	float o0 = t0 - 0.5;
	float o1 = t1 - 0.5;

	vec2 uv0 = uv + hash21(i0);
	vec2 uv1 = uv + hash21(i1);

	vec4 sample0 = sample_water_normal(uv0, flow * o0, mag, foam);
	vec4 sample1 = sample_water_normal(uv1, flow * o1, mag, foam);

	float foam0 = sample_water_foam(uv0, flow * o0 * 0.25, foam);
	float foam1 = sample_water_foam(uv1, flow * o1 * 0.25, foam);
	float weight = abs(t0 - 0.5) * 2.0;

	vec4 result = mix(sample0, sample1, weight);
	result.xyz = normalize(result.xyz);
	foam_tex = mix(foam0, foam1, weight);
	return result;
}

vec3 sky_color_for_dir(vec3 dir) {
	vec3 sky_up = sky_color * 4.0;
	vec3 sky_down = sky_color * 6.0;
	vec3 color = mix(sky_down, sky_up, clamp(dir.y, 0.0, 1.0));
	float sun_dot = dot(normalize(sun_direction), dir);
	float dir_dot = clamp(sun_dot * 0.5 + 0.5, 0.0, 1.0);
	color += sun_color * (1.0 - exp2(dir_dot * -0.5)) * 0.35;
	return color;
}

vec3 env_color(vec3 dir, float gloss) {
	float factor = clamp(dir.y * (1.0 - gloss * 0.5) * 0.5 + 0.5, 0.0, 1.0);
	return mix(env_floor_color, sky_color * 4.0, factor);
}

vec3 water_extinction(float dist) {
	float optical_depth = dist * 6.0;
	vec3 extinct_color = 1.0 - vec3(0.5, 0.4, 0.1);
	return exp2(-optical_depth * extinct_color);
}

vec3 fresnel_color(vec3 view, vec3 normal, vec3 r0, float gloss) {
	float ndotv = max(dot(view, normal), 0.0);
	return r0 + (vec3(1.0) - r0) * pow(1.0 - ndotv, 5.0) * pow(gloss, 20.0);
}

void vertex() {
	float t = TIME;
	v_side_mask = 0.0;

	if (surface_mode == 0) {
		vec3 displaced = displaced_top_world(VERTEX, t);
		vec3 px = displaced_top_world(VERTEX + vec3(normal_step, 0.0, 0.0), t);
		vec3 pz = displaced_top_world(VERTEX + vec3(0.0, 0.0, normal_step), t);
		float depth = max(flow_distance(VERTEX.xz), 0.0001);
		float depth01 = clamp(depth / max(river_depth + pool_depth, 0.0001), 0.0, 1.0);

		VERTEX = displaced;
		NORMAL = normalize(cross(pz - displaced, px - displaced));
		v_height = gerstner_offset(VERTEX.xz, t, depth01).y;
	} else {
		float top_mask = smoothstep(0.82, 1.0, UV.y);
		float depth = max(flow_distance(VERTEX.xz), 0.0001);
		float depth01 = clamp(depth / max(river_depth + pool_depth, 0.0001), 0.0, 1.0);
		float offset_y = gerstner_offset(VERTEX.xz, t, depth01).y;
		VERTEX.y += offset_y * top_mask;
		v_height = offset_y;
		v_side_mask = top_mask;
	}

	v_world_pos = VERTEX;
}

void fragment() {
	vec2 sample_pos = v_world_pos.xz;
	vec4 flow_data = flow_rate_info(sample_pos);
	vec2 flow = flow_data.xy;
	float foam_bias = clamp(flow_data.z * foam_gain, 0.0, 1.0);
	float depth = max(flow_data.w, 0.0001);
	float depth01 = clamp(depth / max(river_depth + pool_depth, 0.0001), 0.0, 1.0);

	float foam_tex = 1.0;
	vec4 flowing = sample_flowing_normal(sample_pos, flow, foam_bias, TIME, foam_tex);
	vec3 geom_normal = normalize(NORMAL);
	vec3 final_normal = normalize(vec3(
		geom_normal.x + flowing.x * detail_strength,
		max(0.08, geom_normal.y * 0.85 + flowing.y * 0.75),
		geom_normal.z + flowing.z * detail_strength
	));

	vec3 view_dir = normalize(CAMERA_POSITION_WORLD - v_world_pos);
	vec3 sun_dir = normalize(sun_direction);
	vec3 reflect_dir = reflect(-view_dir, final_normal);
	float ndotl = max(dot(final_normal, sun_dir), 0.0);
	float sun_dot_v = max(dot(sun_dir, view_dir), 0.0);
	float gloss = mix(0.82, 0.98, depth01);
	float spec = pow(max(dot(reflect(-sun_dir, final_normal), view_dir), 0.0), mix(24.0, 56.0, depth01)) * (0.08 + 0.3 * ndotl);

	float edge_alpha = clamp((1.0 + flowing.w * 0.25) - depth * 10.0, 0.0, 1.0);
	foam_bias *= 1.0 - edge_alpha;
	float foam_blend = 1.0 - pow(max(foam_tex, 0.0001), foam_bias * 5.0);

	vec3 diffuse_light = sky_color * (0.7 + final_normal.y * 0.3) + sun_color * (0.08 + ndotl * 0.42);
	vec3 transmit = mix(shallow_color.rgb, deep_color.rgb, depth01);
	vec3 extinction = water_extinction(depth + abs(v_world_pos.y) * 0.6);
	vec3 inscatter = diffuse_light * (1.0 - exp(-depth * 0.1)) * (1.0 + sun_dot_v);
	vec3 refracted = transmit * extinction + inscatter;

	vec3 reflection = env_color(reflect_dir, gloss);
	reflection = mix(env_color(reflect_dir, 0.0), reflection, pow(gloss, 40.0));
	vec3 fresnel = fresnel_color(view_dir, final_normal, vec3(0.01), gloss) * fresnel_gain;
	vec3 specular_light = reflection + specular_tint.rgb * spec;

	if (surface_mode == 0) {
		vec3 color = mix(refracted, specular_light, fresnel);
		vec3 foam_light = diffuse_light * 0.8;
		color = mix(color, foam_light * foam_color.rgb, foam_blend);
		ALBEDO = color;
		ALPHA = 1.0;
	} else {
		float wall_fade = clamp((v_world_pos.y + river_depth * 1.15) / (river_depth * 1.3), 0.0, 1.0);
		vec3 wall = mix(wall_color.rgb, shallow_color.rgb * 0.55, wall_fade);
		wall *= sky_color * 0.7 + sun_color * (0.15 + ndotl * 0.35);
		wall += foam_color.rgb * foam_blend * v_side_mask * 0.25;
		wall += specular_tint.rgb * max(fresnel.r, max(fresnel.g, fresnel.b)) * v_side_mask * 0.18;
		ALBEDO = wall;
		ALPHA = 1.0;
	}
}
