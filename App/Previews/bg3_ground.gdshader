// BG3 风地形 shader：按高度分带泥土↔草↔干草 + 坡度露土 + 程序 noise 扰动。
// 顶点高度由 C# 脚本在 PlaneMesh subdivided 后直接位移，shader 不再改形状，
// 只负责配色、法线（用 derivative）、距离雾等光照氛围。

shader_type spatial;
render_mode cull_back, depth_draw_opaque, diffuse_burley;

// Gemini 审美版日间泥土 / 草 / 干草色系（去饱和 15%、泥土比草根再深一阶）
// dirt  #3D2F20  草根下的深褐土
// grass_low  #3A3D2D  草根部（和 blade base_color_low 对齐）
// grass_high #96A365  草顶端（和 blade base_color_high 对齐）
// dry_color  #B5A66D  干草 / 枯黄斑块
// fog        #B8D2F2  远雾浅蓝（BG3 典型蓝雾远景）
uniform vec3 dirt_color      : source_color = vec3(0.239, 0.184, 0.125);
uniform vec3 grass_low       : source_color = vec3(0.227, 0.239, 0.176);
uniform vec3 grass_high      : source_color = vec3(0.588, 0.639, 0.396);
uniform vec3 dry_color       : source_color = vec3(0.710, 0.651, 0.427);
uniform vec3 fog_color       : source_color = vec3(0.722, 0.820, 0.949);

uniform float fog_start      : hint_range(0.0, 400.0) = 40.0;
uniform float fog_end        : hint_range(0.0, 800.0) = 220.0;

uniform float macro_scale    : hint_range(0.01, 1.0)  = 0.08;
uniform float micro_scale    : hint_range(0.1, 5.0)   = 1.6;
uniform float slope_bias     : hint_range(0.0, 1.0)   = 0.55;

varying vec3 v_world_pos;
varying vec3 v_world_normal;

// Godot 兼容的哈希噪声（标准 value noise）
float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

float value_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	float a = hash12(i);
	float b = hash12(i + vec2(1.0, 0.0));
	float c = hash12(i + vec2(0.0, 1.0));
	float d = hash12(i + vec2(1.0, 1.0));
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

float fbm(vec2 p) {
	float v = 0.0;
	float amp = 0.5;
	for (int i = 0; i < 4; i++) {
		v += value_noise(p) * amp;
		p *= 2.03;
		amp *= 0.5;
	}
	return v;
}

void vertex() {
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec2 pw = v_world_pos.xz;

	// 多频 fbm：宏观色块 + 微观毛刺
	float macro = fbm(pw * macro_scale);
	float micro = fbm(pw * micro_scale);

	// 高度带：y>1 偏干黄；y<-0.2 偏土色
	float h = v_world_pos.y;
	float high_band = smoothstep(0.8, 2.5, h);
	float low_band  = 1.0 - smoothstep(-0.8, 0.4, h);

	vec3 grass = mix(grass_low, grass_high, clamp(macro * 1.2 + micro * 0.3, 0.0, 1.0));

	vec3 albedo = grass;
	albedo = mix(albedo, dry_color, high_band * 0.75);
	albedo = mix(albedo, dirt_color, low_band);

	// 坡度露土：normal.y 越小越露土
	float slope = 1.0 - clamp(v_world_normal.y, 0.0, 1.0);
	float dirt_blend = smoothstep(slope_bias, slope_bias + 0.25, slope);
	albedo = mix(albedo, dirt_color, dirt_blend * 0.85);

	// 微观杂色扰动（碎草 / 枯枝）
	float splat = step(0.86, micro);
	albedo = mix(albedo, dry_color * 0.75, splat * 0.5);

	// 距离雾（BG3 远景蓝雾感）
	float dist = length(v_world_pos - CAMERA_POSITION_WORLD);
	float fog_t = clamp((dist - fog_start) / max(0.001, (fog_end - fog_start)), 0.0, 1.0);
	albedo = mix(albedo, fog_color, fog_t * 0.55);

	ALBEDO = albedo;
	ROUGHNESS = mix(0.78, 0.92, dirt_blend);
	METALLIC = 0.0;
	SPECULAR = 0.15;
}
