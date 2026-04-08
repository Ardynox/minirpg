shader_type canvas_item;
render_mode unshaded, blend_mix;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap, repeat_disable;

uniform float time = 0.0;
uniform vec2 viewport_size = vec2(1280.0, 720.0);
uniform int mode_a = 0;
uniform int mode_b = 0;
uniform float blend_factor : hint_range(0.0, 1.0) = 0.0;

uniform float overlay_alpha_a : hint_range(0.0, 1.0) = 0.0;
uniform float fog_alpha_a : hint_range(0.0, 1.0) = 0.0;
uniform float edge_tint_alpha_a : hint_range(0.0, 1.0) = 0.0;
uniform float edge_shadow_a : hint_range(0.0, 1.0) = 0.0;
uniform float density_a : hint_range(0.0, 2.0) = 0.0;
uniform float speed_a : hint_range(0.0, 4.0) = 0.0;
uniform float lightning_flash_a : hint_range(0.0, 1.0) = 0.0;
uniform float temperature_bias_a : hint_range(-1.0, 1.0) = 0.0;
uniform float seed_a = 0.0;
uniform vec2 wind_dir_a = vec2(0.0, 1.0);
uniform vec4 tint_a : source_color = vec4(1.0);

uniform float overlay_alpha_b : hint_range(0.0, 1.0) = 0.0;
uniform float fog_alpha_b : hint_range(0.0, 1.0) = 0.0;
uniform float edge_tint_alpha_b : hint_range(0.0, 1.0) = 0.0;
uniform float edge_shadow_b : hint_range(0.0, 1.0) = 0.0;
uniform float density_b : hint_range(0.0, 2.0) = 0.0;
uniform float speed_b : hint_range(0.0, 4.0) = 0.0;
uniform float lightning_flash_b : hint_range(0.0, 1.0) = 0.0;
uniform float temperature_bias_b : hint_range(-1.0, 1.0) = 0.0;
uniform float seed_b = 0.0;
uniform vec2 wind_dir_b = vec2(0.0, 1.0);
uniform vec4 tint_b : source_color = vec4(1.0);
uniform float particle_size_scale : hint_range(0.0, 2.0) = 1.0;
uniform float particle_frequency_scale : hint_range(0.0, 2.0) = 1.0;
uniform float particle_blend_scale : hint_range(0.0, 2.0) = 1.0;
uniform float tint_strength_scale : hint_range(0.0, 2.0) = 1.0;

float hash12(vec2 p, float seed) {
	return fract(sin(dot(p + seed, vec2(127.1, 311.7))) * 43758.5453123);
}

vec2 hash22(vec2 p, float seed) {
	return vec2(hash12(p + vec2(1.7, 9.2), seed), hash12(p + vec2(8.3, 2.8), seed));
}

float value_noise(vec2 p, float seed) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	float a = hash12(i, seed);
	float b = hash12(i + vec2(1.0, 0.0), seed);
	float c = hash12(i + vec2(0.0, 1.0), seed);
	float d = hash12(i + vec2(1.0, 1.0), seed);
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float fbm(vec2 p, float seed) {
	float value = 0.0;
	float amplitude = 0.5;
	for (int octave = 0; octave < 4; octave++) {
		value += value_noise(p, seed) * amplitude;
		p = p * 2.03 + vec2(17.0, 11.0);
		amplitude *= 0.5;
	}
	return value;
}

float edge_mask(vec2 uv) {
	vec2 edge = smoothstep(vec2(0.04), vec2(0.22), uv) * (1.0 - smoothstep(vec2(0.78), vec2(0.96), uv));
	return edge.x * edge.y;
}

float rim_mask(vec2 uv) {
	float center = edge_mask(uv);
	return clamp(1.0 - center, 0.0, 1.0);
}

