// 真·3D shader 草叶（2D 等距伪 3D，Phase 1）。
//
// 几何约定：
//   Mesh = 单个 unit quad，顶点 (±0.5, 0) 为 blade 根部、(±0.5, 1) 为 blade 尖。
//   MODEL_MATRIX 只做平移（MultiMesh instance transform 的 origin=screen_pos，
//   scale=1、rotation=0），所有"按像素"的几何变换在本 shader 里做。
//
// Per-instance 数据打包：
//   INSTANCE_CUSTOM.x = windPhase         // 0..2π，每根 blade 的风摆初相
//   INSTANCE_CUSTOM.y = tilt_rad          // 每根 blade 相对屏幕上方的初始倾角（弧度）
//   INSTANCE_CUSTOM.z = paletteIdx        // 0..7，选 blade_palette 哪一档
//   INSTANCE_CUSTOM.w = hueJitter         // -0.5..0.5，per-blade 色相微扰
//   COLOR.r           = blade_width_px    // blade 底宽（像素）
//   COLOR.g           = blade_height_px   // blade 总高（像素）
//   COLOR.b           = reserved (1.0)
//   COLOR.a           = 1.0 (debug alpha multiplier，默认 1)
//
// Uniform 通道（全局一份，不随 instance 变）：
//   wind_speed     : 风频率（Hz）。0 = 不摆。
//   wind_amp       : 风振幅（像素）。
//   wind_dir       : 风向（屏幕单位向量）。
//   sun_dir        : 太阳方向（屏幕单位向量，y < 0 表示从上方打光）。
//   sun_color      : 太阳色；日夜循环通过它影响草尖 tint。
//   ambient        : 背光面下限（0..1），避免阴影全黑。
//   blade_palette  : 8 档 blade 基色，index 来自 INSTANCE_CUSTOM.z。
//
// 时间来源：Godot 内建 TIME（单位秒），无需额外 uniform。

shader_type canvas_item;
render_mode blend_mix, unshaded;

uniform float  wind_speed        = 1.8;
uniform float  wind_amp          = 2.2;
uniform vec2   wind_dir          = vec2(1.0, 0.15);
uniform vec2   sun_dir           = vec2(0.35, -0.94);
uniform vec4   sun_color : source_color = vec4(1.0, 0.96, 0.86, 1.0);
uniform float  ambient           = 0.55;       // 暗部下限，避免背光草全黑
uniform float  root_darken       = 0.55;       // 草根压暗系数（1=不动，越小越暗）
uniform float  tip_boost         = 1.25;       // 草尖提亮系数
uniform vec4   blade_palette[8] : source_color;

// ── Shadertoy Xsf3zX 风格扩展 ──
// 正交副风：主风沿 wind_dir，副风沿其法线方向；两路频率比 ≈ 1:2.1 做出自然不规则感。
// 参考 DE() 里 sin(iTime*1.3+1.5*p.z), sin(iTime*2.6+1.5*p.x) 的双轴摆动。
uniform float  cross_wind_gain   = 0.35;       // 副风强度相对主风（0=单轴，1=等量）
uniform float  cross_wind_ratio  = 2.1;        // 副风频率倍率

// ── 草尖漂白（pale tip）──
// Shadertoy: pow(ret.y, 9.0)*.7 混向 vec3(.35,.35,near-white)，只作用于极少数 blade（voronoi id 随机）。
// 这里用 |hue_jitter| > threshold 挑出 ~10%~30% blade，尖端漂白到阳光色。
uniform float  tip_bleach_threshold = 0.32;    // |hueJitter| 超过此值才开始漂白（0..0.5）
uniform float  tip_bleach_strength  = 0.75;    // 混向 bleach_color 的最大比例（0..1）
uniform vec3   tip_bleach_color     : source_color = vec3(0.92, 0.90, 0.62);

