// 三条 BG3 草地 punchline（维护人看到这里请记住）：
//   1. 不要做绿色的地毯，要做一万个正在呼吸的半透明翡翠片。（SSS + 个体差异）
//   2. 阴影和根部的"黑"，比叶片的"绿"更能决定草地的真实感。（AO + 接触阴影）
//   3. 风不是在吹草，而是在草地上画出流动的波浪。（宏观风力场，不是叶级别摆动）
//
// BG3 风草叶：程序化 quad + 顶端收窄 + 风摆 + 背光透光近似。
//
// 每 instance 参数走 INSTANCE_CUSTOM（MultiMesh.UseCustomData=true）：
//   .x = 相位 (0..TAU)         —— 让相邻草叶不同步摆
//   .y = 色相偏移 (-1..1)      —— 在基础黄绿色之间微调
//   .z = 高度缩放 (0.6..1.4)   —— 细微长短差异
//   .w = 朝向 (0..TAU)         —— 绕 Y 轴旋转（给每叶子一个面向）
//
// mesh 要求：QuadMesh 面向 +Z，pivot 在底部中央（Size=(1,1), CenterOffset=(0,0.5,0)），
// 即 VERTEX.y 从 0（根部）到 1（顶端），VERTEX.x 在 [-0.5, 0.5]。

shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, diffuse_burley, specular_disabled;

// 日间配色（Gemini 审美版）：橄榄绿压重 + 去饱和 15%
// base_color_low  = #3A3D2D  根部深褐绿
// base_color_high = #96A365  顶端干橄榄绿
// sss_color       = #C8C27D  透射米黄
uniform vec3 base_color_low   : source_color = vec3(0.227, 0.239, 0.176);
uniform vec3 base_color_high  : source_color = vec3(0.588, 0.639, 0.396);
uniform vec3 sss_color        : source_color = vec3(0.784, 0.761, 0.490);
uniform float sss_strength    : hint_range(0.0, 2.0) = 0.9;

uniform vec2 wind_dir         = vec2(1.0, 0.15);
uniform float wind_speed      : hint_range(0.0, 8.0)  = 1.2;
uniform float wind_amp        : hint_range(0.0, 0.8)  = 0.22;
uniform float gust_speed      : hint_range(0.0, 4.0)  = 0.35;
uniform float gust_amp        : hint_range(0.0, 0.8)  = 0.18;

uniform float blade_width     : hint_range(0.02, 0.3) = 0.075;
uniform float blade_taper     : hint_range(0.0, 1.0)  = 0.88;

uniform float hue_variation   : hint_range(0.0, 1.0)  = 0.35;

varying float v_height;
varying float v_hue;

void vertex() {
	float phase     = INSTANCE_CUSTOM.x;
	float hue       = INSTANCE_CUSTOM.y;
	float h_scale   = INSTANCE_CUSTOM.z;
	float yaw       = INSTANCE_CUSTOM.w;

	float t = clamp(VERTEX.y, 0.0, 1.0);
	v_height = t;
	v_hue = hue;

	// 顶端收窄
	float w = blade_width * (1.0 - t * blade_taper);
	VERTEX.x *= w;

	// 每 instance 高度缩放
	VERTEX.y *= h_scale;

	// 绕 Y 轴旋转：叶片朝向 + 法线一起转
	float cy = cos(yaw);
	float sy = sin(yaw);
	vec3 p = VERTEX;
	p.x = VERTEX.x * cy - VERTEX.z * sy;
	p.z = VERTEX.x * sy + VERTEX.z * cy;
	VERTEX = p;

	vec3 n = NORMAL;
	n.x = NORMAL.x * cy - NORMAL.z * sy;
	n.z = NORMAL.x * sy + NORMAL.z * cy;
	NORMAL = n;

	// 风摆：顶端强根部弱 (t^2)，主频 + 阵风低频
	vec2 wd = normalize(wind_dir);
	vec4 world4 = MODEL_MATRIX * vec4(VERTEX, 1.0);
	float primary = sin(TIME * wind_speed + phase + world4.x * 0.35 + world4.z * 0.22);
	float gust    = sin(TIME * gust_speed + phase * 0.5 + world4.x * 0.07);
	float sway    = (primary * wind_amp + gust * gust_amp) * t * t;

	VERTEX.x += wd.x * sway;
	VERTEX.z += wd.y * sway;

	// 叶片整体轻微前倾（视觉密度感）
	VERTEX.x += wd.x * 0.04 * t;
	VERTEX.z += wd.y * 0.04 * t;
}

void fragment() {
	vec3 base = mix(base_color_low, base_color_high, v_height);

	// 暖 #B5A66D 土黄、冷 #4A6356 灰绿（BG3 那种"干植物 + 阴影冷偏"）
	vec3 warm = vec3(0.710, 0.651, 0.427);
	vec3 cool = vec3(0.290, 0.388, 0.337);
	vec3 hue_shift = mix(cool, warm, clamp(v_hue * 0.5 + 0.5, 0.0, 1.0));
	base = mix(base, hue_shift, hue_variation * 0.35);

	// 根部压暗（模拟 AO）
	float ao = mix(0.55, 1.0, v_height);
	base *= ao;

	ALBEDO    = base;
	ROUGHNESS = mix(0.85, 0.55, v_height);
	METALLIC  = 0.0;
	SPECULAR  = 0.15;
	// 兜底亮度：就算光照全灭也能看见草形（EMISSION 只叠一次，不随光源数增长）
	EMISSION  = base * 0.08;
}

void light() {
	// 标准 Lambert
	float ndotl = max(dot(NORMAL, LIGHT), 0.0);
	DIFFUSE_LIGHT += ALBEDO * LIGHT_COLOR * ATTENUATION * ndotl;

	// 背光透射近似：光从叶片背面穿来时透亮（视图空间下 NORMAL / LIGHT / VIEW 同坐标系）
	float back = max(dot(-NORMAL, LIGHT), 0.0);
	float view_back = pow(max(dot(VIEW, -LIGHT), 0.0), 2.0);
	vec3 transmit = sss_color * ATTENUATION * back * view_back * sss_strength;
	DIFFUSE_LIGHT += ALBEDO * LIGHT_COLOR * transmit;
}