float rain_layer(vec2 uv, vec2 dir, float density, float speed, float seed, float size_scale, float frequency_scale) {
	vec2 primary = normalize(dir);
	vec2 secondary = vec2(-primary.y, primary.x);
	float frequency = max(0.2, frequency_scale);
	float line_size = clamp(size_scale, 0.2, 2.0);
	vec2 p = uv * vec2(18.0, 15.0) * frequency;
	float along = dot(p, primary) + time * speed * 2.2;
	float across = dot(p, secondary) * mix(6.0, 10.0, clamp(density, 0.0, 1.0));
	float stripe_id = floor(across);
	float active = step(1.0 - clamp(density, 0.0, 1.0) * 0.2, hash12(vec2(stripe_id, 7.0), seed));
	float stripe = 1.0 - smoothstep(0.04 / line_size, 0.12 / line_size, abs(fract(across) - 0.5));
	float dash_phase = fract(along + hash12(vec2(stripe_id, 3.0), seed) * 9.0);
	float dash = smoothstep(0.08, 0.26, dash_phase) * (1.0 - smoothstep(0.34, 0.6, dash_phase));
	return active * stripe * dash;
}

float snow_layer(vec2 uv, float density, float speed, float seed, float size_scale, float frequency_scale) {
	float frequency = max(0.2, frequency_scale);
	float flake_size = clamp(size_scale, 0.2, 2.0);
	vec2 p = uv * mix(9.0, 14.0, clamp(density, 0.0, 1.0)) * frequency;
	vec2 cell = floor(p);
	vec2 local = fract(p) - 0.5;
	float rnd = hash12(cell, seed);
	vec2 offset = hash22(cell, seed) - 0.5;
	float fall = fract(time * speed * 0.2 + rnd);
	local -= offset * 0.55;
	local.y += fall * 1.15 - 0.575;
	local.x += sin(time * speed * 0.35 + rnd * 6.28318) * 0.03;
	float flake = 1.0 - smoothstep(0.02 / flake_size, 0.07 / flake_size, length(local));
	return flake * step(1.0 - clamp(density, 0.0, 1.0) * 0.16, rnd);
}

float dust_layer(vec2 uv, float density, float speed, float seed, float size_scale, float frequency_scale) {
	float frequency = max(0.2, frequency_scale);
	vec2 flow = uv * vec2(8.0, 5.0) * frequency + vec2(time * speed * 0.28, 0.0);
	float bands = fbm(flow * vec2(2.2, 5.8), seed);
	float grains = snow_layer(uv + vec2(time * speed * 0.1, 0.0), density * 0.55, speed * 0.45, seed * 1.7, size_scale, frequency_scale);
	return smoothstep(0.5, 0.82, bands) * 0.8 + grains * 0.35;
}

float fog_layer(vec2 uv, float density, float speed, float seed, float frequency_scale) {
	float frequency = max(0.2, frequency_scale);
	vec2 flow = uv * vec2(2.2, 1.7) * frequency;
	float low = fbm(flow + vec2(time * speed * 0.06, -time * speed * 0.02), seed);
	float high = fbm(flow * 1.9 - vec2(time * speed * 0.03, 0.0), seed * 1.3);
	float bands = 0.5 + 0.5 * sin((uv.y + uv.x * 0.22 + seed) * 5.5 + time * speed * 0.12);
	return smoothstep(0.4, 0.74, low * 0.7 + high * 0.45) * mix(0.78, 1.0, bands) * clamp(0.7 + density * 0.35, 0.7, 1.15);
}

float thunder_flash(float base_flash, float seed) {
	float phase = fract(time * 0.11 + seed * 0.73);
	float pulse_a = exp(-120.0 * abs(phase - 0.04));
	float pulse_b = exp(-180.0 * abs(phase - 0.09));
	float low = 0.5 + 0.5 * sin(time * 0.7 + seed * 11.0);
	return clamp((pulse_a + pulse_b * 0.7 + low * 0.08) * base_flash, 0.0, 1.0);
}

