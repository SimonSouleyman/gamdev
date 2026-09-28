shader_type canvas_item;
// Handwriting ink for Labels / RichTextLabels / Buttons: the pen presses unevenly (strokes fade
// and darken along their length), the ink skips a little on the paper's tooth, and it bleeds a
// soft feathered halo into the fibres. Works on any font atlas; the text keeps its own colour.

uniform sampler2D detail_tex : filter_linear_mipmap, repeat_enable;
uniform float pressure = 0.25;   // how much the stroke density varies
uniform float tooth = 0.15;      // dry skips from the paper's surface
uniform float bleed = 0.35;      // feathered halo
uniform float bleed_px = 1.3;
uniform float seed = 0.0;

varying vec2 local_pos;
// The vertex colour (font colour x modulate). In fragment() COLOR is already multiplied by the
// glyph texture, so using it again would square the coverage and thin the strokes.
varying vec4 ink_col;

void vertex() {
	local_pos = VERTEX;
	ink_col = COLOR;
}

void fragment() {
	vec4 t = texture(TEXTURE, UV);
	float a = t.a;
	// Blurred coverage for the halo (a small ring of taps, within the atlas padding).
	vec2 px = TEXTURE_PIXEL_SIZE * bleed_px;
	float b = 0.0;
	b += texture(TEXTURE, UV + vec2(px.x, 0.0)).a;
	b += texture(TEXTURE, UV - vec2(px.x, 0.0)).a;
	b += texture(TEXTURE, UV + vec2(0.0, px.y)).a;
	b += texture(TEXTURE, UV - vec2(0.0, px.y)).a;
	b *= 0.25;
	vec2 q = local_pos + vec2(seed * 97.0, seed * 61.0);
	// Pressure: slow variation along the line of writing.
	float pr = texture(detail_tex, q / vec2(900.0, 420.0)).b;
	float dens = 1.0 - pressure * smoothstep(0.35, 0.8, pr);
	// Tooth: fine grain where the nib skipped.
	float th = texture(detail_tex, q / 160.0).a;
	dens *= 1.0 - tooth * smoothstep(0.55, 0.85, th);
	// Ink pools at the edges of a stroke as it dries: darker rim, lighter core.
	float rim = clamp(1.0 - abs(a - 0.5) * 2.0, 0.0, 1.0);
	vec3 ink = ink_col.rgb * (1.0 - rim * 0.25);
	ink = mix(ink, ink * vec3(1.08, 1.02, 0.95), pr * 0.4);
	float halo = clamp(b - a, 0.0, 1.0) * bleed * 0.55;
	float out_a = max(a * dens, halo) * ink_col.a;
	COLOR = vec4(ink, out_a);
}
