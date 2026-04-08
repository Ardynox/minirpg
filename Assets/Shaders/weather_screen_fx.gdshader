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

// --- Noise primitives ---

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

// --- Masking ---

float vignette(vec2 uv, float strength) {
	vec2 d = uv - 0.5;
	float v = 1.0 - dot(d, d) * (2.0 + strength * 4.0);
	return clamp(v, 0.0, 1.0);
}

float soft_edge(vec2 uv) {
	vec2 edge = smoothstep(vec2(0.02), vec2(0.18), uv) * (1.0 - smoothstep(vec2(0.82), vec2(0.98), uv));
	return edge.x * edge.y;
}

// --- Rain atmosphere (fine streaks) ---

float rain_streak(vec2 uv, vec2 dir, float density, float speed, float seed, float size_scale, float freq_scale) {
	vec2 primary = normalize(dir);
	vec2 secondary = vec2(-primary.y, primary.x);
	float frequency = max(0.2, freq_scale);
	float line_size = clamp(size_scale, 0.3, 2.0);
	vec2 p = uv * vec2(20.0, 16.0) * frequency;
	float along = dot(p, primary) + time * speed * 2.8;
	float across = dot(p, secondary) * mix(7.0, 14.0, clamp(density, 0.0, 1.0));
	float stripe_id = floor(across);
	float active = step(1.0 - clamp(density, 0.0, 1.0) * 0.35, hash12(vec2(stripe_id, 7.0), seed));
	float stripe = 1.0 - smoothstep(0.03 / line_size, 0.09 / line_size, abs(fract(across) - 0.5));
	float dash_phase = fract(along + hash12(vec2(stripe_id, 3.0), seed) * 9.0);
	float dash = smoothstep(0.04, 0.14, dash_phase) * (1.0 - smoothstep(0.55, 0.88, dash_phase));
	return active * stripe * dash;
}

float rain_atmosphere(vec2 uv, vec2 dir, float density, float speed, float seed, float size_scale, float freq_scale) {
	float far_rain = rain_streak(uv * 0.65, dir, density * 0.5, speed * 0.6, seed + 1.1, size_scale * 0.6, freq_scale * 0.8) * 0.35;
	float mid_rain = rain_streak(uv, dir, density, speed, seed + 3.7, size_scale, freq_scale) * 0.7;
	float near_rain = rain_streak(uv * 1.45, dir, density * 1.3, speed * 1.5, seed + 7.3, size_scale * 1.4, freq_scale * 1.2);
	return far_rain + mid_rain + near_rain;
}

// --- Prominent rain drops (scattered, varied size, visible) ---

float rain_drop(vec2 uv, vec2 wind_dir, float density, float speed, float seed, float size_scale, float freq_scale, float layer_seed) {
	vec2 drift = normalize(wind_dir);
	float frequency = max(0.2, freq_scale);
	// Grid of potential drop positions
	vec2 grid_size = vec2(6.0, 4.0) * frequency;
	vec2 moving_uv = uv * grid_size + drift * time * speed * 1.6 + layer_seed * vec2(3.7, 1.9);
	vec2 cell_id = floor(moving_uv);
	vec2 cell_uv = fract(moving_uv) - 0.5;

	float rnd = hash12(cell_id, seed + layer_seed);
	// Sparsity: only some cells have a drop
	float spawn = step(1.0 - clamp(density, 0.0, 1.0) * 0.45, rnd);
	if (spawn < 0.5) return 0.0;

	// Randomize position within cell
	vec2 offset = hash22(cell_id, seed + layer_seed + 1.0) - 0.5;
	vec2 pos = cell_uv - offset * 0.6;

	// Random size: some big, some small
	float size_rnd = hash12(cell_id + vec2(5.0, 13.0), seed + layer_seed);
	float radius = mix(0.02, 0.08, size_rnd * size_rnd) * size_scale; // squared for more small, fewer big

	// Elongate along fall direction for rain
	vec2 stretch = vec2(1.0, 0.35); // tall and thin
	// Rotate to wind direction
	float angle = atan(drift.x, drift.y);
	float ca = cos(angle), sa = sin(angle);
	vec2 rotated = vec2(pos.x * ca - pos.y * sa, pos.x * sa + pos.y * ca);
	float dist = length(rotated * stretch / radius);

	// Soft circle with glow
	float drop = smoothstep(1.0, 0.3, dist);
	// Bright core
	float core = smoothstep(0.5, 0.0, dist) * 0.6;
	return (drop + core) * spawn;
}