vec4 build_effect(
	int mode,
	vec2 uv,
	float overlay_alpha,
	float fog_alpha,
	float edge_tint_alpha,
	float edge_shadow,
	float density,
	float speed,
	float lightning_flash,
	float seed,
	vec2 wind_dir,
	float particle_size,
	float particle_frequency
) {
	if (mode == 0) {
		return vec4(0.0);
	}

	float particle = 0.0;
	float haze = 0.0;
	float tint = edge_tint_alpha;
	float shadow = edge_shadow;

	if (mode == 1) {
		particle = rain_layer(uv, wind_dir + vec2(0.06, 0.0), density, speed, seed, particle_size, particle_frequency) * overlay_alpha;
		haze = 0.0;
		tint = 0.0;
		shadow = 0.0;
	} else if (mode == 2) {
		particle = snow_layer(uv, density, speed, seed, particle_size, particle_frequency) * overlay_alpha;
		haze = 0.0;
		tint = 0.0;
		shadow = 0.0;
	} else if (mode == 3) {
		haze = fog_layer(uv, density, speed, seed, particle_frequency) * fog_alpha;
		particle = haze * 0.08;
		shadow *= 0.55;
	} else if (mode == 4) {
		float dust = dust_layer(uv, density, speed, seed, particle_size, particle_frequency);
		particle = dust * overlay_alpha * 0.5;
		haze = dust * fog_alpha;
		shadow *= 0.75;
	} else if (mode == 5) {
		particle = rain_layer(uv, wind_dir + vec2(0.08, 0.0), density, speed * 1.05, seed, particle_size, particle_frequency) * overlay_alpha;
		haze = fog_alpha * 0.2;
		shadow *= 0.85;
	}

	return vec4(particle, haze, tint, shadow);
}

float build_flash(int mode, float lightning_flash, float seed) {
	return mode == 5 ? thunder_flash(lightning_flash, seed) : 0.0;
}

void fragment() {
	vec4 base = texture(screen_texture, SCREEN_UV);
	vec2 resolution = max(viewport_size, vec2(1.0));
	vec2 uv = UV;
	vec2 norm_uv = (uv * resolution) / min(resolution.x, resolution.y);
	float edge = rim_mask(uv);
	float particle_size = clamp(particle_size_scale, 0.0, 2.0);
	float particle_frequency = clamp(particle_frequency_scale, 0.0, 2.0);
	float particle_blend = clamp(particle_blend_scale, 0.0, 2.0);
	float tint_strength = clamp(tint_strength_scale, 0.0, 2.0);

	vec4 effect_a = build_effect(mode_a, norm_uv, overlay_alpha_a, fog_alpha_a, edge_tint_alpha_a, edge_shadow_a, density_a, speed_a, lightning_flash_a, seed_a, wind_dir_a, particle_size, particle_frequency);
	vec4 effect_b = build_effect(mode_b, norm_uv, overlay_alpha_b, fog_alpha_b, edge_tint_alpha_b, edge_shadow_b, density_b, speed_b, lightning_flash_b, seed_b, wind_dir_b, particle_size, particle_frequency);
	vec4 effect = mix(effect_a, effect_b, blend_factor);
	float flash = mix(build_flash(mode_a, lightning_flash_a, seed_a), build_flash(mode_b, lightning_flash_b, seed_b), blend_factor);
	vec3 tint = mix(tint_a.rgb, tint_b.rgb, blend_factor);
	float temperature_bias = mix(temperature_bias_a, temperature_bias_b, blend_factor);

	vec3 cold_tint = vec3(0.82, 0.9, 1.0);
	vec3 warm_tint = vec3(0.96, 0.84, 0.68);
	vec3 climate_tint = mix(cold_tint, warm_tint, clamp(temperature_bias * 0.5 + 0.5, 0.0, 1.0));
	vec3 edge_tint = mix(tint, climate_tint, 0.28);

	float haze_alpha = clamp(effect.y, 0.0, 0.22);
	float particle_alpha = clamp(effect.x * particle_blend, 0.0, 0.18) * edge_mask(uv);
	float tint_alpha = clamp(effect.z * tint_strength, 0.0, 0.18) * edge;
	float shadow_alpha = clamp(effect.w, 0.0, 0.2) * edge;

	vec3 color = base.rgb;
	color *= 1.0 - shadow_alpha * 0.16;
	color = mix(color, color * edge_tint, tint_alpha * 0.14);
	vec3 haze_color = mix(color, tint, 0.08 * tint_strength);
	color = mix(color, haze_color, haze_alpha);
	color = mix(color, vec3(1.0), clamp(flash, 0.0, 0.08) * 0.2);
	vec3 particle_color = mix(color, tint, 0.18 * tint_strength);
	color = mix(color, particle_color, particle_alpha);

	COLOR = vec4(color, 1.0);
}
