// Painterly 2.5D water block shader.
//
// One material, two surface modes:
//   surface_mode = 0  -> top face. FBM flow-advection height field drives
//                        both vertex displacement and shading.
//   surface_mode = 1  -> side face. Vertical light beams + depth darkening;
//                        the top edge is displaced upward where the top
//                        face has crests, so the block silhouette ripples.
//
// World-space sampling — every block that uses this material samples the
// same noise field so tile seams are invisible.
//
// Time comes from the built-in Godot TIME so no C# ticker is required.
//
// Inspired by P_Malin's "Where the River Goes" shadertoy.

shader_type spatial;
render_mode unshaded, cull_disabled;

// --- Mode ------------------------------------------------------------------
uniform int surface_mode : hint_range(0, 1) = 0;

// --- Debug -----------------------------------------------------------------
// 0=normal, 1=height, 2=normal(rgb), 3=gradient, 4=foam mask,
// 5=fresnel, 6=sparkle, 7=N·L, 8=fbm raw.
uniform int debug_view : hint_range(0, 8) = 0;
// C# pushes this from _Process so we can pause / scrub time without touching
// Godot's built-in TIME.
uniform float time_now = 0.0;

// --- Ripples ---------------------------------------------------------------
// Fixed-size uniform array of active disturbance sources. Each slot:
//   xy = world XZ of the ripple center
//   z  = spawn time (matches time_now at emission)
//   w  = amplitude (0 == slot inactive)
// C# keeps at most 16 active and packs them in; inactive slots have w=0
// and are skipped cheaply in the loop. Adding a ripple is a uniform push,
// no GPU resource churn.
// Slot count was 16 originally (matches click ripples + a sparse trail).
// Bumped to 64 so a continuously-moving emitter (e.g. a sailing boat) can
// shed one ripple every ~0.05s for the full 1.5s lifetime without
// evicting older slots — without this the trail reads as discrete
// "pulses" instead of a continuous wake.
uniform vec4 ripples[64];
uniform float ripple_speed      : hint_range(0.5, 8.0)  = 2.8;   // front m/s
uniform float ripple_width      : hint_range(0.3, 8.0)  = 2.0;   // envelope falloff
uniform float ripple_wavenumber : hint_range(2.0, 40.0) = 14.0;  // rings per 2π m
uniform float ripple_gate       : hint_range(0.02, 0.5) = 0.12;  // leading-edge softness
uniform float ripple_damp       : hint_range(0.0, 2.0)  = 0.6;   // amp loss per meter
uniform float ripple_lifetime   : hint_range(0.2, 4.0)  = 1.5;

// Wake ripples (negative-amp slots — see SpawnWakeRipple) get their OWN
// expansion speed and lifetime. The Kelvin wake of a sailing boat is a
// physics phenomenon: the V-angle equals arcsin(c/v) where c is the
// ripple front speed and v is the boat speed. With the default
// ripple_speed=2.8 m/s and a 0.55 m/s boat, c >> v and there is no V at
// all (each ripple races out as a full circle). For a visible Kelvin V
// we need c < v. The defaults below give arcsin(0.30/0.55) ≈ 33° per
// side. Lifetime is shortened to 0.7s so the wake doesn't spam the
// 64-slot pool and so the trail naturally fades a few hull-lengths
// behind the boat instead of lingering across the entire pool.
uniform float ripple_wake_speed    : hint_range(0.05, 4.0) = 0.30;
uniform float ripple_wake_lifetime : hint_range(0.1, 3.0)  = 0.70;
// Impact-moment realism:
//   crater_depth  -> multiplier over ripple amp for the initial downward bowl
//   crater_radius -> Gaussian sigma (meters)
//   crater_life   -> exp fade time constant
// The crater pulls the surface DOWN at the impact point (water displaced
// outward), then within a few frames the cos packet takes over. Without it,
// the impact feels like it's pushing up, which reads backwards.
uniform float ripple_crater_depth  : hint_range(0.0, 6.0)  = 2.0;
uniform float ripple_crater_radius : hint_range(0.05, 1.0) = 0.18;
uniform float ripple_crater_life   : hint_range(0.02, 0.8) = 0.15;
// Emit ramp: packet amplitude fades IN over this time constant so we don't
// instantly have a full-amplitude ring at t=0 — the ring grows as the crater
// collapses.
uniform float ripple_emit_ramp     : hint_range(0.01, 0.5) = 0.08;
// Coupling between ripple center and bulk flow — rings drift downstream
// instead of staying pinned in world space, which reads unnatural in a river.
uniform float ripple_flow_coupling : hint_range(0.0, 2.0) = 1.0;
// Impact-moment foam burst intensity (separate from surface-crest foam).
uniform float ripple_foam_burst    : hint_range(0.0, 3.0) = 1.2;