float rain_drops_scattered(vec2 uv, vec2 wind_dir, float density, float speed, float seed, float size_scale, float freq_scale) {
	float drops = 0.0;
	// Layer 1: large sparse drops
	drops += rain_drop(uv, wind_dir, density * 0.6, speed * 0.9, seed, size_scale * 1.8, freq_scale * 0.7, 0.0) * 0.9;
	// Layer 2: medium drops
	drops += rain_drop(uv, wind_dir, density * 0.9, speed * 1.1, seed, size_scale * 1.0, freq_scale * 1.0, 17.3) * 0.65;
	// Layer 3: small dense drops
	drops += rain_drop(uv, wind_dir, density * 1.3, speed * 1.4, seed, size_scale * 0.5, freq_scale * 1.4, 31.7) * 0.35;
	return drops;
}

// --- Prominent snow flakes (scattered, varied size, visible) ---

float snow_particle(vec2 uv, float density, float speed, float seed, float size_scale, float freq_scale, float layer_seed) {
	float frequency = max(0.2, freq_scale);
	vec2 grid_size = vec2(5.0, 4.5) * frequency;
	// Slow downward drift with horizontal sway
	vec2 moving_uv = uv * grid_size;
	moving_uv.y += time * speed * 0.5 + layer_seed * 2.3;
	moving_uv.x += sin(time * speed * 0.3 + layer_seed * 5.0) * 0.15;
	vec2 cell_id = floor(moving_uv);
	vec2 cell_uv = fract(moving_uv) - 0.5;

	float rnd = hash12(cell_id, seed + layer_seed);
	float spawn = step(1.0 - clamp(density, 0.0, 1.0) * 0.5, rnd);
	if (spawn < 0.5) return 0.0;

	// Randomize position within cell
	vec2 offset = hash22(cell_id, seed + layer_seed + 2.0) - 0.5;
	vec2 pos = cell_uv - offset * 0.55;

	// Individual sway per flake
	float sway_phase = hash12(cell_id + vec2(7.0, 3.0), seed + layer_seed) * 6.28318;
	pos.x += sin(time * speed * 0.6 + sway_phase) * 0.08;

	// Random size: some big, some small
	float size_rnd = hash12(cell_id + vec2(11.0, 7.0), seed + layer_seed);
	float radius = mix(0.02, 0.10, size_rnd * size_rnd) * size_scale;

	float dist = length(pos) / radius;

	// Soft glowing circle
	float flake = smoothstep(1.0, 0.2, dist);
	// Bright sparkle core
	float sparkle_phase = sin(time * 2.0 + rnd * 20.0) * 0.5 + 0.5;
	float core = smoothstep(0.4, 0.0, dist) * 0.5 * (0.7 + sparkle_phase * 0.3);
	return (flake + core) * spawn;
}

float snow_flakes_scattered(vec2 uv, float density, float speed, float seed, float size_scale, float freq_scale) {
	float flakes = 0.0;
	// Layer 1: big slow flakes (close to camera)
	flakes += snow_particle(uv, density * 0.5, speed * 0.6, seed, size_scale * 2.0, freq_scale * 0.6, 0.0) * 1.0;
	// Layer 2: medium flakes
	flakes += snow_particle(uv, density * 0.8, speed * 0.9, seed, size_scale * 1.0, freq_scale * 1.0, 13.7) * 0.6;
	// Layer 3: small fast flakes (far)
	flakes += snow_particle(uv, density * 1.2, speed * 1.3, seed, size_scale * 0.45, freq_scale * 1.5, 27.1) * 0.3;
	return flakes;
}

// --- Fog ---

