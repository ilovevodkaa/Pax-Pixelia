shader_type canvas_item;
// Bayer dither curtain between scenes (MAIN_MENU.md §1.4). `progress` only ever changes in steps of k/8
// (DitherWipe drives it with a timer), so the curtain closes in crisp pixel stages instead of a fade.

#include "res://shaders/front/front_common.gdshaderinc"

uniform float progress = 0.0;          // 0 = open, 1 = closed
uniform float cell = 4.0;              // = the backdrop's vp size so the pattern matches the sky
uniform int dir = 0;                   // 0 even, 1 left → right (forward), 2 right → left (back)
uniform vec4 ink : source_color = vec4(0.043, 0.043, 0.047, 1.0);

void fragment() {
	float b = bayer4(floor(FRAGCOORD.xy / cell));
	float g = dir == 1 ? UV.x : dir == 2 ? 1.0 - UV.x : 0.0;
	float th = dir == 0 ? b : g * 0.6 + b * 0.4;
	COLOR = vec4(ink.rgb, step(th + 0.001, progress));
}