// --- Flow ------------------------------------------------------------------
// Declared before ripple_field / ripple_foam_mask because Godot's gdshader
// parser requires uniforms to be declared before they're referenced inside
// a function body.
uniform float speed : hint_range(0.0, 4.0) = 0.6;
uniform vec2  flow_dir = vec2(1.0, 0.25);
uniform float wave_scale : hint_range(0.2, 8.0) = 2.0;
uniform float wave_amp   : hint_range(0.0, 2.0) = 1.0;
uniform float flow_advect : hint_range(0.0, 1.2) = 0.4;

// Actual vertex displacement (meters) on the top face and on the side
// top-edge silhouette.
uniform float displace_strength : hint_range(0.0, 0.4) = 0.15;
// Exaggerates the 2D gradient fed into the top-face lighting normal.
uniform float normal_strength : hint_range(0.1, 8.0) = 3.2;

// Accumulates active ripples' height + analytic gradient wrt world XZ at p.
// Returned as vec3(grad.x, grad.y, h) to match fbm_flow's convention so we
// can just += it into sample_top_field's output.
//
// Waveform: exp envelope * cos packet, gated so the ring cannot exist past
// the current front. This gives the classic "several concentric rings
// trailing behind the expanding front" look instead of a single bulge.
//   h(delta) = amp * exp(-|delta|*width) * cos(delta*wavenumber)
//              * gate(delta) * life(age) * damping(travel)
// where delta = |p - center| - travel.
// Wake-ripple convention: callers may pass a NEGATIVE amplitude to mark a
// ripple as a "wake" emitter (e.g. shed continuously by a sailing boat).
// Wake ripples participate in the cosine packet (the expanding rings) but
// SKIP the crater / residual / foam burst / crown footprint / caustic
// boost — those are all "impact moment" effects whose per-slot pop is
// what makes a high-cadence trail read as "一下一下点" pulses instead of a
// continuous wake.
vec3 ripple_field(vec2 p, float t) {
	vec3 acc = vec3(0.0);
	// Bulk flow drift: ripple centers advect downstream at a fraction of
	// the flow. Computed once outside the loop so all 64 slots share it.
	vec2 drift_dir = flow_dir * speed * ripple_flow_coupling * 0.15;
	for (int i = 0; i < 64; i++) {
		vec4 r = ripples[i];
		float amp = abs(r.w);
		if (amp < 0.0001) continue;
		bool is_wake = (r.w < 0.0);
		// Wake ripples use a slower expansion speed and shorter lifetime
		// so the V-shape Kelvin wake (arcsin(c/v) angle) is actually
		// visible — without these overrides every wake ripple would race
		// out of the pool in 0.5 frames and the trail would just be one
		// big circle per spawn.
		float eff_lifetime = is_wake ? ripple_wake_lifetime : ripple_lifetime;
		float eff_speed    = is_wake ? ripple_wake_speed    : ripple_speed;
		float age = t - r.z;
		if (age < 0.0 || age > eff_lifetime) continue;

		// Advect the center downstream as the ripple ages.
		vec2 center = r.xy + drift_dir * age;
		vec2 d = p - center;
		float dist = length(d) + 1e-4;

		float travel = age * eff_speed;
		float delta = dist - travel;

		// Envelope (symmetric exp).
		float env = exp(-abs(delta) * ripple_width);
		// Cosine packet -> multiple concentric rings.
		float k = ripple_wavenumber;
		float phase = cos(delta * k);
		// Leading-edge gate: kills any residual oscillation ahead of
		// the expanding front (delta > 0) so it reads as "ripple
		// caught up to here" not "ripple exists everywhere".
		float gate = 1.0 - smoothstep(0.0, ripple_gate, delta);
		// Temporal fade — wake ripples use the shorter wake lifetime.
		float life = 1.0 - smoothstep(0.0, eff_lifetime, age);
		// Distance damping — ripples lose energy as they spread.
		float damp = 1.0 / (1.0 + travel * ripple_damp);
		// Emit ramp — packet starts at 0 amplitude and grows as the
		// crater collapses, so we don't pop in a full ring at t=0.
		// Wake ripples skip this (they have no crater to collapse from);
		// without the skip the high-cadence stream of fresh ripples each
		// fades up over ~0.08s and reads as a strobing brightness wobble.
		float emit = is_wake ? 1.0 : (1.0 - exp(-age / max(ripple_emit_ramp, 0.001)));

		float scale = amp * life * damp * emit;
		float h_pkt = scale * env * phase * gate;

		// Analytic derivative of the packet wrt delta (skipping gate
		// derivative — narrow band, not visually load-bearing).
		float d_env   = -ripple_width * sign(delta) * env;
		float d_phase = -k * sin(delta * k);
		float dh_pkt_ddelta = scale * (d_env * phase + env * d_phase) * gate;
		vec2 dh_pkt_dp = dh_pkt_ddelta * (d / dist);

		acc.z  += h_pkt;
		acc.xy += dh_pkt_dp;

		// --- Per-impact effects (NOT for wake ripples) ----------------
		// Crater + residual are what make a single ripple read as "an
		// object hit the water". Stacked at 20Hz they become the visible
		// pulse train. Wake ripples carry only the packet rings.
		if (!is_wake) {
			// --- Crater (central depression) ---
			// Gaussian bowl at the impact point, fading over ripple_crater_life.
			// Negative height (water displaced outward -> surface dips).
			//   h_c(p) = -amp * depth * exp(-age/life) * exp(-r²/σ²)
			float sigma2 = max(ripple_crater_radius * ripple_crater_radius, 1e-4);
			float crater_time  = exp(-age / max(ripple_crater_life, 0.001));
			float crater_space = exp(-dot(d, d) / sigma2);
			float h_crater = -amp * ripple_crater_depth * crater_time * crater_space;
			vec2  dh_crater_dp = h_crater * (-2.0 * d / sigma2);

			// --- Residual depression (long tail) ---
			// Wider, shallower bowl that fades linearly over the full
			// ripple_lifetime so the surface "remembers" the impact
			// after the sharp crater is gone.
			float sigma2_res = sigma2 * 4.0;
			float residual_time  = 1.0 - smoothstep(0.0, ripple_lifetime, age);
			float residual_space = exp(-dot(d, d) / sigma2_res);
			float h_residual = -amp * 0.08 * residual_time * residual_space;
			vec2  dh_residual_dp = h_residual * (-2.0 * d / sigma2_res);

			acc.z  += h_crater + h_residual;
			acc.xy += dh_crater_dp + dh_residual_dp;
		}
	}
	return acc;
}