float fog_volume(vec2 uv, float density, float speed, float seed, float freq_scale) {
	float frequency = max(0.2, freq_scale);
	vec2 flow_a = uv * vec2(2.4, 1.8) * frequency;
	float low = fbm(flow_a + vec2(time * speed * 0.07, -time * speed * 0.025), seed);
	float high = fbm(flow_a * 1.8 - vec2(time * speed * 0.04, 0.0), seed * 1.3);
	vec2 flow_b = uv * vec2(1.4, 1.1) * frequency + vec2(0.17, 0.0);
	float deep = fbm(flow_b + vec2(time * speed * 0.03, time * speed * 0.015), seed + 4.0);
	float bands = 0.5 + 0.5 * sin((uv.y + uv.x * 0.3 + seed) * 6.0 + time * speed * 0.14);
	float primary = smoothstep(0.38, 0.72, low * 0.7 + high * 0.45) * mix(0.7, 1.0, bands);
	float secondary = smoothstep(0.42, 0.78, deep * 0.8 + high * 0.3) * 0.6;
	return (primary * 0.6 + secondary * 0.4) * clamp(0.7 + density * 0.4, 0.7, 1.2);
}

// --- Dust ---

float dust_layer(vec2 uv, float density, float speed, float seed, float size_scale, float freq_scale) {
	float frequency = max(0.2, freq_scale);
	vec2 flow = uv * vec2(8.0, 5.0) * frequency + vec2(time * speed * 0.35, 0.0);
	float bands = fbm(flow * vec2(2.2, 5.8), seed);
	float grains = snow_particle(uv + vec2(time * speed * 0.15, 0.0), density * 0.6, speed * 0.5, seed * 1.7, size_scale * 0.5, freq_scale, 41.0);
	vec2 streak_dir = vec2(1.0, 0.06);
	float streaks = rain_streak(uv, streak_dir, density * 0.5, speed * 0.7, seed + 3.0, size_scale, freq_scale) * 0.4;
	return smoothstep(0.45, 0.8, bands) * 0.7 + grains * 0.25 + streaks * 0.25;
}

// --- Lightning ---

float thunder_flash(float base_flash, float seed) {
	float phase = fract(time * 0.11 + seed * 0.73);
	float pulse_a = exp(-80.0 * abs(phase - 0.04));
	float pulse_b = exp(-140.0 * abs(phase - 0.09));
	float pulse_c = exp(-200.0 * abs(phase - 0.14));
	float ambient = 0.5 + 0.5 * sin(time * 0.7 + seed * 11.0);
	return clamp((pulse_a + pulse_b * 0.7 + pulse_c * 0.35 + ambient * 0.06) * base_flash, 0.0, 1.0);
}

