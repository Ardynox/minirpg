shader_type canvas_item;

uniform float strength : hint_range(0.0, 1.0) = 0.45;
uniform float softness : hint_range(0.1, 2.0) = 0.65;

void fragment() {
	vec2 uv = UV;
	float dist = distance(uv, vec2(0.5));
	float vignette = smoothstep(softness, softness - 0.3, dist);
	COLOR = vec4(0.0, 0.0, 0.0, (1.0 - vignette) * strength);
}
