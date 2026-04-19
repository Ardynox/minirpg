// Painterly droplet — DENSITY pass.
//
// This shader does NO lighting / coloring. It runs ONLY in the SubViewport
// dedicated to fluid accumulation. Each droplet (sphere mesh) renders here
// as a soft Gaussian "density blob" with additive blending — multiple
// droplets that overlap in screen space ADD their densities together,
// giving the metaball / SPH-style merging that's the visual hallmark of
// real viscous water rendering ("黏着水", Paint Streams style).
//
// The composite pass (painterly_fluid_composite.gdshader) then reads the
// accumulated density texture and turns it into a continuous fluid
// surface with proper fresnel + environment reflection + sun specular
// + back-buffer refraction — but as ONE unified surface, not as N
// independent droplet shaders.
//
// Output:
//   ALBEDO = vec3(density)   — grayscale density value
//   ALPHA  = 1.0             — fully participate in additive blend

shader_type spatial;
render_mode unshaded, blend_add, depth_test_disabled, cull_disabled,
            depth_draw_never, shadows_disabled;

uniform float blob_strength : hint_range(0.0, 2.0) = 0.55;

varying vec3 v_local;

void vertex() {
	// Mesh is a unit sphere (Radius=0.5, Height=1.0). v_local is in the
	// range [-0.5, 0.5] — we use its length to compute distance from the
	// sphere center.
	v_local = VERTEX;
}

void fragment() {
	// Distance from sphere center, normalized to sphere radius (0..1).
	float r = length(v_local) * 2.0;
	// Gaussian falloff: peaks at center, near-zero at silhouette.
	// exp(-r²·k) with k=4 gives a soft blob with a clear center.
	float density = exp(-r * r * 4.0) * blob_strength;
	ALBEDO = vec3(density);
	ALPHA  = 1.0;
}
