shader_type canvas_item;
render_mode unshaded, blend_mix;

uniform int mode = 0;
uniform float intensity : hint_range(0.0, 2.0) = 1.0;
uniform float band_strength : hint_range(0.0, 1.5) = 1.0;
uniform float time = 0.0;
uniform float seed = 0.0;
uniform vec2 wind_dir = vec2(0.25, 1.0);
uniform float coverage_alpha : hint_range(0.0, 1.0) = 0.3;
uniform vec4 tint : source_color = vec4(0.82, 0.9, 1.0, 1.0);
uniform float density : hint_range(0.0, 2.0) = 0.6;
uniform float speed : hint_range(0.0, 4.0) = 1.0;

float hash11(float p) {
	return fract(sin(p * 127.1 + seed * 53.7) * 43758.5453123);
}

float hash12(vec2 p) {
	return fract(sin(dot(p + seed, vec2(127.1, 311.7))) * 43758.5453123);
}

vec2 hash22(vec2 p) {
	return vec2(hash12(p + 1.3), hash12(p + 7.9));
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

float fbm(vec2 p) {
	float value = 0.0;
	float amplitude = 0.5;
	for (int octave = 0; octave < 4; octave++) {
		value += value_noise(p) * amplitude;
		p = p * 2.03 + vec2(17.0, 11.0);
		amplitude *= 0.5;
	}
	return value;
}

float soft_edge_mask(vec2 uv, float feather) {
	vec2 dist = abs(uv - 0.5) * 2.0;
	float edge = max(dist.x, dist.y);
	return 1.0 - smoothstep(1.0 - feather, 1.0, edge);
}

float rain_layer(vec2 uv, vec2 dir, float line_density, float layer_speed, float thickness, float jitter) {
	vec2 primary = normalize(dir);
	vec2 secondary = vec2(-primary.y, primary.x);
	vec2 p = uv * vec2(8.0, 10.0);
	float along = dot(p, primary) + time * layer_speed;
	float across = dot(p, secondary) * mix(8.0, 16.0, clamp(line_density, 0.0, 1.6)) + jitter;
	float stripe_id = floor(across);
	float stripe_random = hash11(stripe_id + jitter * 13.0);
	float active = step(1.0 - clamp(line_density, 0.0, 1.35) * 0.55, stripe_random);
	float stripe = 1.0 - smoothstep(thickness, thickness * 2.8, abs(fract(across) - 0.5));
	float dash_phase = fract(along + stripe_random * 8.0);
	float dash = smoothstep(0.1, 0.45, dash_phase) * (1.0 - smoothstep(0.62, 0.95, dash_phase));
	return active * stripe * dash;
}

float snow_layer(vec2 uv, float scale, float drift, float size) {
	vec2 p = uv * scale;
	vec2 cell = floor(p);
	vec2 local = fract(p) - 0.5;
	float rnd = hash12(cell + vec2(scale, 11.0));
	vec2 offset = hash22(cell + vec2(3.1, 8.7)) - 0.5;
	float fall = fract(time * speed * 0.14 + rnd);
	local -= offset * 0.7;
	local.y += fall * 1.4 - 0.7;
	local.x += sin(time * speed * 0.55 + rnd * 6.28318) * drift;
	float flake = 1.0 - smoothstep(size, size * 1.8, length(local));
	return flake * step(1.0 - clamp(density, 0.0, 1.4) * 0.45, rnd);
}

float dust_particles(vec2 uv) {
	vec2 p = uv * vec2(12.0, 7.0);
	vec2 cell = floor(p);
	vec2 local = fract(p) - 0.5;
	float rnd = hash12(cell + vec2(19.0, 3.0));
	local.x += fract(time * speed * 0.3 + rnd) * 1.5 - 0.75;
	local.y += (hash11(rnd * 29.0) - 0.5) * 0.35;
	float particle = 1.0 - smoothstep(0.06, 0.2, length(local));
	return particle * step(1.0 - clamp(density, 0.0, 1.4) * 0.2, rnd);
}

void fragment() {
	vec2 uv = UV;
	vec2 drift_uv = uv + wind_dir * time * speed * 0.02;
	float edge = soft_edge_mask(uv, mode == 3 ? 0.42 : 0.28);
	float alpha = 0.0;

	if (mode == 1) {
		float rain_a = rain_layer(drift_uv, wind_dir + vec2(0.06, 0.0), density, speed * 2.2, 0.11, seed * 7.0);
		float rain_b = rain_layer(drift_uv * 1.15 + vec2(0.23, 0.11), wind_dir + vec2(-0.03, 0.0), density * 0.72, speed * 3.1, 0.08, seed * 13.0);
		alpha = (rain_a + rain_b * 0.65) * 0.55;
	} else if (mode == 2) {
		float snow_a = snow_layer(drift_uv, 5.0, 0.08, 0.09);
		float snow_b = snow_layer(drift_uv + vec2(0.21, 0.17), 7.0, 0.05, 0.07) * 0.75;
		alpha = (snow_a + snow_b) * 0.85;
	} else if (mode == 3) {
		vec2 fog_flow = drift_uv * vec2(2.7, 2.1);
		float low = fbm(fog_flow + vec2(time * speed * 0.08, -time * speed * 0.03));
		float high = fbm(fog_flow * 1.9 - vec2(time * speed * 0.05, 0.0));
		float ribbon = 0.5 + 0.5 * sin((uv.y + uv.x * 0.45 + seed * 0.7) * 7.5 + time * speed * 0.18);
		alpha = smoothstep(0.42, 0.72, low * 0.75 + high * 0.45) * mix(0.72, 1.0, ribbon) * 0.55;
	} else if (mode == 4) {
		vec2 dust_flow = drift_uv + vec2(time * speed * 0.12, 0.0);
		float bands = fbm(dust_flow * vec2(3.4, 6.2) + vec2(seed * 9.0, 0.0));
		float streaks = rain_layer(dust_flow * 0.95, vec2(1.0, 0.08), density * 0.65, speed * 0.95, 0.2, seed * 5.0);
		float particles = dust_particles(dust_flow) * 0.7;
		alpha = smoothstep(0.45, 0.78, bands) * 0.48 + streaks * 0.32 + particles * 0.2;
	} else if (mode == 5) {
		float phase = fract(time * 0.62 + seed * 0.917);
		float pulse_a = exp(-110.0 * abs(phase - 0.05));
		float pulse_b = exp(-180.0 * abs(phase - 0.09));
		float flash = clamp((pulse_a + pulse_b * 0.65) * intensity, 0.0, 1.0);
		float glow = 1.0 - smoothstep(0.12, 0.72, length((uv - 0.5) * vec2(0.9, 1.1)));
		float branch = 1.0 - smoothstep(0.02, 0.16, abs((uv.x - 0.5) - sin((uv.y + seed) * 11.0) * 0.08));
		alpha = flash * max(glow * 0.45, branch * 0.2);
	}

	alpha *= edge * coverage_alpha * band_strength;
	alpha = clamp(alpha, 0.0, 0.75);
	COLOR = vec4(tint.rgb, alpha * tint.a);
}