// ── blade 锥形收口（taper）──
// 真实草叶根粗尖细：根部=COLOR.r 全宽，尖端=width * tip_width_ratio。
// taper_curve 控制收口弧度：1.0=线性；<1 根部更鼓（bulge）；>1 根部更瘦、尖端更锋利。
uniform float  tip_width_ratio   = 0.15;       // 尖端宽 / 根部宽（0..1）
uniform float  taper_curve       = 0.55;       // 收口曲率（<1 根部饱满）

// ── 亚像素 LOD（解决远景闪光点 aliasing）──
// 当 blade 屏幕宽度 <1 px 时，rasterize 覆盖率会随抖动变 → 每帧闪烁（flicker）。
// 策略：把 blade 物理增粗到屏幕上至少 lod_min_screen_w 像素，同时按 1/√k 衰减 alpha
//      保持"视觉密度"不变——增粗 4 倍则每根变半透明 50%，叠起来总能量守恒。
// zoom_scale 由 CPU 每帧推送：camera.Zoom.x（canvas 下 MODEL_MATRIX 不含 zoom）。
uniform float  zoom_scale        = 1.0;        // camera.Zoom.x；<1 表示缩远、blade 在屏幕上变小
uniform float  lod_min_screen_w  = 1.2;        // 屏幕上保底宽度（px）；低于此则增粗
uniform float  lod_alpha_floor   = 0.30;       // alpha 衰减地板，避免完全消失

varying vec2  v_uv;                // UV 转发（root_tipness 判断）
varying float v_palette_idx;
varying float v_hue_jitter;
varying float v_tip_sun_dot;       // 顶点阶段算好传片段，避免片段重复 dot
varying float v_tip_bleach;        // 0..1，本 blade 尖端漂白系数（Shadertoy pale-tip 风）

void vertex() {
	// VERTEX 是 mesh 本地坐标：x ∈ [-0.5, 0.5]，y ∈ [0, 1]，y=0 根 / y=1 尖。
	// UV 我们在 CPU 端设为 (x+0.5, 1-y)，这样片段里 UV.y=1 表根、UV.y=0 表尖。
	float rootness = UV.y;             // 1 at root, 0 at tip
	float tipness  = 1.0 - rootness;   // 0 at root, 1 at tip

	float width_px  = COLOR.r;
	float height_px = COLOR.g;
	float phase     = INSTANCE_CUSTOM.x;
	float tilt_rad  = INSTANCE_CUSTOM.y;

	// 1) 把 local quad 放大到像素尺寸；同时按 rootness 做锥形收口（根粗尖细）。
	//    taper ∈ [tip_width_ratio, 1.0]：根部=1 全宽，尖端=tip_width_ratio。
	//    pow(rootness, taper_curve) 给一点点非线性，让根部微微鼓起，不是死板的等腰三角。
	float taper = mix(tip_width_ratio, 1.0, pow(rootness, taper_curve));
	vec2 p = vec2(VERTEX.x * width_px * taper, VERTEX.y * height_px);

	// 2) 初始 tilt：沿 blade 长度线性偏移，tip 偏 sin(tilt)*height 像素
	p.x += sin(tilt_rad) * height_px * tipness;

	// 3) 风摆：只顶端动（tipness²），方向沿 wind_dir 屏幕单位向量。
	//    次级高频抖动让草尖有细碎颤动感（真实草在风里不会只做一个主摆）。
	//    正交副风（Shadertoy 双轴 sway 风格）：沿 wind_dir 法向、频率 ~2×，让摆动不再是严格单轴。
	vec2  wd_main  = normalize(wind_dir);
	vec2  wd_cross = vec2(-wd_main.y, wd_main.x);
	float sway_main = sin(TIME * wind_speed + phase) * wind_amp;
	float sway_hi   = sin(TIME * wind_speed * 3.1 + phase * 1.7) * (wind_amp * 0.25);
	float sway_cross = sin(TIME * wind_speed * cross_wind_ratio + phase * 0.7) * (wind_amp * cross_wind_gain);
	float tip_bend   = tipness * tipness;
	p += wd_main  * (sway_main + sway_hi) * tip_bend;
	p += wd_cross *  sway_cross           * tip_bend;

	// 4) y 翻转：mesh y=1 是尖；屏幕上尖要朝上 → 翻负号。
	p.y = -p.y;

	// 5) 写回 VERTEX，让 Godot 的 MODEL_MATRIX（origin=screen_pos）做最后平移。
	VERTEX = p;

	// 6) 日光→草尖 dot：尖端更朝向光源方向时 lit 量高。
	//    approximate blade 法向为指向上方（屏幕 -y）并按 tilt 偏移。
	vec2 blade_normal = normalize(vec2(sin(tilt_rad), -1.0));
	v_tip_sun_dot     = max(dot(blade_normal, -sun_dir), 0.0);

	v_uv          = UV;
	v_palette_idx = INSTANCE_CUSTOM.z;
	v_hue_jitter  = INSTANCE_CUSTOM.w;

	// 7) 尖端漂白检测：只有 |hueJitter| 超过阈值的少数 blade 才会漂白，做出 Shadertoy 里的 pale-tip outlier 感。
	//    smoothstep 给一段软过渡，让漂白/不漂白 blade 之间不会有突兀分界。
	v_tip_bleach  = smoothstep(tip_bleach_threshold, 0.5, abs(INSTANCE_CUSTOM.w));
}

