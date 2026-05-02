// Blade 根部 AO decal（真·3D shader 草，Phase 1）。
//
// 每根 blade 在 base 位置（屏幕空间）下方铺一个椭圆形软阴影，
// 让草叶"坐"在 dirt 顶面上而不是视觉悬浮。
//
// 几何约定：
//   Mesh = unit quad，顶点 x ∈ [-0.5, 0.5]，y ∈ [-0.5, 0.5]，中心 (0,0)。
//   UV: (x+0.5, y+0.5) → 片段内 UV ∈ [0,1]，(0.5, 0.5) 为中心。
//   MODEL_MATRIX 只做平移（origin = blade base screen pos），scale/rotation 在 shader 里做。
//
// Per-instance 数据打包：
//   INSTANCE_CUSTOM.xy = decal_half_size_px（椭圆外接矩形半长，x 横、y 纵）
//   INSTANCE_CUSTOM.z  = decal_darkness   // 0..1，alpha 上限
//   INSTANCE_CUSTOM.w  = reserved
//   COLOR              = 阴影色（base_color.rgb * darkness）
//
// 绘制顺序：AO MultiMeshInstance2D 的 z_index 设为 blade 下 1 层，确保 dirt < AO < blade。

shader_type canvas_item;
render_mode blend_mix, unshaded;

uniform float  softness = 0.55;    // 外圈柔化宽度（0=硬边，1=全柔）

varying vec2  v_uv;
varying float v_darkness;

void vertex() {
	vec2 half_size = INSTANCE_CUSTOM.xy;
	// mesh 顶点已在 [-0.5, 0.5]，直接乘以 2 × half_size = 直径像素数
	VERTEX = vec2(VERTEX.x * 2.0 * half_size.x, VERTEX.y * 2.0 * half_size.y);

	v_uv        = UV;
	v_darkness  = clamp(INSTANCE_CUSTOM.z, 0.0, 1.0);
}

void fragment() {
	// 椭圆内 = 距中心 (0.5, 0.5) 的归一化距离 <= 1
	vec2  d    = (v_uv - vec2(0.5, 0.5)) * 2.0;  // d ∈ [-1, 1]
	float r    = length(d);

	// 软 → 硬：r=0 中心最实，r=1 外缘淡出
	float fall = 1.0 - smoothstep(1.0 - softness, 1.0, r);
	if (r > 1.0) {
		discard;
	}

	float alpha = fall * v_darkness * COLOR.a;
	COLOR = vec4(COLOR.rgb, alpha);
}
