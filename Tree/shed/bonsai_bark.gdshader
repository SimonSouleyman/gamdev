shader_type spatial;
// The bonsai's wood: the photo bark (tinted per species) with three marks from the vertex
// colours BonsaiView writes per node (BranchMeshBuilder.node_colors):
//   r = jin: the whole segment is silver deadwood, bleached with long grain;
//   g = shari: a strip of the trunk stripped to silver wood (UV2.x is the angle around it);
//   b = wire scar: a spiral groove where a wire bit into the bark.
// A juniper's bark (fibre > 0) peels in long red-brown strips that spiral up the trunk (twist),
// and its shari follows the same spiral, like the reference's twisting deadwood.

uniform sampler2D bark_albedo : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D bark_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform vec3 texture_tint : source_color = vec3(0.52, 0.36, 0.28);
uniform vec3 deadwood : source_color = vec3(0.66, 0.64, 0.6);
// One wire turn in UV.y units (the wire's pitch times the bark tiling).
uniform float scar_pitch = 0.2;
uniform float rim_strength = 0.08;
// Turns of the grain around the wood per UV.y unit, and the strength of the stringy strips.
uniform float twist = 0.0;
uniform float fibre = 0.0;

float hash1(float n) {
	return fract(sin(n * 127.1) * 43758.5453);
}

void fragment() {
	// The grain spirals: around (0..1) shifted along the wood.
	float around = UV2.x + UV.y * twist;
	vec2 buv = vec2(UV.x + UV.y * twist * 3.0, UV.y);
	// Stretched along the wood when stringy.
	vec2 suv = vec2(buv.x, buv.y * mix(1.0, 0.35, fibre));
	vec3 bark = texture(bark_albedo, suv).rgb * texture_tint;
	vec3 nrm = texture(bark_normal, suv).rgb;
	if (fibre > 0.0) {
		// Long strips: each a slightly different red-brown, dark cracks between them.
		float lanes = buv.x * 7.0 + 0.35 * sin(UV.y * 5.0 + floor(buv.x * 7.0));
		float lane = floor(lanes);
		float f = fract(lanes);
		float crack = smoothstep(0.1, 0.0, f) + smoothstep(0.9, 1.0, f);
		vec3 strip_col = mix(vec3(0.8, 0.74, 0.7), vec3(1.1, 0.92, 0.82), hash1(lane + floor(UV.y * 1.3)));
		bark = mix(bark, bark * strip_col, fibre);
		bark *= 1.0 - 0.55 * crack * fibre;
		nrm.x = mix(nrm.x, 0.5 + (f - 0.5) * 0.5, 0.5 * fibre);
	}
	float jin = COLOR.r;
	// Shari: a strip down one side (narrower where it fades out below the cut), ragged edges.
	float edge = 0.03 * sin(UV.y * 9.0) + 0.02 * sin(UV.y * 23.0);
	float width = 0.2 * COLOR.g;
	float a = fract(around);
	float strip = smoothstep(0.02, 0.05, a + edge) * smoothstep(0.05 + width, 0.02 + width, a - edge);
	float dead = max(jin, step(0.01, COLOR.g) * strip);
	// Silver deadwood: weathered grey with long dark grain lines and a warmer rim at the bark.
	float grain = texture(bark_albedo, vec2(buv.x * 3.0, UV.y * 0.12)).r;
	float lines = 0.5 + 0.5 * sin((buv.x * 40.0 + grain * 6.0) * 3.14159);
	vec3 silver = deadwood * (0.72 + 0.3 * grain) * (0.85 + 0.15 * lines);
	vec3 col = mix(bark, silver, dead);
	// A thin dark lip where the living bark meets the shari.
	float lip = step(0.01, COLOR.g) * (1.0 - jin) * (smoothstep(0.0, 0.02, a + edge) - smoothstep(0.02, 0.05, a + edge));
	col *= 1.0 - 0.5 * lip;
	// A wire scar: a dark groove spiralling round the branch, swollen lighter bark beside it.
	float s = fract(UV2.x + UV.y / scar_pitch);
	float groove = smoothstep(0.16, 0.04, abs(s - 0.5)) * COLOR.b;
	float ridge = clamp(smoothstep(0.3, 0.16, abs(s - 0.5)) * COLOR.b - groove, 0.0, 1.0);
	col = mix(col, col * 0.22, groove);
	col = mix(col, col * 1.35 + vec3(0.04, 0.02, 0.0), ridge * 0.8);
	ALBEDO = col;
	NORMAL_MAP = mix(nrm, vec3(0.5, 0.5, 1.0), dead * 0.5);
	ROUGHNESS = mix(0.88, 0.75, dead);
	SPECULAR = 0.25;
	RIM = rim_strength;
	RIM_TINT = 0.5;
}