// Mask of impact-moment foam: a quick burst at the center (first ~0.25s) plus
// a thin ring riding the wavefront. Independent of the crest-based surface
// foam so the impact reads as a distinct event.
float ripple_foam_mask(vec2 p, float t) {
	float mask = 0.0;
	vec2 drift_dir = flow_dir * speed * ripple_flow_coupling * 0.15;
	for (int i = 0; i < 64; i++) {
		vec4 r = ripples[i];
		// Wake ripples (encoded with NEGATIVE amp by the boat) are
		// intentionally filtered out here — the burst/crown/front-foam
		// are pulse-style "impact moment" effects and would re-introduce
		// the per-ripple flashes we're trying to avoid in a continuous
		// trail.
		if (r.w < 0.0001) continue;
		float age = t - r.z;
		if (age < 0.0 || age > ripple_lifetime) continue;

		vec2 center = r.xy + drift_dir * age;
		float dist = length(p - center);

		// Central splash burst (first ~0.25s, Gaussian falloff).
		float burst_radius = ripple_crater_radius * 1.6;
		float burst_life = 0.25;
		float burst_time = exp(-age / burst_life) * (1.0 - smoothstep(0.0, burst_life * 3.0, age));
		float burst_space = exp(-(dist * dist) / max(burst_radius * burst_radius, 1e-4));
		mask = max(mask, r.w * ripple_foam_burst * burst_time * burst_space);

		// Wave-front foam crest — thin ring at the leading edge.
		float travel = age * ripple_speed;
		float front = exp(-abs(dist - travel) * 6.0);
		float front_life = 1.0 - smoothstep(0.0, 0.6, age);
		mask = max(mask, r.w * ripple_foam_burst * 0.4 * front * front_life);

		// Crown footprint — bright, sharp, fast-expanding wreath at impact.
		// This is the surface counterpart of the airborne CPU crown
		// particles: in real splashes the crown sheet briefly draws a hot
		// white ring on the water itself before the wave front takes over.
		// Borrowed from how stylized splash shaders sell the impact moment
		// (Sea of Stars / Tunic-style "wreath" pop). Lives ~0.18s, expands
		// at 2.4 m/s — much faster + tighter than the wave-front foam, so
		// they layer into a "burst then ripple" sequence instead of one
		// soft blur.
		float crown_radius = age * 2.4;
		float crown_ring = exp(-abs(dist - crown_radius) * 12.0);
		float crown_life = 1.0 - smoothstep(0.0, 0.18, age);
		mask = max(mask, r.w * ripple_foam_burst * 0.9 * crown_ring * crown_life);
	}
	return clamp(mask, 0.0, 1.0);
}

