shader_type spatial;
// The bonsai's wood: the photo bark (tinted per species) with three marks from the vertex
// colours BonsaiView writes per node (BranchMeshBuilder.node_colors):
//   r = jin: the whole segment is silver deadwood, bleached with long grain;
//   g = shari: a strip of the trunk stripped to silver wood (UV2.x is the angle around it);
//   b = wire scar: a spiral groove where a wire bit into the bark.

uniform sampler2D bark_albedo : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D bark_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform vec3 texture_tint : source_color = vec3(0.52, 0.36, 0.28);
uniform vec3 deadwood : source_color = vec3(0.8, 0.78, 0.74);
// One wire turn in UV.y units (the wire's pitch times the bark tiling).
uniform float scar_pitch = 0.2;
uniform float rim_strength = 0.08;

void fragment() {
	vec3 bark = texture(bark_albedo, UV).rgb * texture_tint;
	vec3 nrm = texture(bark_normal, UV).rgb;
	float jin = COLOR.r;
	// Shari: a strip down one side, its edges a little ragged along the wood.
	float edge = 0.04 * sin(UV.y * 9.0) + 0.03 * sin(UV.y * 23.0);
	float strip = smoothstep(0.02, 0.06, UV2.x + edge) * smoothstep(0.34, 0.28, UV2.x - edge);
	float dead = max(jin, COLOR.g * strip);
	// Silver deadwood: bleached, long grain from the bark texture stretched along the wood.
	float grain = texture(bark_albedo, vec2(UV.x * 3.0, UV.y * 0.15)).r;
	vec3 silver = deadwood * (0.78 + 0.35 * grain);
	vec3 col = mix(bark, silver, dead);
	// A wire scar: a dark groove spiralling round the branch, swollen lighter bark beside it.
	float s = fract(UV2.x + UV.y / scar_pitch);
	float groove = smoothstep(0.16, 0.04, abs(s - 0.5)) * COLOR.b;
	float ridge = clamp(smoothstep(0.3, 0.16, abs(s - 0.5)) * COLOR.b - groove, 0.0, 1.0);
	col = mix(col, col * 0.22, groove);
	col = mix(col, col * 1.35 + vec3(0.04, 0.02, 0.0), ridge * 0.8);
	ALBEDO = col;
	NORMAL_MAP = mix(nrm, vec3(0.5, 0.5, 1.0), dead * 0.6);
	ROUGHNESS = mix(0.85, 0.7, dead);
	SPECULAR = 0.3;
	RIM = rim_strength;
	RIM_TINT = 0.5;
}