void fragment() {
	float rootness = v_uv.y;           // 1 at root, 0 at tip
	float tipness  = 1.0 - rootness;

	// --- blade 横向边缘 anti-alias ---
	// UV.x ∈ [0,1]：中心 0.5，边缘 0/1。用 smoothstep 把边缘像素做 1 px 软过渡。
	float centerness = 1.0 - abs(v_uv.x - 0.5) * 2.0;   // 1 at center, 0 at edge
	float edge_mask  = smoothstep(0.0, 0.35, centerness);

	// --- 基色从 palette 取 + per-blade 色相微扰 ---
	int idx = clamp(int(v_palette_idx + 0.5), 0, 7);
	vec4 base_col = blade_palette[idx];

	// 根→尖 value ramp：
	//   根部 0.55 × base（压暗做 AO），
	//   尖端 1.25 × base（高光），
	//   中段平滑过渡（smoothstep 防线性带状感）
	float value_ramp = mix(root_darken, tip_boost, smoothstep(0.0, 1.0, tipness));

	// --- 日夜着色：草尖朝光面泛 sun_color；背光面落到 ambient ---
	float lit = mix(ambient, 1.0, v_tip_sun_dot);
	vec3  lit_tint = mix(vec3(1.0), sun_color.rgb, tipness * v_tip_sun_dot);

	// --- 微 hue 扰动（HSV 空间的廉价近似：直接在 RGB 通道推拉一点）---
	vec3 hue_shift = vec3(
		-v_hue_jitter * 0.04,
		 v_hue_jitter * 0.05,
		-v_hue_jitter * 0.03
	);

	vec3 col = base_col.rgb;
	col *= value_ramp;
	col *= lit;
	col *= lit_tint;
	col  = clamp(col + hue_shift, vec3(0.0), vec3(1.0));

	// --- 尖端漂白（Shadertoy pale-tip）---
	// Shadertoy 原式：pow(ret.y, 9.0) * .7 → 只在最尖端 ~20% 的像素起作用，非常锐利。
	// 这里用 pow(tipness, 7.0) 模仿同款收敛，避免把半根草都刷白。
	float bleach_mask = v_tip_bleach * pow(tipness, 7.0) * tip_bleach_strength;
	col = mix(col, tip_bleach_color, bleach_mask);

	// --- alpha：根部实，尖端略透（模拟草尖透光）+ 横向 anti-alias ---
	float alpha_ramp = mix(1.0, 0.80, tipness);
	float alpha      = alpha_ramp * edge_mask * base_col.a * COLOR.a;

	COLOR = vec4(col, alpha);
}