// --- Colors ----------------------------------------------------------------
uniform vec3 shallow_color : source_color = vec3(0.34, 0.46, 0.44);
uniform vec3 deep_color    : source_color = vec3(0.05, 0.10, 0.12);
uniform vec3 foam_color    : source_color = vec3(0.90, 0.92, 0.88);
uniform vec3 floor_color   : source_color = vec3(0.34, 0.32, 0.22);
uniform vec3 sun_color     : source_color = vec3(1.25, 1.10, 0.85);
uniform vec3 sky_color     : source_color = vec3(0.42, 0.55, 0.62);

// --- Lighting --------------------------------------------------------------
uniform vec3  sun_dir = vec3(-0.6, 0.75, 0.3);
uniform float specular_power : hint_range(4.0, 128.0) = 48.0;
uniform float specular_gain  : hint_range(0.0, 4.0)   = 1.4;

// --- Painterly ------------------------------------------------------------
uniform float foam_gain     : hint_range(0.0, 3.0) = 1.4;
uniform float foam_threshold : hint_range(0.0, 1.0) = 0.38;
uniform float posterize_steps : hint_range(1.0, 12.0) = 7.0;
uniform float posterize_strength : hint_range(0.0, 1.0) = 0.25;
uniform float fresnel_strength : hint_range(0.0, 1.0) = 0.55;
uniform float sparkle_gain : hint_range(0.0, 3.0) = 1.1;

// --- Side face -------------------------------------------------------------
uniform float block_height : hint_range(0.2, 4.0) = 1.3;
uniform float beam_intensity : hint_range(0.0, 3.0) = 1.15;
uniform float beam_contrast  : hint_range(0.5, 6.0) = 2.6;
uniform float floor_fade     : hint_range(0.0, 1.0) = 0.55;
uniform float waterline_glow : hint_range(0.0, 3.0) = 1.25;

// --- Hash / noise ---------------------------------------------------------
float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yzx + 19.19);
	return fract((p3.x + p3.y) * p3.z);
}

