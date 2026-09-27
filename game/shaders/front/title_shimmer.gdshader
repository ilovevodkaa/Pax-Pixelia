shader_type canvas_item;
// Pixel title (port of Mr. President's title_shimmer): a stepped top-to-bottom gradient from near white to
// gray and, now and then, a light diagonal band sweeping across the letters in steps of `snap` px (one glyph
// pixel). Only the light glyph fill is recoloured; the dark hard shadow is left alone.

uniform float t = 0.0;                 // FrontClock.T
uniform float text_top = 0.0;
uniform float text_height = 90.0;
uniform vec4 top_color : source_color = vec4(0.941, 0.941, 0.949, 1.0);
uniform vec4 bottom_color : source_color = vec4(0.561, 0.561, 0.580, 1.0);
uniform float bands = 5.0;
uniform float period = 6.0;            // 0 = no shimmer («меньше анимации»)
uniform float travel = 1200.0;
uniform float snap = 8.0;              // = font size / 11

varying vec2 local_pos;

void vertex() {
	local_pos = VERTEX;
}

void fragment() {
	float is_fill = step(0.85, min(COLOR.r, min(COLOR.g, COLOR.b)));
	vec2 snapped = floor(local_pos / snap) * snap;
	float k = clamp((snapped.y - text_top) / text_height, 0.0, 1.0);
	k = floor(k * bands) / (bands - 1.0);
	vec3 tone = mix(top_color.rgb, bottom_color.rgb, clamp(k, 0.0, 1.0));
	if (period > 0.0) {
		float phase = floor((mod(t, period) / period * travel * 1.6 - travel * 0.3) / snap) * snap;
		tone = mix(tone, vec3(1.0), step(abs(snapped.x + snapped.y - phase), snap * 1.5));
	}
	COLOR.rgb = mix(COLOR.rgb, tone, is_fill);
}
