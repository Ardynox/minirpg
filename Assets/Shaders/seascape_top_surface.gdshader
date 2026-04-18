shader_type canvas_item;
render_mode unshaded, blend_mix;

// World-space UVs keep the wave field continuous across separate top tiles.
uniform float world_scale : hint_range(0.001, 0.05) = 0.012;
uniform float steepness : hint_range(1.0, 8.0) = 4.0;
uniform float base_frequency : hint_range(0.1, 3.0) = 0.8;
uniform float speed : hint_range(0.0, 4.0) = 1.5;
uniform float base_amplitude : hint_range(0.01, 0.4) = 0.12;
uniform int octaves = 3;

uniform float normal_step : hint_range(0.01, 0.3) = 0.08;
uniform float normal_strength : hint_range(0.1, 8.0) = 3.4;
uniform float edge_softness : hint_range(0.001, 0.1) = 0.02;
uniform float crest_gain : hint_range(0.0, 1.0) = 0.18;

uniform vec2 time_dir_a = vec2(1.0, 0.8);
uniform vec2 time_dir_b = vec2(-0.85, 1.1);

uniform vec4 deep_color : source_color = vec4(0.05, 0.08, 0.10, 0.96);
uniform vec4 shallow_color : source_color = vec4(0.15, 0.20, 0.18, 0.86);
uniform vec4 crest_color : source_color = vec4(0.32, 0.42, 0.39, 1.0);
uniform vec4 specular_tint : source_color = vec4(0.82, 0.88, 0.84, 1.0);
uniform vec3 light_direction = vec3(-0.38, 0.88, 0.24);
uniform vec3 view_direction = vec3(0.0, 1.0, 1.35);
uniform float specular_power : hint_range(4.0, 64.0) = 28.0;
uniform float specular_gain : hint_range(0.0, 1.5) = 0.22;

varying vec2 world_pos;


float hash12(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

float value_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	float a = hash12(i);
	float b = hash12(i + vec2(1.0, 0.0));
	float c = hash12(i + vec2(0.0, 1.0));
	float d = hash12(i + vec2(1.0, 1.0));
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

vec2 micro_noise(vec2 p) {
	return vec2(
		value_noise(p * 0.73 + vec2(3.1, 7.7)),
		value_noise(p * 0.79 + vec2(-5.4, 2.6))
	);
}

vec2 rotate_octave(vec2 p) {
	return vec2(
		1.6 * p.x + 1.2 * p.y,
		-1.2 * p.x + 1.6 * p.y
	);
}

float sea_octave(vec2 uv, float choppy) {
	uv += (micro_noise(uv * 0.65) - 0.5) * 0.35;
	vec2 wv = 1.0 - abs(sin(uv));
	vec2 swv = abs(cos(uv));
	wv = mix(wv, swv, wv);
	float peak = 1.0 - pow(clamp(wv.x * wv.y, 0.0, 1.0), 0.65);
	return pow(clamp(peak, 0.0, 1.0), choppy);
}

float sea_height(vec2 uv) {
	float freq = base_frequency;
	float amp = base_amplitude;
	float choppy = steepness;
	float height = 0.0;
	float t = TIME * speed;
	vec2 dir_a = normalize(time_dir_a);
	vec2 dir_b = normalize(time_dir_b);
	vec2 p = uv;

	for (int octave = 0; octave < 5; octave++) {
		if (octave >= octaves) {
			break;
		}

		float wave = sea_octave((p + dir_a * t) * freq, choppy);
		wave += sea_octave((p - dir_b * t) * freq, choppy);
		height += wave * amp;

		p = rotate_octave(p);
		freq *= 1.9;
		amp *= 0.22;
		choppy = mix(choppy, 1.0, 0.2);
	}

	return height;
}

vec3 sea_normal(vec2 uv) {
	float h = sea_height(uv);
	float hx = sea_height(uv + vec2(normal_step, 0.0)) - h;
	float hy = sea_height(uv + vec2(0.0, normal_step)) - h;
	return normalize(vec3(-hx * normal_strength, 1.0, -hy * normal_strength));
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
}

void fragment() {
	vec4 input_color = COLOR;
	vec2 diamond_uv = UV * 2.0 - 1.0;
	float diamond_dist = abs(diamond_uv.x) + abs(diamond_uv.y);
	float mask = max(1.0 - smoothstep(1.0 - edge_softness, 1.0 + edge_softness, diamond_dist), 0.0);

	vec2 surface_uv = world_pos * world_scale;
	float h = sea_height(surface_uv);
	float depth_bias = value_noise(surface_uv * 0.55 + vec2(7.1, -3.7)) * 0.15;
	float depth_mix = clamp(h * 2.0 + depth_bias, 0.0, 1.0);

	vec3 base = mix(deep_color.rgb, shallow_color.rgb, depth_mix);
	vec3 normal = sea_normal(surface_uv);
	vec3 light = normalize(light_direction);
	vec3 view = normalize(view_direction);

	float diffuse = 0.65 + 0.35 * max(dot(normal, light), 0.0);
	float specular = pow(max(dot(reflect(-light, normal), view), 0.0), specular_power) * specular_gain;
	float crest = smoothstep(0.18, 0.42, h) * crest_gain;
	float edge_falloff = smoothstep(0.32, 0.96, diamond_dist) * 0.12;

	vec3 rgb = base * diffuse;
	rgb = mix(rgb, crest_color.rgb, crest);
	rgb *= 1.0 - edge_falloff;
	rgb += specular_tint.rgb * specular;

	float alpha = mix(deep_color.a, shallow_color.a, depth_mix) * mask * input_color.a;
	COLOR = vec4(rgb * input_color.rgb, alpha);
}