float vnoise(vec2 o) {
	vec2 p = floor(o);
	vec2 f = fract(o);
	float a = hash12(p);
	float b = hash12(p + vec2(1.0, 0.0));
	float c = hash12(p + vec2(0.0, 1.0));
	float d = hash12(p + vec2(1.0, 1.0));
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

// FBM with 2D derivatives + flow advection.
// Returns vec3(dh/dx, dh/dy, height). `t` is threaded in as a parameter
// because Godot's TIME built-in is only visible in the top-level vertex /
// fragment functions, not inside helper functions on every driver.
vec3 fbm_flow(vec2 p, vec2 flow, float persistence, float advect) {
	vec3 acc = vec3(0.0);
	float tot = 0.0;
	float a = 1.0;
	vec2 pp = p;
	vec2 ff = flow;
	for (int i = 0; i < 5; i++) {
		pp += ff;
		float h_center = vnoise(pp);
		float h_dx = vnoise(pp + vec2(0.15, 0.0));
		float h_dy = vnoise(pp + vec2(0.0, 0.15));
		vec2  g = vec2(h_dx - h_center, h_dy - h_center);
		pp += g * advect;

		acc.z += h_center * a;
		acc.xy += g * a;
		tot += a;
		a *= persistence;
		ff = -ff * 0.85;
		pp *= 2.03;
	}
	return acc / tot;
}

// Top-surface wave field at arbitrary world XZ. `t` is the current time.
//
// Xl2XRW-inspired two-layer field: two continuously-advecting FBM layers
// with different flow speeds and a constant seed offset, weighted by a
// phase-shifted sine pair. Both weights sum to 1.0 and the flow never
// jumps, so there's no visible "pop" per period — the river just keeps
// re-shaping itself because the two layers beat against each other.
vec3 sample_top_field(vec2 world_xz, float t) {
	vec2 p = world_xz * wave_scale;
	float tt = speed * t * 0.22;
	vec2 f_a = flow_dir * tt;
	vec2 f_b = flow_dir * tt * 0.62; // slower back-current
	vec3 a = fbm_flow(p,                     f_a, 0.55, flow_advect);
	vec3 b = fbm_flow(p + vec2(13.37, 7.21), f_b, 0.55, flow_advect);
	float w = 0.5 + 0.5 * sin(tt * 6.2831853);
	vec3 base = mix(a, b, w) * wave_amp;

	// Ripples live in world space (not scaled by wave_scale), so sample
	// with raw world_xz and add on top. Gradient axes match because
	// ripple_field returns analytic derivatives wrt world_xz directly.
	base += ripple_field(world_xz, t);
	return base;
}

// --- Vertex --------------------------------------------------------------
varying vec3 v_world;
varying float v_wave_h;
varying vec2  v_wave_grad;

void vertex() {
	float t = time_now;
	vec3 world_pre = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;

	if (surface_mode == 0) {
		// Top face: local +Y == world +Y (the top plane is not rotated in
		// the scene), so a direct VERTEX.y += h * displace_strength works.
		vec3 field = sample_top_field(world_pre.xz, t);
		float h = field.z;
		VERTEX.y += h * displace_strength;
		world_pre.y += h * displace_strength;
		v_wave_h = h;
		v_wave_grad = field.xy;
	} else {
		// Side face: displace ONLY the top edge upward (world +Y) to match
		// the top face's wave height at this world.xz, so the block's
		// silhouette undulates. The scene rotates side planes with Euler
		// (90, *, 0): after that rotation, local -Z maps to world +Y for
		// every side. So "world +Y displacement" becomes "VERTEX.z -=".
		float depth_uv_pre = clamp(-world_pre.y / block_height, 0.0, 1.0);
		// 1 at the waterline, 0 a short way below. Tight falloff so only
		// the top strip deforms — the rest of the wall stays flat.
		float edge = 1.0 - smoothstep(0.0, 0.08, depth_uv_pre);
		vec3 field = sample_top_field(world_pre.xz, t);
		float h = field.z;
		VERTEX.z -= h * displace_strength * edge;
		world_pre.y += h * displace_strength * edge;
		v_wave_h = h;
		v_wave_grad = field.xy;
	}

	v_world = world_pre;
}

// --- Fragment helpers ----------------------------------------------------
vec3 posterize(vec3 col, float steps, float strength) {
	vec3 quant = floor(col * steps) / steps;
	return mix(col, quant, strength);
}

// --- Fragment ------------------------------------------------------------
void fragment() {
	float t = time_now;
	vec3 L = normalize(sun_dir);
	vec3 cam_pos = INV_VIEW_MATRIX[3].xyz;
	vec3 V = normalize(cam_pos - v_world);

	vec3 out_color;

	if (surface_mode == 0) {
		// ---- TOP FACE ----
		float h = v_wave_h;
		vec2  grad_big = v_wave_grad * normal_strength;

		// Fine-octave detail purely for the normal — sparkle without
		// changing silhouette.
		vec3 detail = fbm_flow(v_world.xz * wave_scale * 3.1,
		                       flow_dir * speed * t * 0.18,
		                       0.5, flow_advect * 0.6);
		vec2 grad_detail = detail.xy * normal_strength * 0.6;
		vec2 grad = grad_big + grad_detail;

		vec3 N = normalize(vec3(-grad.x, 1.0, -grad.y));

		// Base shallow/deep by wave height.
		float h_norm = clamp(h * 0.9 + 0.45, 0.0, 1.0);
		vec3 base = mix(deep_color, shallow_color, h_norm);

		// Fresnel — grazing angles pull in the sky color. This is the main
		// fix for the "flat painted top" look; water reads as water the
		// moment you can see it get brighter toward the far edge.
		float fres = pow(1.0 - max(dot(N, V), 0.0), 5.0);
		base = mix(base, sky_color, fres * fresnel_strength);

		// Sky / ambient by N.y (kept, but scaled back since fresnel now
		// does most of the heavy lifting).
		float sky_term = clamp(N.y * 0.5 + 0.5, 0.0, 1.0);
		base += sky_color * 0.12 * sky_term;

		// Sun diffuse.
		float ndl = max(dot(N, L), 0.0);
		base += sun_color * ndl * 0.28;

		// Sun specular (sharp crest glints).
		vec3 R = reflect(-L, N);
		float spec = pow(max(dot(R, V), 0.0), specular_power) * specular_gain;
		base += sun_color * spec;

		// Sparkle: two offset high-freq noise layers with different time
		// rates, pow'd hard so only the tips fire. Different time rates
		// (not locked to flow_dir) give a true "twinkle" instead of a
		// drifting texture. Gated by ndl so only the lit side sparkles.
		vec2 sp_uv_a = v_world.xz * wave_scale * 6.0 + vec2(t * 0.6, t * 0.4);
		vec2 sp_uv_b = v_world.xz * wave_scale * 9.3 + vec2(-t * 0.9, t * 1.1);
		float sp_a = pow(clamp(vnoise(sp_uv_a), 0.0, 1.0), 24.0);
		float sp_b = pow(clamp(vnoise(sp_uv_b), 0.0, 1.0), 28.0);
		float sparkle = (sp_a + sp_b * 0.7) * ndl * sparkle_gain;
		base += sun_color * sparkle;

		// Foam: broad whitecaps only on real peaks (high h AND high gradient).
		// The previous fwidth-based "constant-width crest line" was comparing
		// abs(h - 0.55) which just marked the mean water level, drowning the
		// whole surface in foam.
		float grad_mag = length(grad);
		float h_peak = smoothstep(0.58, 0.78, h);
		float foam_mask = h_peak * smoothstep(foam_threshold, foam_threshold + 0.35,
		                                       grad_mag * foam_gain * 0.35);
		// Impact splash foam — separate mask so the center whites out
		// briefly at entry even if local gradient is low.
		float burst_mask = ripple_foam_mask(v_world.xz, t);
		foam_mask = max(foam_mask, burst_mask);
		base = mix(base, foam_color, foam_mask * 0.55);

		base = posterize(base, posterize_steps, posterize_strength);
		out_color = base;

		// Debug visualizations replace out_color so we can inspect each
		// contribution in isolation. Side face ignores this and renders
		// normally so we always have a reference frame to compare against.
		if (debug_view == 1) out_color = vec3(h * 0.5 + 0.5);
		else if (debug_view == 2) out_color = N * 0.5 + 0.5;
		else if (debug_view == 3) out_color = vec3(grad_mag);
		else if (debug_view == 4) out_color = vec3(foam_mask);
		else if (debug_view == 5) out_color = vec3(fres);
		else if (debug_view == 6) out_color = vec3(clamp(sparkle, 0.0, 1.0));
		else if (debug_view == 7) out_color = vec3(ndl);
		else if (debug_view == 8) out_color = vec3(v_wave_h * 0.5 + 0.5);
	} else {
		// ---- SIDE FACE ----
		float depth_uv = clamp(-v_world.y / block_height, 0.0, 1.0);

		// Base depth ramp: shallow -> deep -> floor.
		vec3 water = mix(shallow_color, deep_color, depth_uv);
		float floor_mix = smoothstep(1.0 - floor_fade, 1.0, depth_uv);
		vec3 base = mix(water, floor_color, floor_mix);

		// Cheap caustics on the pool floor — classic two-sin "dancing
		// light bands" trick (popularised by iq's water shaders on
		// Shadertoy). No real light projection, just two perpendicular
		// sin waves whose product gives a moving checker-of-spots; pow'd
		// hard so only the bright peaks fire. Multiplied by floor_mix so
		// it ONLY appears down on the pool floor, not in the water column.
		float caustic_a = sin(v_world.x * 4.5 + t * 1.7);
		float caustic_b = sin(v_world.z * 5.2 - t * 1.3);
		float caustic = pow(max(caustic_a * caustic_b, 0.0), 3.0);

		// Splash boost: when a fresh ripple is overhead, the caustics
		// flare for ~0.6s (the surface above is concentrating light into
		// the floor). This couples the surface event to the depth feel —
		// you SEE something hit the water in the underwater silhouette.
		float ripple_boost = 0.0;
		for (int i = 0; i < 64; i++) {
			vec4 r = ripples[i];
			// Same wake-filter trick as ripple_foam_mask: negative amp
			// means "boat wake — no impact flash, no caustic boost".
			if (r.w < 0.0001) continue;
			float age = t - r.z;
			if (age < 0.0 || age > 0.6) continue;
			float dist = length(v_world.xz - r.xy);
			ripple_boost += r.w * (1.0 - age / 0.6) * exp(-dist * 1.2);
		}
		ripple_boost = clamp(ripple_boost * 3.0, 0.0, 1.0);

		float caustic_intensity = 0.20 + ripple_boost * 0.55;
		base += sun_color * caustic * floor_mix * caustic_intensity;

		// Vertical beams driven by 1-D noise over a horizontal world coord
		// so the same shaft persists top-to-bottom (parallel streaks).
		float horizontal = v_world.x * 1.3 + v_world.z * 1.7;
		float beam_time = t * speed * 0.22;
		float b0 = vnoise(vec2(horizontal * 2.6 + beam_time, 0.17));
		float b1 = vnoise(vec2(horizontal * 5.1 - beam_time * 0.7, 4.83));
		float b2 = vnoise(vec2(horizontal * 9.5 + beam_time * 1.4, 9.11));
		float beam_seed = (b0 * 0.55 + b1 * 0.30 + b2 * 0.15);
		float beam_band = pow(clamp(beam_seed, 0.0, 1.0), beam_contrast);
		float beam_fade = 1.0 - depth_uv;
		beam_fade = beam_fade * beam_fade;
		float beam = beam_band * beam_fade * beam_intensity;

		// Waterline shimmer using the top-face wave height here.
		float top_h = v_wave_h;
		float waterline = smoothstep(0.0, 0.02, depth_uv) * smoothstep(0.22, 0.02, depth_uv);
		float shimmer = (top_h * 0.5 + 0.5);
		base += foam_color * waterline * shimmer * waterline_glow * 0.65;

		// Beams (additive).
		base += mix(sky_color, sun_color, 0.5) * beam * 0.8;

		// Horizontal brushstrokes for painterly feel.
		float brush = vnoise(vec2(v_world.y * 22.0, horizontal * 0.9));
		base *= mix(0.88, 1.08, brush);

		// Light posterize so beams survive.
		base = posterize(base, posterize_steps, posterize_strength * 0.35);

		out_color = base;
	}

	ALBEDO = out_color;
	ALPHA = 1.0;
}