// --- Effect builder ---
// Returns: x=particle, y=haze, z=tint, w=shadow
// 'drops' is output separately as the 5th channel via the out parameter

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
	float particle_frequency,
	out float drops
) {
	drops = 0.0;
	if (mode == 0) {
		return vec4(0.0);
	}

	float particle = 0.0;
	float haze = 0.0;
	float tint = edge_tint_alpha;
	float shadow = edge_shadow;

	if (mode == 1) {
		// Rain: atmosphere streaks + scattered prominent drops + mist
		particle = rain_atmosphere(uv, wind_dir + vec2(0.06, 0.0), density, speed, seed, particle_size, particle_frequency) * overlay_alpha;
		drops = rain_drops_scattered(uv, wind_dir, density, speed, seed, particle_size, particle_frequency) * overlay_alpha;
		haze = fog_volume(uv, density * 0.35, speed * 0.25, seed + 2.0, particle_frequency * 0.7) * fog_alpha;
		tint *= 0.6;
		shadow *= 0.4;
	} else if (mode == 2) {
		// Snow: scattered prominent flakes + whiteout haze
		drops = snow_flakes_scattered(uv, density, speed, seed, particle_size, particle_frequency) * overlay_alpha;
		haze = fog_volume(uv, density * 0.4, speed * 0.12, seed + 3.0, particle_frequency * 0.6) * fog_alpha;
		tint *= 0.3;
		shadow *= 0.2;
	} else if (mode == 3) {
		// Fog: double-layer volumetric
		haze = fog_volume(uv, density, speed, seed, particle_frequency) * fog_alpha;
		particle = haze * 0.06;
	} else if (mode == 4) {
		// Dust: bands + scattered grains + streaks
		float dust = dust_layer(uv, density, speed, seed, particle_size, particle_frequency);
		particle = dust * overlay_alpha * 0.6;
		haze = dust * fog_alpha * 0.8;
	} else if (mode == 5) {
		// Thunderstorm: heavy rain + prominent drops + haze + darkening
		particle = rain_atmosphere(uv, wind_dir + vec2(0.08, 0.0), density * 1.1, speed * 1.15, seed, particle_size, particle_frequency) * overlay_alpha;
		drops = rain_drops_scattered(uv, wind_dir, density * 1.2, speed * 1.3, seed, particle_size, particle_frequency) * overlay_alpha;
		haze = fog_volume(uv, density * 0.5, speed * 0.3, seed + 1.5, particle_frequency * 0.7) * fog_alpha;
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

	float particle_size = clamp(particle_size_scale, 0.0, 2.0);
	float particle_frequency = clamp(particle_frequency_scale, 0.0, 2.0);
	float particle_blend = clamp(particle_blend_scale, 0.0, 2.0);
	float tint_strength = clamp(tint_strength_scale, 0.0, 2.0);

	// Build effects for both layers
	float drops_a, drops_b;
	vec4 effect_a = build_effect(mode_a, norm_uv, overlay_alpha_a, fog_alpha_a, edge_tint_alpha_a, edge_shadow_a, density_a, speed_a, lightning_flash_a, seed_a, wind_dir_a, particle_size, particle_frequency, drops_a);
	vec4 effect_b = build_effect(mode_b, norm_uv, overlay_alpha_b, fog_alpha_b, edge_tint_alpha_b, edge_shadow_b, density_b, speed_b, lightning_flash_b, seed_b, wind_dir_b, particle_size, particle_frequency, drops_b);
	vec4 effect = mix(effect_a, effect_b, blend_factor);
	float drops = mix(drops_a, drops_b, blend_factor);
	float flash = mix(build_flash(mode_a, lightning_flash_a, seed_a), build_flash(mode_b, lightning_flash_b, seed_b), blend_factor);
	vec3 tint = mix(tint_a.rgb, tint_b.rgb, blend_factor);
	float temperature_bias = mix(temperature_bias_a, temperature_bias_b, blend_factor);

	// Climate color grading
	vec3 cold_tint = vec3(0.78, 0.88, 1.0);
	vec3 warm_tint = vec3(0.96, 0.82, 0.64);
	vec3 climate_tint = mix(cold_tint, warm_tint, clamp(temperature_bias * 0.5 + 0.5, 0.0, 1.0));
	vec3 weather_tint = mix(tint, climate_tint, 0.35);

	// Unpack effect channels
	float particle_alpha = clamp(effect.x * particle_blend, 0.0, 0.85);
	float haze_alpha = clamp(effect.y, 0.0, 0.75);
	float tint_alpha = clamp(effect.z * tint_strength, 0.0, 0.70);
	float shadow_alpha = clamp(effect.w, 0.0, 0.70);
	float drop_alpha = clamp(drops * particle_blend, 0.0, 0.95);

	// Total weather intensity for vignette and desaturation
	float weather_intensity = clamp(particle_alpha + haze_alpha * 0.8 + shadow_alpha + drop_alpha * 0.3, 0.0, 1.0);

	vec3 color = base.rgb;

	// Desaturation - weather washes out colors
	float luma = dot(color, vec3(0.299, 0.587, 0.114));
	float desat = clamp(haze_alpha * 0.45 + shadow_alpha * 0.35, 0.0, 0.5);
	color = mix(color, vec3(luma), desat);

	// Shadow/darkening
	color *= 1.0 - shadow_alpha * 0.65;

	// Edge tint (atmospheric color at edges)
	float edge = 1.0 - soft_edge(uv);
	color = mix(color, color * weather_tint, tint_alpha * edge * 0.55);

	// Haze/fog overlay
	vec3 haze_color = mix(color, weather_tint, 0.45 * tint_strength);
	color = mix(color, haze_color, haze_alpha);

	// Lightning flash
	float flash_strength = clamp(flash, 0.0, 0.65);
	color = mix(color, vec3(1.0), flash_strength);

	// Fine particle overlay (atmospheric rain streaks, dust)
	vec3 particle_color = mix(vec3(1.0), weather_tint, 0.3 * tint_strength);
	color = mix(color, particle_color, particle_alpha * soft_edge(uv));

	// Prominent drops/flakes overlay (bright, visible, scattered)
	vec3 drop_color = mix(vec3(1.0), weather_tint, 0.15 * tint_strength);
	color = mix(color, drop_color, drop_alpha * soft_edge(uv));

	// Weather-aware vignette
	float vig = vignette(uv, weather_intensity * 1.5);
	color *= mix(1.0, vig, 0.3 + weather_intensity * 0.4);

	COLOR = vec4(color, 1.0);
}
